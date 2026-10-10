using Dalamud.Game.Command;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Client.Game;
using GlamourTagger.Windows;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using GlamourTagger.IPC;

namespace GlamourTagger;

// Entry point: services, windows, slash commands, and the two preview paths (Glamourer IPC / native Fitting Room).
public sealed class Plugin : IDalamudPlugin
{
    [PluginService] public static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] public static ICommandManager CommandManager { get; private set; } = null!;
    [PluginService] public static IChatGui ChatGui { get; private set; } = null!;
    [PluginService] public static IPluginLog Log { get; private set; } = null!;
    [PluginService] public static ITextureProvider TextureProvider { get; private set; } = null!;
    [PluginService] public static IDataManager DataManager { get; private set; } = null!;
    [PluginService] public static IFramework Framework { get; private set; } = null!;
    [PluginService] public static IGameGui GameGui { get; private set; } = null!;
    [PluginService] public static IClientState ClientState { get; private set; } = null!;
    [PluginService] public static IPlayerState PlayerState { get; private set; } = null!;

    public Configuration Configuration { get; init; }
    public WindowSystem WindowSystem = new("GlamourTagger");
    public MainWindow MainWindow { get; init; }
    public ChangelogWindow ChangelogWindow { get; init; }
    public GuideWindow GuideWindow { get; init; }
    public BackupManager BackupManager { get; private set; } = null!;
    public SettingsWindow SettingsWindow { get; init; }
    // what this plugin last put on each slot, per preview mode (slot name -> item id)
    public Dictionary<string, uint> GlamourerPreviewItems { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, uint> FittingRoomItems { get; } = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> lastTryOnKeys = new(StringComparer.OrdinalIgnoreCase);
    public IReadOnlyCollection<string> LastFittingRoomTryOnKeys => lastTryOnKeys;
    private bool fittingRoomSessionActive = false;
    // equipment-bar slot name of the last Fitting Room try-on ("Body", "Finger", ...), null = none
    public string? LastFittingRoomTryOnSlot { get; private set; }
    // Fitting Room try-ons are sent one at a time from OnFrameworkUpdate (see FittingRoomTryOnQueue)
    private readonly FittingRoomTryOnQueue fittingRoomTryOns = new();
    // frames between two queued try-ons: 1 = every frame. Raise it if the game still skips some
    private const int FittingRoomTryOnFrameInterval = 1;
    private int framesUntilNextTryOn;
    public GlamourTagger.Windows.FittingRoomAssistWindow FittingRoomAssistWindow { get; init; } = null!;

    public PenumbraIpcHandler PenumbraIpc { get; private set; } = null!;
    public GlamourerIpcHandler GlamourerIpc { get; private set; } = null!;

    private string? pendingOldVersion;
    // chat error throttling (once per 5 minutes)
    private DateTime? lastIpcErrorTime = null;
    private DateTime? lastApplyErrorTime = null;

    private const string CommandName = "/gtag";
    private const string AliasCommand1 = "/gtagger";
    private const string AliasCommand2 = "/glamourtagger";

    public Plugin()
    {
        // 1. Régi config fájl átírása a betöltés előtt
        MigrateLegacyConfigFile();

        // 2. A már migrált config betöltése
        Configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        Configuration.Initialize(PluginInterface, this);

        BackupManager = new BackupManager(this);

        PenumbraIpc = new PenumbraIpcHandler(PluginInterface, Configuration);
        GlamourerIpc = new GlamourerIpcHandler(PluginInterface);

        MainWindow = new MainWindow(this);
        WindowSystem.AddWindow(MainWindow);

        ChangelogWindow = new ChangelogWindow(this);
        WindowSystem.AddWindow(ChangelogWindow);

        GuideWindow = new GuideWindow(this);
        WindowSystem.AddWindow(GuideWindow);

        SettingsWindow = new SettingsWindow(this);
        WindowSystem.AddWindow(SettingsWindow);

        FittingRoomAssistWindow = new FittingRoomAssistWindow(this);
        WindowSystem.AddWindow(FittingRoomAssistWindow);

        CheckForUpdates();

        // /gtag and its two aliases all open the main window
        CommandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "Opens the Glamour Tagger window."
        });
        CommandManager.AddHandler(AliasCommand1, new CommandInfo(OnCommand)
        {
            HelpMessage = "Opens the Glamour Tagger window."
        });
        CommandManager.AddHandler(AliasCommand2, new CommandInfo(OnCommand)
        {
            HelpMessage = "Opens the Glamour Tagger window."
        });

        PluginInterface.UiBuilder.Draw += DrawUI;
        PluginInterface.UiBuilder.OpenMainUi += ToggleMainUi;
        PluginInterface.UiBuilder.OpenConfigUi += ToggleMainUi;
        Framework.Update += OnFrameworkUpdate;
        ClientState.ClassJobChanged += OnClassJobChanged;
    }

    public void Dispose()
    {
        PenumbraIpc?.Dispose();
        GlamourerIpc?.Dispose();
        BackupManager.Dispose();

        WindowSystem.RemoveAllWindows();
        MainWindow.Dispose();
        ChangelogWindow.Dispose();
        GuideWindow.Dispose();
        SettingsWindow.Dispose();
        FittingRoomAssistWindow.Dispose();

        CommandManager.RemoveHandler(CommandName);
        CommandManager.RemoveHandler(AliasCommand1);
        CommandManager.RemoveHandler(AliasCommand2);

        PluginInterface.UiBuilder.Draw -= DrawUI;
        PluginInterface.UiBuilder.OpenMainUi -= ToggleMainUi;
        PluginInterface.UiBuilder.OpenConfigUi -= ToggleMainUi;
        Framework.Update -= OnFrameworkUpdate;
        ClientState.ClassJobChanged -= OnClassJobChanged;
    }

    private void OnCommand(string command, string args)
    {
        ToggleMainUi();
    }

    private void DrawUI()
    {
        WindowSystem.Draw();
    }

    public void ToggleMainUi()
    {
        // Ha épp megnyitjuk a főablakot, és van még meg nem jelenített frissítési értesítés
        if (!MainWindow.IsOpen && pendingOldVersion != null)
        {
            var currentVersionStr = PluginInterface.Manifest.AssemblyVersion?.ToString() ?? "1.0.1.1";

            // Megjelenítjük a Changelog ablakot
            ChangelogWindow.ShowUpdateNotification(pendingOldVersion);

            // Elmentjük az új verziót a konfigba, hogy legközelebb ne jelenjen meg újra
            Configuration.LastSeenVersion = currentVersionStr;
            Configuration.Save();

            // Töröljük a várakozó jelzést
            pendingOldVersion = null;
        }

        MainWindow.IsOpen = !MainWindow.IsOpen;
    }

    // Which item is on this slot right now? Needed when only the dye changes (brush) and no item id is passed in.
    public unsafe uint GetActiveOrEquippedItemId(byte slot)
    {
        string slotKey = slot switch
        {
            1 => "MainHand",
            2 => "OffHand",
            3 => "Head",
            4 => "Body",
            5 => "Hands",
            7 => "Legs",
            8 => "Feet",
            9 => "Ears",
            10 => "Neck",
            11 => "Wrists",
            12 => "RFinger",
            14 => "LFinger",
            _ => string.Empty
        };

        // 1. Try live Glamourer state first (reflects real equipment + any active preview)
        if (!string.IsNullOrEmpty(slotKey) && GlamourerIpc.IsAvailable())
        {
            uint liveId = GlamourerIpc.GetCurrentSlotItemId(slotKey);
            if (liveId != 0)
            {
                Log.Debug($"[GlamourTagger Debug] GetActiveOrEquippedItemId (Slot {slot} / {slotKey}): Found live Glamourer Item ID {liveId}");
                return liveId;
            }
        }

        // 2. Fall back to tracked preview items dictionary
        if (!string.IsNullOrEmpty(slotKey) && GlamourerPreviewItems.TryGetValue(slotKey, out var trackedId) && trackedId != 0)
        {
            Log.Debug($"[GlamourTagger Debug] GetActiveOrEquippedItemId (Slot {slot} / {slotKey}): Found tracked preview Item ID {trackedId}");
            return trackedId;
        }

        // 3. Fallback to actual in-game equipped item via FFXIVClientStructs
        try
        {
            var inventoryManager = InventoryManager.Instance();
            if (inventoryManager != null)
            {
                var container = inventoryManager->GetInventoryContainer(InventoryType.EquippedItems);
                if (container != null)
                {
                    int inventoryIndex = slot switch
                    {
                        1 => 0,  // MainHand
                        2 => 1,  // OffHand
                        3 => 2,  // Head
                        4 => 3,  // Body
                        5 => 4,  // Hands
                        7 => 6,  // Legs
                        8 => 7,  // Feet
                        9 => 8,  // Ears
                        10 => 9, // Neck
                        11 => 10,// Wrists
                        12 => 11,// RFinger
                        14 => 12,// LFinger
                        _ => -1
                    };

                    if (inventoryIndex >= 0)
                    {
                        var slotItem = container->GetInventorySlot(inventoryIndex);
                        if (slotItem != null)
                        {
                            // Check for GlamourId first if the item is glamoured in-game
                            uint id = slotItem->GlamourId != 0 ? slotItem->GlamourId : slotItem->ItemId;
                            if (id != 0)
                            {
                                Log.Debug($"[GlamourTagger Debug] GetActiveOrEquippedItemId (Slot {slot}): Found in-game equipped Item ID {id} (GlamourId: {slotItem->GlamourId}, ItemId: {slotItem->ItemId})");
                                return id;
                            }
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Log.Debug($"[GlamourTagger Debug] GetActiveOrEquippedItemId exception for slot {slot}: {ex.Message}");
        }

        Log.Debug($"[GlamourTagger Debug] GetActiveOrEquippedItemId (Slot {slot}): No item found, returning 0");
        return 0;
    }

    // Glamourer Preview path. Falls back to the /glamour command and then to the Fitting Room if the IPC fails.
    public void ApplyItemWithGlamourer(uint itemId, byte slot = 0, byte? overrideDye1 = null, byte? overrideDye2 = null, bool exactSlot = false)
    {
        Framework.RunOnFrameworkThread(() =>
        {
            // Resolve slot if zero based on Excel item category
            if (slot == 0 && itemId != 0)
            {
                var itemSheet = DataManager.GetExcelSheet<Lumina.Excel.Sheets.Item>();
                var item = itemSheet?.GetRow(itemId);
                if (item != null)
                {
                    slot = GlamourerIpc.GetGlamourerSlotId(item.Value.EquipSlotCategory.RowId);
                }
            }

            // If itemId is 0 (e.g. applying dye via Painter Roller), resolve active or equipped item ID
            if (itemId == 0 && slot != 0)
            {
                itemId = GetActiveOrEquippedItemId(slot);
            }

            // If itemId is still 0 (e.g., slot is empty), skip IPC call gracefully to avoid ItemInvalid errors
            if (itemId == 0)
            {
                Log.Debug($"[GlamourTagger] Cannot apply item/dye: Item ID is 0 for slot {slot}. Skipping.");
                return;
            }

            // Track the actively previewed item in Glamourer mode
            TrackGlamourerPreviewItem(itemId, slot);

            byte dye1 = overrideDye1 ?? 0;
            byte dye2 = overrideDye2 ?? 0;

            string slotKeyForDyeResolve = slot switch
            {
                1 => "MainHand",
                2 => "OffHand",
                3 => "Head",
                4 => "Body",
                5 => "Hands",
                7 => "Legs",
                8 => "Feet",
                9 => "Ears",
                10 => "Neck",
                11 => "Wrists",
                12 => "RFinger",
                14 => "LFinger",
                _ => string.Empty
            };

            // plain preview click (no dyes passed in) -> use the selected dye set
            if (overrideDye1 == null && overrideDye2 == null)
            {
                if (Configuration.SelectedDyeSet >= 0 && Configuration.SelectedDyeSet < Configuration.DyeSets.Length)
                {
                    var activeSet = Configuration.DyeSets[Configuration.SelectedDyeSet];
                    dye1 = activeSet.Dye1;
                    dye2 = activeSet.Dye2;
                }
            }

            // Resolve "Keep current dye" sentinel against live Glamourer state, if present.
            // Skipped entirely (no extra IPC) when neither channel uses the sentinel.
            if ((dye1 == DyeHelper.DyeKeepCurrent || dye2 == DyeHelper.DyeKeepCurrent) && !string.IsNullOrEmpty(slotKeyForDyeResolve))
            {
                if (GlamourerIpc.TryGetSlotStains(slotKeyForDyeResolve, out byte curStain, out byte curStain2))
                {
                    if (dye1 == DyeHelper.DyeKeepCurrent) dye1 = curStain;
                    if (dye2 == DyeHelper.DyeKeepCurrent) dye2 = curStain2;
                }
                else
                {
                    Log.Debug($"[GlamourTagger Debug] Keep-current dye requested for slot {slot} but Glamourer state was unavailable. Falling back to dye 0.");
                    ChatGui.Print("[Glamour Tagger Error] Glamourer IPC failed - could not read current stains, dye reset instead.");
                    if (dye1 == DyeHelper.DyeKeepCurrent) dye1 = 0;
                    if (dye2 == DyeHelper.DyeKeepCurrent) dye2 = 0;
                }
            }

            // Step 1: Glamourer IPC Application
            try
            {
                bool success = false;

                // Double-ring toggle logic for preview table clicks
                if (!exactSlot && (slot == GlamourerIpcHandler.SlotRightRing || slot == GlamourerIpcHandler.SlotLeftRing))
                {
                    if (Configuration.LeftRingActive && Configuration.RightRingActive)
                    {
                        bool rightSuccess = GlamourerIpc.ApplySlotItem(GlamourerIpcHandler.SlotRightRing, itemId, dye1, dye2);
                        bool leftSuccess = GlamourerIpc.ApplySlotItem(GlamourerIpcHandler.SlotLeftRing, itemId, dye1, dye2);
                        success = rightSuccess && leftSuccess;
                    }
                    else if (Configuration.LeftRingActive && !Configuration.RightRingActive)
                    {
                        success = GlamourerIpc.ApplySlotItem(GlamourerIpcHandler.SlotLeftRing, itemId, dye1, dye2);
                    }
                    else
                    {
                        success = GlamourerIpc.ApplySlotItem(GlamourerIpcHandler.SlotRightRing, itemId, dye1, dye2);
                    }
                }
                else if (slot == 1) // MainHand
                {
                    success = GlamourerIpc.ApplySlotItem(1, itemId, dye1, dye2);

                    // For dual-wield jobs (Ninja, Monk, Dancer, Red Mage, Viper, etc.), also apply to OffHand (Slot 2)
                    var itemSheet = DataManager.GetExcelSheet<Lumina.Excel.Sheets.Item>();
                    var item = itemSheet?.GetRowOrDefault(itemId);
                    if (item != null && IsDualWieldWeapon(item.Value))
                    {
                        GlamourerIpc.ApplySlotItem(2, itemId, dye1, dye2);
                        TrackGlamourerPreviewItem(itemId, 2);
                    }
                }
                else if (slot == 2) // OffHand / Shield
                {
                    // Glamourer.SetItem.V3 cannot resolve Shield-type OffHand items over IPC
                    // (confirmed upstream limitation — issue filed with Glamourer). Shields go
                    // straight through /glamour applyitem for the item itself, and GetState +
                    // ApplyState for the dye. No MainHand is read or changed.
                    success = CommandManager.ProcessCommand($"/glamour applyitem {itemId} | <me>");

                    if (success)
                    {
                        TrackGlamourerPreviewItem(itemId, 2);

                        // Always write the stains, including None + None (0,0) - otherwise an existing
                        // shield dye is never removed. Every other slot already behaves this way.
                        GlamourerIpc.TryApplyShieldStain(itemId, dye1, dye2);
                    }
                }

                else
                {
                    // Armor / Accessory slots
                    success = GlamourerIpc.ApplySlotItem(slot, itemId, dye1, dye2);
                }

                if (success)
                {
                    Log.Debug($"Glamourer IPC succeeded for slot {slot}, item {itemId}.");
                    return;
                }
            }
            catch (Exception ex)
            {
                Log.Debug($"Glamourer apply item IPC failed for item {itemId}: {ex.Message}");
            }

            // Step 2 & 3: Fallback handling
            if (overrideDye1 != null || overrideDye2 != null)
            {
                HandleApplyItemFallback();
                TryOnItem(itemId, dye1, dye2);
            }
            else
            {
                HandleIpcFallback(itemId, dye1, dye2);
            }
        });
    }

    /// <summary>
    /// Helper method to check if an item is a shield usable by Gladiator, Paladin, or Beastmaster.
    /// </summary>
    public static bool IsShieldJob(Lumina.Excel.Sheets.Item item)
    {
        var jobCat = item.ClassJobCategory.ValueNullable;
        if (!jobCat.HasValue) return false;

        var cat = jobCat.Value;
        // GLD = Gladiator, PLD = Paladin, BST = Beastmaster
        return cat.GLA || cat.PLD || cat.BSM;
    }

    /// <summary>
    /// Helper method to detect dual-wield job weapons. (Ninja, Monk, Dancer, Red Mage)
    /// </summary>
    public static bool IsDualWieldWeapon(Lumina.Excel.Sheets.Item item)
    {
        // Biztonságosan lekérjük a nullable struktúrát
        var jobCat = item.ClassJobCategory.ValueNullable;
        if (!jobCat.HasValue) return false;

        // Kibontjuk a konkrét ClassJobCategory struktúrát
        var cat = jobCat.Value;

        // NIN = Ninja, ROG = Rogue, MNK = Monk, PGL = Pugilist, DNC = Dancer, RDM = Red Mage
        return cat.NIN || cat.ROG || cat.MNK || cat.PGL || cat.DNC || cat.RDM || cat.VPR || cat.AST || cat.MCH || cat.PCT || cat.SAM;
    }

    // remember what was previewed on which slot (several alias keys, because the slot names differ between callers)
    public void TrackGlamourerPreviewItem(uint itemId, byte slot = 0)
    {
        if (itemId == 0) return;

        try
        {
            if (slot != 0)
            {
                switch (slot)
                {
                    case 1:
                        GlamourerPreviewItems["MainHand"] = itemId;
                        var itemSheetMH = DataManager.GetExcelSheet<Lumina.Excel.Sheets.Item>();
                        var itemMH = itemSheetMH?.GetRow(itemId);
                        if (itemMH != null && IsDualWieldWeapon(itemMH.Value))
                        {
                            GlamourerPreviewItems["OffHand"] = itemId;
                            GlamourerPreviewItems["Offhand"] = itemId;
                        }
                        break;
                    case 2:
                        GlamourerPreviewItems["OffHand"] = itemId;
                        GlamourerPreviewItems["Offhand"] = itemId;
                        GlamourerPreviewItems["Shield"] = itemId;
                        break;
                    case 3: GlamourerPreviewItems["Head"] = itemId; break;
                    case 4: GlamourerPreviewItems["Body"] = itemId; break;
                    case 5: GlamourerPreviewItems["Hands"] = itemId; break;
                    case 7: GlamourerPreviewItems["Legs"] = itemId; break;
                    case 8: GlamourerPreviewItems["Feet"] = itemId; break;
                    case 9: GlamourerPreviewItems["Ears"] = itemId; break;
                    case 10: GlamourerPreviewItems["Neck"] = itemId; break;
                    case 11: GlamourerPreviewItems["Wrists"] = itemId; break;
                    case 12: GlamourerPreviewItems["RFinger"] = itemId; GlamourerPreviewItems["Finger"] = itemId; break;
                    case 14: GlamourerPreviewItems["LFinger"] = itemId; GlamourerPreviewItems["Finger"] = itemId; break;
                }
                return;
            }

            var itemSheet = DataManager.GetExcelSheet<Lumina.Excel.Sheets.Item>();
            var item = itemSheet?.GetRow(itemId);
            if (item == null) return;

            var categoryId = item.Value.EquipSlotCategory.RowId;
            switch (categoryId)
            {
                case 1: case 13: GlamourerPreviewItems["MainHand"] = itemId; break;
                case 2:
                    GlamourerPreviewItems["OffHand"] = itemId;
                    GlamourerPreviewItems["Offhand"] = itemId;
                    GlamourerPreviewItems["Shield"] = itemId;
                    break;
                case 3: GlamourerPreviewItems["Head"] = itemId; break;
                case 4: GlamourerPreviewItems["Body"] = itemId; break;
                case 5: GlamourerPreviewItems["Hands"] = itemId; break;
                case 7: GlamourerPreviewItems["Legs"] = itemId; break;
                case 8: GlamourerPreviewItems["Feet"] = itemId; break;
                case 9: GlamourerPreviewItems["Ears"] = itemId; break;
                case 10: GlamourerPreviewItems["Neck"] = itemId; break;
                case 11: GlamourerPreviewItems["Wrists"] = itemId; break;
                case 12:
                    if (Configuration.RightRingActive) GlamourerPreviewItems["RFinger"] = itemId;
                    if (Configuration.LeftRingActive) GlamourerPreviewItems["LFinger"] = itemId;
                    GlamourerPreviewItems["Finger"] = itemId;
                    break;
            }
        }
        catch (Exception ex)
        {
            Log.Debug($"Failed to track glamourer preview item: {ex.Message}");
        }
    }

    /// <summary>
    /// Reseteli az IPC hibaüzenet 5 perces cooldownját (pl. az ablak bezárásakor).
    /// </summary>
    public void ResetIpcErrorCooldown()
    {
        lastIpcErrorTime = null;
        lastApplyErrorTime = null;
        PenumbraIpc?.ResetErrorCooldown();
    }

    // IPC failed: try the /glamour chat command, then the native Fitting Room
    private void HandleIpcFallback(uint itemId, byte dye1 = 0, byte dye2 = 0)
    {
        var now = DateTime.Now;

        if (lastIpcErrorTime == null || (now - lastIpcErrorTime.Value).TotalMinutes >= 5)
        {
            lastIpcErrorTime = now;
            ChatGui.PrintError("[Glamour Tagger] Glamourer IPC preview failed. Falling back to other methods.");
        }

        try
        {
            // Step 2 Fallback: Glamourer slash command
            if (CommandManager.ProcessCommand($"/glamour applyitem {itemId} | <me>"))
            {
                return;
            }
        }
        catch (Exception ex)
        {
            Log.Debug($"Glamourer slash command fallback failed: {ex.Message}");
        }

        // Step 3 Fallback: Native Fitting Room
        HandleApplyItemFallback();
        TryOnItem(itemId, dye1, dye2);
    }

    // tells the user (at most once per 5 minutes) that a Glamourer preview fell back to the Fitting Room
    public void HandleApplyItemFallback()
    {
        var now = DateTime.Now;

        // Throttled error message: Only print to chat once every 5 minutes to avoid chat spam
        if (lastApplyErrorTime == null || (now - lastApplyErrorTime.Value).TotalMinutes >= 5)
        {
            lastApplyErrorTime = now;
            ChatGui.PrintError("[Glamour Tagger] Glamourer preview failed or missing. Falling back to Fitting Room.");
        }
    }

    // Revert button of the top bar
    public void RevertGlamourerPreview()
    {
        Framework.RunOnFrameworkThread(() =>
        {
            GlamourerIpc.RevertEquipment();
            GlamourerPreviewItems.Clear();
        });
    }

    // ===== Equipment bar Middle-Click: take an item off a slot =====

    /// <summary>The Emperor's New piece for a Glamourer slot id. 0 = none exists (weapons).</summary>
    private static uint GetEmperorItemId(byte slot) => slot switch
    {
        1 => 13775,       // The Emperor's New Fists (Pugilist / Monk only)
        2 => 30067,       // The Emperor's New Shield (Gladiator / Paladin / Beast only)
        3 => 10032,       // The Emperor's New Hat
        4 => 10033,       // The Emperor's New Robe
        5 => 10034,       // The Emperor's New Gloves
        7 => 10035,       // The Emperor's New Breeches
        8 => 10036,       // The Emperor's New Boots
        9 => 9293,        // The Emperor's New Earrings
        10 => 9292,       // The Emperor's New Necklace
        11 => 9294,       // The Emperor's New Bracelet
        12 or 14 => 9295, // The Emperor's New Ring
        _ => 0
    };

    /// <summary>
    /// Takes the item off one slot. useEmperor == false: Glamourer's "Nothing" item;
    /// true: the matching Emperor's New piece. Fitting Room mode always uses the Emperor's
    /// piece (it can only try ON real items). Weapons are ignored.
    /// </summary>
    public void ClearSlotPreview(byte slot, bool useEmperor, bool fittingRoomMode)
    {
        uint emperorId = GetEmperorItemId(slot);
        if (emperorId == 0) return; // weapons / unknown slot

        // Weapons: only the jobs that can hold the invisible piece; there is no "Nothing" for them.
        if (slot is 1 or 2)
        {
            if (!PlayerState.IsLoaded) return;
            uint job = PlayerState.ClassJob.RowId;
            bool allowed = slot == 1 ? job is 2 or 20   // Pugilist, Monk
                                     : job is 1 or 19 or 43;  // Gladiator, Paladin, BST
            if (!allowed) return;
            useEmperor = true;
        }

        if (fittingRoomMode)
        {
            TryOnItem(emperorId);
            return;
        }

        if (useEmperor)
        {
            // Existing apply path, unchanged. exactSlot: true = exactly this ring, no RR/LR fan-out.
            ApplyItemWithGlamourer(emperorId, slot, (byte)0, (byte)0, exactSlot: true);
            return;
        }

        Framework.RunOnFrameworkThread(() =>
        {
            // Glamourer's own "Nothing" item id for a slot: uint.MaxValue - 128 - slot.
            // Item id 0 = "Nothing" in Glamourer's SetItem (confirmed in game).
            // The explicit id is only a backup; both ring slots share the right ring's id.
            ulong nothingId = uint.MaxValue - 128u - (slot == 14 ? 12u : (uint)slot);
            if (GlamourerIpc.ApplySlotItem(slot, 0) || GlamourerIpc.ApplySlotItem(slot, nothingId))
                return;

            // Same fallback as the normal apply path: Fitting Room, with the invisible piece.
            HandleApplyItemFallback();
            TryOnItem(emperorId);
        });
    }

    // Fitting Room path: native try-on through the game's own agent, works without Glamourer.
    // Queued, not sent at once: OnFrameworkUpdate sends one try-on per frame (see FittingRoomTryOnQueue).
    public void TryOnItem(uint itemId, byte? overrideDye1 = null, byte? overrideDye2 = null)
    {
        Framework.RunOnFrameworkThread(() => fittingRoomTryOns.Enqueue(itemId, overrideDye1, overrideDye2));
    }

    /// <summary>Sends the next queued try-on when its frame has come. Returns true while the queue is busy.</summary>
    private bool SendNextQueuedTryOn()
    {
        if (fittingRoomTryOns.Count == 0)
        {
            framesUntilNextTryOn = 0;
            return false;
        }

        if (--framesUntilNextTryOn > 0) return true;
        if (!fittingRoomTryOns.TryDequeue(out var request)) return false;

        framesUntilNextTryOn = FittingRoomTryOnFrameInterval;
        SendTryOn(request.ItemId, request.Dye1, request.Dye2);
        return true;
    }

    // one try-on, straight to the game (only called for queued requests, on the framework thread)
    private void SendTryOn(uint itemId, byte? overrideDye1, byte? overrideDye2)
    {
        try
        {
            if (!fittingRoomSessionActive)
            {
                fittingRoomSessionActive = true;
                FittingRoomAssistWindow.NotifySessionStarted();
            }

            TrackFittingRoomItem(itemId);

            byte dye1 = overrideDye1 ?? 0;
            byte dye2 = overrideDye2 ?? 0;

            // No dyes passed in (plain Try On click) -> use the selected dye set.
            // Dyes passed in explicitly (brush, Glamourer fallback) are used as-is, even None + None.
            if (overrideDye1 == null && overrideDye2 == null && Configuration.SelectedDyeSet >= 0 && Configuration.SelectedDyeSet < Configuration.DyeSets.Length)
            {
                var activeSet = Configuration.DyeSets[Configuration.SelectedDyeSet];
                dye1 = activeSet.Dye1;
                dye2 = activeSet.Dye2;
            }

            // 255 ("Keep current dye") is not a real stain - the Fitting Room renders it as glossy black.
            ResolveKeepCurrentForFittingRoom(itemId, ref dye1, ref dye2);

            AgentTryon.TryOn(0, itemId, dye1, dye2);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to try on item using native AgentTryOn.");
        }
    }

    /// <summary>
    /// Fitting Room only: replaces the "Keep current dye" sentinel (255) with a real stain ID.
    /// Glamourer available -> the slot's current stains from Glamourer state; otherwise -> 0 (no dye).
    /// </summary>
    private void ResolveKeepCurrentForFittingRoom(uint itemId, ref byte dye1, ref byte dye2)
    {
        // No sentinel -> no extra IPC call at all
        if (dye1 != DyeHelper.DyeKeepCurrent && dye2 != DyeHelper.DyeKeepCurrent) return;

        byte curStain = 0;
        byte curStain2 = 0;

        try
        {
            var itemSheet = DataManager.GetExcelSheet<Lumina.Excel.Sheets.Item>();
            var item = itemSheet?.GetRow(itemId);
            if (item != null)
            {
                string slotKey = GlamourerIpc.GetGlamourerSlotId(item.Value.EquipSlotCategory.RowId) switch
                {
                    1 => "MainHand",
                    2 => "OffHand",
                    3 => "Head",
                    4 => "Body",
                    5 => "Hands",
                    7 => "Legs",
                    8 => "Feet",
                    9 => "Ears",
                    10 => "Neck",
                    11 => "Wrists",
                    12 => "RFinger",
                    _ => string.Empty
                };

                // TryGetSlotStains checks Glamourer availability itself; false = missing/disabled/read failed
                if (!string.IsNullOrEmpty(slotKey) && GlamourerIpc.TryGetSlotStains(slotKey, out byte s1, out byte s2))
                {
                    curStain = s1;
                    curStain2 = s2;
                }
            }
        }
        catch (Exception ex)
        {
            Log.Debug($"[GlamourTagger] Fitting Room keep-current dye lookup failed, using no dye: {ex.Message}");
        }

        if (dye1 == DyeHelper.DyeKeepCurrent) dye1 = curStain;
        if (dye2 == DyeHelper.DyeKeepCurrent) dye2 = curStain2;
    }

    // same idea as TrackGlamourerPreviewItem, for the Fitting Room. Kept when the Fitting Room closes, like the
    // game's own try-on list; cleared by the Fitting Room Assist buttons or a job change (if enabled).
    public void TrackFittingRoomItem(uint itemId)
    {
        if (itemId == 0) return;

        try
        {
            var itemSheet = DataManager.GetExcelSheet<Lumina.Excel.Sheets.Item>();
            var item = itemSheet?.GetRow(itemId);
            if (item == null) return;

            lastTryOnKeys.Clear();

            void Track(string key)
            {
                FittingRoomItems[key] = itemId;
                lastTryOnKeys.Add(key);
            }

            var categoryId = item.Value.EquipSlotCategory.RowId;
            switch (categoryId)
            {
                case 1:
                case 13:
                    Track("MainHand");
                    if (item != null && IsDualWieldWeapon(item.Value))
                    {
                        Track("OffHand");
                        Track("Offhand");
                    }
                    break;
                case 2:
                    Track("OffHand");
                    Track("Offhand");
                    Track("Shield");
                    break;
                case 3: Track("Head"); break;
                case 4: Track("Body"); break;
                case 5: Track("Hands"); break;
                case 7: Track("Legs"); break;
                case 8: Track("Feet"); break;
                case 9: Track("Ears"); break;
                case 10: Track("Neck"); break;
                case 11: Track("Wrists"); break;
                case 12:
                    // Native Fitting Room can't tell us right vs. left once you try on more than
                    // one ring in the same session (the game alternates internally) - only the
                    // right finger is safe to assume here.
                    Track("Finger");
                    Track("RFinger");
                    break;
            }

            LastFittingRoomTryOnSlot = categoryId switch
            {
                1 or 13 => "MainHand",
                2 => "OffHand",
                3 => "Head",
                4 => "Body",
                5 => "Hands",
                7 => "Legs",
                8 => "Feet",
                9 => "Ears",
                10 => "Neck",
                11 => "Wrists",
                12 => "Finger",
                _ => null
            };
        }
        catch (Exception ex)
        {
            Log.Debug($"Failed to track fitting room item: {ex.Message}");
        }
    }

    public void ClearFittingRoomCache()
    {
        FittingRoomItems.Clear();
        lastTryOnKeys.Clear();
        LastFittingRoomTryOnSlot = null;
    }

    private void OnClassJobChanged(uint classJobId)
    {
        if (Configuration.ClearFittingRoomCacheOnJobChange && FittingRoomItems.Count > 0)
        {
            ClearFittingRoomCache();
        }
    }

    // every frame: sends the queued Fitting Room try-ons; while a session is active, notices when the game window closes
    private unsafe void OnFrameworkUpdate(IFramework framework)
    {
        // While try-ons are still being sent, the Fitting Room may not be open yet: no close check until the queue is empty
        if (SendNextQueuedTryOn()) return;

        if (!fittingRoomSessionActive) return;

        var agent = AgentTryon.Instance();
        var addon = GameGui.GetAddonByName("FittingShop");
        bool isFittingRoomOpen = (addon != nint.Zero) || (agent != null && agent->IsAgentActive());

        if (!isFittingRoomOpen)
        {
            // The try-on memory is kept on purpose: the game's Fitting Room also remembers what was
            // tried on when it is opened again.
            fittingRoomSessionActive = false;
            FittingRoomAssistWindow.NotifySessionEnded();
        }
    }

    public void LinkItemInChat(uint itemId)
    {
        try
        {
            var seString = SeString.CreateItemLink(itemId, false);
            ChatGui.Print(seString);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to link item in chat.");
        }
    }

    // Options -> Sync Favorites: reads Glamourer's files from disk and adds its favourites to F
    public void ImportFavoritesFromGlamourer()
    {
        BackupManager.CreatePreImportBackup();

        Task.Run(() =>
        {
            try
            {
                var configDir = PluginInterface.ConfigDirectory.Parent?.FullName;
                if (configDir == null)
                {
                    ChatGui.Print("[Glamour Tagger] Plugin directory structure not found.");
                    return;
                }

                string[] possiblePaths = new[]
                {
                    Path.Combine(configDir, "Glamourer.json"),
                    Path.Combine(configDir, "Glamourer", "config.json"),
                    Path.Combine(configDir, "Glamourer", "favorites.json"),
                    Path.Combine(configDir, "Glamourer", "Favorites.json")
                };

                var tempFavorites = new HashSet<uint>();
                bool fileFound = false;

                foreach (var path in possiblePaths)
                {
                    if (!File.Exists(path)) continue;
                    fileFound = true;

                    try
                    {
                        string jsonContent = File.ReadAllText(path);
                        using var doc = JsonDocument.Parse(jsonContent);
                        ExtractFavoritesFromElement(doc.RootElement, tempFavorites);
                    }
                    catch (Exception ex)
                    {
                        Log.Error(ex, $"Error reading file: {path}");
                    }
                }

                if (!fileFound)
                {
                    ChatGui.Print("[Glamour Tagger] Glamourer configuration file not found.");
                    return;
                }

                Framework.RunOnFrameworkThread(() =>
                {
                    int addedCount = 0;
                    foreach (var id in tempFavorites)
                    {
                        if (Configuration.Favorites.Add(id))
                            addedCount++;
                    }
                    Configuration.Save();
                    MainWindow.isDirty = true;
                    ChatGui.Print($"[Glamour Tagger] Successfully imported {addedCount} new favorite items from Glamourer!");
                });
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to import Glamourer favorites.");
                ChatGui.Print($"[Glamour Tagger] Import error: {ex.Message}");
            }
        });
    }

    // doesn't rely on one exact file layout: walks the whole json and picks up anything under a Favorites-like key
    private void ExtractFavoritesFromElement(JsonElement element, HashSet<uint> tempFavorites)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in element.EnumerateObject())
            {
                if (prop.NameEquals("Favorites") || prop.NameEquals("ItemFavorites") || prop.NameEquals("FavoriteItems"))
                {
                    ProcessFavoritesContainer(prop.Value, tempFavorites);
                }
                else
                {
                    ExtractFavoritesFromElement(prop.Value, tempFavorites);
                }
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var child in element.EnumerateArray())
            {
                ExtractFavoritesFromElement(child, tempFavorites);
            }
        }
    }

    private void ProcessFavoritesContainer(JsonElement container, HashSet<uint> tempFavorites)
    {
        if (container.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in container.EnumerateArray())
            {
                if (TryExtractItemId(item, out uint itemId))
                {
                    tempFavorites.Add(itemId);
                }
            }
        }
        else if (container.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in container.EnumerateObject())
            {
                if (uint.TryParse(prop.Name, out uint itemIdFromKey))
                {
                    tempFavorites.Add(itemIdFromKey);
                }
                else if (TryExtractItemId(prop.Value, out uint itemIdFromVal))
                {
                    tempFavorites.Add(itemIdFromVal);
                }
            }
        }
    }

    private bool TryExtractItemId(JsonElement element, out uint itemId)
    {
        itemId = 0;

        if (element.ValueKind == JsonValueKind.Number && element.TryGetUInt32(out itemId))
        {
            return itemId > 0;
        }
        if (element.ValueKind == JsonValueKind.String)
        {
            ReadOnlySpan<char> val = element.GetString().AsSpan();
            if (val.StartsWith("Item:", StringComparison.OrdinalIgnoreCase))
                val = val.Slice(5);

            return uint.TryParse(val, out itemId) && itemId > 0;
        }
        if (element.ValueKind == JsonValueKind.Object)
        {
            if (element.TryGetProperty("Id", out var idProp) || element.TryGetProperty("ItemId", out idProp))
            {
                return TryExtractItemId(idProp, out itemId);
            }
        }

        return false;
    }

    // compares the running version with the last one the user saw, to decide whether the changelog should pop up
    private void CheckForUpdates()
    {
        var currentVersionStr = PluginInterface.Manifest.AssemblyVersion?.ToString() ?? "1.0.1.1";
        var lastSeen = Configuration.LastSeenVersion;

        if (string.IsNullOrEmpty(lastSeen))
        {
            // Legelső telepítéskor nem nyitunk ablakot, csak elmentjük a verziót
            Configuration.LastSeenVersion = currentVersionStr;
            Configuration.Save();
            return;
        }

        if (lastSeen != currentVersionStr)
        {
            if (Version.TryParse(lastSeen, out var lastVer) &&
                Version.TryParse(currentVersionStr, out var currentVer) &&
                currentVer > lastVer)
            {
                // Új verzió esetén csak eltároljuk a régi verziószámot memóriában
                pendingOldVersion = lastSeen;
            }
            else
            {
                Configuration.LastSeenVersion = currentVersionStr;
                Configuration.Save();
            }
        }
    }

    // early builds saved the config under the template's namespace - patch the type names so those configs still load
    private void MigrateLegacyConfigFile()
    {
        try
        {
            var configFile = PluginInterface.ConfigFile;
            if (!configFile.Exists) return;

            string content = File.ReadAllText(configFile.FullName);
            bool isDirty = false;

            // A régi SamplePlugin névtér cseréje GlamourTagger-re a JSON $type mezőjében
            if (content.Contains("SamplePlugin.Configuration"))
            {
                content = content.Replace("SamplePlugin.Configuration", "GlamourTagger.Configuration");
                isDirty = true;
            }

            // Ha az assembly név is SamplePlugin-ként hivatkozott
            if (content.Contains(", SamplePlugin"))
            {
                content = content.Replace(", SamplePlugin", ", GlamourTagger");
                isDirty = true;
            }

            if (isDirty)
            {
                File.WriteAllText(configFile.FullName, content);
                Log.Info("[Glamour Tagger] Legacy configuration migrated successfully to GlamourTagger namespace.");
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[Glamour Tagger] Failed to migrate legacy configuration file.");
        }
    }
}
