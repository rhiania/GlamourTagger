using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using Dalamud.Plugin.Ipc.Exceptions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace GlamourTagger.IPC;

// Keeps a cache of "item name -> mods that change it", built from Penumbra's IPC on a background task.
// The UI only ever reads the cache, it never waits for Penumbra.
public class PenumbraIpcHandler : IDisposable
{
    private readonly IDalamudPluginInterface pluginInterface;
    private readonly Configuration config;
    private readonly object lockObj = new();

    // Cache: Item Name -> List of affecting mod display names
    private Dictionary<string, List<string>> moddedItemsCache = new(StringComparer.OrdinalIgnoreCase);

    // Raised after each cache refresh with (previous cache, new cache).
    public event Action<Dictionary<string, List<string>>, Dictionary<string, List<string>>>? OnCacheRefreshed;

    public bool IsAvailable { get; private set; }
    private bool isRefreshing = false;
    private DateTime lastRefreshTime = DateTime.MinValue;
    private DateTime? lastPenumbraErrorTime = null;

    // Subscriptions for Penumbra lifecycle events
    private ICallGateSubscriber<object>? initializedSubscriber;
    private ICallGateSubscriber<object>? disposedSubscriber;
    private ICallGateSubscriber<bool, object>? enabledChangeSubscriber;
    private ICallGateSubscriber<int, int, object>? redrawObjectSubscriber;

    public PenumbraIpcHandler(IDalamudPluginInterface pi, Configuration config)
    {
        pluginInterface = pi;
        this.config = config;

        // Restore saved cache from configuration if available
        if (config.SavedPenumbraCache != null && config.SavedPenumbraCache.Count > 0)
        {
            moddedItemsCache = new Dictionary<string, List<string>>(config.SavedPenumbraCache, StringComparer.OrdinalIgnoreCase);
        }

        SubscribeLifecycleEvents();
        RefreshCacheAsync(500);
    }

    // re-scan when Penumbra (re)starts or gets enabled, go quiet when it goes away
    private void SubscribeLifecycleEvents()
    {
        try
        {
            initializedSubscriber = pluginInterface.GetIpcSubscriber<object>("Penumbra.Initialized");
            initializedSubscriber.Subscribe(OnPenumbraInitialized);

            disposedSubscriber = pluginInterface.GetIpcSubscriber<object>("Penumbra.Disposed");
            disposedSubscriber.Subscribe(OnPenumbraDisposed);

            enabledChangeSubscriber = pluginInterface.GetIpcSubscriber<bool, object>("Penumbra.EnabledChange");
            enabledChangeSubscriber.Subscribe(OnPenumbraEnabledChange);

            try
            {
                redrawObjectSubscriber = pluginInterface.GetIpcSubscriber<int, int, object>("Penumbra.RedrawObject.V5");
            }
            catch
            {
                redrawObjectSubscriber = pluginInterface.GetIpcSubscriber<int, int, object>("Penumbra.RedrawObject");
            }

            Plugin.Log.Info("[GlamourTagger] Subscribed to Penumbra lifecycle IPC events.");
        }
        catch (Exception ex)
        {
            Plugin.Log.Warning($"[GlamourTagger] Failed to subscribe to Penumbra events: {ex.Message}");
        }
    }

    private void OnPenumbraInitialized()
    {
        Plugin.Log.Info("[GlamourTagger] Penumbra initialized event received. Triggering delayed cache refresh.");
        RefreshCacheAsync(1000);
    }

    private void OnPenumbraDisposed()
    {
        Plugin.Log.Info("[GlamourTagger] Penumbra disposed event received. Clearing cache.");
        lock (lockObj)
        {
            IsAvailable = false;
        }
    }

    private void OnPenumbraEnabledChange(bool enabled)
    {
        Plugin.Log.Info($"[GlamourTagger] Penumbra enabled state changed: {enabled}");
        if (enabled)
        {
            RefreshCacheAsync(1000);
        }
        else
        {
            lock (lockObj)
            {
                IsAvailable = false;
            }
        }
    }

    // ApiVersion comes back either as a plain int or as a (breaking, feature) tuple - try both
    public bool CheckApiVersion()
    {
        try
        {
            int breaking;
            int feature = 0;

            try
            {
                var intSubscriber = pluginInterface.GetIpcSubscriber<int>("Penumbra.ApiVersion");
                breaking = intSubscriber.InvokeFunc();
            }
            catch
            {
                var tupleSubscriber = pluginInterface.GetIpcSubscriber<(int Breaking, int Feature)>("Penumbra.ApiVersion");
                (breaking, feature) = tupleSubscriber.InvokeFunc();
            }

            Plugin.Log.Info($"[GlamourTagger] Connected to Penumbra IPC API Version {breaking}.{feature}");
            return breaking >= 5;
        }
        catch (Exception ex)
        {
            Plugin.Log.Debug($"[GlamourTagger] Penumbra IPC is not available: {ex.Message}");
            return false;
        }
    }

