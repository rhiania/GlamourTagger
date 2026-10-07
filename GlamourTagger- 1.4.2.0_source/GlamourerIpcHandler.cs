using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace GlamourTagger.IPC;

/// <summary>
/// Optimized IPC wrapper for Glamourer try-on functionality using Glamourer.SetItem.V3.
/// </summary>
public sealed class GlamourerIpcHandler : IDisposable
{
    private readonly IDalamudPluginInterface apluginInterface;

    // Cached IPC CallGates - List<byte> forces Newtonsoft JSON to serialize as array [0,0] instead of base64 string
    private readonly ICallGateSubscriber<(int Major, int Minor)> apiVersionSubscriber;
    private readonly ICallGateSubscriber<int, byte, ulong, List<byte>, uint, ulong, int> setItemSubscriber; 
    private readonly ICallGateSubscriber<int, uint, ulong, int> revertStateSubscriber;
    private readonly ICallGateSubscriber<int, uint, (int, JObject?)> getStateSubscriber;
    private readonly ICallGateSubscriber<object, int, uint, ulong, int> applyStateSubscriber;

    // Glamourer ApiEquipSlot constants (RFinger = 12, LFinger = 14)
    public const byte SlotRightRing = 12;
    public const byte SlotLeftRing = 14;

    // ApplyFlag.Once (0x01) | ApplyFlag.Equipment (0x02) - temporary preview without persisting or locking state
    private const ulong ApplyFlagPreview = 3uL;

    // ApplyFlagEx.RevertDefault = Equipment (0x02) | Customization (0x04) = 6uL
    private const ulong ApplyFlagRevert = 6uL;

    /// <summary>Item and stain state for a single equipment slot, read from Glamourer.GetState.</summary>
    public readonly record struct SlotState(uint ItemId, byte Stain, byte Stain2);

    public GlamourerIpcHandler(IDalamudPluginInterface pluginInterface)
    {
        apluginInterface = pluginInterface;

        apiVersionSubscriber = apluginInterface.GetIpcSubscriber<(int, int)>("Glamourer.ApiVersion.V2");
        setItemSubscriber = apluginInterface.GetIpcSubscriber<int, byte, ulong, List<byte>, uint, ulong, int>("Glamourer.SetItem.V3");
        getStateSubscriber = apluginInterface.GetIpcSubscriber<int, uint, (int, JObject?)>("Glamourer.GetState");
        applyStateSubscriber = apluginInterface.GetIpcSubscriber<object, int, uint, ulong, int>("Glamourer.ApplyState");
        revertStateSubscriber = apluginInterface.GetIpcSubscriber<int, uint, ulong, int>("Glamourer.RevertState");
    }

    // cheap check before every call: if Glamourer answers ApiVersion, it is loaded
    public bool IsAvailable()
    {
        try
        {
            var (major, _) = apiVersionSubscriber.InvokeFunc();
            return major >= 0;
        }
        catch
        {
            return false;
        }
    }

    public bool TryOnLeftRing(ulong itemId, byte stainId = 0)
    {
        return ApplyRingItem(SlotLeftRing, itemId, stainId);
    }

    public bool TryOnRightRing(ulong itemId, byte stainId = 0)
    {
        return ApplyRingItem(SlotRightRing, itemId, stainId);
    }

    public bool ApplyRingItem(byte slot, ulong itemId, byte stainId = 0)
    {
        if (!IsAvailable())
        {
            Plugin.Log.Error("[GlamourTagger] Glamourer IPC is not available or Glamourer is disabled.");
            return false;
        }

        // List<byte> serializes cleanly to [stainId, 0] JSON array over Dalamud IPC
        List<byte> stains = new List<byte> { stainId, 0 };
        const int localPlayerIndex = 0;
        const uint key = 0;

        try
        {
            int resultCode = setItemSubscriber.InvokeFunc(localPlayerIndex, slot, itemId, stains, key, ApplyFlagPreview);

            // 0 = Success, 1 = NothingDone
            if (resultCode is 0 or 1)
            {
                return true;
            }

            Plugin.Log.Debug($"[GlamourTagger] Glamourer SetItem IPC error code: {resultCode} (Slot: {slot}, Item: {itemId})");
            return false;
        }
        catch (Exception ex)
        {
            Plugin.Log.Error(ex, $"[GlamourTagger] Exception during Glamourer SetItem IPC call (Slot: {slot}, Item: {itemId})");
            return false;
        }
    }

