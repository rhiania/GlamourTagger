using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace GlamourTagger;

// Rolling backups of the config (tags, flags, wishlists): session start/end, autosave, pre-import.
public class BackupManager : IDisposable
{
    private readonly Plugin plugin;
    private readonly string backupDir;
    private readonly object lockObj = new();
    private CancellationTokenSource? debounceCts;
    private bool isDirty = false;

    public BackupManager(Plugin plugin)
    {
        this.plugin = plugin;
        this.backupDir = Path.Combine(Plugin.PluginInterface.ConfigDirectory.FullName, "backups");

        if (!Directory.Exists(backupDir))
        {
            Directory.CreateDirectory(backupDir);
        }

        // 1. Takarítási logika futtatása betöltéskor
        RotateBackups();

        // 2. Aznapi start mentés ellenőrzése és létrehozása
        CreateStartBackupIfNeeded();
    }

    // called on every config save - restarts the 5s autosave timer
    public void MarkDirty()
    {
        lock (lockObj)
        {
            isDirty = true;
            debounceCts?.Cancel();
            debounceCts?.Dispose();
            debounceCts = new CancellationTokenSource();
            var token = debounceCts.Token;

            // 5 másodperces debounce időzítő háttérszálon
            Task.Delay(5000, token).ContinueWith(task =>
            {
                if (!task.IsCanceled)
                {
                    SaveAutoSaveAsync();
                }
            }, TaskScheduler.Default);
        }
    }

    /// <summary>
    /// Azonnal kiírja a lemezre a függőben lévő autosave-et (pl. ablak bezárásakor vagy exportálás előtt).
    /// </summary>
    public void FlushAutoSave()
    {
        lock (lockObj)
        {
            if (!isDirty) return;
            isDirty = false;
            debounceCts?.Cancel();
            debounceCts?.Dispose();
            debounceCts = null;
        }

        try
        {
            var filePath = Path.Combine(backupDir, "autosave_current.json");
            File.WriteAllText(filePath, SerializeConfig());
        }
        catch (Exception ex)
        {
            Plugin.Log.Error(ex, "[Glamour Tagger] Immediate autosave failed.");
        }
    }

    /// <summary>
    /// Biztonsági mentést készít közvetlenül adatok importálása előtt.
    /// </summary>
    public void CreatePreImportBackup()
    {
        try
        {
            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmm");
            var filePath = Path.Combine(backupDir, $"backup_{timestamp}_preimport.json");
            File.WriteAllText(filePath, SerializeConfig());
        }
        catch (Exception ex)
        {
            Plugin.Log.Error(ex, "[Glamour Tagger] Preimport backup failed.");
        }
    }

    // debounced autosave, the file write itself goes to a background task
    private void SaveAutoSaveAsync()
    {
        lock (lockObj)
        {
            if (!isDirty) return;
            isDirty = false;
        }

        var json = SerializeConfig();
        var filePath = Path.Combine(backupDir, "autosave_current.json");

        Task.Run(async () =>
        {
            try
            {
                await File.WriteAllTextAsync(filePath, json);
            }
            catch (Exception ex)
            {
                Plugin.Log.Error(ex, "[Glamour Tagger] Asyncron autosave failed.");
            }
        });
    }

