using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Textures;
using GlamourTagger.IPC;
using System;
using System.Collections.Generic;
using System.Numerics;

namespace GlamourTagger.Windows;

public partial class MainWindow
{
    private readonly HashSet<string> selectedSlots = new();
    private byte selectedItemSlot = 0;

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

    // Marker (droplet) appearance.
    // The glyph is defined only here.
    private static readonly string DyeMarkerIcon = FontAwesomeIcon.Tint.ToIconString();
    private const float DyeMarkerSize = 11f;      // glyph size on the 38x38 equipment icons
    private const float DyeMarkerSizeRing = 7f;   // smaller glyph on the RR/LR ring icons
    // Horizontal inset of the glyph from the rect's right edge, as a fraction of glyph size.
    // Wide glyphs (FillDrip, ~1.12em) need >1 to stay inside the icon, narrow ones (Tint) can
    // go down to about 0.70.
    private const float DyeMarkerInsetFactor = 1f;
    private static readonly Vector4 DyeMarkerColor = new(0.65f, 0.15f, 0.15f, 1f); // dark red

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

    /// <summary>
    /// Draws the small "this slot gets dyed" tint droplet into the top-right corner of a rect.
    /// Raw draw-list text (2 calls, one being a 1px shadow for readability) - not an ImGui item,
    /// so it can't affect layout and costs nothing measurable.
    /// </summary>
    private static void DrawDyeTargetMarker(Vector2 rectMin, Vector2 rectMax, float glyphSize)
    {
        var dl = ImGui.GetWindowDrawList();
        Vector2 pos = new(rectMax.X - glyphSize * DyeMarkerInsetFactor, rectMin.Y + 1f);
        dl.AddText(UiBuilder.IconFont, glyphSize, pos + new Vector2(1f, 1f), ImGui.GetColorU32(new Vector4(0f, 0f, 0f, 0.85f)), DyeMarkerIcon);
        dl.AddText(UiBuilder.IconFont, glyphSize, pos, ImGui.GetColorU32(DyeMarkerColor), DyeMarkerIcon);
    }

    private static readonly string[] AvailableSlots = {
        "Head", "Body", "Hands", "Legs", "Feet",
        "Ears", "Neck", "Wrists", "Finger", "MainHand", "OffHand"
    };

    [Flags]
    public enum IconFlag : uint
    {
        None = 0,
        Mainhand = 1 << 0,
        Head = 1 << 1,
        Body = 1 << 2,
        Hands = 1 << 3,
        Legs = 1 << 4,
        Feet = 1 << 5,
        Offhand = 1 << 6,
        Ears = 1 << 7,
        Neck = 1 << 8,
        Wrists = 1 << 9,
        Finger = 1 << 10,
        All = 0x7FF
    }

    // Struktúra az ikonok adatainak és az armouryboard.tex-en belüli koordinátáinak tárolásához
    private record EquipSlotIconInfo(
        string SlotName,
        string DisplayName,
        IconFlag Flag,
        float CropX,      // Kivágás kezdő X koordinátája pixelekben
        float CropY,      // Kivágás kezdő Y koordinátája pixelekben
        float CropWidth,  // Ikon szélessége pixelekben
        float CropHeight  // Ikon magassága pixelekben
    );

    // Az Armoury Board slotok és a hozzájuk tartozó UV kivágások (az armouryboard_hr1.tex kép alapján)
    private static readonly EquipSlotIconInfo[] EquipmentSlotIcons = new EquipSlotIconInfo[]
    {
        new("MainHand", "Main Hand", IconFlag.Mainhand, 0f,   0f, 80f, 80f),
        new("OffHand",  "Off Hand",  IconFlag.Offhand,  240f, 80f, 80f, 80f),
        new("Head",     "Head",      IconFlag.Head,     80f,  0f, 80f, 80f),
        new("Body",     "Chest",     IconFlag.Body,     160f, 0f, 80f, 80f),
        new("Hands",    "Gloves",    IconFlag.Hands,    240f, 0f, 80f, 80f),
        new("Legs",     "Pants",     IconFlag.Legs,     80f, 80f, 80f, 80f),
        new("Feet",     "Feet",      IconFlag.Feet,     160f, 80f, 80f, 80f),
        new("Ears",     "Earrings",  IconFlag.Ears,     0f, 160f, 80f, 80f),
        new("Neck",     "Necklace",  IconFlag.Neck,     80f, 160f, 80f, 80f),
        new("Wrists",   "Bracelet",  IconFlag.Wrists,   160f, 160f, 80f, 80f),
        new("Finger",   "Ring",      IconFlag.Finger,   240f, 160f, 80f, 80f)
    };

    // Megosztott textúra hivatkozás (nem töltődik újra minden képkockában)
    private ISharedImmediateTexture? armouryTexture = null;

    // --- Responsive layout tuning ---
    // Collapse order ablak-szűkítéskor (legszélesebb küszöb elsőnek): "Dye sets:" felirat -> a
    // paletta ikon minden dye setnél -> a 3. dye set teljes egészében.
    private const float DyeLabelMinWidth = 415f;
    // Minimum width (px) each dye set needs before it gets dropped when the window shrinks.
    private const float DyeSetMinWidthEach = 40f;
    // How far the dye squares are allowed to poke past the bar's top/bottom edge - purely
    // cosmetic, doesn't affect the bar's official height (see barContentHeight further down).
    private const float DyeSquareOverflow = 1f;
    // Balances the gap BELOW the bar against the gap ABOVE it. The two aren't automatically
    // symmetric, since the space above comes from MainWindow.cs's own Separator()/Spacing()
    // calls. Bigger value = bigger bottom gap; negative values work too.
    private const float BottomGapAdjustment = 3f;
    // Where the dye picker popups open: left edge = the set's Dye 1 square, top edge = the line under the
    // bar where the item table starts. Fine-tune here: + = right / down, - = left / up.
    private const float DyePickerPopupOffsetX = 0f;
    private const float DyePickerPopupOffsetY = 0f;
    // Smallest a dynamic gap is allowed to shrink to (never fully collapses to 0).
    private const float EquipBarSpacerMinWidth = 4f;
    // Largest a dynamic gap is allowed to grow to (avoids huge empty stretches on wide windows).
    private const float EquipBarSpacerMaxWidth = 60f;
    /// <summary>
    /// Draws a spacer of the given exact width. continueOnSameLine must be false only for the
    /// very FIRST spacer in a fresh group (nothing to attach to yet); true for every other one,
    /// so it attaches to whatever was drawn immediately before it instead of starting a new line.
    /// Calling SameLine() as the very first statement in a new group is dangerous - since there is
    /// no previous item within OUR group yet, ImGui looks further back to whatever the LAST item
    /// drawn anywhere was, even something from a totally different part of the window (e.g. a
    /// Separator() call from MainWindow.cs) - which silently glues our content onto that unrelated
    /// element's line.
    /// </summary>
    private void DrawEquipBarSpacer(float gap, bool continueOnSameLine)
    {
        if (continueOnSameLine)
        {
            ImGui.SameLine(0, 0);
        }
        ImGui.Dummy(new Vector2(gap, 1f));
        ImGui.SameLine(0, 0);
    }

    // ===== ALL / dye-mode stack (left of the equipment icons) =====
    // One frameless column with two rows, same pattern as RR/LR and Retweet/Brush:
    //   top row    = dye-mode toggle (FillDrip glyph)
    //   bottom row = "ALL" (fake small caps)
    // The column's WIDTH is automatic (the wider row, nudges included), so changing sizes can
    // never make it overlap the first equipment icon. Vertical positions are measured from the
    // top of the bar (the equipment icons are 38px tall, so 0..38 spans the bar).

    // Distance between the column and the first equipment icon. 0 = touching.
    // NEGATIVE values pull the equipment icons closer (into the column's right edge).
    private const float AllToEquipIconsGap = 0f;
    // Height of each row's clickable area, centred on the row's centre.
    private const float AllStackRowHitHeight = 22f;
    // Height reserved for the column in the layout (must equal the equipment icon height).
    private const float AllStackCellHeight = 38f;

