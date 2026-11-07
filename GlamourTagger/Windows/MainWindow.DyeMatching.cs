using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace GlamourTagger.Windows;

// "Dye Matching Colors Only" (DyeMatching in the code): the add-on mode of the Instant Hover Over Dye Preview
// (MainWindow_DyeHover.cs). While it is ON, only the dye channels that match the set's dyes get the shown color.
//   MainWindow_DyeHover.cs      BeginDyePickerSession   -> CanUseDyeMatching, EnsureDyeMatchGroups
//                               BeginDyePreviewFrame    -> DrawDyeMatchingToggle
//                               EndDyePreviewFrame      -> DrawDyeMatchingLabel
//                               UpdateDyePreviewTargets -> EnsureDyeMatchGroups
//                               Ctrl+click handlers     -> SplitDyeMatchGroups
//   MainWindow_Dyeing.cs        ResolveDyes             -> session MatchGroups (DyeMatchGroups.TryResolve)
//   MainWindow_DyeDraw.cs       DrawDyeSetSquare, FitDyePickerPosition -> DyeMatchingAvailable, DyeMatchingBannerWidth
// Its per-popup state (MatchingOn, MatchGroups, ...) lives in DyePreviewSession, next to the rest of the popup's state.

public partial class MainWindow
{
    // false = only the greyed-out "coming soon" toggle - before tested
    private static readonly bool DyeMatchingAvailable = true;
    private static readonly string DyeMatchingToggleGlyph = FontAwesomeIcon.Rainbow.ToIconString();
    private const float DyeMatchingToggleGlyphSize = 16f;
    private const float DyeMatchingToggleNudgeY = 1f;    // + = down, - = up
    private const float DyeMatchingHelpGap = 6f;          // gap between its (?) help marker and the toggle
    private const string DyeMatchingComingSoonTooltip = "Dye Matching Colors Only - coming soon";
    private const string DyeMatchingUnavailableTooltip = "Dye Matching Colors Only: the dyes couldn't be read (see /xllog)";
    private const string DyeMatchingFallbackLine = "Glamourer isn't running - falling back to the Fitting Room:";

    // Toggle color while ON/OFF
    //private static readonly Vector4 DyeMatchingOnColor = new(0.9f, 0.9f, 0.68f, 0.95f);    // Equipment Bar gold, like the vertical labels
    private static readonly Vector4 DyeMatchingOnColor = new(0.65f, 0.15f, 0.15f, 1f);       // droplet red, like the Dye Target toggle
    private static readonly Vector4 DyeMatchingOffColor = new(0.9f, 0.9f, 0.68f, 0.95f);     // Equipment Bar gold
    //private static readonly Vector4 DyeMatchingOffColor = new(0.9f, 0.9f, 0.68f, 0.3f);    // dim gold, like an unselected slot


    // Second vertical label, right of the color rows, only while the mode is ON (font size and alpha shared with the left one)
    private const string DyeMatchingLabelText = "DYE MATCHING COLORS ONLY";
    private const float DyeMatchingLabelGap = 6f;          // space between the last swatch column and the label strip
    private const float DyeMatchingLabelStripWidth = 26f;  // width of the strip (widens the popup while ON); must be > 0
    private const float DyeMatchingLabelNudgeX = 0f;       // + = right, - = left
    private const float DyeMatchingLabelNudgeY = -12f;       // + = down, - = up (applied after vertical centring)
    private const float DyeMatchingLabelLetterGap = 0f;   // extra pixels between letters (negative = tighter)
    private const float DyeMatchingLabelWordGap = 20f;     // height of the gap between words
    private const float DyeMatchingBannerWidth = DyeMatchingLabelGap + DyeMatchingLabelStripWidth; // what ON adds to the popup's width

    private const string DyeMatchingHelpText =
        "DYE MATCHING COLORS ONLY\n" +
        "Recolors every dye channel on the dye targets that has the same color as the clicked dye (channel 1 and 2 alike).\n" +
        "Ctrl does the same for the set's other dye.\n" +
        "Right clicks work the same as if matching is off.\n" +
        "If both dyes of the set are the same, everything matching changes together until you Ctrl+click (this splits dye groups into channels).\n" +
        "Uses Glamourer dyes in Glamourer Preview mode, and in-game equipped item dyes in Fitting Room mode (or if Glamourer isn't running).\n" + FittingRoomSkipsAccessoriesNote;

