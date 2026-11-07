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
    // Droplet of a dye target that is not dyed: an accessory while the dyes go to the Fitting Room
    private static readonly Vector4 DyeMarkerDimmedColor = new(0.40f, 0.40f, 0.40f, 0.90f); // dark grey, barely transparent
    
    /// <summary>
    /// Draws the small "this slot gets dyed" tint droplet into the top-right corner of a rect.
    /// Raw draw-list text (2 calls, one being a 1px shadow for readability) - not an ImGui item,
    /// so it can't affect layout and costs nothing measurable.
    /// </summary>
    private static void DrawDyeTargetMarker(Vector2 rectMin, Vector2 rectMax, float glyphSize, bool dimmed = false)
    {
        var dl = ImGui.GetWindowDrawList();
        Vector4 color = dimmed ? DyeMarkerDimmedColor : DyeMarkerColor;
        Vector2 pos = new(rectMax.X - glyphSize * DyeMarkerInsetFactor, rectMin.Y + 1f);
        dl.AddText(UiBuilder.IconFont, glyphSize, pos + new Vector2(1f, 1f), ImGui.GetColorU32(new Vector4(0f, 0f, 0f, 0.85f)), DyeMarkerIcon);
        dl.AddText(UiBuilder.IconFont, glyphSize, pos, ImGui.GetColorU32(color), DyeMarkerIcon);
    }

    private static readonly string[] AvailableSlots = {
        "Head", "Body", "Hands", "Legs", "Feet",
        "Ears", "Neck", "Wrists", "Finger", "MainHand", "OffHand"
    };

    // Struktúra az ikonok adatainak és az armouryboard.tex-en belüli koordinátáinak tárolásához
    private record EquipSlotIconInfo(
        string SlotName,
        string DisplayName,
        float CropX,      // Kivágás kezdő X koordinátája pixelekben
        float CropY,      // Kivágás kezdő Y koordinátája pixelekben
        float CropWidth,  // Ikon szélessége pixelekben
        float CropHeight  // Ikon magassága pixelekben
    );

    // Az Armoury Board slotok és a hozzájuk tartozó UV kivágások (az armouryboard_hr1.tex kép alapján)
    private static readonly EquipSlotIconInfo[] EquipmentSlotIcons = new EquipSlotIconInfo[]
    {
        new("MainHand", "Main Hand", 0f,   0f, 80f, 80f),
        new("OffHand",  "Off Hand",  240f, 80f, 80f, 80f),
        new("Head",     "Head",      80f,  0f, 80f, 80f),
        new("Body",     "Chest",     160f, 0f, 80f, 80f),
        new("Hands",    "Gloves",    240f, 0f, 80f, 80f),
        new("Legs",     "Pants",     80f, 80f, 80f, 80f),
        new("Feet",     "Feet",      160f, 80f, 80f, 80f),
        new("Ears",     "Earrings",  0f, 160f, 80f, 80f),
        new("Neck",     "Necklace",  80f, 160f, 80f, 80f),
        new("Wrists",   "Bracelet",  160f, 160f, 80f, 80f),
        new("Finger",   "Ring",      240f, 160f, 80f, 80f)    
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
    private static readonly float AllToEquipIconsGap = 0f;
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
       
        // || DyePickerBarClicked: the clicks still work under an open dye picker (MainWindow_DyeDraw.cs)
        bool toggleClicked = ImGui.InvisibleButton("##DyeSelectModeToggle", new Vector2(Math.Max(1f, toggleRowW), AllStackRowHitHeight))
                             || DyePickerBarClicked(ImGuiMouseButton.Left);
        bool toggleMiddle = ImGui.IsItemClicked(ImGuiMouseButton.Middle) || DyePickerBarClicked(ImGuiMouseButton.Middle);
        
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
        
        bool allClicked = ImGui.InvisibleButton("##EquipBarAll", new Vector2(Math.Max(1f, allTextSize.X), AllStackRowHitHeight))
                          || DyePickerBarClicked(ImGuiMouseButton.Left);
        
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


    /// <summary>Tooltip line of an accessory slot while the dyes go to the Fitting Room, which can't dye it; empty otherwise.</summary>
    private string FittingRoomSkipHint(string slotName)
    {
        if (Plugin.FittingRoomCanDye(slotName) || !DyesGoToFittingRoom) return "";
        return applyMode == 1
            ? "\nAccessories can't be dyed in Fitting Room - the dyes skip these slots"
            : "\nGlamourer isn't running - dyes go to the Fitting Room:\nAccessories can't be dyed in there - the dyes skip these slots";
    }

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

    /// <summary>Measurements of the bar, passed to the parts drawn in other files (dye sets: MainWindow_DyeDraw.cs).</summary>
    private readonly record struct EquipBarLayout(
        float StartY,                  // window-local Y of the bar's top edge
        float StartScreenY,            // the same in screen space
        float ContentHeight,           // the bar's official height
        float IconHeight,              // equipment icon height
        float LabelHeight,             // "Dye sets:" label (2 lines)
        float StackedIconRowHeight,    // one row of the retweet / brush icons
        float StackedIconGroupHeight,  // both rows
        bool AnyPopupOpen);            // a popup was open when the bar started drawing

    private void DrawEquipmentBar()
    {
        // Deliberately taller than the 38px equipment icon bar: the centering formula
        // (barStartY + (barContentHeight - ringGroupHeight) / 2f) gives a NEGATIVE margin when
        // ringGroupHeight is bigger than barContentHeight - which naturally makes RR/LR poke out
        // a couple px above/below the bar's edges, on purpose, without affecting the bar's
        // official height (that is controlled separately, see below).
        float rowBtnHeight = 18f;

        BeginEquipBarFrame(); // dye picker open over the bar (MainWindow_DyeDraw.cs)
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
        
        // Accessory droplets are dimmed while the dyes go to the Fitting Room, which can't dye them.
        // Only checked while such a droplet shows, so Glamourer isn't asked on every frame.
        bool dimAccessoryMarkers = dyeMarkersVisible && AccessoryIsDyeTarget() && DyesGoToFittingRoom;

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
                    DrawDyeTargetMarker(ImGui.GetItemRectMin(), ImGui.GetItemRectMax(), DyeMarkerSize,
                        dimmed: dimAccessoryMarkers && !Plugin.FittingRoomCanDye(slot.SlotName));
                }

                // Mouse handling - dye-target mode takes priority over filtering
                if (dyeClickMode && EquipBarClicked(ImGuiMouseButton.Left, anyPopupOpenInBar))
                {
                    // Dye ONLY this slot. isDirty deliberately untouched -> no table refilter.
                    BeginDyeTargetOverride();
                    dyeTargetSlots.Clear();
                    dyeTargetSlots.Add(slot.SlotName);
                    NormalizeDyeTargets();
                }
                else if (dyeClickMode && EquipBarClicked(ImGuiMouseButton.Right, anyPopupOpenInBar))
                {
                    // Add/remove this slot from the dye targets.
                    BeginDyeTargetOverride();
                    if (!dyeTargetSlots.Remove(slot.SlotName)) dyeTargetSlots.Add(slot.SlotName);
                    NormalizeDyeTargets();
                }
                else if (EquipBarClicked(ImGuiMouseButton.Left, anyPopupOpenInBar))
                {
                    // Bal klikk: kizárólag erre az ikonra szűr
                    selectedSlots.Clear();
                    selectedSlots.Add(slot.SlotName);
                    isDirty = true;
                }
                else if (EquipBarClicked(ImGuiMouseButton.Right, anyPopupOpenInBar))
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
                        ? "DYE TARGET mode - (the filter will not change)\nLeft-Click: dye ONLY this slot\nRight-Click: add/remove this slot from the dye targets" 
                        + FittingRoomSkipHint(slot.SlotName) + clearHint
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

        if (dyeClickMode && EquipBarClicked(ImGuiMouseButton.Left, anyPopupOpenInBar))
        {
            // Dye ONLY the right ring: clears every other dye target, Finger becomes the only one.
            BeginDyeTargetOverride();
            dyeTargetSlots.Clear();
            dyeTargetSlots.Add("Finger");
            dyeTargetRightRing = true;
            dyeTargetLeftRing = false;
            NormalizeDyeTargets();
        }
        else if (dyeClickMode && EquipBarClicked(ImGuiMouseButton.Right, anyPopupOpenInBar))
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
        else if (EquipBarClicked(ImGuiMouseButton.Left, anyPopupOpenInBar))
        {
            // Bal klikk: kizárólag a jobb kézre vált
            plugin.Configuration.RightRingActive = true;
            plugin.Configuration.LeftRingActive = false;
            plugin.Configuration.Save();
        }
        else if (EquipBarClicked(ImGuiMouseButton.Right, anyPopupOpenInBar))
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

        // Ring dye markers only show when the Finger slot is actually a dye target. In Fitting Room mode they show
        // too (dimmed: it can't dye rings), so the ring selection stays visible when the mode changes.
        if (dyeMarkersVisible && DyeTargetsRightRing && IsDyeTarget("Finger"))
        {
            Vector2 rrIconMin = rrMin + iconOffsetMin;
            DrawDyeTargetMarker(rrIconMin, rrIconMin + new Vector2(ringIconSize, ringIconSize), DyeMarkerSizeRing, dimmed: dimAccessoryMarkers);
        }

        ImGui.PopStyleColor(3);

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(dyeClickMode
                        ? "DYE TARGET mode - (the selection will not change)\nLeft-Click: dye ONLY this slot\nRight-Click: add/remove this slot from the dye targets" 
                        + FittingRoomSkipHint("Finger") + ClearSlotHint
                        : "Preview on Right Ring slot (RR)" + ClearSlotHint + "\nShift+: change to Dye Target mode");
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

        if (dyeClickMode && EquipBarClicked(ImGuiMouseButton.Left, anyPopupOpenInBar))
        {
            // Dye ONLY the left ring: clears every other dye target, Finger becomes the only one.
            BeginDyeTargetOverride();
            dyeTargetSlots.Clear();
            dyeTargetSlots.Add("Finger");
            dyeTargetLeftRing = true;
            dyeTargetRightRing = false;
            NormalizeDyeTargets();
        }
        else if (dyeClickMode && EquipBarClicked(ImGuiMouseButton.Right, anyPopupOpenInBar))
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
        else if (EquipBarClicked(ImGuiMouseButton.Left, anyPopupOpenInBar))
        {
            // Bal klikk: kizárólag a bal kézre vált
            plugin.Configuration.LeftRingActive = true;
            plugin.Configuration.RightRingActive = false;
            plugin.Configuration.Save();
        }
        else if (EquipBarClicked(ImGuiMouseButton.Right, anyPopupOpenInBar))
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

        // Ring dye markers only show when the Finger slot is actually a dye target (see RR above)
        if (dyeMarkersVisible && DyeTargetsLeftRing && IsDyeTarget("Finger"))
        {
            Vector2 lrIconMin = lrMin + iconOffsetMin;
            DrawDyeTargetMarker(lrIconMin, lrIconMin + new Vector2(ringIconSize, ringIconSize), DyeMarkerSizeRing, dimmed: dimAccessoryMarkers);
        }

        ImGui.PopStyleColor(3);
        ImGui.PopStyleVar(); // FramePadding

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(dyeClickMode
                       ? "DYE TARGET mode - (the selection will not change)\nLeft-Click: dye ONLY this slot\nRight-Click: add/remove this slot from the dye targets" 
                       + FittingRoomSkipHint("Finger") + ClearSlotHint
                       : "Preview on Left Ring slot (LR)\n" +
                       "Note: In-game 'Fitting Room' mode does not support Left Ring selection." +ClearSlotHint +"\nShift+: change to Dye Target mode");
        }

        ImGui.EndGroup(); // Ring buttons end

        // The slot section was clicked under an open Instant Hover Over Dye Preview: it follows the new dye targets
        if (dyeTargetsClickedUnderPicker && dyePreview != null) UpdateDyePreviewTargets(dyePreview);

        // --- DYNAMIC GAP #2: between icon bar / RR-LR group and dye sets ---
        DrawEquipBarSpacer(perGapWidth, continueOnSameLine: true);

        // 4. dye channels (MainWindow_DyeDraw.cs)
        DrawDyeSets(new EquipBarLayout(barStartY, barStartScreenY, barContentHeight, equipmentIconHeight,
            labelHeight, stackedIconRowHeight, stackedIconGroupHeight, anyPopupOpenInBar));

        // --- DYNAMIC GAP #3: after the dye sets ---
        DrawEquipBarSpacer(perGapWidth, continueOnSameLine: true);

        ImGui.EndGroup(); // Teljes Bar vége

        // Force the row's official height to exactly the equipment icons' height,
        // regardless of RR/LR or the dye squares poking out above/below it, PLUS a small manual
        // adjustment (BottomGapAdjustment) to balance the gap below against the gap above.
        ImGui.SetCursorPosY(barStartY + equipmentIconHeight + BottomGapAdjustment);
    }
}
