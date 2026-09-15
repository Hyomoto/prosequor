using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.Common;
using Prosequor.Inventory;

namespace Prosequor.Ability;

/// <summary>
/// Fallback give into <c>prosequorcarry</c> when hotbar/backpack cannot take the stack.
/// Vanilla <see cref="PlayerInventoryManager.TryGiveItemstack"/> already walks opened
/// <see cref="InventoryBasePlayer"/> inventories, so this is a safety net for leftovers.
/// </summary>
[HarmonyPatch(typeof(PlayerInventoryManager), nameof(PlayerInventoryManager.TryGiveItemstack))]
public static class CarryInventoryGivePatch
{
    public static void Postfix(
        PlayerInventoryManager __instance,
        ItemStack itemstack,
        bool slotNotifyEffect,
        ref bool __result)
    {
        if (itemstack == null || itemstack.StackSize <= 0)
        {
            return;
        }

        IPlayer? player = __instance.player;
        if (player?.Entity == null)
        {
            return;
        }

        if (player.InventoryManager.GetOwnInventory(ProsequorCarryInventory.InventoryClassName)
            is not ProsequorCarryInventory carry
            || carry.Count <= 0)
        {
            return;
        }

        ItemSlot source = new DummySlot(itemstack);
        ItemStackMoveOperation op = new(
            player.Entity.World,
            EnumMouseButton.Left,
            (EnumModifierKey)0,
            EnumMergePriority.AutoMerge,
            itemstack.StackSize);

        bool placedAny = false;
        for (int i = 0; i < carry.Count && source.StackSize > 0; i++)
        {
            ItemSlot target = carry[i];
            int before = source.StackSize;
            source.TryPutInto(target, ref op);
            if (source.StackSize != before)
            {
                placedAny = true;
                if (slotNotifyEffect)
                {
                    target.MarkDirty();
                    player.InventoryManager.NotifySlot(player, target);
                }
            }

            op.RequestedQuantity = source.StackSize;
        }

        if (source.Itemstack == null || source.StackSize <= 0)
        {
            itemstack.StackSize = 0;
            __result = true;
        }
        else if (placedAny)
        {
            itemstack.StackSize = source.StackSize;
            __result = true;
        }
    }
}
