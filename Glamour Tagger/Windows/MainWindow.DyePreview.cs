using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using SlotStates = System.Collections.Generic.Dictionary<string, GlamourTagger.IPC.GlamourerIpcHandler.SlotState>;

namespace GlamourTagger.Windows;

//   MainWindow_EquipmentBar.cs  dye square click        -> BeginDyePickerSession
//   MainWindow_DyeHelper.cs     DrawDyePickerPopup      -> GetDyePreview, BeginDyePickerPopup, BeginDyePreviewFrame,
//                                                          UpdateDyePreviewSwatch, EndDyePreviewFrame
//                               DyePickerConsumesLeftClick -> OnDyePreviewPicked, OnDyePreviewOtherChannelSaved
//                               ResolveDyes             -> ResolveDyePreviewKeepCurrent
//                               ApplySlotDyeWithItemId  -> DyePreviewWithoutGlamourer, DyePreviewUsesLightOffHandPath, ApplyDyePreviewOffHand
//   MainWindow.cs               OnClose                 -> EndDyePreview
public partial class MainWindow
{
    // How long the cursor (or the mouse wheel) has to rest on a colour before it is applied, in ms. 0 = at once.
    private const int DyePreviewDwellMs = 80;

    // A revert only runs if the popup was still open this recently (ms). Protects against restoring an
    // outdated slot snapshot when the session went stale (window collapsed, dye set scrolled out of view, ...).
    private const int DyePreviewRevertWindowMs = 1000;

    // OffHand / shield in Glamourer Preview: the normal dye path re-applies the item through a chat
    // command and then patches its dyes. While previewing, the item is already on the character, so only
    // the dye patch is needed. Set to false to fall back to the normal (heavier) path.
    private static readonly bool DyePreviewLightOffHandPath = true;

    private static readonly Vector4? DyePreviewBackgroundColor = null;
    // new(0.135f, 0.135f, 0.102f, 0.95f); - gold
    // new(0.15f, 0.128f, 0.015f, 0.95f); - more gold
    // new(0.11f, 0.13f, 0.18f, 0.95f); - custom blue to match header
    // new(0.14f, 0.20f, 0.32f, 0.45f); //same blue as header
    // new Vector4(0.12f, 0.25f, 0.50f, 0.45f) - active header
    // new Vector4(0.12f, 0.25f, 0.40f, 0.45f) - active row
    // new Vector4(0.9f, 0.9f, 0.68f, 0.95f) - header text

    // Thin double outline along the popup's edge
    private static readonly Vector4 DyePreviewFrameColor =
    new Vector4(0.40f, 0.40f, 0.40f, 1f);
    //new Vector4(0.70f, 0.20f, 0.20f, 1f);
    //new Vector4(0.35f, 0.10f, 0.10f, 0.95f);
    //new Vector4(0.9f, 0.9f, 0.68f, 0.95f); // Equipment Bar gold

    private const float DyePreviewFrameThickness = 1f; // thickness of each of the two lines
    private const float DyePreviewFrameInset = -1f;     // distance of the outer line from the popup's edge
    private const float DyePreviewFrameGap = 2f;       // space between the two lines
    private const float DyePreviewPopupRounding = 10f; // corner radius of the preview popup and its outline; 0 = square


    // Vertical label, one letter per line, in a strip left of the colour rows.
    private const string DyePreviewLabelText = "INSTANT HOVER OVER DYE PREVIEW";
    private const float DyePreviewLabelStripWidth = 33f; // width of the strip (pushes the colour rows right); must be > 0
    private const float DyePreviewLabelNudgeX = -4f;     // + = right, - = left
    private const float DyePreviewLabelNudgeY = 0f;      // + = down, - = up (applied after vertical centring)
    private const float DyePreviewLabelLetterGap = -1f;   // extra pixels between letters (negative = tighter)
    private const float DyePreviewLabelWordGap = 12f;    // height of the gap between words
    private const float DyePreviewLabelFontScale = 1.2f; // 1 = normal text size
    private const float DyePreviewLabelAlpha = 1f;    // the label is drawn in the Equipment Bar gold at this alpha

    // Gap between the (!) help marker and the popup title.
    private const float DyePreviewHelpGap = 15f;

    // Swatch outlines. Offset = distance from the swatch edge in pixels; negative = inside the swatch.
    // They are drawn in the order below, so a later one lies on top of an earlier one on the same swatch.

