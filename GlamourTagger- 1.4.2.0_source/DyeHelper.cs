using Dalamud.Plugin.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace GlamourTagger;

// one row of the Stain sheet, colour already converted for ImGui
public record DyeInfo(byte Id, string Name, Vector4 ColorVec, byte Shade);

// Stain sheet lookup + grouping by shade. Loaded once, then cached.
public static class DyeHelper
{
    private static List<DyeInfo>? cachedDyes;
    private static Dictionary<byte, List<DyeInfo>>? groupedDyes;

    // Explicit whitelist of metallic dye IDs requested for the metallic overlay
    private static readonly HashSet<byte> MetallicDyeIds = new()
    {
        92, 93, 94, 112, 113, 114, 115, 116, 117, 118, 119, 120, 122, 123, 124, 125
    };

    // Official in-game ordering sequence for Special category dyes (shade 10)
    private static readonly List<byte> SpecialDyeOrder = new()
    {
        101, // Pure White
        102, // Jet Black
        103, // Pastel Pink
        104, 105, 106, 107, 108, 109, 110, 111,
        112, 113,
        114, 115, 116, 117, 118, 119, 120,
        92, 93, 94,
        122, 123, 124, 125
    };

    public static bool IsMetallic(byte id) => MetallicDyeIds.Contains(id);
    /// <summary>Sentinel dye value: leave this stain channel at its current in-game value instead of applying/resetting it.</summary>
    public const byte DyeKeepCurrent = 255;

    public static List<DyeInfo> GetDyes(IDataManager dataManager)
    {
        if (cachedDyes != null) return cachedDyes;

        cachedDyes = new List<DyeInfo>();
        groupedDyes = new Dictionary<byte, List<DyeInfo>>();

        try
        {
            var stainSheet = dataManager.GetExcelSheet<Lumina.Excel.Sheets.Stain>();
            if (stainSheet != null)
            {
                foreach (var stain in stainSheet)
                {
                    if (stain.RowId == 0) continue;
                    string name = stain.Name.ToString();
                    if (string.IsNullOrEmpty(name)) continue;

                    // Stain.Color is 0xRRGGBB
                    uint c = stain.Color;
                    float r = ((c >> 16) & 0xFF) / 255.0f;
                    float g = ((c >> 8) & 0xFF) / 255.0f;
                    float b = (c & 0xFF) / 255.0f;
                    Vector4 colorVec = new Vector4(r, g, b, 1.0f);

                    byte shade = stain.Shade;
                    var info = new DyeInfo((byte)stain.RowId, name, colorVec, shade);
                    cachedDyes.Add(info);

                    if (!groupedDyes.TryGetValue(shade, out var shadeList))
                    {
                        shadeList = new List<DyeInfo>();
                        groupedDyes[shade] = shadeList;
                    }
                    shadeList.Add(info);
                }

                // Sort Special category (shade 10) according to official in-game sequence
                foreach (var kvp in groupedDyes)
                {
                    if (kvp.Key == 10)
                    {
                        kvp.Value.Sort((a, b) =>
                        {
                            int idxA = SpecialDyeOrder.IndexOf(a.Id);
                            int idxB = SpecialDyeOrder.IndexOf(b.Id);
                            if (idxA != -1 && idxB != -1) return idxA.CompareTo(idxB);
                            if (idxA != -1) return -1;
                            if (idxB != -1) return 1;
                            return a.Id.CompareTo(b.Id);
                        });
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.Error(ex, "Failed to load stain sheet from DataManager.");
        }

        return cachedDyes;
    }

    public static Dictionary<byte, List<DyeInfo>> GetGroupedDyes(IDataManager dataManager)
    {
        if (groupedDyes == null) GetDyes(dataManager);
        return groupedDyes ?? new Dictionary<byte, List<DyeInfo>>();
    }

    public static DyeInfo? GetDye(IDataManager dataManager, byte id)
    {
        if (id == 0) return null;
        return GetDyes(dataManager).FirstOrDefault(d => d.Id == id);
    }

    // Stain.Shade -> section title in the dye picker
    public static string GetShadeName(byte shade) => shade switch
    {
        1 or 2 => "Grayscale",
        3 or 4 => "Reds",
        5 => "Browns",
        6 => "Yellows",
        7 => "Greens",
        8 => "Blues",
        9 => "Purples",
        10 => "Special / Metallic",
        _ => $"Category {shade}"
    };
}