    // --- Top row: dye-mode toggle ---
    private static readonly string ToggleGlyph = FontAwesomeIcon.FillDrip.ToIconString();
    // Vertical position of the row's CENTRE (9.5 = middle of the bar's upper half).
    private const float ToggleRowCenterY = 7.5f;
    // Horizontal position of the row's LEFT edge, from the column's left edge.
    private const float ToggleRowX = 0f;
    // FillDrip glyph size in px.
    private const float ToggleGlyphSize = 15f;

    // --- Top row, alternative look: "DYE" label in fake small caps instead of the glyph ---
    // Not used at the moment. Uses ToggleRowCenterY / ToggleRowX for its position.
    // Size of the "DYE" text (1.0 = normal UI font size).
    private const float DyeTextScale = 1.05f;
    // Moves ONLY the drawn "DYE" text, not its clickable area. Positive X = right, positive Y = down.
    private static readonly Vector2 DyeTextNudge = new(0f, 0f);

    // --- Bottom row: ALL ---
    // Vertical position of the row's CENTRE (28.5 = middle of the bar's lower half).
    private const float AllRowCenterY = 28.5f;
    // Horizontal position of the row's LEFT edge, from the column's left edge.
    private const float AllRowX = 0f;
    // "ALL" text size (1.0 = normal UI font size).
    private const float AllTextScale = 1.3f;
    // Moves ONLY the drawn "ALL" text, not its clickable area - for fine-tuning the look
    // (e.g. the baseline) without shifting the row. Positive X = right, positive Y = down.
    private static readonly Vector2 AllTextNudge = new(0f, 0f);

    // Shared colours of the equipment bar (same values as goldColor / greyColor in DrawEquipmentBar).
    private static readonly Vector4 EquipBarGold = new(0.9f, 0.9f, 0.68f, 0.95f);
    private static readonly Vector4 EquipBarDim = new(0.9f, 0.9f, 0.68f, 0.3f);

    // Fake small caps: ImGui has no OpenType feature support, so the first letter is drawn at
    // full size and the rest as capitals at SmallCapsScale, all sitting on one baseline.
    private const float SmallCapsScale = 0.85f; // size of the small capitals relative to the first letter

    /// <summary>Size of a fake-small-caps string at the given font size (uses the current font).</summary>
    private static Vector2 MeasureTextSmallCaps(string text, float fontSize)
    {
        if (string.IsNullOrEmpty(text)) return Vector2.Zero;
        float k = fontSize / ImGui.GetFontSize();
        string first = text[..1].ToUpperInvariant();
        string rest = text[1..].ToUpperInvariant();
        float w = ImGui.CalcTextSize(first).X * k + ImGui.CalcTextSize(rest).X * k * SmallCapsScale;
        return new Vector2(w, ImGui.GetTextLineHeight() * k);
    }

    /// <summary>Draws a fake-small-caps string with the current font into the given draw list.</summary>
    private static void AddTextSmallCaps(ImDrawListPtr dl, float fontSize, Vector2 pos, uint col, string text)
    {
        if (string.IsNullOrEmpty(text)) return;
        var font = ImGui.GetFont();
        float k = fontSize / ImGui.GetFontSize();
        float smallSize = fontSize * SmallCapsScale;
        // Baseline alignment: the smaller glyphs start lower by the difference in ascent.
        float smallYOffset = (fontSize - smallSize) * (font.Ascent / font.FontSize);

        string first = text[..1].ToUpperInvariant();
        string rest = text[1..].ToUpperInvariant();

        dl.AddText(font, fontSize, pos, col, first);
        if (rest.Length > 0)
        {
            float firstW = ImGui.CalcTextSize(first).X * k;
            dl.AddText(font, smallSize, new Vector2(pos.X + firstW, pos.Y + smallYOffset), col, rest);
        }
    }

    /// <summary>Size of an icon-font glyph drawn at the given pixel size.</summary>
    private static Vector2 MeasureIconGlyph(string glyph, float size)
    {
        ImGui.PushFont(UiBuilder.IconFont);
        Vector2 s = ImGui.CalcTextSize(glyph) * (size / ImGui.GetFontSize());
        ImGui.PopFont();
        return s;
    }

    /// <summary>Width of the toggle row (the FillDrip glyph).</summary>
    private static float MeasureToggleRowWidth()
    {
        float w = 0f;

        w = MeasureIconGlyph(ToggleGlyph, ToggleGlyphSize).X;

        return w;
    }

    /// <summary>Width of the "ALL" row (fake small caps).</summary>
    private static float MeasureAllRowWidth() => MeasureTextSmallCaps("ALL", ImGui.GetFontSize() * AllTextScale).X;

    /// <summary>Total width of the ALL/toggle column - used by the layout AND the width budget.</summary>
    private static float MeasureAllStackWidth() =>
        Math.Max(0f, Math.Max(ToggleRowX + MeasureToggleRowWidth(), AllRowX + MeasureAllRowWidth()));

