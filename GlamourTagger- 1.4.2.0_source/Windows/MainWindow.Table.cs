using Dalamud.Bindings.ImGui;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Interface;
using Lumina.Excel.Sheets;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Interface.Textures;

namespace GlamourTagger.Windows;

public partial class MainWindow
{
    // --- filter / sort / selection state of the item table ---
    private long lastCycleTime = 0;

    private readonly HashSet<string> selectedJobs = new();
    private readonly HashSet<int> selectedDyes = new();
    private bool filterUntagged = false;
    private readonly Dictionary<string, int> selectedTags = new();
    private int tagFilterMode = 0;

    // one equippable item, computed once from the Item sheet
    private record ItemRow(
    uint Id,
    string IdString,
    string Name,
    ushort Icon,
    string SlotName,
    uint EquipSlotCategory,
    ushort ModelMain,
    ushort ModelVariant,
    string ModelString,
    string JobString,
    HashSet<string> JobAbbrs,
    byte DyeCount
    );

    // cachedItems = everything, filteredItems = what the table shows right now
    private readonly List<ItemRow> cachedItems = new();
    private List<ItemRow> filteredItems = new();
    private readonly List<bool> filteredItemToggles = new();
    private readonly List<string> cachedUniqueTags = new();

    // flag column filters: 0 = all, 1 = only flagged, 2 = only unflagged
    private int favFilterMode = 0;
    private int dfFilterMode = 0;
    private int tfFilterMode = 0;
    private int wlFilterMode = 0;
    private int dwlFilterMode = 0;
    private int modFilterMode = 0;

    private string nameFilter = "";
    private string idFilter = "";
    private string modelFilter = "";
    private string tagFilterSearch = "";
    private string assignTagSearch = "";

    private int sortColumnIndex = -1; // -1 = nincs aktív rendezés
    private bool sortAscending = true; // true = A-Z (▼), false = Z-A (▲)

    private bool onlyOnePerModelType = false;
    private bool ignoreVariants = false;

    private uint selectedItemId = 0;
    private string selectedItemName = "";
    private string newTagInput = "";
    private bool scrollToSelectedItem = false;
    private bool resetScrollToTop = false;

    private static readonly string[] AvailableJobs = {
        "PLD", "WAR", "DRK", "GNB", "MNK", "DRG", "NIN", "SAM", "RPR", "VPR",
        "BRD", "MCH", "DNC", "BLM", "SMN", "RDM", "PCT", "WHM", "SCH", "AST",
        "SGE", "BST", "BLU", "GLA", "MRD", "PGL", "LNC", "ROG", "ARC", "THM",
        "ACN", "CNJ", "CRP", "BSM", "ARM", "GSM", "LTW", "WVR", "ALC", "CUL",
        "MIN", "BTN", "FSH"
    };

    // runs once in the constructor: every item that has an equip slot and a model
    private void InitializeItemCache()
    {
        var itemSheet = Plugin.DataManager.GetExcelSheet<Item>();
        if (itemSheet == null) return;

        foreach (var item in itemSheet)
        {
            if (item.EquipSlotCategory.RowId == 0 || string.IsNullOrEmpty(item.Name.ToString()))
                continue;

            string slotName = GetSlotName(item.EquipSlotCategory.RowId);
            // low 16 bits = model id, next 16 bits = variant
            ushort modelMain = (ushort)item.ModelMain;
            ushort modelVariant = (ushort)(item.ModelMain >> 16);

            if (modelMain == 0 && modelVariant == 0)
                continue;

            string modelString = $"{modelMain}-{modelVariant}";
            string jobStr = item.ClassJobCategory.ValueNullable?.Name.ToString() ?? "All Classes";

            // job abbreviations for the job filter ("All Classes" = every job)
            var jobAbbrs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (jobStr.Equals("All Classes", StringComparison.OrdinalIgnoreCase) || string.IsNullOrEmpty(jobStr))
            {
                foreach (var j in AvailableJobs) jobAbbrs.Add(j);
            }
            else
            {
                var parts = jobStr.Split(' ', ',', '/');
                foreach (var p in parts)
                {
                    if (!string.IsNullOrWhiteSpace(p)) jobAbbrs.Add(p.Trim());
                }
            }

            cachedItems.Add(new ItemRow(
                item.RowId,
                item.RowId.ToString(),
                item.Name.ToString(),
                item.Icon,
                slotName,
                item.EquipSlotCategory.RowId,
                modelMain,
                modelVariant,
                modelString,
                jobStr,
                jobAbbrs,
                item.DyeCount
            ));
        }
    }

