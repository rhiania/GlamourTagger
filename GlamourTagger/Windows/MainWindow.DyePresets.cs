using Dalamud.Bindings.ImGui;
using System;
using System.Collections.Generic;
using System.Numerics;
using SlotStates = System.Collections.Generic.Dictionary<string, GlamourTagger.IPC.GlamourerIpcHandler.SlotState>;
using SlotState = GlamourTagger.IPC.GlamourerIpcHandler.SlotState;

namespace GlamourTagger.Windows;

// Dye set operations: the menu of a dye set (Right-click on its palette icon, MainWindow_DyeDraw.cs) -
// copy from another set or from an item (in-game gear, Glamourer, Fitting Room try-on), swap, reset - and every change
// of a dye set with its one undo step.
public partial class MainWindow
{
    private const string DyeSetMenuId = "DyeSetMenu";
    private const float DyeMenuSwatchScale = 0.85f;   // menu swatch size, relative to the text line height
    private const float DyeMenuNoDyeIconScale = 0.5f; // Keep Current / No Dye icon on the menu swatches (bar and picker: 0.8)
    private const float DyeMenuSwatchGap = 3f;        // space between and after the two menu swatches

    // Slots of the "Copy from item" menus, in Glamourer's slot names, top to bottom
    private static readonly (string Key, string Name)[] DyeSetMenuSlots =
    {
        ("MainHand", "Main Hand"), ("OffHand", "Off Hand"), ("Head", "Head"), ("Body", "Chest"),
        ("Hands", "Gloves"), ("Legs", "Pants"), ("Feet", "Feet"), ("Ears", "Earrings"),
        ("Neck", "Necklace"), ("Wrists", "Bracelet"), ("RFinger", "Right Ring"), ("LFinger", "Left Ring"),
    };

    // Where "Copy from item" reads the dyes from
    private enum DyeCopySource { Equipped, Glamourer, FittingRoom }

    // The three sources of "Copy from item", read once when a dye set menu opens
    private SlotStates dyeSetMenuEquipped = new();     // the in-game gear (works without Glamourer)
    private SlotStates? dyeSetMenuGlamourer;           // null = Glamourer was not running
    private SlotStates dyeSetMenuFittingRoom = new();  // this plugin's remembered try-ons, on the slots the Fitting Room can dye

    /// <summary>One undo step per dye set: its dyes before the last change.</summary>
    private readonly Dictionary<int, (byte Dye1, byte Dye2)> dyeSetUndo = new();

    /// <summary>Set by the first save in an open dye picker, so a whole popup is one undo step.</summary>
    private bool dyePickerUndoRecorded;

    /// <summary>Opens the menu of a dye set. Call it inside the palette icon's ID scope, like DrawDyeSetMenu.</summary>
    private void OpenDyeSetMenu()
    {
        dyeSetMenuEquipped = EquippedGear.Read();
        dyeSetMenuGlamourer = plugin.GlamourerIpc.IsAvailable() ? plugin.GlamourerIpc.GetAllSlotStates() : null;
        dyeSetMenuFittingRoom = plugin.GetFittingRoomTryOns();
        ImGui.OpenPopup(DyeSetMenuId);
    }