    //orig outline
    //// Dim gold: the dye(s) the dye targets had when the popup opened (shown while nothing is kept)
    //private static readonly SwatchOutline DyePreviewOriginalOutline = new(new Vector4(1f, 0.85f, 0.1f, 0.35f), Thickness: 2f, Offset: 2f);
    // Blue: the Ctrl-clicked colour of the other dye channel. Inside the swatch, so it never hides the gold / red ones
    private static readonly SwatchOutline DyePreviewOtherChannelOutline = new(new Vector4(0.35f, 0.60f, 1f, 1f), Thickness: 2f, Offset: -3f, Rounding: 2f);
    // Gold: the right-clicked (kept) colour. Same gold as the picker's border around the dye stored in the set
    private static readonly SwatchOutline DyePreviewKeptOutline = new(new Vector4(1f, 0.85f, 0.1f, 1f), Thickness: 2f, Offset: -3f, Rounding: 2f);
    // Red: the colour that is shown right now (hovered or scrolled to). Thinner than the gold, so both stay visible
    private static readonly SwatchOutline DyePreviewShownOutline = new(new Vector4(0.70f, 0.20f, 0.20f, 1f), Thickness: 1.5f, Offset: 2f, Rounding: 5f);

    // "Dye Matching Colors" - planned add-on mode of this preview. Only its (disabled) toggle exists so far.
    private static readonly string DyeMatchingToggleGlyph = FontAwesomeIcon.Clone.ToIconString();
    private const float DyeMatchingToggleGlyphSize = 15f;
    private const float DyeMatchingToggleNudgeY = -2f;    // + = down, - = up
    private const string DyeMatchingComingSoonTooltip = "Dye Matching Colors - coming soon";

    private const string DyePreviewHelpText =
        "INSTANT HOVER OVER DYE PREVIEW\n" +
        "Hover over a color to instantly preview it on the slots targeted (set on the equipment bar).\n" +
        "Scroll the mouse wheel anywhere in this popup to step through the colors one by one.\n" +
        "(If you scroll, you still need to click on the dye to apply it permanently)\n\n" +
        "Left-click a color to apply color and save it into this Dye Set.\n" +
        "Right-click a color to apply it and keep it without saving it into the Dye Set or closing the popup. " +
        "Right-click it again to un-keep it.\n\n" +
        "The other dye channel:\n" +
        "   It always uses the other dye of the Dye Set.\n" +
        "   Ctrl+Left-click to apply and save the color into the OTHER dye channel of this Dye Set.\n" +
        "   Ctrl+Right-click to apply and keep the color on the OTHER dye channel without saving it into the Dye Set. " +
        "Ctrl+Right-click it again to un-keep it.\n\n" +
        "If you close this popup without left-clicking a color, the dyes revert to the kept color,\n" +
        "   otherwise to the previous dyes (in Fitting Room Try On mode nothing reverts then).\n" +
        "   (gold outline for current dye channel, blue outline for the other dye channel)";


    /// <summary>"Nothing chosen" in the int dye fields below. Not a dye: real dye ids are 0..255 (0 = None / no dye, 255 = Keep current dye).</summary>
    private const int DyeUnset = -1;

    private static readonly string[] DyePreviewLabelLetters = DyePreviewLabelText.Select(c => c.ToString()).ToArray();

    // Rounding = corner radius in pixels, 0 = square corners
    private readonly record struct SwatchOutline(Vector4 Color, float Thickness, float Offset, float Rounding = 0f);

    /// <summary>
    /// Everything that lives exactly as long as one Shift-opened dye picker popup. A new instance is
    /// created when the popup opens and dropped when it closes, so nothing can leak between popups.
    /// </summary>
    private sealed class DyePreviewSession
    {
        public DyePreviewSession(int setIdx, int channel, int applyMode, bool glamourerLive, SlotStates slots)
        {
            SetIdx = setIdx;
            Channel = channel;
            ApplyMode = applyMode;
            GlamourerLive = glamourerLive;
            Slots = slots;
        }

        // --- Fixed for the whole popup ---
        public readonly int SetIdx;           // dye set the popup belongs to
        public readonly int Channel;          // dye channel being edited: 1 or 2
        public readonly int ApplyMode;        // applyMode when the popup opened (0 = Glamourer Preview, 1 = Fitting Room)
        public readonly bool GlamourerLive;   // Glamourer Preview mode with Glamourer running: slot dyes are known, a revert is possible
        public readonly SlotStates Slots;     // item + dyes of every slot when the popup opened: every apply and the revert use this
        
