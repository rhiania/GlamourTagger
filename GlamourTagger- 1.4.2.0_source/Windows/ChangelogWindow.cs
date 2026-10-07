using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Windowing;
using Dalamud.Utility;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace GlamourTagger.Windows;

// one release: version, date, lines (a line starting with '!' gets highlighted)
public record ChangelogEntry(string Version, string Date, string[] Changes);

// newest first
public static class ChangelogData
{
    public static readonly ChangelogEntry[] Entries = new ChangelogEntry[]
    {
        new("1.4.2.0", "2026-10-05", new[]
        {
            "!Added Middle-click and Ctrl+Middle-click on Equipment Bar slots to remove the previewed item from that slot or to equip the matching Emperor's New piece.",
            "Fixed 'Keep current dye' in Fitting Room Try On mode: items no longer turn glossy black. With Glamourer running, dyes are as set in Glamourer, without Glamourer the item is tried on undyed.",
            "Fixed an issue in Fitting Room Try On mode where the brush applied the selected Dye Set instead of its own when painting a 'No Dye' + 'No Dye' set.",
            "Fixed an issue in Fitting Room Try On mode where shield dyes were not removed when both dye channels were set to 'No Dye'."    
        }),

        new("1.4.1.0", "2026-10-03", new[]
        {
            "!Added 'Current Job' quick search button to the Item Name header: one click lists items for your current job (j:), clicking again clears it.",
            "!Added Dye Target selection on the Equipment Bar: Shift+click slots (or by using the new dye target toggle) to choose which slots the brush dyes, without changing the slot filter.",
            "Added droplet markers on the Equipment Bar to show which slots and rings will be dyed.",
            "Fixed middle-click filter reset on headers with search boxes (Item Name, Item ID, Model ID).",
            "Fixed 'Clear all filter' not clearing the Model ID filter.",
            "Fixed the PM header highlight not reacting when only one mod state was filtered out."
        }),

        new("1.4.0.0", "2026-09-26", new[]
        {
            "!Added Dye Set integration for Item Previews in both Glamourer Preview and Fitting Room Try On modes.",
            "!Added 'Apply Color' brush tool on the Equipment Bar to apply any Dye Set directly to selected slot previews.",
            "!Added Dye Swap button inside Dye Sets to quickly swap primary and secondary dyes.",
            "!Added Native Fitting Room Memory Assistant window to reliably handle dye previews in native Fitting Room mode.",            
            "!Added 'Revert Character to game state' button to quickly restore your original ingame character appearance.",
            "Fixed popup click-through, now blocking accidental button presses behind active popups.",
            "General small UI polish for the new feature."
        }),

        new("1.3.0.1", "2026-09-20", new[]
        {
            "Fixed the issue Glamour Tagger not saving previous Penumbra cache catches between sessions."
        }),

        new("1.3.0.0", "2026-09-19", new[]
        {
            "!Added Right/Left Ring toggles for item previews for both hands (with automatic fallback safety measures).*",
            "!Added 'Penumbra Mods' column and 'Affecting Mods' display powered by Penumbra IPC integration to list modded items and all affecting mods.*",
            "!Added manual mod toggle for the Demodded state to mark mods on specific items as inactive or exclude items with false/partial mod installations from mod filters.",
            "!Added manual mod toggle for the Unusable state to mark mods on specific items as broken or mark items as unusable.",
            "!Added filter options for the new Unmodded and Unusable item states, as well as specific mod states.",
            "!Added Penumbra self - redraw and manual data recatch trigger.",
            "!Added Penumbra Cache Catch Notification system with functions to jump to items and apply items directly from the window.Also added settings for muting notifications.",
            "Implemented complete standalone failsafes to run Glamour Tagger gracefully without Glamourer or Penumbra.",
            "Fixed main table scrollbar truncation issue where bottom items were cut off with specific item counts.",
            "Fixed tag cell click handling so click actions trigger across the entire cell area instead of strictly on tag badges.",
            "Fixed column sorting to automatically jump/scroll to the first sorted item in the list.",
            "Fixed missing 'Select All' and 'Clear All' options in the Dye column filter popup.",
            "Fixed misalignment in table headers.",
            "Adjusted icons and design of the main table.\n\n",

            "* Thank you for the feedback and ideas, AngelaKatt!",
        }),

        new("1.2.0.0", "2026-09-13", new[]
        {
            "!Added Automatic Backup & Restore Manager with autosaves, pre-import backups, and Merge/Full Reset restore options.",
            "!Added Equipment Selection Bar above the main item table for fast visual slot filtering.*",
            "!Added 'Jump to Item' button next to the scroll preview box to instantly scroll the table to the selected item.",
            "!Improved table header ergonomics: left-click headers directly opens filter popups, while dedicated small buttons handle sorting.*",
            "!Added list icon scaling options alongside enlarged icon previews on tooltips and on the bottom.*",
            "Improved tag overflow handling in table cells to maintain clean layout alignment.",
            "Implemented some design adjustments.",
            "Rebuilt the file system for easier plugin management & development.\n ",
            "* Thank you for the feedback and ideas AngelaKatt!"
        }),

        new("1.1.0.1", "2026-09-09", new[]
        {
            "Fixed update changelog window to appear on the first UI opening after an update."
        }),

        new("1.1.0.0", "2026-09-09", new[]
        {
            "!Added 'not' filter logic for the tag filters, working alongside both the 'or' and 'and' logic.",
            "!Added options to search in the main search box for tags (t:), jobs (j:), ids (id:), model ids (m:) with prefixes.",
            "!Added 'search tag' options when filtering by tags or adding existing tags for items and for the tag manager table.",
            "!Export and import functionality now includes Favourites (F, DF, TF) and Wishlists (WL, DWL) too.",
            "Added change log windows and info.",
            "Added contrast to odd rows with different main model ID and for the selected row.",
            "Adjusted wording and added tooltips."

        }),
        new("1.0.2.1", "2026-09-03", new[]
        {
            "Debugged item folders and patched up the Guide."
        }),
        new("1.0.2.0", "2026-09-03", new[]
        {
            "!Removed Ctrl requirement from scrolling through items",
            "Item currently previewed shows up on the 'scroll here' button",
            "!Added middle-click function to headers to clear the filters in the columns",
            "Filtered columns now light up",
            "Smaller adjustments for tables and the design"
        }),
        new("1.0.1.0", "2026-08-30", new[]
        {
            "Added external link buttons for Garland Tools, Gamer Escape, and Teamcraft\n ",
            "* Thank you for the testing and feedbacks Sleepy!"
        }),
        new("1.0.0.0", "2026-08-30", new[]
        {
            "Initial release of Glamour Tagger."
        })
    };
}