    private void DrawDyeSetMenu(int setIdx)
    {
        if (!ImGui.BeginPopup(DyeSetMenuId)) return;

        DrawPopupTitle($"-- Dye Set {setIdx + 1} --"); // Equipment Bar gold title
        ImGui.Separator();

        if (ImGui.BeginMenu("Copy from Dye Set"))
        {
            DrawDyeSetChoices(setIdx, swap: false);
            ImGui.EndMenu();
        }

        if (ImGui.BeginMenu("Swap with Dye Set"))
        {
            DrawDyeSetChoices(setIdx, swap: true);
            ImGui.EndMenu();
        }

        DrawCopyFromItemMenu(setIdx);

        ImGui.Separator();

        if (ImGui.BeginMenu("Reset"))
        {
            if (DyeMenuItem("No Dye + No Dye", 0, 0, enabled: true)) SetDyeSetDyes(setIdx, 0, 0);
            if (DyeMenuItem("Keep Current + Keep Current", DyeHelper.DyeKeepCurrent, DyeHelper.DyeKeepCurrent, enabled: true))
            {
                SetDyeSetDyes(setIdx, DyeHelper.DyeKeepCurrent, DyeHelper.DyeKeepCurrent);
            }
            ImGui.EndMenu();
        }

        ImGui.Separator();

        bool canUndo = dyeSetUndo.TryGetValue(setIdx, out var undo);
        if (ImGui.MenuItem("Undo last change", "", false, canUndo)) UndoDyeSetChange(setIdx);
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
        {
            ImGui.SetTooltip(canUndo ? $"Back to: {DyeMenuName(undo.Dye1)} / {DyeMenuName(undo.Dye2)}" : "Nothing to undo yet");
        }

        ImGui.EndPopup();
    }