        //orig outline
        //public readonly List<byte> OriginalDyes = new(12); // dyes the dye targets had on the edited channel (GlamourerLive only)
        public SlotStates? GlamourerDyes;     // Fitting Room only: Glamourer's dyes for "Keep current dye", read once on first use

        // --- What is on the character ---
        public int ShownDye = DyeUnset;          // edited channel: the dye last sent
        public int KeptDye = DyeUnset;           // edited channel: right-clicked dye the popup falls back to on close
        public int OtherChannelDye = DyeUnset;   // other channel: Ctrl-clicked dye
        public bool OtherChannelSaved;        // the other dye of the set was changed with Ctrl + Left-click
        public bool AnythingApplied;          // something was sent to the character during this popup
        public bool Committed;                // closed by a Left-click: whatever is shown stays
        public bool KeepCurrentWarningShown;  // "could not read current stains" is printed at most once per popup
        public bool ApplyErrorLogged;         // an exception during an apply is logged once per popup, not on every hover

        // --- Hover and mouse wheel ---
        public int FocusDye = DyeUnset;          // the red-outlined swatch: shown, or about to be shown by the mouse wheel
        public int PendingDye = DyeUnset;        // colour waiting for the dwell time
        public long PendingSince;             // Environment.TickCount64 when PendingDye started waiting
        public bool WheelStepPending;         // PendingDye was chosen with the mouse wheel
        public bool HoverSuspended;           // hovering is ignored until the cursor reaches a different swatch than HoverSuspendedOn
        public int HoverSuspendedOn = DyeUnset;  // swatch the cursor was resting on when hovering got suspended (DyeUnset = none)

        // --- Rebuilt every frame while the swatches are drawn ---
        public readonly List<byte> SwatchOrder = new(160);                  // dye ids in display order (mouse wheel)
        public readonly Dictionary<byte, Vector2> SwatchPositions = new(160); // top-left corner of every swatch (outlines)
        public Vector2 SwatchSize;
        public int HoveredDye = DyeUnset;        // swatch under the cursor this frame
        public Vector2 ContentTopLeft;        // top-left of the popup content, before the label strip indent
        public long LastDrawnTick;            // Environment.TickCount64 of the last frame the popup was open, 0 = not drawn yet
    }

    /// <summary>The preview of the currently open dye picker popup; null while no preview popup is open.</summary>
    private DyePreviewSession? dyePreview;

    /// <summary>Set only while a preview sends dyes through the shared apply path (see ApplyDyePreview).</summary>
    private DyePreviewSession? dyePreviewApplying;



    /// <summary>
    /// Called right before a dye picker popup is opened from a dye square.
    /// withPreview = the square was Shift+Clicked: the popup opens as an Instant Hover Over Dye Preview.
    /// </summary>
    private void BeginDyePickerSession(int setIdx, int dyeChannel, bool withPreview)
    {
        EndDyePreview(); // normally nothing to do: the previous popup ended its own session when it closed
        if (!withPreview) return;

        // The one slot read of this popup. In Glamourer Preview mode it carries the slots' current dyes.
        var slots = FetchActiveSlotStates();
        bool glamourerLive = applyMode == 0 && plugin.GlamourerIpc.IsAvailable();

        var session = new DyePreviewSession(setIdx, dyeChannel, applyMode, glamourerLive, slots);

        //orig outline
        //if (glamourerLive)
        //{
        //    foreach (string slotKey in EnumerateDyeTargetSlotKeys())
        //    {
        //        if (!slots.TryGetValue(slotKey, out var state) || state.ItemId == 0) continue;

        //        byte dye = dyeChannel == 1 ? state.Stain : state.Stain2;
        //        if (!session.OriginalDyes.Contains(dye)) session.OriginalDyes.Add(dye);
        //    }
        //}

        dyePreview = session;
    }

    /// <summary>The preview session if the given popup is the one that was opened with Shift+Click, otherwise null.</summary>
    private DyePreviewSession? GetDyePreview(int setIdx, int dyeChannel)
    {
        return dyePreview != null && dyePreview.SetIdx == setIdx && dyePreview.Channel == dyeChannel ? dyePreview : null;
    }