    /// <summary>
    /// Draws the frameless ALL / dye-mode column at the current cursor position and leaves the
    /// cursor right after it on the same line, ready for the equipment icons.
    /// Top row   : dye-mode toggle. Left-Click toggles, Middle-Click resets the dye targets.
    /// Bottom row: ALL (filter all/none - or dye all/none while in dye mode).
    /// </summary>
    private void DrawAllStackCell(bool dyeClickMode)
    {
        var dl = ImGui.GetWindowDrawList();
        Vector2 cellLocal = ImGui.GetCursorPos();        // window-local, for SetCursorPos
        Vector2 cellScreen = ImGui.GetCursorScreenPos(); // screen space, for drawing
        Vector4 white = new(1f, 1f, 1f, 1f);

        // ===================== Top row: dye-mode toggle =====================
        // Submitted FIRST: if the two rows ever overlap, the first-submitted item wins
        // the hover/click, so the toggle stays usable.
        float toggleRowW = MeasureToggleRowWidth();
        ImGui.SetCursorPos(cellLocal + new Vector2(ToggleRowX, ToggleRowCenterY - AllStackRowHitHeight / 2f));
        bool toggleClicked = ImGui.InvisibleButton("##DyeSelectModeToggle", new Vector2(Math.Max(1f, toggleRowW), AllStackRowHitHeight));
        bool toggleMiddle = ImGui.IsItemClicked(ImGuiMouseButton.Middle);
        bool toggleHovered = ImGui.IsItemHovered();
        bool togglePressed = ImGui.IsItemActive();

        if (toggleClicked) dyeSelectModeToggle = !dyeSelectModeToggle;
        if (toggleMiddle) ResetDyeTargetsToFilter();

        // Colour = state. ON: droplet-marker red. OFF but forked (something to reset): gold.
        // OFF: dim, like an unselected equipment icon. Hover/press brighten towards white.
        Vector4 toggleCol = dyeSelectModeToggle ? DyeMarkerColor
                          : DyeTargetsDifferFromFilter ? EquipBarGold
                          : EquipBarDim;
        if (togglePressed) toggleCol = white;
        else if (toggleHovered) toggleCol = Vector4.Lerp(toggleCol, white, 0.5f);
        uint toggleColU32 = ImGui.GetColorU32(toggleCol);

        float toggleCenterScreenY = cellScreen.Y + ToggleRowCenterY;

        Vector2 glyphSize = MeasureIconGlyph(ToggleGlyph, ToggleGlyphSize);
        Vector2 glyphPos = new(cellScreen.X + ToggleRowX, toggleCenterScreenY - glyphSize.Y / 2f);
        dl.AddText(UiBuilder.IconFont, ToggleGlyphSize, glyphPos, toggleColU32, ToggleGlyph);

        if (toggleHovered)
        {
            ImGui.SetTooltip(
                $"Dye Target mode: {(dyeSelectModeToggle ? "ON" : "OFF")}\n" +
                "   While ON, clicking a slot picks which gets dyed instead of changing the filter\n" +
                "   Shift+Clicks on slots do the same without switching this on.\n" +
                "Middle-Click: reset - dye the filtered slots" +
                (DyeTargetsDifferFromFilter ? "" : " - (nothing to reset)"));
        }

        // ===================== Bottom row: ALL =====================
        bool allSelected = selectedSlots.Count == AvailableSlots.Length;
        float allFontSize = ImGui.GetFontSize() * AllTextScale;
        Vector2 allTextSize = MeasureTextSmallCaps("ALL", allFontSize);

        ImGui.SetCursorPos(cellLocal + new Vector2(AllRowX, AllRowCenterY - AllStackRowHitHeight / 2f));
        bool allClicked = ImGui.InvisibleButton("##EquipBarAll", new Vector2(Math.Max(1f, allTextSize.X), AllStackRowHitHeight));
        bool allHovered = ImGui.IsItemHovered();
        bool allPressed = ImGui.IsItemActive();

        Vector4 allCol = allSelected ? EquipBarGold : EquipBarDim;
        if (allPressed) allCol = white;
        else if (allHovered) allCol = Vector4.Lerp(allCol, white, 0.5f);

        Vector2 allTextPos = new(cellScreen.X + AllRowX, cellScreen.Y + AllRowCenterY - allTextSize.Y / 2f);
        AddTextSmallCaps(dl, allFontSize, allTextPos + AllTextNudge, ImGui.GetColorU32(allCol), "ALL");

        if (allClicked)
        {
            if (dyeClickMode)
            {
                // Dye-only: select/clear ALL dye targets, filter untouched.
                BeginDyeTargetOverride();
                // "All" means BOTH rings in Glamourer mode; Fitting Room can only dye one ring,
                // so there it's the right ring only (the big Finger icon alone shows the droplet).
                bool wantLeftRing = applyMode == 0;
                bool allDyed = dyeTargetSlots.Count == AvailableSlots.Length
                               && dyeTargetRightRing
                               && dyeTargetLeftRing == wantLeftRing;
                dyeTargetSlots.Clear();
                if (!allDyed)
                {
                    foreach (var s in AvailableSlots) dyeTargetSlots.Add(s);
                    dyeTargetRightRing = true;
                    dyeTargetLeftRing = wantLeftRing;
                }
                else
                {
                    // Cleared: ring flags are irrelevant now, restore the default for next time.
                    dyeTargetRightRing = true;
                    dyeTargetLeftRing = false;
                }
                NormalizeDyeTargets();
            }
            else if (allSelected)
            {
                selectedSlots.Clear();
                isDirty = true;
            }
            else
            {
                selectedSlots.Clear();
                foreach (var s in AvailableSlots) selectedSlots.Add(s);
                isDirty = true;
            }
        }

        if (allHovered)
        {
            ImGui.SetTooltip(dyeClickMode
                        ? "DYE TARGET mode\nSelect or Remove all slots" 
                        : "Select or Remove all slots\nShift +: change to Dye Target mode");
        }

        // Reserve the whole column as the last item (Dummy = non-interactive), so the equipment
        // icons chain off the column's full size, not off the last row's clickable area.
        ImGui.SetCursorPos(cellLocal);
        ImGui.Dummy(new Vector2(MeasureAllStackWidth(), AllStackCellHeight));
        // SameLine(0, negative) would mean "default spacing" in ImGui, so the gap is applied by
        // hand instead - this is what makes negative AllToEquipIconsGap values actually work.
        ImGui.SameLine(0, 0);
        if (AllToEquipIconsGap != 0f) ImGui.SetCursorPosX(ImGui.GetCursorPosX() + AllToEquipIconsGap);
    }

    private const string ClearSlotHintGlamourer = "\nMiddle-Click: take the item off (Nothing)\nCtrl+Middle-Click: put on the Emperor's New piece";
    private const string ClearSlotHintFittingRoom = "\nMiddle-Click: take the item off (Emperor's New piece)";
    private string ClearSlotHint => applyMode == 1 ? ClearSlotHintFittingRoom : ClearSlotHintGlamourer;

