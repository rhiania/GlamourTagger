using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Windowing;
using Dalamud.Utility;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace GlamourTagger.Windows;

// Main window, split into partial files by area:
//   MainWindow.cs    - Draw(): top bar, item table, footer
//   _Table           - item cache, filtering, sorting, cell helpers
//   _EquipmentBar    - slot filter bar + dye sets
//   _DyeHelper       - dye picker popup, applying dyes
//   _Penumbra        - mod states, affecting mods row, catch window
//   _TagManager / _Backup / _Options - the extra windows and the Options dropdown
public partial class MainWindow : Window, IDisposable
{
    private readonly Plugin plugin;

    // true = the filtered list has to be rebuilt before the rows are drawn
    public bool isDirty = true;
    private bool anyPopupOpen;

    private int scrollTargetRow = -1;
    private float currentIconSize;
    private float dynRowHeight;
    private float footerHeight = 80f;

    // layout values measured in Draw and reused by the cell helpers
    private float footerStartPosY;
    public float rowStartY;
    public float headerRowStartY;
    public float lineHeight;
    public float headerRow2StartY;

    private bool showPenumbraCatchWindow = false;
    private readonly List<(ItemRow Item, string ModName)> newlyUnmoddedCatches = new();
    private readonly List<(ItemRow Item, string ModName)> previouslyDemoddedCatches = new();
    private readonly List<(ItemRow Item, string ModName)> previouslyUnusableCatches = new();
    // Session-only flag to suppress catch popups until main window is re-opened
    public bool sessionSuppressCatchWindow = false;

    public MainWindow(Plugin plugin) : base("Glamour Tagger", ImGuiWindowFlags.NoScrollbar)
    {
        this.Size = new Vector2(1000, 600);
        this.SizeCondition = ImGuiCond.FirstUseEver;
        this.SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(500, 300),
            MaximumSize = new Vector2(1800, 1200)
        };
        this.plugin = plugin;

        // everything selected = nothing filtered out
        foreach (var slot in AvailableSlots) selectedSlots.Add(slot);
        foreach (var job in AvailableJobs) selectedJobs.Add(job);
        selectedDyes.Add(0); selectedDyes.Add(1); selectedDyes.Add(2);

        InitializeItemCache();
        RebuildTagCache();

