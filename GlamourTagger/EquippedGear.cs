using FFXIVClientStructs.FFXIV.Client.Game;
using System;
using System.Collections.Generic;
using SlotState = GlamourTagger.IPC.GlamourerIpcHandler.SlotState;

namespace GlamourTagger;

/// <summary>
/// The character's own in-game gear, read straight from the game: the item on every slot (its glamour, if it has one)
/// and its two dyes. A dyed item has one dye pair, which shows on its glamour too. The native Fitting Room shows this
/// gear on every slot nothing was tried on, whatever Glamourer has changed on the character.
/// </summary>
public static class EquippedGear
{
    // Slot key -> index in the game's equipped items container (5 is the old belt slot)
    private static readonly (string SlotKey, int Index)[] Slots =
    {
        ("MainHand", 0), ("OffHand", 1), ("Head", 2), ("Body", 3), ("Hands", 4), ("Legs", 6),
        ("Feet", 7), ("Ears", 8), ("Neck", 9), ("Wrists", 10), ("RFinger", 11), ("LFinger", 12),
    };

    /// <summary>Every equipped slot with its item and dyes. Empty slots are left out.</summary>
    public static unsafe Dictionary<string, SlotState> Read()
    {
        var states = new Dictionary<string, SlotState>(StringComparer.OrdinalIgnoreCase);

        var inventory = InventoryManager.Instance();
        var container = inventory != null ? inventory->GetInventoryContainer(InventoryType.EquippedItems) : null;
        if (container == null) return states;

        foreach (var (slotKey, index) in Slots)
        {
            if (index >= container->Size) continue;

            var item = container->GetInventorySlot(index);
            if (item == null || item->ItemId == 0) continue;

            // The glamour is what shows; % 500000 drops the HQ / collectable offset from the id
            uint shownItemId = (item->GlamourId != 0 ? item->GlamourId : item->ItemId) % 500000;
            states[slotKey] = new SlotState(shownItemId, item->Stains[0], item->Stains[1]);
        }

        return states;
    }
}