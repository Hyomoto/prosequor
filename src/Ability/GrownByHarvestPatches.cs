using HarmonyLib;
using Prosequor.Xp.Adapters;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace Prosequor.Ability;

/// <summary>
/// Harvest drops from planted crops / berry bushes inherit the planter as
/// <c>Grown By</c> (not the harvester). Break-path GetDrops also notes units for harvest XP.
/// </summary>
public static class GrownByHarvestPatches
{
    [HarmonyPatch(typeof(Block), nameof(Block.GetDrops))]
    public static class BlockGetDropsGrownByPatch
    {
        [HarmonyPostfix]
        [HarmonyPriority(Priority.Last)]
        public static void Postfix(
            Block __instance,
            IWorldAccessor world,
            BlockPos pos,
            ref ItemStack[] __result)
        {
            if (world?.Side != EnumAppSide.Server)
            {
                return;
            }

            OwnerCredit.StampGrownByDrops(world, __instance, pos, __result);
            HarvestXp.NoteBreakDrops(__instance, pos, __result);
        }
    }
}
