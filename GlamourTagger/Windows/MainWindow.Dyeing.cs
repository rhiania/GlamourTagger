using System;
using System.Collections.Generic;
using System.Linq;

namespace GlamourTagger.Windows;

// Dyeing: which slots get dyed (dye targets) and applying dyes to them.
// Used by the brush button (MainWindow_DyeDraw.cs) and the Instant Hover Over Dye Preview (MainWindow_DyeHover.cs).
public partial class MainWindow
{
    // ---------------------------------------------------------------------------------------------
    // Dye targets
    // ---------------------------------------------------------------------------------------------

    // --- Dye target selection (decoupled from the equipment FILTER) -------------------------
    // While dyeTargetOverrideActive == false the dye targets simply MIRROR selectedSlots (+ the
    // RR/LR flags). The first shift-click (or click while the dye-mode toggle is on) forks: the
    // filter is copied in here and only these fields decide what gets dyed. Dye-only clicks
    // never touch selectedSlots / isDirty -> no table refilter.
    private readonly HashSet<string> dyeTargetSlots = new();
    private bool dyeTargetRightRing = true;
    private bool dyeTargetLeftRing = false;
    private bool dyeTargetOverrideActive = false;
    private bool dyeSelectModeToggle = false; // session-only on purpose, not persisted

    // When true, the override dissolves again as soon as the dye targets become identical to the
    // filter (markers disappear and the two stay linked from then on). false = the dye targets
    // stay "pinned" once forked.
    private const bool AutoSyncDyeTargetsWhenIdentical = true;

    /// <summary>The slots a dye set is actually applied to.</summary>
    private IReadOnlyCollection<string> EffectiveDyeSlots => dyeTargetOverrideActive ? dyeTargetSlots : selectedSlots;
    private bool DyeTargetsRightRing => dyeTargetOverrideActive ? dyeTargetRightRing : plugin.Configuration.RightRingActive;
    private bool DyeTargetsLeftRing => dyeTargetOverrideActive ? dyeTargetLeftRing : plugin.Configuration.LeftRingActive;

    /// <summary>True if this slot will actually be dyed (works in mirror mode too, not just when forked).</summary>
    private bool IsDyeTarget(string slotName) =>
        dyeTargetOverrideActive ? dyeTargetSlots.Contains(slotName) : selectedSlots.Contains(slotName);

    /// <summary>True when the dye targets differ from the filter - this is what shows the markers.
    /// Ring flags only count when Finger is actually a dye target; otherwise they're irrelevant
    /// and must not cause phantom markers.</summary>
    private bool DyeTargetsDifferFromFilter =>
        dyeTargetOverrideActive &&
        (!dyeTargetSlots.SetEquals(selectedSlots)
         || (dyeTargetSlots.Contains("Finger")
             && (dyeTargetRightRing != plugin.Configuration.RightRingActive
                 || dyeTargetLeftRing != plugin.Configuration.LeftRingActive)));

    /// <summary>Copies the current filter into the dye targets and engages the override.</summary>
    private void BeginDyeTargetOverride()
    {
        if (dyeTargetOverrideActive) return;
        dyeTargetSlots.Clear();
        foreach (var s in selectedSlots) dyeTargetSlots.Add(s);
        dyeTargetRightRing = plugin.Configuration.RightRingActive;
        dyeTargetLeftRing = plugin.Configuration.LeftRingActive;
        dyeTargetOverrideActive = true;
    }

    /// <summary>Drops the override so the dye targets follow the filter again.</summary>
    private void ResetDyeTargetsToFilter()
    {
        dyeTargetOverrideActive = false;
        dyeTargetSlots.Clear();
    }

    /// <summary>Call after every dye-target change: dissolves the override if it became a no-op.</summary>
    private void NormalizeDyeTargets()
    {
        if (AutoSyncDyeTargetsWhenIdentical && dyeTargetOverrideActive && !DyeTargetsDifferFromFilter)
            ResetDyeTargetsToFilter();
    }

    /// <summary>The dye target slots as slot snapshot keys ("Finger" becomes the targeted ring slots).</summary>
    private IEnumerable<string> EnumerateDyeTargetSlotKeys()
    {
        foreach (string slotName in EffectiveDyeSlots)
        {
            if (slotName != "Finger")
            {
                yield return slotName;
                continue;
            }

            if (DyeTargetsRightRing) yield return "RFinger";
            if (DyeTargetsLeftRing) yield return "LFinger";
        }
    }

    // ---------------------------------------------------------------------------------------------
    // Applying
    // ---------------------------------------------------------------------------------------------

