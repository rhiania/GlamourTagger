using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using System.Numerics;

namespace GlamourTagger.Windows;

// Visual & Notification Settings window
public class SettingsWindow : Window
{
    private readonly Plugin plugin;
    
    // (!) marker with a hover tooltip
    private static void HelpMarker3(string desc)
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

    public SettingsWindow(Plugin plugin) : base("Glamour Tagger Settings", ImGuiWindowFlags.NoCollapse)
    {
        this.plugin = plugin;

        // 1. Első megnyitáskori méret (X = szélesség, Y = magasság):
        // Ezt a méretet csak a legelső megnyitáskor alkalmazza, utána a felhasználó szabadon átméretezheti.
        this.Size = new Vector2(520, 350);
        this.SizeCondition = ImGuiCond.FirstUseEver;

        // --- TESZTELÉSI OPCIÓ ---
        // Teszteléshez vedd ki az alábbi sor kommentjét: ez kikényszeríti, hogy az ablak
        // MINDEN megnyitáskor szigorúan a fenti (this.Size) méretet vegye fel, figyelmen kívül hagyva a mentett méretet.
        //this.SizeCondition = ImGuiCond.Always;

        this.SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(300, 200),
            MaximumSize = new Vector2(800, 800)
        };
    }

    public void Dispose() { }

    public override void Draw()
    {
        ImGui.TextUnformatted("Icon & Tooltip Settings");
        ImGui.Separator();
        ImGui.Spacing();

        ImGui.Indent(30f);

        float tableSize = plugin.Configuration.TableIconSize;
        if (ImGui.SliderFloat("Table Icon Size", ref tableSize, 16.0f, 48.0f, "%.0f px"))
        {
            plugin.Configuration.TableIconSize = tableSize;
            plugin.Configuration.Save();
            plugin.MainWindow.isDirty = true;
        }
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("Adjusts the size of icons in the item table (and row height).");
        }

        float scale = plugin.Configuration.HoverIconScale;
        if (ImGui.SliderFloat("Hover Icon Scale", ref scale, 1.5f, 5.0f, "%.1fx"))
        {
            plugin.Configuration.HoverIconScale = scale;
            plugin.Configuration.Save();
        }
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("Adjusts how large the hover-over magnified icon appears.");
        }

        ImGui.Unindent(30f);

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        DrawPenumbraNotificationSettings();
    }

    public void DrawPenumbraNotificationSettings()
    {
        ImGui.TextUnformatted("Penumbra Catch Notification Settings");
        ImGui.Separator();
        ImGui.Spacing();

        ImGui.Indent(15f);

        // English explanation for casual players
        ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.80f, 0.80f, 0.85f, 1.0f));
        ImGui.TextWrapped("When Penumbra updates item caches, Glamour Tagger scans for changes and highlights newly modded items or items you previously marked as demodded.");
        ImGui.PopStyleColor();

        ImGui.Spacing();
        HelpMarker3("Note: This feature requires Penumbra to be installed and active.");
        ImGui.SameLine();
        ImGui.TextDisabled("Requires Penumbra IPC connection.");

        ImGui.Unindent(15f);

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        ImGui.Indent(30f);

        // 1. Session Mute Reset Controls
        if (plugin.MainWindow.sessionSuppressCatchWindow)
        {
            ImGui.AlignTextToFramePadding();
            ImGui.TextColored(new Vector4(0.9f, 0.6f, 0.2f, 1.0f), "Catch notifications are currently muted for this session.");
            ImGui.SameLine(0, 10f);
            if (ImGui.Button("Reset Session Mute"))
            {
                plugin.MainWindow.sessionSuppressCatchWindow = false;
            }
            ImGui.Spacing();
        }
        else
        {
            ImGui.AlignTextToFramePadding();
            ImGui.TextUnformatted("Session Mute:");
            ImGui.SameLine(0, 10f);
            if (ImGui.Button("Mute for Current Session"))
            {
                plugin.MainWindow.sessionSuppressCatchWindow = true;
            }
            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip("Mutes catch notification popups until the main window is closed, or until you reset the mute here.");
            }
            ImGui.Spacing();
        }

        ImGui.Unindent(30f);

        ImGui.Separator();
        ImGui.Spacing();

        ImGui.Indent(30f);

        // 2. Persistent Catch Toggles using Custom Toggle Switches
        bool neverShow = plugin.Configuration.NeverShowCatchWindow;
        bool ignoreUnusable = plugin.Configuration.IgnoreUnusableCatches;
        bool ignorePrev = plugin.Configuration.IgnorePrevDemoddedCatches;
        bool ignoreNewly = plugin.Configuration.IgnoreNewlyModdedCatches;

        // --- Master Switch: Disable All ---
        if (DrawToggleSwitch("##MasterCatchToggle", ref neverShow))
        {
            plugin.Configuration.NeverShowCatchWindow = neverShow;
            plugin.Configuration.IgnoreUnusableCatches = neverShow;
            plugin.Configuration.IgnorePrevDemoddedCatches = neverShow;
            plugin.Configuration.IgnoreNewlyModdedCatches = neverShow;
            plugin.Configuration.Save();
        }
        ImGui.SameLine(0, 10f);
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted("Never show catch notification window");

        ImGui.Indent(24f);

        // --- Ignore Previously Unusable ---
        if (DrawToggleSwitch("##IgnoreUnusableToggle", ref ignoreUnusable))
        {
            plugin.Configuration.IgnoreUnusableCatches = ignoreUnusable;
            UpdateMasterToggleState();
            plugin.Configuration.Save();
        }
        ImGui.SameLine(0, 10f);
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted("Ignore 'Previously Unusable' catches");

        // --- Ignore Previously Demodded ---
        if (DrawToggleSwitch("##IgnorePrevDemToggle", ref ignorePrev))
        {
            plugin.Configuration.IgnorePrevDemoddedCatches = ignorePrev;
            UpdateMasterToggleState();
            plugin.Configuration.Save();
        }
        ImGui.SameLine(0, 10f);
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted("Ignore 'Previously Demodded' catches");

        // --- Ignore Newly Added ---
        if (DrawToggleSwitch("##IgnoreNewlyModToggle", ref ignoreNewly))
        {
            plugin.Configuration.IgnoreNewlyModdedCatches = ignoreNewly;
            UpdateMasterToggleState();
            plugin.Configuration.Save();
        }
        ImGui.SameLine(0, 10f);
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted("Ignore 'Newly Added' catches");

        ImGui.Unindent(54f);
    }

    private void UpdateMasterToggleState()
    {
        // Synchronize Master Switch: If both sub-options are muted, Master becomes active
        bool allMuted = plugin.Configuration.IgnoreUnusableCatches
                 && plugin.Configuration.IgnorePrevDemoddedCatches
                 && plugin.Configuration.IgnoreNewlyModdedCatches;
        plugin.Configuration.NeverShowCatchWindow = allMuted;
    }

    // hand-drawn on/off switch (rounded track + knob) on top of an invisible button
    public static bool DrawToggleSwitch(string str_id, ref bool v)
    {
        Vector2 p = ImGui.GetCursorScreenPos();
        var drawList = ImGui.GetWindowDrawList();

        float height = ImGui.GetFrameHeight() * 0.85f;
        float width = height * 1.8f;
        float radius = height * 0.5f;

        bool clicked = ImGui.InvisibleButton(str_id, new Vector2(width, height));
        if (clicked) v = !v;

        uint colBg = v
            ? ImGui.GetColorU32(new Vector4(0.18f, 0.55f, 0.68f, 1.0f))   // Active (Teal Accent)
            : ImGui.GetColorU32(new Vector4(0.25f, 0.25f, 0.28f, 1.0f));  // Inactive (Dark Gray)

        float t = v ? 1.0f : 0.0f;
        if (ImGui.IsItemHovered())
        {
            colBg = v
                ? ImGui.GetColorU32(new Vector4(0.22f, 0.65f, 0.78f, 1.0f))
                : ImGui.GetColorU32(new Vector4(0.32f, 0.32f, 0.36f, 1.0f));
        }

        drawList.AddRectFilled(p, new Vector2(p.X + width, p.Y + height), colBg, radius);

        float knobX = p.X + radius + t * (width - radius * 2.0f);
        drawList.AddCircleFilled(new Vector2(knobX, p.Y + radius), radius - 2.0f, ImGui.GetColorU32(new Vector4(0.95f, 0.95f, 0.95f, 1.0f)));

        return clicked;
    }
}
