using Dalamud.Bindings.ImGui;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;

namespace GlamourTagger.Windows;

public partial class MainWindow
{
    // --- Tag & Flag Manager window + tag helpers ---
    private bool showTagManagerWindow = false;
    private string newTagInputBuffer = "";
    private string tagManagerSearchFilter = "";
    private bool tagManagerCacheDirty = true;
    private List<TagManagerData> tagManagerCache = new();

    // one row of the manager table (EditBuffer = the rename text box)
    private class TagManagerData
    {
        public string TagName = "";
        public string EditBuffer = "";
        public int ItemCount = 0;
    }

    // the 10 colours offered for tags
    private static readonly Vector4[] PaletteColors = new Vector4[]
    {
        Configuration.DefaultTagColor,
        new(0.24f, 0.10f, 0.40f, 1.0f),
        new(0.28f, 0.12f, 0.28f, 1.0f),
        new(0.30f, 0.10f, 0.06f, 1.0f),
        new(0.30f, 0.20f, 0.05f, 1.0f),
        new(0.28f, 0.25f, 0.05f, 1.0f),
        new(0.18f, 0.24f, 0.08f, 1.0f),
        new(0.10f, 0.23f, 0.23f, 1.0f),
        new(0.12f, 0.12f, 0.15f, 1.0f),
        new(0.0f, 0.0f, 0.0f, 1.0f)
    };

    // colour of a tag that has no picked colour yet (= the first palette colour)
    private static readonly Vector4 DefaultTagColor = PaletteColors[0];

    // tags without a picked colour get the default blue
    private Vector4 GetTagColor(string tag)
    {
        if (plugin.Configuration.TagColors.TryGetValue(tag, out var color))
            return color;
        return DefaultTagColor;
    }

    // sorted list of every tag in use, for the dropdowns and filters
    private void RebuildTagCache()
    {
        cachedUniqueTags.Clear();
        cachedUniqueTags.AddRange(
            plugin.Configuration.ItemTags.Values
            .Where(tags => tags != null)
            .SelectMany(x => x)
            .Concat(plugin.Configuration.TagColors.Keys) // created in the manager, not assigned yet
            .Distinct()
            .OrderBy(x => x)
        );

        // the Tag & Flag Manager counts items from the same data
        tagManagerCacheDirty = true;
    }

    // shapes of the exported json files
    public class FavoritesExportData
    {
        public HashSet<uint> Favorites { get; set; } = new();
        public HashSet<uint> DoubleFavorites { get; set; } = new();
        public HashSet<uint> TripleFavorites { get; set; } = new();
    }

    public class WishlistExportData
    {
        public HashSet<uint> Wishlist { get; set; } = new();
        public HashSet<uint> DoubleWishlist { get; set; } = new();
    }

    private enum JsonFileType
    {
        Unknown,
        Tags,
        Favorites,
        Wishlist
    }

    // Módválasztó gombok állapota
    private bool exportImportTags = true;
    private bool exportImportFavorites = false;
    private bool exportImportWishlists = false;

