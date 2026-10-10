using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using ImGui = Dalamud.Bindings.ImGui.ImGui;

namespace GlamourTagger.Windows;

public partial class MainWindow
{
    // mod states shown in the table: -1 unusable, 0 unmodded, 1 demodded, 2 modded
    private readonly HashSet<int> selectedModFilters = new() { -1, 0, 1, 2 };

    // Persistent state variables to ensure 100% instant frame-accurate tree node height calculations
    private bool isCatchIntroOpen = true;
    private bool isPrevDemoddedOpen = true;
    private bool isNewlyModdedOpen = false;
    private bool isUnusableOpen = true;

    // Splits the mods Penumbra reports for an item into active / demodded / unusable
    // (the last two are the user's manual marks) and returns the item's overall state.
    public int GetItemModState(uint itemId, string itemName, out List<string> activeMods, out List<string> demoddedMods, out List<string> unusableMods)
    {
        activeMods = new List<string>();
        demoddedMods = new List<string>();
        unusableMods = new List<string>();

        if (!plugin.PenumbraIpc.IsItemModded(itemName, out var allMods))
            return 0; // 0: Unmodded

        if (!plugin.Configuration.DemoddedMods.TryGetValue(itemId, out var demSet))
            demSet = new HashSet<string>();

        if (!plugin.Configuration.UnusableMods.TryGetValue(itemId, out var unusableSet))
            unusableSet = new HashSet<string>();

        foreach (var m in allMods)
        {
            if (unusableSet.Contains(m)) unusableMods.Add(m);
            else if (demSet.Contains(m)) demoddedMods.Add(m);
            else activeMods.Add(m);
        }

        // Ha van legalább egy aktív (working) mod -> Modded (2)
        if (activeMods.Count > 0)
            return 2;

        // Ha nincs aktív mod, de van Unusable mod -> Unusable (-1)
        if (unusableMods.Count > 0)
            return -1;

        // Ha csak Demodded modok vannak -> Demodded (1)
        if (demoddedMods.Count > 0)
            return 1;

        return 0; // 0: Unmodded
    }

    // gem icon of the PM column: colour = mod state, tooltip lists the mods
    private void DrawModButton(ItemRow item, float rowHeight, float rowStartY)
    {
        int modState = GetItemModState(item.Id, item.Name, out var actMods, out var demMods, out var unusableMods);

        string iconStr = FontAwesomeIcon.Gem.ToIconString();

        float fontScale = 0.85f;

        ImGui.PushFont(UiBuilder.IconFont);
        Vector2 textSize = ImGui.CalcTextSize(iconStr);
        float scaledWidth = textSize.X * fontScale;
        float scaledHeight = textSize.Y * fontScale;

        float cursorY = rowStartY + Math.Max(0f, (rowHeight - scaledHeight) * 0.5f);

        ImGui.SetWindowFontScale(fontScale);
        ImGui.SetCursorPosY(cursorY);

        float availWidth = ImGui.GetContentRegionAvail().X;
        float offsetX = Math.Max(0f, (availWidth - scaledWidth) * 0.5f);

        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + offsetX);

