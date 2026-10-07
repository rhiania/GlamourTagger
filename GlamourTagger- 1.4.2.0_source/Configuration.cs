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
    public bool ClearFittingRoomCacheOnJobChange { get; set; } = true;
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
    }

    // every save also pokes the backup manager (debounced autosave)
    public void Save()
    {
        this.pluginInterface?.SavePluginConfig(this);
        this.plugin?.BackupManager?.MarkDirty(); // Így a meglévő példányt hívja meg
    }
}
