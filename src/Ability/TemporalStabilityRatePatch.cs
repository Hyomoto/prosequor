using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Vintagestory.GameContent;

namespace Prosequor.Ability;

/// <summary>
/// Replaces vanilla temporal gain divisors (200 recover / 800 drain) with Adaptation-scaled helpers.
/// </summary>
[HarmonyPatch(typeof(EntityBehaviorTemporalStabilityAffected), nameof(EntityBehaviorTemporalStabilityAffected.OnGameTick))]
public static class TemporalStabilityRatePatch
{
    [HarmonyTranspiler]
    public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        MethodInfo recover = AccessTools.Method(
            typeof(PlayerInteractionStation),
            nameof(PlayerInteractionStation.GetRecoveryDivisor));
        MethodInfo drain = AccessTools.Method(
            typeof(PlayerInteractionStation),
            nameof(PlayerInteractionStation.GetDrainDivisor));

        List<CodeInstruction> original = [.. instructions];
        List<CodeInstruction> patched = new(original.Count);
        int recoverHits = 0;
        int drainHits = 0;

        foreach (CodeInstruction code in original)
        {
            if (code.opcode == OpCodes.Ldc_R8 && code.operand is double value)
            {
                if (value == PlayerInteractionStation.VanillaTemporalRecoverDivisor)
                {
                    recoverHits++;
                    patched.Add(new CodeInstruction(OpCodes.Ldarg_0).WithLabels(code.labels).WithBlocks(code.blocks));
                    patched.Add(new CodeInstruction(OpCodes.Call, recover));
                    continue;
                }

                if (value == PlayerInteractionStation.VanillaTemporalDrainDivisor)
                {
                    drainHits++;
                    patched.Add(new CodeInstruction(OpCodes.Ldarg_0).WithLabels(code.labels).WithBlocks(code.blocks));
                    patched.Add(new CodeInstruction(OpCodes.Call, drain));
                    continue;
                }
            }

            patched.Add(code);
        }

        if (recoverHits == 1 && drainHits == 1)
        {
            return patched;
        }

        throw new InvalidOperationException(
            $"[prosequor] Temporal stability transpiler failed. recoverHits={recoverHits} drainHits={drainHits} (want 1/1).");
    }
}