    /// <summary>
    /// Ends the preview: decides what stays on the character (see the file header) and drops the session.
    /// Safe to call at any time, also when no preview is running.
    /// </summary>
    private void EndDyePreview()
    {
        var session = dyePreview;
        if (session == null) return;
        dyePreview = null;

        bool popupJustClosed = Environment.TickCount64 - session.LastDrawnTick <= DyePreviewRevertWindowMs;
        if (session.Committed || !session.AnythingApplied || !popupJustClosed || applyMode != session.ApplyMode)
        {
            return;
        }

        if (session.KeptDye != DyeUnset)
        {
            if (session.ShownDye != session.KeptDye)
            {
                ShowDyePreviewColor(session, (byte)session.KeptDye);
            }
        }
        else if (session.GlamourerLive)
        {
            // "Keep current dye" resolves against the slot snapshot, i.e. to each slot's own original dye.
            // The edited channel always goes back. The other channel stays on a colour chosen with Ctrl
            // (kept or saved); otherwise it goes back too.
            bool otherChannelChosen = session.OtherChannelDye != DyeUnset || session.OtherChannelSaved;
            byte otherDye = otherChannelChosen ? GetDyePreviewOtherChannelDye(session) : DyeHelper.DyeKeepCurrent; 
            ApplyDyePreview(session, DyeHelper.DyeKeepCurrent, otherDye);
        }
    }

    /// <summary>The dye target slots as slot snapshot keys ("Finger" becomes the targeted ring slots).</summary>
    private IEnumerable<string> EnumerateDyeTargetSlotKeys()
    {
        foreach (string slotName in EffectiveDyeSlots)
        {
            if (slotName != "Finger")
            {
                yield return slotName;
                continue;
            }

            if (DyeTargetsRightRing) yield return "RFinger";
            if (DyeTargetsLeftRing) yield return "LFinger";
        }
    }

    // ---------------------------------------------------------------------------------------------
    // Applying
    // ---------------------------------------------------------------------------------------------

    /// <summary>Shows a colour on the edited channel of every dye target and makes it the outlined one.</summary>
    private void ShowDyePreviewColor(DyePreviewSession session, byte dyeId)
    {
        ApplyDyePreview(session, dyeId, GetDyePreviewOtherChannelDye(session));
        session.ShownDye = dyeId;
        session.FocusDye = dyeId;
    }

    /// <summary>
    /// Sends one dye for the edited channel and one for the other channel to every dye target, through
    /// the same slot loop the brush button uses - but on the slot snapshot of this popup, so no slot is read again.
    /// </summary>
    private void ApplyDyePreview(DyePreviewSession session, byte editedChannelDye, byte otherChannelDye)
    {
        byte dye1 = session.Channel == 1 ? editedChannelDye : otherChannelDye;
        byte dye2 = session.Channel == 1 ? otherChannelDye : editedChannelDye;

        session.AnythingApplied = true;
        dyePreviewApplying = session;
        try
        {
            ApplyDyesToDyeTargets(dye1, dye2, session.Slots, ref session.KeepCurrentWarningShown);
        }
        catch (Exception ex)
        {
            // This runs in the middle of drawing the popup: an exception escaping here would leave ImGui's
            // popup / indent / ID stacks unbalanced and take the game down. Log it once and carry on.
            if (!session.ApplyErrorLogged)
            {
                Plugin.Log.Error(ex, "[GlamourTagger] Dye preview apply failed.");
                session.ApplyErrorLogged = true;
            }
        }
        finally
        {
            dyePreviewApplying = null;
        }
    }

    /// <summary>
    /// What the other dye channel gets: the Ctrl+Right-clicked colour if there is one, otherwise the
    /// other dye of the set this popup belongs to (which may be "Keep current dye").
    /// </summary>
    private byte GetDyePreviewOtherChannelDye(DyePreviewSession session)
    {
        if (session.OtherChannelDye != DyeUnset) return (byte)session.OtherChannelDye;

        var editedSet = plugin.Configuration.DyeSets[session.SetIdx];
        return session.Channel == 1 ? editedSet.Dye2 : editedSet.Dye1;
    }

    /// <summary>
    /// Sets (or with DyeUnset: clears) the Ctrl-clicked colour of the other channel and shows the result.
    /// The edited channel keeps the colour it is showing; if nothing is shown yet it stays as it is.
    /// </summary>
    private void SetDyePreviewOtherChannel(DyePreviewSession session, int dyeId)
    {
        session.OtherChannelDye = dyeId;

        byte editedChannelDye = session.ShownDye != DyeUnset ? (byte)session.ShownDye : DyeHelper.DyeKeepCurrent;
        ApplyDyePreview(session, editedChannelDye, GetDyePreviewOtherChannelDye(session));
    }

