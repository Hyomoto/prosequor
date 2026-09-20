using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace Prosequor.Xp.Adapters;

/// <summary>
/// Per-block XP seam. Tree fells call OnBlockBroken for every log; DidBreakBlock does not.
/// Ore deposits override <see cref="Block.OnBlockBroken"/> and do not always reach this
/// base postfix (same class of miss as <c>BlockReeds</c>).
/// </summary>
[HarmonyPatch(typeof(Block), nameof(Block.OnBlockBroken))]
public static class BlockOnBlockBrokenXpPatch
{
    [HarmonyPostfix]
    public static void Postfix(Block __instance, IWorldAccessor world, BlockPos pos, IPlayer byPlayer)
    {
        if (world?.Side != EnumAppSide.Server || byPlayer == null || __instance == null)
        {
            return;
        }

        ProsequorModSystem.For(world.Api)?.NotifyBlockBrokenXp(byPlayer, __instance, pos);
    }
}

[HarmonyPatch(typeof(BlockOre), nameof(BlockOre.OnBlockBroken))]
public static class BlockOreOnBlockBrokenXpPatch
{
    [HarmonyPostfix]
    public static void Postfix(
        BlockOre __instance,
        IWorldAccessor world,
        BlockPos pos,
        IPlayer byPlayer) =>
        BlockOnBlockBrokenXpPatch.Postfix(__instance, world, pos, byPlayer);
}
