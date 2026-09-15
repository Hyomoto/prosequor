using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.GameContent;

namespace Prosequor.Ability;

/// <summary>
/// Loose ground meal → chance-gated friendliness credited to vanilla
/// <c>EntityItem.byPlayerUid</c>. Both seek-and-eat tasks use this food source.
/// </summary>
[HarmonyPatch(typeof(LooseItemFoodSource), nameof(LooseItemFoodSource.ConsumeOnePortion))]
public static class LooseItemEatFriendlinessPatch
{
    [HarmonyPostfix]
    public static void Postfix(EntityItem ___entity, Entity entity, float __result)
    {
        if (__result < 1f || entity?.World?.Side != EnumAppSide.Server)
        {
            return;
        }

        AnimalFeedXp.Emit(
            entity.Api,
            entity,
            ___entity?.ByPlayerUid,
            CallerIdentities.Loose,
            EventFactBuilder.CodeOf(___entity?.Itemstack),
            ___entity?.Pos.AsBlockPos);
        LooseItemEatStation.TryGainFriendliness(entity, ___entity?.ByPlayerUid);
    }
}