    /// <summary>
    /// Fitting Room mode, called by ResolveDyes while a preview applies: resolves "Keep current dye" from
    /// ONE Glamourer read per popup, instead of the read per slot Plugin.TryOnItem would do on every hover.
    /// Same rule as Plugin.ResolveKeepCurrentForFittingRoom: Glamourer's dye, or no dye if Glamourer is not running.
    /// </summary>
    private (byte, byte) ResolveDyePreviewKeepCurrent(DyePreviewSession session, string jsonSlot, byte dye1, byte dye2)
    {
        if (dye1 != DyeHelper.DyeKeepCurrent && dye2 != DyeHelper.DyeKeepCurrent) return (dye1, dye2);

        session.GlamourerDyes ??= plugin.GlamourerIpc.GetAllSlotStates(); // empty when Glamourer is not running
        session.GlamourerDyes.TryGetValue(jsonSlot, out var glamourerState); // slot not found -> (0, 0) = no dye

        return (dye1 == DyeHelper.DyeKeepCurrent ? glamourerState.Stain : dye1,
                dye2 == DyeHelper.DyeKeepCurrent ? glamourerState.Stain2 : dye2);
    }

    /// <summary>True while a Glamourer Preview hover preview applies and the light OffHand path is enabled.</summary>
    private bool DyePreviewUsesLightOffHandPath => DyePreviewLightOffHandPath && dyePreviewApplying is { GlamourerLive: true };

    /// <summary>
    /// True while a preview applies in Glamourer Preview mode although Glamourer was not running when the
    /// popup opened. Those applies go straight to the Fitting Room (see ApplySlotDyeWithItemId).
    /// </summary>
    private bool DyePreviewWithoutGlamourer => dyePreviewApplying is { ApplyMode: 0, GlamourerLive: false };

    /// <summary>
    /// OffHand / shield while previewing: only patches the dyes of the item that is already on the
    /// character, without the "/glamour applyitem" chat command of the normal OffHand path.
    /// </summary>
    private void ApplyDyePreviewOffHand(uint itemId, byte dye1, byte dye2)
    {
        Plugin.Framework.RunOnFrameworkThread(() =>
        {
            plugin.GlamourerIpc.TryApplyShieldStain(itemId, dye1, dye2);
        });
    }

    // ---------------------------------------------------------------------------------------------
    // Input
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// Called for every swatch right after it was drawn (the swatch must still be ImGui's last item):
    /// remembers where it is and handles Right-click and hover on it.
    /// </summary>
    private void UpdateDyePreviewSwatch(DyePreviewSession session, byte dyeId, Vector2 position, Vector2 size)
    {
        session.SwatchOrder.Add(dyeId);
        session.SwatchPositions[dyeId] = position;
        session.SwatchSize = size;

        if (ImGui.IsItemClicked(ImGuiMouseButton.Right)) OnDyePreviewRightClick(session, dyeId);
        if (ImGui.IsItemHovered()) OnDyePreviewHover(session, dyeId);
    }

    /// <summary>Plain Left-click picked a colour: it stays on the character when the popup closes.</summary>
    private void OnDyePreviewPicked(DyePreviewSession session, byte dyeId)
    {
        // A fast click can beat the dwell time: make sure the character shows what was picked
        if (session.ShownDye != dyeId) ShowDyePreviewColor(session, dyeId);
        session.Committed = true;
    }

    /// <summary>
    /// Ctrl + Left-click saved a colour into the set's other dye (see DyePickerConsumesLeftClick): the other
    /// channel follows the set's other dye, which is now this colour. A Ctrl+Right-click keep is dropped.
    /// </summary>
    private void OnDyePreviewOtherChannelSaved(DyePreviewSession session)
    {
        session.OtherChannelSaved = true;
        SetDyePreviewOtherChannel(session, DyeUnset);
    }

