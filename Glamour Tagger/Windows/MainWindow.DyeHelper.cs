using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using FFXIVClientStructs.FFXIV.Client.Game;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace GlamourTagger.Windows;

public partial class MainWindow
{
    /// <summary>
    /// Renders the color selection popup grid for a dye channel. popupPosition = where the popup opens
    /// (top-left corner, screen coordinates).
    /// </summary>
    private void DrawDyePickerPopup(int setIdx, int dyeChannel, Vector2 popupPosition)
    {
        // Only used on the frame the popup opens; afterwards it stays where it is
        ImGui.SetNextWindowPos(popupPosition, ImGuiCond.Appearing);
        // Opened with Shift+Click -> Instant Hover Over Dye Preview (MainWindow_DyePreview.cs); null = plain picker
        var preview = GetDyePreview(setIdx, dyeChannel);

        if (BeginDyePickerPopup($"DyePickerPopup_{setIdx}_{dyeChannel}", preview))
        {
            var set = plugin.Configuration.DyeSets[setIdx];
            byte currentDye = dyeChannel == 1 ? set.Dye1 : set.Dye2;
            byte otherChannelDye = dyeChannel == 1 ? set.Dye2 : set.Dye1;

            //splotch
            //string title = $"-- Select Dye {dyeChannel} (Set {setIdx + 1}) --";
            if (preview != null)
            {
                //BeginDyePreviewFrame(preview, title);
                BeginDyePreviewFrame(preview);
            }
            else
            {
                //ImGui.TextDisabled(title);
                DrawDyePickerTitle(setIdx, dyeChannel);
                ImGui.Separator();
            }
            ImGui.Spacing();

            // =========================================================================
            // SETTINGS / CONFIGURATION
            // =========================================================================
            Vector2 dyeSquareSize = new Vector2(24f, 24f);
            const int itemsPerRow = 12;

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
                    const float tintSlashNudgeX = 1.5f;
                    const float tintSlashNudgeY = 0f;
                    const float tintNudgeX = 1f;
                    const float tintNudgeY = 0f;

                    // 0. Keep Current Dye
                    ImGui.PushID("dye_pick_keep");

                    bool keepFramed = PushSavedDyeFrame(DyeHelper.DyeKeepCurrent, currentDye, otherChannelDye);

                    Vector2 keepBtnPos = ImGui.GetCursorScreenPos();
                    Vector4 keepBgColor = new Vector4(0.25f, 0.25f, 0.25f, 1.0f); // same grey as "None" - the Lock icon is what tells them apart now

                    if (ImGui.ColorButton("##dye_btn_keep", keepBgColor, ImGuiColorEditFlags.NoTooltip, dyeSquareSize)
                        && !DyePickerConsumesLeftClick(setIdx, dyeChannel, DyeHelper.DyeKeepCurrent, preview))
                    {
                        if (dyeChannel == 1) set.Dye1 = DyeHelper.DyeKeepCurrent; else set.Dye2 = DyeHelper.DyeKeepCurrent;
                        plugin.Configuration.Save();
                        ImGui.CloseCurrentPopup();
                    }

                    // Lock icon centered on Keep Current box
                    var keepDrawList = ImGui.GetWindowDrawList();
                    ImGui.PushFont(UiBuilder.IconFont);
                    ImGui.SetWindowFontScale(0.8f);
                    string lockIcon = FontAwesomeIcon.Tint.ToIconString();
                    Vector2 lockTextSize = ImGui.CalcTextSize(lockIcon);
                    Vector2 lockTextPos = keepBtnPos + (dyeSquareSize - lockTextSize) * 0.5f + new Vector2(tintNudgeX, tintNudgeY);
                    keepDrawList.AddText(UiBuilder.IconFont, ImGui.GetFontSize(), lockTextPos, ImGui.GetColorU32(new Vector4(0.75f, 0.75f, 0.75f, 0.9f)), lockIcon);
                    ImGui.PopFont();
                    ImGui.SetWindowFontScale(1f);

                    PopSavedDyeFrame(keepFramed);

                    if (preview != null) UpdateDyePreviewSwatch(preview, DyeHelper.DyeKeepCurrent, keepBtnPos, dyeSquareSize);
                    if (ImGui.IsItemHovered())
                    {
                        ImGui.SetTooltip("Keep current Glamourer dye (channel unchanged)");
                    }

                    ImGui.PopID();

                    count++;
                    if (count % itemsPerRow != 0)
                    {
                        ImGui.SameLine(0, 3f);
                    }

                    // 1. None (No Dye)
                    ImGui.PushID("dye_pick_0");

                    bool noneFramed = PushSavedDyeFrame(0, currentDye, otherChannelDye);

                    Vector2 btnPos = ImGui.GetCursorScreenPos();
                    Vector4 noneBgColor = new Vector4(0.25f, 0.25f, 0.25f, 1.0f);

                    if (ImGui.ColorButton("##dye_btn_0", noneBgColor, ImGuiColorEditFlags.NoTooltip, dyeSquareSize)
                        && !DyePickerConsumesLeftClick(setIdx, dyeChannel, 0, preview))
                    {
                        if (dyeChannel == 1) set.Dye1 = 0; else set.Dye2 = 0;
                        plugin.Configuration.Save();
                        ImGui.CloseCurrentPopup();
                    }

                    // Ban icon centered on None box
                    var drawList = ImGui.GetWindowDrawList();
                    ImGui.PushFont(UiBuilder.IconFont);
                    ImGui.SetWindowFontScale(0.8f);
                    string banIcon = FontAwesomeIcon.TintSlash.ToIconString();
                    Vector2 textSize = ImGui.CalcTextSize(banIcon);
                    Vector2 textPos = btnPos + (dyeSquareSize - textSize) * 0.5f + new Vector2(tintSlashNudgeX, tintSlashNudgeY);
                    drawList.AddText(UiBuilder.IconFont, ImGui.GetFontSize(), textPos, ImGui.GetColorU32(new Vector4(0.75f, 0.75f, 0.75f, 0.9f)), banIcon);
                    ImGui.PopFont();
                    ImGui.SetWindowFontScale(1f);

                    PopSavedDyeFrame(noneFramed);

                    if (preview != null) UpdateDyePreviewSwatch(preview, 0, btnPos, dyeSquareSize);
                    if (ImGui.IsItemHovered())
                    {
                        ImGui.SetTooltip("None (No Dye)");
                    }

                    ImGui.PopID();

                    count++;
                    if (count % itemsPerRow != 0)
                    {
                        ImGui.SameLine(0, 3f);
                    }
                }

                // 2. Render dye items for current category
                foreach (var dye in dyes)
                {
                    // Apply SameLine BEFORE drawing items (skipping the first item in each line)
                    if (count > 0 && count % itemsPerRow != 0)
                    {
                        ImGui.SameLine(0, 3f);
                    }

                    ImGui.PushID($"dye_pick_{dye.Id}");

                    bool dyeFramed = PushSavedDyeFrame(dye.Id, currentDye, otherChannelDye);

                    Vector2 squarePos = ImGui.GetCursorScreenPos();

                    if (ImGui.ColorButton($"##dye_btn_{dye.Id}", dye.ColorVec, ImGuiColorEditFlags.NoTooltip, dyeSquareSize)
                        && !DyePickerConsumesLeftClick(setIdx, dyeChannel, dye.Id, preview))
                    {
                        if (dyeChannel == 1) set.Dye1 = dye.Id; else set.Dye2 = dye.Id;
                        plugin.Configuration.Save();
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
            var set = plugin.Configuration.DyeSets[setIdx];
            if (dyeChannel == 1) set.Dye2 = dyeId; else set.Dye1 = dyeId;
            plugin.Configuration.Save();

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

    /// <summary>Title of a dye picker popup: "-- Select Dye [channel glyphs] (Set N) --".</summary>
    private static void DrawDyePickerTitle(int setIdx, int dyeChannel)
    {
        ImGui.TextDisabled("-- Select Dye");
        ImGui.SameLine(0, DyeChannelGlyphGap);
        DrawDyeChannelGlyphs(dyeChannel);
        ImGui.SameLine(0, DyeChannelGlyphGap);
        ImGui.TextDisabled($"(Set {setIdx + 1}) --");
    }

    /// <summary>Draws the glyphs of a dye channel (1 or 2) at the cursor, centred on the text line.</summary>
    private static void DrawDyeChannelGlyphs(int dyeChannel)
    {
        string glyphs = dyeChannel == 2 ? DyeChannelGlyphDouble : DyeChannelGlyph;
        float glyphSize = ImGui.GetFontSize() * DyeChannelGlyphScale;
        Vector2 size = MeasureIconGlyph(glyphs, glyphSize);
        float lineHeight = ImGui.GetTextLineHeight();

        Vector2 position = ImGui.GetCursorScreenPos();
        ImGui.Dummy(new Vector2(size.X, lineHeight));

        Vector2 glyphPosition = new(position.X, position.Y + (lineHeight - size.Y) * 0.5f + DyeChannelGlyphNudgeY);
        ImGui.GetWindowDrawList().AddText(UiBuilder.IconFont, glyphSize, glyphPosition, ImGui.GetColorU32(ImGuiCol.TextDisabled), glyphs);
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
    /// Shared by the brush button and the Instant Hover Over Dye Preview (MainWindow_DyePreview.cs).
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
    /// </summary>
    private IEnumerable<string> GetDyeTargetsInApplyOrder()
    {
        string? lastSlot = applyMode == 1 ? plugin.LastFittingRoomTryOnSlot : null;

        foreach (var slotName in EffectiveDyeSlots)
        {
            if (!string.Equals(slotName, lastSlot, StringComparison.OrdinalIgnoreCase)) yield return slotName;
        }

        if (lastSlot != null && EffectiveDyeSlots.Contains(lastSlot, StringComparer.OrdinalIgnoreCase))
        {
            yield return lastSlot;
        }
    }


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
        // Fitting Room mode: pass the "keep current" sentinel through untouched -
        // Plugin.TryOnItem resolves it (Glamourer's current stain if available, otherwise no dye).
        if (applyMode == 1)
        {
            // The Instant Hover Over Dye Preview resolves the sentinel itself, from one cached Glamourer
            // read per popup - TryOnItem would read the Glamourer state for every slot on every hover.
            return dyePreviewApplying != null
                ? ResolveDyePreviewKeepCurrent(dyePreviewApplying, jsonSlot, dye1, dye2)
                : (dye1, dye2);
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
                plugin.ApplyItemWithGlamourer(targetItemId, glamourerSlot, dye1, dye2, exactSlot: true);
            }
        }
    }

    private unsafe uint GetActiveSlotItemId(string jsonSlotName, InventoryContainer* container, int equippedSlotIdx)
    {
        // 1. Mode 0 (Glamourer Preview): Check active item previewed in Glamourer state
        if (applyMode == 0 && plugin.GlamourerIpc.IsAvailable())
        {
            uint glamourerItemId = plugin.GlamourerIpc.GetCurrentSlotItemId(jsonSlotName);
            if (glamourerItemId != 0) return glamourerItemId;
        }

        // 2. Mode 1 (Fitting Room): Check tracked items for Fitting Room mode
        if (applyMode == 1)
        {
            if (plugin.FittingRoomItems.TryGetValue(jsonSlotName, out uint trackedItemId) && trackedItemId != 0)
            {
                return trackedItemId;
            }
        }

        // 3. Fallback: If no preview item is active, fallback to character's equipped item
        if (container != null)
        {
            var item = container->GetInventorySlot(equippedSlotIdx);
            if (item != null && item->ItemId != 0)
            {
                uint rawItemId = item->GlamourId != 0 ? item->GlamourId : item->ItemId;
                return rawItemId % 500000;
            }
        }

        return 0;
    }

    /// <summary>
    /// Snapshots current active item IDs + stains across all slots. Live Glamourer state is the
    /// primary source (reflects both real equipment and any active preview, including try-ons
    /// done outside this plugin); the local preview-tracking dictionary only fills gaps Glamourer
    /// didn't report; equipped inventory items are the final fallback.
    /// </summary>
    public unsafe Dictionary<string, GlamourTagger.IPC.GlamourerIpcHandler.SlotState> FetchActiveSlotStates()
    {
        var activeStates = new Dictionary<string, GlamourTagger.IPC.GlamourerIpcHandler.SlotState>(StringComparer.OrdinalIgnoreCase);

        // 1. Glamourer Preview mode: live IPC state is the source of truth
        if (applyMode == 0 && plugin.GlamourerIpc.IsAvailable())
        {
            activeStates = plugin.GlamourerIpc.GetAllSlotStates();
        }

        // 1b. Fill any slot Glamourer didn't report using the local preview-tracking dictionary
        if (applyMode == 0)
        {
            foreach (var (slot, itemId) in plugin.GlamourerPreviewItems)
            {
                if (itemId != 0 && (!activeStates.TryGetValue(slot, out var existing) || existing.ItemId == 0))
                {
                    activeStates[slot] = new GlamourTagger.IPC.GlamourerIpcHandler.SlotState(itemId, 0, 0);
                }
            }
        }

        // 2. Fallback to Fitting Room tracked items or in-game equipped items
        var invManager = InventoryManager.Instance();
        var container = invManager != null ? invManager->GetInventoryContainer(InventoryType.EquippedItems) : null;

        var slotMappings = new (string JsonSlot, int EquippedIdx)[]
        {
            ("MainHand", 0),
            ("OffHand", 1),
            ("Head", 2),
            ("Body", 3),
            ("Hands", 4),
            ("Legs", 6),
            ("Feet", 7),
            ("Ears", 8),
            ("Neck", 9),
            ("Wrists", 10),
            ("RFinger", 11),
            ("LFinger", 12)
        };

        foreach (var (jsonSlot, equippedIdx) in slotMappings)
        {
            // If the slot item ID was not set by Glamourer state or preview tracking, fall back
            if (!activeStates.TryGetValue(jsonSlot, out var existing) || existing.ItemId == 0)
            {
                if (applyMode == 1 && plugin.FittingRoomItems.TryGetValue(jsonSlot, out uint trackedItemId) && trackedItemId != 0)
                {
                    activeStates[jsonSlot] = new GlamourTagger.IPC.GlamourerIpcHandler.SlotState(trackedItemId, 0, 0);
                }
                else if (container != null && equippedIdx < container->Size)
                {
                    var item = container->GetInventorySlot(equippedIdx);
                    if (item != null && item->ItemId != 0)
                    {
                        uint rawItemId = item->GlamourId != 0 ? item->GlamourId : item->ItemId;
                        activeStates[jsonSlot] = new GlamourTagger.IPC.GlamourerIpcHandler.SlotState(rawItemId % 500000, 0, 0);
                    }
                }
            }
        }

        return activeStates;
    }

    private unsafe void ApplyEquippedSlotDye(InventoryContainer* container, string jsonSlotName, int equippedSlotIdx, byte glamourerSlot, byte dye1, byte dye2)
    {
        // Retrieve currently previewed or equipped item ID for target slot
        uint targetItemId = GetActiveSlotItemId(jsonSlotName, container, equippedSlotIdx);

        if (targetItemId != 0)
        {
            if (applyMode == 1)
            {
                plugin.TryOnItem(targetItemId, dye1, dye2);
            }
            else
            {
                plugin.ApplyItemWithGlamourer(targetItemId, glamourerSlot, dye1, dye2, exactSlot: true);
            }
        }
    }
}