    private static readonly string[] DyeMatchingLabelLetters = DyeMatchingLabelText.Select(c => c.ToString()).ToArray();

    private static readonly VerticalLabelLayout DyeMatchingLabelLayout = new(DyeMatchingLabelStripWidth, DyeMatchingLabelLetterGap,
        DyeMatchingLabelWordGap, new Vector2(DyeMatchingLabelNudgeX, DyeMatchingLabelNudgeY));

    // ---------------------------------------------------------------------------------------------
    // Toggle, tooltip and label
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// Right end of the header row: the Dye Matching Colors Only toggle, then its (?) help marker, right-aligned to the
    /// swatch columns of the last frame - not to the popup's edge, so the popup shrinks back when the mode goes OFF.
    /// The toggle is only greyed out when the dyes couldn't be read, and then it says so.
    /// </summary>
    private void DrawDyeMatchingToggle(DyePreviewSession session)
    {
        Vector2 glyphSize = MeasureIconGlyph(DyeMatchingToggleGlyph, DyeMatchingToggleGlyphSize);
        float width = MathF.Max(1f, glyphSize.X);
        float lineHeight = ImGui.GetTextLineHeight();
        float helpWidth = DyeMatchingAvailable ? DyeMatchingHelpGap + ImGui.CalcTextSize("(?)").X : 0f;

        // Right-aligned on the title's row. On the first frame there are no swatch columns yet: the popup's edge
        ImGui.SameLine();
        float rightEdge = session.LastSwatchesRight > 0f
            ? session.LastSwatchesRight
            : ImGui.GetCursorScreenPos().X + ImGui.GetContentRegionAvail().X;
        float freeSpace = rightEdge - ImGui.GetCursorScreenPos().X - width - helpWidth;
        if (freeSpace > 0f) ImGui.SetCursorPosX(ImGui.GetCursorPosX() + freeSpace);

        Vector2 position = ImGui.GetCursorScreenPos();
        bool clicked = ImGui.InvisibleButton("##DyeMatchingToggle", new Vector2(width, lineHeight));
        bool hovered = ImGui.IsItemHovered();
        bool usable = DyeMatchingAvailable && CanUseDyeMatching(session);

        if (clicked && usable) SetDyeMatching(session, !session.MatchingOn);

        Vector4 color = !usable ? ImGui.GetStyle().Colors[(int)ImGuiCol.TextDisabled]
                      : session.MatchingOn ? DyeMatchingOnColor
                      : DyeMatchingOffColor;
        if (usable && hovered) color = Vector4.Lerp(color, Vector4.One, 0.5f);

        Vector2 glyphPosition = new(position.X, position.Y + (lineHeight - glyphSize.Y) * 0.5f + DyeMatchingToggleNudgeY);
        ImGui.GetWindowDrawList().AddText(UiBuilder.IconFont, DyeMatchingToggleGlyphSize, glyphPosition, ImGui.GetColorU32(color), DyeMatchingToggleGlyph);

        if (hovered) ImGui.SetTooltip(GetDyeMatchingTooltip(session, usable));

        if (DyeMatchingAvailable)
        {
            ImGui.SameLine(0, DyeMatchingHelpGap);
            HelpMarker2(DyeMatchingHelpText);
        }
    }

    /// <summary>Switches the mode and shows the result at once: ON = the shown color only on the matching channels.</summary>
    private void SetDyeMatching(DyePreviewSession session, bool on)
    {
        if (on)
        {
            EnsureDyeMatchGroups(session);
            if (!CanUseDyeMatching(session)) return;
        }

        session.MatchingOn = on;
        if (on) FitDyePreviewBannerOnScreen();

        // Nothing sent yet: the next hover already uses the new mode
        if (session.AnythingApplied) ReapplyDyePreview(session);
    }

    /// <summary>
    /// The dyes are always known: Glamourer's in Glamourer Preview mode, otherwise what the Fitting Room shows (Fitting
    /// Room mode, or the fallback without Glamourer, see Plugin.GetFittingRoomSlotStates). False only if building the
    /// color groups failed.
    /// </summary>
    private static bool CanUseDyeMatching(DyePreviewSession session) => session.MatchingPossible ?? true;