    private void OnDyePreviewRightClick(DyePreviewSession session, byte dyeId)
    {
        // Ctrl + Right-click: the colour of the OTHER channel. On that colour again: clear it.
        if (ImGui.GetIO().KeyCtrl)
        {
            SetDyePreviewOtherChannel(session, session.OtherChannelDye == dyeId ? DyeUnset : dyeId);
            return;
        }

        // Right-click on the kept colour again: un-keep it. Nothing changes on the character.
        if (session.KeptDye == dyeId)
        {
            session.KeptDye = DyeUnset;
            return;
        }

        // A click is a deliberate choice: it ends a mouse wheel step that is still waiting
        session.HoverSuspended = false;
        session.WheelStepPending = false;

        if (session.ShownDye != dyeId) ShowDyePreviewColor(session, dyeId);
        session.KeptDye = dyeId;
        session.FocusDye = dyeId;
    }

    private void OnDyePreviewHover(DyePreviewSession session, byte dyeId)
    {
        session.HoveredDye = dyeId;

        // Ctrl is for the other dye channel: while it is held hovering previews nothing. The swatch under
        // the cursor also stays ignored after Ctrl is released - otherwise the colour that was just
        // Ctrl-clicked would jump onto the edited channel the moment the key is let go.
        if (ImGui.GetIO().KeyCtrl)
        {
            session.HoverSuspended = true;
            session.HoverSuspendedOn = dyeId;
            return;
        }

        if (session.HoverSuspended)
        {
            if (dyeId == session.HoverSuspendedOn) return;

            // The cursor reached another swatch: hovering is in charge again
            session.HoverSuspended = false;
            session.WheelStepPending = false;
            session.FocusDye = session.ShownDye; // outline back on the colour that is really shown
        }

        // Already on the character: nothing to do. This is all a resting cursor costs per frame.
        if (dyeId == session.ShownDye)
        {
            session.PendingDye = dyeId;
            return;
        }

        long now = Environment.TickCount64;
        if (session.PendingDye != dyeId)
        {
            session.PendingDye = dyeId;
            session.PendingSince = now;
        }

        if (now - session.PendingSince >= DyePreviewDwellMs)
        {
            ShowDyePreviewColor(session, dyeId);
        }
    }

    /// <summary>
    /// Mouse wheel anywhere over the popup: down = next colour, up = previous colour, in display order.
    /// The outline moves at once; the colour is applied after the dwell time, so spinning the wheel does
    /// not apply every colour on the way. savedDye = the dye stored in the set (start point if nothing is outlined).
    /// </summary>
    private void UpdateDyePreviewMouseWheel(DyePreviewSession session, byte savedDye)
    {
        long now = Environment.TickCount64;
        float wheel = ImGui.GetIO().MouseWheel;

        if (wheel != 0f && session.SwatchOrder.Count > 0 && ImGui.IsWindowHovered())
        {
            int startDye = session.FocusDye != DyeUnset ? session.FocusDye
                         : session.HoveredDye != DyeUnset ? session.HoveredDye
                         : savedDye;

            int index = session.SwatchOrder.IndexOf((byte)startDye);
            index = index < 0 ? 0 : Math.Clamp(index + (wheel < 0f ? 1 : -1), 0, session.SwatchOrder.Count - 1);

            byte targetDye = session.SwatchOrder[index];
            if (targetDye != session.FocusDye)
            {
                session.FocusDye = targetDye;
                session.PendingDye = targetDye;
                session.PendingSince = now;
                session.WheelStepPending = true;
            }

            // The cursor is most likely still resting on a swatch: it must not take over again
            // until it reaches a different one.
            session.HoverSuspended = true;
            session.HoverSuspendedOn = session.HoveredDye;
        }

        if (session.WheelStepPending && now - session.PendingSince >= DyePreviewDwellMs)
        {
            session.WheelStepPending = false;
            if (session.PendingDye != session.ShownDye) ShowDyePreviewColor(session, (byte)session.PendingDye);
        }
    }

    /// <summary>
    /// Opens the dye picker popup. With a preview it gets the dark gold background, and a popup that
    /// is no longer open ends the preview (click outside, Esc, or a colour was picked).
    /// </summary>
    private bool BeginDyePickerPopup(string popupId, DyePreviewSession? preview)
    {
        if (preview == null) return ImGui.BeginPopup(popupId);

        if (DyePreviewBackgroundColor is { } background) ImGui.PushStyleColor(ImGuiCol.PopupBg, background);
        ImGui.PushStyleVar(ImGuiStyleVar.PopupRounding, DyePreviewPopupRounding);

        bool open = ImGui.BeginPopup(popupId);

        ImGui.PopStyleVar();

        // Popped right away on purpose: the background is already drawn, and the tooltips inside the
        // popup (they use the same style colour) must keep their normal background.
        if (DyePreviewBackgroundColor.HasValue) ImGui.PopStyleColor();

        if (!open && preview.LastDrawnTick != 0)
        {
            EndDyePreview();
        }

        return open;
    }