    // brush button: apply one dye set to every dye-target slot, on whatever item is currently shown there
    private unsafe void ApplyDyeSetToSelectedSlots(int setIdx)
    {
        if (setIdx < 0 || setIdx >= plugin.Configuration.DyeSets.Length) return;

        var set = plugin.Configuration.DyeSets[setIdx];

        // Snapshot current active item IDs + stains across all slots in a single operation
        var activeStates = FetchActiveSlotStates();

        // Set once per press if any slot needed a "keep current" fallback due to a failed GetState read
        bool keepCurrentFallbackWarned = false;

        ApplyDyesToDyeTargets(set.Dye1, set.Dye2, activeStates, ref keepCurrentFallbackWarned);
    }

    /// <summary>
    /// Applies a pair of dyes to every dye-target slot, on the items of the given slot snapshot.
    /// Shared by the brush button and the Instant Hover Over Dye Preview (MainWindow_DyeHover.cs).
    /// </summary>
    private void ApplyDyesToDyeTargets(byte dye1, byte dye2, Dictionary<string, GlamourTagger.IPC.GlamourerIpcHandler.SlotState> activeStates, ref bool keepCurrentFallbackWarned)
    {
        // Iterates through all multi-selected slots in the Equipment Bar
        foreach (var slotName in GetDyeTargetsInApplyOrder())
        {
            switch (slotName)
            {
                case "MainHand":
                    uint mainItemId = activeStates.TryGetValue("MainHand", out var mhState) ? mhState.ItemId : 0;
                    if (mainItemId != 0)
                    {
                        var (mhDye1, mhDye2) = ResolveDyes("MainHand", activeStates, dye1, dye2, ref keepCurrentFallbackWarned);
                        ApplySlotDyeWithItemId(mainItemId, 1, mhDye1, mhDye2);

                        // Dual-wield weapon check (Ninja, Monk, Dancer, Red Mage, Viper, etc.)
                        var itemSheet = Plugin.DataManager.GetExcelSheet<Lumina.Excel.Sheets.Item>();
                        var item = itemSheet?.GetRowOrDefault(mainItemId);
                        if (item != null && Plugin.IsDualWieldWeapon(item.Value))
                        {
                            uint offHandId = activeStates.TryGetValue("OffHand", out var offState) && offState.ItemId != 0 ? offState.ItemId : mainItemId;
                            var (ohDye1, ohDye2) = ResolveDyes("OffHand", activeStates, dye1, dye2, ref keepCurrentFallbackWarned);
                            ApplySlotDyeWithItemId(offHandId, 2, ohDye1, ohDye2);
                        }
                    }
                    break;

                case "OffHand":
                    uint shieldItemId = activeStates.TryGetValue("OffHand", out var shieldState) ? shieldState.ItemId : 0;

                    if (shieldItemId != 0)
                    {
                        var itemSheet = Plugin.DataManager.GetExcelSheet<Lumina.Excel.Sheets.Item>();
                        // GetRowOrDefault, not GetRow: Glamourer reports some off-hands (two-handed weapons,
                        // model-only off-hand pieces) with an id that is not in the Item sheet, and GetRow throws on that
                        var shieldItem = itemSheet?.GetRowOrDefault(shieldItemId);
                        if (shieldItem.HasValue)
                        {
                            var itemVal = shieldItem.Value;
                            bool isShieldOrDualWield = itemVal.EquipSlotCategory.RowId == 2 || Plugin.IsDualWieldWeapon(itemVal);

                            if (isShieldOrDualWield)
                            {
                                var (shDye1, shDye2) = ResolveDyes("OffHand", activeStates, dye1, dye2, ref keepCurrentFallbackWarned);
                                ApplySlotDyeWithItemId(shieldItemId, 2, shDye1, shDye2);
                            }
                        }
                        else
                        {
                            // expected for off-hands that are not real items - nothing to dye there
                            Plugin.Log.Debug($"[DyeHelper] OffHand ItemId {shieldItemId} is not in the Item sheet, skipped.");
                        }
                    }
                    else if (activeStates.TryGetValue("MainHand", out var mhState2) && mhState2.ItemId != 0)
                    {
                        // Dual-wield weapons (Ninja, Monk, etc.) occupy the OffHand slot visually
                        // even though the OffHand itself reports item ID 0.
                        var itemSheet = Plugin.DataManager.GetExcelSheet<Lumina.Excel.Sheets.Item>();
                        var item = itemSheet?.GetRowOrDefault(mhState2.ItemId);

                        if (item.HasValue && Plugin.IsDualWieldWeapon(item.Value))
                        {
                            var (dwDye1, dwDye2) = ResolveDyes("OffHand", activeStates, dye1, dye2, ref keepCurrentFallbackWarned);
                            ApplySlotDyeWithItemId(mhState2.ItemId, 2, dwDye1, dwDye2);
                        }
                        else if (!item.HasValue)
                        {
                            Plugin.Log.Error($"[DyeHelper] Failed to load Lumina sheet row for MainHand ItemId: {mhState2.ItemId}");
                        }
                    }
                    break;

                case "Head":
                    ApplySlotDyeResolved("Head", 3, activeStates, dye1, dye2, ref keepCurrentFallbackWarned);
                    break;
                case "Body":
                    ApplySlotDyeResolved("Body", 4, activeStates, dye1, dye2, ref keepCurrentFallbackWarned);
                    break;
                case "Hands":
                    ApplySlotDyeResolved("Hands", 5, activeStates, dye1, dye2, ref keepCurrentFallbackWarned);
                    break;
                case "Legs":
                    ApplySlotDyeResolved("Legs", 7, activeStates, dye1, dye2, ref keepCurrentFallbackWarned);
                    break;
                case "Feet":
                    ApplySlotDyeResolved("Feet", 8, activeStates, dye1, dye2, ref keepCurrentFallbackWarned);
                    break;
                case "Ears":
                    ApplySlotDyeResolved("Ears", 9, activeStates, dye1, dye2, ref keepCurrentFallbackWarned);
                    break;
                case "Neck":
                    ApplySlotDyeResolved("Neck", 10, activeStates, dye1, dye2, ref keepCurrentFallbackWarned);
                    break;
                case "Wrists":
                    ApplySlotDyeResolved("Wrists", 11, activeStates, dye1, dye2, ref keepCurrentFallbackWarned);
                    break;
                case "Finger":
                    if (DyeTargetsRightRing)
                    {
                        ApplySlotDyeResolved("RFinger", 12, activeStates, dye1, dye2, ref keepCurrentFallbackWarned);
                    }
                    if (DyeTargetsLeftRing)
                    {
                        ApplySlotDyeResolved("LFinger", 14, activeStates, dye1, dye2, ref keepCurrentFallbackWarned);
                    }
                    break;
            }
        }
    }

