using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Prosequor.Xp.Adapters;
using Vintagestory.API.Common;
using Vintagestory.GameContent;

namespace Prosequor.Ability;

/// <summary>
/// Stamps absorb multiplier when fertilizer is applied; scales slow-release drain ticks
/// and emits absorb XP when the pool actually drops.
/// </summary>
[HarmonyPatch(typeof(BlockEntitySoilNutrition), nameof(BlockEntitySoilNutrition.OnBlockInteract))]
public static class SoilNutritionFertilizeAbsorbStampPatch
{
    [HarmonyPrefix]
    public static void Prefix(BlockEntitySoilNutrition __instance, out float __state)
    {
        __state = FertilizerAbsorbRate.SumSlowRelease(__instance);
    }

    [HarmonyPostfix]
    public static void Postfix(
        BlockEntitySoilNutrition __instance,
        IPlayer byPlayer,
        bool __result,
        float __state)
    {
        if (!__result
            || byPlayer == null
            || __instance.Api?.World?.Side != EnumAppSide.Server)
        {
            return;
        }

        float after = FertilizerAbsorbRate.SumSlowRelease(__instance);
        if (after <= __state + 0.0001f)
        {
            return;
        }

        FertilizerAbsorbRate.TryStampOnFertilize(__instance.Api.World, byPlayer, __instance);
        ProsequorBlockPedigreeStation.TryAddCareCredit(
            __instance,
            byPlayer.PlayerUID,
            FarmlandCareKind.Fertilize);
    }
}

[HarmonyPatch(typeof(BlockEntitySoilNutrition), "updateSoilFertility")]
public static class SoilNutritionSlowReleaseAbsorbPatch
{
    [HarmonyPrefix]
    public static void Prefix(BlockEntitySoilNutrition __instance, out float __state)
    {
        __state = FertilizerAbsorbRate.SumSlowRelease(__instance);
    }

    [HarmonyPostfix]
    public static void Postfix(BlockEntitySoilNutrition __instance, float __state)
    {
        float transferred = __state - FertilizerAbsorbRate.SumSlowRelease(__instance);
        FertilizerAbsorbXp.OnAbsorbed(__instance, transferred);
    }

    [HarmonyTranspiler]
    public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        MethodInfo step = AccessTools.Method(
            typeof(FertilizerAbsorbRate),
            nameof(FertilizerAbsorbRate.GetSlowReleaseStep));

        foreach (CodeInstruction code in instructions)
        {
            if (code.opcode == OpCodes.Ldc_R4 && code.operand is float value && value == 0.25f)
            {
                yield return new CodeInstruction(OpCodes.Ldarg_0);
                yield return new CodeInstruction(OpCodes.Call, step);
                continue;
            }

            yield return code;
        }
    }
}