    /// <summary>The match groups of this popup, built the first time they are needed.</summary>
    private DyeMatchGroups EnsureDyeMatchGroups(DyePreviewSession session)
    {
        if (session.MatchGroups != null) return session.MatchGroups;

        DyeMatchGroups groups;
        try
        {
            groups = BuildDyeMatchGroups(session);
        }
        catch (Exception ex)
        {
            // Runs while the popup is drawn: an escaping exception would leave ImGui's stacks unbalanced
            Plugin.Log.Error(ex, "[GlamourTagger] Dye Matching Colors Only: building the color groups failed.");
            groups = new DyeMatchGroups(session.Channel, session.SetDyeAtOpen, session.OtherSetDyeAtOpen);
            session.MatchingPossible = false;
        }

        // A Ctrl+click before the groups were built has already split a same-color group
        if (session.MatchSplit) groups.SplitOtherChannel();

        session.MatchGroups = groups;
        return groups;
    }

    /// <summary>The first Ctrl+click splits a same-color group; groups rebuilt later (new dye targets) start split too.</summary>
    private static void SplitDyeMatchGroups(DyePreviewSession session)
    {
        session.MatchSplit = true;
        session.MatchGroups?.SplitOtherChannel();
    }

    private string GetDyeMatchingTooltip(DyePreviewSession session, bool usable)
    {
        if (!DyeMatchingAvailable) return DyeMatchingComingSoonTooltip;
        if (!usable) return DyeMatchingUnavailableTooltip;

        var groups = EnsureDyeMatchGroups(session);

        // Same dye twice, split by a Ctrl+click: both lines would have the same name, the set's dye tells them apart
        bool splitSameDye = groups.DyeA == groups.DyeB && !groups.SplitPending;
        int otherChannel = session.Channel == 1 ? 2 : 1;

        // Glamourer Preview without Glamourer: say where the dyes go
        string fallback = session.ApplyMode == 0 && !session.GlamourerLive ? DyeMatchingFallbackLine + "\n" : "";

        string tooltip = fallback + $"Dye Matching Colors Only: {(session.MatchingOn ? "ON" : "OFF")} - click to switch\n"
            + "Dye only these colors on the dye targets as a group:\n"
            + DyeMatchLine(groups.DyeA, groups.DyeB, groups.CountA, splitSameDye ? session.Channel : 0);

        // Same dye twice and not split yet: one group, one line
        if (!groups.SplitPending) tooltip += "\n" + DyeMatchLine(groups.DyeB, groups.DyeA, groups.CountB, splitSameDye ? otherChannel : 0);

        // Fitting Room: the accessories among the dye targets are left out, say why
        if (session.UsesFittingRoom && AccessoryIsDyeTarget()) tooltip += "\n" + FittingRoomSkipsAccessoriesNote;
        return tooltip;
    }

    /// <summary>
    /// One tooltip line, e.g. "Rhotano Blue group - 3 channels". A group is named after the color its channels had
    /// when the popup opened, so the name stays even when the set's dye changes. setChannel > 0 adds the set's dye
    /// it belongs to (same dye twice, after a split): "Snow White group (Dye 2) - 1 channel".
    /// </summary>
    private static string DyeMatchLine(byte dye, byte otherDye, int count, int setChannel = 0)
    {
        string name = dye switch
        {
            DyeHelper.DyeKeepCurrent => otherDye == DyeHelper.DyeKeepCurrent ? "Any dye" : "Any other dye",
            0 => "No dye",
            _ => DyeHelper.GetDye(Plugin.DataManager, dye)?.Name ?? $"Dye {dye}",
        };
        name += " group";
        if (setChannel > 0) name += $" (Dye {setChannel})";

        return count switch
        {
            0 => $"{name} - no channels match",
            1 => $"{name} - 1 channel matches",
            _ => $"{name} - {count} channels match",
        };
    }

    /// <summary>
    /// The "Dye Matching Colors Only" label right of the color rows while the mode is ON. The popup sizes itself
    /// to its content, so a dummy item at the strip's right edge is what widens it.
    /// </summary>
    private static void DrawDyeMatchingLabel(DyePreviewSession session, float contentBottom)
    {
        if (session.SwatchesRight <= 0f) return;

        var stripTopLeft = new Vector2(session.SwatchesRight + DyeMatchingLabelGap, session.HeaderBottomY);
        DrawVerticalLabel(DyeMatchingLabelLetters, stripTopLeft, contentBottom, DyeMatchingLabelLayout);

        ImGui.SetCursorScreenPos(stripTopLeft);
        ImGui.Dummy(new Vector2(DyeMatchingLabelStripWidth, 1f));
    }