    // main entry: put an item (+ two dyes) on one slot of the local player as a temporary preview
    public bool ApplySlotItem(byte slot, ulong itemId, byte dye1 = 0, byte dye2 = 0, ulong? flagsOverride = null)
    {
        if (!IsAvailable())
        {
            Plugin.Log.Error("[GlamourTagger] Glamourer IPC is not available or Glamourer is disabled.");
            return false;
        }

        // List<byte> (not byte[]) so the IPC serializes the stains as a JSON array [dye1, dye2]
        List<byte> stains = new List<byte> { dye1, dye2 };
        const int localPlayerIndex = 0;
        const uint key = 0;

        // ApplyFlagPreview by default for all slots, unless explicitly overridden
        ulong flags = flagsOverride ?? ApplyFlagPreview;

        Plugin.Log.Debug($"[GlamourTagger Debug] IPC SetItem Invoking -> Slot: {slot} | ItemID: {itemId} | Stains: [{dye1}, {dye2}] | Flags: {flags}");

        try
        {
            int resultCode = setItemSubscriber.InvokeFunc(localPlayerIndex, slot, itemId, stains, key, flags);

            Plugin.Log.Debug($"[GlamourTagger Debug] IPC SetItem Returned -> Code: {resultCode} | Slot: {slot} | ItemID: {itemId}");

            if (resultCode is 0 or 1)
            {
                return true;
            }

            // Outputs exact diagnostic details to Dalamud Log / Console
            Plugin.Log.Error($"[GlamourTagger Debug] SetItem IPC Failed | Code: {resultCode} | Slot: {slot} | ItemID: {itemId} | Stains: [{dye1}, {dye2}] | Flags: {flags}");
            return false;
        }
        catch (Exception ex)
        {
            Plugin.Log.Error(ex, $"[GlamourTagger] Exception during Glamourer SetItem IPC call (Slot: {slot}, Item: {itemId})");
            return false;
        }
    }

    // EquipSlotCategory row (Item sheet) -> Glamourer ApiEquipSlot id
    public byte GetGlamourerSlotId(uint equipSlotCategory)
    {
        return equipSlotCategory switch
        {
            1 or 13 => 1,               // MainHand
            2 => 2,                     // OffHand (ApiEquipSlot.OffHand = 2)
            3 or 14 or 15 or 20 => 3,   // Head
            4 or 16 or 19 or 21 => 4,   // Body
            5 => 5,                     // Hands
            7 or 17 or 18 => 7,         // Legs
            8 => 8,                     // Feet
            9 => 9,                     // Ears
            10 => 10,                   // Neck
            11 => 11,                   // Wrists
            12 => 12,                   // RFinger
            _ => 0
        };
    }

    public uint GetCurrentSlotItemId(string jsonSlotName, int objectIndex = 0)
    {
        return TryGetSlotState(jsonSlotName, objectIndex, out var state) ? state.ItemId : 0;
    }

