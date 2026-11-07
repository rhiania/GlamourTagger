using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using System;
using System.Linq;
using System.Numerics;

namespace GlamourTagger.Windows;

// Everything drawn for dyes: the dye sets on the equipment bar and the dye picker popup.
// Painting goes through MainWindow_Dyeing.cs, the Shift+Click preview through MainWindow_DyeHover.cs,
// the dye set menu (Right-click on a palette) through MainWindow_DyePresets.cs.
public partial class MainWindow
{
    // Swatch colour of Keep Current and None, on the bar and in the picker.
    // same grey as "None" - the Lock icon is what tells them apart now
    private static readonly Vector4 NoDyeSwatchColor = new(0.25f, 0.25f, 0.25f, 1.0f);
    private static readonly Vector4 NoDyeIconColor = new(0.75f, 0.75f, 0.75f, 0.9f);
    private const float NoDyeIconScale = 0.8f;
    private static readonly Vector2 KeepCurrentIconNudge = new(1f, 0f);   // + = right / down
    private static readonly Vector2 NoneIconNudge = new(1.5f, 0f);        // + = right / down


    // "Keep current" keeps the dye the slot shows: Glamourer's, or in Fitting Room mode the Fitting Room's
    private string KeepCurrentDyeName => applyMode == 1 || !plugin.GlamourerIpc.IsAvailable() ? "Keep current dye (as Fitting Room)" : "Keep current Glamourer dye";

    // Dye picker grid. Also used to work out the popup's width before it opens (FitDyePickerPosition)
    // Where the dye picker popups open: left edge = the set's Dye 1 square, top edge = the line under the
    // bar where the item table starts. Fine-tune here: + = right / down, - = left / up.
    private const float DyePickerPopupOffsetX = 0f;
    private const float DyePickerPopupOffsetY = 1f;

    private const float DyePickerSwatchSize = 24f;
    private const int DyePickerItemsPerRow = 12;
    private const float DyePickerSwatchGap = 3f;

    // Marker of the open picker on the bar: frame around its dye square + outline around its palette - keep one of the two colour lines
    //private static Vector4 OpenPickerMarkerColor => EquipBarGold;                       // Equipment Bar gold
    private static Vector4 OpenPickerMarkerColor => DyePreviewShownOutline.Color;     // red, the same as the shown colour's outline in the picker
    private const float OpenPickerFrameThickness = 1.5f;       // square frame, in pixels
    private const float OpenPickerPaletteOutlineOffset = 1f;   // palette outline; below 1 (e.g. 0.5) looks thinner and softer


    // Palette icon of a dye set. Opaque on purpose: the gold outline of an open picker must not show through it.
    private static readonly Vector4 PaletteColor = new(0.21f, 0.21f, 0.21f, 1f);
    private static readonly Vector4 PaletteHoveredColor = new(0.25f, 0.25f, 0.25f, 1f);


    // ---------------------------------------------------------------------------------------------
    // Dye sets on the equipment bar
    // ---------------------------------------------------------------------------------------------

    /// <summary>The "Dye sets:" label and the 3 dye sets, drawn into the equipment bar (see DrawEquipmentBar).</summary>
    private void DrawDyeSets(EquipBarLayout bar)
    {
        ImGui.BeginGroup();
        ImGui.SetCursorPosY(bar.StartY);

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
            ImGui.SetCursorPosY(bar.StartY + (bar.ContentHeight - bar.LabelHeight) / 2f);
            ImGui.PushStyleColor(ImGuiCol.Text, EquipBarGold); // match RR/LR's gold
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
        float middle = bar.StartY + (bar.ContentHeight - paletteIconSize) / 2f + paletteVerticalNudge;

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
            
            // Right-click: the dye set menu (MainWindow_DyePresets.cs)
            if (!bar.AnyPopupOpen && ImGui.IsItemClicked(ImGuiMouseButton.Right)) OpenDyeSetMenu();
            bool isHovered = ImGui.IsItemHovered();
            DrawDyeSetMenu(i);
            ImGui.PopID();

            var drawList = ImGui.GetWindowDrawList();
            var iconStr = FontAwesomeIcon.Palette.ToIconString();

            // One of its dye pickers is open: gold outline around the palette
            if (dyePickerOpen is { } openPicker && openPicker.SetIdx == i)
            {
                AddIconGlyphOutline(drawList, 23f, screenPos, ImGui.GetColorU32(OpenPickerMarkerColor), iconStr, OpenPickerPaletteOutlineOffset);
            }

            // Base Outer Palette (Dark Gray)
            Vector4 outerColor = isHovered ? PaletteHoveredColor : PaletteColor;
            //Vector4 outerColor = isHovered
            //    ? new Vector4(0.55f, 0.55f, 0.55f, 0.35f)
            //    : new Vector4(0.35f, 0.35f, 0.35f, 0.5f);

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
                ImGui.SetTooltip(tooltip + "\nRight-click: Dye Set Menu");
            }

            ImGui.EndGroup();

            ImGui.SameLine(0, 4f);