    // header cell of a column with a filter popup: label, sort button, left-click opens the popup, middle-click clears
    private void RenderHeaderWithPopup(
        int colIndex,
        string label,
        string popupId,
        bool isActiveFilter,
        System.Action clearAction,
        bool centered = true)
    {
        if (isActiveFilter)
        {
            uint darkBlueCol = ImGui.GetColorU32(new Vector4(0.12f, 0.25f, 0.50f, 0.45f));
            ImGui.TableSetBgColor(ImGuiTableBgTarget.CellBg, darkBlueCol);
        }

        float cellStartX = ImGui.GetCursorPosX();
        float availWidth = ImGui.GetContentRegionAvail().X;
        float lineHeight = ImGui.GetTextLineHeight();
        float cellHeight = lineHeight * 2.2f + 4f;

        Vector2 cellMin = ImGui.GetCursorScreenPos();
        Vector2 cellMax = new Vector2(cellMin.X + availWidth, cellMin.Y + cellHeight);

        // 1. GOMB TERÜLETÉNEK KISZÁMÍTÁSA KÉPERNYŐ KOORDINÁTÁKBAN
        float orderBtnWidth = 18f;
        float btnX = cellStartX + Math.Max(0f, availWidth - orderBtnWidth);

        Vector2 btnMin = new Vector2(cellMin.X + Math.Max(0f, availWidth - orderBtnWidth), cellMin.Y);
        Vector2 btnMax = new Vector2(cellMin.X + availWidth, cellMin.Y + lineHeight);

        // Nyers fizikai ellenőrzése annak, hogy az egér a gomb felett van-e
        bool isMouseOverOrderArea = ImGui.IsMouseHoveringRect(btnMin, btnMax, false);

        // 1. SZÖVEG KIRAJZOLÁSA ELŐSZÖR (így ez kerül az alsó rétegre)
        float textWidth = ImGui.CalcTextSize(label).X;
        float textX = centered
            ? cellStartX + (availWidth - textWidth) * 0.5f
            : cellStartX;

        float itemSpacingY = 8f;

        ImGui.NewLine();
        ImGui.SetCursorPosX(textX);
        ImGui.SetCursorPosY(headerRow2StartY + 4f);

        ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.9f, 0.9f, 0.68f, 0.95f));
        ImGui.TextUnformatted(label);
        ImGui.PopStyleColor();

        // 2. RENDEZŐ GOMB (HÁROMSZÖG) KIRAJZOLÁSA UTÁNA (a felső rétegre kerül)
        ImGui.SetCursorPosX(btnX);
        ImGui.SetCursorPosY(headerRowStartY);

        bool orderClicked = DrawOrderButton(colIndex);
        bool orderHovered = ImGui.IsItemHovered();

        // 3. Cella interakciók
        bool isCellHovered = ImGui.IsMouseHoveringRect(cellMin, cellMax, false);

        // A cella KIZÁRÓLAG akkor reagál, ha az egér NICS a gomb téglalapján belül!
        if (!anyPopupOpen && isCellHovered && !isMouseOverOrderArea)
        {
            if (ImGui.IsMouseClicked(ImGuiMouseButton.Left))
            {
                ImGui.OpenPopup(popupId);
            }
            else if (ImGui.IsMouseClicked(ImGuiMouseButton.Middle))
            {
                clearAction();
                MarkFilterDirty();
            }

            ImGui.SetTooltip("Left-Click: Open Filter Menu\nMiddle-Click: Clear Filter");
        }
    }

    // A fájl/osztály elején deklarálható a szín és transzparencia:
    // (R: 0.7, G: 0.7, B: 0.7, Alpha: 0.40 -> 40%-os halvány szürke)
    private static readonly Vector4 SortIconColor = new(0.7f, 0.7f, 0.7f, 0.40f);

    private bool DrawOrderButton(int colIndex)
    {
        // Fix szélesség (18px), hogy a bogyó/dupla nyíl és a háromszög alatt is pontosan ugyanakkora legyen
        Vector2 btnSize = new(18f, ImGui.GetTextLineHeight());

        // FontAwesome ikon kiválasztása
        FontAwesomeIcon faIcon = (sortColumnIndex == colIndex)
        ? (sortAscending ? FontAwesomeIcon.SortDown : FontAwesomeIcon.SortUp)
        : FontAwesomeIcon.Sort;

        string iconStr = faIcon.ToIconString();

        // Háttérszínek teljes nullázása (hover és active állapotban sem látszik keret/kijelölés)
        ImGui.PushStyleColor(ImGuiCol.Button, Vector4.Zero);
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, Vector4.Zero);
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, Vector4.Zero);

        // Ikon színe és átlátszósága
        ImGui.PushStyleColor(ImGuiCol.Text, SortIconColor);
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, Vector2.Zero);

        // Dalamud FontAwesome betűtípus aktiválása az ikonhoz
        ImGui.PushFont(UiBuilder.IconFont);

        bool clicked = ImGui.Button($"{iconStr}##Sort_{colIndex}", btnSize);

        ImGui.PopFont();
        ImGui.PopStyleVar();
        ImGui.PopStyleColor(4);

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("Click to sort (A-Z / Z-A)");
        }

        if (clicked)
        {
            ToggleSort(colIndex);
        }

        return clicked;
    }

    // Fixed width of the "current job" quick-search button in the Item Name header.
    private const float JobSearchBtnWidth = 20f;

    // Abbreviation of the job the local player is currently on ("PLD", "WHM", ...).
    // Empty string when not logged in or the sheet row cannot be resolved.
    private static string GetCurrentJobAbbreviation()
    {
        try
        {
            if (!Plugin.PlayerState.IsLoaded) return string.Empty;
            return Plugin.PlayerState.ClassJob.ValueNullable?.Abbreviation.ToString() ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    // Base id of the in-game class/job icon set. The icon id is JobIconBase + ClassJob RowId,
    // e.g. 62000 + 1 = 62001 = Gladiator.
    private const uint JobIconBase = 62000;

    // Padding inside the button, so the icon doesn't touch the frame.
    private const float JobIconInset = 0f;

    // Per-icon fine tuning for the in-game class/job icons (62001 - 62043).
    // Index = ClassJob RowId, so index 1 = icon 62001.
    // X: + right, - left.  Y: + down, - up.  (pixels)
    // Z: scale multiplier on top of the auto-fit. 1.0 = unchanged, 1.2 = 20% bigger, 0.9 = 10% smaller.
    private static readonly Vector3[] JobIconAdjust = new Vector3[44]
    {
        new(0f, 0f, 1f), //     -   (index 0, Adventurer - unused)
        new(0f, 1.5f, 1f), // 62001  GLA
        new(0f, 1f, 1f), // 62002  PGL
        new(0f, 1.5f, 1f), // 62003  MRD
        new(0f, 0.5f, 1.15f), // 62004  LNC
        new(0f, 1.5f, 1.05f), // 62005  ARC
        new(0f, 1.5f, 1.1f), // 62006  CNJ
        new(0f, 1f, 1.1f), // 62007  THM
        new(0f, 1f, 1.1f), // 62008  CRP
        new(0f, 1f, 1.1f), // 62009  BSM
        new(0f, 1f, 1f), // 62010  ARM
        new(0f, 0.5f, 1.15f), // 62011  GSM
        new(0f, 1f, 1f), // 62012  LTW
        new(0f, 1f, 1.1f), // 62013  WVR
        new(-0.5f, 1f, 1f), // 62014  ALC
        new(0f, 1.5f, 1f), // 62015  CUL
        new(0f, 1f, 1.05f), // 62016  MIN
        new(0f, 1f, 1.05f), // 62017  BTN
        new(0f, 1.5f, 1f), // 62018  FSH
        new(0f, 1.5f, 1f), // 62019  PLD
        new(0f, 1.5f, 1f), // 62020  MNK
        new(0f, 1f, 1f), // 62021  WAR
        new(0f, 1f, 1.15f), // 62022  DRG
        new(-0.5f, 1f, 1f), // 62023  BRD
        new(0f, 2f, 1.2f), // 62024  WHM
        new(0f, 1f, 1f), // 62025  BLM
        new(0f, 0.5f, 1.1f), // 62026  ACN
        new(-0.5f, 1f, 1.15f), // 62027  SMN
        new(0f, 0f, 1f), // 62028  SCH
        new(0f, 1f, 1f), // 62029  ROG
        new(0.5f, 1f, 1f), // 62030  NIN
        new(0f, 1.5f, 1.15f), // 62031  MCH
        new(0f, 2f, 1.2f), // 62032  DRK
        new(0f, 2f, 1.05f), // 62033  AST
        new(0f, 1f, 1f), // 62034  SAM
        new(0f, 1f, 1.15f), // 62035  RDM
        new(0f, 2f, 1.05f), // 62036  BLU
        new(0f, 1.05f, 1f), // 62037  GNB
        new(0f, 1f, 1.05f), // 62038  DNC
        new(0f, 1f, 1f), // 62039  RPR
        new(0.2f, 1.5f, 1.05f), // 62040  SGE
        new(0f, 0.5f, 1.1f), // 62041  VPR
        new(0f, 0f, 1.05f), // 62042  PCT
        new(0.5f, 0f, 1f), // 62043  BST
    };

    private static Vector3 GetJobIconAdjust(uint rowId)
        => rowId < (uint)JobIconAdjust.Length ? JobIconAdjust[rowId] : new Vector3(0f, 0f, 1f);

    // small button with the current job's icon: puts j:<JOB> into the search box
    private void DrawCurrentJobSearchButton()
    {
        uint jobRowId = 0;
        string abbr = string.Empty;

        try
        {
            if (Plugin.PlayerState.IsLoaded)
            {
                jobRowId = Plugin.PlayerState.ClassJob.RowId;
                abbr = Plugin.PlayerState.ClassJob.ValueNullable?.Abbreviation.ToString() ?? string.Empty;
            }
        }
        catch
        {
            jobRowId = 0;
            abbr = string.Empty;
        }

        var jobIcon = jobRowId > 0
            ? Plugin.TextureProvider.GetFromGameIcon(new GameIconLookup(JobIconBase + jobRowId)).GetWrapOrDefault()
            : null;

        // If the chosen set has no icon for this row, fall back to the plain 62000 set.
        if (jobIcon == null && jobRowId > 0 && JobIconBase != 62000)
            jobIcon = Plugin.TextureProvider.GetFromGameIcon(new GameIconLookup(62000 + jobRowId)).GetWrapOrDefault();

        Vector2 btnSize = new(JobSearchBtnWidth, JobSearchBtnWidth);
        Vector2 p0 = ImGui.GetCursorScreenPos();

        bool clicked = ImGui.InvisibleButton("##JobSearchBtn", btnSize);
        bool hovered = ImGui.IsItemHovered();
        bool active = ImGui.IsItemActive();

        var dl = ImGui.GetWindowDrawList();

        // Same dark red frame as the Revert button, drawn manually so we control the icon placement.
        uint bgCol = ImGui.GetColorU32(
            active ? new Vector4(0.55f, 0.20f, 0.20f, 0.95f)
          : hovered ? new Vector4(0.45f, 0.15f, 0.15f, 0.95f)
                    : new Vector4(0.35f, 0.10f, 0.10f, 0.95f));
        dl.AddRectFilled(p0, p0 + btnSize, bgCol, 2f);

        if (jobIcon != null && jobIcon.Width > 0 && jobIcon.Height > 0)
        {
            // Fit the texture inside the button keeping its aspect ratio, then center it.
            float boxW = btnSize.X - JobIconInset * 2f;
            float boxH = btnSize.Y - JobIconInset * 2f;
            Vector3 adj = GetJobIconAdjust(jobRowId);
            float userScale = adj.Z > 0f ? adj.Z : 1f;

            float fit = Math.Min(boxW / jobIcon.Width, boxH / jobIcon.Height) * userScale;

            Vector2 drawSize = new(jobIcon.Width * fit, jobIcon.Height * fit);
            Vector2 imgMin = p0 + (btnSize - drawSize) * 0.5f + new Vector2(adj.X, adj.Y);

            dl.AddImage(jobIcon.Handle, imgMin, imgMin + drawSize);
        }
        else
        {
            // FontAwesome fallback (not logged in / icon not cached yet), centered the same way.
            ImGui.PushFont(UiBuilder.IconFont);
            string fa = FontAwesomeIcon.UserTag.ToIconString();
            Vector2 ts = ImGui.CalcTextSize(fa);
            dl.AddText(p0 + (btnSize - ts) * 0.5f, ImGui.GetColorU32(ImGuiCol.Text), fa);
            ImGui.PopFont();
        }

        if (hovered)
        {
            ImGui.SetTooltip(string.IsNullOrEmpty(abbr)
                ? "Search items for your current job (character not available)"
                : $"Search items for your current job (j:{abbr})\nClick again to clear the search.");
        }

        if (!clicked || string.IsNullOrEmpty(abbr)) return;

        string query = $"j:{abbr}";
        // Toggle: clicking again while the same query is active clears the search box.
        nameFilter = nameFilter.Equals(query, StringComparison.OrdinalIgnoreCase) ? string.Empty : query;
        MarkFilterDirty();
    }

    // "Clear all filter" button
    private void ResetAllFilters()
    {
        favFilterMode = 0;
        dfFilterMode = 0;
        tfFilterMode = 0;
        wlFilterMode = 0;
        dwlFilterMode = 0;
        modFilterMode = 0;

        nameFilter = "";
        idFilter = "";
        modelFilter = "";
        selectedModFilters.Clear();
        selectedModFilters.Add(-1); selectedModFilters.Add(0); selectedModFilters.Add(1); selectedModFilters.Add(2);

        selectedSlots.Clear();
        foreach (var slot in AvailableSlots) selectedSlots.Add(slot);
        ResetDyeTargetsToFilter(); // dye targets follow the filter again after a full reset

        selectedJobs.Clear();
        foreach (var job in AvailableJobs) selectedJobs.Add(job);

        selectedDyes.Clear();
        selectedDyes.Add(0); selectedDyes.Add(1); selectedDyes.Add(2);

        filterUntagged = false;
        selectedTags.Clear();
        tagFilterMode = 0;

        sortColumnIndex = -1;
        sortAscending = true;

        onlyOnePerModelType = false;
        ignoreVariants = false;

        isDirty = true;
        resetScrollToTop = true;
    }

    // button with a FontAwesome icon in front of the text, centred as one block
    private bool DrawIconButton(string id, FontAwesomeIcon icon, string text, Vector2 size)
    {
        bool clicked = ImGui.Button($"##{id}", size);

        Vector2 rectMin = ImGui.GetItemRectMin();
        Vector2 rectSize = ImGui.GetItemRectSize();

        string iconStr = icon.ToIconString();

        ImGui.PushFont(UiBuilder.IconFont);
        float iconWidth = ImGui.CalcTextSize(iconStr).X;
        ImGui.PopFont();

        float textWidth = ImGui.CalcTextSize(text).X;
        float spacing = 6f;
        float totalWidth = iconWidth + spacing + textWidth;

        float startX = rectMin.X + (rectSize.X - totalWidth) / 2f;
        float startY = rectMin.Y + (rectSize.Y - ImGui.GetTextLineHeight()) / 2f;

        var drawList = ImGui.GetWindowDrawList();
        uint textColor = ImGui.GetColorU32(ImGuiCol.Text);

        drawList.AddText(UiBuilder.IconFont, ImGui.GetFontSize(), new Vector2(startX, startY), textColor, iconStr);
        drawList.AddText(new Vector2(startX + iconWidth + spacing, startY), textColor, text);

        return clicked;
    }

    private void SelectAndLinkItem(ItemRow item)
    {
        selectedItemId = item.Id;
        selectedItemName = item.Name;
        selectedItemSlot = GetGlamourerSlotId(item.EquipSlotCategory);
        LinkItemToChat(item.Id);
    }

    private void DrawRightText(string text)
    {
        float availWidth = ImGui.GetContentRegionAvail().X;
        float textWidth = ImGui.CalcTextSize(text).X;
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + Math.Max(0f, availWidth - textWidth));
        ImGui.TextUnformatted(text);
    }

    // one tag badge inside a table cell. Right-click = add another tag, Ctrl+Shift+Right-click = remove this one
    private (bool tagRemoved, bool openAddPopup) DrawTagCellBadge(string tag, ItemRow item, List<string> itemTags, int index)
    {
        bool tagRemoved = false;
        bool openAddPopup = false;
        var tagCol = GetTagColor(tag);

        ImGui.PushStyleColor(ImGuiCol.Button, tagCol);
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, tagCol with { W = Math.Min(1.0f, tagCol.W + 0.15f) });
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, tagCol with { W = 1.0f });

        ImGui.PushID($"cell_tag_{item.Id}_{index}");
        ImGui.Button(tag);

        if (ImGui.IsItemClicked(ImGuiMouseButton.Left))
        {
            selectedItemId = item.Id;
            selectedItemName = item.Name;
            selectedItemSlot = GetGlamourerSlotId(item.EquipSlotCategory);
        }

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("Right-Click: Add existing tag\nCtrl+Shift+Right-Click: Remove tag");
        }

        var io = ImGui.GetIO();
        if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
        {
            if (io.KeyCtrl && io.KeyShift)
            {
                itemTags.Remove(tag);
                if (itemTags.Count == 0) plugin.Configuration.ItemTags.Remove(item.Id);
                plugin.Configuration.Save();
                RebuildTagCache();
                isDirty = true;
                tagRemoved = true;
            }
            else
            {
                openAddPopup = true;
            }
        }

        ImGui.PopID();
        ImGui.PopStyleColor(3);

        return (tagRemoved, openAddPopup);
    }

    // Tags column cell: badges, "+N more" overflow popup, and the add-tag popup
    private void DrawTagCell(ItemRow item, float rowHeight)
    {
        string addPopupId = $"AddTagCellPopup_{item.Id}";
        string overflowPopupId = $"TagCellOverflowPopup_{item.Id}";
        bool shouldOpenAddPopup = false;

        float availWidth = ImGui.GetContentRegionAvail().X;
        if (availWidth <= 5) return;

        Vector2 cellStart = ImGui.GetCursorScreenPos();

        // 1. Draw full cell background dummy to allow clicking empty space on tagged and untagged cells
        ImGui.Dummy(new Vector2(availWidth, rowHeight));

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("Right-Click: Add existing tag");
        }
        if (!anyPopupOpen && ImGui.IsItemClicked(ImGuiMouseButton.Left))
        {
            selectedItemId = item.Id;
            selectedItemName = item.Name;
            selectedItemSlot = GetGlamourerSlotId(item.EquipSlotCategory);
        }
        if (!anyPopupOpen && ImGui.IsItemClicked(ImGuiMouseButton.Right))
        {
            shouldOpenAddPopup = true;
        }

        // 2. Reset cursor position to overlay badges on top of the cell dummy
        ImGui.SetCursorScreenPos(cellStart);

        if (plugin.Configuration.ItemTags.TryGetValue(item.Id, out var itemTags) && itemTags.Count > 0)
        {
            ImGui.SetCursorPosY(ImGui.GetCursorPosY() + Math.Max(0f, (rowHeight - 22f) * 0.5f));

            float currentLineWidth = 0f;
            int overflowStartIndex = -1;
            float overflowBtnWidth = 55f;

            ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 6.0f);
            ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(6, 2));

            for (int t = 0; t < itemTags.Count; t++)
            {
                string tag = itemTags[t];
                float badgeWidth = ImGui.CalcTextSize(tag).X + 12f;

                if (t > 0 && currentLineWidth + badgeWidth > availWidth - overflowBtnWidth)
                {
                    overflowStartIndex = t;
                    break;
                }

                var (tagRemoved, openAdd) = DrawTagCellBadge(tag, item, itemTags, t);
                if (openAdd) shouldOpenAddPopup = true;

                if (tagRemoved)
                {
                    ImGui.PopStyleVar(2);
                    return;
                }

                currentLineWidth += badgeWidth + 4f;

                if (t < itemTags.Count - 1 && overflowStartIndex == -1)
                {
                    ImGui.SameLine(0, 4f);
                }
            }

            if (overflowStartIndex != -1)
            {
                ImGui.SameLine(0, 4f);
                int remainingCount = itemTags.Count - overflowStartIndex;

                ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.25f, 0.25f, 0.30f, 0.90f));
                if (ImGui.Button($"+{remainingCount} more##CellOverflow_{item.Id}"))
                {
                    ImGui.OpenPopup(overflowPopupId);
                }
                if (ImGui.IsItemHovered())
                {
                    ImGui.SetTooltip("Left-Click: Show remaining tags\nRight-Click: Add existing tag");
                }
                if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
                {
                    shouldOpenAddPopup = true;
                }
                ImGui.PopStyleColor();

                if (ImGui.BeginPopup(overflowPopupId))
                {
                    ImGui.TextDisabled("-- Additional Tags --");
                    ImGui.Separator();

                    ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 6.0f);
                    ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(6, 2));

                    for (int o = overflowStartIndex; o < itemTags.Count; o++)
                    {
                        string tag = itemTags[o];
                        var (tagRemoved, openAdd) = DrawTagCellBadge(tag, item, itemTags, o + 500);
                        if (openAdd) shouldOpenAddPopup = true;

                        if (tagRemoved)
                        {
                            ImGui.PopStyleVar(2);
                            ImGui.EndPopup();
                            ImGui.PopStyleVar(2);
                            return;
                        }

                        if (o < itemTags.Count - 1)
                            ImGui.SameLine(0, 4f);
                    }

                    ImGui.PopStyleVar(2);
                    ImGui.EndPopup();
                }
            }

            ImGui.PopStyleVar(2);
        }

        // opened down here so it works the same from the empty area, a badge, or the overflow button
        if (shouldOpenAddPopup)
        {
            ImGui.OpenPopup(addPopupId);
        }

        if (ImGui.BeginPopup(addPopupId))
        {
            ImGui.TextDisabled("-- Select Tag to Add --");
            if (cachedUniqueTags.Count == 0)
            {
                ImGui.TextUnformatted("No existing tags available");
            }
            else
            {
                for (int i = 0; i < cachedUniqueTags.Count; i++)
                {
                    string existingTag = cachedUniqueTags[i];
                    bool isAlreadyAdded = itemTags != null && itemTags.Contains(existingTag);
                    if (ImGui.Selectable(existingTag, isAlreadyAdded))
                    {
                        if (!isAlreadyAdded)
                        {
                            AddTagToItem(item.Id, existingTag);
                        }
                    }
                }
            }
            ImGui.EndPopup();
        }
    }

    // preview the item with whichever mode is selected
    private void ApplyCurrentItem(ItemRow item)
    {
        if (applyMode == 0) // Glamourer Preview
        {
            byte slotId = GetGlamourerSlotId(item.EquipSlotCategory);
            plugin.ApplyItemWithGlamourer(item.Id, slotId);
        }
        else // Fitting Room Try On
        {
            Plugin.Framework.RunOnFrameworkThread(() =>
            {
                plugin.TryOnItem(item.Id);
            });
        }
    }

    // mouse wheel over the "Scroll Here" button: step through the filtered list and preview each item
    private void CycleSelectedItem(int step)
    {
        if (filteredItems.Count == 0) return;

        // small throttle so one wheel flick doesn't fire a dozen previews
        long now = Environment.TickCount64;
        if (now - lastCycleTime < 80) return;
        lastCycleTime = now;

        int currentIndex = filteredItems.FindIndex(x => x.Id == selectedItemId);
        int newIndex;

        if (currentIndex < 0)
        {
            newIndex = step > 0 ? 0 : filteredItems.Count - 1;
        }
        else
        {
            newIndex = Math.Clamp(currentIndex + step, 0, filteredItems.Count - 1);
        }

        var item = filteredItems[newIndex];
        selectedItemId = item.Id;
        selectedItemName = item.Name;
        selectedItemSlot = GetGlamourerSlotId(item.EquipSlotCategory);

        ApplyCurrentItem(item);
    }

    // Applies every active filter to cachedItems, then dedup (one per model), sort and row striping.
    // Only runs when isDirty is set, not every frame.
    private void RebuildFilteredList()
    {
        bool hasNameFilter = !string.IsNullOrEmpty(nameFilter);

        string searchPrefix = "";
        string searchQuery = nameFilter;

        // "prefix:query" in the search box searches something other than the name (t:, p:, j:, id:, m:)
        if (hasNameFilter)
        {
            int colonIdx = nameFilter.IndexOf(':');
            if (colonIdx > 0 && colonIdx < nameFilter.Length - 1)
            {
                searchPrefix = nameFilter.Substring(0, colonIdx).ToLowerInvariant();
                searchQuery = nameFilter.Substring(colonIdx + 1);
            }
        }

        bool hasIdFilter = !string.IsNullOrEmpty(idFilter);
        bool hasModelFilter = !string.IsNullOrEmpty(modelFilter);
        bool allSlotsSelected = selectedSlots.Count == AvailableSlots.Length;
        bool allJobsSelected = selectedJobs.Count == AvailableJobs.Length;
        bool hasTagFilter = filterUntagged || selectedTags.Count > 0;

        var resultList = new List<ItemRow>(cachedItems.Count);

        plugin.PenumbraIpc.RefreshIfNeeded();

        foreach (var item in cachedItems)
        {
            // Lekérjük, hogy az adott tárgy moddolva van-e
            int modState = GetItemModState(item.Id, item.Name, out var actMods, out var demMods, out var unusableMods);
            var affectingMods = actMods.Concat(unusableMods).Concat(demMods).ToList();
            // Modded Filter (M oszlop) ellenőrzése (Mode 1 sees Demodded(1) & Modded(2). Mode 2 sees Unmodded(0))
            if (unusableMods.Count > 0)
            {
                affectingMods.Add("unusable");
                affectingMods.Add("[unusable]");
            }

            if (demMods.Count > 0)
            {
                affectingMods.Add("demodded");
                affectingMods.Add("[demodded]");
            }

            if (!selectedModFilters.Contains(modState)) continue;

            // flag columns
            if (!CheckFavState(plugin.Configuration.Favorites, item.Id, favFilterMode)) continue;
            if (!CheckFavState(plugin.Configuration.DoubleFavorites, item.Id, dfFilterMode)) continue;
            if (!CheckFavState(plugin.Configuration.TripleFavorites, item.Id, tfFilterMode)) continue;
            if (!CheckFavState(plugin.Configuration.Wishlist, item.Id, wlFilterMode)) continue;
            if (!CheckFavState(plugin.Configuration.DoubleWishlist, item.Id, dwlFilterMode)) continue;

            if (hasNameFilter)
            {
                if (searchPrefix == "t" || searchPrefix == "tag")
                {
                    bool hasMatchingTag = plugin.Configuration.ItemTags.TryGetValue(item.Id, out var itemTags)
                        && itemTags != null
                        && itemTags.Any(t => t.Contains(searchQuery, StringComparison.OrdinalIgnoreCase));
                    if (!hasMatchingTag) continue;
                }

                else if (searchPrefix == "p" || searchPrefix == "p:")
                {
                    // 'p:' szűrés: csak akkor felel meg, ha a mod neve tartalmazza a keresett szöveget
                    bool matchesMod = affectingMods.Any(m => m.Contains(searchQuery, StringComparison.OrdinalIgnoreCase));
                    if (!matchesMod) continue;
                }

                else if (searchPrefix == "j" || searchPrefix == "job")
                {
                    if (!item.JobString.Contains(searchQuery, StringComparison.OrdinalIgnoreCase) &&
                        !item.JobAbbrs.Any(j => j.Contains(searchQuery, StringComparison.OrdinalIgnoreCase)))
                        continue;
                }
                else if (searchPrefix == "id")
                {
                    if (!item.IdString.Contains(searchQuery, StringComparison.OrdinalIgnoreCase)) continue;
                }
                else if (searchPrefix == "m" || searchPrefix == "model")
                {
                    if (!item.ModelString.Contains(searchQuery, StringComparison.OrdinalIgnoreCase)) continue;
                }
                else
                {
                    if (!item.Name.Contains(nameFilter, StringComparison.OrdinalIgnoreCase)) continue;
                }
            }

            // the separate ID / Model search boxes, then the slot / job / dye filters
            if (hasIdFilter && !item.IdString.Contains(idFilter)) continue;
            if (hasModelFilter && !item.ModelString.Contains(modelFilter, StringComparison.OrdinalIgnoreCase)) continue;

            if (!allSlotsSelected && selectedSlots.Count > 0 && !selectedSlots.Contains(item.SlotName)) continue;
            if (!allJobsSelected && selectedJobs.Count > 0 && !item.JobAbbrs.Overlaps(selectedJobs)) continue;
            if (selectedDyes.Count > 0 && !selectedDyes.Contains(item.DyeCount)) continue;

            if (hasTagFilter)
            {
                bool hasItemTags = plugin.Configuration.ItemTags.TryGetValue(item.Id, out var itemTags) && itemTags != null && itemTags.Count > 0;
                bool matchUntagged = filterUntagged && !hasItemTags;

                bool matchSpecificTag = false;
                if (selectedTags.Count > 0)
                {
                    var requiredTags = selectedTags.Where(kv => kv.Value == 1).Select(kv => kv.Key).ToList();
                    var excludedTags = selectedTags.Where(kv => kv.Value == -1).Select(kv => kv.Key).ToList();

                    // 1. Pozitív tagek vizsgálata (AND vs OR mód szerint)
                    bool matchesRequired;
                    if (requiredTags.Count == 0)
                    {
                        matchesRequired = true; // Ha nincs pozitív tag kijelölve, minden item átmegy ezen a lépésen
                    }
                    else if (tagFilterMode == 1) // AND mód: ÖSSZES kijelölt pozitív tag kell
                    {
                        matchesRequired = hasItemTags && requiredTags.All(t => itemTags!.Contains(t));
                    }
                    else // OR mód: LEGALÁBB EGY kijelölt pozitív tag kell
                    {
                        matchesRequired = hasItemTags && requiredTags.Any(t => itemTags!.Contains(t));
                    }

                    // 2. Kizárt (NOT) tagek vizsgálata: EGYETLEN kizárt tag sem lehet rajta
                    bool matchesExcluded = !hasItemTags || !excludedTags.Any(t => itemTags!.Contains(t));

                    // 3. A kizárás MINDIG AND logikával szűkíti a pozitív találatokat:
                    matchSpecificTag = matchesRequired && matchesExcluded;
                }

                if (!matchUntagged && !matchSpecificTag)
                    continue;
            }

            resultList.Add(item);
        }

        // keep only the first item of each model (optionally ignoring the variant)
        if (onlyOnePerModelType)
        {
            var deduplicated = new List<ItemRow>(resultList.Count);
            if (ignoreVariants)
            {
                var seen = new HashSet<ushort>();
                foreach (var item in resultList)
                {
                    if (seen.Add(item.ModelMain)) deduplicated.Add(item);
                }
            }
            else
            {
                var seen = new HashSet<(ushort, ushort)>();
                foreach (var item in resultList)
                {
                    if (seen.Add((item.ModelMain, item.ModelVariant))) deduplicated.Add(item);
                }
            }
            resultList = deduplicated;
        }

        // sort by the clicked column, ties fall back to the name
        if (sortColumnIndex >= 0)
        {
            int col = sortColumnIndex;
            bool isAsc = sortAscending;

            resultList.Sort((x, y) =>
            {
                int cmp = col switch
                {
                    0 => plugin.Configuration.Favorites.Contains(x.Id).CompareTo(plugin.Configuration.Favorites.Contains(y.Id)),
                    1 => plugin.Configuration.DoubleFavorites.Contains(x.Id).CompareTo(plugin.Configuration.DoubleFavorites.Contains(y.Id)),
                    2 => plugin.Configuration.TripleFavorites.Contains(x.Id).CompareTo(plugin.Configuration.TripleFavorites.Contains(y.Id)),
                    3 => plugin.Configuration.Wishlist.Contains(x.Id).CompareTo(plugin.Configuration.Wishlist.Contains(y.Id)),
                    4 => plugin.Configuration.DoubleWishlist.Contains(x.Id).CompareTo(plugin.Configuration.DoubleWishlist.Contains(y.Id)),
                    5 => GetItemModState(x.Id, x.Name, out _, out _, out _).CompareTo(GetItemModState(y.Id, y.Name, out _, out _, out _)),
                    6 => string.Compare(x.Name, y.Name, StringComparison.OrdinalIgnoreCase),
                    7 => x.Id.CompareTo(y.Id),
                    8 => x.ModelMain != y.ModelMain ? x.ModelMain.CompareTo(y.ModelMain) : x.ModelVariant.CompareTo(y.ModelVariant),
                    9 => string.Compare(x.JobString, y.JobString, StringComparison.OrdinalIgnoreCase),
                    10 => x.DyeCount.CompareTo(y.DyeCount),
                    11 => CompareItemTags(x.Id, y.Id, isAsc),
                    _ => 0
                };

                if (cmp == 0 && col != 6) cmp = string.Compare(x.Name, y.Name, StringComparison.OrdinalIgnoreCase);
                return isAsc ? cmp : -cmp;
            });
        }

        filteredItems = resultList;

        // row striping: the stripe flips when the model id changes, so items sharing a model sit in one band
        filteredItemToggles.Clear();
        if (filteredItems.Count > 0)
        {
            bool currentToggle = true;
            ushort lastModelMain = filteredItems[0].ModelMain;
            filteredItemToggles.Add(currentToggle);

            for (int x = 1; x < filteredItems.Count; x++)
            {
                var item = filteredItems[x];
                if (item.ModelMain != lastModelMain)
                {
                    currentToggle = !currentToggle;
                    lastModelMain = item.ModelMain;
                }
                filteredItemToggles.Add(currentToggle);
            }
        }
    }

    private int CompareItemTags(uint xId, uint yId, bool isAsc)
    {
        bool hasX = plugin.Configuration.ItemTags.TryGetValue(xId, out var tagsX) && tagsX.Count > 0;
        bool hasY = plugin.Configuration.ItemTags.TryGetValue(yId, out var tagsY) && tagsY.Count > 0;

        // 1. Ha az egyiknek van tagje, a másiknak nincs: a tagged elem MINDIG elöl marad
        if (hasX && !hasY) return isAsc ? -1 : 1;
        if (!hasX && hasY) return isAsc ? 1 : -1;

        // 2. Ha egyiknek sincs tagje: egyenlőek
        if (!hasX && !hasY) return 0;

        // 3. Ha mindkettőnek van tagje: ábécé szerinti rendezés a tag neve alapján
        return string.Compare(tagsX![0], tagsY![0], StringComparison.OrdinalIgnoreCase);
    }

    private void CheckHeaderInteraction(System.Action clearAction)
    {
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("Middle-Click: Clear filter in column");
        }
        if (ImGui.IsItemClicked(ImGuiMouseButton.Middle))
        {
            clearAction();
            isDirty = true;
        }
    }

    // Reset counters for the three header search boxes. Bumping one gives the box a new ImGui ID,
    // so the text can be cleared even while the box has keyboard focus.
    private int nameSearchResetGen = 0;
    private int idSearchResetGen = 0;
    private int modelSearchResetGen = 0;

    // Middle-click clear + tooltip for header cells with a search box (Item Name / Item ID / Model ID).
    // Must be called right AFTER the InputText of the cell.
    // cellMin: top-left of the cell in SCREEN coordinates.
    // blockWidth / blockHeight: top-right button area (sort / job / help) that has priority over the cell.
    private bool SearchHeaderMiddleClicked(Vector2 cellMin, float availWidth, float blockWidth, float blockHeight)
    {
        if (anyPopupOpen) return false;

        // Cell area: from the cell top down to the bottom of the search box (= last drawn item).
        Vector2 cellMax = new Vector2(cellMin.X + availWidth, ImGui.GetItemRectMax().Y);
        if (!ImGui.IsMouseHoveringRect(cellMin, cellMax, false)) return false;

        // Buttons in the top-right corner come first.
        Vector2 blockMin = new Vector2(cellMax.X - blockWidth, cellMin.Y);
        Vector2 blockMax = new Vector2(cellMax.X, cellMin.Y + blockHeight + 4f);
        if (ImGui.IsMouseHoveringRect(blockMin, blockMax, false)) return false;

        // Tooltip only on the title row (above the search box); the middle-click still works on the whole cell.
        Vector2 titleMax = new Vector2(cellMax.X, ImGui.GetItemRectMin().Y);
        if (ImGui.IsMouseHoveringRect(cellMin, titleMax, false))
            ImGui.SetTooltip("Middle-Click: Clear Filter");
        return ImGui.IsMouseClicked(ImGuiMouseButton.Middle);
    }

    // sort button click: first direction -> other direction -> off
    private void ToggleSort(int columnIndex)
    {
        // F, DF, TF, W, DW, M (0..5) és Dyes (10) oszlopoknál az 1. kattintás DESC
        bool startDesc = (columnIndex >= 0 && columnIndex <= 5) || columnIndex == 10;

        if (sortColumnIndex != columnIndex)
        {
            sortColumnIndex = columnIndex;
            sortAscending = !startDesc; // ha startDesc, akkor false (DESC) lesz az első
        }
        else
        {
            if (startDesc)
            {
                // Ciklus: DESC (false) -> ASC (true) -> Off (-1)
                if (!sortAscending)
                {
                    sortAscending = true;
                }
                else
                {
                    sortColumnIndex = -1;
                }
            }
            else
            {
                // Ciklus: ASC (true) -> DESC (false) -> Off (-1)
                if (sortAscending)
                {
                    sortAscending = false;
                }
                else
                {
                    sortColumnIndex = -1;
                }
            }
        }
        isDirty = true;
        resetScrollToTop = true;
    }

    private void RenderColumnHeaderWithPopup(string headerLabel, string popupTitle, string popupId, ref int filterMode, HashSet<uint> favSet, bool isActive, System.Action clearAction)
    {
        if (isActive)
        {
            uint darkBlueCol = ImGui.GetColorU32(new Vector4(0.12f, 0.25f, 0.50f, 0.45f));
            ImGui.TableSetBgColor(ImGuiTableBgTarget.CellBg, darkBlueCol);
        }

        float cellStartX = ImGui.GetCursorPosX();
        float availWidth = ImGui.GetContentRegionAvail().X;
        float textWidth = ImGui.CalcTextSize(headerLabel).X;

        ImGui.SetCursorPosX(cellStartX + Math.Max(0f, (availWidth - textWidth) * 0.5f));
        ImGui.TableHeader(headerLabel);
        CheckHeaderInteraction(clearAction);

        float btnWidth = ImGui.CalcTextSize("▼").X + ImGui.GetStyle().FramePadding.X * 2f;
        ImGui.SetCursorPosX(cellStartX + Math.Max(0f, (availWidth - btnWidth) * 0.5f));

        if (ImGui.SmallButton($"▼##{popupId}"))
        {
            ImGui.OpenPopup(popupId);
        }
        CheckHeaderInteraction(clearAction);

        if (ImGui.BeginPopup(popupId))
        {
            ImGui.TextDisabled($"-- {popupTitle} Filter --");
            if (ImGui.RadioButton($"All##{popupId}", filterMode == 0)) { filterMode = 0; isDirty = true; }
            if (ImGui.RadioButton($"Only flagged##{popupId}", filterMode == 1)) { filterMode = 1; isDirty = true; }
            if (ImGui.RadioButton($"Only unflagged##{popupId}", filterMode == 2)) { filterMode = 2; isDirty = true; }

            ImGui.Separator();

            if (ImGui.Selectable("Flag all visible"))
            {
                foreach (var item in filteredItems) favSet.Add(item.Id);
                plugin.Configuration.Save();
                isDirty = true;
            }

            if (ImGui.Selectable("Delete all visible"))
            {
                foreach (var item in filteredItems) favSet.Remove(item.Id);
                plugin.Configuration.Save();
                isDirty = true;
            }

            ImGui.EndPopup();
        }
    }

    // star cell for F / DF / TF, a click toggles the flag
    private void DrawStarButton(uint itemId, HashSet<uint> favSet, Vector4 activeColor, int index, float rowHeight)
    {
        bool isFav = favSet.Contains(itemId);
        ImGui.PushStyleColor(ImGuiCol.Text, isFav ? activeColor : new Vector4(0.35f, 0.35f, 0.35f, 1.0f));

        string iconStr = FontAwesomeIcon.Star.ToIconString();
        float fontScale = 0.85f;

        ImGui.PushFont(UiBuilder.IconFont);
        Vector2 textSize = ImGui.CalcTextSize(iconStr);
        float scaledHeight = textSize.Y * fontScale;
        float posY = rowStartY + Math.Max(0f, (dynRowHeight - scaledHeight) * 0.5f);

        ImGui.SetWindowFontScale(fontScale);
        ImGui.SetCursorPosY(posY);

        float availWidth = ImGui.GetContentRegionAvail().X;
        float iconWidth = textSize.X * fontScale;
        float offsetX = Math.Max(0f, (availWidth - iconWidth) * 0.5f);

        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + offsetX);

        ImGui.TextUnformatted(iconStr);
        if (!anyPopupOpen && ImGui.IsItemClicked(ImGuiMouseButton.Left))
        {
            if (isFav) favSet.Remove(itemId); else favSet.Add(itemId);
            plugin.Configuration.Save();
            isDirty = true;
        }

        ImGui.PopFont();
        ImGui.SetWindowFontScale(1.0f);
        ImGui.PopStyleColor();
    }

    // same for the wishlist columns, with a basket icon
    private void DrawWButton(uint itemId, HashSet<uint> set, Vector4 activeColor, string idPrefix, float rowHeight)
    {
        bool isSel = set.Contains(itemId);
        ImGui.PushStyleColor(ImGuiCol.Text, isSel ? activeColor : new Vector4(0.35f, 0.35f, 0.35f, 1.0f));

        string iconStr = FontAwesomeIcon.ShoppingBasket.ToIconString();
        float fontScale = 0.85f; 

        ImGui.PushFont(UiBuilder.IconFont);
        Vector2 textSize = ImGui.CalcTextSize(iconStr);
        float scaledHeight = textSize.Y * fontScale;
        float posY = rowStartY + Math.Max(0f, (dynRowHeight - scaledHeight) * 0.5f);

        ImGui.SetWindowFontScale(fontScale);
        ImGui.SetCursorPosY(posY);

        float availWidth = ImGui.GetContentRegionAvail().X;
        float iconWidth = textSize.X * fontScale;
        float offsetX = Math.Max(0f, (availWidth - iconWidth) * 0.5f);

        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + offsetX);

        ImGui.TextUnformatted(iconStr);
        if (!anyPopupOpen && ImGui.IsItemClicked(ImGuiMouseButton.Left))
        {
            if (isSel) set.Remove(itemId); else set.Add(itemId);
            plugin.Configuration.Save();
            isDirty = true;
        }

        ImGui.PopFont();
        ImGui.SetWindowFontScale(1f);
        ImGui.PopStyleColor();
    }

    private void LinkItemToChat(uint itemId)
    {
        try
        {
            var seString = SeString.CreateItemLink(itemId, false);
            Plugin.ChatGui.Print(seString);
        }
        catch (Exception ex)
        {
            Plugin.Log.Error($"Chat link error: {ex.Message}");
        }
    }

    // mode 0 = all, 1 = only flagged, 2 = only unflagged
    private bool CheckFavState(HashSet<uint> set, uint itemId, int mode)
    {
        bool isFav = set.Contains(itemId);
        if (mode == 1 && !isFav) return false;
        if (mode == 2 && isFav) return false;
        return true;
    }

    private void DrawCenteredText(string text)
    {
        float textWidth = ImGui.CalcTextSize(text).X;
        float availWidth = ImGui.GetContentRegionAvail().X;
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + (availWidth - textWidth) * 0.5f);
        ImGui.TextUnformatted(text);
    }

    // EquipSlotCategory row -> slot name used by the equipment bar filter
    private string GetSlotName(uint equipSlotCategory)
    {
        return equipSlotCategory switch
        {
            1 or 13 => "MainHand",
            2 => "OffHand",
            3 or 14 or 15 or 20 => "Head",
            4 or 16 or 19 or 21 => "Body",
            5 => "Hands",
            7 or 17 or 18 => "Legs",
            8 => "Feet",
            9 => "Ears",
            10 => "Neck",
            11 => "Wrists",
            12 => "Finger",
            _ => "Other"
        };
    }

    private byte GetGlamourerSlotId(uint equipSlotCategory)
    {
        return plugin.GlamourerIpc.GetGlamourerSlotId(equipSlotCategory);
    }

    // a filter changed: rebuild the list and jump back to the top
    private void MarkFilterDirty()
    {
        isDirty = true;
        resetScrollToTop = true;
    }
}
