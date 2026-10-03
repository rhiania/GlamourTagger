# Glamour Tagger

Designed to help you tag, flag, organize and filter all items effortlessly, bridging the gap between massive item databases and your glamours.

> **This mod would not have come to life without [Glamourer](https://github.com/Otuv/Glamourer) or [Penumbra](https://github.com/xivdev/Penumbra).** Huge thanks to their developers for the inspiration and integration capabilities!

---

## New with v1.4.1.0
* Supplemented dyeing slots with an option to keep current filters, and added a quick job filter button.
## New with v1.4.0.0
* Introducing Dye Sets with more Glamourer integration!
## New with v1.3.0.0
* Left ring slot preview support!
* Penumbra integration to see and filter by your mods!

---

## Installation (Dalamud Repository)

To install this plugin in FFXIV via Dalamud, add the following custom repository URL:

```https://raw.githubusercontent.com/rhiania/GlamourTagger/refs/heads/main/repo.json```

> **Prerequisite for character preview:** [Glamourer](https://github.com/Otuv/Glamourer) must be installed.

> **Prerequisite for mod listings and filters:** [Penumbra](https://github.com/xivdev/Penumbra) must be installed.

---

## Functions & Quick Guide

1. **Tag, Flag and Organize items:**
   * Flag your Favorites (`F`), Double Favorites (`DF`), Triple Favorites (`TF`), Wish-listed (`W`) or Double Wish-listed (`DW`) items by clicking the `star` or `shopping basket` symbols.
   * Use column header pop-ups to quickly bulk-add or bulk-remove flags for filtered items.
   * Sync your favourites directly from Glamourer via the Options menu.
   * Assign custom tags or add/delete them by right-clicking (or `Ctrl + Shift + right-clicking`) in the tag column.

2. **Manage Your Tags:**
   * Open `Options -> Tag Management` to rename tags, customize badge colors, view item counts, or delete tags (requires `Ctrl + Shift`).

3. **Export or Import Tags, Favourites and Wishlists:**
   * Easily back up or share your tags, favourites and wishlists via JSON files with custom directory path support.

4. **Filter & Search:**
   * Use the new Equipment bar to filter by item slots quickly (one slot with left-clicking and add item slots with right-clicking).
   * Use headers or the search bars to filter items.
   * For tag filters, switch between **OR Mode** (items matching *ANY* selected tag) and **AND Mode** (items matching *ALL* selected tags simultaneously), as well as hide filters with the **NOT** logic.
   * Use `t:`, `p:`, `j:`, `id:`, `m:` prefixes to quickly filter out tags, penumbra mods, jobs, item IDs or model IDs.
   * **New:** Click the job icon in the 'Item Name' header to instantly list the items of your current job (same as j:). Click it again to clear the search.
   * Check 'Only Show One Item Per Model Type' (with optional 'Ignore Variants') to clean up duplicate models.

5. **Preview Items:**
   * Toggle between 'Glamourer Preview' and 'Fitting Room Try On'. Right-click any item to instantly preview.
   * When previewing rings, you can select your right, your left or both of your hands to preview the items.
     
   * Dye Previews: Select a Dye Set to preview items with dyes applied in both Glamourer and Fitting Room modes.
   * Click the swap icon inside a dye set to swap primary/secondary dyes and use the Brush icon to apply any dye set to one or more selected equipment slots without having to activate that dye set globally.
   *  Dye Targets: By default the brush dyes the slots selected in the slot filter. Hold Shift and click slots on the Equipment Bar to pick different slots to dye without changing the filter. Works with RR/LR and 'ALL' too.
   *  Switch on the Dye Target toggle to do the same without holding Shift. Slots marked with a small droplet will be dyed. Middle-click the toggle to make the dye targets follow the filter again.
  
   * Fitting Room Assistant: Native fitting room previews track state via the Fitting Room Memory Assistant window.
   * Revert Character to Game State: Click the button to instantly reset your character's appearance back to their actual ingame look.


6. **Fast Scrolling:**
   * Hover over the 'Scroll Here' button at the bottom, then `Scroll Mouse Wheel` to rapidly cycle through filtered items for preview.
   * Use the `Jump to item` button right by the scroll box to quickly navigate to the previewed/selected item in your list.

7. **Penumbra integration & Mods affecting items:**
  * When Penumbra is active, Glamour Tagger detects modded items and marks them in the 'Penumbra Mods' column.
  * You can see your mods affecting the items in tooltips and in the bottom panel by selecting an item.
  * New states added for mods and items, which lets you filter out items where mod parts are uninstalled or where a mod falsely claims an item is modified (`Demodded` state) and items where the mod does not render properly or is unusable for you (`Unusable` state).
  * Penumbra Redraw & Recatch: you can trigger a manual cache recatch or force a Penumbra redraw on your character.
  * Penumbra Cache Collection: Glamour Tagger now collects modded items and their active mods from Penumbra on startup and whenever you click `Recatch & Redraw`.
  * Penumbra Notifications: Automatic pop-up alerts notify you when Penumbra refreshes caches and detects newly modded items or reintroduced mods previously marked as `Demodded` or `Unusable`. Also you can configure notification frequency or mute them in the Settings.

8. **Chat Integration:**
   * Shift + Left-click any item name in the table to link the item directly into your chat.
     
9. **Item links:**
   * Quickly look up equipment items using integrated links to community databases:
     * [Garland Tools](https://www.garlandtools.org/) for detailed item origin and recipe data.
     * [Gamer Escape](https://ffxiv.gamerescape.com/) for complete item lore and acquisition guides.
     * [FFXIV Teamcraft](https://ffxivteamcraft.com/) for craft tracking.

10. **Adjust visuals:**
    * Look up the visual adjustments for icon sizes in the Options menu - customize the size of icons in the lists or of the hover-over icon reveals.

11. **Automatic backup system with restore functions:**
    * The plugin has an automatic backup system to save your tags, flags and configs periodically and before importing to prevent data loss.
    * Open the Backup & Restore Manager from the Options menu to inspect backups or restore your data via Merge or Full Reset modes.

12. **Standalone & Failsafe Operation:**
    * Glamour Tagger includes full IPC failsafes. If Glamourer or Penumbra are disabled or not installed, the plugin operates smoothly in standalone mode without crashing or throwing errors.

---

#### Example Tag Collection (Work in Progress)
As an example, I’ve shared my own personal tag collection in the example folder.

*This is a raw, uncurated collection of mine, created for my own organization of my favourite items — focusing mostly on hands, legs and footwear. It is a baseline reflecting my own wardrobe choices and may contain minor errors, while only some items are tagged. Feel free to use it as a base, tweak it, or build your own system upon it!*

**How to use this, or any other collections:**
1. Download or copy the `.json` file from the `/examples` folder.
2. In-game, open **Glamour Tagger** -> **Options** -> **Tag Management**.
3. Use the **Import & Merge** feature to load (and merge) the tags into your plugin.

---

## Maintenance & Afterword

* This plugin was built with AI assistance — initially for personal use, but shared in case others find it helpful.
* Please note this is a hobby project maintained as time permits alongside a real-life work schedule.
* **Feedback & Contributions:** Constructive feedback, ideas, and issues are always welcome via the [GitHub Repository](https://github.com/rhiania/GlamourTagger).
