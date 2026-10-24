using Dalamud.Bindings.ImGui;
using System.Numerics;

namespace GlamourTagger.Windows;

public partial class MainWindow
{
    // 0 = Glamourer Preview, 1 = Fitting Room Try On
    private int applyMode = 0;

    // (!) marker with a hover tooltip
    private static void HelpMarker(string desc)
    {
        ImGui.TextDisabled("(!)");
        if (ImGui.IsItemHovered())
        {
            ImGui.BeginTooltip();
            ImGui.PushTextWrapPos(ImGui.GetFontSize() * 35.0f);
            ImGui.TextUnformatted(desc);
            ImGui.PopTextWrapPos();
            ImGui.EndTooltip();
        }
    }

    // same, but (?)
    private static void HelpMarker2(string desc)
    {
        ImGui.TextDisabled("(?)");
        if (ImGui.IsItemHovered())
        {
            ImGui.BeginTooltip();
            ImGui.PushTextWrapPos(ImGui.GetFontSize() * 35.0f);
            ImGui.TextUnformatted(desc);
            ImGui.PopTextWrapPos();
            ImGui.EndTooltip();
        }
    }

    // red Options button of the top bar + its dropdown
    private void DrawOptionsDropdown()
    {
        float indent1 = 15f;
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + indent1);
        ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.35f, 0.10f, 0.10f, 0.95f));
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.45f, 0.15f, 0.15f, 0.95f));
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.55f, 0.20f, 0.20f, 0.95f));
        bool optionsClicked = ImGui.Button("Options", new Vector2(110f, 0));
        ImGui.PopStyleColor(3);

        if (optionsClicked)
        {
            ImGui.OpenPopup("OptionsDropdownPopup");
        }

        ImGui.SetNextWindowSize(new Vector2(250f, 0));

        if (ImGui.BeginPopup("OptionsDropdownPopup"))
        {
            ImGui.Spacing(); ImGui.Spacing(); ImGui.Spacing(); ImGui.Spacing(); ImGui.Spacing();
            ImGui.Separator();

            if (ImGui.Button("Visual & Notification Settings", new Vector2(-1, 0)))
            {
                plugin.SettingsWindow.Toggle();
                ImGui.CloseCurrentPopup();
            }
            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip("Open settings to adjust UI visuals, icon scales, and Penumbra catch notifications.");
            }

            ImGui.Spacing(); ImGui.Spacing();
            ImGui.Separator();
            ImGui.TextUnformatted("Glamourer's Favourites");

            // reads Glamourer's own favourites from disk and adds them to F
            if (ImGui.Button("Sync Favorites here", new Vector2(-1, 0)))
            {
                plugin.ImportFavoritesFromGlamourer();
            }
            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip("Imports favorited items from Glamourer into the mod's Favorites (F) list.");
            }

            ImGui.Separator();
            ImGui.Spacing(); ImGui.Spacing();
            ImGui.Separator();
            ImGui.TextUnformatted("Tag & Flag Management");

            if (ImGui.Button("Open Tag & Flag Manager", new Vector2(-1, 0)))
            {
                showTagManagerWindow = !showTagManagerWindow;
                tagManagerCacheDirty = true;
                ImGui.CloseCurrentPopup();
            }
            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip("Open the Tag & Flag Manager window to rename, recolor, delete, or create tags,\nas well as for the tag & flag export and import functions.");
            }

            if (ImGui.Button("Open Backup & Restore Manager", new Vector2(-1, 0)))
            {
                showRestoreManagerWindow = !showRestoreManagerWindow;
                ImGui.CloseCurrentPopup();
            }
            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip("Manage automatic backups, restore tags/flags/wishlists, or delete old backups.");
            }

            ImGui.Separator();
            ImGui.Spacing(); ImGui.Spacing(); ImGui.Spacing(); ImGui.Spacing();
            ImGui.Separator();

            if (ImGui.Button("Changelog", new Vector2(-1, 0)))
            {
                plugin.ChangelogWindow.ShowAllLogs();
                ImGui.CloseCurrentPopup();
            }

            ImGui.Separator();

            if (ImGui.Button("Guide & Thanks", new Vector2(-1, 0)))
            {
                plugin.GuideWindow.Toggle();
                ImGui.CloseCurrentPopup();
            }

            ImGui.EndPopup();
        }
    }

    // dev only: fake an update from an older version to check the changelog popup (see the commented-out call in Draw)
    private string debugVersionInput = "1.0.0.0";

    private void DrawDebugChangelogControls()
    {
        if (ImGui.Button("Test Update from 1.0.0.0"))
        {
            plugin.ChangelogWindow?.ShowUpdateNotification("1.0.0.0");
        }
        ImGui.SameLine();
        ImGui.SetNextItemWidth(90f);
        ImGui.InputText("##DebugVerInput", ref debugVersionInput, 20);
        ImGui.SameLine();
        if (ImGui.Button("Test Update from Typed Version"))
        {
            plugin.ChangelogWindow?.ShowUpdateNotification(debugVersionInput);
        }
        ImGui.Separator();
    }
}
