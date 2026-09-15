using HarmonyLib;
using Vintagestory.GameContent;

namespace Prosequor.Ability;

/// <summary>
/// Live success-chance transform on fish bobber-seek rates.
/// Baited and baitless properties share the pipeline; only baited stamps <c>used-bait</c>.
/// </summary>
[HarmonyPatch(typeof(EntityFish))]
public static class FishSeekBobberSuccessPatches
{
    [HarmonyPostfix]
    [HarmonyPatch("get_BaitBobberSeekChance")]
    public static void BaitBobberSeekChancePostfix(EntityFish __instance, ref double __result)
    {
        FishSeekBobberSuccess.ApplySeekChance(__instance, ref __result, usedBait: true);
    }

    [HarmonyPostfix]
    [HarmonyPatch("get_NoBaitBobberSeekChance")]
    public static void NoBaitBobberSeekChancePostfix(EntityFish __instance, ref double __result)
    {
        FishSeekBobberSuccess.ApplySeekChance(__instance, ref __result, usedBait: false);
    }
}