    /// <summary>
    /// Resolves the current item ID and stains for a single equipment slot, handling the
    /// same slot-name aliases the rest of the plugin uses (Shield/Chest/Gloves/Pants/Finger).
    /// </summary>
    public bool TryGetSlotState(string jsonSlotName, int objectIndex, out SlotState state)
    {
        state = default;
        if (!IsAvailable()) return false;

        try
        {
            var (resultCode, root) = getStateSubscriber.InvokeFunc(objectIndex, 0);
            if (resultCode != 0 || root == null) return false;

            var equip = root["Equipment"] as JObject;
            if (equip == null) return false;

            foreach (var prop in equip.Properties())
            {
                bool isMatch = string.Equals(prop.Name, jsonSlotName, StringComparison.OrdinalIgnoreCase) ||
                    (jsonSlotName.Equals("Shield", StringComparison.OrdinalIgnoreCase) && prop.Name.Equals("OffHand", StringComparison.OrdinalIgnoreCase)) ||
                    (jsonSlotName.Equals("Chest", StringComparison.OrdinalIgnoreCase) && prop.Name.Equals("Body", StringComparison.OrdinalIgnoreCase)) ||
                    (jsonSlotName.Equals("Gloves", StringComparison.OrdinalIgnoreCase) && prop.Name.Equals("Hands", StringComparison.OrdinalIgnoreCase)) ||
                    (jsonSlotName.Equals("Pants", StringComparison.OrdinalIgnoreCase) && prop.Name.Equals("Legs", StringComparison.OrdinalIgnoreCase)) ||
                    ((jsonSlotName.Equals("Finger", StringComparison.OrdinalIgnoreCase) || jsonSlotName.Equals("Ring", StringComparison.OrdinalIgnoreCase)) &&
                     (prop.Name.Equals("RFinger", StringComparison.OrdinalIgnoreCase) || prop.Name.Equals("LFinger", StringComparison.OrdinalIgnoreCase)));

                if (!isMatch) continue;

                uint itemId = prop.Value?["ItemId"]?.Value<uint>() ?? 0;
                byte stain = prop.Value?["Stain"]?.Value<byte>() ?? 0;
                byte stain2 = prop.Value?["Stain2"]?.Value<byte>() ?? 0;
                state = new SlotState(itemId, stain, stain2);
                return itemId != 0;
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.Debug($"[GlamourTagger] Failed to read Glamourer state for slot {jsonSlotName}: {ex.Message}");
        }

        return false;
    }

    /// <summary>Convenience wrapper for the dye-sentinel resolution path — item ID is not needed there.</summary>
    public bool TryGetSlotStains(string jsonSlotName, out byte stain, out byte stain2, int objectIndex = 0)
    {
        if (TryGetSlotState(jsonSlotName, objectIndex, out var state))
        {
            stain = state.Stain;
            stain2 = state.Stain2;
            return true;
        }

        stain = 0;
        stain2 = 0;
        return false;
    }

    /// <summary>
    /// Retrieves all currently previewed/equipped item IDs and stains across all equipment
    /// slots from Glamourer state in a single IPC call.
    /// </summary>
    public Dictionary<string, SlotState> GetAllSlotStates(int objectIndex = 0)
    {
        var result = new Dictionary<string, SlotState>(StringComparer.OrdinalIgnoreCase);
        if (!IsAvailable()) return result;

        try
        {
            var (resultCode, root) = getStateSubscriber.InvokeFunc(objectIndex, 0);
            if (resultCode != 0 || root == null) return result;

            var equip = root["Equipment"] as JObject;
            if (equip == null) return result;

            foreach (var prop in equip.Properties())
            {
                uint itemId = prop.Value?["ItemId"]?.Value<uint>() ?? 0;
                byte stain = prop.Value?["Stain"]?.Value<byte>() ?? 0;
                byte stain2 = prop.Value?["Stain2"]?.Value<byte>() ?? 0;
                result[prop.Name] = new SlotState(itemId, stain, stain2);
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.Debug($"[GlamourTagger] Failed to parse Glamourer state dictionary: {ex.Message}");
        }

        return result;
    }

    /// <summary>Item-ID-only view of <see cref="GetAllSlotStates"/>, kept for existing callers.</summary>
    public Dictionary<string, uint> GetAllSlotItemIds(int objectIndex = 0)
    {
        var result = new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase);
        foreach (var (slot, state) in GetAllSlotStates(objectIndex))
        {
            result[slot] = state.ItemId;
        }
        return result;
    }

    private static bool TryExtractItemId(System.Text.Json.JsonElement element, out uint itemId)
    {
        itemId = 0;

        if (element.ValueKind == System.Text.Json.JsonValueKind.Number)
        {
            if (element.TryGetUInt32(out itemId)) return itemId > 0;
            if (element.TryGetUInt64(out var u64Val))
            {
                itemId = (uint)u64Val;
                return itemId > 0;
            }
        }
        else if (element.ValueKind == System.Text.Json.JsonValueKind.String)
        {
            var val = element.GetString().AsSpan();
            if (val.StartsWith("Item:", StringComparison.OrdinalIgnoreCase))
                val = val.Slice(5);

            if (uint.TryParse(val, out itemId)) return itemId > 0;
            if (ulong.TryParse(val, out var u64Val))
            {
                itemId = (uint)u64Val;
                return itemId > 0;
            }
        }
        else if (element.ValueKind == System.Text.Json.JsonValueKind.Object)
        {
            if (element.TryGetProperty("Id", out var idProp) || element.TryGetProperty("ItemId", out idProp))
            {
                return TryExtractItemId(idProp, out itemId);
            }
        }

        return false;
    }

    /// <summary>
    /// Fetches current state, patches only the OffHand stain values, and re-applies the
    /// whole state via Glamourer.ApplyState — a different IPC entry point than SetItem,
    /// in case SetItem's Shield validation issue doesn't affect ApplyState the same way.
    /// </summary>
    public bool TryApplyShieldStain(uint expectedShieldItemId, byte dye1, byte dye2, int objectIndex = 0)
    {
        try
        {
            var (getEc, state) = getStateSubscriber.InvokeFunc(objectIndex, 0);
            if (getEc != 0 || state == null)
            {
                Plugin.Log.Error($"[GlamourTagger] GetState failed before shield stain patch | Code: {getEc}");
                return false;
            }

            var offHand = state["Equipment"]?["OffHand"];
            if (offHand == null)
            {
                Plugin.Log.Error("[GlamourTagger] GetState JSON had no Equipment.OffHand entry.");
                return false;
            }

            uint currentId = offHand["ItemId"]?.Value<uint>() ?? 0;
            if (currentId != expectedShieldItemId)
            {
                Plugin.Log.Error($"[GlamourTagger] OffHand ItemId in state ({currentId}) didn't match expected shield ({expectedShieldItemId}); aborting.");
                return false;
            }

            offHand["Stain"] = dye1;
            offHand["Stain2"] = dye2;

            int applyEc = applyStateSubscriber.InvokeFunc(state, objectIndex, 0u, ApplyFlagPreview);
            Plugin.Log.Debug($"[GlamourTagger Debug] ApplyState (shield stain patch) -> Code: {applyEc}");
            return applyEc is 0 or 1;
        }
        catch (Exception ex)
        {
            Plugin.Log.Error(ex, "[GlamourTagger] Exception during shield stain ApplyState patch.");
            return false;
        }
    }

    // undo every preview: back to the game state
    public bool RevertEquipment()
    {
        if (!IsAvailable()) return false;

        try
        {
            int resultCode = revertStateSubscriber.InvokeFunc(0, 0, ApplyFlagRevert);
            return resultCode is 0 or 1;
        }
        catch (Exception ex)
        {
            Plugin.Log.Error(ex, "[GlamourTagger] Exception during Glamourer RevertState IPC call.");
            return false;
        }
    }

    // debugging aid: writes the raw GetState json into the log
    public void DumpState(int objectIndex = 0)
    {
        try
        {
            var (ec, data) = getStateSubscriber.InvokeFunc(objectIndex, 0);
            Plugin.Log.Debug($"[GlamourTagger Debug] GetState -> Code: {ec} | JSON: {data?.ToString() ?? "(null)"}");
        }
        catch (Exception ex)
        {
            Plugin.Log.Error(ex, "[GlamourTagger Debug] GetState IPC threw an exception.");
        }
    }

    public void Dispose()
    {
    }
}