public class ChangelogWindow : Window, IDisposable
{
    private readonly Plugin plugin;
    private bool showFullChangelog = false;
    private string filterFromVersion = string.Empty;
    private int visibleCount = 10;

    public ChangelogWindow(Plugin plugin) : base("Glamour Tagger - Update Notes", ImGuiWindowFlags.NoCollapse)
    {
        this.plugin = plugin;
        this.Size = new Vector2(670, 420);
        this.SizeCondition = ImGuiCond.FirstUseEver;
    }

    // after an update: only the entries newer than the version the user last saw
    public void ShowUpdateNotification(string oldVersion)
    {
        showFullChangelog = false;
        filterFromVersion = oldVersion ?? string.Empty;
        visibleCount = 10;
        this.WindowName = "Glamour Tagger - Update Notes";

        // Automatikus magasság a tartalomhoz igazítva (min 120px, max 420px)
        this.Flags = ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.AlwaysAutoResize;
        this.SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(780, 120),
            MaximumSize = new Vector2(780, 600)
        };

        this.IsOpen = true;
    }

    // Options -> Changelog: everything, 10 entries at a time
    public void ShowAllLogs()
    {
        showFullChangelog = true;
        filterFromVersion = string.Empty;
        visibleCount = 10;
        this.WindowName = "Glamour Tagger - Changelog";

        // Visszaállítás a normál fix/átméretezhető ablakra
        this.Flags = ImGuiWindowFlags.NoCollapse;
        this.SizeConstraints = null;
        this.Size = new Vector2(780, 600);
        this.SizeCondition = ImGuiCond.Appearing;

        this.IsOpen = true;
    }

    public override void Draw()
    {
        try
        {
            var entriesToShow = GetEntriesToShow();

            if (entriesToShow.Count == 0)
            {
                ImGui.TextUnformatted("No new updates to display.");
                return;
            }

            // the update popup lists all new entries, the full changelog is paged
            int countToDisplay = showFullChangelog ? Math.Min(visibleCount, entriesToShow.Count) : entriesToShow.Count;

            for (int i = 0; i < countToDisplay; i++)
            {
                var entry = entriesToShow[i];
                ImGui.TextColored(new Vector4(0.4f, 0.8f, 1.0f, 1.0f), $"Version {entry.Version}");
                if (!string.IsNullOrEmpty(entry.Date))
                {
                    ImGui.SameLine();
                    ImGui.TextDisabled($"({entry.Date})");
                }

                ImGui.Indent(12f);
                foreach (var change in entry.Changes)
                {
                    ImGui.Bullet();
                    ImGui.SameLine();

                    // '!' lines = the bigger changes, drawn in a warmer colour
                    if (change.StartsWith("!"))
                    {
                        Vector4 sarga = new Vector4(1f, 0.96f, 0.7f, 1.0f); 
                        ImGui.PushStyleColor(ImGuiCol.Text, sarga);
                        ImGui.TextWrapped(change);
                        ImGui.PopStyleColor();
                    }
                    else
                    {
                        Vector4 szurke = new Vector4(1f, 1f, 1f, 0.7f); 
                        ImGui.PushStyleColor(ImGuiCol.Text, szurke);
                        ImGui.TextWrapped(change);
                        ImGui.PopStyleColor();
                    }
                }
                ImGui.Unindent(12f);
                ImGui.Spacing();
                ImGui.Separator();
                ImGui.Spacing();
            }

            // --- GitHub Roadmap & Feature Request Button ---
            ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.18f, 0.26f, 0.38f, 0.95f));

            if (DrawIconButton("github_roadmap", FontAwesomeIcon.ArrowUpRightFromSquare, "GitHub — See what's next / Feature request", new Vector2(-1, 28)))
            {
                Util.OpenLink("https://github.com/rhiania/GlamourTagger/discussions");
            }
            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip("Open GitHub repository to view upcoming features or submit feedback");
            }

            ImGui.PopStyleColor(1);
            ImGui.Spacing();

            // paging for the full changelog
            if (showFullChangelog && visibleCount < entriesToShow.Count)
            {
                if (ImGui.Button("Older updates", new Vector2(-1, 28)))
                {
                    visibleCount += 10;
                }
                ImGui.Spacing();
            }

            if (!showFullChangelog)
            {
                ImGui.Spacing();
                if (ImGui.Button("View Full Changelog", new Vector2(-1, 28)))
                {
                    ShowAllLogs();
                }
            }
        }
        catch (Exception ex)
        {
            this.IsOpen = false;
            System.Diagnostics.Debug.WriteLine($"Error rendering ChangelogWindow: {ex}");
        }
    }

    // empty button with the icon + text drawn on top, centred as one block
    private bool DrawIconButton(string id, FontAwesomeIcon icon, string text, Vector2 size)
    {
        Vector2 cursorPos = ImGui.GetCursorScreenPos();
        Vector2 itemSize = new Vector2(
            size.X < 0 ? ImGui.GetContentRegionAvail().X : size.X,
            size.Y
        );

        bool clicked = ImGui.Button($"##{id}", itemSize);

        Vector2 actualSize = ImGui.GetItemRectSize();

        ImGui.PushFont(UiBuilder.IconFont);
        float iconWidth = ImGui.CalcTextSize(icon.ToIconString()).X;
        ImGui.PopFont();

        float textWidth = ImGui.CalcTextSize(text).X;
        float spacing = 6f;
        float totalWidth = iconWidth + spacing + textWidth;

        float startX = cursorPos.X + (actualSize.X - totalWidth) * 0.5f;
        float startY = cursorPos.Y + (actualSize.Y - ImGui.GetTextLineHeight()) * 0.5f;

        var drawList = ImGui.GetWindowDrawList();
        uint textColor = ImGui.GetColorU32(ImGuiCol.Text);

        drawList.AddText(UiBuilder.IconFont, ImGui.GetFontSize(), new Vector2(startX, startY), textColor, icon.ToIconString());
        drawList.AddText(ImGui.GetFont(), ImGui.GetFontSize(), new Vector2(startX + iconWidth + spacing, startY), textColor, text);

        return clicked;
    }

    // all entries, or only the versions above filterFromVersion
    private List<ChangelogEntry> GetEntriesToShow()
    {
        if (showFullChangelog || string.IsNullOrEmpty(filterFromVersion))
        {
            return ChangelogData.Entries.ToList();
        }

        if (!Version.TryParse(filterFromVersion, out var oldVer))
        {
            return ChangelogData.Entries.ToList();
        }

        var result = new List<ChangelogEntry>();
        foreach (var entry in ChangelogData.Entries)
        {
            if (Version.TryParse(entry.Version, out var entryVer) && entryVer > oldVer)
            {
                result.Add(entry);
            }
        }
        return result;
    }

    public void Dispose() { }
}