    /// <summary>All dye sets as menu items; the set the menu belongs to is listed too, greyed out.</summary>
    private void DrawDyeSetChoices(int setIdx, bool swap)
    {
        var sets = plugin.Configuration.DyeSets;
        for (int other = 0; other < sets.Length; other++)
        {
            bool self = other == setIdx;
            byte dye1 = sets[other].Dye1;
            byte dye2 = sets[other].Dye2;
            string text = self ? $"Set {other + 1} (this set)" : $"Set {other + 1}: {DyeMenuName(dye1)} / {DyeMenuName(dye2)}";

            if (DyeMenuItem(text, dye1, dye2, enabled: !self))
            {
                if (swap) SwapDyeSets(setIdx, other);
                else SetDyeSetDyes(setIdx, dye1, dye2);
            }
            if (self && ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled)) ImGui.SetTooltip("This is the set you are editing");
        }
    }

    /// <summary>"Copy from item": a slot's two dyes, from the in-game gear, from Glamourer or from a Fitting Room try-on.</summary>
    private void DrawCopyFromItemMenu(int setIdx)
    {
        if (!ImGui.BeginMenu("Copy from item")) return;

        if (ImGui.BeginMenu("Equipped (in game)"))
        {
            DrawCopyFromItemChoices(setIdx, dyeSetMenuEquipped, DyeCopySource.Equipped);
            ImGui.EndMenu();
        }

        var glamourer = dyeSetMenuGlamourer;
        bool glamourerOpen = ImGui.BeginMenu("Glamourer", glamourer != null);
        if (glamourer == null && ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
        {
            ImGui.SetTooltip("Glamourer is not running");
        }
        if (glamourerOpen)
        {
            if (glamourer != null) DrawCopyFromItemChoices(setIdx, glamourer, DyeCopySource.Glamourer);
            ImGui.EndMenu();
        }

        bool anyTryOn = dyeSetMenuFittingRoom.Count > 0;
        bool fittingRoomOpen = ImGui.BeginMenu("Fitting Room", anyTryOn);
        if (!anyTryOn && ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
        {
            ImGui.SetTooltip("Nothing tried on through Glamour Tagger yet");
        }
        if (fittingRoomOpen)
        {
            DrawCopyFromItemChoices(setIdx, dyeSetMenuFittingRoom, DyeCopySource.FittingRoom);
            ImGui.EndMenu();
        }

        ImGui.EndMenu();
    }

    /// <summary>
    /// The slots of one source with their dyes (as read when the menu opened): both dyes exactly as the source holds
    /// them, also on items with one dye channel or none and on "Nothing" (same rule as Dye Matching Colors Only).
    /// </summary>
    private void DrawCopyFromItemChoices(int setIdx, SlotStates slotStates, DyeCopySource source)
    {
        var itemSheet = Plugin.DataManager.GetExcelSheet<Lumina.Excel.Sheets.Item>();

        slotStates.TryGetValue("MainHand", out var mainHandState);
        var mainHandItem = mainHandState.ItemId != 0 ? itemSheet?.GetRowOrDefault(mainHandState.ItemId) : null;
        bool dualWield = mainHandItem.HasValue && Plugin.IsDualWieldWeapon(mainHandItem.Value);

        foreach (var (key, name) in DyeSetMenuSlots)
        {
            slotStates.TryGetValue(key, out var state);

            // A dual-wield off-hand without an item of its own is dyed like the main hand (as in the slot loop)
            if (key == "OffHand" && dualWield && state.ItemId == 0) state = mainHandState;

            string? unavailable = DyeCopyUnavailableReason(key, state, dualWield, source);
            bool usable = unavailable == null;
            byte dye1 = state.Stain;
            byte dye2 = state.Stain2;
            string text = usable ? $"{name}: {DyeMenuName(dye1)} / {DyeMenuName(dye2)}" : name;

            if (DyeMenuItem(text, dye1, dye2, enabled: usable, showSwatches: usable)) SetDyeSetDyes(setIdx, dye1, dye2);
            if (unavailable != null && ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled)) ImGui.SetTooltip(unavailable);
        }
    }

    /// <summary>Why a slot of a "Copy from item" menu is greyed out; null = it can be copied.</summary>
    private static string? DyeCopyUnavailableReason(string slotKey, SlotState state, bool dualWield, DyeCopySource source)
    {
        // The Fitting Room shows accessories undyed: it has no dyes of theirs to copy
        if (source == DyeCopySource.FittingRoom && !Plugin.FittingRoomCanDye(slotKey)) return "The Fitting Room can't dye accessories";

        // No item at all ("Nothing" has an item id of its own in Glamourer)
        if (state.ItemId == 0) return source == DyeCopySource.FittingRoom ? "Not tried on through Glamour Tagger" : "No item";

        // The empty off-hand of a two-handed weapon: only a shield or a dual-wield part is an off-hand that gets dyed
        if (slotKey == "OffHand" && !dualWield && !IsDyeableOffHandItem(state.ItemId)) return "No off-hand: two-handed weapon";

        return null;
    }

    /// <summary>A menu item with the two dyes as small squares in front of its text.</summary>
    private static bool DyeMenuItem(string text, byte dye1, byte dye2, bool enabled, bool showSwatches = true)
    {
        // Leading spaces keep the room for the two squares, drawn over the item afterwards
        float square = ImGui.GetTextLineHeight() * DyeMenuSwatchScale;
        float reserved = 2f * square + 2f * DyeMenuSwatchGap;
        int spaces = (int)MathF.Ceiling(reserved / MathF.Max(1f, ImGui.CalcTextSize(" ").X));

        bool clicked = ImGui.MenuItem(new string(' ', spaces) + text, "", false, enabled);
        if (!showSwatches) return clicked;

        // The item's rect starts half an item spacing before its text
        Vector2 itemMin = ImGui.GetItemRectMin();
        var topLeft = new Vector2(itemMin.X + ImGui.GetStyle().ItemSpacing.X * 0.5f, itemMin.Y + (ImGui.GetItemRectSize().Y - square) * 0.5f);
        DrawDyeMenuSwatch(topLeft, square, dye1, enabled);
        DrawDyeMenuSwatch(topLeft + new Vector2(square + DyeMenuSwatchGap, 0f), square, dye2, enabled);
        return clicked;
    }

    private static void DrawDyeMenuSwatch(Vector2 topLeft, float size, byte dyeId, bool enabled)
    {
        var dye = dyeId is 0 or DyeHelper.DyeKeepCurrent ? null : DyeHelper.GetDye(Plugin.DataManager, dyeId);
        Vector4 color = dye?.ColorVec ?? NoDyeSwatchColor;
        if (!enabled) color.W *= 0.5f;

        var drawList = ImGui.GetWindowDrawList();
        Vector2 bottomRight = topLeft + new Vector2(size, size);
        drawList.AddRectFilled(topLeft, bottomRight, ImGui.GetColorU32(color));
        drawList.AddRect(topLeft, bottomRight, ImGui.GetColorU32(new Vector4(0f, 0f, 0f, 0.6f)));
        // Keep Current and No Dye share the grey: the icon tells them apart, as on the bar and in the picker
        if (dye == null) DrawNoDyeIcon(topLeft, new Vector2(size, size), dyeId == DyeHelper.DyeKeepCurrent, DyeMenuNoDyeIconScale, enabled ? 1f : 0.5f);
        else if (DyeHelper.IsMetallic(dyeId)) DrawMetallicOverlay(drawList, topLeft, new Vector2(size, size));
    }

    private static string DyeMenuName(byte dyeId) => dyeId switch
    {
        DyeHelper.DyeKeepCurrent => "Keep Current",
        0 => "No Dye",
        _ => DyeHelper.GetDye(Plugin.DataManager, dyeId)?.Name ?? $"Dye {dyeId}",
    };

    // ---------------------------------------------------------------------------------------------
    // Changing dye sets (with one undo step per set)
    // ---------------------------------------------------------------------------------------------

    /// <summary>Remembers a set's dyes before a change: the one undo step of that set.</summary>
    private void RecordDyeSetUndo(int setIdx)
    {
        var set = plugin.Configuration.DyeSets[setIdx];
        dyeSetUndo[setIdx] = (set.Dye1, set.Dye2);
    }

    /// <summary>Called before every save in a dye picker: only the first one of the popup is remembered.</summary>
    private void RecordDyePickerUndo(int setIdx)
    {
        if (dyePickerUndoRecorded) return;
        dyePickerUndoRecorded = true;
        RecordDyeSetUndo(setIdx);
    }

    private void SetDyeSetDyes(int setIdx, byte dye1, byte dye2)
    {
        RecordDyeSetUndo(setIdx);
        var set = plugin.Configuration.DyeSets[setIdx];
        set.Dye1 = dye1;
        set.Dye2 = dye2;
        plugin.Configuration.Save();
    }

    private void SwapDyeSets(int setIdx, int otherIdx)
    {
        RecordDyeSetUndo(setIdx);
        RecordDyeSetUndo(otherIdx);
        var set = plugin.Configuration.DyeSets[setIdx];
        var other = plugin.Configuration.DyeSets[otherIdx];
        (set.Dye1, set.Dye2, other.Dye1, other.Dye2) = (other.Dye1, other.Dye2, set.Dye1, set.Dye2);
        plugin.Configuration.Save();
    }

    /// <summary>
    /// One dye of a set. A Middle-click on a dye square is its own undo step; saves in the dye picker (fromPicker)
    /// share one undo step for the whole popup.
    /// </summary>
    private void SetDyeSetChannel(int setIdx, int dyeChannel, byte dyeId, bool fromPicker = false)
    {
        if (fromPicker) RecordDyePickerUndo(setIdx);
        else RecordDyeSetUndo(setIdx);

        var set = plugin.Configuration.DyeSets[setIdx];
        if (dyeChannel == 1) set.Dye1 = dyeId; else set.Dye2 = dyeId;
        plugin.Configuration.Save();
    }

    /// <summary>The swap icon of a dye set: Dye 1 and Dye 2 change places.</summary>
    private void SwapDyeSetChannels(int setIdx)
    {
        RecordDyeSetUndo(setIdx);
        var set = plugin.Configuration.DyeSets[setIdx];
        (set.Dye1, set.Dye2) = (set.Dye2, set.Dye1);
        plugin.Configuration.Save();
    }

    /// <summary>Back to the dyes before the set's last change. One step: afterwards there is nothing to undo.</summary>
    private void UndoDyeSetChange(int setIdx)
    {
        if (!dyeSetUndo.Remove(setIdx, out var undo)) return;

        var set = plugin.Configuration.DyeSets[setIdx];
        set.Dye1 = undo.Dye1;
        set.Dye2 = undo.Dye2;
        plugin.Configuration.Save();
    }
}