    /// <summary>
    /// The dye-target slots in the order they get dyed. Fitting Room mode: the slot of the last try-on goes last.
    /// With the game's "try on one item at a time" setting every try-on replaces the previous one, so whatever
    /// is dyed last is what stays visible - and that should be the item the user tried on last.
    /// While the dyes go to the Fitting Room, accessories are left out: it can't dye them.
    /// </summary>
    private IEnumerable<string> GetDyeTargetsInApplyOrder()
    {
        string? lastSlot = applyMode == 1 ? plugin.LastFittingRoomTryOnSlot : null;

        // The Fitting Room can't dye accessories: no try-on is sent for them at all
        bool skipAccessories = DyesGoToFittingRoom;
        if (skipAccessories && lastSlot != null && !Plugin.FittingRoomCanDye(lastSlot)) lastSlot = null;

        foreach (var slotName in EffectiveDyeSlots)
        {
            if (skipAccessories && !Plugin.FittingRoomCanDye(slotName)) continue;
            if (!string.Equals(slotName, lastSlot, StringComparison.OrdinalIgnoreCase)) yield return slotName;
        }

        if (lastSlot != null && EffectiveDyeSlots.Contains(lastSlot, StringComparer.OrdinalIgnoreCase))
        {
            yield return lastSlot;
        }
    }

    /// <summary>
    /// True when the dyes end up in the native Fitting Room: Fitting Room mode, or Glamourer Preview mode without
    /// Glamourer (the hover preview goes there directly, the brush through the apply fallback).
    /// Glamourer is only asked outside a preview apply.
    /// </summary>
    private bool DyesGoToFittingRoom =>
        applyMode == 1
        || DyePreviewWithoutGlamourer
        || (applyMode == 0 && dyePreviewApplying == null && !plugin.GlamourerIpc.IsAvailable());

    /// <summary>True when an accessory (earrings, necklace, bracelet, ring) is among the dye targets.</summary>
    private bool AccessoryIsDyeTarget()
    {
        foreach (string slotName in EffectiveDyeSlots)
        {
            if (!Plugin.FittingRoomCanDye(slotName)) return true;
        }
        return false;
    }

