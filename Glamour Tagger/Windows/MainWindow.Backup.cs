using Dalamud.Bindings.ImGui;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;

namespace GlamourTagger.Windows;

public partial class MainWindow
{
    // --- Backup & Restore Manager window (opened from Options) ---
    private bool showRestoreManagerWindow = false;
    private readonly HashSet<string> selectedBackupFiles = new();
    private int restoreMode = 0; // 0 = Merge, 1 = Full Reset
    private string? selectedBackupToRestore = null;

    private bool showRestoreConfirmModal = false;
    private bool showDeleteConfirmModal = false;
    private bool isDeleteAllTarget = false;

    private void DrawRestoreManagerWindow()
    {
        // Az ImGuiWindowFlags.AlwaysAutoResize biztosítja, hogy az ablak magassága és szélessége automatikusan igazodjon a tartalomhoz.
        if (!ImGui.Begin("Backup & Restore Manager", ref showRestoreManagerWindow, ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.AlwaysAutoResize))
        {
            ImGui.End();
            return;
        }

        ImGui.TextWrapped("Automatic backups are saved periodically to the plugin folder to prevent data loss mainly for tags, favorites and whishlist flags.");

        ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.85f, 0.85f, 0.35f, 1.0f));
        ImGui.TextWrapped("Note: This feature only manages automatic backups. Custom manual exports (.json files) are not affected.");
        ImGui.PopStyleColor();

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        // backup files, one checkbox each
        var backups = plugin.BackupManager.GetAvailableBackups();

        ImGui.Indent(30f);
        ImGui.TextUnformatted($"Available Backups ({backups.Count}):");
        ImGui.Unindent(30f);

        // ImGuiTableFlags.SizingFixedFit segítségével a táblázat szélessége pontosan a tartalomhoz igazodik, szövegtakarás nélkül.
        if (ImGui.BeginTable("BackupListTable", 4, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingFixedFit, System.Numerics.Vector2.Zero))
        {
            ImGui.TableSetupColumn("##select", ImGuiTableColumnFlags.WidthFixed, 25);
            ImGui.TableSetupColumn("File Name", ImGuiTableColumnFlags.WidthFixed); // Automatikusan felveszi a leghosszabb fájlnév méretét
            ImGui.TableSetupColumn("Type", ImGuiTableColumnFlags.WidthFixed, 130);
            ImGui.TableSetupColumn("Date & Time", ImGuiTableColumnFlags.WidthFixed, 150); // Bőséges hely a teljes YYYY-MM-DD HH:mm:ss dátumnak
            ImGui.TableHeadersRow();

            foreach (var backup in backups)
            {
                ImGui.TableNextRow();

                ImGui.TableSetColumnIndex(0);
                bool isChecked = selectedBackupFiles.Contains(backup.FilePath);
                if (ImGui.Checkbox($"##chk_{backup.FileName}", ref isChecked))
                {
                    if (isChecked) selectedBackupFiles.Add(backup.FilePath);
                    else selectedBackupFiles.Remove(backup.FilePath);
                }

                ImGui.TableSetColumnIndex(1);
                ImGui.TextUnformatted(backup.FileName);

                ImGui.TableSetColumnIndex(2);
                ImGui.TextUnformatted(backup.DisplayType);

                ImGui.TableSetColumnIndex(3);
                ImGui.TextUnformatted(backup.CreationTime.ToString("yyyy-MM-dd HH:mm:ss"));
            }

            ImGui.EndTable();
        }

        // Törlési opciók a táblázat alatt
        var io = ImGui.GetIO();
        bool isShiftCtrlDown = io.KeyCtrl && io.KeyShift;

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        ImGui.Indent(30f);
        // the delete buttons only work while Shift+Ctrl is held
        ImGui.BeginDisabled(!isShiftCtrlDown);

        if (ImGui.Button("Delete Selected Backups") && selectedBackupFiles.Count > 0)
        {
            isDeleteAllTarget = false;
            showDeleteConfirmModal = true;
        }

        ImGui.SameLine();

        if (ImGui.Button("Delete ALL Backups") && backups.Count > 0)
        {
            isDeleteAllTarget = true;
            showDeleteConfirmModal = true;
        }

        ImGui.EndDisabled();

        ImGui.TextDisabled("Hold [Shift + Ctrl] to enable backup deletion buttons.");
        ImGui.Unindent(30f);

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        // 2. Restore (Visszaállítás) szekció
        ImGui.TextUnformatted("Restore Configuration:");

        // the list is newest first, so this is the newest of the checked ones
        var latestSelectedBackup = backups.FirstOrDefault(b => selectedBackupFiles.Contains(b.FilePath));

        //jav
        //var availableBackups = plugin.BackupManager.GetAvailableBackups();
        //// the list is newest first, so this is the newest of the checked ones
        //var latestSelectedBackup = availableBackups.FirstOrDefault(b => selectedBackupFiles.Contains(b.FilePath));


        if (latestSelectedBackup == null)
        {
            ImGui.TextDisabled("Select at least one backup file using the checkboxes above to restore.");
        }
        else
        {
            ImGui.TextColored(new Vector4(0.4f, 0.8f, 1.0f, 1.0f), $"Selected for restore (latest checked): {latestSelectedBackup.FileName}");
        }

        ImGui.Spacing();
        ImGui.Indent(15f);
        ImGui.RadioButton(" Merge Mode (Combines saved tags/favs/wishlists with current settings)", ref restoreMode, 0);
        ImGui.RadioButton(" Full Reset Mode (Clears current settings and replaces with backup)", ref restoreMode, 1);

        ImGui.Spacing();
        ImGui.Indent(30f);

        ImGui.BeginDisabled(latestSelectedBackup == null);
        if (ImGui.Button("Restore Backup", new Vector2(160, 0)))
        {
            if (latestSelectedBackup != null)
            {
                selectedBackupToRestore = latestSelectedBackup.FilePath;
                showRestoreConfirmModal = true;
            }
        }
        ImGui.EndDisabled();
        ImGui.Unindent(30f);

        ImGui.End();
    }

    // both confirm popups are drawn from MainWindow.Draw every frame and open through the show* flags
    private void DrawBackupConfirmModals()
    {
        // --- RESTORE CONFIRMATION MODAL ---
        if (showRestoreConfirmModal && selectedBackupToRestore != null)
        {
            ImGui.OpenPopup("Confirm Restore##RestoreModal");
        }

        if (ImGui.BeginPopupModal("Confirm Restore##RestoreModal", ref showRestoreConfirmModal, ImGuiWindowFlags.AlwaysAutoResize))
        {
            string fileName = Path.GetFileName(selectedBackupToRestore ?? "");
            string modeText = restoreMode == 0
                ? "MERGE (Keep existing & add missing tags & flags)"
                : "FULL RESET (Overwrites all current tags & flags)";

            ImGui.TextUnformatted("Are you sure you want to restore this backup?");
            ImGui.Spacing();
            ImGui.Text($"File: {fileName}");
            ImGui.Text($"Mode: {modeText}");
            ImGui.Spacing();
            ImGui.TextColored(new Vector4(1.0f, 0.8f, 0.2f, 1.0f), "Warning: This action will update your active configuration.");
            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();

            if (ImGui.Button("Yes, Restore", new Vector2(120, 0)))
            {
                bool isMerge = (restoreMode == 0);
                // restore, then refresh everything that caches tags
                if (plugin.BackupManager.RestoreFromBackup(selectedBackupToRestore!, isMerge))
                {
                    plugin.Configuration.Save();
                    RebuildTagCache();
                    isDirty = true;
                    Plugin.ChatGui.Print($"[Glamour Tagger] Successfully restored: {fileName}");
                }
                else
                {
                    Plugin.ChatGui.Print($"[Glamour Tagger Error] Failed to restore backup: {fileName}");
                }

                selectedBackupToRestore = null;
                showRestoreConfirmModal = false;
                ImGui.CloseCurrentPopup();
            }

            ImGui.SameLine();

            if (ImGui.Button("Cancel", new Vector2(120, 0)))
            {
                selectedBackupToRestore = null;
                showRestoreConfirmModal = false;
                ImGui.CloseCurrentPopup();
            }

            ImGui.EndPopup();
        }

        // --- DELETE CONFIRMATION MODAL ---
        if (showDeleteConfirmModal)
        {
            ImGui.OpenPopup("Confirm Delete Backups##Modal");
        }

        if (ImGui.BeginPopupModal("Confirm Delete Backups##Modal", ref showDeleteConfirmModal, ImGuiWindowFlags.AlwaysAutoResize))
        {
            if (isDeleteAllTarget)
            {
                ImGui.Text("Are you sure you want to DELETE ALL backup files?\nThis action cannot be undone!");
            }
            else
            {
                ImGui.Text($"Are you sure you want to delete {selectedBackupFiles.Count} selected backup file(s)?");
            }

            ImGui.Spacing();

            if (ImGui.Button("Yes, Delete", new Vector2(120, 0)))
            {
                if (isDeleteAllTarget)
                {
                    plugin.BackupManager.DeleteAllBackups();
                    selectedBackupFiles.Clear();
                    selectedBackupToRestore = null;
                    Plugin.ChatGui.Print("[Glamour Tagger] All backups have been deleted.");
                }
                else
                {
                    plugin.BackupManager.DeleteBackupFiles(selectedBackupFiles);
                    selectedBackupFiles.Clear();
                    selectedBackupToRestore = null;
                    Plugin.ChatGui.Print("[Glamour Tagger] Selected backups have been deleted.");
                }
                showDeleteConfirmModal = false;
                ImGui.CloseCurrentPopup();
            }

            ImGui.SameLine();

            if (ImGui.Button("Cancel", new Vector2(120, 0)))
            {
                showDeleteConfirmModal = false;
                ImGui.CloseCurrentPopup();
            }

            ImGui.EndPopup();
        }
    }
}