        if (modState == 2)
        {
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.20f, 0.85f, 0.95f, 1.0f));
            ImGui.TextUnformatted(iconStr);
            ImGui.PopStyleColor();
        }
        else if (modState == 1)
        {
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.12f, 0.45f, 0.55f, 0.50f));
            ImGui.TextUnformatted(iconStr);
            ImGui.PopStyleColor();
        }
        else if (modState == -1) // Unusable
        {
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.75f, 0.15f, 0.15f, 0.65f));
            ImGui.TextUnformatted(iconStr);
            ImGui.PopStyleColor();
        }
        else
        {
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.25f, 0.25f, 0.25f, 0.60f));
            ImGui.TextUnformatted(iconStr);
            ImGui.PopStyleColor();
        }

        ImGui.PopFont();
        ImGui.SetWindowFontScale(1f);

        if (modState != 0 && ImGui.IsItemHovered())
        {
            ImGui.BeginTooltip();

            Vector4 redColor = new Vector4(0.75f, 0.25f, 0.25f, 1.0f);    // Unusable
            Vector4 purpleColor = new Vector4(0.80f, 0.50f, 0.65f, 1.0f); // Demodded
            Vector4 goldColor = new Vector4(0.9f, 0.9f, 0.68f, 0.95f);    // Header

            if (unusableMods.Count > 0)
            {
                ImGui.TextColored(goldColor, "has");
                ImGui.SameLine();
                ImGui.TextColored(redColor, "[Unusable]");
                if (demMods.Count > 0)
                {
                    ImGui.SameLine();
                    ImGui.TextColored(purpleColor, "[Demodded]");
                }
            }
            else if (demMods.Count > 0)
            {
                ImGui.TextColored(goldColor, "has");
                ImGui.SameLine();
                ImGui.TextColored(purpleColor, "[Demodded]");
            }

            if (actMods.Count > 0)
            {
                ImGui.TextColored(goldColor, $"Active ({actMods.Count}):");
                foreach (var mod in actMods) ImGui.TextUnformatted($"• {mod}");
            }

            if (unusableMods.Count > 0)
            {
                ImGui.TextColored(goldColor, $"Unusable ({unusableMods.Count}):");
                foreach (var mod in unusableMods) ImGui.TextUnformatted($"• {mod}");
            }

            if (demMods.Count > 0)
            {
                ImGui.TextColored(goldColor, $"Demodded ({demMods.Count}):");
                foreach (var mod in demMods) ImGui.TextUnformatted($"• {mod}");
            }

            ImGui.EndTooltip();
        }
    }

    // "Affecting Mods" line of the selected item panel: state badges + one chip per mod, overflow goes into a popup
    private void RenderAffectingModsRow(List<string> affectingMods)
    {
        int modState = GetItemModState(selectedItemId, selectedItemName, out var actMods, out var demMods, out var unusableMods);
        bool hasUnusable = unusableMods.Count > 0;
        bool hasDemodded = demMods.Count > 0;

        ImGui.AlignTextToFramePadding();
        ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1.0f), $"Affecting Mods ({affectingMods.Count}):");

        // right-click on the label = set every mod of this item at once
        if (ImGui.BeginPopupContextItem("AllModsContextMenu"))
        {
            if (ImGui.Selectable("Set all to active"))
            {
                plugin.Configuration.DemoddedMods.Remove(selectedItemId);
                plugin.Configuration.UnusableMods.Remove(selectedItemId);
                plugin.Configuration.Save();
                isDirty = true;
            }
            if (ImGui.Selectable("Set all to demodded"))
            {
                if (!plugin.Configuration.DemoddedMods.ContainsKey(selectedItemId))
                    plugin.Configuration.DemoddedMods[selectedItemId] = new HashSet<string>();
                foreach (var m in affectingMods) plugin.Configuration.DemoddedMods[selectedItemId].Add(m);
                plugin.Configuration.UnusableMods.Remove(selectedItemId);
                plugin.Configuration.Save();
                isDirty = true;
            }
            if (ImGui.Selectable("Set all to unusable"))
            {
                if (!plugin.Configuration.UnusableMods.ContainsKey(selectedItemId))
                    plugin.Configuration.UnusableMods[selectedItemId] = new HashSet<string>();
                foreach (var m in affectingMods) plugin.Configuration.UnusableMods[selectedItemId].Add(m);
                plugin.Configuration.DemoddedMods.Remove(selectedItemId);
                plugin.Configuration.Save();
                isDirty = true;
            }
            ImGui.EndPopup();
        }

        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Right-click to set state for all mods.");

        ImGui.SameLine(0, 2f);
        HelpMarker2("Right-click individual mod badges or the row header to manually override states for more filtering options:\n" +
                    "• Demodded: The mod does not alter the item, or a setting disables its installation (leaving the original item visible).\n" +
                    "• Unusable: The mod breaks or fails to render the item, making it unusable for glamour.\n\n" +
                    "Item State Logic (as shown in the Penumbra Mods column)      -- If an item has:\n" +
                    "  - At least one active, working mod -> Modded\n" +
                    "  - No active mods and at least one unusable mod -> Unusable\n" +
                    "  - No active or unusable mods, but only demodded mods -> Demodded\n\n" +
                    "• Items with demodded or unusable mods receive special tags to make filtering easier.\n" +
                    "• You can search mod names and special tags in the search box using the 'p:' prefix (e.g. p:demodded, p:unusable).");

        ImGui.SameLine(0, 8f);

        if (affectingMods == null || affectingMods.Count == 0)
        {
            ImGui.TextDisabled("-");
            return;
        }

        float availWidth = ImGui.GetContentRegionAvail().X;
        float currentLineWidth = 0f;
        int overflowStartIndex = -1;

        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 4.0f);
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(6, 2));

        // 1. Unusable Icon Badge
        if (hasUnusable)
        {
            ImGui.PushFont(UiBuilder.IconFont);

            string unusableIcon = FontAwesomeIcon.Skull.ToIconString();                 

            float unusableWidth = ImGui.CalcTextSize(unusableIcon).X + 12f;
            currentLineWidth += unusableWidth + 4f;

            ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.55f, 0.12f, 0.12f, 0.55f));
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.55f, 0.12f, 0.12f, 0.65f));
            ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.55f, 0.12f, 0.12f, 0.55f));
            ImGui.Button(unusableIcon);
            ImGui.PopStyleColor(3);
            ImGui.PopFont();

            if (ImGui.IsItemHovered()) ImGui.SetTooltip("has Unusable");
            ImGui.SameLine(0, 4f);
        }

        // 2. Demodded Icon Badge
        if (hasDemodded)
        {
            ImGui.PushFont(UiBuilder.IconFont);

            string demoddedIcon = FontAwesomeIcon.Moon.ToIconString();        
            float demoddedWidth = ImGui.CalcTextSize(demoddedIcon).X + 12f;
            currentLineWidth += demoddedWidth + 4f;

            ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.45f, 0.25f, 0.35f, 0.75f));
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.45f, 0.25f, 0.35f, 0.85f));
            ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.45f, 0.25f, 0.35f, 0.75f));
            ImGui.Button(demoddedIcon);
            ImGui.PopStyleColor(3);
            ImGui.PopFont();

            if (ImGui.IsItemHovered()) ImGui.SetTooltip("has Demodded");
            ImGui.SameLine(0, 4f);
        }

        // active first, then unusable, then demodded
        var sortedModsToDraw = actMods.Select(m => new { Name = m, ModType = 0 })
                             .Concat(unusableMods.Select(m => new { Name = m, ModType = -1 }))
                             .Concat(demMods.Select(m => new { Name = m, ModType = 1 }))
                             .ToList();

        for (int i = 0; i < sortedModsToDraw.Count; i++)
        {
            var mod = sortedModsToDraw[i];
            float badgeWidth = ImGui.CalcTextSize(mod.Name).X + 12f;

            // out of room -> the rest goes behind a "+N more" button
            if (currentLineWidth + badgeWidth > availWidth - 75f && i < sortedModsToDraw.Count - 1)
            {
                overflowStartIndex = i;
                break;
            }

            DrawModChip(mod.Name, mod.ModType, i);
            currentLineWidth += badgeWidth + 4f;

            if (i < sortedModsToDraw.Count - 1)
            {
                ImGui.SameLine(0, 4f);
            }
        }

        if (overflowStartIndex != -1)
        {
            ImGui.SameLine(0, 4f);
            int remainingCount = sortedModsToDraw.Count - overflowStartIndex;
            ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.25f, 0.25f, 0.30f, 0.90f));
            if (ImGui.Button($"+{remainingCount} more##ModOverflowBtn"))
            {
                ImGui.OpenPopup("AffectingModsOverflowPopup");
            }
            ImGui.PopStyleColor();

            if (ImGui.BeginPopup("AffectingModsOverflowPopup"))
            {
                ImGui.TextDisabled("-- Additional Affecting Mods --");
                ImGui.Separator();

                ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 4.0f);
                ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(6, 2));

                for (int k = overflowStartIndex; k < sortedModsToDraw.Count; k++)
                {
                    var mod = sortedModsToDraw[k];
                    DrawModChip(mod.Name, mod.ModType, 500 + k);
                    if (k < sortedModsToDraw.Count - 1) ImGui.SameLine(0, 4f);
                }

                ImGui.PopStyleVar(2);
                ImGui.EndPopup();
            }
        }

        ImGui.PopStyleVar(2);
    }

    private void DrawModChip(string modName, int modType, int index)
    {
        // modType: 0 = Active, 1 = Demodded, -1 = Unusable
        if (modType == -1) // Unusable
        {
            ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.50f, 0.12f, 0.12f, 0.38f));
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.50f, 0.12f, 0.12f, 0.48f));
            ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.50f, 0.12f, 0.12f, 0.55f));
        }
        else if (modType == 1) // Demodded
        {
            ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.10f, 0.30f, 0.40f, 0.35f));
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.12f, 0.35f, 0.45f, 0.45f));
            ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.15f, 0.40f, 0.50f, 0.5f));
        }
        else
        {
            ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.12f, 0.45f, 0.55f, 0.50f));
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.18f, 0.55f, 0.68f, 0.60f));
            ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.22f, 0.65f, 0.78f, 0.65f));
        }

        ImGui.PushID($"mod_chip_{index}");
        ImGui.Button(modName);

        // right-click a chip = change this one mod's state for the selected item
        if (ImGui.BeginPopupContextItem($"ModContextMenu_{index}"))
        {
            if (modType != 0 && ImGui.Selectable("Set to active"))
            {
                if (plugin.Configuration.DemoddedMods.TryGetValue(selectedItemId, out var demSet)) demSet.Remove(modName);
                if (plugin.Configuration.UnusableMods.TryGetValue(selectedItemId, out var unSet)) unSet.Remove(modName);
                plugin.Configuration.Save();
                isDirty = true;
            }
            if (modType != 1 && ImGui.Selectable("Set to demodded"))
            {
                if (!plugin.Configuration.DemoddedMods.ContainsKey(selectedItemId))
                    plugin.Configuration.DemoddedMods[selectedItemId] = new HashSet<string>();

                plugin.Configuration.DemoddedMods[selectedItemId].Add(modName);
                if (plugin.Configuration.UnusableMods.TryGetValue(selectedItemId, out var unSet)) unSet.Remove(modName);
                plugin.Configuration.Save();
                isDirty = true;
            }
            if (modType != -1 && ImGui.Selectable("Set to unusable"))
            {
                if (!plugin.Configuration.UnusableMods.ContainsKey(selectedItemId))
                    plugin.Configuration.UnusableMods[selectedItemId] = new HashSet<string>();

                plugin.Configuration.UnusableMods[selectedItemId].Add(modName);
                if (plugin.Configuration.DemoddedMods.TryGetValue(selectedItemId, out var demSet)) demSet.Remove(modName);
                plugin.Configuration.Save();
                isDirty = true;
            }
            ImGui.EndPopup();
        }

        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Right-click to toggle state.");

        ImGui.PopID();
        ImGui.PopStyleColor(3);
    }

    // Penumbra cache was refreshed: collect the items that are modded now but weren't in the previous scan, then show the catch window
    private void HandlePenumbraCacheUpdate(Dictionary<string, List<string>> oldCache, Dictionary<string, List<string>> newCache)
    {
        Plugin.Framework.RunOnFrameworkThread(() =>
        {
            // 1. Check session suppression or global 'Never Show' setting
            if (sessionSuppressCatchWindow || plugin.Configuration.NeverShowCatchWindow)
                return;

            // item names that are in the new scan but not in the old one
            var newlyCaughtNames = newCache.Keys.Except(oldCache.Keys, StringComparer.OrdinalIgnoreCase).ToList();
            if (newlyCaughtNames.Count == 0) return;

            newlyUnmoddedCatches.Clear();
            previouslyDemoddedCatches.Clear();
            previouslyUnusableCatches.Clear();

            foreach (var name in newlyCaughtNames)
            {
                //jav
                //var matchingItems = cachedItems.Where(i => i.Name.Equals(name, StringComparison.OrdinalIgnoreCase)).ToList();
                var matchingItems = itemsByName[name];

                var modName = newCache.TryGetValue(name, out var mods) ? string.Join(", ", mods) : "";

                foreach (var item in matchingItems)
                {
                    GetItemModState(item.Id, item.Name, out _, out var demMods, out var unusableMods);

                    if (unusableMods.Count > 0)
                    {
                        if (!plugin.Configuration.IgnoreUnusableCatches)
                            previouslyUnusableCatches.Add((item, modName));
                    }
                    else if (demMods.Count > 0)
                    {
                        if (!plugin.Configuration.IgnorePrevDemoddedCatches)
                            previouslyDemoddedCatches.Add((item, modName));
                    }
                    else
                    {
                        if (!plugin.Configuration.IgnoreNewlyModdedCatches)
                            newlyUnmoddedCatches.Add((item, modName));
                    }
                }
            }

            // 2. Open catch window only if filtered results contain items
            if (newlyUnmoddedCatches.Count > 0 || previouslyDemoddedCatches.Count > 0 || previouslyUnusableCatches.Count > 0)
            {
                isUnusableOpen = true;
                isPrevDemoddedOpen = true;
                isNewlyModdedOpen = false;
                showPenumbraCatchWindow = true;
            }
        });
    }

    // "Penumbra New Catches" window: up to three collapsible tables + mute / options / close
    private void DrawPenumbraCatchWindow()
    {
        // =========================================================================
        // ADJUSTABLE LIMITS & SPACING
        // =========================================================================
        float maxModColumnWidth = 300f;     // Max width for Mod Name column
        float maxItemColumnWidth = 300f;    // Max width for Item Name column
        float maxWindowHeight = 950f;       // Max total height for the window area

        float buttonSideMargin = 40f;        // Distance of buttons from left/right window edges
        float buttonSpacing = 15f;          // Spacing between Dummy, Dummy2, and Close buttons
        // =========================================================================

        if (ImGui.Begin("Penumbra New Catches", ref showPenumbraCatchWindow, ImGuiWindowFlags.AlwaysAutoResize))
        {
            // 1. Calculate dynamic column widths across BOTH tables
            float cellPadding = 12f;
            float maxModWidth = 10f;
            float maxItemWidth = 10f;

            var allCatches = previouslyUnusableCatches.Concat(previouslyDemoddedCatches).Concat(newlyUnmoddedCatches);
            foreach (var (item, modName) in allCatches)
            {
                float wMod = ImGui.CalcTextSize($"- {modName}").X;
                float wItem = ImGui.CalcTextSize($"- {item.Name}").X;

                if (wMod > maxModWidth) maxModWidth = wMod;
                if (wItem > maxItemWidth) maxItemWidth = wItem;
            }

            float modColWidth = Math.Min(maxModWidth + cellPadding, maxModColumnWidth);
            float itemColWidth = Math.Min(maxItemWidth + cellPadding, maxItemColumnWidth);
            float jumpColWidth = 56f; // Strictly fits the 24px icon button + padding

            // 2. Measure widths of top header texts
            string mainHeader = "Penumbra detected new modded items. Here is the breakdown:";
            float mainHeaderWidth = ImGui.CalcTextSize(mainHeader).X;

            bool isUnusableDisabled = plugin.Configuration.IgnoreUnusableCatches;
            string unusableHeader = isUnusableDisabled
                ? "Previously Unusable Cache Catches are disabled"
                : $"Previously Unusable Items Caught ({previouslyUnusableCatches.Count}):";
            float unusableHeaderWidth = ImGui.CalcTextSize(unusableHeader).X + 28f;

            bool isPrevDisabled = plugin.Configuration.IgnorePrevDemoddedCatches;
            string prevHeader = isPrevDisabled
                ? "Previously Demodded Cache Catches are disabled"
                : $"Previously Demodded Items Caught ({previouslyDemoddedCatches.Count}):";
            float prevHeaderWidth = ImGui.CalcTextSize(prevHeader).X + 28f;

            bool isNewlyDisabled = plugin.Configuration.IgnoreNewlyModdedCatches;
            string newHeader = isNewlyDisabled
                ? "Newly Modded Cache Catches are disabled"
                : $"Newly Modded Items ({newlyUnmoddedCatches.Count}):";
            float newHeaderWidth = ImGui.CalcTextSize(newHeader).X + 28f;

            // Total required width for tables (3 columns + ImGui table cell padding overhead)
            float totalTableWidth = modColWidth + itemColWidth + jumpColWidth + 24f;

            // Strict content width ensures window auto-resizes directly around content
            float contentWidth = Math.Max(totalTableWidth, Math.Max(mainHeaderWidth, Math.Max(unusableHeaderWidth, Math.Max(prevHeaderWidth, newHeaderWidth))));

            // Introductory explanation block
            // Collapsible introductory explanation block
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.9f, 0.7f, 0.2f, 1.0f));
            ImGui.SetNextItemOpen(isCatchIntroOpen);
            isCatchIntroOpen = ImGui.TreeNodeEx("Penumbra Cache Catch Introduction##CatchIntroHeader", ImGuiTreeNodeFlags.NoTreePushOnOpen);
            ImGui.PopStyleColor();

            if (isCatchIntroOpen)
            {
                ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.85f, 0.85f, 0.85f, 1.0f));
                ImGui.TextWrapped("Glamour Tagger reads Penumbra's data at plugin startup, when you click the 'Recatch Penumbra & Redraw Self' button, " +
                                 "and automatically while you use the item list (at most once every 30 seconds).\n" + 
                                 "This notification window appears because Glamour Tagger cannot automatically determine how Penumbra settings affect an item " +
                                 "— whether a mod actively changes it, leaves it unaffected, or renders it unusable.\n" +
                                 "Therefore, users can only categorize these mod badges manually. This window streamlines that sorting process " +
                                 "by grouping newly detected mods and previously applied states to them, thus eliminating manual searches.");
                ImGui.PopStyleColor();
                ImGui.Spacing();
            }
            ImGui.Separator();

            // 3. Render Header Text
            ImGui.TextUnformatted(mainHeader);
            ImGui.Separator();

            // 4. Calculate dynamic height based on content
            float treeNodeHeight = 26f;  // TreeNode height + padding
            float itemSpacing = 4f;       // ItemSpacing.Y set in child window
            float rowHeight = 32f;        // 24f requested height + 8f ImGui table cell padding
            float tablePadding = 12f;     // BeginTable top/bottom frame overhead
            float childPadding = 16f;     // BeginChild top + bottom WindowPadding

            float estimatedContentHeight = childPadding;

            // Previously Unusable
            estimatedContentHeight += treeNodeHeight + itemSpacing;
            if (!isUnusableDisabled && isUnusableOpen && previouslyUnusableCatches.Count > 0)
            {
                estimatedContentHeight += (previouslyUnusableCatches.Count * rowHeight) + tablePadding + itemSpacing;
            }

            // Previously Demodded
            estimatedContentHeight += treeNodeHeight + itemSpacing;
            if (!isPrevDisabled && isPrevDemoddedOpen && previouslyDemoddedCatches.Count > 0)
            {
                estimatedContentHeight += (previouslyDemoddedCatches.Count * rowHeight) + tablePadding + itemSpacing;
            }

            // Newly Modded
            estimatedContentHeight += treeNodeHeight + itemSpacing;
            if (!isNewlyDisabled && isNewlyModdedOpen && newlyUnmoddedCatches.Count > 0)
            {
                estimatedContentHeight += (newlyUnmoddedCatches.Count * rowHeight) + tablePadding + itemSpacing;
            }

            // Small safety buffer to prevent 1px rounding subpixel scrollbars
            estimatedContentHeight += 6f;

            float maxTableAreaHeight = Math.Max(100f, maxWindowHeight - 90f);
            float childHeight = Math.Min(estimatedContentHeight, maxTableAreaHeight);

            bool needsScrollbar = estimatedContentHeight > maxTableAreaHeight;
            float childWidth = contentWidth + (needsScrollbar ? ImGui.GetStyle().ScrollbarSize : 0f);

            // Pass exact childWidth so ImGui AlwaysAutoResize wraps tightly
            if (ImGui.BeginChild("CatchTablesScrollArea", new Vector2(childWidth, childHeight)))
            {
                ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(0, 4f));

                // Previously Unusable
                Vector4 unusableHeaderColor = isUnusableDisabled
                    ? new Vector4(0.55f, 0.55f, 0.55f, 0.80f)
                    : new Vector4(0.85f, 0.35f, 0.35f, 1.0f);

                ImGui.PushStyleColor(ImGuiCol.Text, unusableHeaderColor);
                ImGui.SetNextItemOpen(isUnusableOpen);
                isUnusableOpen = ImGui.TreeNodeEx($"{unusableHeader}##UnusableCatchHeader", ImGuiTreeNodeFlags.NoTreePushOnOpen);
                ImGui.PopStyleColor();

                if (!isUnusableDisabled && isUnusableOpen && previouslyUnusableCatches.Count > 0)
                {
                    RenderCatchTable("UnusableCatchesTable", previouslyUnusableCatches, "Unusable", modColWidth, itemColWidth, jumpColWidth);
                }

                // Previously Demodded
                Vector4 prevHeaderColor = isPrevDisabled
                    ? new Vector4(0.55f, 0.55f, 0.55f, 0.80f)
                    : new Vector4(0.2f, 0.8f, 0.9f, 1.0f);

                ImGui.PushStyleColor(ImGuiCol.Text, prevHeaderColor);
                ImGui.SetNextItemOpen(isPrevDemoddedOpen);
                isPrevDemoddedOpen = ImGui.TreeNodeEx($"{prevHeader}##PrevDemCatchHeader", ImGuiTreeNodeFlags.NoTreePushOnOpen);
                ImGui.PopStyleColor();

                if (!isPrevDisabled && isPrevDemoddedOpen && previouslyDemoddedCatches.Count > 0)
                {
                    RenderCatchTable("PrevDemoddedCatchesTable", previouslyDemoddedCatches, "Dem", modColWidth, itemColWidth, jumpColWidth);
                }

                // Newly Modded
                Vector4 newHeaderColor = isNewlyDisabled
                    ? new Vector4(0.55f, 0.55f, 0.55f, 0.80f)
                    : new Vector4(0.9f, 0.7f, 0.2f, 1.0f);

                ImGui.PushStyleColor(ImGuiCol.Text, newHeaderColor);
                ImGui.SetNextItemOpen(isNewlyModdedOpen);
                isNewlyModdedOpen = ImGui.TreeNodeEx($"{newHeader}##NewlyModCatchHeader", ImGuiTreeNodeFlags.NoTreePushOnOpen);
                ImGui.PopStyleColor();

                if (!isNewlyDisabled && isNewlyModdedOpen && newlyUnmoddedCatches.Count > 0)
                {
                    RenderCatchTable("NewlyModdedCatchesTable", newlyUnmoddedCatches, "New", modColWidth, itemColWidth, jumpColWidth);
                }

                ImGui.PopStyleVar();
                ImGui.EndChild();
            }

            ImGui.Separator();
            ImGui.Spacing();

            // 5. Render Bottom Buttons with adjustable side margins
            float usableButtonWidth = contentWidth - (buttonSideMargin * 2f);
            float btnWidth = Math.Max(10f, (usableButtonWidth - (buttonSpacing * 2f)) / 3f);

            ImGui.SetCursorPosX(ImGui.GetCursorPosX() + buttonSideMargin);

            ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.35f, 0.10f, 0.10f, 0.95f));
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.3f, 0.55f, 0.85f, 0.95f));
            ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.55f, 0.20f, 0.20f, 0.95f));

            // --- BUTTON 1: Mute for Session ---
            if (ImGui.Button("Mute Session", new Vector2(btnWidth, 24f)))
            {
                sessionSuppressCatchWindow = true;
                showPenumbraCatchWindow = false;
            }
            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip("Dismisses this notification and mutes further catch popups\nuntil the Glamour Tagger main window is closed and reopened.");
            }

            ImGui.SameLine(0, buttonSpacing);

            // --- BUTTON 2: Options (Jump to Settings) ---
            string jumpIconStr = FontAwesomeIcon.SlidersH.ToIconString();

            if (DrawIconButton("CatchOptionsBtn", "Options", jumpIconStr, new Vector2(btnWidth, 24f)))
            {
                plugin.SettingsWindow.Toggle();
            }
            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip("Jump to Visual & Notification Settings to manage Penumbra catch preferences.");
            }

            ImGui.SameLine(0, buttonSpacing);

            // --- BUTTON 3: Close ---
            if (ImGui.Button("Close", new Vector2(btnWidth, 24f)))
            {
                showPenumbraCatchWindow = false;
            }

            ImGui.PopStyleColor(3);
        }

        ImGui.End();
    }

    // one table of catches: mod | item | jump + apply buttons
    private void RenderCatchTable(string tableId, List<(ItemRow Item, string ModName)> catches, string idPrefix, float modColWidth, float itemColWidth, float jumpColWidth)
    {
        if (ImGui.BeginTable(tableId, 3, ImGuiTableFlags.SizingFixedFit | ImGuiTableFlags.RowBg))
        {
            ImGui.TableSetupColumn("Mod", ImGuiTableColumnFlags.WidthFixed, modColWidth);
            ImGui.TableSetupColumn("Item", ImGuiTableColumnFlags.WidthFixed, itemColWidth);
            ImGui.TableSetupColumn("Jump", ImGuiTableColumnFlags.WidthFixed, jumpColWidth);

            // Sort entries alphabetically by Mod Name first, then by Item Name
            var sortedCatches = catches
                .OrderBy(c => c.ModName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(c => c.Item.Name, StringComparer.OrdinalIgnoreCase);

            foreach (var (item, modName) in sortedCatches)
            {
                ImGui.TableNextRow(ImGuiTableRowFlags.None, 24f);

                // 1. Mod Name Column
                ImGui.TableNextColumn();
                ImGui.AlignTextToFramePadding();
                string rawModText = $"- {modName}";
                string displayModText = TruncateText(rawModText, modColWidth - 8f);
                ImGui.TextUnformatted(displayModText);
                if (displayModText.EndsWith("...") && ImGui.IsItemHovered())
                {
                    ImGui.SetTooltip(modName);
                }

                // 2. Item Name Column
                ImGui.TableNextColumn();
                ImGui.AlignTextToFramePadding();
                string rawItemText = $"- {item.Name}";
                string displayItemText = TruncateText(rawItemText, itemColWidth - 8f);
                ImGui.TextUnformatted(displayItemText);
                if (displayItemText.EndsWith("...") && ImGui.IsItemHovered())
                {
                    ImGui.SetTooltip(item.Name);
                }

                // 3. Jump & Apply Button Column
                ImGui.TableNextColumn();
                DrawCatchJumpButton($"jump_{idPrefix}_{item.Id}", item);
                ImGui.SameLine(0, 4f);
                DrawCatchApplyAndJumpButton($"apply_{idPrefix}_{item.Id}", item);
            }
            ImGui.EndTable();
        }
    }

    // cut text to fit maxWidth and add "..." (binary search on the length)
    private string TruncateText(string text, float maxWidth)
    {
        if (ImGui.CalcTextSize(text).X <= maxWidth)
            return text;

        string suffix = "...";
        float suffixWidth = ImGui.CalcTextSize(suffix).X;

        int low = 0, high = text.Length;
        int bestLen = 0;

        while (low <= high)
        {
            int mid = (low + high) / 2;
            string sub = text.Substring(0, mid);
            if (ImGui.CalcTextSize(sub).X + suffixWidth <= maxWidth)
            {
                bestLen = mid;
                low = mid + 1;
            }
            else
            {
                high = mid - 1;
            }
        }

        return text.Substring(0, bestLen) + suffix;
    }

    // ">>" button: select the item, link it in chat, scroll the main table to it
    private void DrawCatchJumpButton(string id, ItemRow item)
    {
        ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.35f, 0.10f, 0.10f, 0.95f));
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.3f, 0.55f, 0.85f, 0.95f));
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.55f, 0.20f, 0.20f, 0.95f));

        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, Vector2.Zero);
        ImGui.PushStyleVar(ImGuiStyleVar.ButtonTextAlign, new Vector2(0.5f, 0.5f));

        ImGui.PushFont(UiBuilder.IconFont);
        string jumpIconStr = FontAwesomeIcon.AngleDoubleRight.ToIconString();

        if (ImGui.Button($"{jumpIconStr}##{id}", new Vector2(24f, 24f)))
        {
            SelectAndLinkItem(item);
            scrollToSelectedItem = true;
        }

        ImGui.PopFont();
        ImGui.PopStyleVar(2);
        ImGui.PopStyleColor(3);

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("Jump to item in table");
        }
    }

    // eye button: same as jump, plus preview the item
    private void DrawCatchApplyAndJumpButton(string id, ItemRow item)
    {
        ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.35f, 0.10f, 0.10f, 0.95f));
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.3f, 0.55f, 0.85f, 0.95f));
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.55f, 0.20f, 0.20f, 0.95f));

        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, Vector2.Zero);
        ImGui.PushStyleVar(ImGuiStyleVar.ButtonTextAlign, new Vector2(0.5f, 0.5f));

        ImGui.PushFont(UiBuilder.IconFont);
        string eyeIconStr = FontAwesomeIcon.Eye.ToIconString();

        if (ImGui.Button($"{eyeIconStr}##{id}", new Vector2(24f, 24f)))
        {
            SelectAndLinkItem(item);
            ApplyCurrentItem(item);
            scrollToSelectedItem = true;
        }

        ImGui.PopFont();
        ImGui.PopStyleVar(2);
        ImGui.PopStyleColor(3);

        if (ImGui.IsItemHovered())
        {
            string actionStr = applyMode == 0 ? "apply to current actor" : "Try On";
            ImGui.SetTooltip($"Apply item ({actionStr}) and jump to it in table");
        }
    }

    // button with the text first and the icon after it
    private bool DrawIconButton(string id, string label, string iconStr, Vector2 size)
    {
        Vector2 cursorPos = ImGui.GetCursorScreenPos();
        bool clicked = ImGui.Button($"##{id}", size);

        Vector2 textSize = ImGui.CalcTextSize(label);

        ImGui.PushFont(UiBuilder.IconFont);
        Vector2 iconSize = ImGui.CalcTextSize(iconStr);
        ImGui.PopFont();

        float totalWidth = textSize.X + 6f + iconSize.X;
        float startX = cursorPos.X + (size.X - totalWidth) * 0.5f;
        float startY = cursorPos.Y + (size.Y - textSize.Y) * 0.5f;

        var drawList = ImGui.GetWindowDrawList();
        drawList.AddText(new Vector2(startX, startY), ImGui.GetColorU32(ImGuiCol.Text), label);

        ImGui.PushFont(UiBuilder.IconFont);
        drawList.AddText(new Vector2(startX + textSize.X + 6f, startY), ImGui.GetColorU32(ImGuiCol.Text), iconStr);
        ImGui.PopFont();

        return clicked;
    }
}