    // Tooltip / help line wherever the Fitting Room's missing accessory dyes matter
    private const string FittingRoomSkipsAccessoriesNote = "Accessories are left out: the Fitting Room can't dye them.";

    /// <summary>Looks up a slot's item ID and applies dyes, resolving any "keep current" sentinel first.</summary>
    private void ApplySlotDyeResolved(string jsonSlot, byte glamourerSlot, Dictionary<string, GlamourTagger.IPC.GlamourerIpcHandler.SlotState> activeStates, byte dye1, byte dye2, ref bool keepCurrentFallbackWarned)
    {
        uint itemId = activeStates.TryGetValue(jsonSlot, out var state) ? state.ItemId : 0;
        var (resolvedDye1, resolvedDye2) = ResolveDyes(jsonSlot, activeStates, dye1, dye2, ref keepCurrentFallbackWarned);
        ApplySlotDyeWithItemId(itemId, glamourerSlot, resolvedDye1, resolvedDye2);
    }

    /// <summary>
    /// Substitutes DyeHelper.DyeKeepCurrent with the slot's actual current stain from the snapshot.
    /// Falls back to 0 (reset) if the slot's state wasn't available, logging + printing once per press.
    /// </summary>
    private (byte, byte) ResolveDyes(string jsonSlot, Dictionary<string, GlamourTagger.IPC.GlamourerIpcHandler.SlotState> activeStates, byte dye1, byte dye2, ref bool keepCurrentFallbackWarned)
    {
        // Dye Matching Colors Only: the match groups decide each channel (MainWindow_DyeMatching.cs)
        if (dyePreviewApplying is { MatchingOn: true, MatchGroups: { } matchGroups })
        {
            if (matchGroups.TryResolve(jsonSlot, dye1, dye2, out var matchedDyes)) return matchedDyes;
            dye1 = dye2 = DyeHelper.DyeKeepCurrent; // not in the groups: stays as it is
        }

        // Fitting Room mode, or a preview falling back to the Fitting Room without Glamourer: "keep current" is the dye
        // the Fitting Room shows on the slot, which the slot snapshot holds (Plugin.GetFittingRoomSlotStates).
        // A dual-wield off-hand has no item of its own in the game: it shows the main hand's dyes. An empty slot has
        // nothing to keep: no dye.
        if (applyMode == 1 || DyePreviewWithoutGlamourer)
        {
            if (!activeStates.TryGetValue(jsonSlot, out var shown) && jsonSlot == "OffHand") activeStates.TryGetValue("MainHand", out shown);
            return (dye1 == DyeHelper.DyeKeepCurrent ? shown.Stain : dye1,
                    dye2 == DyeHelper.DyeKeepCurrent ? shown.Stain2 : dye2);
        }

        if (dye1 != DyeHelper.DyeKeepCurrent && dye2 != DyeHelper.DyeKeepCurrent)
        {
            return (dye1, dye2);
        }

        if (activeStates.TryGetValue(jsonSlot, out var state))
        {
            byte resolved1 = dye1 == DyeHelper.DyeKeepCurrent ? state.Stain : dye1;
            byte resolved2 = dye2 == DyeHelper.DyeKeepCurrent ? state.Stain2 : dye2;
            return (resolved1, resolved2);
        }

        // GetState didn't have this slot (Glamourer unavailable / read failed) - fall back to reset.
        Plugin.Log.Debug($"[GlamourTagger Debug] Keep-current dye requested for {jsonSlot} but Glamourer state was unavailable. Falling back to dye 0.");

        if (!keepCurrentFallbackWarned)
        {
            Plugin.ChatGui.Print("[Glamour Tagger Error] Glamourer IPC failed - could not read current stains, dye reset instead.");
            keepCurrentFallbackWarned = true;
        }

        byte fallback1 = dye1 == DyeHelper.DyeKeepCurrent ? (byte)0 : dye1;
        byte fallback2 = dye2 == DyeHelper.DyeKeepCurrent ? (byte)0 : dye2;
        return (fallback1, fallback2);
    }

