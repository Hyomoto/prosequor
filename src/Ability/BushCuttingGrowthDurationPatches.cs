using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.GameContent;

namespace Prosequor.Ability;

/// <summary>
/// Place stamps growth-time multiplier and scales remaining mature days for bush cuttings.
/// </summary>
[HarmonyPatch(typeof(Block), nameof(Block.DoPlaceBlock))]
public static class BlockDoPlaceBlockBushCuttingGrowthPatch
{
    [HarmonyPostfix]
    public static void Postfix(
        bool __result,
        IWorldAccessor world,
        IPlayer byPlayer,
        BlockSelection blockSel)
    {
        if (!__result
            || world?.Side != EnumAppSide.Server
            || byPlayer == null
            || blockSel?.Position == null)
        {
            return;
        }

        BlockEntity? be = world.BlockAccessor.GetBlockEntity(blockSel.Position);
        BEBehaviorFruitingBushCutting? cutting = be?.GetBehavior<BEBehaviorFruitingBushCutting>();
        if (be == null || cutting == null)
        {
            return;
        }

        BushCuttingGrowthDuration.TryStampOnPlace(world, byPlayer, be, cutting);
    }
}


