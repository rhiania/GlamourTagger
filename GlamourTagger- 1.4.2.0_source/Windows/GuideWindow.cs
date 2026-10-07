using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Windowing;
using Dalamud.Utility;
using System;
using System.Numerics;

namespace GlamourTagger.Windows;

// Static help text: quick guide, thanks, links. No state.
public class GuideWindow : Window, IDisposable
{
    public GuideWindow(Plugin plugin) : base("Glamour Tagger - Guide & Thanks")
    {
        Size = new Vector2(1150, 1000);
        SizeCondition = ImGuiCond.FirstUseEver;
    }

    public void Dispose() { }

    public override void Draw()
    {
        Vector4 piros = new Vector4(1f, 0.30f, 0.30f, 1f);

        ImGui.Dummy(new Vector2(0, 10));
        ImGui.Separator();
        ImGui.Spacing();
        ImGui.Spacing();
        // centred titel lines
        string text = "Welcome to Glamour Tagger";
        float availWidth = ImGui.GetContentRegionAvail().X;
        float textWidth = ImGui.CalcTextSize(text).X;
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + Math.Max(0f, (availWidth - textWidth) * 0.5f));
        ImGui.TextColored(piros, text);
        ImGui.Spacing();
        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();
        ImGui.Spacing();
        ImGui.TextWrapped("Designed to help you tag, flag, organize and filter all items effortlessly, bridging the gap between the massive item databases, your installed mods, and your glamours.");
        ImGui.Spacing();
        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();
        ImGui.Spacing();
        string text2 = "This mod would not have come to life without Glamourer and Penumbra.";
        float textWidth2 = ImGui.CalcTextSize(text2).X;
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + Math.Max(0f, (availWidth - textWidth2) * 0.5f));
        ImGui.TextColored(piros, text2);
        string text3 = "Huge thanks to their developers for the inspiration and integration capabilities.";
        float textWidth3 = ImGui.CalcTextSize(text3).X;
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + Math.Max(0f, (availWidth - textWidth3) * 0.5f));
        ImGui.TextColored(piros, text3);
        ImGui.Spacing();
        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();
        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();
        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();
        ImGui.Spacing();
        // numbered feature list - has to be kept in sync with the features by hand
        ImGui.TextUnformatted("Functions & Quick Guide");
        ImGui.Spacing();
        ImGui.Spacing();
        ImGui.Spacing();
        ImGui.BulletText("1. Tag, Flag and Organize items:");
        ImGui.TextWrapped($"Flag your Favorites (F), Double Favorites (DF), Triple Favorites (TF) and your Wish-listed (W) or Double Wish-listed (DW) items separately by clicking the ");
        ImGui.SameLine(0, 0);

        ImGui.PushFont(UiBuilder.IconFont);
        ImGui.SetWindowFontScale(0.7f);
        ImGui.TextUnformatted(FontAwesomeIcon.Star.ToIconString());
        ImGui.SetWindowFontScale(1f);
        ImGui.PopFont();
        ImGui.SameLine(0, 0);
        ImGui.TextWrapped(" or ");
        ImGui.SameLine(0, 0);

        ImGui.PushFont(UiBuilder.IconFont);
        ImGui.SetWindowFontScale(0.7f);
        ImGui.TextUnformatted(FontAwesomeIcon.ShoppingBasket.ToIconString());
        ImGui.SetWindowFontScale(1f);
        ImGui.PopFont();
        ImGui.TextWrapped(" symbols to help you before, during, and after glamouring sessions."); 
        ImGui.TextWrapped("Use column header pop-ups to quickly bulk-add or bulk-remove flags for all currently filtered/listed items.");
        ImGui.TextWrapped("You can also sync your favorites directly from Glamourer via the Options menu."); 
        ImGui.Spacing();
        ImGui.Spacing();
        ImGui.TextWrapped("Assign custom tags to build item groups and glamour sets.");
        ImGui.TextWrapped("Tag your items by using the bottom panel or you can directly add or delete existing tags by right-clicking (or Ctrl + Shift + right clicking) in the tag column.");
        ImGui.Spacing();
        ImGui.Spacing();
        ImGui.Spacing();

        ImGui.BulletText("2. Manage your Tags:");
        ImGui.TextWrapped("Open Options -> Tag Management to rename tags, customize badge colors, view item counts, or completely delete tags (holding Ctrl + Shift required).");
        ImGui.TextWrapped("or");
        ImGui.Spacing();
        ImGui.Spacing();
        ImGui.Spacing();

        ImGui.BulletText("3. Export or Import tags & flags:");
        ImGui.TextWrapped("In the same menu easily export or import your favourites, wishlists and tags to/from JSON files, for backup or sharing, with support for custom directory paths.");
        ImGui.TextWrapped("You can select entire folders there as well, it will export/import only the selected type of data.");
        ImGui.Spacing();
        ImGui.Spacing();
        ImGui.Spacing();

        ImGui.BulletText("4. Filter & Search:");
        ImGui.TextWrapped("Use the new equipment bar to quickly change between item slots. Left-clicks search for the specific item slot, right-clicks add the item slots into the filter.");
        ImGui.TextWrapped("Or use the headers and search bars to filter by --anything--, even by using different logic for tags:");
        ImGui.TextWrapped("Switch between *OR Mode* displaying items matching ANY selected tag and *AND Mode* displaying items matching ALL selected tags simultaneously. Clicking repeatedly on a tag sets it up for 'NOT' logic, hiding any items set like that from search results.");
        ImGui.Spacing();
        ImGui.TextWrapped("Use prefixes in the 'item search' box to quickly search tags or data. Use t: or tag: for tags, j: or job: for jobs, id: for item ID, m: or model: for model ID or p: for penumbra mod quick searches (without space, e.g 't:gothic' or 'j:WHM').");
        ImGui.TextWrapped("Click the job icon in the 'Item Name' header to instantly list the items of your current job (same as j:). Click it again to clear the search.");
        ImGui.Spacing();
        ImGui.TextWrapped("Middle-click the headers to clear the active filter in the column or use the 'clear all filter' button to clear all active filters.");
        ImGui.Spacing();
        ImGui.Spacing();
        ImGui.TextWrapped("At the end of table check 'Only Show One Item Per Model Type' with an optional 'Ignore Variants' toggle to clean up clutter by hiding duplicate models shared across different items.");
        ImGui.Spacing();
        ImGui.Spacing();
        ImGui.Spacing();

        ImGui.BulletText("5. Preview items & rings on both hands:");
        ImGui.TextWrapped("Toggle between 'Glamourer Preview' and 'Fitting Room Try On' to change how to preview the items.\nFormer applies items to your character using Glamourer, latter opens native game fitting room.");
        ImGui.TextWrapped("When previewing rings, use the RR (Right Ring) and LR (Left Ring) toggles on the Equipment Bar to select which finger slot receives the previewed ring. (Selecting both via right click previews the item on both hands.) " +
            "Built-in fallback measures ensure at least one ring slot remains active.");
        ImGui.Spacing();
        ImGui.Spacing();
        ImGui.TextWrapped("• Take items off: Middle-click a slot on the Equipment Bar to remove the previewed item from that slot. Ctrl + Middle-click equips the matching Emperor's New piece instead." +
                "The ring icon follows the RR/LR selection, middle-click RR or LR to affect only that ring. The filter and the dye targets do not change.");
        ImGui.TextWrapped("In Fitting Room mode the Emperor's New piece is always used.");
        ImGui.TextWrapped("Weapons and shields can only be hidden with The Emperor's New Fists (PUG / MNK) or The Emperor's New Shield (GLA / PLD / BSM).");
        ImGui.Spacing();
        ImGui.Spacing();
        ImGui.TextWrapped("• Dye Previews: Select a Dye Set on the Equipment Bar to preview items with dyes applied in both Glamourer and Fitting Room modes.");
        ImGui.TextWrapped("Right-click a dye square to toggle between 'Keep current dye' and 'No Dye'. In Glamourer Preview mode 'Keep current dye' leaves the channel unchanged. In Fitting Room Try On mode it uses the dye your character currently has on that slot in Glamourer, or no dye if Glamourer is not running.");
        ImGui.TextWrapped("Click the swap icon inside a dye set to swap primary/secondary dyes.");
        ImGui.Spacing();
        ImGui.TextWrapped("• Use the Brush icon ('Apply Color') to apply any dye set to one or more selected equipment slots without having to activate that dye set globally. The brush always uses the dyes of its own set, including 'No Dye', no matter which set is selected.");
        ImGui.TextWrapped("• Dye Targets: By default the brush dyes the slots selected in the slot filter. Hold Shift and click slots on the Equipment Bar to pick different slots to dye without changing the filter (Left-click: only this slot, Right-click: add/remove). Works with RR/LR and 'ALL' too.");
        ImGui.TextWrapped("• Switch on the Dye Target toggle (paint bucket icon above 'ALL') to do the same without holding Shift. Slots marked with a small droplet will be dyed. Middle-click the toggle to make the dye targets follow the filter again.");
        ImGui.Spacing();
        ImGui.Spacing();
        ImGui.TextWrapped("• Fitting Room Assistant: Native fitting room previews track state via the Fitting Room Memory Assistant window. If you accidentally close it, click the puzzle piece icon next to the 'Fitting Room Try On' radio button to reopen it.");
        ImGui.Spacing();
        ImGui.TextWrapped("• Revert to Game Character: Click the 'Revert Character to game state' button (located next to 'Clear all filter') to instantly reset your character's appearance back to their actual ingame look.");

        ImGui.Spacing();
        ImGui.Spacing();

        ImGui.TextWrapped("All in all, right-click any item to instantly preview it with dyes.");
        ImGui.TextWrapped("or");
        ImGui.Spacing();
        ImGui.Spacing();
        ImGui.Spacing();

        ImGui.BulletText("6. Cycle through items Fast:");
        ImGui.TextWrapped("Hover over the 'Scroll Here' button at the bottom, then Scroll Mouse Wheel Up/Down to rapidly cycle through and preview filtered items on your character or in the fitting room.");
        ImGui.TextWrapped("Use the 'Jump to Item' button beside the scroll box to quickly navigate to the selected/previewed item in the list.");
        ImGui.Spacing();
        ImGui.Spacing();
        ImGui.Spacing();

        Vector4 dia = new Vector4(0.20f, 0.85f, 0.95f, 1.0f);
        Vector4 diadark = new Vector4(0.12f, 0.45f, 0.55f, 0.50f);
        Vector4 diagrey = new Vector4(0.25f, 0.25f, 0.25f, 0.60f);
        Vector4 diared = new Vector4(0.75f, 0.15f, 0.15f, 0.65f);
        Vector4 skull = new Vector4(0.55f, 0.12f, 0.12f, 0.55f);
        Vector4 moon = new Vector4(0.45f, 0.25f, 0.35f, 0.75f);


        ImGui.BulletText("7. Penumbra integration & Mods affecting items:");
        ImGui.TextWrapped("When Penumbra is active, Glamour Tagger detects modded items and marks them in the 'Penumbra Mods' column (◆ symbol).");
        ImGui.TextWrapped($"Hover over the ");
            ImGui.SameLine(0, 0);

        ImGui.PushFont(UiBuilder.IconFont);
        ImGui.SetWindowFontScale(0.7f);
        ImGui.TextColored(dia, FontAwesomeIcon.Diamond.ToIconString());
        ImGui.SetWindowFontScale(1f);
        ImGui.PopFont();
        ImGui.SameLine(0, 0);
        ImGui.TextWrapped(" symbol or select an item to view its 'Affecting Mods' list in the tooltip or in the bottom panel.");
        ImGui.Spacing();
        ImGui.TextWrapped($"Manual Mod Toggle - ");
            ImGui.SameLine(0, 0);

        ImGui.PushFont(UiBuilder.IconFont);
        ImGui.SetWindowFontScale(0.7f);
        ImGui.TextColored(moon, FontAwesomeIcon.Moon.ToIconString());
        ImGui.SetWindowFontScale(1f);
        ImGui.PopFont();
        ImGui.SameLine(0, 0);
        ImGui.TextWrapped(" Demodded status:");
        ImGui.Indent(50f);
        ImGui.TextWrapped("Right-click a row or any mod badge at the bottom to set it as inactive/demodded. This lets you exclude items where mod parts are uninstalled or where a mod falsely claims an item is modified. Right-click 'demodded' mod tags to reset them to active (or unusable).");
        ImGui.Unindent(50f);
        ImGui.TextWrapped($"Manual Mod Toggle - ");
            ImGui.SameLine(0, 0);

        ImGui.PushFont(UiBuilder.IconFont);
        ImGui.SetWindowFontScale(0.7f);
        ImGui.TextColored(skull, FontAwesomeIcon.Skull.ToIconString());
        ImGui.SetWindowFontScale(1f);
        ImGui.PopFont();
        ImGui.SameLine(0, 0);
        ImGui.TextWrapped(" Unusable status:");
        ImGui.Indent(50f);
        ImGui.TextWrapped("Right-click a row or any mod badge at the bottom to set it as unusable. This lets you exclude items where the mod does not render properly or is unusable for you. Right-click 'unusable' mod tags to reset them to active (or demodded).");
        ImGui.Unindent(50f);
        ImGui.Indent();
        ImGui.TextWrapped("Items with at least one 'demodded' or 'unusable' tag will get a corresponding mod badge searchable by the 'p:' prefix in the item search box (e.g. p:demodded or p:unusable) to help you reevaluate them.");
        ImGui.Unindent();
        ImGui.TextWrapped("Item State Logic in the 'Penumbra Mods' column (helps filter out unchanged or broken items):\n");
        ImGui.Indent(50f);
        ImGui.TextWrapped("• Unmodded: Items with no affecting mods (represented by a grey ");
            ImGui.SameLine(0, 0);

        ImGui.PushFont(UiBuilder.IconFont);
        ImGui.SetWindowFontScale(0.7f);
        ImGui.TextColored(diagrey, FontAwesomeIcon.Diamond.ToIconString());
        ImGui.SetWindowFontScale(1f);
        ImGui.PopFont();
        ImGui.SameLine(0, 0);
        ImGui.TextWrapped(" symbol).");
        ImGui.TextWrapped("• Modded: Items with at least one active, working mod (represented by a bright blue ");
            ImGui.SameLine(0, 0);

        ImGui.PushFont(UiBuilder.IconFont);
        ImGui.SetWindowFontScale(0.7f);
        ImGui.TextColored(dia, FontAwesomeIcon.Diamond.ToIconString());
        ImGui.SetWindowFontScale(1f);
        ImGui.PopFont();
        ImGui.SameLine(0, 0);
        ImGui.TextWrapped(" symbol).");
        ImGui.TextWrapped("• Unusable: Items with no active working mods, but at least one unusable mod (represented by a dark red ");
            ImGui.SameLine(0, 0);

        ImGui.PushFont(UiBuilder.IconFont);
        ImGui.SetWindowFontScale(0.7f);
        ImGui.TextColored(diared, FontAwesomeIcon.Diamond.ToIconString());
        ImGui.SetWindowFontScale(1f);
        ImGui.PopFont();
        ImGui.SameLine(0, 0);
        ImGui.TextWrapped(" symbol).");
        ImGui.TextWrapped("• Demodded: Items where all affecting mods are set to 'demodded' (represented by a darker blue ");
            ImGui.SameLine(0, 0);

        ImGui.PushFont(UiBuilder.IconFont);
        ImGui.SetWindowFontScale(0.7f);
        ImGui.TextColored(diadark, FontAwesomeIcon.Diamond.ToIconString());
        ImGui.SetWindowFontScale(1f);
        ImGui.PopFont();
        ImGui.SameLine(0, 0);
        ImGui.TextWrapped(" symbol).");
        ImGui.Unindent(50f);

        ImGui.Spacing();
        ImGui.TextWrapped("Penumbra Redraw & Recatch: Use the button in the bottom right to trigger a manual cache recatch or force a Penumbra redraw on your character.");
        ImGui.Spacing();
        ImGui.TextWrapped("Penumbra Cache Collection: Glamour Tagger collects modded items and their active mods from Penumbra on startup and whenever you click 'Recatch & Redraw'.");
        ImGui.TextWrapped("Penumbra Notifications: Automatic popup alerts notify you when Penumbra refreshes caches and detects newly modded items or reintroduced mods previously marked as 'demodded' or 'unusable'. " +
            "You can configure notification frequency or mute them in the Settings."); 
        ImGui.Spacing();
        ImGui.Spacing();
        ImGui.Spacing();


        ImGui.BulletText("8. Look up the items directly on web:");
        ImGui.TextWrapped("Look up the items directly in Garland Tools, Gamer Escape or Teamcraft for sources and details with the three buttons at the bottom.");
        ImGui.Spacing();
        ImGui.Spacing();
        ImGui.Spacing();

        ImGui.BulletText("9. Link items in Chat:");
        ImGui.TextWrapped("Shift + left-click any item name in the table to link the item directly into your chat.");
        ImGui.Spacing();
        ImGui.Spacing();
        ImGui.Spacing();

        ImGui.BulletText("10. Adjust visuals & Notifications:");
        ImGui.TextWrapped("Customize table icon sizes and hover-over magnified preview scales in the Options -> Visual & Notification Settings menu, as well as Penumbra catch notification popups.");
        ImGui.Spacing();
        ImGui.Spacing();
        ImGui.Spacing();

        ImGui.BulletText("11. Automatic back-up system with restore functions:");
        ImGui.TextWrapped("The plugin has an automatic backup system to save your tags, flags and configs periodically and before importing to prevent data loss.");
        ImGui.TextWrapped("Open the Backup & Restore Manager from the Options menu to inspect backups or restore your data via Merge or Full Reset modes.");
        ImGui.Spacing();
        ImGui.Spacing();
        ImGui.Spacing();

        ImGui.BulletText("12. Standalone & Failsafe Operation:");
        ImGui.TextWrapped("Glamour Tagger includes full IPC failsafes. If Glamourer or Penumbra are disabled or not installed, the plugin operates smoothly in standalone mode without crashing or throwing errors.");
        ImGui.Spacing();
        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();
        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();
        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();
        ImGui.Spacing();

        // afterword + GitHub links
        ImGui.TextWrapped("Afterword & Maintenance");
        ImGui.Spacing();
        ImGui.Spacing();
        ImGui.Spacing();
        ImGui.TextWrapped("This plugin was built with AI assistance — initially just for my own personal use, but I decided to share it in case others find it helpful.");
        ImGui.Spacing();
        ImGui.Spacing();
        ImGui.TextWrapped("Please note that this is a hobby project. It will be maintained as long as I play and use it, but since this is not my day job, updates or bug fixes may take some time depending on my real-life work schedule.");
        ImGui.Spacing();
        ImGui.Spacing();
        ImGui.TextWrapped("I am far from a professional software engineer, so constructive feedback, ideas, and contributions are always welcome! If you encounter any issues or have suggestions, please feel free to reach out via the GitHub repository's Issues or Discussion tab.");
        ImGui.TextWrapped("Check out GitHub Discussions to see planned features, my backlog, and the collected community ideas as well.");
        ImGui.Spacing();
        ImGui.Spacing();


        if (ImGui.Button("GitHub Repository"))
        {
            Util.OpenLink("https://github.com/rhiania/GlamourTagger");
        }

        ImGui.SameLine();

        if (ImGui.Button("GitHub Discussions"))
        {
            Util.OpenLink("https://github.com/rhiania/GlamourTagger/discussions");
        }

        ImGui.Spacing(); 
        ImGui.Separator();
        ImGui.Spacing();
        ImGui.Spacing();
        ImGui.Spacing();



        if (ImGui.Button("Close", new Vector2(100, 0)))
        {
            IsOpen = false;
        }
    }
}