    // re-applies the same item with new dyes - that is how a slot gets dyed in both modes
    private void ApplySlotDyeWithItemId(uint targetItemId, byte glamourerSlot, byte dye1, byte dye2)
    {
        // Instant Hover Over Dye Preview: remembers what each slot got; with Dye Matching Colors an unchanged slot gets no call
        if (targetItemId != 0 && dyePreviewApplying != null && !ShouldSendPreviewDyes(dyePreviewApplying, glamourerSlot, dye1, dye2)) return;

        Plugin.Log.Debug($"[GlamourTagger Debug] ApplySlotDyeWithItemId -> TargetItemID: {targetItemId} | GlamourerSlot: {glamourerSlot} | Dyes: [{dye1}, {dye2}] | ApplyMode: {applyMode}");
        if (targetItemId != 0)
        {
            if (applyMode == 1)
            {
                plugin.TryOnItem(targetItemId, dye1, dye2);
            }
            else if (DyePreviewWithoutGlamourer)
            {
                // Instant Hover Over Dye Preview in Glamourer Preview mode without Glamourer: go to the Fitting
                // Room directly. The normal path ends up there too, but only after a failed IPC attempt (and an
                // error in the log) for every slot on every hover.
                plugin.HandleApplyItemFallback();
                plugin.TryOnItem(targetItemId, dye1, dye2);
            }
            else if (glamourerSlot == 2 && DyePreviewUsesLightOffHandPath)
            {
                // Instant Hover Over Dye Preview: the OffHand item is already on the character, only its dyes change
                ApplyDyePreviewOffHand(targetItemId, dye1, dye2);
            }
            else
            {
                plugin.ApplyItemWithGlamourer(targetItemId, glamourerSlot, dye1, dye2, exactSlot: true, dyeOnly: true);
            }
        }
    }

    /// <summary>
    /// Snapshots current active item IDs + stains across all slots.
    /// Fitting Room mode, and Glamourer Preview mode without Glamourer (the dyes fall back to the Fitting Room there):
    /// what the Fitting Room shows - this plugin's try-ons, otherwise the in-game gear with its in-game dyes
    /// (Plugin.GetFittingRoomSlotStates).
    /// Glamourer Preview mode: live Glamourer state is the primary source (reflects both real equipment and any
    /// active preview, including try-ons done outside this plugin); the local preview-tracking dictionary only fills
    /// gaps Glamourer didn't report; the in-game gear is the final fallback.
    /// </summary>
    public Dictionary<string, GlamourTagger.IPC.GlamourerIpcHandler.SlotState> FetchActiveSlotStates()
    {
        if (applyMode == 1 || !plugin.GlamourerIpc.IsAvailable()) return plugin.GetFittingRoomSlotStates();

        // 1. Live Glamourer state is the source of truth
        var activeStates = plugin.GlamourerIpc.GetAllSlotStates();

        // 1b. Fill any slot Glamourer didn't report using the local preview-tracking dictionary
        foreach (var (slot, itemId) in plugin.GlamourerPreviewItems)
        {
            if (itemId != 0 && (!activeStates.TryGetValue(slot, out var existing) || existing.ItemId == 0))
            {
                activeStates[slot] = new GlamourTagger.IPC.GlamourerIpcHandler.SlotState(itemId, 0, 0);
            }
        }

        // 2. Fallback to the in-game equipped items. Item only: in this mode the dyes come from Glamourer
        foreach (var (slotKey, gear) in EquippedGear.Read())
        {
            if (!activeStates.TryGetValue(slotKey, out var existing) || existing.ItemId == 0)
            {
                activeStates[slotKey] = new GlamourTagger.IPC.GlamourerIpcHandler.SlotState(gear.ItemId, 0, 0);
            }
        }

        return activeStates;
    }

    // ---------------------------------------------------------------------------------------------
    // Glamourer slot ids
    // ---------------------------------------------------------------------------------------------

    /// <summary>Slot snapshot key -> Glamourer slot id, the same ids the slot loop above applies to.</summary>
    private static byte GlamourerSlotOfKey(string slotKey) => slotKey switch
    {
        "MainHand" => 1,
        "OffHand" => 2,
        "Head" => 3,
        "Body" => 4,
        "Hands" => 5,
        "Legs" => 7,
        "Feet" => 8,
        "Ears" => 9,
        "Neck" => 10,
        "Wrists" => 11,
        "RFinger" => 12,
        "LFinger" => 14,
        _ => 0
    };

    /// <summary>
    /// True for an off-hand the slot loop dyes on its own: a shield, or a part of a dual-wield weapon.
    /// A two-handed weapon (e.g. a WHM staff) reports an empty off-hand of its own, which is never dyed.
    /// </summary>
    private static bool IsDyeableOffHandItem(uint itemId)
    {
        if (itemId == 0) return false;
        var item = Plugin.DataManager.GetExcelSheet<Lumina.Excel.Sheets.Item>()?.GetRowOrDefault(itemId);
        return item.HasValue && (item.Value.EquipSlotCategory.RowId == 2 || Plugin.IsDualWieldWeapon(item.Value));
    }
}