    /// <summary>
    /// Start of a preview popup's content: reserves the label strip and draws the header row
    /// ((!) help marker, title, Dye Matching Colors toggle) and the separator below it.
    /// </summary>
    //splotch
    //private void BeginDyePreviewFrame(DyePreviewSession session, string title)
    private void BeginDyePreviewFrame(DyePreviewSession session)
    {
        session.LastDrawnTick = Environment.TickCount64;
        session.SwatchOrder.Clear();
        session.SwatchPositions.Clear();
        session.HoveredDye = DyeUnset;

        // Everything in the popup is drawn indented by the width of the label strip
        session.ContentTopLeft = ImGui.GetCursorScreenPos();
        ImGui.Indent(DyePreviewLabelStripWidth);

        HelpMarker2(DyePreviewHelpText);
        ImGui.SameLine(0, DyePreviewHelpGap);
        //splotch
        //ImGui.TextDisabled(title);
        DrawDyePickerTitle(session.SetIdx, session.Channel);
        DrawDyeMatchingToggle();

        // ImGui.Separator() ignores the indent and would run through the label strip, so the same
        // line is drawn by hand, starting at the indent.
        Vector2 separatorStart = ImGui.GetCursorScreenPos();
        float separatorEndX = separatorStart.X + ImGui.GetContentRegionAvail().X + ImGui.GetStyle().WindowPadding.X;
        ImGui.GetWindowDrawList().AddLine(separatorStart, new Vector2(separatorEndX, separatorStart.Y), ImGui.GetColorU32(ImGuiCol.Separator), 1f);
        ImGui.Dummy(new Vector2(0f, 1f));
    }

    /// <summary>
    /// End of a preview popup's content: draws the vertical label and the swatch outlines (last, so no
    /// swatch can cover them) and handles the mouse wheel. savedDye = the dye currently stored in the set.
    /// </summary>
    private void EndDyePreviewFrame(DyePreviewSession session, byte savedDye)
    {
        ImGui.Unindent(DyePreviewLabelStripWidth);

        float contentBottom = ImGui.GetCursorScreenPos().Y - ImGui.GetStyle().ItemSpacing.Y;
        DrawVerticalLabel(DyePreviewLabelLetters, session.ContentTopLeft, contentBottom, DyePreviewLabelStripWidth, new Vector2(DyePreviewLabelNudgeX, DyePreviewLabelNudgeY));

        // Bottom to top. The dim gold only shows while the original dyes are what the popup falls back to.
        
        //orig outline
        //if (session.KeptDye == DyeUnset)
        //{
        //    foreach (byte originalDye in session.OriginalDyes)
        //    {
        //        DrawSwatchOutline(session, originalDye, DyePreviewOriginalOutline);
        //    }
        //}

        DrawSwatchOutline(session, session.OtherChannelDye, DyePreviewOtherChannelOutline);
        DrawSwatchOutline(session, session.KeptDye, DyePreviewKeptOutline);
        DrawSwatchOutline(session, session.FocusDye, DyePreviewShownOutline);
        
        DrawDyePreviewPopupFrame();

        UpdateDyePreviewMouseWheel(session, savedDye);
    }

    /// <summary>
    /// Thin double outline along the edge of the preview popup. The clip rect is widened to the whole
    /// popup first: the window's own clip rect stops short of its edge and would cut the lines off.
    /// </summary>
    private static void DrawDyePreviewPopupFrame()
    {
        var drawList = ImGui.GetWindowDrawList();
        Vector2 min = ImGui.GetWindowPos();
        Vector2 max = min + ImGui.GetWindowSize();
        float rounding = DyePreviewPopupRounding; 
        uint color = ImGui.GetColorU32(DyePreviewFrameColor);

        ImGui.PushClipRect(min, max, false);
        for (int line = 0; line < 2; line++)
        {
            // + half the thickness: AddRect centres the line on the rectangle, this keeps it inside the popup
            float inset = DyePreviewFrameInset + line * (DyePreviewFrameThickness + DyePreviewFrameGap) + DyePreviewFrameThickness * 0.5f;
            var offset = new Vector2(inset, inset);
            drawList.AddRect(min + offset, max - offset, color, MathF.Max(0f, rounding - inset), ImDrawFlags.None, DyePreviewFrameThickness);
        }
        ImGui.PopClipRect();
    }