        // so the catch window can react when Penumbra's mod list changed
        plugin.PenumbraIpc.OnCacheRefreshed += HandlePenumbraCacheUpdate;
    }

    public void Dispose()
    {
        plugin.PenumbraIpc.OnCacheRefreshed -= HandlePenumbraCacheUpdate;
    }

    public override void OnClose()
    {
        base.OnClose();
        // Ha mindkét gyűrű aktív volt bezáráskor, a következő nyitásra visszállunk csak az RR-re
        if (plugin.Configuration.RightRingActive && plugin.Configuration.LeftRingActive)
        {
            plugin.Configuration.RightRingActive = true;
            plugin.Configuration.LeftRingActive = false;
            plugin.Configuration.Save();
        }

        // IPC hibaüzenet 5 perces cooldownjának resetelése
        plugin.ResetIpcErrorCooldown();

        // Reset session suppression when the user closes the main plugin window
        sessionSuppressCatchWindow = false;

        // Ha volt folyamatban lévő módosítás, az ablak bezárásakor azonnal kiírjuk a lemezre
        plugin.BackupManager.FlushAutoSave();
    }

    public override void Draw()
    {
        anyPopupOpen = ImGui.IsPopupOpen("", ImGuiPopupFlags.AnyPopupId);

        //DrawDebugChangelogControls(); // Uncomment to show debug changelog controls

        ImGui.Spacing();

        // --- top bar: Options | preview mode | Fitting Room Assist | Revert | Clear all filter ---
        DrawOptionsDropdown();

        bool glamourerAvailable = plugin.GlamourerIpc.IsAvailable();

        ImGui.SameLine(0, 30);
        ImGui.TextUnformatted("Mode:");
        ImGui.SameLine(0, 12);

        if (ImGui.RadioButton("Glamourer Preview", applyMode == 0)) applyMode = 0;
        ImGui.SameLine(0, 12);
        if (ImGui.RadioButton("Fitting Room Try On", applyMode == 1)) applyMode = 1;

        ImGui.SameLine(0, 8);
        ImGui.PushFont(UiBuilder.IconFont);
        // puzzle button reopens the Fitting Room Assist window (dimmed outside Fitting Room mode)
        bool dimmed = applyMode != 1;
        Vector4 baseCol = new Vector4(dimmed ? 0.45f : 0.9f, dimmed ? 0.45f : 0.9f, dimmed ? 0.45f : 0.9f, 1.0f);
        ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.35f, 0.10f, 0.10f, dimmed ? 0.35f : 0.95f));
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.45f, 0.15f, 0.15f, dimmed ? 0.5f : 0.95f));
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.55f, 0.20f, 0.20f, dimmed ? 0.65f : 0.95f));
        ImGui.PushStyleColor(ImGuiCol.Text, baseCol);
        if (ImGui.Button($"{FontAwesomeIcon.PuzzlePiece.ToIconString()}##ReopenFittingRoomAssist", new Vector2(0, 0)))
        {
            plugin.FittingRoomAssistWindow.IsOpen = true;
        }
        ImGui.PopStyleColor(4);
        ImGui.PopFont();
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("Reopen the Fitting Room Assist window");
        }

        // right side of the top bar, only drawn if there is room for it
        float clearBtnWidth = 200f;
        float revertBtnWidth = 24f;
        float revertBtnGap = 6f;
        float availX = ImGui.GetContentRegionAvail().X;
        if (availX > clearBtnWidth + revertBtnWidth + revertBtnGap)
        {
            // --- Revert button (small icon button, moved here from the equipment bar) ---
            ImGui.SameLine(ImGui.GetWindowWidth() - clearBtnWidth - revertBtnWidth - revertBtnGap - ImGui.GetStyle().WindowPadding.X - 30f);

            ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.35f, 0.10f, 0.10f, 0.95f));
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.45f, 0.15f, 0.15f, 0.95f));
            ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.55f, 0.20f, 0.20f, 0.95f));
            ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, Vector2.Zero);
            ImGui.PushStyleVar(ImGuiStyleVar.ButtonTextAlign, new Vector2(0.5f, 0.5f));

            ImGui.PushFont(UiBuilder.IconFont);
            ImGui.SetWindowFontScale(0.8f);
            string revertIconStr = FontAwesomeIcon.Undo.ToIconString();
            if (ImGui.Button($"{revertIconStr}##EquipBar_RevertBtn", new Vector2(revertBtnWidth, revertBtnWidth)))
            {
                plugin.RevertGlamourerPreview();
            }
            ImGui.PopFont();
            ImGui.SetWindowFontScale(1f);

            ImGui.PopStyleVar(2);
            ImGui.PopStyleColor(3);

            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip("Revert your character to the game state");
            }

            // --- Clear all filter ---
            ImGui.SameLine(0, revertBtnGap);
            ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.35f, 0.10f, 0.10f, 0.95f));         // Base dark red
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.45f, 0.15f, 0.15f, 0.95f)); // Slightly brighter on hover
            ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.55f, 0.20f, 0.20f, 0.95f));  // Even brighter when clicked
            bool ClearClicked = ImGui.Button("Clear all filter", new Vector2(clearBtnWidth, 0));
            ImGui.PopStyleColor(3);
            if (ClearClicked)
            {
                ResetAllFilters();
            }
        }

        // extra windows owned by this one
        if (showTagManagerWindow) DrawTagManagerWindow();
        if (showPenumbraCatchWindow) DrawPenumbraCatchWindow();

        ImGui.Spacing();
        ImGui.Separator();

        DrawEquipmentBar();

        // --- item table ---
        // row height follows the icon size from the settings
        currentIconSize = plugin.Configuration.TableIconSize;
        ImGui.PushStyleVar(ImGuiStyleVar.CellPadding, new Vector2(ImGui.GetStyle().CellPadding.X, 2f));
        float cellPaddingY = 2f;
        dynRowHeight = Math.Max(24f, currentIconSize + 4f);
        float totalClipperRowHeight = dynRowHeight + (cellPaddingY * 2f);

        if (ImGui.BeginTable("GlamourTable", 12,
            ImGuiTableFlags.Borders | ImGuiTableFlags.ScrollY | ImGuiTableFlags.ScrollX |
            ImGuiTableFlags.Resizable, new Vector2(0, -footerHeight)))
        {
            // header row stays visible while scrolling
            ImGui.TableSetupScrollFreeze(0, 1);

            ImGui.TableSetupColumn("F", ImGuiTableColumnFlags.WidthFixed, 20);
            ImGui.TableSetupColumn("DF", ImGuiTableColumnFlags.WidthFixed, 20);
            ImGui.TableSetupColumn("TF", ImGuiTableColumnFlags.WidthFixed, 20);
            ImGui.TableSetupColumn("W", ImGuiTableColumnFlags.WidthFixed, 20);
            ImGui.TableSetupColumn("DW", ImGuiTableColumnFlags.WidthFixed, 20);
            ImGui.TableSetupColumn("M", ImGuiTableColumnFlags.WidthFixed, 20);
            ImGui.TableSetupColumn("Item Name", ImGuiTableColumnFlags.WidthFixed, 200);
            ImGui.TableSetupColumn("ID", ImGuiTableColumnFlags.WidthFixed, 60);
            ImGui.TableSetupColumn("Model ID", ImGuiTableColumnFlags.WidthFixed, 60);
            ImGui.TableSetupColumn("Jobs", ImGuiTableColumnFlags.WidthFixed, 60);
            ImGui.TableSetupColumn("Dyes", ImGuiTableColumnFlags.WidthFixed, 35);
            ImGui.TableSetupColumn("Tags", ImGuiTableColumnFlags.WidthStretch);

            float frameHeight = ImGui.GetFrameHeight(); // A search box (InputText) pontos magassága
            float headerRowHeight = 52f; // Fix, egységes cellamagasság a teljes fejlécnek

            // 1. Átadunk egy fix sormagasságot az ImGui-nak, így mind a 12 oszlop cellája hajszálpontosan ugyanakkora lesz
            ImGui.TableNextRow(ImGuiTableRowFlags.Headers, headerRowHeight);

            float cellTopY = ImGui.GetCursorPosY();

            // 2. Első sor (Címsor szövegek) igazítása a cella tetejéhez
            headerRowStartY = cellTopY + 3f;

            // 3. Search box igazítása: a box alja pontosan a cella magasságának 95%-ánál lesz
            headerRow2StartY = cellTopY + (headerRowHeight * 0.95f) - frameHeight;

            // header cells: title + sort button on top, filter popup or search box below
            uint darkBlueCol2 = ImGui.GetColorU32(new Vector4(0.12f, 0.25f, 0.40f, 0.45f));
            ImGui.TableSetBgColor(ImGuiTableBgTarget.RowBg0, ImGui.GetColorU32(new Vector4(0.14f, 0.20f, 0.32f, 0.45f)));

            // Column 0: F
            ImGui.TableSetColumnIndex(0);
            RenderHeaderWithPopup(0, "F", "FavFilterPopup", favFilterMode != 0, () => favFilterMode = 0, true);

            if (ImGui.BeginPopup("FavFilterPopup"))
            {
                ImGui.TextDisabled("-- Favourite Filter --");
                if (ImGui.RadioButton("All##FavFilterPopup", favFilterMode == 0)) { favFilterMode = 0; isDirty = true; }
                if (ImGui.RadioButton("Only flagged##FavFilterPopup", favFilterMode == 1)) { favFilterMode = 1; isDirty = true; }
                if (ImGui.RadioButton("Only unflagged##FavFilterPopup", favFilterMode == 2)) { favFilterMode = 2; isDirty = true; }
                ImGui.Separator();
                if (ImGui.Selectable("Flag all visible")) { foreach (var item in filteredItems) plugin.Configuration.Favorites.Add(item.Id); plugin.Configuration.Save(); isDirty = true; }
                if (ImGui.Selectable("Delete all visible")) { foreach (var item in filteredItems) plugin.Configuration.Favorites.Remove(item.Id); plugin.Configuration.Save(); isDirty = true; }
                ImGui.EndPopup();
            }

            // Column 1: DF
            ImGui.TableSetColumnIndex(1);
            RenderHeaderWithPopup(1, "DF", "DFFilterPopup", dfFilterMode != 0, () => dfFilterMode = 0, true);

            if (ImGui.BeginPopup("DFFilterPopup"))
            {
                ImGui.TextDisabled("-- Double Favourite Filter --");
                if (ImGui.RadioButton("All##DFFilterPopup", dfFilterMode == 0)) { dfFilterMode = 0; isDirty = true; }
                if (ImGui.RadioButton("Only flagged##DFFilterPopup", dfFilterMode == 1)) { dfFilterMode = 1; isDirty = true; }
                if (ImGui.RadioButton("Only unflagged##DFFilterPopup", dfFilterMode == 2)) { dfFilterMode = 2; isDirty = true; }
                ImGui.Separator();
                if (ImGui.Selectable("Flag all visible")) { foreach (var item in filteredItems) plugin.Configuration.DoubleFavorites.Add(item.Id); plugin.Configuration.Save(); isDirty = true; }
                if (ImGui.Selectable("Delete all visible")) { foreach (var item in filteredItems) plugin.Configuration.DoubleFavorites.Remove(item.Id); plugin.Configuration.Save(); isDirty = true; }
                ImGui.EndPopup();
            }

            // Column 2: TF
            ImGui.TableSetColumnIndex(2);
            RenderHeaderWithPopup(2, "TF", "TFFilterPopup", tfFilterMode != 0, () => tfFilterMode = 0, true);

            if (ImGui.BeginPopup("TFFilterPopup"))
            {
                ImGui.TextDisabled("-- Triple Favourite Filter --");
                if (ImGui.RadioButton("All##TFFilterPopup", tfFilterMode == 0)) { tfFilterMode = 0; isDirty = true; }
                if (ImGui.RadioButton("Only flagged##TFFilterPopup", tfFilterMode == 1)) { tfFilterMode = 1; isDirty = true; }
                if (ImGui.RadioButton("Only unflagged##TFFilterPopup", tfFilterMode == 2)) { tfFilterMode = 2; isDirty = true; }
                ImGui.Separator();
                if (ImGui.Selectable("Flag all visible")) { foreach (var item in filteredItems) plugin.Configuration.TripleFavorites.Add(item.Id); plugin.Configuration.Save(); isDirty = true; }
                if (ImGui.Selectable("Delete all visible")) { foreach (var item in filteredItems) plugin.Configuration.TripleFavorites.Remove(item.Id); plugin.Configuration.Save(); isDirty = true; }
                ImGui.EndPopup();
            }

            // Column 3: W
            ImGui.TableSetColumnIndex(3);
            RenderHeaderWithPopup(3, "W", "WLFilterPopup", wlFilterMode != 0, () => wlFilterMode = 0, true);

            if (ImGui.BeginPopup("WLFilterPopup"))
            {
                ImGui.TextDisabled("-- Wishlist Filter --");
                if (ImGui.RadioButton("All##WLFilterPopup", wlFilterMode == 0)) { wlFilterMode = 0; isDirty = true; }
                if (ImGui.RadioButton("Only flagged##WLFilterPopup", wlFilterMode == 1)) { wlFilterMode = 1; isDirty = true; }
                if (ImGui.RadioButton("Only unflagged##WLFilterPopup", wlFilterMode == 2)) { wlFilterMode = 2; isDirty = true; }
                ImGui.Separator();
                if (ImGui.Selectable("Flag all visible")) { foreach (var item in filteredItems) plugin.Configuration.Wishlist.Add(item.Id); plugin.Configuration.Save(); isDirty = true; }
                if (ImGui.Selectable("Delete all visible")) { foreach (var item in filteredItems) plugin.Configuration.Wishlist.Remove(item.Id); plugin.Configuration.Save(); isDirty = true; }
                ImGui.EndPopup();
            }

            // Column 4: DW
            ImGui.TableSetColumnIndex(4);
            RenderHeaderWithPopup(4, "DW", "DWLFilterPopup", dwlFilterMode != 0, () => dwlFilterMode = 0, true);

            if (ImGui.BeginPopup("DWLFilterPopup"))
            {
                ImGui.TextDisabled("-- Double Wishlist Filter --");
                if (ImGui.RadioButton("All##DWLFilterPopup", dwlFilterMode == 0)) { dwlFilterMode = 0; isDirty = true; }
                if (ImGui.RadioButton("Only flagged##DWLFilterPopup", dwlFilterMode == 1)) { dwlFilterMode = 1; isDirty = true; }
                if (ImGui.RadioButton("Only unflagged##DWLFilterPopup", dwlFilterMode == 2)) { dwlFilterMode = 2; isDirty = true; }
                ImGui.Separator();
                if (ImGui.Selectable("Flag all visible")) { foreach (var item in filteredItems) plugin.Configuration.DoubleWishlist.Add(item.Id); plugin.Configuration.Save(); isDirty = true; }
                if (ImGui.Selectable("Delete all visible")) { foreach (var item in filteredItems) plugin.Configuration.DoubleWishlist.Remove(item.Id); plugin.Configuration.Save(); isDirty = true; }
                ImGui.EndPopup();
            }

            // Column 5: M (Modded) Multi-Select Filter
            ImGui.TableSetColumnIndex(5);
            bool isModFiltered = selectedModFilters.Count < 4;
            RenderHeaderWithPopup(5, "PM", "ModFilterPopup", isModFiltered, () => {
                selectedModFilters.Clear();
                selectedModFilters.Add(-1); selectedModFilters.Add(0); selectedModFilters.Add(1); selectedModFilters.Add(2); isDirty = true;
            }, true);

            if (ImGui.BeginPopup("ModFilterPopup"))
            {
                ImGui.TextDisabled("-- Penumbra Mods Filter --");

                ImGui.TextDisabled("    Item State Logic:");
                ImGui.SameLine(0, 4f);
                HelpMarker2("Item State Logic:   -- Demodded and Unusable states are set manually --\n" +
                        "• Modded: The item has at least one active, working mod.\n" +
                        "• Demodded: The item has no active or unusable mods, and all affecting mods are set to Demodded (item is unchanged).\n" +
                        "• Unmodded: The item is not affected by any mods.\n" +
                        "• Unusable: The item has no active mods and at least one affecting mod is set to Unusable.\n\n" +
                        "Note: Items with Demodded or Unusable mods also receive special tags, " +
                        "which can be searched using the 'p:' prefix (e.g., p:demodded, p:unusable).");

                float availWidthA = ImGui.GetContentRegionAvail().X;
                float spacing = ImGui.GetStyle().ItemSpacing.X;
                float buttonWidth = (availWidthA - spacing) * 0.5f;

                if (ImGui.Button("Select All", new Vector2(buttonWidth, 0)))
                {
                    selectedModFilters.Add(-1); selectedModFilters.Add(0); selectedModFilters.Add(1); selectedModFilters.Add(2);
                    isDirty = true;
                }
                ImGui.SameLine();
                if (ImGui.Button("Clear All", new Vector2(buttonWidth, 0)))
                {
                    selectedModFilters.Clear();
                    isDirty = true;
                }
                ImGui.Separator();

                bool modded = selectedModFilters.Contains(2);
                if (ImGui.Checkbox("Modded", ref modded))
                {
                    if (modded) selectedModFilters.Add(2); else selectedModFilters.Remove(2);
                    isDirty = true;
                }

                bool demodded = selectedModFilters.Contains(1);
                if (ImGui.Checkbox("Demodded", ref demodded))
                {
                    if (demodded) selectedModFilters.Add(1); else selectedModFilters.Remove(1);
                    isDirty = true;
                }

                bool unmodded = selectedModFilters.Contains(0);
                if (ImGui.Checkbox("Unmodded", ref unmodded))
                {
                    if (unmodded) selectedModFilters.Add(0); else selectedModFilters.Remove(0);
                    isDirty = true;
                }

                bool unusable = selectedModFilters.Contains(-1);
                if (ImGui.Checkbox("Unusable", ref unusable))
                {
                    if (unusable) selectedModFilters.Add(-1); else selectedModFilters.Remove(-1);
                    isDirty = true;
                }

                ImGui.EndPopup();
            }

            // Column 6: Item Name
            ImGui.TableSetColumnIndex(6);
            if (!string.IsNullOrWhiteSpace(nameFilter))
                ImGui.TableSetBgColor(ImGuiTableBgTarget.CellBg, darkBlueCol2);

            float cellStartX6 = ImGui.GetCursorPosX();
            float availWidth6 = ImGui.GetContentRegionAvail().X;
            Vector2 cellMin6 = ImGui.GetCursorScreenPos(); // cell top-left in SCREEN coordinates

            float helpWidth = ImGui.CalcTextSize("(?)").X;
            float orderBtnWidth5 = 18f;
            float topRightSpacing = 4f;
            float jobBtnSpacing = 8.0f; // adjustable gap
            float topRightTotalWidth = JobSearchBtnWidth + jobBtnSpacing + helpWidth + topRightSpacing + orderBtnWidth5;

            // 1. Szöveg vágása (PushClipRect) és kirajzolása
            ImGui.SetCursorPosY(headerRowStartY);
            ImGui.SetCursorPosX(cellStartX6);

            Vector2 clipMin6 = ImGui.GetCursorScreenPos();
            Vector2 clipMax6 = new Vector2(clipMin6.X + Math.Max(0f, availWidth6 - topRightTotalWidth - 2f), clipMin6.Y + ImGui.GetTextLineHeight() + 4f);

            ImGui.PushClipRect(clipMin6, clipMax6, true);
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.9f, 0.9f, 0.68f, 0.95f));
            ImGui.TextUnformatted("Item Name");
            ImGui.PopStyleColor();
            ImGui.PopClipRect(); // Vágókeret lezárása

            float topRightX = cellStartX6 + Math.Max(0f, availWidth6 - topRightTotalWidth);
            ImGui.SetCursorPosY(headerRowStartY);
            ImGui.SetCursorPosX(topRightX);

            DrawCurrentJobSearchButton();

            ImGui.SameLine(0, jobBtnSpacing);
            ImGui.SetCursorPosY(headerRowStartY);

            HelpMarker2("Search by prefixes:\n" +
                "• t: or tag:   (e.g. t:gothic)\n" +
                "• p:     (e.g. p:Nier)\n" +
                "• j: or job:   (e.g. j:PLD)\n" +
                "• id:    (e.g. id:1001)\n" +
                "• m: or model: (e.g. m:50)");

            ImGui.SameLine(0, topRightSpacing);
            ImGui.SetCursorPosY(headerRowStartY);
            DrawOrderButton(6);

            ImGui.SetCursorPosY(headerRow2StartY);
            ImGui.SetNextItemWidth(-1);
            ImGui.PushID(nameSearchResetGen);
            if (ImGui.InputTextWithHint("##ItemSearch", "Search (or t:, p:, j:, id:, m:)...", ref nameFilter, 100)) MarkFilterDirty();
            ImGui.PopID();

            // Cell-wide middle-click clear + tooltip, behind the job / (?) / sort buttons
            if (SearchHeaderMiddleClicked(cellMin6, availWidth6, topRightTotalWidth, Math.Max(JobSearchBtnWidth, ImGui.GetTextLineHeight())))
            {
                nameFilter = "";
                nameSearchResetGen++;
                MarkFilterDirty();
            }

            // Column 7: ID
            ImGui.TableSetColumnIndex(7);
            bool isIdFiltered = !string.IsNullOrWhiteSpace(idFilter);
            if (isIdFiltered) ImGui.TableSetBgColor(ImGuiTableBgTarget.CellBg, darkBlueCol2);

            float cellStartX7 = ImGui.GetCursorPosX();
            float availWidth7 = ImGui.GetContentRegionAvail().X;
            float orderBtnWidth7 = 18f;
            Vector2 cellMin7 = ImGui.GetCursorScreenPos();

            // 1. Szöveg vágása (PushClipRect) és kirajzolása
            ImGui.NewLine();
            ImGui.SetCursorPosY(headerRowStartY);
            ImGui.SetCursorPosX(cellStartX7 + 2f);

            Vector2 clipMin7 = ImGui.GetCursorScreenPos();
            Vector2 clipMax7 = new Vector2(clipMin7.X + Math.Max(0f, availWidth7 - orderBtnWidth7 - 2f), clipMin7.Y + ImGui.GetTextLineHeight() + 4f);

            ImGui.PushClipRect(clipMin7, clipMax7, true);
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.9f, 0.9f, 0.68f, 0.95f));
            ImGui.TextUnformatted("Item ID");
            ImGui.PopStyleColor();
            ImGui.PopClipRect(); // Vágókeret lezárása

            ImGui.SetCursorPosY(headerRowStartY);
            ImGui.SetCursorPosX(cellStartX7 + Math.Max(0f, availWidth7 - orderBtnWidth7));
            DrawOrderButton(7);

            // 3. InputText a 2. sorban
            ImGui.SetCursorPosY(headerRow2StartY);
            ImGui.SetNextItemWidth(-1);
            ImGui.PushID(idSearchResetGen);
            if (ImGui.InputTextWithHint("##IdSearch", "Id...", ref idFilter, 20)) MarkFilterDirty();
            ImGui.PopID();

            if (SearchHeaderMiddleClicked(cellMin7, availWidth7, orderBtnWidth7, ImGui.GetTextLineHeight()))
            {
                idFilter = "";
                idSearchResetGen++;
                MarkFilterDirty();
            }

            // Column 8: Model ID
            ImGui.TableSetColumnIndex(8);
            bool isModelFiltered = !string.IsNullOrWhiteSpace(modelFilter);
            if (isModelFiltered) ImGui.TableSetBgColor(ImGuiTableBgTarget.CellBg, darkBlueCol2);

            float cellStartX8 = ImGui.GetCursorPosX();
            float availWidth8 = ImGui.GetContentRegionAvail().X;
            float orderBtnWidth8 = 18f;
            Vector2 cellMin8 = ImGui.GetCursorScreenPos();

            // 1. Szöveg vágása (PushClipRect) és kirajzolása
            ImGui.NewLine();
            ImGui.SetCursorPosY(headerRowStartY);
            ImGui.SetCursorPosX(cellStartX8 + 2f);

            Vector2 clipMin8 = ImGui.GetCursorScreenPos();
            Vector2 clipMax8 = new Vector2(clipMin8.X + Math.Max(0f, availWidth8 - orderBtnWidth8 - 2f), clipMin8.Y + ImGui.GetTextLineHeight() + 4f);

            ImGui.PushClipRect(clipMin8, clipMax8, true);
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.9f, 0.9f, 0.68f, 0.95f));
            ImGui.TextUnformatted("Model ID");
            ImGui.PopStyleColor();
            ImGui.PopClipRect(); // Vágókeret lezárása

            ImGui.SetCursorPosY(headerRowStartY);
            ImGui.SetCursorPosX(cellStartX8 + Math.Max(0f, availWidth8 - orderBtnWidth8));
            DrawOrderButton(8);

            // 3. InputText a 2. sorban
            ImGui.SetCursorPosY(headerRow2StartY);
            ImGui.SetNextItemWidth(-1);
            ImGui.PushID(modelSearchResetGen);
            if (ImGui.InputTextWithHint("##ModelSearch", "Model...", ref modelFilter, 20)) MarkFilterDirty();
            ImGui.PopID();

            if (SearchHeaderMiddleClicked(cellMin8, availWidth8, orderBtnWidth8, ImGui.GetTextLineHeight()))
            {
                modelFilter = "";
                modelSearchResetGen++;
                MarkFilterDirty();
            }

            // Column 9: Jobs
            ImGui.TableSetColumnIndex(9);
            bool isJobFiltered = selectedJobs.Count < AvailableJobs.Length;
            RenderHeaderWithPopup(9, "Jobs", "JobFilterPopup", isJobFiltered, () => {
                selectedJobs.Clear();
                foreach (var j in AvailableJobs) selectedJobs.Add(j);
            }, false);
            if (ImGui.BeginPopup("JobFilterPopup"))
            {
                if (ImGui.Button("Select All")) { foreach (var j in AvailableJobs) selectedJobs.Add(j); isDirty = true; }
                ImGui.SameLine();
                if (ImGui.Button("Clear All")) { selectedJobs.Clear(); isDirty = true; }
                ImGui.Separator();
                if (ImGui.BeginTable("JobGrid", 2, ImGuiTableFlags.SizingFixedFit))
                {
                    foreach (var job in AvailableJobs)
                    {
                        ImGui.TableNextColumn();
                        bool isChecked = selectedJobs.Contains(job);
                        if (ImGui.Checkbox(job, ref isChecked))
                        {
                            if (isChecked) selectedJobs.Add(job); else selectedJobs.Remove(job);
                            isDirty = true;
                        }
                    }
                    ImGui.EndTable();
                }
                ImGui.EndPopup();
            }

            // Column 10: Dyes
            ImGui.TableSetColumnIndex(10);
            bool isDyeFiltered = selectedDyes.Count < 3;
            RenderHeaderWithPopup(10, "Dyes", "DyeFilterPopup", isDyeFiltered, () => {
                selectedDyes.Clear();
                selectedDyes.Add(0); selectedDyes.Add(1); selectedDyes.Add(2);
            }, true);
            if (ImGui.BeginPopup("DyeFilterPopup"))
            {
                if (ImGui.Button("Select All"))

                    {
                        selectedDyes.Add(0);
                    selectedDyes.Add(1);
                    selectedDyes.Add(2);
                    isDirty = true;
                }
                ImGui.SameLine();
                if (ImGui.Button("Clear All"))

                    {
                        selectedDyes.Clear();
                    isDirty = true;
                }
                ImGui.Separator();

                bool noDye = selectedDyes.Contains(0);
                if (ImGui.Checkbox("No dyes", ref noDye)) { if (noDye) selectedDyes.Add(0); else selectedDyes.Remove(0); isDirty = true; }
                bool oneDye = selectedDyes.Contains(1);
                if (ImGui.Checkbox("One dye", ref oneDye)) { if (oneDye) selectedDyes.Add(1); else selectedDyes.Remove(1); isDirty = true; }
                bool twoDye = selectedDyes.Contains(2);
                if (ImGui.Checkbox("Two dyes", ref twoDye)) { if (twoDye) selectedDyes.Add(2); else selectedDyes.Remove(2); isDirty = true; }
                ImGui.EndPopup();
            }

            // Column 11: Tags
            ImGui.TableSetColumnIndex(11);
            bool isTagFiltered = filterUntagged || selectedTags.Count > 0;
            RenderHeaderWithPopup(11, "Tags", "TagFilterPopup", isTagFiltered, () => {
                filterUntagged = false;
                selectedTags.Clear();
                tagFilterMode = 0;
            }, false);

            if (ImGui.BeginPopup("TagFilterPopup"))
            {
                if (ImGui.Checkbox("Untagged items", ref filterUntagged)) isDirty = true;
                ImGui.Separator();
                if (ImGui.RadioButton("OR (Any selected tag)", tagFilterMode == 0)) { tagFilterMode = 0; isDirty = true; }
                if (ImGui.RadioButton("AND (All selected tags)", tagFilterMode == 1)) { tagFilterMode = 1; isDirty = true; }
                ImGui.Separator();
                float availWidthA = ImGui.GetContentRegionAvail().X;
                float spacing = ImGui.GetStyle().ItemSpacing.X;
                float buttonWidth = (availWidthA - spacing) * 0.5f;

                if (ImGui.Button("Select All", new Vector2(buttonWidth, 0)))
                {
                    foreach (var t in cachedUniqueTags) selectedTags[t] = 1;
                    isDirty = true;
                }

                ImGui.SameLine();

                if (ImGui.Button("Clear All", new Vector2(buttonWidth, 0)))
                {
                    selectedTags.Clear();
                    isDirty = true;
                }
                ImGui.Separator();

                ImGui.SetNextItemWidth(-1);
                ImGui.InputTextWithHint("##TagFilterSearch", "Search tags...", ref tagFilterSearch, 30);
                ImGui.Separator();

                foreach (var tag in cachedUniqueTags)
                {
                    // Szűrés a keresőszó alapján
                    if (!string.IsNullOrEmpty(tagFilterSearch) && !tag.Contains(tagFilterSearch, StringComparison.OrdinalIgnoreCase))
                        continue;

                    int currentState = selectedTags.TryGetValue(tag, out var val) ? val : 0;

                    FontAwesomeIcon? iconEnum = currentState switch
                    {
                        1 => FontAwesomeIcon.Check,
                        -1 => FontAwesomeIcon.Times,
                        _ => null
                    };

                    ImGui.PushID(tag);

                    string iconStr = iconEnum.HasValue ? iconEnum.Value.ToIconString() : "  ";

                    // Módosítás: FramePadding nullázása + középre igazítás gomb szinten
                    ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(0, 0));
                    ImGui.PushStyleVar(ImGuiStyleVar.ButtonTextAlign, new Vector2(0.7f, 0.6f));

                    ImGui.PushStyleColor(ImGuiCol.Button, ImGui.GetStyle().Colors[(int)ImGuiCol.FrameBg]);
                    ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.5f, 0.1f, 0.1f, 0.4f)); 
                    ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.6f, 0.15f, 0.15f, 0.6f));

                    if (iconEnum.HasValue)
                        ImGui.PushFont(UiBuilder.IconFont);

                    bool buttonClicked = ImGui.Button($"{iconStr}##chk", new Vector2(22, 22));

                    if (iconEnum.HasValue)
                        ImGui.PopFont();

                    ImGui.PopStyleColor(3);
                    ImGui.PopStyleVar(2);

                    if (buttonClicked)
                    {
                        currentState = currentState switch
                        {
                            0 => 1,
                            1 => -1,
                            _ => 0
                        };

                        if (currentState == 0)
                            selectedTags.Remove(tag);
                        else
                            selectedTags[tag] = currentState;

                        isDirty = true;
                    }

                    ImGui.SameLine();
                    ImGui.AlignTextToFramePadding();

                    if (ImGui.Selectable(tag, currentState != 0, ImGuiSelectableFlags.DontClosePopups))
                    {
                        currentState = currentState switch
                        {
                            0 => 1,
                            1 => -1,
                            _ => 0
                        };

                        if (currentState == 0)
                            selectedTags.Remove(tag);
                        else
                            selectedTags[tag] = currentState;

                        isDirty = true;
                    }

                    ImGui.PopID();
                }

                ImGui.EndPopup();
            }

            // a filter changed somewhere above -> rebuild the visible list once, before drawing rows
            if (isDirty)
            {
                RebuildFilteredList();
                if (resetScrollToTop)
                {
                    ImGui.SetScrollY(0f);
                    resetScrollToTop = false;
                }
                isDirty = false;
            }

            // --- 1. JUMP TO ITEM SCROLL LOGIKA (4. SORBA POZICIONÁLÁS) ---
            if (scrollToSelectedItem)
            {
                int targetIndex = filteredItems.FindIndex(x => x.Id == selectedItemId);
                if (targetIndex >= 0)
                {
                    scrollTargetRow = Math.Max(0, targetIndex - 3);
                    ImGui.SetScrollY(scrollTargetRow * totalClipperRowHeight);
                }
                scrollToSelectedItem = false;
            }

            // row colours: striped per model id (not per row), plus hover / selected
            uint bgCol1 = ImGui.GetColorU32(new Vector4(0.09f, 0.09f, 0.11f, 0.60f));
            uint bgCol2 = ImGui.GetColorU32(new Vector4(0.16f, 0.17f, 0.19f, 0.55f));
            uint hoverBlueCol = ImGui.GetColorU32(new Vector4(0.14f, 0.20f, 0.32f, 0.55f));
            uint selectedBlueCol = ImGui.GetColorU32(new Vector4(0.18f, 0.24f, 0.38f, 0.60f)); 

            float verticalOffset = Math.Max(0f, (dynRowHeight - ImGui.GetTextLineHeight()) * 0.5f);

            // clipper: only the rows that are on screen get drawn, the full item list is far too long for every frame
            var clipper = new ImGuiListClipper();
            clipper.Begin(filteredItems.Count, totalClipperRowHeight);

            while (clipper.Step())
            {
                for (int i = clipper.DisplayStart; i < clipper.DisplayEnd; i++)
                {
                    var item = filteredItems[i];
                    bool bgToggle = (filteredItemToggles.Count > i) && filteredItemToggles[i];
                    bool isItemSelected = selectedItemId == item.Id;

                    ImGui.TableNextRow(ImGuiTableRowFlags.None, dynRowHeight);
                    ImGui.PushID((int)item.Id);

                    rowStartY = ImGui.GetCursorPosY();
                    // Base vertical offset for standard 1.0f font text in this row
                    float textPosY = rowStartY + Math.Max(0f, (dynRowHeight - ImGui.GetTextLineHeight()) * 0.5f);

                    ImGui.TableSetColumnIndex(0);
                    ImGui.PushStyleColor(ImGuiCol.Header, 0);
                    ImGui.PushStyleColor(ImGuiCol.HeaderHovered, 0);
                    ImGui.PushStyleColor(ImGuiCol.HeaderActive, 0);

                    // invisible full-row selectable: select / hover / right-click for the whole row
                    bool rowClicked = !anyPopupOpen && ImGui.Selectable("##RowSelect", isItemSelected,
                        ImGuiSelectableFlags.SpanAllColumns,
                        new Vector2(0, dynRowHeight));

                    ImGui.SetItemAllowOverlap();

                    bool isRowHovered = !anyPopupOpen && ImGui.IsItemHovered();
                    bool isRowRightClicked = !anyPopupOpen && ImGui.IsItemClicked(ImGuiMouseButton.Right);

                    ImGui.PopStyleColor(3);

                    ImGui.SetCursorPosY(rowStartY);
                    ImGui.AlignTextToFramePadding();

                    if (rowClicked)
                    {
                        selectedItemId = item.Id;
                        selectedItemName = item.Name;
                        selectedItemSlot = GetGlamourerSlotId(item.EquipSlotCategory);
                    }

                    if (isItemSelected)
                    {
                        ImGui.TableSetBgColor(ImGuiTableBgTarget.RowBg0, selectedBlueCol);
                    }
                    else if (isRowHovered)
                    {
                        ImGui.TableSetBgColor(ImGuiTableBgTarget.RowBg0, hoverBlueCol);
                    }
                    else
                    {
                        ImGui.TableSetBgColor(ImGuiTableBgTarget.RowBg0, bgToggle ? bgCol1 : bgCol2);
                    }

                    // columns 0-4: F / DF / TF stars, W / DW baskets
                    DrawStarButton(item.Id, plugin.Configuration.Favorites, new Vector4(1.0f, 0.85f, 0.1f, 1.0f), 0, dynRowHeight);

                    ImGui.TableNextColumn();
                    DrawStarButton(item.Id, plugin.Configuration.DoubleFavorites, new Vector4(1.0f, 0.55f, 0.0f, 1.0f), 1, dynRowHeight);

                    ImGui.TableNextColumn();
                    DrawStarButton(item.Id, plugin.Configuration.TripleFavorites, new Vector4(0.95f, 0.2f, 0.2f, 1.0f), 2, dynRowHeight);

                    ImGui.TableNextColumn();
                    DrawWButton(item.Id, plugin.Configuration.Wishlist, new Vector4(1.0f, 0.85f, 0.1f, 1.0f), "wl", dynRowHeight);

                    ImGui.TableNextColumn();
                    DrawWButton(item.Id, plugin.Configuration.DoubleWishlist, new Vector4(0.95f, 0.2f, 0.2f, 1.0f), "dwl", dynRowHeight);

                    // M oszlop bogyó kirajzolása
                    ImGui.TableNextColumn();
                    DrawModButton(item, dynRowHeight, rowStartY);

                    ImGui.TableNextColumn();

                    verticalOffset = Math.Max(0f, (dynRowHeight - ImGui.GetTextLineHeight()) * 0.5f);

                    // column 6: icon + name. areaMin..areaMax covers the name / ID / model cells for the clicks handled below
                    Vector2 areaMin = ImGui.GetCursorScreenPos(); 
                    float cols5To8StartX = ImGui.GetCursorScreenPos().X;
                    bool isIconHovered = false;

                    var iconTexture = Plugin.TextureProvider.GetFromGameIcon(new GameIconLookup(item.Icon)).GetWrapOrDefault();
                    if (iconTexture != null)
                    {
                        float iconPosY = rowStartY + Math.Max(0f, (dynRowHeight - currentIconSize) * 0.5f);
                        ImGui.SetCursorPosY(iconPosY);

                        // Ikon kirajzolása a konfigból vett dinamikus mérettel
                        ImGui.Image(iconTexture.Handle, new Vector2(currentIconSize, currentIconSize));

                        // KÜLÖN HOVER EFFECT az ikon felett: Kinagyított ikon Tooltip
                        if (ImGui.IsItemHovered())
                        {
                            isIconHovered = true;
                            float zoomedSize = currentIconSize * plugin.Configuration.HoverIconScale;

                            // A nagy ikon jobb alsó sarkát a cellabeli ikon jobb alsó sarkához igazítjuk (pivot: 1.0, 1.0)
                            ImGui.SetNextWindowPos(ImGui.GetItemRectMax(), ImGuiCond.Always, new Vector2(1.0f, 1.0f));

                            // A fekete margó/zóna eltüntetése 1 pixeles vékony keretre
                            ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(2f, 2f));
                            ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 2f);

                            ImGui.BeginTooltip();
                            ImGui.Image(iconTexture.Handle, new Vector2(zoomedSize, zoomedSize));
                            ImGui.EndTooltip();

                            ImGui.PopStyleVar(2);
                        }

                        ImGui.SameLine();
                    }

                    ImGui.SetCursorPosY(textPosY);
                    ImGui.TextUnformatted(item.Name);

                    // Column 7: ID
                    ImGui.TableNextColumn();
                    ImGui.SetCursorPosY(textPosY);
                    DrawRightText(item.IdString);

                    // Column 8: Model ID
                    ImGui.TableNextColumn();
                    ImGui.SetCursorPosY(textPosY);
                    DrawRightText(item.ModelString);

                    float col8RightX = ImGui.GetCursorScreenPos().X + ImGui.GetContentRegionAvail().X;
                    Vector2 areaMax = new Vector2(col8RightX, areaMin.Y + dynRowHeight);

                    // Shift+click = link in chat, right-click = preview the item
                    if (!anyPopupOpen && ImGui.IsMouseHoveringRect(areaMin, areaMax, false))
                    {
                        if (!isIconHovered) // csak akkor írjuk felül a cella szövegével, ha az ikon nincs kijelölve
                        {
                            string actionStr = applyMode == 0 ? "apply to current actor." : "Try On.";
                            ImGui.SetTooltip($"Shift+Left-Click: Link item in chat.\nRight-Click: {actionStr}");
                        }

                        if (rowClicked && ImGui.GetIO().KeyShift)
                        {
                            LinkItemToChat(item.Id);
                        }

                        if (isRowRightClicked)
                        {
                            selectedItemId = item.Id;
                            selectedItemName = item.Name;
                            selectedItemSlot = GetGlamourerSlotId(item.EquipSlotCategory);
                            ApplyCurrentItem(item);
                        }
                    }

                    ImGui.TableNextColumn();
                    ImGui.SetCursorPosY(textPosY);
                    // column 9: jobs
                    ImGui.TextUnformatted(item.JobString);

                    // Column 10: Dyes
                    ImGui.TableNextColumn();
                    if (item.DyeCount > 0)
                    {
                        ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1f, 1f, 1f, 0.85f));

                        string splotchIcon = FontAwesomeIcon.Splotch.ToIconString();
                        string dyeText = item.DyeCount == 1 ? splotchIcon : $"{splotchIcon}{splotchIcon}";

                        float fontScale = 0.6f;
                        ImGui.PushFont(UiBuilder.IconFont);

                        // Measure text height at 0.6f scale before setting Y position
                        Vector2 textSize = ImGui.CalcTextSize(dyeText);
                        float scaledHeight = textSize.Y * fontScale;
                        float dyePosY = rowStartY + Math.Max(0f, (dynRowHeight - scaledHeight) * 0.5f);

                        ImGui.SetWindowFontScale(fontScale);
                        ImGui.SetCursorPosY(dyePosY);

                        DrawCenteredText(dyeText);
                        ImGui.PopStyleColor();
                        ImGui.PopFont();
                        ImGui.SetWindowFontScale(1f);
                    }
                    else
                    {
                        ImGui.SetCursorPosY(textPosY);
                        DrawCenteredText("-");
                    }

                    ImGui.TableNextColumn();
                    // column 11: tag badges
                    DrawTagCell(item, dynRowHeight);

                    ImGui.PopID();
                }
            }
            clipper.End();

            ImGui.EndTable();

            ImGui.PopStyleVar();

            // --- footer: counter, scroll-to-cycle button, selected item panel, web links ---
            footerStartPosY = ImGui.GetCursorPosY();

            ImGui.Separator();

            ImGui.Text($"{filteredItems.Count} / {cachedItems.Count} Items Visible");
            ImGui.SameLine(225);

            // hides items that share a model, so every look shows up only once
            if (ImGui.Checkbox("Only Show One Item Per Model Type", ref onlyOnePerModelType)) isDirty = true;

            if (onlyOnePerModelType)
            {
                ImGui.SameLine(0, 15);
                if (ImGui.Checkbox("Ignore Variants", ref ignoreVariants)) isDirty = true;
            }
            else ignoreVariants = false;

            ImGui.SameLine(0, 30);

            float jumpBtnWidth = 24f; // A Jump gomb fix ikon méretű (24px széles)
            float btnSpacing = 4f;   // A gombok közötti távolság
            float rightMargin = 10f;
            float availSpace = ImGui.GetContentRegionAvail().X - rightMargin;
            float remainingWidth = Math.Max(100f, availSpace - jumpBtnWidth - btnSpacing);

            string scrollBtnText = (selectedItemId != 0 && !string.IsNullOrEmpty(selectedItemName))
                ? $"Scrolling | {selectedItemName}"
                : "Scroll Here";

            // 1. SCROLL HERE GOMB (Kitölti a helyet a Jump gomb előtt)
            ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.35f, 0.10f, 0.10f, 0.95f));
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.3f, 0.55f, 0.85f, 0.95f));
            ImGui.Button(scrollBtnText, new Vector2(remainingWidth, 24));
            ImGui.PopStyleColor(2);

            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip("Scroll Mouse Wheel Up/Down here\nto cycle through filtered items!");

                var io = ImGui.GetIO();
                if (io.MouseWheel != 0)
                {
                    CycleSelectedItem(io.MouseWheel > 0 ? -1 : 1);
                }
            }

            // 2. JUMP TO ITEM GOMB (Közvetlenül a Scroll Here gombtól jobbra)
            ImGui.SameLine(0, btnSpacing);

            ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.35f, 0.10f, 0.10f, 0.95f));
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.3f, 0.55f, 0.85f, 0.95f));
            ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.55f, 0.20f, 0.20f, 0.95f));

            // Belső padding nullázása és pontos középre igazítás (X: 0.5, Y: 0.5)
            ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, Vector2.Zero);
            ImGui.PushStyleVar(ImGuiStyleVar.ButtonTextAlign, new Vector2(0.5f, 0.5f));

            ImGui.PushFont(UiBuilder.IconFont);

            // --- IKON VÁLASZTÁSOK ---
            string jumpIconStr = FontAwesomeIcon.AngleDoubleRight.ToIconString();           // 3. Dupla jobbra nyíl (»)

            bool jumpClicked = ImGui.Button($"{jumpIconStr}##JumpToSelectedItem", new Vector2(jumpBtnWidth, 24));
            ImGui.PopFont();
            ImGui.PopStyleVar(2); // Visszaállítjuk a 2 StyleVar-t
            ImGui.PopStyleColor(3);

            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip("Jump to selected item in table");
            }

            if (jumpClicked)
            {
                scrollToSelectedItem = true;
            }
        }

        ImGui.Separator();

            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();

            // selected item panel
            var selectedItemRow = selectedItemId != 0
                ? cachedItems.FirstOrDefault(x => x.Id == selectedItemId)
                : null;

        // A tagek és a bal oldali nagy ikon magasságának összehangolása
        float iconBoxSize = 78.0f;

        // BAL OLDAL: Nagy ikon (ha van kiválasztott elem)
        ImGui.BeginGroup();
        if (selectedItemRow != null)
        {
            var selIconTexture = Plugin.TextureProvider.GetFromGameIcon(new GameIconLookup(selectedItemRow.Icon)).GetWrapOrDefault();
            if (selIconTexture != null)
            {
                ImGui.Image(selIconTexture.Handle, new Vector2(iconBoxSize, iconBoxSize));

                if (ImGui.IsItemHovered())
                {
                    string actionStr = applyMode == 0 ? "apply to current actor." : "Try On.";
                    ImGui.SetTooltip($"Shift+Left-Click: Link item in chat.\nRight-Click: {actionStr}");

                    if (ImGui.IsItemClicked(ImGuiMouseButton.Left) && ImGui.GetIO().KeyShift)
                        LinkItemToChat(selectedItemRow.Id);

                    if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
                        ApplyCurrentItem(selectedItemRow);
                }
            }
        }
        ImGui.EndGroup();

        // JOBB OLDAL: 3 soros dedikált blokk (Csak ha van kiválasztott elem)
        if (selectedItemRow != null)
        {
            ImGui.SameLine(0, 12f);
            ImGui.BeginGroup();

            // Kompakt sorköz beállítása a 3 sor között
            ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(ImGui.GetStyle().ItemSpacing.X, 3f));

            // 1. SOR: Selected Info + Assign / Add Tag
            ImGui.AlignTextToFramePadding();
            ImGui.Text($"Selected: {selectedItemName} (ID: {selectedItemId})");
            ImGui.SameLine(0, 15f);

            if (cachedUniqueTags.Count > 0)
            {
                ImGui.SetNextItemWidth(130);
                if (ImGui.BeginCombo("##AddExistingTag", "Assign Tag..."))
                {
                    ImGui.SetNextItemWidth(-1);
                    ImGui.InputTextWithHint("##AssignTagSearch", "Search...", ref assignTagSearch, 30);
                    ImGui.Separator();

                    for (int i = 0; i < cachedUniqueTags.Count; i++)
                    {
                        string tag = cachedUniqueTags[i];

                        if (!string.IsNullOrEmpty(assignTagSearch) && !tag.Contains(assignTagSearch, StringComparison.OrdinalIgnoreCase))
                            continue;

                        if (ImGui.Selectable(tag))
                        {
                            AddTagToItem(selectedItemId, tag);
                        }
                    }
                    ImGui.EndCombo();
                }
                ImGui.SameLine();
            }

            ImGui.SetNextItemWidth(150);
            bool enterPressed = ImGui.InputText("##NewTag", ref newTagInput, 50, ImGuiInputTextFlags.EnterReturnsTrue);
            ImGui.SameLine();

            if ((enterPressed || ImGui.Button("Add Tag")) && !string.IsNullOrWhiteSpace(newTagInput))
            {
                AddTagToItem(selectedItemId, newTagInput.Trim());
                newTagInput = "";
            }

            // 2. SOR: Active Tags (garantáltan új sorba kerül)
            plugin.Configuration.ItemTags.TryGetValue(selectedItemId, out var currentTags);
            RenderActiveTagsRow(currentTags ?? new List<string>(), selectedItemId);

            // 3. SOR: Affecting Mods (garantáltan új sorba kerül)
            bool isSelModded = plugin.PenumbraIpc.IsItemModded(selectedItemRow.Name, out var selMods);
            RenderAffectingModsRow(isSelModded && selMods != null ? selMods : new List<string>());

            ImGui.PopStyleVar(); // ItemSpacing visszaállítása
            ImGui.EndGroup();
        }

        ImGui.Separator();
        ImGui.Spacing();

        ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.18f, 0.26f, 0.38f, 0.95f));
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.24f, 0.34f, 0.48f, 0.95f));
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.30f, 0.42f, 0.58f, 0.95f));

        // web lookup buttons, all three the same width
        float extraWidth = 30f;
        float padding = ImGui.GetStyle().FramePadding.X * 2;
        float startIndent = 20f;
        float buttonSpacing = 20f;

        ImGui.PushFont(UiBuilder.IconFont);
        float iconWidth = ImGui.CalcTextSize(FontAwesomeIcon.ArrowUpRightFromSquare.ToIconString()).X;
        ImGui.PopFont();

        float maxTextWidth = Math.Max(ImGui.CalcTextSize("Garland Tools").X,
                             Math.Max(ImGui.CalcTextSize("Gamer Escape").X, ImGui.CalcTextSize("Teamcraft").X));

        Vector2 buttonSize = new(iconWidth + 6f + maxTextWidth + padding + extraWidth, 0);

        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + startIndent);

        // 1. Garland Tools
        if (DrawIconButton("gt", FontAwesomeIcon.ArrowUpRightFromSquare, "Garland Tools", buttonSize))
        {
            Util.OpenLink($"https://www.garlandtools.org/db/#item/{selectedItemId}");
        }
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Open item in Garland Tools");

        ImGui.SameLine(0f, buttonSpacing);

        // 2. Gamer Escape
        if (DrawIconButton("ge", FontAwesomeIcon.ArrowUpRightFromSquare, "Gamer Escape", buttonSize))
        {
            string formattedName = Uri.EscapeDataString(selectedItemName.Replace(' ', '_'));
            Util.OpenLink($"https://ffxiv.gamerescape.com/wiki/{formattedName}");
        }
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Open item in Gamer Escape");

        ImGui.SameLine(0f, buttonSpacing);

        // 3. Teamcraft
        if (DrawIconButton("tc", FontAwesomeIcon.ArrowUpRightFromSquare, "Teamcraft", buttonSize))
        {
            Util.OpenLink($"https://ffxivteamcraft.com/db/en/item/{selectedItemId}");
        }
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Open item in Teamcraft");

        ImGui.PopStyleColor(3);

        // Penumbra recatch + redraw button, right aligned
        float redrawBtnWidth = 300f;
        float redrawavailX = ImGui.GetContentRegionAvail().X;
        if (redrawavailX > redrawBtnWidth)
        {
            // Adjusted offset slightly (-25f) to accommodate the HelpMarker icon
            ImGui.SameLine(ImGui.GetWindowWidth() - redrawBtnWidth - ImGui.GetStyle().WindowPadding.X - 20f - 25f);

            HelpMarker2("• Glamour Tagger caches Penumbra item data only upon startup and when clicking this button.\n" +
                        "• Because background Penumbra changes are not continuously tracked in real time, please click this button " +
                        "to refresh and recatch mods.");

            ImGui.SameLine(0, 5f);
            ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.35f, 0.10f, 0.10f, 0.95f));         // Base dark red
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.45f, 0.15f, 0.15f, 0.95f)); // Slightly brighter on hover
            ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.55f, 0.20f, 0.20f, 0.95f));  // Even brighter when clicked
            bool ClearClicked = ImGui.Button("Recatch Penumbra & Redraw Self", new Vector2(redrawBtnWidth, 0));
            ImGui.PopStyleColor(3);
            if (ClearClicked)
            {
                plugin.PenumbraIpc.RedrawSelfAndRefresh();
            }
        }

        // Dynamic footer height calculation for the table size
        float measuredFooterHeight = ImGui.GetCursorPosY() - footerStartPosY;
        footerHeight = Math.Max(175f, measuredFooterHeight);

        if (showRestoreManagerWindow)
        {
            DrawRestoreManagerWindow();
        }
        DrawBackupConfirmModals();
    }
}