    /// <summary>
    /// Switching the mode ON near the right screen edge moves the popup left once, so the label strip fits.
    /// It never moves back by itself, and the popup can be dragged anywhere afterwards.
    /// </summary>
    private static void FitDyePreviewBannerOnScreen()
    {
        var viewport = ImGui.GetMainViewport();
        Vector2 windowPos = ImGui.GetWindowPos();
        float screenRight = viewport.WorkPos.X + viewport.WorkSize.X;
        float overflow = windowPos.X + ImGui.GetWindowSize().X + DyeMatchingBannerWidth - screenRight;
        if (overflow > 0f) ImGui.SetWindowPos(new Vector2(MathF.Max(viewport.WorkPos.X, windowPos.X - overflow), windowPos.Y));
    }

    // ---------------------------------------------------------------------------------------------
    // Match groups: which dye channel follows which set dye
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// The match groups of one preview popup: only the dye targets, only the dye channels the item really has.
    /// The dyes come from the popup's slot snapshot: Glamourer's in Glamourer Preview, what the Fitting Room shows in
    /// Fitting Room mode and without Glamourer. The dyes a slot starts with count as already sent, so untouched slots get no call.
    /// </summary>
    private DyeMatchGroups BuildDyeMatchGroups(DyePreviewSession session)
    {
        var groups = new DyeMatchGroups(session.Channel, session.SetDyeAtOpen, session.OtherSetDyeAtOpen);
        var itemSheet = Plugin.DataManager.GetExcelSheet<Lumina.Excel.Sheets.Item>();

        // Dual-wield: the slot loop dyes the OffHand together with the MainHand
        session.Slots.TryGetValue("MainHand", out var mainHandState);
        var mainHandItem = mainHandState.ItemId != 0 ? itemSheet?.GetRowOrDefault(mainHandState.ItemId) : null;
        bool dualWield = mainHandItem.HasValue && Plugin.IsDualWieldWeapon(mainHandItem.Value);

        // With a dual-wield main hand among the targets, the off-hand is added with it, uncounted, whatever the order
        var targetKeys = EnumerateDyeTargetSlotKeys().ToList();
        bool offHandFollowsMainHand = dualWield && targetKeys.Contains("MainHand");
        foreach (string slotKey in targetKeys)
        {
            if (offHandFollowsMainHand && slotKey == "OffHand") continue;
            AddSlot(slotKey, counted: true);
            if (offHandFollowsMainHand && slotKey == "MainHand") AddSlot("OffHand", counted: false);
        }

        return groups;

        void AddSlot(string slotKey, bool counted)
        {
            session.Slots.TryGetValue(slotKey, out var state);
            byte dye1 = state.Stain;
            byte dye2 = state.Stain2;

            // An off-hand without an item of its own shows the dual-wield main hand, with its dyes
            bool borrowsMainHand = slotKey == "OffHand" && dualWield && state.ItemId == 0;
            if (borrowsMainHand) (dye1, dye2) = (mainHandState.Stain, mainHandState.Stain2);

            // No item at all: nothing can be dyed there (the apply skips it too), so it is not counted either
            if (state.ItemId == 0 && !borrowsMainHand) return;

            // The Fitting Room can't dye accessories: never painted there, so not counted either
            if (session.UsesFittingRoom && !Plugin.FittingRoomCanDye(slotKey)) return;

            // The off-hand only counts where the slot loop dyes it: a shield or a dual-wield part,
            // not the empty off-hand a two-handed weapon reports
            if (slotKey == "OffHand" && !dualWield && !IsDyeableOffHandItem(state.ItemId)) return;

            if (!groups.AddSlot(slotKey, dye1, dye2, counted)) return;

            byte glamourerSlot = GlamourerSlotOfKey(slotKey);
            if (glamourerSlot != 0) session.LastSentDyes.TryAdd(glamourerSlot, (dye1, dye2));
        }
    }

    /// <summary>
    /// Which dye channel of which dye target follows the edited dye (group A) or the set's other dye (group B).
    /// Fixed when built, from the dyes the slots had when the popup opened, so the two groups never merge,
    /// whatever colors they get later.
    /// </summary>
    private sealed class DyeMatchGroups
    {
        private enum Group : byte { None, A, B }

        private sealed class SlotChannels
        {
            public byte Original1, Original2;   // dyes when the popup opened
            public Group Group1, Group2;
            public bool Counted;                // false: follows another slot (dual-wield off-hand), not in the tooltip counts
        }