    // one "start" backup per day (the day flips at 4am, not at midnight)
    private void CreateStartBackupIfNeeded()
    {
        try
        {
            // Jelenlegi logikai nap meghatározása (reggel 4-es eltolással)
            var currentLogicalDate = DateTime.Now.AddHours(-4).ToString("yyyyMMdd");

            var hasStartForToday = Directory.GetFiles(backupDir, "backup_*_*_start.json")
                .Select(f => new BackupFileInfo(f))
                .Any(info => info.IsValid && info.DateStr == currentLogicalDate);

            if (!hasStartForToday)
            {
                var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmm");
                var filePath = Path.Combine(backupDir, $"backup_{timestamp}_start.json");
                File.WriteAllText(filePath, SerializeConfig());
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.Error(ex, "[Glamour Tagger] Creating start backup file failed.");
        }
    }

    // written on Dispose, i.e. plugin unload / game exit
    public void CreateEndBackup()
    {
        try
        {
            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmm");
            var filePath = Path.Combine(backupDir, $"backup_{timestamp}_end.json");
            File.WriteAllText(filePath, SerializeConfig());
        }
        catch (Exception ex)
        {
            Plugin.Log.Error(ex, "[Glamour Tagger] Creating end backup file failed.");
        }
    }

    // a backup is simply the whole config as indented json
    private string SerializeConfig()
    {
        var options = new JsonSerializerOptions { WriteIndented = true };
        return JsonSerializer.Serialize(plugin.Configuration, options);
    }

    // thin out old backups so the folder doesn't grow forever
    public void RotateBackups()
    {
        try
        {
            // 1. Preimport mentések tisztítása: a 24 óránál régebbi preimport fájlok törlése
            var preimportFiles = Directory.GetFiles(backupDir, "backup_*_preimport.json");
            var cutOffTime = DateTime.Now.AddHours(-24);

            foreach (var filePath in preimportFiles)
            {
                if (File.GetCreationTime(filePath) < cutOffTime)
                {
                    try
                    {
                        File.Delete(filePath);
                    }
                    catch (Exception ex)
                    {
                        Plugin.Log.Error(ex, $"[Glamour Tagger] Old preimport backup deletion failed: {filePath}");
                    }
                }
            }

            // 2. Normál mentések begyűjtése — a preimport mentések KIZÁRÁSÁVAL
            var files = Directory.GetFiles(backupDir, "backup_*_*.json")
                .Select(f => new BackupFileInfo(f))
                .Where(info => info.IsValid && info.Type != "preimport")
                .ToList();

            if (files.Count == 0) return;

            // Csoportosítás relatív aktív napok (YYYYMMDD dátumbélyeg) szerint csökkenő sorrendben
            var groupedByDay = files
                .GroupBy(f => f.DateStr)
                .OrderByDescending(g => g.Key)
                .ToList();

            var filesToDelete = new List<BackupFileInfo>();

            for (int dayIndex = 0; dayIndex < groupedByDay.Count; dayIndex++)
            {
                var dayGroup = groupedByDay[dayIndex].ToList();

                if (dayIndex == 0)
                {
                    // 0. aktív nap (mai / legutóbbi): 1x start, Max 2x end (legfrissebbek)
                    var startFiles = dayGroup.Where(f => f.Type == "start").OrderBy(f => f.TimestampStr).ToList();
                    var endFiles = dayGroup.Where(f => f.Type == "end").OrderByDescending(f => f.TimestampStr).ToList();

                    if (startFiles.Count > 1) filesToDelete.AddRange(startFiles.Skip(1));
                    if (endFiles.Count > 2) filesToDelete.AddRange(endFiles.Skip(2));
                }
                else if (dayIndex == 1)
                {
                    // -1. aktív nap: Naponta max 2 mentés (aznapi legelső start és legutolsó end)
                    var firstStart = dayGroup.Where(f => f.Type == "start").OrderBy(f => f.TimestampStr).FirstOrDefault();
                    var lastEnd = dayGroup.Where(f => f.Type == "end").OrderByDescending(f => f.TimestampStr).FirstOrDefault();

                    foreach (var file in dayGroup)
                    {
                        if (file != firstStart && file != lastEnd)
                        {
                            filesToDelete.Add(file);
                        }
                    }
                }
                else
                {
                    // -2. és régebbi aktív napok: Naponta max 1 mentés (aznapi legutolsó end)
                    var lastEnd = dayGroup.Where(f => f.Type == "end").OrderByDescending(f => f.TimestampStr).FirstOrDefault();
                    var fallback = lastEnd ?? dayGroup.OrderByDescending(f => f.TimestampStr).FirstOrDefault();

                    foreach (var file in dayGroup)
                    {
                        if (file != fallback) filesToDelete.Add(file);
                    }
                }
            }

            // actually delete what the rules above marked
            foreach (var file in filesToDelete)
            {
                File.Delete(file.FilePath);
                files.Remove(file);
            }

            // Végső korlát (Hard limit): Összesen maximum 8 db backup_*.json maradhat (preimport nélkül)
            var remainingFiles = files
                .Where(f => File.Exists(f.FilePath))
                .OrderBy(f => f.DateStr)
                .ThenBy(f => f.TimestampStr)
                .ToList();

            while (remainingFiles.Count > 8)
            {
                var oldestFile = remainingFiles.First();
                File.Delete(oldestFile.FilePath);
                remainingFiles.RemoveAt(0);
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.Error(ex, "[Glamour Tagger] Backup rotation unsuccessful.");
        }
    }

    // row data for the Backup & Restore Manager table
    public class BackupItemInfo
    {
        public string FilePath { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public string DisplayType { get; set; } = string.Empty;
        public DateTime CreationTime { get; set; }
        public long FileSizeBytes { get; set; }
    }

    /// <summary>
    /// Beolvassa a mentési mappában található összes létező backup fájlt.
    /// </summary>
    public List<BackupItemInfo> GetAvailableBackups()
    {
        var list = new List<BackupItemInfo>();
        if (!Directory.Exists(backupDir)) return list;

        var files = Directory.GetFiles(backupDir, "*.json");
        foreach (var file in files)
        {
            var info = new FileInfo(file);
            var name = info.Name;

            // the type shown in the table comes from the file name suffix
            string displayType = "Autosave";
            if (name.Contains("_start")) displayType = "Session Start";
            else if (name.Contains("_end")) displayType = "Session End";
            else if (name.Contains("_preimport")) displayType = "Pre-Import";
            else if (name.Equals("autosave_current.json", StringComparison.OrdinalIgnoreCase)) displayType = "Current Autosave";

            list.Add(new BackupItemInfo
            {
                FilePath = file,
                FileName = name,
                DisplayType = displayType,
                CreationTime = info.LastWriteTime,
                FileSizeBytes = info.Length
            });
        }

        return list.OrderByDescending(x => x.CreationTime).ToList();
    }

    /// <summary>
    /// Adott mentési fájlok törlése a lemezről.
    /// </summary>
    public void DeleteBackupFiles(IEnumerable<string> filePaths)
    {
        foreach (var path in filePaths)
        {
            try
            {
                if (File.Exists(path)) File.Delete(path);
            }
            catch (Exception ex)
            {
                Plugin.Log.Error(ex, $"[Glamour Tagger] Failed to delete backup: {path}");
            }
        }
    }

    /// <summary>
    /// Az összes mentési fájl törlése a mentési mappából.
    /// </summary>
    public void DeleteAllBackups()
    {
        try
        {
            if (Directory.Exists(backupDir))
            {
                var files = Directory.GetFiles(backupDir, "*.json");
                foreach (var file in files)
                {
                    File.Delete(file);
                }
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.Error(ex, "[Glamour Tagger] Failed to delete all backups.");
        }
    }

    /// <summary>
    /// Visszaállítja a megadott backup fájlból a konfigot (Merge vagy Full Reset módban).
    /// </summary>
    public bool RestoreFromBackup(string filePath, bool isMerge)
    {
        try
        {
            if (!File.Exists(filePath)) return false;

            string json = File.ReadAllText(filePath);
            var loadedConfig = JsonSerializer.Deserialize<Configuration>(json);
            if (loadedConfig == null) return false;

            if (isMerge)
            {
                // 1. Tagek bemergelése (meglévők megtartásával)
                if (loadedConfig.ItemTags != null)
                {
                    plugin.Configuration.MergeItemTags(loadedConfig.ItemTags);
                }

                // 2. Kedvencek és Wishlist bemergelése
                if (loadedConfig.Favorites != null) plugin.Configuration.Favorites.UnionWith(loadedConfig.Favorites);
                if (loadedConfig.DoubleFavorites != null) plugin.Configuration.DoubleFavorites.UnionWith(loadedConfig.DoubleFavorites);
                if (loadedConfig.TripleFavorites != null) plugin.Configuration.TripleFavorites.UnionWith(loadedConfig.TripleFavorites);
                if (loadedConfig.Wishlist != null) plugin.Configuration.Wishlist.UnionWith(loadedConfig.Wishlist);
                if (loadedConfig.DoubleWishlist != null) plugin.Configuration.DoubleWishlist.UnionWith(loadedConfig.DoubleWishlist);
            }
            else
            {
                // Full Reset: Teljes felülírás a mentésben lévő értékekkel
                plugin.Configuration.ItemTags = loadedConfig.ItemTags ?? new();
                plugin.Configuration.Favorites = loadedConfig.Favorites ?? new();
                plugin.Configuration.DoubleFavorites = loadedConfig.DoubleFavorites ?? new();
                plugin.Configuration.TripleFavorites = loadedConfig.TripleFavorites ?? new();
                plugin.Configuration.Wishlist = loadedConfig.Wishlist ?? new();
                plugin.Configuration.DoubleWishlist = loadedConfig.DoubleWishlist ?? new();
            }

            plugin.Configuration.Save();
            return true;
        }
        catch (Exception ex)
        {
            Plugin.Log.Error(ex, "[Glamour Tagger] Failed to restore backup file.");
            return false;
        }
    }

    public void Dispose()
    {
        lock (lockObj)
        {
            debounceCts?.Cancel();
            debounceCts?.Dispose();
        }

        // don't lose a pending autosave on unload
        if (isDirty)
        {
            try
            {
                File.WriteAllText(Path.Combine(backupDir, "autosave_current.json"), SerializeConfig());
            }
            catch { }
        }

        CreateEndBackup();
    }

    // parses backup_yyyyMMdd_HHmm_<type>.json
    private class BackupFileInfo
    {
        public string FilePath { get; }
        public string DateStr { get; } = string.Empty; // Logikai dátum (reggel 4 órás váltással)
        public string TimestampStr { get; } = string.Empty; // Valós időbélyeg a pontos rendezéshez
        public string Type { get; } = string.Empty;
        public bool IsValid { get; }

        public BackupFileInfo(string filePath)
        {
            FilePath = filePath;
            var fileName = Path.GetFileNameWithoutExtension(filePath);
            var parts = fileName.Split('_');

            if (parts.Length == 4 && parts[0] == "backup")
            {
                if (DateTime.TryParseExact($"{parts[1]}_{parts[2]}", "yyyyMMdd_HHmm",
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.None, out var dt))
                {
                    // 4 óra levonásával a 00:00–03:59 közötti mentések még az előző nap csoportjába kerülnek
                    DateStr = dt.AddHours(-4).ToString("yyyyMMdd");
                    TimestampStr = $"{parts[1]}_{parts[2]}";
                    Type = parts[3];
                    IsValid = true;
                }
            }
        }
    }
}
