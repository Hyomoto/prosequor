using HarmonyLib;
using Prosequor.Xp.Adapters;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace Prosequor.Ability;

/// <summary>
/// Player place stamps forage blocks so world-spawned copies stay unmarked.
/// Reed harvests go through <see cref="BlockReeds.OnBlockBroken"/> (does not
/// always reach <see cref="Block.OnBlockBroken"/>).
/// </summary>
[HarmonyPatch(typeof(Block), nameof(Block.DoPlaceBlock))]
public static class ForageDoPlaceBlockPatch
{
    [HarmonyPostfix]
    public static void Postfix(
        Block __instance,
        bool __result,
        IWorldAccessor world,
        IPlayer byPlayer,
        BlockSelection blockSel)
    {
        if (!__result
            || world?.Side != EnumAppSide.Server
            || byPlayer == null
            || blockSel?.Position == null
            || !ForageBlocks.IsPlayerPlaceableForage(__instance))
        {
            return;
        }

        ForagePlayerPlaced.Mark(world, blockSel.Position);
    }
}

[HarmonyPatch(typeof(Block), nameof(Block.OnBlockBroken))]
public static class ForageBlockBrokenMarkPatch
{
    [HarmonyPostfix]
    public static void Postfix(IWorldAccessor world, BlockPos pos) =>
        ForagePlayerPlaced.ClearIfGone(world, pos);
}

[HarmonyPatch(typeof(BlockReeds), nameof(BlockReeds.OnBlockBroken))]
public static class ForageReedBrokenPatch
{
    [HarmonyPostfix]
    public static void Postfix(
        IWorldAccessor world,
        BlockPos pos,
        IPlayer byPlayer,
        BlockReeds __instance)
    {
        if (world?.Side != EnumAppSide.Server || byPlayer == null || __instance == null)
        {
            return;
        }

        ProsequorModSystem.For(world.Api)?.NotifyBlockBrokenXp(byPlayer, __instance, pos);
        ForagePlayerPlaced.ClearIfGone(world, pos);
    }
}

/// <summary>
/// Loose sticks (and any other forage with RightClickPickup) give items via
/// <c>SetBlock(0)</c> and never reach <see cref="Block.OnBlockBroken"/>.
/// </summary>
[HarmonyPatch(typeof(BlockBehaviorRightClickPickup), nameof(BlockBehaviorRightClickPickup.OnBlockInteractStart))]
public static class ForageRightClickPickupXpPatch
{
    [HarmonyPostfix]
    public static void Postfix(
        BlockBehaviorRightClickPickup __instance,
        IWorldAccessor world,
        IPlayer byPlayer,
        BlockSelection blockSel,
        EnumHandling handling)
    {
        if (handling == EnumHandling.PassThrough
            || world?.Side != EnumAppSide.Server
            || byPlayer == null
            || blockSel?.Position == null
            || __instance?.block == null)
        {
            return;
        }

        Block block = __instance.block;
        if (!ForageBlocks.IsMushroom(block) && !ForageBlocks.IsFlatForage(block))
        {
            return;
        }

        if (!ForagePlayerPlaced.IsWild(world, block, blockSel.Position))
        {
            ForagePlayerPlaced.ClearIfGone(world, blockSel.Position);
            return;
        }

        ProsequorModSystem.For(world.Api)?.NotifyBlockBrokenXp(byPlayer, block, blockSel.Position);
        ForagePlayerPlaced.ClearIfGone(world, blockSel.Position);
    }
}
