using Dalamud.Configuration;
using Dalamud.Plugin;
using System;
using System.Collections.Generic;

namespace GlamourTagger;

// one dye set of the equipment bar: stain ids for the two dye channels
[Serializable]
public class DyeSet
{
    public byte Dye1 { get; set; } = DyeHelper.DyeKeepCurrent; // default: keep Glamourer's own color (don't overwrite)
    public byte Dye2 { get; set; } = DyeHelper.DyeKeepCurrent; // default: keep Glamourer's own color (don't overwrite)
}

[Serializable]
public class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 0;
    public string LastSeenVersion { get; set; } = string.Empty;

    public string CustomExportImportPath { get; set; } = string.Empty;
    // flags = sets of item ids
    public HashSet<uint> Favorites { get; set; } = new();
    public HashSet<uint> DoubleFavorites { get; set; } = new();
    public HashSet<uint> TripleFavorites { get; set; } = new();
    public HashSet<uint> Wishlist { get; set; } = new();
    public HashSet<uint> DoubleWishlist { get; set; } = new();
    // tags per item id, and the colour picked for each tag
    public Dictionary<uint, List<string>> ItemTags { get; set; } = new();
    public Dictionary<string, System.Numerics.Vector4> TagColors = new();
    // manual Penumbra overrides per item: mod names marked demodded / unusable
        // colour of a tag that has no picked colour (the first colour of the Tag & Flag Manager palette)
    public static readonly System.Numerics.Vector4 DefaultTagColor = new(0.09f, 0.16f, 0.29f, 1.0f);
    // default colour the Tag & Flag Manager gave new tags up to v1.4.2 (see the config v2 step in Initialize)
    private static readonly System.Numerics.Vector4 LegacyManagerTagColor = new(0.15f, 0.30f, 0.55f, 1.0f);
    public Dictionary<uint, HashSet<string>> DemoddedMods { get; set; } = new();
    public Dictionary<uint, HashSet<string>> UnusableMods { get; set; } = new();
    // last Penumbra scan (item name -> mods), saved so new catches can be detected after a restart too
    public Dictionary<string, List<string>> SavedPenumbraCache { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    // Penumbra catch notification switches (Settings window)
    public bool NeverShowCatchWindow { get; set; } = false;
    public bool IgnorePrevDemoddedCatches { get; set; } = false;
    public bool IgnoreNewlyModdedCatches { get; set; } = false;
    public bool IgnoreUnusableCatches { get; set; } = false;

    // Fitting Room Assist window
    public bool ClearFittingRoomCacheOnJobChange { get; set; } = false; 
    public bool FittingRoomInfoExpanded { get; set; } = true;

    public float TableIconSize { get; set; } = 24.0f; // Alapértelmezett sorméret / ikonméret pixelben
    public float HoverIconScale { get; set; } = 3.0f;  // A kinagyított hover tooltip szorzója (pl. 3x-os)

    // which ring slot(s) a ring preview goes to (RR / LR buttons)
    public bool RightRingActive { get; set; } = true;
    public bool LeftRingActive { get; set; } = false;

    public int SelectedDyeSet { get; set; } = -1; // -1 = None selected
    public DyeSet[] DyeSets { get; set; } = new DyeSet[3] { new(), new(), new() };

    public bool ShowAffectingModsSection { get; set; } = true;

    [NonSerialized]
    private IDalamudPluginInterface? pluginInterface;

    [System.Text.Json.Serialization.JsonIgnore]
    [Newtonsoft.Json.JsonIgnore]
    private Plugin? plugin;

    public void Initialize(IDalamudPluginInterface pluginInterface, Plugin plugin)
    {
        this.pluginInterface = pluginInterface;
        this.plugin = plugin;

        if (DyeSets == null || DyeSets.Length < 3)
        {
            DyeSets = new DyeSet[3] { new(), new(), new() };
        }

        // Config v1: "Clear on Job Change" used to default to ON, but it was never connected to a job change.
        // Now that it works, everyone starts from OFF instead of suddenly losing their try-on memory.
        if (Version < 1)
        {
            ClearFittingRoomCacheOnJobChange = false;
            Version = 1;
            Save();
        }

        // Config v2: tags created in the Tag & Flag Manager used to get their own, brighter default blue.
        // They now share DefaultTagColor. The old blue was never in the palette, so it can only be that default.
        if (Version < 2)
        {
            TagColors ??= new();
            foreach (var tag in new List<string>(TagColors.Keys))
            {
                if (System.Numerics.Vector4.Distance(TagColors[tag], LegacyManagerTagColor) < 0.005f)
                {
                    TagColors[tag] = DefaultTagColor;
                }
            }
            Version = 2;
            Save();
        }
    }

    /// <summary>
    /// Adds the incoming tags to the tags the items already have. Nothing is removed or replaced;
    /// a tag the item already has (exact same text) is skipped. Returns the number of tags added. Does not save.
    /// </summary>
    public int MergeItemTags(IReadOnlyDictionary<uint, List<string>> incoming)
    {
        int added = 0;

        foreach (var (itemId, incomingTags) in incoming)
        {
            if (incomingTags == null) continue; // shared / hand-edited files can contain "id": null

            ItemTags.TryGetValue(itemId, out var currentTags);

            foreach (var incomingTag in incomingTags)
            {
                if (string.IsNullOrWhiteSpace(incomingTag)) continue;
                string tag = incomingTag.Trim();

                if (currentTags == null)
                {
                    currentTags = new List<string>();
                    ItemTags[itemId] = currentTags;
                }

                if (currentTags.Contains(tag)) continue;

                currentTags.Add(tag);
                added++;
            }
        }

        return added;
    }

    // every save also pokes the backup manager (debounced autosave)
    public void Save()
    {
        this.pluginInterface?.SavePluginConfig(this);
        this.plugin?.BackupManager?.MarkDirty(); // Így a meglévő példányt hívja meg
    }
}