            // 2. Two Dye Squares (side by side, staggered: Dye 1 flush with the bar's top edge,
            // Dye 2 flush with the bar's bottom edge)
            ImGui.BeginGroup();
            ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, Vector2.Zero); // removes the default border/padding around the ColorButtons

            // Dye 1 - poking slightly ABOVE the bar's top edge on purpose (see DyeSquareOverflow).
            ImGui.SetCursorPosY(bar.StartY - DyeSquareOverflow);

            // Both dye pickers of this set open here, below the bar, so they never cover the set's own squares
            Vector2 dyePickerPopupPos = new(
                ImGui.GetCursorScreenPos().X + DyePickerPopupOffsetX,
                bar.StartScreenY + bar.IconHeight + BottomGapAdjustment + DyePickerPopupOffsetY);

            DrawDyeSetSquare(i, 1, dyeSquareSize, dyePickerPopupPos, bar.AnyPopupOpen);

            ImGui.SameLine(0, 1f); // squares almost touching
            // Dye 2 - poking slightly BELOW the bar's bottom edge on purpose (see DyeSquareOverflow).
            ImGui.SetCursorPosY(bar.StartY + bar.ContentHeight - dyeSquareDimension + DyeSquareOverflow);

            DrawDyeSetSquare(i, 2, dyeSquareSize, dyePickerPopupPos, bar.AnyPopupOpen);

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
            ImGui.SetCursorPosY(bar.StartY + (bar.ContentHeight - bar.StackedIconGroupHeight) / 2f);

            // Predict hover BEFORE drawing (Selectable bakes its text color into the same draw
            // call). SelectableTextAlign centers the glyph both horizontally and vertically inside
            // its own button box. Header/HeaderHovered/HeaderActive transparent = no background
            // highlight; the icon's OWN color brightens instead.
            Vector2 swapBtnScreenPos = ImGui.GetCursorScreenPos();
            bool swapWillHover = ImGui.IsMouseHoveringRect(swapBtnScreenPos, swapBtnScreenPos + new Vector2(stackedIconRowW, bar.StackedIconRowHeight));
            Vector4 swapIconColor = swapWillHover ? Vector4.Lerp(EquipBarGold, new Vector4(1f, 1f, 1f, 1f), 0.5f) : EquipBarGold;

            ImGui.PushStyleColor(ImGuiCol.Header, new Vector4(0, 0, 0, 0));
            ImGui.PushStyleColor(ImGuiCol.HeaderHovered, new Vector4(0, 0, 0, 0));
            ImGui.PushStyleColor(ImGuiCol.HeaderActive, new Vector4(0, 0, 0, 0));
            ImGui.PushStyleColor(ImGuiCol.Text, swapIconColor);
            ImGui.PushStyleVar(ImGuiStyleVar.SelectableTextAlign, new Vector2(-1f, 0.5f));
            ImGui.PushFont(UiBuilder.IconFont);
            ImGui.SetWindowFontScale(0.90f);
            bool swapClicked = ImGui.Selectable($"{FontAwesomeIcon.Retweet.ToIconString()}##SwapDyes_{i}", false, ImGuiSelectableFlags.None, new Vector2(stackedIconRowW, bar.StackedIconRowHeight));
            bool swapHovered = ImGui.IsItemHovered();
            ImGui.SetWindowFontScale(1.0f);
            ImGui.PopFont();
            ImGui.PopStyleVar();
            ImGui.PopStyleColor(4);

            // SetTooltip must run AFTER PopFont/scale-reset above, not while the icon font +
            // fractional scale are still active - otherwise the tooltip text renders in the icon font
            // (no matching glyphs) and comes out as garbage.
            if (swapClicked) SwapDyeSetChannels(i);
            if (swapHovered) ImGui.SetTooltip($"Swap Dye 1 and Dye 2 for Set {i + 1}");

            Vector2 applyBtnScreenPos = ImGui.GetCursorScreenPos();
            bool applyWillHover = ImGui.IsMouseHoveringRect(applyBtnScreenPos, applyBtnScreenPos + new Vector2(stackedIconRowW, bar.StackedIconRowHeight));
            Vector4 applyIconColor = applyWillHover ? Vector4.Lerp(EquipBarGold, new Vector4(1f, 1f, 1f, 1f), 0.5f) : EquipBarGold;

            ImGui.PushStyleColor(ImGuiCol.Header, new Vector4(0, 0, 0, 0));
            ImGui.PushStyleColor(ImGuiCol.HeaderHovered, new Vector4(0, 0, 0, 0));
            ImGui.PushStyleColor(ImGuiCol.HeaderActive, new Vector4(0, 0, 0, 0));
            ImGui.PushStyleColor(ImGuiCol.Text, applyIconColor);
            ImGui.PushStyleVar(ImGuiStyleVar.SelectableTextAlign, new Vector2(4f, 0.5f));
            ImGui.PushFont(UiBuilder.IconFont);
            ImGui.SetWindowFontScale(0.90f);
            bool applyClicked = ImGui.Selectable($"{FontAwesomeIcon.Brush.ToIconString()}##ApplyDyes_{i}", false, ImGuiSelectableFlags.None, new Vector2(stackedIconRowW, bar.StackedIconRowHeight));
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
                // The Fitting Room can't dye accessories: say so when one is a dye target
                string skipNote = AccessoryIsDyeTarget() && DyesGoToFittingRoom ? "\n" + FittingRoomSkipsAccessoriesNote : "";
                ImGui.SetTooltip($"Apply Dye Set {i + 1} to selected equipment slots{skipNote}");
            }

            ImGui.PopStyleVar(2);
            ImGui.EndGroup();
        }

        ImGui.EndGroup();
    }

    /// <summary>
    /// One dye square of a dye set (dyeChannel 1 or 2) at the cursor. Click and Right-click open its picker,
    /// with Shift as an Instant Hover Over Dye Preview; Middle-click toggles No Dye / Keep Current.
    /// Both squares of a set share popupPosition.
    /// </summary>
    private void DrawDyeSetSquare(int setIdx, int dyeChannel, Vector2 squareSize, Vector2 popupPosition, bool anyPopupOpenInBar)
    {
        var currentSet = plugin.Configuration.DyeSets[setIdx];
        byte dye = dyeChannel == 1 ? currentSet.Dye1 : currentSet.Dye2;

        var dyeInfo = DyeHelper.GetDye(Plugin.DataManager, dye);
        bool isKeepCurrent = dye == DyeHelper.DyeKeepCurrent;
        bool isNone = dye == 0;
        Vector4 squareColor = (isKeepCurrent || isNone)
            ? NoDyeSwatchColor // same grey as the popup's None/Keep Current swatches
            : (dyeInfo?.ColorVec ?? new Vector4(0.2f, 0.2f, 0.2f, 0.6f));

        ImGui.PushID($"Dye{dyeChannel}_Square_{setIdx}");
        Vector2 squarePos = ImGui.GetCursorScreenPos();
        bool clicked = ImGui.ColorButton($"##Dye{dyeChannel}_Btn_{setIdx}", squareColor, ImGuiColorEditFlags.NoTooltip, squareSize);
        bool rightClicked = !anyPopupOpenInBar && ImGui.IsItemClicked(ImGuiMouseButton.Right);
        if (clicked || rightClicked)
        {
            // Shift opens the Instant Hover Over Dye Preview (MainWindow_DyeHover.cs), Shift+Right-click with Dye Matching Colors ON
            bool shift = ImGui.GetIO().KeyShift;
            OpenDyePicker(setIdx, dyeChannel, withPreview: shift, withMatching: shift && rightClicked);
        }
        if (!anyPopupOpenInBar && ImGui.IsItemClicked(ImGuiMouseButton.Middle))
        {
            // Quick toggle, no popup needed: a dye or Keep Current (255) -> None (0), None -> Keep Current
            SetDyeSetChannel(setIdx, dyeChannel, isNone ? DyeHelper.DyeKeepCurrent : (byte)0);
        }
        if (isKeepCurrent || isNone)
        {
            DrawNoDyeIcon(squarePos, squareSize, isKeepCurrent);
        }
        else if (dyeInfo != null && DyeHelper.IsMetallic(dyeInfo.Id))
        {
            DrawMetallicOverlay(ImGui.GetWindowDrawList(), squarePos, squareSize);
        }
        // Its picker is open: gold frame around the square
        if (dyePickerOpen is { } openPicker && openPicker == (setIdx, dyeChannel))
        {
            // Same corner rounding as the dye square itself
            ImGui.GetWindowDrawList().AddRect(squarePos, squarePos + squareSize, ImGui.GetColorU32(OpenPickerMarkerColor), ImGui.GetStyle().FrameRounding, ImDrawFlags.None, OpenPickerFrameThickness);
        }
        if (ImGui.IsItemHovered())
        {
            int otherChannel = dyeChannel == 1 ? 2 : 1;
            string name = isKeepCurrent ? KeepCurrentDyeName : (dyeInfo != null ? dyeInfo.Name : "None (No Dye)");
            string matchingHint = DyeMatchingAvailable ? "\nShift+Right-click: the same, with Matching Colors Only ON" : "";
            ImGui.SetTooltip($"Set {setIdx + 1} - Dye {dyeChannel}: {name}\n" +
                             $"Click / Right-click: select dye (in the picker: Ctrl+Click sets Dye {otherChannel})\n" +
                             $"Shift+Click: Instant Hover Over Dye Preview{matchingHint}\n" +
                             "Middle-click: toggle No Dye / Keep Current");
        }
        DrawDyePickerPopup(setIdx, dyeChannel, popupPosition);
        ImGui.PopID();
    }

    // ---------------------------------------------------------------------------------------------
    // Dye picker popup
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// Renders the color selection popup grid for a dye channel. popupPosition = where the popup opens
    /// (top-left corner, screen coordinates).
    /// </summary>
    private void DrawDyePickerPopup(int setIdx, int dyeChannel, Vector2 popupPosition)
    {
        string popupId = DyePickerPopupId(setIdx, dyeChannel);
        // Opened with Shift+Click -> Instant Hover Over Dye Preview (MainWindow_DyeHover.cs); null = plain picker
        var preview = GetDyePreview(setIdx, dyeChannel);

        // Only used on the frame the popup opens; afterwards it stays where it is (or where it was dragged)
        ImGui.SetNextWindowPos(FitDyePickerPosition(popupPosition, preview), ImGuiCond.Appearing);
        ReopenDyePickerIfClosedByBar(setIdx, dyeChannel, popupId);

        if (BeginDyePickerPopup(popupId, preview))
        {
            dyePickerOpenNow = (setIdx, dyeChannel);
            dyePickerLastPos = ImGui.GetWindowPos();

            var set = plugin.Configuration.DyeSets[setIdx];
            byte currentDye = dyeChannel == 1 ? set.Dye1 : set.Dye2;
            byte otherChannelDye = dyeChannel == 1 ? set.Dye2 : set.Dye1;

            //splotch
            //string title = $"-- Select Dye {dyeChannel} (Set {setIdx + 1}) --";
            if (preview != null)
            {
                //BeginDyePreviewFrame(preview, title);
                BeginDyePreviewFrame(preview, currentDye);
            }
            else
            {
                //ImGui.TextDisabled(title);
                DrawDyePickerTitle(setIdx, dyeChannel, currentDye);
                ImGui.Separator();
            }
            ImGui.Spacing();

            // =========================================================================
            // SETTINGS / CONFIGURATION
            // =========================================================================
            Vector2 dyeSquareSize = new(DyePickerSwatchSize, DyePickerSwatchSize);
            const int itemsPerRow = DyePickerItemsPerRow;

            // one block per shade category
            var grouped = DyeHelper.GetGroupedDyes(Plugin.DataManager);
            var sortedCategories = grouped.OrderBy(kvp => kvp.Key);

            bool firstCategory = true;

            foreach (var (shade, dyes) in sortedCategories)
            {
                // Clear any lingering SameLine state from the previous row and start on a new line
                if (!firstCategory)
                {
                    ImGui.Spacing();
                }
                firstCategory = false;

                // Category title cleanly rendered on its own line
                ImGui.TextDisabled(DyeHelper.GetShadeName(shade));

                int count = 0;

                // 0. "Keep Current Dye" box, then 1. "None (No Dye)" box, both inserted at the start of Grayscale (shade == 2)
                if (shade == 2)
                {
                    // 0. Keep Current Dye
                    DrawNoDyeSwatch(setIdx, dyeChannel, DyeHelper.DyeKeepCurrent, currentDye, otherChannelDye, dyeSquareSize, preview);

                    count++;
                    if (count % itemsPerRow != 0)
                    {
                        ImGui.SameLine(0, DyePickerSwatchGap);
                    }

                    // 1. None (No Dye)
                    DrawNoDyeSwatch(setIdx, dyeChannel, 0, currentDye, otherChannelDye, dyeSquareSize, preview);

                    count++;
                    if (count % itemsPerRow != 0)
                    {
                        ImGui.SameLine(0, DyePickerSwatchGap);
                    }
                }

                // 2. Render dye items for current category
                foreach (var dye in dyes)
                {
                    // Apply SameLine BEFORE drawing items (skipping the first item in each line)
                    if (count > 0 && count % itemsPerRow != 0)
                    {
                        ImGui.SameLine(0, DyePickerSwatchGap);
                    }

                    ImGui.PushID($"dye_pick_{dye.Id}");

                    bool dyeFramed = PushSavedDyeFrame(dye.Id, currentDye, otherChannelDye);

                    Vector2 squarePos = ImGui.GetCursorScreenPos();

                    if (ImGui.ColorButton($"##dye_btn_{dye.Id}", dye.ColorVec, ImGuiColorEditFlags.NoTooltip, dyeSquareSize)
                        && !DyePickerConsumesLeftClick(setIdx, dyeChannel, dye.Id, preview))
                    {
                        SetDyeSetChannel(setIdx, dyeChannel, dye.Id, fromPicker: true);
                        Plugin.Log.Debug($"[GlamourTagger Debug] Dye selected in picker -> Set: {setIdx + 1} | Channel: {dyeChannel} | DyeID: {dye.Id}");
                        ImGui.CloseCurrentPopup();
                    }

                    // Render metallic overlay strictly for whitelisted metallic IDs
                    if (DyeHelper.IsMetallic(dye.Id))
                    {
                        DrawMetallicOverlay(ImGui.GetWindowDrawList(), squarePos, dyeSquareSize);
                    }

                    PopSavedDyeFrame(dyeFramed);

                    if (preview != null) UpdateDyePreviewSwatch(preview, dye.Id, squarePos, dyeSquareSize);
                    if (ImGui.IsItemHovered())
                    {
                        ImGui.SetTooltip($"{dye.Name} (ID: {dye.Id})");
                    }

                    ImGui.PopID();

                    count++;
                }
            }

            if (preview != null) EndDyePreviewFrame(preview, currentDye);
            ImGui.EndPopup();
        }
    }


    // ---------------------------------------------------------------------------------------------
    // Dye picker over the equipment bar
    // ---------------------------------------------------------------------------------------------

    // While a dye picker is open, the slot section of the equipment bar keeps working (dye targets and filter).
    // ImGui closes a popup on a click outside it, so after such a click the picker is opened again on the next
    // frame, where it was, with the same session. Every other click outside closes it as always.
    private (int SetIdx, int Channel)? dyePickerOpen;              // the picker that was open on the last frame
    private (int SetIdx, int Channel)? dyePickerOpenNow;           // the picker open on this frame, set while it is drawn
    private (int SetIdx, int Channel)? reopenDyePicker;            // to reopen on this frame
    private (int SetIdx, int Channel)? reopenDyePickerRequested;   // a click on the slot section closed it this frame
    private Vector2 dyePickerLastPos;
    private bool dyeTargetsClickedUnderPicker;                     // the slot section was clicked this frame

    /// <summary>True while a dye picker is open: the table waits with refiltering until it is closed.</summary>
    private bool IsDyePickerOpen => dyePickerOpen != null || dyePickerOpenNow != null;

    private static string DyePickerPopupId(int setIdx, int dyeChannel) => $"DyePickerPopup_{setIdx}_{dyeChannel}";

    /// <summary>Opens the dye picker of a dye square. Call it inside the square's ID scope (see DrawDyeSetSquare).</summary>
    private void OpenDyePicker(int setIdx, int dyeChannel, bool withPreview, bool withMatching)
    {
        BeginDyePickerSession(setIdx, dyeChannel, withPreview, withMatching);
        dyePickerUndoRecorded = false;
        reopenDyePicker = null;
        reopenDyePickerRequested = null;
        ImGui.OpenPopup(DyePickerPopupId(setIdx, dyeChannel));
    }

    /// <summary>Start of the equipment bar: the picker open on the last frame decides how the slot section takes clicks.</summary>
    private void BeginEquipBarFrame()
    {
        dyePickerOpen = dyePickerOpenNow;
        dyePickerOpenNow = null;
        reopenDyePicker = reopenDyePickerRequested;
        reopenDyePickerRequested = null;
        dyeTargetsClickedUnderPicker = false;
    }

    /// <summary>
    /// A click on the last item of the bar's slot section: ImGui's own click, or - while a dye picker is open and
    /// blocks that - the click read directly (DyePickerBarClicked).
    /// </summary>
    private bool EquipBarClicked(ImGuiMouseButton button, bool anyPopupOpen) =>
        dyePickerOpen != null ? DyePickerBarClicked(button) : !anyPopupOpen && ImGui.IsItemClicked(button);

    /// <summary>A click on the last item while a dye picker is open; the picker is reopened on the next frame.</summary>
    private bool DyePickerBarClicked(ImGuiMouseButton button)
    {
        if (dyePickerOpen == null || !ImGui.IsMouseClicked(button) || !ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenBlockedByPopup)) return false;

        reopenDyePickerRequested = dyePickerOpen;
        dyeTargetsClickedUnderPicker = true;
        return true;
    }

    /// <summary>Right before the picker's BeginPopup: reopens it where it was, if a click on the slot section closed it.</summary>
    private void ReopenDyePickerIfClosedByBar(int setIdx, int dyeChannel, string popupId)
    {
        if (reopenDyePicker != (setIdx, dyeChannel)) return;
        reopenDyePicker = null;

        if (!ImGui.IsPopupOpen(popupId)) ImGui.OpenPopup(popupId);
        ImGui.SetNextWindowPos(dyePickerLastPos, ImGuiCond.Always);
    }

    /// <summary>
    /// Where a dye picker opens: under its dye set, moved left so it stays inside the Glamour Tagger window and the
    /// screen. The width is worked out from the grid, as an auto-sized popup has no size before its first frame.
    /// </summary>
    private static Vector2 FitDyePickerPosition(Vector2 position, DyePreviewSession? preview)
    {
        float width = DyePickerItemsPerRow * DyePickerSwatchSize + (DyePickerItemsPerRow - 1) * DyePickerSwatchGap
                    + 2f * ImGui.GetStyle().WindowPadding.X;
        if (preview != null) width += DyePreviewLabelStripWidth;
        float bannerWidth = preview is { MatchingOn: true } ? DyeMatchingBannerWidth : 0f;

        var viewport = ImGui.GetMainViewport();
        float screenLeft = viewport.WorkPos.X;
        float screenRight = screenLeft + viewport.WorkSize.X;
        float windowRight = ImGui.GetWindowPos().X + ImGui.GetWindowSize().X;

        float x = MathF.Min(position.X, windowRight - width);
        x = MathF.Min(x, screenRight - width - bannerWidth);
        return new Vector2(MathF.Max(screenLeft, x), position.Y);
    }

    /// <summary>The Keep Current (dyeId 255) or None (dyeId 0) swatch at the start of Grayscale in the dye picker.</summary>
    private void DrawNoDyeSwatch(int setIdx, int dyeChannel, byte dyeId, byte currentDye, byte otherChannelDye, Vector2 squareSize, DyePreviewSession? preview)
    {
        bool keepCurrent = dyeId == DyeHelper.DyeKeepCurrent;
        string idSuffix = keepCurrent ? "keep" : "0";

        ImGui.PushID($"dye_pick_{idSuffix}");

        bool framed = PushSavedDyeFrame(dyeId, currentDye, otherChannelDye);

        Vector2 btnPos = ImGui.GetCursorScreenPos();

        if (ImGui.ColorButton($"##dye_btn_{idSuffix}", NoDyeSwatchColor, ImGuiColorEditFlags.NoTooltip, squareSize)
            && !DyePickerConsumesLeftClick(setIdx, dyeChannel, dyeId, preview))
        {
            SetDyeSetChannel(setIdx, dyeChannel, dyeId, fromPicker: true);
            ImGui.CloseCurrentPopup();
        }

        DrawNoDyeIcon(btnPos, squareSize, keepCurrent);

        PopSavedDyeFrame(framed);

        if (preview != null) UpdateDyePreviewSwatch(preview, dyeId, btnPos, squareSize);
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(keepCurrent ? KeepCurrentDyeName + " (channel unchanged)" : "None (No Dye)");
        }

        ImGui.PopID();
    }

    /// <summary>
    /// Tint icon (Keep Current) or TintSlash icon (None), centred on a swatch. Shared by the bar, the picker and the
    /// dye set menu (smaller scale there, and dimmed on greyed-out menu items).
    /// </summary>
    private static void DrawNoDyeIcon(Vector2 squarePos, Vector2 squareSize, bool keepCurrent, float scale = NoDyeIconScale, float alpha = 1f)
    {
        // Lock icon centered on Keep Current box
        // Ban icon centered on None box
        string icon = keepCurrent ? FontAwesomeIcon.Tint.ToIconString() : FontAwesomeIcon.TintSlash.ToIconString();
        // The nudges are tuned at the normal scale: a smaller icon gets a proportionally smaller nudge
        Vector2 nudge = (keepCurrent ? KeepCurrentIconNudge : NoneIconNudge) * (scale / NoDyeIconScale);
        Vector4 color = NoDyeIconColor;
        color.W *= alpha;

        var drawList = ImGui.GetWindowDrawList();
        ImGui.PushFont(UiBuilder.IconFont);
        ImGui.SetWindowFontScale(scale);
        Vector2 iconSize = ImGui.CalcTextSize(icon);
        Vector2 iconPos = squarePos + (squareSize - iconSize) * 0.5f + nudge;
        drawList.AddText(UiBuilder.IconFont, ImGui.GetFontSize(), iconPos, ImGui.GetColorU32(color), icon);
        ImGui.PopFont();
        ImGui.SetWindowFontScale(1f);
    }

    // Swatch frames in the dye picker: gold = the dye saved in the channel being edited,
    // blue = the dye saved in the set's other channel.
    private static readonly Vector4 SavedDyeFrameColor = new(1f, 0.85f, 0.1f, 1f);
    private static readonly Vector4 OtherChannelSavedDyeFrameColor = new(0.35f, 0.60f, 1f, 1f);
    private const float SavedDyeFrameThickness = 2f;

    /// <summary>
    /// Left-click on a swatch, in the plain picker and in the Instant Hover Over Dye Preview alike.
    /// Ctrl + Left-click saves the colour into the set's OTHER channel and keeps the popup open; returns true,
    /// and the caller then neither saves into the edited channel nor closes the popup.
    /// A plain Left-click returns false: the caller saves the colour and closes the popup as always.
    /// </summary>
    private bool DyePickerConsumesLeftClick(int setIdx, int dyeChannel, byte dyeId, DyePreviewSession? preview)
    {
        if (ImGui.GetIO().KeyCtrl)
        {
            SetDyeSetChannel(setIdx, dyeChannel == 1 ? 2 : 1, dyeId, fromPicker: true); // the set's OTHER dye

            if (preview != null) OnDyePreviewOtherChannelSaved(preview);
            return true;
        }

        if (preview != null) OnDyePreviewPicked(preview, dyeId);
        return false;
    }

    /// <summary>
    /// Pushes the frame of one swatch in the dye picker (gold wins when both channels hold the same dye).
    /// Returns true when a frame was pushed - pass that to PopSavedDyeFrame right after the swatch.
    /// </summary>
    private static bool PushSavedDyeFrame(byte swatchDye, byte editedChannelDye, byte otherChannelDye)
    {
        Vector4? color = swatchDye == editedChannelDye ? SavedDyeFrameColor
                       : swatchDye == otherChannelDye ? OtherChannelSavedDyeFrameColor
                       : null;
        if (color == null) return false;

        ImGui.PushStyleColor(ImGuiCol.Border, color.Value);
        ImGui.PushStyleVar(ImGuiStyleVar.FrameBorderSize, SavedDyeFrameThickness);
        return true;
    }

    private static void PopSavedDyeFrame(bool pushed)
    {
        if (!pushed) return;
        ImGui.PopStyleVar();
        ImGui.PopStyleColor();
    }

    //splotch
    // One splotch = Dye 1 / one dye channel, two = Dye 2 / two channels. Shared by the Dyes column and the picker title.
    private static readonly string DyeChannelGlyph = FontAwesomeIcon.Splotch.ToIconString();
    private static readonly string DyeChannelGlyphDouble = DyeChannelGlyph + DyeChannelGlyph;
    private static readonly Vector4 DyeChannelGlyphColor = new(1f, 1f, 1f, 0.85f);
    private const float DyeChannelGlyphScale = 0.6f;  // relative to the text size
    private const float DyeChannelGlyphGap = 5f;      // space before / after the glyphs in the title
    private const float DyeChannelGlyphNudgeY = 1f;   // + = down, - = up


    // Title splotches in the saved dye's color. Dark dyes get a gold outline, Keep Current / No Dye are grey with
    // a white outline. Metallic dyes - keep one of the two lines:
    private static readonly MetallicGlyphLook MetallicGlyphStyle = MetallicGlyphLook.Highlight;          // small white shine on each splotch
    //private static readonly MetallicGlyphLook MetallicGlyphStyle = MetallicGlyphLook.TwoToneOutline;   // light outline top-left, dark bottom-right
    private const float DarkDyeLuminance = 0.22f;        // below this a dye counts as dark (0 = black, 1 = white)
    private static readonly Vector4 NoDyeGlyphOutlineColor = new(1f, 1f, 1f, 0.8f);
    private const float MetallicHighlightScale = 0.45f;  // shine size, relative to the splotch
    private static readonly Vector4 MetallicHighlightColor = new(1f, 1f, 1f, 0.55f);
    private static readonly Vector4 MetallicOutlineLightColor = new(1f, 1f, 1f, 0.9f);
    private static readonly Vector4 MetallicOutlineDarkColor = new(0.35f, 0.35f, 0.35f, 0.9f);

    private enum MetallicGlyphLook { Highlight, TwoToneOutline }


    /// <summary>
    /// Title of a dye picker popup: "-- Select Dye N [channel glyphs] (Set N) --", in the Equipment Bar gold,
    /// the glyphs in the saved dye's color.
    /// </summary>
    private static void DrawDyePickerTitle(int setIdx, int dyeChannel, byte savedDye)
    {
        ImGui.TextColored(PopupTitleColor, $"-- Select Dye {dyeChannel}");
        ImGui.SameLine(0, DyeChannelGlyphGap);
        DrawDyeChannelGlyphs(dyeChannel, savedDye);
        ImGui.SameLine(0, DyeChannelGlyphGap);
        ImGui.TextColored(PopupTitleColor, $"(Set {setIdx + 1}) --");
    }

    /// <summary>Draws the glyphs of a dye channel (1 or 2) at the cursor, centred on the text line, in the color of dyeId.</summary>
    private static void DrawDyeChannelGlyphs(int dyeChannel, byte dyeId)
    {
        string glyphs = dyeChannel == 2 ? DyeChannelGlyphDouble : DyeChannelGlyph;
        float glyphSize = ImGui.GetFontSize() * DyeChannelGlyphScale;
        Vector2 size = MeasureIconGlyph(glyphs, glyphSize);
        float lineHeight = ImGui.GetTextLineHeight();

        Vector2 position = ImGui.GetCursorScreenPos();
        ImGui.Dummy(new Vector2(size.X, lineHeight));

        Vector2 glyphPosition = new(position.X, position.Y + (lineHeight - size.Y) * 0.5f + DyeChannelGlyphNudgeY);
        var drawList = ImGui.GetWindowDrawList();

        var dye = dyeId is 0 or DyeHelper.DyeKeepCurrent ? null : DyeHelper.GetDye(Plugin.DataManager, dyeId);
        if (dye == null)
        {
            // Keep Current / No Dye
            AddIconGlyphOutline(drawList, glyphSize, glyphPosition, ImGui.GetColorU32(NoDyeGlyphOutlineColor), glyphs);
            drawList.AddText(UiBuilder.IconFont, glyphSize, glyphPosition, ImGui.GetColorU32(ImGuiCol.TextDisabled), glyphs);
            return;
        }

        bool metallic = DyeHelper.IsMetallic(dye.Id);
        if (metallic && MetallicGlyphStyle == MetallicGlyphLook.TwoToneOutline)
        {
            AddTwoToneGlyphOutline(drawList, glyphSize, glyphPosition, glyphs);
        }
        else if (IsDarkDye(dye.ColorVec))
        {
            AddIconGlyphOutline(drawList, glyphSize, glyphPosition, ImGui.GetColorU32(EquipBarGold), glyphs);
        }

        drawList.AddText(UiBuilder.IconFont, glyphSize, glyphPosition, ImGui.GetColorU32(dye.ColorVec), glyphs);

        if (metallic && MetallicGlyphStyle == MetallicGlyphLook.Highlight)
        {
            AddMetallicGlyphHighlights(drawList, glyphSize, glyphPosition, dyeChannel == 2 ? 2 : 1);
        }
    }

    private static bool IsDarkDye(Vector4 color) => 0.2126f * color.X + 0.7152f * color.Y + 0.0722f * color.Z < DarkDyeLuminance;

    /// <summary>A 1px outline around an icon glyph: the glyph drawn 8 times around its position, before the glyph itself.</summary>
    private static void AddIconGlyphOutline(ImDrawListPtr drawList, float size, Vector2 position, uint color, string glyph, float offset = 1f)
    {
        for (int dx = -1; dx <= 1; dx++)
        {
            for (int dy = -1; dy <= 1; dy++)
            {
                if (dx != 0 || dy != 0) drawList.AddText(UiBuilder.IconFont, size, position + new Vector2(dx, dy) * offset, color, glyph);
            }
        }
    }

    /// <summary>Metallic look: light outline towards the top-left, dark towards the bottom-right.</summary>
    private static void AddTwoToneGlyphOutline(ImDrawListPtr drawList, float size, Vector2 position, string glyph)
    {
        uint light = ImGui.GetColorU32(MetallicOutlineLightColor);
        uint dark = ImGui.GetColorU32(MetallicOutlineDarkColor);
        drawList.AddText(UiBuilder.IconFont, size, position + new Vector2(1f, 1f), dark, glyph);
        drawList.AddText(UiBuilder.IconFont, size, position + new Vector2(1f, 0f), dark, glyph);
        drawList.AddText(UiBuilder.IconFont, size, position + new Vector2(0f, 1f), dark, glyph);
        drawList.AddText(UiBuilder.IconFont, size, position + new Vector2(-1f, -1f), light, glyph);
        drawList.AddText(UiBuilder.IconFont, size, position + new Vector2(-1f, 0f), light, glyph);
        drawList.AddText(UiBuilder.IconFont, size, position + new Vector2(0f, -1f), light, glyph);
    }

    /// <summary>Metallic look: a smaller, translucent white splotch in the middle of each splotch.</summary>
    private static void AddMetallicGlyphHighlights(ImDrawListPtr drawList, float size, Vector2 position, int count)
    {
        float highlightSize = size * MetallicHighlightScale;
        Vector2 glyphSize = MeasureIconGlyph(DyeChannelGlyph, size);
        Vector2 highlightGlyphSize = MeasureIconGlyph(DyeChannelGlyph, highlightSize);
        uint color = ImGui.GetColorU32(MetallicHighlightColor);

        for (int i = 0; i < count; i++)
        {
            Vector2 center = position + new Vector2(glyphSize.X * (i + 0.5f), glyphSize.Y * 0.5f);
            drawList.AddText(UiBuilder.IconFont, highlightSize, center - highlightGlyphSize * 0.5f, color, DyeChannelGlyph);
        }
    }

    /// <summary>
    /// Draws the metallic-dye sheen overlay onto a dye square. All offsets and thicknesses are
    /// expressed as fractions of squareSize so the same overlay scales correctly whether it's
    /// drawn on the 24x24 dye picker swatch or the larger, frameHeight-sized equipment bar square.
    /// </summary>
    private static void DrawMetallicOverlay(ImDrawListPtr dl, Vector2 squarePos, Vector2 squareSize)
    {
        // Baseline tuning was done at 24px; express thickness/offset literals as fractions of that.
        float k(float pixelsAt24) => squareSize.X * (pixelsAt24 / 24f);

        // 1. Soft blur glow behind the sheen lines
        Vector2 glowStart = squarePos + new Vector2(k(3f), squareSize.Y * 0.80f);
        Vector2 glowEnd = squarePos + new Vector2(squareSize.X * 0.80f, k(3f));
        dl.AddLine(glowStart, glowEnd, ImGui.GetColorU32(new Vector4(1.0f, 1.0f, 1.0f, 0.18f)), k(9.0f));
        Vector2 glow2Start = squarePos + new Vector2(k(2f), squareSize.Y * 0.850f);
        Vector2 glow2End = squarePos + new Vector2(squareSize.X * 0.85f, k(2f));
        dl.AddLine(glow2Start, glow2End, ImGui.GetColorU32(new Vector4(1.0f, 1.0f, 1.0f, 0.18f)), k(6.0f));
        Vector2 glow3Start = squarePos + new Vector2(k(1f), squareSize.Y * 0.90f);
        Vector2 glow3End = squarePos + new Vector2(squareSize.X * 0.90f, k(1f));
        dl.AddLine(glow3Start, glow3End, ImGui.GetColorU32(new Vector4(1.0f, 1.0f, 1.0f, 0.18f)), k(3.0f));

        Vector2 darkStart = squarePos + new Vector2(squareSize.X + k(7f), squareSize.Y * 0.45f);
        Vector2 darkEnd = squarePos + new Vector2(squareSize.X * 0.45f, squareSize.Y + k(7f));
        dl.AddLine(darkStart, darkEnd, ImGui.GetColorU32(new Vector4(0.07f, 0.07f, 0.07f, 0.15f)), k(10.0f));
        Vector2 dark2Start = squarePos + new Vector2(squareSize.X, squareSize.Y * 0.60f);
        Vector2 dark2End = squarePos + new Vector2(squareSize.X * 0.60f, squareSize.Y);
        dl.AddLine(dark2Start, dark2End, ImGui.GetColorU32(new Vector4(0.07f, 0.07f, 0.07f, 0.20f)), k(7.0f));
        Vector2 dark3Start = squarePos + new Vector2(squareSize.X, squareSize.Y * 0.75f);
        Vector2 dark3End = squarePos + new Vector2(squareSize.X * 0.75f, squareSize.Y);
        dl.AddLine(dark3Start, dark3End, ImGui.GetColorU32(new Vector4(0.07f, 0.07f, 0.07f, 0.3f)), k(5.0f));

        // 2. Sharp sheen rail lines on top of the glow
        Vector2 l1Start = squarePos + new Vector2(k(1f), squareSize.Y * 0.65f);
        Vector2 l1End = squarePos + new Vector2(squareSize.X * 0.65f, k(1f));
        Vector2 l2Start = squarePos + new Vector2(k(2f), squareSize.Y * 0.90f);
        Vector2 l2End = squarePos + new Vector2(squareSize.X * 0.90f, k(2f));

        dl.AddLine(l1Start, l1End, ImGui.GetColorU32(new Vector4(1.0f, 1.0f, 1.0f, 0.30f)), k(1.5f));
        dl.AddLine(l2Start, l2End, ImGui.GetColorU32(new Vector4(1.0f, 1.0f, 1.0f, 0.40f)), k(2.0f));
    }
}