    private static void DrawSwatchOutline(DyePreviewSession session, int dyeId, SwatchOutline outline)
    {
        if (dyeId == DyeUnset || !session.SwatchPositions.TryGetValue((byte)dyeId, out Vector2 position)) return;

        var offset = new Vector2(outline.Offset, outline.Offset);
        ImGui.GetWindowDrawList().AddRect(
            position - offset,
            position + session.SwatchSize + offset,
            ImGui.GetColorU32(outline.Color),
            outline.Rounding,
            ImDrawFlags.None,
            outline.Thickness);
    }

    /// <summary>
    /// Draws a text one letter per line, top to bottom, inside a strip of the given width that starts at
    /// stripTopLeft, centred vertically between stripTopLeft.Y and bottomY. Draw-list text only: it is
    /// not an ImGui item, so it cannot change the popup's layout.
    /// </summary>
    private static void DrawVerticalLabel(string[] letters, Vector2 stripTopLeft, float bottomY, float stripWidth, Vector2 nudge)
    {
        var drawList = ImGui.GetWindowDrawList();
        var font = ImGui.GetFont();
        float fontSize = ImGui.GetFontSize() * DyePreviewLabelFontScale;
        float letterStep = fontSize + DyePreviewLabelLetterGap;

        // Height of the whole label, so it can be centred as one block
        float totalHeight = 0f;
        int letterCount = 0;
        foreach (string letter in letters)
        {
            if (letter == " ")
            {
                totalHeight += DyePreviewLabelWordGap;
            }
            else
            {
                totalHeight += letterStep;
                letterCount++;
            }
        }
        if (letterCount > 0) totalHeight -= DyePreviewLabelLetterGap; // no gap after the last letter

        float y = stripTopLeft.Y + ((bottomY - stripTopLeft.Y) - totalHeight) * 0.5f + nudge.Y;
        uint color = ImGui.GetColorU32(new Vector4(EquipBarGold.X, EquipBarGold.Y, EquipBarGold.Z, DyePreviewLabelAlpha));

        foreach (string letter in letters)
        {
            if (letter == " ")
            {
                y += DyePreviewLabelWordGap;
                continue;
            }

            // CalcTextSize measures at the normal font size, so it is scaled to the label's size
            float letterWidth = ImGui.CalcTextSize(letter).X * DyePreviewLabelFontScale;
            float x = stripTopLeft.X + (stripWidth - letterWidth) * 0.5f + nudge.X;
            drawList.AddText(font, fontSize, new Vector2(x, y), color, letter);
            y += letterStep;
        }
    }

    /// <summary>
    /// Right end of the header row: the toggle of the planned "Dye Matching Colors" mode.
    /// The mode does not exist yet - the icon is drawn disabled and only explains itself in a tooltip.
    /// </summary>
    private static void DrawDyeMatchingToggle()
    {
        Vector2 glyphSize = MeasureIconGlyph(DyeMatchingToggleGlyph, DyeMatchingToggleGlyphSize);
        float width = MathF.Max(1f, glyphSize.X);
        float lineHeight = ImGui.GetTextLineHeight();

        // Right-aligned on the title's row
        ImGui.SameLine();
        float freeSpace = ImGui.GetContentRegionAvail().X - width;
        if (freeSpace > 0f) ImGui.SetCursorPosX(ImGui.GetCursorPosX() + freeSpace);

        // Only a hover area for the tooltip - clicking does nothing
        Vector2 position = ImGui.GetCursorScreenPos();
        ImGui.InvisibleButton("##DyeMatchingToggle", new Vector2(width, lineHeight));
        bool hovered = ImGui.IsItemHovered();

        Vector2 glyphPosition = new(position.X, position.Y + (lineHeight - glyphSize.Y) * 0.5f + DyeMatchingToggleNudgeY);
        uint disabledColor = ImGui.GetColorU32(ImGui.GetStyle().Colors[(int)ImGuiCol.TextDisabled]);
        ImGui.GetWindowDrawList().AddText(UiBuilder.IconFont, DyeMatchingToggleGlyphSize, glyphPosition, disabledColor, DyeMatchingToggleGlyph);

        if (hovered) ImGui.SetTooltip(DyeMatchingComingSoonTooltip);
    }
}
