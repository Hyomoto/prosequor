using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.GameContent;

namespace Prosequor.Ability;

/// <summary>Harmony adapters for <c>prosequor:mounted</c> rideable behaviors.</summary>
[HarmonyPatch]
public static class RidingAbilityPatches
{
    [HarmonyPostfix]
    [HarmonyPatch(typeof(EntityBehaviorRideable), nameof(EntityBehaviorRideable.DidMount))]
    public static void DidMountPostfix(EntityBehaviorRideable __instance, EntityAgent entityAgent) =>
        MountedStation.RememberMount(__instance, entityAgent);

    [HarmonyPostfix]
    [HarmonyPatch(typeof(EntityBehaviorRideable), nameof(EntityBehaviorRideable.DidUnmount))]
    public static void DidUnmountPostfix(EntityBehaviorRideable __instance, EntityAgent entityAgent) =>
        MountedStation.RememberUnmount(__instance, entityAgent);

    [HarmonyPrefix]
    [HarmonyPatch(typeof(EntityBehaviorGait), "Move")]
    public static void GaitMovePrefix(EntityBehaviorGait __instance)
    {
        Entity entity = __instance.entity;
        if (entity == null)
        {
            return;
        }

        EntityBehaviorRideable? rideable = entity.GetBehavior<EntityBehaviorRideable>();
        if (rideable == null)
        {
            __instance.MoveSpeedModifier = 1.0;
            return;
        }

        if (!MountedStation.TryGetDriver(rideable, out EntityAgent? driver)
            || driver is not EntityPlayer entityPlayer
            || entityPlayer.Player == null)
        {
            __instance.MoveSpeedModifier = 1.0;
            return;
        }

        __instance.MoveSpeedModifier = MountedStation.ResolveMoveSpeed(entityPlayer.Player, entity);
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(EntityBehaviorRideableAccessories), "EntityBehaviorDressable_CanRide")]
    public static void CanRidePostfix(
        EntityBehaviorRideableAccessories __instance,
        IMountableSeat seat,
        ref string errorMessage,
        ref bool __result)
    {
        if (__result)
        {
            return;
        }

        if (seat?.Passenger is not EntityPlayer entityPlayer || entityPlayer.Player == null)
        {
            return;
        }

        Entity? mount = __instance.entity;
        if (!MountedStation.ResolveCanRide(entityPlayer.Player, mount))
        {
            return;
        }

        __result = true;
        errorMessage = null!;
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(EntityBehaviorRideable), "DoSaddleBreak")]
    public static bool DoSaddleBreakPrefix(EntityBehaviorRideable __instance, out int __state)
    {
        __state = __instance.RemainingSaddleBreaks;

        if (!MountedStation.TryGetDriver(__instance, out EntityAgent? driver)
            || driver is not EntityPlayer entityPlayer
            || entityPlayer.Player == null)
        {
            return true;
        }

        float chance = MountedStation.ResolveSaddleBreakChance(entityPlayer.Player, __instance.entity);
        if (chance <= 0f)
        {
            return true;
        }

        Entity? entity = __instance.entity;
        if (entity?.Api?.World == null)
        {
            return true;
        }

        Random random = entity.World?.Rand ?? new Random();
        if (random.NextDouble() >= chance)
        {
            return true;
        }

        // Stay mounted: refresh tame progress without applying the break animation (XSkills pattern).
        AccessTools.Field(typeof(EntityBehaviorRideable), "mountedTotalMs")
            ?.SetValue(__instance, entity.Api.World.ElapsedMilliseconds);
        AccessTools.Field(typeof(EntityBehaviorRideable), "jumpNow")?.SetValue(__instance, false);
        object? ebg = AccessTools.Field(typeof(EntityBehaviorRideable), "ebg")?.GetValue(__instance);
        if (ebg != null)
        {
            AccessTools.Method(ebg.GetType(), "SetIdle")?.Invoke(ebg, null);
        }

        object? intervalObj = AccessTools.Field(typeof(EntityBehaviorRideable), "saddleBreakDayInterval")
            ?.GetValue(__instance);
        float interval = intervalObj is float f ? f : 0f;
        double totalDays = entity.Api.World.Calendar.TotalDays;
        if (totalDays - __instance.LastSaddleBreakTotalDays > interval)
        {
            __instance.RemainingSaddleBreaks--;
            __instance.LastSaddleBreakTotalDays = totalDays;
            if (__instance.RemainingSaddleBreaks <= 0)
            {
                AccessTools.Method(typeof(EntityBehaviorRideable), "ConvertToTamedAnimal")
                    ?.Invoke(__instance, null);
            }
        }

        return false;
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(EntityBehaviorRideable), "DoSaddleBreak")]
    public static void DoSaddleBreakPostfix(EntityBehaviorRideable __instance, int __state) =>
        SaddleBreakXp.AwardBreakIfProgressed(__instance, __state);

    [HarmonyPrefix]
    [HarmonyPatch(typeof(EntityBehaviorRideable), "ConvertToTamedAnimal")]
    public static void ConvertToTamedAnimalPrefix(EntityBehaviorRideable __instance) =>
        SaddleBreakXp.AwardTame(__instance);
}
