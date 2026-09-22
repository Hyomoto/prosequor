using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace Prosequor.Ability;

/// <summary>
/// Folds Construction reinforcement strength before vanilla StrengthenBlock stores it.
/// </summary>
[HarmonyPatch(typeof(ModSystemBlockReinforcement), nameof(ModSystemBlockReinforcement.StrengthenBlock))]
public static class ReinforceAbilityPatches
{
    [HarmonyPrefix]
    public static void Prefix(BlockPos pos, IPlayer byPlayer, ref int strength, ref bool __state)
    {
        __state = false;
        if (byPlayer == null || strength <= 0)
        {
            return;
        }

        int next = ReinforceStation.ResolveStrength(byPlayer, strength);
        if (next != strength)
        {
            strength = next;
        }

        // Emit only when StrengthenBlock succeeds (Postfix checks __result).
        __state = true;
    }

    [HarmonyPostfix]
    public static void Postfix(BlockPos pos, IPlayer byPlayer, bool __result, bool __state)
    {
        if (!__state || !__result || byPlayer == null)
        {
            return;
        }

        ReinforceStation.EmitReinforced(byPlayer, pos);
    }
}
