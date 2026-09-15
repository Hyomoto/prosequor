using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace Prosequor.Xp.Adapters;

/// <summary>
/// Per-block XP seam. Tree fells call OnBlockBroken for every log; DidBreakBlock does not.
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
