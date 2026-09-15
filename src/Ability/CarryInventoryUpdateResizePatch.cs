using System.Reflection;
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.Common;
using Prosequor.Inventory;

namespace Prosequor.Ability;

/// <summary>
/// <see cref="InventoryNetworkUtil.UpdateFromPacket(IWorldAccessor, Packet_InventoryUpdate)"/>
/// checks <c>slotId &gt;= Count</c> before indexing. Grow carry capacity first so a
/// server update that races ahead of client <c>ApplyBasicSlots</c> does not crash.
/// </summary>
[HarmonyPatch(typeof(InventoryNetworkUtil), nameof(InventoryNetworkUtil.UpdateFromPacket))]
[HarmonyPatch([typeof(IWorldAccessor), typeof(Packet_InventoryUpdate)])]
public static class CarryInventoryUpdateResizePatch
{
    static readonly FieldInfo? InvField = AccessTools.Field(typeof(InventoryNetworkUtil), "inv");

    public static void Prefix(InventoryNetworkUtil __instance, Packet_InventoryUpdate packet)
    {
        if (packet == null || packet.SlotId < 0 || InvField == null)
        {
            return;
        }

        if (InvField.GetValue(__instance) is ProsequorCarryInventory carry)
        {
            carry.EnsureSlotCapacity(packet.SlotId + 1);
        }
    }
}
