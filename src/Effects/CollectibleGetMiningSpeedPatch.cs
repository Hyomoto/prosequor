using HarmonyLib;
using Prosequor.Ability;
using Vintagestory.API.Common;

namespace Prosequor.Effects;

/// <summary>
/// Thin mining-speed egress into interaction-speed. Vanilla miningSpeedMul only scales Ore/Stone.
/// </summary>
[HarmonyPatch(typeof(CollectibleObject), nameof(CollectibleObject.GetMiningSpeed))]
public static class CollectibleGetMiningSpeedPatch
{
    [HarmonyPostfix]
    public static void Postfix(
        ref float __result,
        IItemStack itemstack,
        BlockSelection blockSel,
        Block block,
        IPlayer forPlayer)
    {
        if (forPlayer?.Entity == null || block == null)
        {
            return;
        }

        EnumBlockMaterial material = block.GetBlockMaterial(
            forPlayer.Entity.World.BlockAccessor,
            blockSel?.Position);

        __result = InteractionSpeedStation.Run(
            forPlayer,
            EventFactBuilder.CodeOf(block),
            EventFactBuilder.CodeOf(itemstack?.Collectible),
            __result,
            material);
    }
}
