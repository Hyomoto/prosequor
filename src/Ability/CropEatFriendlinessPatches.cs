using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.GameContent;

namespace Prosequor.Ability;

/// <summary>
/// Farmland crop pilfer → chance-gated friendliness. Berry bushes are out of scope.
/// </summary>
[HarmonyPatch(typeof(BlockEntityFarmland), nameof(BlockEntityFarmland.ConsumeOnePortion))]
public static class CropEatFriendlinessPatch
{
    [HarmonyPrefix]
    public static void Prefix(BlockEntityFarmland __instance, out string? __state) =>
        __state = EventFactBuilder.CodeOf(__instance.GetCrop());

    [HarmonyPostfix]
    public static void Postfix(
        BlockEntityFarmland __instance,
        Entity entity,
        float __result,
        string? __state)
    {
        if (__result < 1f
            || entity == null
            || __instance.Api?.World?.Side != EnumAppSide.Server)
        {
            return;
        }

        ProsequorBlockPedigreeStation.TryGetPlanter(__instance, out string? planterUid);
        AnimalFeedXp.Emit(
            __instance.Api,
            entity,
            planterUid,
            CallerIdentities.Crop,
            __state,
            __instance.Pos);
        CropEatStation.TryGainFriendliness(entity, planterUid);
    }
}
