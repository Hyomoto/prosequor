using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace Prosequor.Ability;

/// <summary>Harmony adapters for boat motion and ratline endurance via <c>prosequor:mounted</c>.</summary>
[HarmonyPatch]
public static class BoatingAbilityPatches
{
    static double ratlineTickSavedTotalHours;

    [HarmonyPostfix]
    [HarmonyPatch(typeof(EntityBoat), nameof(EntityBoat.DidMount))]
    public static void DidMountPostfix(EntityBoat __instance, EntityAgent entityAgent) =>
        BoatingStation.RememberMount(__instance, entityAgent);

    [HarmonyPostfix]
    [HarmonyPatch(typeof(EntityBoat), nameof(EntityBoat.DidUnmount))]
    public static void DidUnmountPostfix(EntityBoat __instance, EntityAgent entityAgent) =>
        BoatingStation.RememberUnmount(__instance, entityAgent);

    [HarmonyPostfix]
    [HarmonyPatch(typeof(EntityBoat), "SeatsToMotion")]
    public static void SeatsToMotionPostfix(EntityBoat __instance, ref Vec2d __result)
    {
        if (!BoatingStation.TryGetCaptain(__instance, out EntityAgent? captain)
            || captain is not EntityPlayer entityPlayer
            || entityPlayer.Player == null)
        {
            return;
        }

        float forward = BoatingStation.ResolveForwardSpeed(entityPlayer.Player, __instance);
        float turn = BoatingStation.ResolveTurnSpeed(entityPlayer.Player, __instance);
        __result.X *= forward;
        __result.Y *= turn;
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(ModSystemBoatingSoundAndRatlineStamina), "onTickServer")]
    public static void RatlineStaminaPrefix(ModSystemBoatingSoundAndRatlineStamina __instance)
    {
        ICoreAPI? api = (ICoreAPI?)AccessTools.Field(typeof(ModSystemBoatingSoundAndRatlineStamina), "api")
            ?.GetValue(__instance);
        ratlineTickSavedTotalHours = api?.World?.Calendar?.TotalHours ?? 0;
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(ModSystemBoatingSoundAndRatlineStamina), "onTickServer")]
    public static void RatlineStaminaPostfix(ModSystemBoatingSoundAndRatlineStamina __instance)
    {
        ICoreAPI? api = (ICoreAPI?)AccessTools.Field(typeof(ModSystemBoatingSoundAndRatlineStamina), "api")
            ?.GetValue(__instance);
        if (api?.World?.Calendar == null)
        {
            return;
        }

        object? playersObj = AccessTools.Field(typeof(ModSystemBoatingSoundAndRatlineStamina), "playersOnRatlines")
            ?.GetValue(__instance);
        if (playersObj is not Dictionary<string, EntityPlayer> playersOnRatlines)
        {
            return;
        }

        float elapsed = (float)(api.World.Calendar.TotalHours - ratlineTickSavedTotalHours);
        if (elapsed < 0.001f)
        {
            return;
        }

        foreach (EntityPlayer player in playersOnRatlines.Values)
        {
            if (player?.Player == null)
            {
                continue;
            }

            float reduction = BoatingStation.ResolveRatlineDrainReduction(player.Player);
            if (reduction <= 0f)
            {
                continue;
            }

            TreeAttribute attrs = (TreeAttribute)player.WatchedAttributes;
            if (!attrs.HasAttribute("remainingMountedStrengthHours"))
            {
                continue;
            }

            float remaining = attrs.GetFloat("remainingMountedStrengthHours", 0f);
            float refund = elapsed * reduction;
            attrs.SetFloat("remainingMountedStrengthHours", Math.Min(2f, remaining + refund));
        }
    }
}