    // create / rename / recolour / delete tags, export & import at the bottom
    private void DrawTagManagerWindow()
    {
        ImGui.SetNextWindowSize(new Vector2(520, 800), ImGuiCond.FirstUseEver);
        if (ImGui.Begin("Tag & Flag Manager", ref showTagManagerWindow))
        {
            ImGui.Spacing();
            ImGui.Separator();
            ImGui.TextUnformatted("Create New Tag");
            ImGui.SetNextItemWidth(250f);
            ImGui.InputText("##NewTagInput", ref newTagInputBuffer, 50);
            ImGui.SameLine();
            if (ImGui.Button("Add Tag"))
            {
                CreateNewTag(newTagInputBuffer);
                newTagInputBuffer = "";
            }

            ImGui.Separator();
            ImGui.Spacing();

            ImGui.SetNextItemWidth(-1);
            ImGui.InputTextWithHint("##TagManagerSearch", "Search tags...", ref tagManagerSearchFilter, 50);
            ImGui.Spacing();

            RefreshTagManagerCache();
            if (ImGui.BeginTable("TagManagerTable", 4, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY, new Vector2(0, -262)))
            {
                ImGui.TableSetupScrollFreeze(0, 1);

                ImGui.TableSetupColumn("Tag Name", ImGuiTableColumnFlags.WidthStretch);
                ImGui.TableSetupColumn("Color", ImGuiTableColumnFlags.WidthFixed, 40f);
                ImGui.TableSetupColumn("Items", ImGuiTableColumnFlags.WidthFixed, 75f);
                ImGui.TableSetupColumn("Del", ImGuiTableColumnFlags.WidthFixed, 25f);

                ImGui.TableNextRow(ImGuiTableRowFlags.Headers);

                ImGui.TableSetColumnIndex(0);
                ImGui.TextUnformatted("Tag Name");

                ImGui.TableSetColumnIndex(1);
                {
                    string headerText = "Color";
                    float cellStartX = ImGui.GetCursorPosX();
                    float availWidth = ImGui.GetContentRegionAvail().X;
                    float textWidth = ImGui.CalcTextSize(headerText).X;
                    ImGui.SetCursorPosX(cellStartX + Math.Max(0f, (availWidth - textWidth) * 0.5f));
                    ImGui.TextUnformatted(headerText);
                }

                ImGui.TableSetColumnIndex(2);
                ImGui.TextUnformatted("Items");

                ImGui.TableSetColumnIndex(3);
                {
                    string headerText = "Del";
                    float cellStartX = ImGui.GetCursorPosX();
                    float availWidth = ImGui.GetContentRegionAvail().X;
                    float textWidth = ImGui.CalcTextSize(headerText).X;
                    ImGui.SetCursorPosX(cellStartX + Math.Max(0f, (availWidth - textWidth) * 0.5f));
                    ImGui.TextUnformatted(headerText);
                }

                for (int i = 0; i < tagManagerCache.Count; i++)
                {
                    var entry = tagManagerCache[i];

                    if (!string.IsNullOrEmpty(tagManagerSearchFilter) &&
                        !entry.TagName.Contains(tagManagerSearchFilter, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    ImGui.TableNextRow();

                    ImGui.TableSetColumnIndex(0);
                    ImGui.SetNextItemWidth(-1);
                    // rename: Enter or clicking away commits it
                    if (ImGui.InputText($"##TagName_{i}", ref entry.EditBuffer, 50, ImGuiInputTextFlags.EnterReturnsTrue))
                    {
                        RenameTag(entry.TagName, entry.EditBuffer);
                    }
                    if (ImGui.IsItemDeactivatedAfterEdit() && entry.EditBuffer != entry.TagName)
                    {
                        RenameTag(entry.TagName, entry.EditBuffer);
                    }

                    ImGui.TableSetColumnIndex(1);
                    var currentColor = GetTagColor(entry.TagName);

                    ImGui.PushStyleColor(ImGuiCol.Button, currentColor);
                    ImGui.PushStyleColor(ImGuiCol.ButtonHovered, currentColor with { W = 0.8f });
                    ImGui.PushStyleColor(ImGuiCol.ButtonActive, currentColor with { W = 1.0f });
                    ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 6f);

                    float col1Width = ImGui.GetContentRegionAvail().X;
                    float btnSize1 = 24f;
                    ImGui.SetCursorPosX(ImGui.GetCursorPosX() + Math.Max(0f, (col1Width - btnSize1) * 0.5f));

                    if (ImGui.Button($"##ColorBtn_{i}", new Vector2(btnSize1, btnSize1)))
                    {
                        ImGui.OpenPopup($"ColorPalettePopup_{i}");
                    }
                    ImGui.PopStyleVar();
                    ImGui.PopStyleColor(3);

                    // 2 x 5 colour swatches
                    if (ImGui.BeginPopup($"ColorPalettePopup_{i}"))
                    {
                        ImGui.TextUnformatted("Select Tag Color");
                        ImGui.Separator();

                        for (int row = 0; row < 2; row++)
                        {
                            for (int col = 0; col < 5; col++)
                            {
                                int paletteIdx = row * 5 + col;
                                if (paletteIdx < PaletteColors.Length)
                                {
                                    var pColor = PaletteColors[paletteIdx];
                                    ImGui.PushID($"palette_{i}_{paletteIdx}");
                                    ImGui.PushStyleColor(ImGuiCol.Button, pColor);
                                    ImGui.PushStyleColor(ImGuiCol.ButtonHovered, pColor with { W = 0.8f });
                                    ImGui.PushStyleColor(ImGuiCol.ButtonActive, pColor with { W = 1.0f });
                                    ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 4f);

                                    if (ImGui.Button("##col", new Vector2(22, 22)))
                                    {
                                        plugin.Configuration.TagColors[entry.TagName] = pColor;
                                        plugin.Configuration.Save();
                                        tagManagerCacheDirty = true;
                                        isDirty = true;
                                        ImGui.CloseCurrentPopup();
                                    }
                                    ImGui.PopStyleVar();
                                    ImGui.PopStyleColor(3);
                                    ImGui.PopID();

                                    if (col < 4) ImGui.SameLine();
                                }
                            }
                        }
                        ImGui.EndPopup();
                    }

                    ImGui.TableSetColumnIndex(2);
                    ImGui.TextUnformatted($"{entry.ItemCount} items");

                    ImGui.TableSetColumnIndex(3);
                    // delete needs Ctrl+Shift held - it removes the tag from every item
                    bool isCtrlShift = ImGui.GetIO().KeyCtrl && ImGui.GetIO().KeyShift;

                    if (!isCtrlShift) ImGui.BeginDisabled();

                    ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.35f, 0.10f, 0.10f, 0.95f));
                    ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.45f, 0.15f, 0.15f, 0.95f));
                    ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.55f, 0.20f, 0.20f, 0.95f));

                    float col3Width = ImGui.GetContentRegionAvail().X;
                    float btnSize3 = 22f;
                    ImGui.SetCursorPosX(ImGui.GetCursorPosX() + Math.Max(0f, (col3Width - btnSize3) * 0.5f));

                    if (ImGui.Button($"X##del_{i}", new Vector2(btnSize3, 22f)))
                    {
                        if (isCtrlShift)
                        {
                            DeleteTagCompletely(entry.TagName);
                        }
                    }
                    ImGui.PopStyleColor(3);

                    if (!isCtrlShift)
                    {
                        ImGui.EndDisabled();
                        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
                        {
                            ImGui.SetTooltip("Hold Ctrl + Shift to enable deletion.");
                        }
                    }
                }
                ImGui.EndTable();
            }

            ImGui.Spacing();
            ImGui.Spacing();
            ImGui.Spacing();
            ImGui.Separator();
            ImGui.TextUnformatted("Export & Import");

            ImGui.Spacing();
            ImGui.TextUnformatted("   Backup Directory / File Path:");

            // export / import target: empty = plugin config folder, otherwise a folder or a .json file
            string customPath = plugin.Configuration.CustomExportImportPath;
            ImGui.SetNextItemWidth(-1);
            if (ImGui.InputTextWithHint("##CustomPath", "Default (Plugin Config Folder)", ref customPath, 260))
            {
                plugin.Configuration.CustomExportImportPath = customPath;
                plugin.Configuration.Save();
            }
            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip("Leave empty to use default plugin config directory.");
            }

            string resolvedPath = GetResolvedBackupPath(TagsFilePrefix, SeveralExportCategoriesSelected); 
            bool isDefault = string.IsNullOrWhiteSpace(plugin.Configuration.CustomExportImportPath);
            string targetText = isDefault ? $"   Target: [Default] {resolvedPath}" : $"Target: {resolvedPath}";

            ImGui.PushStyleColor(ImGuiCol.Text, ImGui.GetStyle().Colors[(int)ImGuiCol.TextDisabled]);
            ImGui.TextWrapped(targetText);
            ImGui.PopStyleColor();

            ImGui.Spacing();
            ImGui.Spacing();

            // --- MÓDVÁLASZTÓ CHECKBOXOK ---
            ImGui.TextUnformatted("   Select categories to export/import:");

            ImGui.Indent(15);
            ImGui.Checkbox("Favorites (F, DF, TF)##expImp", ref exportImportFavorites);
            ImGui.SameLine(0, 15);
            ImGui.Checkbox("Wishlists (WL, DWL)##expImp", ref exportImportWishlists);
            ImGui.SameLine(0, 15);
            ImGui.Checkbox("Tags##expImp", ref exportImportTags);
            ImGui.Unindent();

            ImGui.Spacing();
            ImGui.Spacing();

            float spacing = 8f;
            float btnWidth = (ImGui.GetContentRegionAvail().X - spacing) * 0.5f;

            if (ImGui.Button("Export & Backup", new Vector2(btnWidth, 0)))
            {
                ExportSelectedDataToFile();
            }
            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip("Saves selected data types into separate JSON files.");
            }

            ImGui.SameLine(0, spacing);

            if (ImGui.Button("Import & Merge", new Vector2(btnWidth, 0)))
            {
                ImportSelectedDataFromFile();
            }
            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip("Loads the selected data types from JSON files and merges them into your data.");
            }

            ImGui.Separator();
            ImGui.Spacing();
        }

        ImGui.End();
    }

    private void RenderTagsAsBadges(List<string> tags, bool removable, Action<string>? onRemove)
    {
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 6.0f);
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(6, 2));

        for (int i = 0; i < tags.Count; i++)
        {
            string tag = tags[i];
            string displayText = removable ? $"{tag} ×" : tag;

            var tagCol = GetTagColor(tag);
            ImGui.PushStyleColor(ImGuiCol.Button, tagCol);
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, tagCol with { W = Math.Min(1.0f, tagCol.W + 0.15f) });
            ImGui.PushStyleColor(ImGuiCol.ButtonActive, tagCol with { W = 1.0f });

            if (ImGui.Button($"{displayText}##tag_{i}"))
            {
                if (removable && onRemove != null)
                {
                    onRemove(tag);
                    ImGui.PopStyleColor(3);
                    break;
                }
            }
            ImGui.PopStyleColor(3);
            if (i < tags.Count - 1) ImGui.SameLine(0, 4);
        }

        ImGui.PopStyleVar(2);
    }

    // no duplicates; saves and refreshes the caches
    private void AddTagToItem(uint itemId, string tag)
    {
        if (!plugin.Configuration.ItemTags.ContainsKey(itemId))
            plugin.Configuration.ItemTags[itemId] = new List<string>();

        if (!plugin.Configuration.ItemTags[itemId].Contains(tag))
        {
            plugin.Configuration.ItemTags[itemId].Add(tag);
            plugin.Configuration.Save();
            RebuildTagCache();
            isDirty = true;
        }
    }

    // "Active Tags" line of the selected item panel, same overflow handling as the mods row
    private void RenderActiveTagsRow(List<string> currentTags, uint itemId)
    {
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted("Active Tags:");
        ImGui.SameLine(0, 8f);

        if (currentTags == null || currentTags.Count == 0)
        {
            ImGui.TextDisabled("-");
            return;
        }

        float availWidth = ImGui.GetContentRegionAvail().X;
        float currentLineWidth = 0f;
        int overflowStartIndex = -1;

        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 6.0f);
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(6, 2));

        for (int i = 0; i < currentTags.Count; i++)
        {
            string tag = currentTags[i];
            float badgeWidth = ImGui.CalcTextSize($"{tag} ×").X + 12f;

            if (currentLineWidth + badgeWidth > availWidth - 75f && i < currentTags.Count - 1)
            {
                overflowStartIndex = i;
                break;
            }

            if (DrawTagBadge(tag, itemId, currentTags, i))
            {
                break;
            }
            currentLineWidth += badgeWidth + 4f;

            // CSÁK AKKOR tegyen SameLine-t, ha van még következő tag az aktuális sorban
            if (i < currentTags.Count - 1)
            {
                ImGui.SameLine(0, 4f);
            }
        }

        if (overflowStartIndex != -1)
        {
            ImGui.SameLine(0, 4f);
            int remainingCount = currentTags.Count - overflowStartIndex;
            ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.25f, 0.25f, 0.30f, 0.90f));
            if (ImGui.Button($"+{remainingCount} more##TagOverflowBtn"))
            {
                ImGui.OpenPopup("ActiveTagsOverflowPopup");
            }
            ImGui.PopStyleColor();

            if (ImGui.BeginPopup("ActiveTagsOverflowPopup"))
            {
                ImGui.TextDisabled("-- Additional Active Tags --");
                ImGui.Separator();

                ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 6.0f);
                ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(6, 2));

                for (int k = overflowStartIndex; k < currentTags.Count; k++)
                {
                    string tag = currentTags[k];
                    if (DrawTagBadge(tag, itemId, currentTags, 500 + k))
                    {
                        break;
                    }
                    if (k < currentTags.Count - 1) ImGui.SameLine(0, 4f);
                }

                ImGui.PopStyleVar(2);
                ImGui.EndPopup();
            }
        }

        ImGui.PopStyleVar(2);
    }

    // badge with an x: left or right click removes the tag from the item. Returns true if it did.
    private bool DrawTagBadge(string tag, uint itemId, List<string> currentTags, int index)
    {
        bool removed = false;
        var tagCol = GetTagColor(tag);
        ImGui.PushStyleColor(ImGuiCol.Button, tagCol);
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, tagCol with { W = Math.Min(1.0f, tagCol.W + 0.15f) });
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, tagCol with { W = 1.0f });

        ImGui.PushID($"tag_badge_{itemId}_{index}");
        if (ImGui.Button($"{tag} ×") || ImGui.IsItemClicked(ImGuiMouseButton.Right))
        {
            currentTags.Remove(tag);
            if (currentTags.Count == 0) plugin.Configuration.ItemTags.Remove(itemId);
            plugin.Configuration.Save();
            RebuildTagCache();
            isDirty = true;
            removed = true;
        }

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("Click to remove tag");
        }

        ImGui.PopID();
        ImGui.PopStyleColor(3);
        return removed;
    }

    // counts how many items use each tag; tags that only exist as a colour entry (created, not assigned yet) show up with 0
    private void RefreshTagManagerCache()
    {
        if (!tagManagerCacheDirty) return;

        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var kvp in plugin.Configuration.ItemTags)
        {
            if (kvp.Value == null) continue;
            foreach (var tag in kvp.Value)
            {
                if (!counts.ContainsKey(tag)) counts[tag] = 0;
                counts[tag]++;
            }
        }

        foreach (var tag in cachedUniqueTags)
        {
            if (!counts.ContainsKey(tag)) counts[tag] = 0;
        }
        foreach (var kvp in plugin.Configuration.TagColors)
        {
            if (!counts.ContainsKey(kvp.Key)) counts[kvp.Key] = 0;
        }

        tagManagerCache.Clear();
        foreach (var kvp in counts)
        {
            tagManagerCache.Add(new TagManagerData
            {
                TagName = kvp.Key,
                EditBuffer = kvp.Key,
                ItemCount = kvp.Value
            });
        }

        tagManagerCache.Sort((a, b) => string.Compare(a.TagName, b.TagName, StringComparison.OrdinalIgnoreCase));
        tagManagerCacheDirty = false;
    }

    // a new tag only lives as a colour entry until it gets assigned to an item
    private void CreateNewTag(string tagName)
    {
        if (string.IsNullOrWhiteSpace(tagName)) return;
        tagName = tagName.Trim();

        if (!plugin.Configuration.TagColors.ContainsKey(tagName))
        {
            plugin.Configuration.TagColors[tagName] = DefaultTagColor;
        }

        if (!cachedUniqueTags.Contains(tagName, StringComparer.OrdinalIgnoreCase))
        {
            cachedUniqueTags.Add(tagName);
            cachedUniqueTags.Sort(StringComparer.OrdinalIgnoreCase);
        }

        plugin.Configuration.Save();
        tagManagerCacheDirty = true;
        isDirty = true;
    }

    // rename everywhere: on every item and in the colour table
    private void RenameTag(string oldName, string newName)
    {
        if (string.IsNullOrWhiteSpace(newName) || oldName == newName) return;
        newName = newName.Trim();

        foreach (var kvp in plugin.Configuration.ItemTags)
        {
            var tags = kvp.Value;
            if (tags != null && tags.Contains(oldName))
            {
                tags.Remove(oldName);
                if (!tags.Contains(newName))
                {
                    tags.Add(newName);
                }
                tags.Sort(StringComparer.OrdinalIgnoreCase);
            }
        }

        if (plugin.Configuration.TagColors.TryGetValue(oldName, out var col))
        {
            plugin.Configuration.TagColors.Remove(oldName);
            plugin.Configuration.TagColors[newName] = col;
        }

        plugin.Configuration.Save();
        tagManagerCacheDirty = true;
        RebuildTagCache();
        isDirty = true;
    }

    // remove from every item, drop items left without tags, drop the colour
    private void DeleteTagCompletely(string tagName)
    {
        foreach (var kvp in plugin.Configuration.ItemTags)
        {
            kvp.Value?.Remove(tagName);
        }

        var keysToRemove = plugin.Configuration.ItemTags.Where(kvp => kvp.Value == null || kvp.Value.Count == 0).Select(kvp => kvp.Key).ToList();
        foreach (var k in keysToRemove)
        {
            plugin.Configuration.ItemTags.Remove(k);
        }

        plugin.Configuration.TagColors.Remove(tagName);
        plugin.Configuration.Save();
        tagManagerCacheDirty = true;
        RebuildTagCache();
        isDirty = true;
    }

    // file name prefixes of the three export categories
    private const string TagsFilePrefix = "item_tags";
    private const string FavoritesFilePrefix = "item_favorites";
    private const string WishlistsFilePrefix = "item_wishlists";
    private static readonly string[] ExportFilePrefixes = { TagsFilePrefix, FavoritesFilePrefix, WishlistsFilePrefix };

    // a typed .json path can hold only one category: with more than one ticked, each gets its own file
    private bool SeveralExportCategoriesSelected =>
        (exportImportTags ? 1 : 0) + (exportImportFavorites ? 1 : 0) + (exportImportWishlists ? 1 : 0) > 1;

    // where an export goes: default folder, custom folder, or the exact .json path that was typed in.
    // separateCategoryFile only matters for a typed .json path: the category then gets its own file
    // next to it (name.json -> name_item_tags.json), so several categories do not overwrite each other.
    private string GetResolvedBackupPath(string filePrefix, bool separateCategoryFile = false)
    {
        string customPath = plugin.Configuration.CustomExportImportPath?.Trim() ?? "";
        string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmm");

        if (string.IsNullOrWhiteSpace(customPath))
        {
            return Path.Combine(Plugin.PluginInterface.ConfigDirectory.FullName, $"{filePrefix}_export_{timestamp}.json");
        }

        if (Directory.Exists(customPath) || !customPath.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
        {
            return Path.Combine(customPath, $"{filePrefix}_export_{timestamp}.json");
        }

        return separateCategoryFile ? GetCategoryFilePath(customPath, filePrefix) : customPath;
    }

    /// <summary>"C:\Backups\name.json" + "item_tags" -> "C:\Backups\name_item_tags.json"</summary>
    private static string GetCategoryFilePath(string jsonFilePath, string filePrefix)
    {
        string directory = Path.GetDirectoryName(jsonFilePath) ?? "";
        string fileName = Path.GetFileNameWithoutExtension(jsonFilePath);
        return Path.Combine(directory, $"{fileName}_{filePrefix}.json");
    }

    /// <summary>The per-category files of a typed .json path that exist on disk (written by an export of several categories).</summary>
    private static IEnumerable<string> GetExistingCategoryFiles(string jsonFilePath)
    {
        foreach (string filePrefix in ExportFilePrefixes)
        {
            string categoryFile = GetCategoryFilePath(jsonFilePath, filePrefix);
            if (File.Exists(categoryFile)) yield return categoryFile;
        }
    }

    private void EnsureDirectoryExists(string filePath)
    {
        string? directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }
    }

    // work out what an import file contains by looking at its keys
    private JsonFileType DetectJsonFileType(string jsonContent)
    {
        try
        {
            var token = Newtonsoft.Json.Linq.JToken.Parse(jsonContent);
            if (token is Newtonsoft.Json.Linq.JObject obj)
            {
                if (obj.ContainsKey("Favorites") || obj.ContainsKey("DoubleFavorites") || obj.ContainsKey("TripleFavorites"))
                    return JsonFileType.Favorites;

                if (obj.ContainsKey("Wishlist") || obj.ContainsKey("DoubleWishlist"))
                    return JsonFileType.Wishlist;

                // Régi/hagyományos Tag-ek JSON formátumának felismerése (kulcsok az Item ID-k)
                var firstProp = obj.Properties().FirstOrDefault();
                if (firstProp != null && uint.TryParse(firstProp.Name, out _) && firstProp.Value is Newtonsoft.Json.Linq.JArray)
                {
                    return JsonFileType.Tags;
                }
            }
        }
        catch { }

        return JsonFileType.Unknown;
    }

    // one json file per ticked category
    private void ExportSelectedDataToFile()
    {
        plugin.BackupManager.FlushAutoSave();

        if (!exportImportTags && !exportImportFavorites && !exportImportWishlists)
        {
            Plugin.ChatGui.Print("[Glamour Tagger Error] No categories selected for export.");
            return;
        }

        try
        {
            if (exportImportTags)
            {
                string targetPath = GetResolvedBackupPath(TagsFilePrefix, SeveralExportCategoriesSelected); 
                EnsureDirectoryExists(targetPath);
                string json = Newtonsoft.Json.JsonConvert.SerializeObject(plugin.Configuration.ItemTags, Newtonsoft.Json.Formatting.Indented);
                File.WriteAllText(targetPath, json);
                Plugin.ChatGui.Print("[Glamour Tagger] Tags exported to: " + targetPath);
            }

            if (exportImportFavorites)
            {
                string targetPath = GetResolvedBackupPath(FavoritesFilePrefix, SeveralExportCategoriesSelected); 
                EnsureDirectoryExists(targetPath);
                var favData = new FavoritesExportData
                {
                    Favorites = plugin.Configuration.Favorites,
                    DoubleFavorites = plugin.Configuration.DoubleFavorites,
                    TripleFavorites = plugin.Configuration.TripleFavorites
                };
                string json = Newtonsoft.Json.JsonConvert.SerializeObject(favData, Newtonsoft.Json.Formatting.Indented);
                File.WriteAllText(targetPath, json);
                Plugin.ChatGui.Print("[Glamour Tagger] Favorites exported to: " + targetPath);
            }

            if (exportImportWishlists)
            {
                string targetPath = GetResolvedBackupPath(WishlistsFilePrefix, SeveralExportCategoriesSelected); 
                EnsureDirectoryExists(targetPath);
                var wlData = new WishlistExportData
                {
                    Wishlist = plugin.Configuration.Wishlist,
                    DoubleWishlist = plugin.Configuration.DoubleWishlist
                };
                string json = Newtonsoft.Json.JsonConvert.SerializeObject(wlData, Newtonsoft.Json.Formatting.Indented);
                File.WriteAllText(targetPath, json);
                Plugin.ChatGui.Print("[Glamour Tagger] Wishlists exported to: " + targetPath);
            }
        }
        catch (Exception ex)
        {
            Plugin.ChatGui.Print("[Glamour Tagger Error] Export failed: " + ex.Message);
        }
    }

    // takes a safety backup first, then merges every matching json file found at the target path
    private void ImportSelectedDataFromFile()
    {
        plugin.BackupManager.CreatePreImportBackup();

        if (!exportImportTags && !exportImportFavorites && !exportImportWishlists)
        {
            Plugin.ChatGui.Print("[Glamour Tagger Error] No categories selected for import.");
            return;
        }

        string targetPath = GetResolvedBackupPath(TagsFilePrefix); 
        List<string> filesToImport = new();

        if (Directory.Exists(targetPath))
        {
            filesToImport.AddRange(Directory.GetFiles(targetPath, "*.json"));
        }
        else
        {
            string? directoryName = Path.GetDirectoryName(targetPath);
            if (!string.IsNullOrEmpty(directoryName) && Directory.Exists(directoryName))
            {
                if (File.Exists(targetPath))
                {
                    filesToImport.Add(targetPath);
                }
                // an export of several categories to a typed .json path wrote one file per category next to it
                filesToImport.AddRange(GetExistingCategoryFiles(targetPath));

                if (filesToImport.Count == 0)
                {
                    filesToImport.AddRange(Directory.GetFiles(directoryName, "*.json"));
                }
            }
            else if (File.Exists(targetPath))
            {
                filesToImport.Add(targetPath);
            }
        }

        if (filesToImport.Count == 0)
        {
            Plugin.ChatGui.Print("[Glamour Tagger Error] No JSON files found near: " + targetPath);
            return;
        }

        // files are read off the main thread, config changes go back to the framework thread
        Task.Run(() =>
        {
            try
            {
                int importedCount = 0;

                foreach (var file in filesToImport)
                {
                    string json = File.ReadAllText(file);
                    var fileType = DetectJsonFileType(json);
                    string fileName = Path.GetFileName(file);

                    if (fileType == JsonFileType.Tags && exportImportTags)
                    {
                        var importedTags = Newtonsoft.Json.JsonConvert.DeserializeObject<Dictionary<uint, List<string>>>(json);
                        if (importedTags != null)
                        {
                            Plugin.Framework.RunOnFrameworkThread(() =>
                            {
                                // real merge: imported tags are added to the tags an item already has
                                int addedTags = plugin.Configuration.MergeItemTags(importedTags);
                                plugin.Configuration.Save();
                                RebuildTagCache();
                                isDirty = true;
                                tagManagerCacheDirty = true;
                                Plugin.ChatGui.Print($"[Glamour Tagger] Imported Tags from: {fileName} ({addedTags} new tag(s) added)");
                            });
                            importedCount++;
                        }
                    }
                    else if (fileType == JsonFileType.Favorites && exportImportFavorites)
                    {
                        var importedFavs = Newtonsoft.Json.JsonConvert.DeserializeObject<FavoritesExportData>(json);
                        if (importedFavs != null)
                        {
                            Plugin.Framework.RunOnFrameworkThread(() =>
                            {
                                if (importedFavs.Favorites != null) plugin.Configuration.Favorites.UnionWith(importedFavs.Favorites);
                                if (importedFavs.DoubleFavorites != null) plugin.Configuration.DoubleFavorites.UnionWith(importedFavs.DoubleFavorites);
                                if (importedFavs.TripleFavorites != null) plugin.Configuration.TripleFavorites.UnionWith(importedFavs.TripleFavorites);
                                plugin.Configuration.Save();
                                isDirty = true;
                                Plugin.ChatGui.Print($"[Glamour Tagger] Imported Favorites from: {fileName}");
                            });
                            importedCount++;
                        }
                    }
                    else if (fileType == JsonFileType.Wishlist && exportImportWishlists)
                    {
                        var importedWl = Newtonsoft.Json.JsonConvert.DeserializeObject<WishlistExportData>(json);
                        if (importedWl != null)
                        {
                            Plugin.Framework.RunOnFrameworkThread(() =>
                            {
                                if (importedWl.Wishlist != null) plugin.Configuration.Wishlist.UnionWith(importedWl.Wishlist);
                                if (importedWl.DoubleWishlist != null) plugin.Configuration.DoubleWishlist.UnionWith(importedWl.DoubleWishlist);
                                plugin.Configuration.Save();
                                isDirty = true;
                                Plugin.ChatGui.Print($"[Glamour Tagger] Imported Wishlist from: {fileName}");
                            });
                            importedCount++;
                        }
                    }
                }

                if (importedCount == 0)
                {
                    Plugin.Framework.RunOnFrameworkThread(() =>
                    {
                        Plugin.ChatGui.Print("[Glamour Tagger] No matching files found for the selected import options.");
                    });
                }
            }
            catch (Exception ex)
            {
                Plugin.Framework.RunOnFrameworkThread(() =>
                {
                    Plugin.ChatGui.Print("[Glamour Tagger Error] Import failed: " + ex.Message);
                });
            }
        });

        plugin.Configuration.Save();
    }
}