    /// <summary>Middle-Click on the bar: empties a slot. Never touches the filter or dye targets.</summary>
    private void ClearSlotFromBar(string slotName, bool useEmperor, bool ringRight, bool ringLeft)
    {
        bool fittingRoom = applyMode == 1;

        if (slotName == "Finger")
        {
            if (fittingRoom)
            {
                // The Fitting Room cannot target a specific finger: one try-on is all it supports.
                plugin.ClearSlotPreview(GlamourerIpcHandler.SlotRightRing, useEmperor, true);
                return;
            }
            if (ringRight) plugin.ClearSlotPreview(GlamourerIpcHandler.SlotRightRing, useEmperor, false);
            if (ringLeft) plugin.ClearSlotPreview(GlamourerIpcHandler.SlotLeftRing, useEmperor, false);
            return;
        }

        byte slotId = slotName switch
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
            _ => 0 
        };
        if (slotId != 0) plugin.ClearSlotPreview(slotId, useEmperor, fittingRoom);
    }

    private void DrawEquipmentBar()
    {
        // Deliberately taller than the 38px equipment icon bar: the centering formula
        // (barStartY + (barContentHeight - ringGroupHeight) / 2f) gives a NEGATIVE margin when
        // ringGroupHeight is bigger than barContentHeight - which naturally makes RR/LR poke out
        // a couple px above/below the bar's edges, on purpose, without affecting the bar's
        // official height (that is controlled separately, see below).
        float rowBtnHeight = 18f;

        bool anyPopupOpenInBar = ImGui.IsPopupOpen("", ImGuiPopupFlags.AnyPopupId);

        // Dye-target click mode: either the toggle is on, or Shift is held (momentary).
        bool shiftHeld = ImGui.GetIO().KeyShift;
        bool ctrlHeld = ImGui.GetIO().KeyCtrl;
        bool dyeClickMode = dyeSelectModeToggle || shiftHeld;
        // Markers: always while in dye mode (shows what WILL be dyed, even if it equals the
        // filter), otherwise only when the dye targets differ from the filter.
        // Shift is ignored for the markers while typing in a text box - otherwise every capital
        // letter typed in a search field would flash them. (Clicks still honour Shift normally.)
        bool dyeMarkersVisible = dyeSelectModeToggle
                                 || (shiftHeld && !ImGui.GetIO().WantTextInput)
                                 || DyeTargetsDifferFromFilter;

        // Textúra lekérése a Dalamudtól
        armouryTexture ??= Plugin.TextureProvider.GetFromGame("ui/uld/armouryboard_hr1.tex");
        var wrap = armouryTexture.GetWrapOrDefault();

        Vector4 goldColor = new Vector4(0.9f, 0.9f, 0.68f, 0.95f);
        Vector4 greyColor = new Vector4(0.9f, 0.9f, 0.68f, 0.3f);

        // Biztosítjuk, hogy legalább a jobb gyűrű (RR) aktív legyen alapértelmezés szerint
        if (!plugin.Configuration.RightRingActive && !plugin.Configuration.LeftRingActive)
        {
            plugin.Configuration.RightRingActive = true;
            plugin.Configuration.Save();
        }

        ImGui.BeginGroup();

        // --- Dynamic gap budget: measured ONCE from the actual remaining row width, then split
        // evenly across all 3 gaps, so the gaps scale smoothly with the window's width.
        float totalAvailWidth = ImGui.GetContentRegionAvail().X;
        float iconsWidth = EquipmentSlotIcons.Length * 38f;
        float allButtonWidth = MeasureAllStackWidth() + AllToEquipIconsGap;
        float ringSectionWidth = 4f + 46f; // spacing before RR/LR + ringBtnWidth
        float labelWidthForBudget = ImGui.CalcTextSize("Dye \nsets:").X;
        float dyeSquareDimForBudget = ImGui.GetFrameHeight();
        float perDyeSetWidth = 15f + 25f + 8f + (2f * dyeSquareDimForBudget + 1f) + 4f + 20f;
        float dyeSetsWidthForBudget = perDyeSetWidth * 3f; // worst case: assume all 3 sets visible

        float totalFixedContentWidth = allButtonWidth + iconsWidth + ringSectionWidth + labelWidthForBudget + dyeSetsWidthForBudget; 
        float totalGapBudget = totalAvailWidth - totalFixedContentWidth;
        float perGapWidth = Math.Clamp(totalGapBudget / 3f, EquipBarSpacerMinWidth, EquipBarSpacerMaxWidth);

        // --- DYNAMIC GAP #1: before the equipment bar itself ---
        DrawEquipBarSpacer(perGapWidth, continueOnSameLine: false);

        // --- Shared vertical anchor for the whole bar ---
        // Instead of assuming which element is tallest (equipment icons), we measure every
        // sub-element's actual height up front (all are deterministic - fixed sizes or font
        // metrics, nothing needs a draw pass to know its size) and take the real max. Every
        // sub-group then centers against THIS computed value, so if any element's size changes
        // later, the whole bar's centering adapts automatically.
        float barStartY = ImGui.GetCursorPosY();
        float barStartScreenY = ImGui.GetCursorScreenPos().Y;

        const float equipmentIconHeight = 38f;               // ALL button + slot icons
        float ringGroupHeight = (2 * rowBtnHeight) - 2f;      // RR/LR: intentionally taller than the bar, see above
        const float stackedIconRowHeight = 19f;               // single source of truth for the retweet/brush icons below
        float stackedIconGroupHeight = 2 * stackedIconRowHeight;
        float labelHeight = 2 * ImGui.GetTextLineHeight();    // "Dye sets:" label (2 lines)

        // The equipment icons are the ONLY thing that determines the bar's official height.
        // RR/LR and the dye squares are allowed to be taller / poke out past this on purpose,
        // because we explicitly re-pin the cursor after the whole bar ends (see the very end of
        // this function) - so their overflow can never push whatever comes after (the table) down.
        float barContentHeight = equipmentIconHeight;

        // 1. BAL OLDAL: frameless ALL / dye-mode column (two rows, see DrawAllStackCell).
        DrawAllStackCell(dyeClickMode);

        ImGui.BeginGroup();

        // 2. KÖZÉP: Equipment Icon Bar (11 natív .tex ikon mint gomb)
        // 2. Végigmegyünk az ikonokon és kirajzoljuk őket UV-kivágással
        for (int i = 0; i < EquipmentSlotIcons.Length; i++)
        {
            var slot = EquipmentSlotIcons[i];
            bool isSelected = selectedSlots.Contains(slot.SlotName);

            if (i > 0) ImGui.SameLine(0, 0f);

            if (wrap != null)
            {
                float texWidth = wrap.Width;
                float texHeight = wrap.Height;

                // UV koordináták kiszámítása (0.0 és 1.0 közötti arányok)
                Vector2 uv0 = new Vector2(slot.CropX / texWidth, slot.CropY / texHeight);
                Vector2 uv1 = new Vector2((slot.CropX + slot.CropWidth) / texWidth, (slot.CropY + slot.CropHeight) / texHeight);

                // Ha nincs kijelölve az adott slot, halványabban/sötétebben jelenítjük meg
                Vector4 tintColor = isSelected ? new Vector4(1f, 1f, 1f, 1f) : new Vector4(0.4f, 0.4f, 0.4f, 0.6f);

                // Predict hover/press BEFORE drawing.
                Vector2 iconBtnScreenPos = ImGui.GetCursorScreenPos();
                bool iconHovered = ImGui.IsMouseHoveringRect(iconBtnScreenPos, iconBtnScreenPos + new Vector2(38f, 38f));
                bool iconPressed = iconHovered && ImGui.IsMouseDown(ImGuiMouseButton.Left);
                if (iconPressed) tintColor = new Vector4(1f, 1f, 1f, 1f);
                else if (iconHovered) tintColor = Vector4.Lerp(tintColor, new Vector4(1f, 1f, 1f, 1f), 0.5f);

                ImGui.PushID($"EquipBtn_{slot.SlotName}");

                // Gomb hátterének és keretének átlátszóvá tétele
                ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0, 0, 0, 0));
                ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0, 0, 0, 0));
                ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0, 0, 0, 0));
                ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(1, 1));

                ImGui.ImageButton(wrap.Handle, new Vector2(38f, 38f), uv0, uv1, 0, new Vector4(0, 0, 0, 0), tintColor);

                // --- Dye-target marker: only while the dye targets differ from the filter ---
                if (dyeMarkersVisible && IsDyeTarget(slot.SlotName))
                {
                    DrawDyeTargetMarker(ImGui.GetItemRectMin(), ImGui.GetItemRectMax(), DyeMarkerSize);
                }

                // Mouse handling - dye-target mode takes priority over filtering
                if (!anyPopupOpenInBar && dyeClickMode && ImGui.IsItemClicked(ImGuiMouseButton.Left))
                {
                    // Dye ONLY this slot. isDirty deliberately untouched -> no table refilter.
                    BeginDyeTargetOverride();
                    dyeTargetSlots.Clear();
                    dyeTargetSlots.Add(slot.SlotName);
                    NormalizeDyeTargets();
                }
                else if (!anyPopupOpenInBar && dyeClickMode && ImGui.IsItemClicked(ImGuiMouseButton.Right))
                {
                    // Add/remove this slot from the dye targets.
                    BeginDyeTargetOverride();
                    if (!dyeTargetSlots.Remove(slot.SlotName)) dyeTargetSlots.Add(slot.SlotName);
                    NormalizeDyeTargets();
                }
                else if (!anyPopupOpenInBar && ImGui.IsItemClicked(ImGuiMouseButton.Left))
                {
                    // Bal klikk: kizárólag erre az ikonra szűr
                    selectedSlots.Clear();
                    selectedSlots.Add(slot.SlotName);
                    isDirty = true;
                }
                else if (!anyPopupOpenInBar && ImGui.IsItemClicked(ImGuiMouseButton.Right))
                {
                    // Jobb klikk: hozzáadja vagy kiveszi a szűrőből
                    if (selectedSlots.Contains(slot.SlotName))
                        selectedSlots.Remove(slot.SlotName);
                    else
                        selectedSlots.Add(slot.SlotName);

                    isDirty = true;
                }
                else if (!anyPopupOpenInBar && ImGui.IsItemClicked(ImGuiMouseButton.Middle))
                {
                    ClearSlotFromBar(slot.SlotName, ctrlHeld,
                        plugin.Configuration.RightRingActive, plugin.Configuration.LeftRingActive);
                }

                if (ImGui.IsItemHovered())
                {
                    string clearHint = slot.SlotName == "MainHand" ? "\nMiddle-Click for PUG/MNK: hide it with The Emperor's New Fists"
                                     : slot.SlotName == "OffHand" ? "\nMiddle-Click for shield users: hide it with The Emperor's New Shield"
                                     : ClearSlotHint;

                    ImGui.SetTooltip(dyeClickMode
                        ? "DYE TARGET mode - (the filter will not change)\nLeft-Click: dye ONLY this slot\nRight-Click: add/remove this slot from the dye targets" + clearHint
                        : "Left-Click: Filter ONLY by this equipment\nRight-Click: Add/remove this slot to/from filter" + clearHint + "\nShift+: change to Dye Target mode");
                }

                ImGui.PopStyleVar();
                ImGui.PopStyleColor(3);
                ImGui.PopID();
            }
            else
            {
                // Visszaesési opció, amíg a textúra be nem töltődik
                if (ImGui.Button(slot.DisplayName, new Vector2(38f, 38f)))
                {
                    selectedSlots.Clear();
                    selectedSlots.Add(slot.SlotName);
                    isDirty = true;
                }
            }
        }

        ImGui.EndGroup();

        ImGui.SameLine(0, 4f);

        // 3. JOBB OLDAL: RR és LR toggle gombok (ikon a szöveg mellett)
        ImGui.BeginGroup();

        // Centered against the dynamically computed barContentHeight (see top of function).
        const float ringVerticalNudge = -2f;
        ImGui.SetCursorPosY(barStartY + (barContentHeight - ringGroupHeight) / 2f + ringVerticalNudge);

        Vector2 ringUv0 = Vector2.Zero;
        Vector2 ringUv1 = Vector2.One;
        Vector2 ringUv0_mirrored = Vector2.Zero;
        Vector2 ringUv1_mirrored = Vector2.One;

        if (wrap != null)
        {
            float tw = wrap.Width;
            float th = wrap.Height;
            ringUv0 = new Vector2(240f / tw, 160f / th);
            ringUv1 = new Vector2((240f + 80f) / tw, (160f + 80f) / th);

            ringUv0_mirrored = new Vector2((240f + 80f) / tw, 160f / th);
            ringUv1_mirrored = new Vector2(240f / tw, (160f + 80f) / th);
        }

        Vector4 iconActiveTint = new Vector4(1f, 1f, 1f, 1f);
        Vector4 iconInactiveTint = new Vector4(0.4f, 0.4f, 0.4f, 0.6f);
        float ringBtnWidth = 46f; 
        // Pontosan kitölti a 18px ikont + RR/LR szöveget
        // How big the RR/LR icon glyph itself is drawn.
        // Független a rowBtnHeight-től (ami csak azt szabályozza, mennyire lógjon ki a gomb a keretből).
        float ringIconSize = 24f;
        // Derived (nem kemény szám), így az ikon mindig középen marad a sorában, bármekkora is a
        // rowBtnHeight.
        Vector2 iconOffsetMin = new Vector2(0f, (rowBtnHeight - ringIconSize) / 2f);
        Vector2 iconOffsetMax = iconOffsetMin + new Vector2(ringIconSize, ringIconSize);

        float textOffsetY = (rowBtnHeight - ImGui.GetTextLineHeight()) * 0.5f;
        Vector2 textOffset = new Vector2(ringIconSize +1f, textOffsetY);

        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(0, 0));

        // --- RR (Right Ring) ---
        bool rrActive = plugin.Configuration.RightRingActive;
        Vector4 rrTint = rrActive ? iconActiveTint : iconInactiveTint;
        Vector4 rrTextColor = rrActive ? goldColor : greyColor;

        ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0, 0, 0, 0));
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0, 0, 0, 0));
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0, 0, 0, 0));

        ImGui.Button("##RR_Button", new Vector2(ringBtnWidth, rowBtnHeight));

        if (!anyPopupOpenInBar && dyeClickMode && ImGui.IsItemClicked(ImGuiMouseButton.Left))
        {
            // Dye ONLY the right ring: clears every other dye target, Finger becomes the only one.
            BeginDyeTargetOverride();
            dyeTargetSlots.Clear();
            dyeTargetSlots.Add("Finger");
            dyeTargetRightRing = true;
            dyeTargetLeftRing = false;
            NormalizeDyeTargets();
        }
        else if (!anyPopupOpenInBar && dyeClickMode && ImGui.IsItemClicked(ImGuiMouseButton.Right))
        {
            BeginDyeTargetOverride();
            if (!dyeTargetSlots.Contains("Finger"))
            {
                // Finger wasn't dyed yet: add it, with ONLY this ring.
                dyeTargetSlots.Add("Finger");
                dyeTargetRightRing = true;
                dyeTargetLeftRing = false;
            }
            else
            {
                dyeTargetRightRing = !dyeTargetRightRing;
                if (!dyeTargetRightRing && !dyeTargetLeftRing)
                {
                    // Last dyed ring removed -> Finger is no longer a dye target at all.
                    dyeTargetSlots.Remove("Finger");
                    dyeTargetRightRing = true; // default for the next time Finger gets dyed
                }
            }
            NormalizeDyeTargets();
        }
        else if (!anyPopupOpenInBar && ImGui.IsItemClicked(ImGuiMouseButton.Left))
        {
            // Bal klikk: kizárólag a jobb kézre vált
            plugin.Configuration.RightRingActive = true;
            plugin.Configuration.LeftRingActive = false;
            plugin.Configuration.Save();
        }
        else if (!anyPopupOpenInBar && ImGui.IsItemClicked(ImGuiMouseButton.Right))
        {
            // Jobb klikk: hozzáadja/kiveszi (többes kijelölés)
            plugin.Configuration.RightRingActive = !rrActive;
            if (!plugin.Configuration.RightRingActive && !plugin.Configuration.LeftRingActive)
            {
                plugin.Configuration.RightRingActive = true;
            }
            plugin.Configuration.Save();
        }
        else if (!anyPopupOpenInBar && ImGui.IsItemClicked(ImGuiMouseButton.Middle))
        {
            ClearSlotFromBar("Finger", ctrlHeld, ringRight: true, ringLeft: false);
        }

        // Button() drawn AFTER the click handling above; we draw the icon/text manually below, so
        // we CAN check hover/active here, right after the button, unlike ALL/equipment icons.
        bool rrHovered = ImGui.IsItemHovered();
        bool rrPressed = ImGui.IsItemActive();
        if (rrPressed) { rrTint = new Vector4(1f, 1f, 1f, 1f); rrTextColor = new Vector4(1f, 1f, 1f, 1f); }
        else if (rrHovered) { rrTint = Vector4.Lerp(rrTint, new Vector4(1f, 1f, 1f, 1f), 0.5f); rrTextColor = Vector4.Lerp(rrTextColor, new Vector4(1f, 1f, 1f, 1f), 0.5f); }

        Vector2 rrMin = ImGui.GetItemRectMin();
        var drawList = ImGui.GetWindowDrawList();

        if (wrap != null)
        {
            drawList.AddImage(wrap.Handle, rrMin + iconOffsetMin, rrMin + iconOffsetMax, ringUv0, ringUv1, ImGui.GetColorU32(rrTint));
        }
        drawList.AddText(rrMin + textOffset, ImGui.GetColorU32(rrTextColor), "RR");

        // Ring dye markers are only meaningful when the Finger slot is actually a dye target,
        // and only in Glamourer mode - Fitting Room can't target the left ring at all, so there
        // the big Finger icon's own droplet is enough on its own.
        if (dyeMarkersVisible && applyMode == 0 && DyeTargetsRightRing && IsDyeTarget("Finger"))
        {
            Vector2 rrIconMin = rrMin + iconOffsetMin;
            DrawDyeTargetMarker(rrIconMin, rrIconMin + new Vector2(ringIconSize, ringIconSize), DyeMarkerSizeRing);
        }

        ImGui.PopStyleColor(3);

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(dyeClickMode
                        ? "DYE TARGET mode - (the selection will not change)\nLeft-Click: dye ONLY this slot\nRight-Click: add/remove this slot from the dye targets" + ClearSlotHint
                        : "Preview on Right Ring slot (RR)" +ClearSlotHint +"\nShift+: change to Dye Target mode");
        }

        // Függőleges távolság csökkentése a két gomb között (enyhe átfedés)
        ImGui.SetCursorPosY(ImGui.GetCursorPosY() - 2f);

        // --- LR (Left Ring) ---
        bool lrActive = plugin.Configuration.LeftRingActive;
        Vector4 lrTint = lrActive ? iconActiveTint : iconInactiveTint;
        Vector4 lrTextColor = lrActive ? goldColor : greyColor;

        ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0, 0, 0, 0));
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0, 0, 0, 0));
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0, 0, 0, 0));

        ImGui.Button("##LR_Button", new Vector2(ringBtnWidth, rowBtnHeight));

        if (!anyPopupOpenInBar && dyeClickMode && ImGui.IsItemClicked(ImGuiMouseButton.Left))
        {
            // Dye ONLY the left ring: clears every other dye target, Finger becomes the only one.
            BeginDyeTargetOverride();
            dyeTargetSlots.Clear();
            dyeTargetSlots.Add("Finger");
            dyeTargetLeftRing = true;
            dyeTargetRightRing = false;
            NormalizeDyeTargets();
        }
        else if (!anyPopupOpenInBar && dyeClickMode && ImGui.IsItemClicked(ImGuiMouseButton.Right))
        {
            BeginDyeTargetOverride();
            if (!dyeTargetSlots.Contains("Finger"))
            {
                // Finger wasn't dyed yet: add it, with ONLY this ring.
                dyeTargetSlots.Add("Finger");
                dyeTargetLeftRing = true;
                dyeTargetRightRing = false;
            }
            else
            {
                dyeTargetLeftRing = !dyeTargetLeftRing;
                if (!dyeTargetRightRing && !dyeTargetLeftRing)
                {
                    // Last dyed ring removed -> Finger is no longer a dye target at all.
                    dyeTargetSlots.Remove("Finger");
                    dyeTargetRightRing = true; // default for the next time Finger gets dyed
                }
            }
            NormalizeDyeTargets();
        }
        else if (!anyPopupOpenInBar && ImGui.IsItemClicked(ImGuiMouseButton.Left))
        {
            // Bal klikk: kizárólag a bal kézre vált
            plugin.Configuration.LeftRingActive = true;
            plugin.Configuration.RightRingActive = false;
            plugin.Configuration.Save();
        }
        else if (!anyPopupOpenInBar && ImGui.IsItemClicked(ImGuiMouseButton.Right))
        {
            // Jobb klikk: hozzáadja/kiveszi (többes kijelölés)
            plugin.Configuration.LeftRingActive = !lrActive;
            if (!plugin.Configuration.RightRingActive && !plugin.Configuration.LeftRingActive)
            {
                plugin.Configuration.RightRingActive = true;
            }
            plugin.Configuration.Save();
        }
        else if (!anyPopupOpenInBar && ImGui.IsItemClicked(ImGuiMouseButton.Middle))
        {
            ClearSlotFromBar("Finger", ctrlHeld, ringRight: false, ringLeft: true);
        }

        bool lrHovered = ImGui.IsItemHovered();
        bool lrPressed = ImGui.IsItemActive();
        if (lrPressed) { lrTint = new Vector4(1f, 1f, 1f, 1f); lrTextColor = new Vector4(1f, 1f, 1f, 1f); }
        else if (lrHovered) { lrTint = Vector4.Lerp(lrTint, new Vector4(1f, 1f, 1f, 1f), 0.5f); lrTextColor = Vector4.Lerp(lrTextColor, new Vector4(1f, 1f, 1f, 1f), 0.5f); }

        Vector2 lrMin = ImGui.GetItemRectMin();

        if (wrap != null)
        {
            drawList.AddImage(wrap.Handle, lrMin + iconOffsetMin, lrMin + iconOffsetMax, ringUv0_mirrored, ringUv1_mirrored, ImGui.GetColorU32(lrTint));
        }
        drawList.AddText(lrMin + textOffset, ImGui.GetColorU32(lrTextColor), "LR");

        // Ring dye markers are only meaningful when the Finger slot is actually a dye target,
        // and only in Glamourer mode - Fitting Room can't target the left ring at all, so there
        // the big Finger icon's own droplet is enough on its own.
        if (dyeMarkersVisible && applyMode == 0 && DyeTargetsLeftRing && IsDyeTarget("Finger"))
        {
            Vector2 lrIconMin = lrMin + iconOffsetMin;
            DrawDyeTargetMarker(lrIconMin, lrIconMin + new Vector2(ringIconSize, ringIconSize), DyeMarkerSizeRing);
        }

        ImGui.PopStyleColor(3);
        ImGui.PopStyleVar(); // FramePadding

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(dyeClickMode
                       ? "DYE TARGET mode - (the selection will not change)\nLeft-Click: dye ONLY this slot\nRight-Click: add/remove this slot from the dye targets" + ClearSlotHint
                       : "Preview on Left Ring slot (LR)\n" +
                             "Note: In-game 'Fitting Room' mode does not support Left Ring selection." +ClearSlotHint +"\nShift+: change to Dye Target mode");
        }

        ImGui.EndGroup(); // Ring gombok vége

        // --- DYNAMIC GAP #2: between icon bar / RR-LR group and dye sets ---
        DrawEquipBarSpacer(perGapWidth, continueOnSameLine: true);

        // 4. dye channels
        ImGui.BeginGroup();
        ImGui.SetCursorPosY(barStartY);

        // --- Layout & Customization Settings ---
        float itemSpacingY = ImGui.GetStyle().ItemSpacing.Y;

        float dyeSquareDimension = ImGui.GetFrameHeight() + 1f;

        Vector2 dyeSquareSize = new(dyeSquareDimension, dyeSquareDimension);

        // --- Label: "Dye preview:" (Vertically Centered against the shared bar anchor) ---
        // Ez tűnik el elsőként ablak-szűkítéskor - a tényleges dye kockák/ikonok fontosabbak,
        // mint ez a felirat, szóval ez megy először.
        bool showDyeLabel = ImGui.GetContentRegionAvail().X >= DyeLabelMinWidth;
        if (showDyeLabel)
        {
            ImGui.BeginGroup();
            ImGui.SetCursorPosY(barStartY + (barContentHeight - labelHeight) / 2f);
            ImGui.PushStyleColor(ImGuiCol.Text, goldColor); // match RR/LR's gold
            ImGui.TextUnformatted("Dye \nsets:");
            ImGui.PopStyleColor();
            ImGui.EndGroup();

            ImGui.SameLine(0, 1);
            HelpMarker("Use and select dye sets for previewing items, \nUse the brush button to apply the dyes to selected slots.\n Note: Use the equipment bar to select which slots to dye with the brush.");
        }

        Vector4 activeGoldColor = new Vector4(1.0f, 0.85f, 0.1f, 1.0f);
        Vector4 inactiveGreyColor = ImGui.GetStyle().Colors[(int)ImGuiCol.TextDisabled];

        float paletteIconSize = 22f; // matches the palette icon's InvisibleButton hitbox below
        // Fine-tune: negative moves the palette icon UP, positive moves it DOWN. The icon font's
        // own glyph doesn't necessarily sit dead-center within its nominal size, so this exists
        // purely for eyeballing it right - not something we can compute exactly from font metrics.
        const float paletteVerticalNudge = -2f;
        float middle = barStartY + (barContentHeight - paletteIconSize) / 2f + paletteVerticalNudge;

        // --- 3 Dye Sets Loop ---
        // Collapse order when the window gets too narrow: Dye Set 3 drops first (see DyeSetMinWidthEach).
        int visibleDyeSets = 3;
        if (ImGui.GetContentRegionAvail().X < DyeSetMinWidthEach)
        {
            visibleDyeSets = 2;
        }
        for (int i = 0; i < visibleDyeSets; i++)
        {
            ImGui.SameLine(0, 15f);

            bool isSetSelected = (plugin.Configuration.SelectedDyeSet == i);
            var currentSet = plugin.Configuration.DyeSets[i];

            // 1. Palette Icon (vertically centred, works as a radio button for the dye set)
            ImGui.BeginGroup();
            ImGui.SetCursorPosY(middle);

            Vector2 screenPos = ImGui.GetCursorScreenPos();
            // The hitbox has to match the glyph's real footprint (drawn at 23px below), otherwise the
            // group's reported width is too small and the dye squares that follow (SameLine below)
            // end up on top of the palette icon.
            float size = 23f;

            // Invisible button to capture clicks over the palette icon area
            ImGui.PushID($"DyeSetRadio_{i}");
            if (ImGui.InvisibleButton($"##RadioBtn_{i}", new Vector2(size, size)))
            {
                plugin.Configuration.SelectedDyeSet = isSetSelected ? -1 : i;
                plugin.Configuration.Save();
            }
            bool isHovered = ImGui.IsItemHovered();
            ImGui.PopID();

            drawList = ImGui.GetWindowDrawList();
            var iconStr = FontAwesomeIcon.Palette.ToIconString();

            // Base Outer Palette (Dark Gray)
            Vector4 outerColor = isHovered
                ? new Vector4(0.55f, 0.55f, 0.55f, 0.35f)
                : new Vector4(0.35f, 0.35f, 0.35f, 0.5f);

            // Render background palette (23px)
            drawList.AddText(UiBuilder.IconFont, 23f, screenPos, ImGui.GetColorU32(outerColor), iconStr);

            // Render inner active palette (smaller, white, on top)
            if (isSetSelected)
            {
                Vector4 innerColor = new Vector4(1.0f, 1.0f, 1.0f, 1.0f);
                Vector2 centeredOffset = screenPos + new Vector2(3f, 3f);
                drawList.AddText(UiBuilder.IconFont, 17f, centeredOffset, ImGui.GetColorU32(innerColor), iconStr);
            }

            if (isHovered)
            {
                string tooltip = isSetSelected
                    ? $"Dye Set {i + 1} Active (Click to deselect)"
                    : $"Select Dye Set {i + 1}";
                ImGui.SetTooltip(tooltip);
            }

            ImGui.EndGroup();

            ImGui.SameLine(0, 4f);

            // 2. Two Dye Squares (side by side, staggered: Dye 1 flush with the bar's top edge,
            // Dye 2 flush with the bar's bottom edge)
            ImGui.BeginGroup();
            ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, Vector2.Zero); // removes the default border/padding around the ColorButtons

            // Dye 1 - poking slightly ABOVE the bar's top edge on purpose (see DyeSquareOverflow).
            ImGui.SetCursorPosY(barStartY - DyeSquareOverflow);

            const float tintSlashNudgeX = 1.5f;
            const float tintSlashNudgeY = 0f;
            const float tintNudgeX = 1f;
            const float tintNudgeY = 0f;

            // Dye 1
            var dye1Info = DyeHelper.GetDye(Plugin.DataManager, currentSet.Dye1);
            bool dye1IsKeepCurrent = currentSet.Dye1 == DyeHelper.DyeKeepCurrent;
            bool dye1IsNone = currentSet.Dye1 == 0;
            Vector4 dye1Color = (dye1IsKeepCurrent || dye1IsNone)
                ? new Vector4(0.25f, 0.25f, 0.25f, 1.0f) // same grey as the popup's None/Keep Current swatches
                : (dye1Info?.ColorVec ?? new Vector4(0.2f, 0.2f, 0.2f, 0.6f));

            ImGui.PushID($"Dye1_Square_{i}");
            Vector2 dye1SquarePos = ImGui.GetCursorScreenPos();
            // Both dye pickers of this set open here, below the bar, so they never cover the set's own squares
            Vector2 dyePickerPopupPos = new(
                dye1SquarePos.X + DyePickerPopupOffsetX,
                barStartScreenY + equipmentIconHeight + BottomGapAdjustment + DyePickerPopupOffsetY);
            if (ImGui.ColorButton($"##Dye1_Btn_{i}", dye1Color, ImGuiColorEditFlags.NoTooltip, dyeSquareSize))
            {
                // Shift+Click opens the picker as an Instant Hover Over Dye Preview (MainWindow_DyePreview.cs)
                BeginDyePickerSession(i, 1, withPreview: ImGui.GetIO().KeyShift);
                ImGui.OpenPopup($"DyePickerPopup_{i}_1");
            }
            if (!anyPopupOpenInBar && ImGui.IsItemClicked(ImGuiMouseButton.Right))
            {
                // Quick toggle, no popup needed: Keep Current (255) <-> None (0), alternating on repeat right-clicks
                currentSet.Dye1 = dye1IsKeepCurrent ? (byte)0 : DyeHelper.DyeKeepCurrent;
                plugin.Configuration.Save();
            }
            if (dye1IsKeepCurrent)
            {
                var dye1DrawList = ImGui.GetWindowDrawList();
                ImGui.PushFont(UiBuilder.IconFont);
                ImGui.SetWindowFontScale(0.8f);
                string dye1LockIcon = FontAwesomeIcon.Tint.ToIconString();
                Vector2 dye1LockSize = ImGui.CalcTextSize(dye1LockIcon);
                Vector2 dye1LockPos = dye1SquarePos + (dyeSquareSize - dye1LockSize) * 0.5f + new Vector2(tintNudgeX, tintNudgeY);
                dye1DrawList.AddText(UiBuilder.IconFont, ImGui.GetFontSize(), dye1LockPos, ImGui.GetColorU32(new Vector4(0.75f, 0.75f, 0.75f, 0.9f)), dye1LockIcon);
                ImGui.PopFont();
                ImGui.SetWindowFontScale(1f);
            }
            else if (dye1IsNone)
            {
                var dye1DrawList = ImGui.GetWindowDrawList();
                ImGui.PushFont(UiBuilder.IconFont);
                ImGui.SetWindowFontScale(0.8f);
                string dye1BanIcon = FontAwesomeIcon.TintSlash.ToIconString();
                Vector2 dye1BanSize = ImGui.CalcTextSize(dye1BanIcon);
                Vector2 dye1BanPos = dye1SquarePos + (dyeSquareSize - dye1BanSize) * 0.5f + new Vector2(tintSlashNudgeX, tintSlashNudgeY);
                dye1DrawList.AddText(UiBuilder.IconFont, ImGui.GetFontSize(), dye1BanPos, ImGui.GetColorU32(new Vector4(0.75f, 0.75f, 0.75f, 0.9f)), dye1BanIcon);
                ImGui.PopFont();
                ImGui.SetWindowFontScale(1f);
            }
            else if (dye1Info != null && DyeHelper.IsMetallic(dye1Info.Id))
            {
                DrawMetallicOverlay(ImGui.GetWindowDrawList(), dye1SquarePos, dyeSquareSize);
            }
            if (ImGui.IsItemHovered())
            {
                string name1 = dye1IsKeepCurrent ? "Keep current Glamourer dye" : (dye1Info != null ? dye1Info.Name : "None (No Dye)");
                ImGui.SetTooltip($"Set {i + 1} - Dye 1: {name1}\nClick to select dye (in the picker: Ctrl+Click sets Dye 2)\nShift+Click: Instant Hover Over Dye Preview\nRight-click: toggle Keep Current / No Dye");
            }
            DrawDyePickerPopup(i, 1, dyePickerPopupPos); 
            ImGui.PopID();

            ImGui.SameLine(0, 1f); // squares almost touching
            // Dye 2 - poking slightly BELOW the bar's bottom edge on purpose (see DyeSquareOverflow).
            ImGui.SetCursorPosY(barStartY + barContentHeight - dyeSquareDimension + DyeSquareOverflow);

            // Dye 2
            var dye2Info = DyeHelper.GetDye(Plugin.DataManager, currentSet.Dye2);
            bool dye2IsKeepCurrent = currentSet.Dye2 == DyeHelper.DyeKeepCurrent;
            bool dye2IsNone = currentSet.Dye2 == 0;
            Vector4 dye2Color = (dye2IsKeepCurrent || dye2IsNone)
                ? new Vector4(0.25f, 0.25f, 0.25f, 1.0f) // same grey as the popup's None/Keep Current swatches
                : (dye2Info?.ColorVec ?? new Vector4(0.2f, 0.2f, 0.2f, 0.6f));

            ImGui.PushID($"dye2_Square_{i}");
            Vector2 dye2SquarePos = ImGui.GetCursorScreenPos();
            if (ImGui.ColorButton($"##dye2_Btn_{i}", dye2Color, ImGuiColorEditFlags.NoTooltip, dyeSquareSize))
            {
                // Shift+Click opens the picker as an Instant Hover Over Dye Preview (MainWindow_DyePreview.cs)
                BeginDyePickerSession(i, 2, withPreview: ImGui.GetIO().KeyShift);
                ImGui.OpenPopup($"DyePickerPopup_{i}_2");
            }
            if (!anyPopupOpenInBar && ImGui.IsItemClicked(ImGuiMouseButton.Right))
            {
                // Quick toggle, no popup needed: Keep Current (255) <-> None (0), alternating on repeat right-clicks
                currentSet.Dye2 = dye2IsKeepCurrent ? (byte)0 : DyeHelper.DyeKeepCurrent;
                plugin.Configuration.Save();
            }
            if (dye2IsKeepCurrent)
            {
                var dye2DrawList = ImGui.GetWindowDrawList();
                ImGui.PushFont(UiBuilder.IconFont);
                ImGui.SetWindowFontScale(0.8f);
                string dye2LockIcon = FontAwesomeIcon.Tint.ToIconString();
                Vector2 dye2LockSize = ImGui.CalcTextSize(dye2LockIcon);
                Vector2 dye2LockPos = dye2SquarePos + (dyeSquareSize - dye2LockSize) * 0.5f + new Vector2(tintNudgeX, tintNudgeY);
                dye2DrawList.AddText(UiBuilder.IconFont, ImGui.GetFontSize(), dye2LockPos, ImGui.GetColorU32(new Vector4(0.75f, 0.75f, 0.75f, 0.9f)), dye2LockIcon);
                ImGui.PopFont();
                ImGui.SetWindowFontScale(1f);
            }
            else if (dye2IsNone)
            {
                var dye2DrawList = ImGui.GetWindowDrawList();
                ImGui.PushFont(UiBuilder.IconFont);
                ImGui.SetWindowFontScale(0.8f);
                string dye2BanIcon = FontAwesomeIcon.TintSlash.ToIconString();
                Vector2 dye2BanSize = ImGui.CalcTextSize(dye2BanIcon);
                Vector2 dye2BanPos = dye2SquarePos + (dyeSquareSize - dye2BanSize) * 0.5f + new Vector2(tintSlashNudgeX, tintSlashNudgeY);
                dye2DrawList.AddText(UiBuilder.IconFont, ImGui.GetFontSize(), dye2BanPos, ImGui.GetColorU32(new Vector4(0.75f, 0.75f, 0.75f, 0.9f)), dye2BanIcon);
                ImGui.PopFont();
                ImGui.SetWindowFontScale(1f);
            }
            else if (dye2Info != null && DyeHelper.IsMetallic(dye2Info.Id))
            {
                DrawMetallicOverlay(ImGui.GetWindowDrawList(), dye2SquarePos, dyeSquareSize);
            }
            if (ImGui.IsItemHovered())
            {
                string name2 = dye2IsKeepCurrent ? "Keep current Glamourer dye" : (dye2Info != null ? dye2Info.Name : "None (No Dye)");
                ImGui.SetTooltip($"Set {i + 1} - Dye 2: {name2}\nClick to select dye (in the picker: Ctrl+Click sets Dye 1)\nShift+Click: Instant Hover Over Dye Preview\nRight-click: toggle Keep Current / No Dye");
            }
            DrawDyePickerPopup(i, 2, dyePickerPopupPos); 
            ImGui.PopID();
            ImGui.PopStyleVar();
            ImGui.EndGroup();

            ImGui.SameLine(0, 1f);

            // 3. Two stacked selectable icons (swap dyes / apply dye set).
            // The icons sit in a fixed 2-row block (stackedIconRowHeight each, 0px spacing) measured
            // from the group's own local start, so the group is always exactly 2 rows tall.
            ImGui.BeginGroup();
            ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(0f, 0f)); // No gap between stacked rows
            ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(0f, 0f)); // Removes internal padding

            const float stackedIconRowW = 20f; // 14px is too narrow: at 0.90/0.80 scale the glyphs get clipped on the right edge
            ImGui.SetCursorPosY(barStartY + (barContentHeight - stackedIconGroupHeight) / 2f);

            // Predict hover BEFORE drawing (Selectable bakes its text color into the same draw
            // call). SelectableTextAlign centers the glyph both horizontally and vertically inside
            // its own button box. Header/HeaderHovered/HeaderActive transparent = no background
            // highlight; the icon's OWN color brightens instead.
            Vector2 swapBtnScreenPos = ImGui.GetCursorScreenPos();
            bool swapWillHover = ImGui.IsMouseHoveringRect(swapBtnScreenPos, swapBtnScreenPos + new Vector2(stackedIconRowW, stackedIconRowHeight));
            Vector4 swapIconColor = swapWillHover ? Vector4.Lerp(goldColor, new Vector4(1f, 1f, 1f, 1f), 0.5f) : goldColor;

            ImGui.PushStyleColor(ImGuiCol.Header, new Vector4(0, 0, 0, 0));
            ImGui.PushStyleColor(ImGuiCol.HeaderHovered, new Vector4(0, 0, 0, 0));
            ImGui.PushStyleColor(ImGuiCol.HeaderActive, new Vector4(0, 0, 0, 0));
            ImGui.PushStyleColor(ImGuiCol.Text, swapIconColor);
            ImGui.PushStyleVar(ImGuiStyleVar.SelectableTextAlign, new Vector2(-1f, 0.5f));
            ImGui.PushFont(UiBuilder.IconFont);
            ImGui.SetWindowFontScale(0.90f);
            bool swapClicked = ImGui.Selectable($"{FontAwesomeIcon.Retweet.ToIconString()}##SwapDyes_{i}", false, ImGuiSelectableFlags.None, new Vector2(stackedIconRowW, stackedIconRowHeight));
            bool swapHovered = ImGui.IsItemHovered();
            ImGui.SetWindowFontScale(1.0f);
            ImGui.PopFont();
            ImGui.PopStyleVar();
            ImGui.PopStyleColor(4);

            // SetTooltip must run AFTER PopFont/scale-reset above, not while the icon font +
            // fractional scale are still active - otherwise the tooltip text renders in the icon font
            // (no matching glyphs) and comes out as garbage.
            if (swapClicked)
            {
                (currentSet.Dye1, currentSet.Dye2) = (currentSet.Dye2, currentSet.Dye1);
                plugin.Configuration.Save();
            }
            if (swapHovered) ImGui.SetTooltip($"Swap Dye 1 and Dye 2 for Set {i + 1}");

            Vector2 applyBtnScreenPos = ImGui.GetCursorScreenPos();
            bool applyWillHover = ImGui.IsMouseHoveringRect(applyBtnScreenPos, applyBtnScreenPos + new Vector2(stackedIconRowW, stackedIconRowHeight));
            Vector4 applyIconColor = applyWillHover ? Vector4.Lerp(goldColor, new Vector4(1f, 1f, 1f, 1f), 0.5f) : goldColor;

            ImGui.PushStyleColor(ImGuiCol.Header, new Vector4(0, 0, 0, 0));
            ImGui.PushStyleColor(ImGuiCol.HeaderHovered, new Vector4(0, 0, 0, 0));
            ImGui.PushStyleColor(ImGuiCol.HeaderActive, new Vector4(0, 0, 0, 0));
            ImGui.PushStyleColor(ImGuiCol.Text, applyIconColor);
            ImGui.PushStyleVar(ImGuiStyleVar.SelectableTextAlign, new Vector2(4f, 0.5f));
            ImGui.PushFont(UiBuilder.IconFont);
            ImGui.SetWindowFontScale(0.90f);
            bool applyClicked = ImGui.Selectable($"{FontAwesomeIcon.Brush.ToIconString()}##ApplyDyes_{i}", false, ImGuiSelectableFlags.None, new Vector2(stackedIconRowW, stackedIconRowHeight));
            bool applyHovered = ImGui.IsItemHovered();
            ImGui.SetWindowFontScale(1.0f);
            ImGui.PopFont();
            ImGui.PopStyleVar();
            ImGui.PopStyleColor(4);

            if (applyClicked)
            {
                ApplyDyeSetToSelectedSlots(i);
            }
            if (applyHovered)
            {
                ImGui.SetTooltip($"Apply Dye Set {i + 1} to selected equipment slots");
            }

            ImGui.PopStyleVar(2);
            ImGui.EndGroup();
        }

        ImGui.EndGroup();

        // --- DYNAMIC GAP #3: after the dye sets ---
        DrawEquipBarSpacer(perGapWidth, continueOnSameLine: true);

        ImGui.EndGroup(); // Teljes Bar vége

        // Force the row's official height to exactly the equipment icons' height,
        // regardless of RR/LR or the dye squares poking out above/below it, PLUS a small manual
        // adjustment (BottomGapAdjustment) to balance the gap below against the gap above.
        ImGui.SetCursorPosY(barStartY + equipmentIconHeight + BottomGapAdjustment);
    }
}
