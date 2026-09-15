using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;

namespace Prosequor.Ability;

/// <summary>On-foot sprint / swim / sneak speed from passive skill folds.</summary>
[HarmonyPatch]
public static class LocomotionAbilityPatches
{
    [HarmonyPostfix]
    [HarmonyPatch(typeof(EntityAgent), nameof(EntityAgent.GetWalkSpeedMultiplier))]
    public static void GetWalkSpeedMultiplierPostfix(EntityAgent __instance, ref double __result) =>
        PlayerInteractionStation.ApplyWalkSpeedBonus(__instance, ref __result);
}

/// <summary>Which locomotion bonus a walk-speed sample should use.</summary>
public enum FootLocomotion
{
    None,
    Sprint,
    Swim,
    Sneak
}
