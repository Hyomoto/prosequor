using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.GameContent;

namespace Prosequor.Ability;

/// <summary>
/// Bloomery harvest XP: finished bloom taken by breaking the bloomery or Bloom Brigand extract.
/// Flat deed — pays once per successful harvest.
/// </summary>
public static class BloomeryHarvestXp
{
    static readonly AccessTools.FieldRef<BlockEntityBloomery, InventoryGeneric>? InvField =
        AccessTools.FieldRefAccess<BlockEntityBloomery, InventoryGeneric>("bloomeryInv");

    const int OutSlotIndex = 2;

    public static ItemSlot? TryGetOutSlot(BlockEntityBloomery? be)
    {
        if (be == null || InvField == null)
        {
            return null;
        }

        InventoryGeneric? inv = InvField(be);
        if (inv == null || inv.Count <= OutSlotIndex)
        {
            return null;
        }

        return inv[OutSlotIndex];
    }

    public static ItemStack? TryGetOutStack(BlockEntityBloomery? be) =>
        TryGetOutSlot(be)?.Itemstack;

    /// <summary>True when the bloomery has finished product ready to take.</summary>
    public static bool HasFinishedBloom(BlockEntityBloomery? be)
    {
        ItemStack? outStack = TryGetOutStack(be);
        return be != null
            && outStack != null
            && outStack.StackSize > 0
            && !be.IsBurning;
    }

    /// <summary>
    /// Pay metalworking for a successful bloom take. <paramref name="bloom"/> is the
    /// extracted product; missing player / bloom → no-op.
    /// </summary>
    public static void Settle(IPlayer? byPlayer, ItemStack? bloom, Block? bloomeryBlock)
    {
        if (byPlayer?.PlayerUID == null
            || byPlayer.Entity?.World?.Side != EnumAppSide.Server
            || bloom?.Collectible == null)
        {
            return;
        }

        Prosequor.Xp.Activity.Deed.Emit(
            byPlayer.Entity.World.Api,
            byPlayer.PlayerUID,
            Prosequor.Xp.Activity.DeedToken.BloomeryHarvest,
            caller: CallerIdentities.Hand,
            target: EventFactBuilder.CodeOf(bloomeryBlock)
                ?? EventFactBuilder.CodeOf(bloom));
    }
}