        private readonly Dictionary<string, SlotChannels> slots = new(StringComparer.OrdinalIgnoreCase);
        private readonly int editedChannel;

        public DyeMatchGroups(int editedChannel, byte dyeA, byte dyeB)
        {
            this.editedChannel = editedChannel;
            DyeA = dyeA;
            DyeB = dyeB;
            SplitPending = dyeA == dyeB;
        }

        public byte DyeA { get; }   // the edited channel's dye in the set when the popup opened
        public byte DyeB { get; }   // the set's other dye when the popup opened

        /// <summary>Both set dyes were the same: everything matching is group A until the first Ctrl+click splits it.</summary>
        public bool SplitPending { get; private set; }

        public int CountA { get; private set; }   // matching channels, for the toggle's tooltip
        public int CountB { get; private set; }

        /// <summary>
        /// Adds a dye target once. Both channels count, also on items with one dye channel or none: Glamourer keeps
        /// both dyes of every slot, and they show again on an item that has the channel (same as the normal preview).
        /// counted = false: the slot is dyed but left out of the counts (a dual-wield off-hand follows its main hand).
        /// </summary>
        public bool AddSlot(string slotKey, byte original1, byte original2, bool counted = true)
        {
            if (slots.ContainsKey(slotKey)) return false;

            slots[slotKey] = new SlotChannels
            {
                Original1 = original1,
                Original2 = original2,
                Group1 = Match(original1),
                Group2 = Match(original2),
                Counted = counted,
            };
            Recount();
            return true;
        }

        /// <summary>First Ctrl+click with the same dye twice in the set: the other channel's positions become group B.</summary>
        public void SplitOtherChannel()
        {
            if (!SplitPending) return;
            SplitPending = false;

            foreach (var channels in slots.Values)
            {
                if (editedChannel == 1 && channels.Group2 == Group.A) channels.Group2 = Group.B;
                if (editedChannel == 2 && channels.Group1 == Group.A) channels.Group1 = Group.B;
            }
            Recount();
        }

        /// <summary>
        /// The dyes one slot gets: group A the edited channel's dye, group B the other channel's dye, the rest
        /// keeps what it had. dye1 / dye2 are in channel order, as the slot loop passes them. False = not a dye target.
        /// </summary>
        public bool TryResolve(string slotKey, byte dye1, byte dye2, out (byte, byte) dyes)
        {
            if (!slots.TryGetValue(slotKey, out var channels))
            {
                dyes = default;
                return false;
            }

            byte dyeForA = editedChannel == 1 ? dye1 : dye2;
            byte dyeForB = editedChannel == 1 ? dye2 : dye1;
            dyes = (Pick(channels.Group1, channels.Original1, dyeForA, dyeForB),
                    Pick(channels.Group2, channels.Original2, dyeForA, dyeForB));
            return true;
        }

        // Keep current dye as a set dye matches every DYED channel the other set dye does not claim;
        // undyed channels only ever match a No Dye set dye
        private Group Match(byte dye)
        {
            bool dyed = dye != 0;
            if (DyeA == DyeB) return dye == DyeA || (DyeA == DyeHelper.DyeKeepCurrent && dyed) ? Group.A : Group.None;
            if (DyeA != DyeHelper.DyeKeepCurrent && dye == DyeA) return Group.A;
            if (DyeB != DyeHelper.DyeKeepCurrent && dye == DyeB) return Group.B;
            if (DyeA == DyeHelper.DyeKeepCurrent && dyed) return Group.A;
            if (DyeB == DyeHelper.DyeKeepCurrent && dyed) return Group.B;
            return Group.None;
        }

        // "Keep current dye" brings back the channel's own dye
        private static byte Pick(Group group, byte original, byte dyeForA, byte dyeForB)
        {
            byte dye = group switch { Group.A => dyeForA, Group.B => dyeForB, _ => original };
            return dye == DyeHelper.DyeKeepCurrent ? original : dye;
        }

        private void Recount()
        {
            CountA = 0;
            CountB = 0;
            foreach (var channels in slots.Values)
            {
                if (!channels.Counted) continue;
                Count(channels.Group1);
                Count(channels.Group2);
            }

            void Count(Group group)
            {
                if (group == Group.A) CountA++;
                else if (group == Group.B) CountB++;
            }
        }
    }
}