    // only one refresh at a time; the optional delay gives Penumbra time to finish loading
    public void RefreshCacheAsync(int delayMs = 0)
    {
        lock (lockObj)
        {
            if (isRefreshing) return;
            isRefreshing = true;
        }

        Task.Run(async () =>
        {
            try
            {
                if (delayMs > 0)
                {
                    await Task.Delay(delayMs);
                }

                Plugin.Log.Info("[GlamourTagger] Starting Penumbra cache refresh...");

                if (!CheckApiVersion())
                {
                    Plugin.Log.Warning("[GlamourTagger] Penumbra API is unavailable or running an unsupported version.");
                    lock (lockObj)
                    {
                        IsAvailable = false;
                        moddedItemsCache = new(StringComparer.OrdinalIgnoreCase);
                    }
                    return;
                }

                var newCache = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

                // 1. Primary Strategy: Query changed items directly for each installed mod
                bool resolvedUsingMods = TryResolveByModChangedItems(newCache);

                if (!resolvedUsingMods)
                {
                    // 2. Secondary Strategy: Fallback to collection items + CheckCurrentChangedItemFunc delegate
                    TryResolveByCollectionAndCheckFunc(newCache);
                }

                // tidy up: unique mod names per item, sorted
                foreach (var key in newCache.Keys.ToList())
                {
                    newCache[key] = newCache[key].Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x).ToList();
                }

                Dictionary<string, List<string>> oldCacheCopy;
                lock (lockObj)
                {
                    // Compared with the last successful scan (the saved one), not with the live cache: the live cache is
                    // emptied while Penumbra is unavailable, and everything would count as newly modded once it is back.
                    oldCacheCopy = new Dictionary<string, List<string>>(config.SavedPenumbraCache ?? moddedItemsCache, StringComparer.OrdinalIgnoreCase);
                    moddedItemsCache = newCache;
                    IsAvailable = true;
                    lastRefreshTime = DateTime.Now;

                    // Persist new cache to configuration file
                    config.SavedPenumbraCache = new Dictionary<string, List<string>>(newCache, StringComparer.OrdinalIgnoreCase);
                    config.Save();
                }

                // Notify UI about the cache update
                OnCacheRefreshed?.Invoke(oldCacheCopy, newCache);

                Plugin.Log.Info($"[GlamourTagger] Penumbra cache refresh complete. Total modded items cached: {newCache.Count}");
            }
            catch (Exception ex)
            {
                Plugin.Log.Error(ex, "[GlamourTagger] Exception occurred during Penumbra IPC cache refresh.");
                lock (lockObj)
                {
                    IsAvailable = false;
                    moddedItemsCache = new(StringComparer.OrdinalIgnoreCase);
                }
            }
            finally
            {
                lock (lockObj)
                {
                    isRefreshing = false;
                    // also after a failed attempt: without Penumbra, RefreshIfNeeded would otherwise retry on every list rebuild
                    lastRefreshTime = DateTime.Now;
                }
            }
        });
    }

    /// <summary>
    /// Queries Penumbra for installed mods and fetches changed items per mod.
    /// Maps Item Name directly to the Mod's Display Name.
    /// </summary>
    private bool TryResolveByModChangedItems(Dictionary<string, List<string>> cache)
    {
        try
        {
            var penumbraMods = GetPenumbraModsSafe();
            if (penumbraMods.Count == 0) return false;

            Plugin.Log.Info($"[GlamourTagger] Querying changed items directly for {penumbraMods.Count} mod(s)...");

            int mappedItemsCount = 0;
            foreach (var (modDirectory, modName) in penumbraMods)
            {
                string displayName = !string.IsNullOrWhiteSpace(modName) ? modName : modDirectory;
                if (string.IsNullOrWhiteSpace(displayName)) continue;

                var changedItems = GetChangedItemsForModSafe(modDirectory);
                if (changedItems == null || changedItems.Count == 0) continue;

                foreach (var itemName in changedItems.Keys)
                {
                    if (string.IsNullOrWhiteSpace(itemName)) continue;

                    if (!cache.TryGetValue(itemName, out var modNames))
                    {
                        modNames = new List<string>();
                        cache[itemName] = modNames;
                    }

                    if (!modNames.Contains(displayName, StringComparer.OrdinalIgnoreCase))
                    {
                        modNames.Add(displayName);
                        mappedItemsCount++;
                    }
                }
            }

            Plugin.Log.Info($"[GlamourTagger] Mapped {mappedItemsCount} mod-item associations via GetChangedItems per mod.");
            return cache.Count > 0;
        }
        catch (Exception ex)
        {
            Plugin.Log.Warning($"[GlamourTagger] Failed to resolve changed items per mod: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Resolution Fallback: Fetch changed items for all collections and query Penumbra's
    /// CheckCurrentChangedItem delegate to resolve exact affecting ModIdentifiers for each item.
    /// </summary>
    private void TryResolveByCollectionAndCheckFunc(Dictionary<string, List<string>> cache)
    {
        try
        {
            var collectionsSubscriber = pluginInterface.GetIpcSubscriber<Dictionary<Guid, string>>("Penumbra.GetCollections.V5");
            var collections = collectionsSubscriber.InvokeFunc();

            if (collections == null || collections.Count == 0) return;

            Func<string, (string Identifier, string Name)[]>? checkFunc = GetCheckCurrentChangedItemFuncSafe();
            var penumbraMods = GetPenumbraModsSafe();

            foreach (var (collectionId, _) in collections)
            {
                var changedItems = GetChangedItemsForCollectionSafe(collectionId);
                if (changedItems == null) continue;

                foreach (var itemName in changedItems.Keys)
                {
                    if (string.IsNullOrEmpty(itemName)) continue;

                    if (!cache.TryGetValue(itemName, out var modNames))
                    {
                        modNames = new List<string>();
                        cache[itemName] = modNames;
                    }

                    if (checkFunc != null)
                    {
                        try
                        {
                            var modIdentifiers = checkFunc(itemName);
                            if (modIdentifiers != null)
                            {
                                foreach (var (identifier, name) in modIdentifiers)
                                {
                                    string modDisplayName = name;
                                    if (string.IsNullOrWhiteSpace(modDisplayName) && penumbraMods.TryGetValue(identifier, out var mappedName))
                                    {
                                        modDisplayName = mappedName;
                                    }
                                    if (string.IsNullOrWhiteSpace(modDisplayName))
                                    {
                                        modDisplayName = identifier;
                                    }

                                    if (!string.IsNullOrWhiteSpace(modDisplayName) &&
                                        !modNames.Contains(modDisplayName, StringComparer.OrdinalIgnoreCase))
                                    {
                                        modNames.Add(modDisplayName);
                                    }
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            Plugin.Log.Debug($"[GlamourTagger] Error executing CheckCurrentChangedItemFunc for {itemName}: {ex.Message}");
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.Warning($"[GlamourTagger] TryResolveByCollectionAndCheckFunc encountered an issue: {ex.Message}");
        }
    }

    // the *Safe helpers below try more than one IPC label and return null / empty instead of throwing
    private Func<string, (string Identifier, string Name)[]>? GetCheckCurrentChangedItemFuncSafe()
    {
        try
        {
            var subscriber = pluginInterface.GetIpcSubscriber<Func<string, (string Identifier, string Name)[]>>("Penumbra.CheckCurrentChangedItem");
            return subscriber.InvokeFunc();
        }
        catch
        {
            try
            {
                var fallbackSubscriber = pluginInterface.GetIpcSubscriber<Func<string, (string Identifier, string Name)[]>>("Penumbra.CheckCurrentChangedItemFunc");
                return fallbackSubscriber.InvokeFunc();
            }
            catch
            {
                return null;
            }
        }
    }

    private Dictionary<string, object?>? GetChangedItemsForModSafe(string modDirectory)
    {
        try
        {
            var subscriber = pluginInterface.GetIpcSubscriber<string, Dictionary<string, object?>>("Penumbra.GetChangedItems");
            return subscriber.InvokeFunc(modDirectory);
        }
        catch
        {
            try
            {
                var subscriberV5 = pluginInterface.GetIpcSubscriber<string, Dictionary<string, object?>>("Penumbra.GetChangedItems.V5");
                return subscriberV5.InvokeFunc(modDirectory);
            }
            catch
            {
                return null;
            }
        }
    }

    private Dictionary<string, string> GetPenumbraModsSafe()
    {
        try
        {
            var subscriber = pluginInterface.GetIpcSubscriber<Dictionary<string, string>>("Penumbra.GetMods");
            return subscriber.InvokeFunc();
        }
        catch
        {
            try
            {
                var listSubscriber = pluginInterface.GetIpcSubscriber<List<(string Directory, string Name)>>("Penumbra.GetMods");
                var list = listSubscriber.InvokeFunc();
                return list.ToDictionary(x => x.Directory, x => x.Name, StringComparer.OrdinalIgnoreCase);
            }
            catch
            {
                try
                {
                    var fallbackSubscriber = pluginInterface.GetIpcSubscriber<Dictionary<string, string>>("Penumbra.GetModList");
                    return fallbackSubscriber.InvokeFunc();
                }
                catch
                {
                    return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                }
            }
        }
    }

    private Dictionary<string, object?>? GetChangedItemsForCollectionSafe(Guid collectionId)
    {
        try
        {
            var subscriber = pluginInterface.GetIpcSubscriber<Guid, Dictionary<string, object?>>("Penumbra.GetChangedItemsForCollection.V5");
            return subscriber.InvokeFunc(collectionId);
        }
        catch (IpcNotReadyError)
        {
            try
            {
                var fallbackSubscriber = pluginInterface.GetIpcSubscriber<Guid, Dictionary<string, object?>>("Penumbra.GetChangedItemsForCollection");
                return fallbackSubscriber.InvokeFunc(collectionId);
            }
            catch (Exception ex)
            {
                Plugin.Log.Warning($"[GlamourTagger] Penumbra GetChangedItemsForCollection IPC call not ready: {ex.Message}");
                return null;
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.Warning($"[GlamourTagger] Failed to get changed items for collection {collectionId}: {ex.Message}");
            return null;
        }
    }

    // shortest time between two automatic refreshes started by RefreshIfNeeded
    private const int AutoRefreshIntervalSeconds = 15;

    // called while the table list is rebuilt: at most one background refresh per AutoRefreshIntervalSeconds
    public void RefreshIfNeeded()
    {
        if ((DateTime.Now - lastRefreshTime).TotalSeconds > AutoRefreshIntervalSeconds)
        {
            RefreshCacheAsync();
        }
    }

    // lookup is by item name - that is how Penumbra reports changed items
    public bool IsItemModded(string itemName, out List<string> affectingMods)
    {
        if (string.IsNullOrEmpty(itemName))
        {
            affectingMods = new List<string>();
            return false;
        }

        lock (lockObj)
        {
            if (moddedItemsCache.TryGetValue(itemName, out var mods))
            {
                affectingMods = new List<string>(mods);
                return mods.Count > 0;
            }
        }

        affectingMods = new List<string>();
        return false;
    }

    // "Recatch Penumbra & Redraw Self" button
    public void RedrawSelfAndRefresh()
    {
        try
        {
            // 1. Attempt Penumbra IPC call
            if (redrawObjectSubscriber != null)
            {
                redrawObjectSubscriber.InvokeAction(0, 0);
                Plugin.Log.Debug("[GlamourTagger] Penumbra RedrawObject(0, 0) called for self via IPC.");
                RefreshCacheAsync(100);
                return;
            }

            // 2. Fallback attempt via slash command if IPC subscriber isn't bound
            if (Plugin.CommandManager.ProcessCommand("/penumbra redraw"))
            {
                RefreshCacheAsync(100);
                return;
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.Debug($"Penumbra redraw call failed: {ex.Message}");
        }

        // 3. Executed if IPC/command failed or threw an exception
        HandlePenumbraFallback();
    }

    /// <summary>
    /// Külön hibaüzenet kezelő a Penumbra hívásokhoz, 5 perces némasággal.
    /// </summary>
    public void HandlePenumbraFallback()
    {
        var now = DateTime.Now;

        if (lastPenumbraErrorTime == null || (now - lastPenumbraErrorTime.Value).TotalMinutes >= 5)
        {
            lastPenumbraErrorTime = now;
            Plugin.ChatGui.PrintError("[Glamour Tagger] Penumbra IPC is not available or Penumbra is disabled.");
        }
    }

    public void ResetErrorCooldown()
    {
        lastPenumbraErrorTime = null;
    }

    public void Dispose()
    {
        try
        {
            initializedSubscriber?.Unsubscribe(OnPenumbraInitialized);
            disposedSubscriber?.Unsubscribe(OnPenumbraDisposed);
            enabledChangeSubscriber?.Unsubscribe(OnPenumbraEnabledChange);
        }
        catch { }
    }
}
