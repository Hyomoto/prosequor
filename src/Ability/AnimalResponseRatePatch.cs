using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.GameContent;

namespace Prosequor.Ability;

/// <summary>
/// Replaces flee/seek <c>AiTaskBase.ExecutionChance</c> loads with Inconspicuity-scaled helpers.
/// Helpers must return <see cref="double"/> to match the field / <c>NextDouble</c> compare.
/// </summary>
public static class AnimalResponseRatePatch
{
    /// <summary>Shipped essentials: public double field on <see cref="AiTaskBase"/>.</summary>
    public static readonly FieldInfo ExecutionChanceField =
        AccessTools.Field(typeof(AiTaskBase), nameof(AiTaskBase.ExecutionChance))
        ?? AccessTools.Field(typeof(AiTaskBase), "ExecutionChance")
        ?? throw new InvalidOperationException("[prosequor] AiTaskBase.ExecutionChance field missing.");

    public static IEnumerable<CodeInstruction> TranspileExecutionChanceLoad(
        IEnumerable<CodeInstruction> instructions,
        MethodInfo helper,
        string label)
    {
        FieldInfo field = ExecutionChanceField;
        List<CodeInstruction> original = [.. instructions];
        List<CodeInstruction> patched = new(original.Count);
        int hits = 0;

        for (int i = 0; i < original.Count; i++)
        {
            CodeInstruction code = original[i];
            if (i + 1 < original.Count
                && code.opcode == OpCodes.Ldarg_0
                && original[i + 1].opcode == OpCodes.Ldfld
                && Equals(original[i + 1].operand, field))
            {
                hits++;
                patched.Add(new CodeInstruction(OpCodes.Ldarg_0).WithLabels(code.labels).WithBlocks(code.blocks));
                patched.Add(new CodeInstruction(OpCodes.Call, helper));
                i++;
                continue;
            }

            patched.Add(code);
        }

        if (hits == 1)
        {
            return patched;
        }

        throw new InvalidOperationException(
            $"[prosequor] Animal response transpiler failed ({label}). hits={hits} (want 1).");
    }
}

[HarmonyPatch(typeof(AiTaskFleeEntity), nameof(AiTaskFleeEntity.ShouldExecute))]
public static class AnimalFleeResponseRatePatch
{
    [HarmonyTranspiler]
    public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) =>
        AnimalResponseRatePatch.TranspileExecutionChanceLoad(
            instructions,
            AccessTools.Method(typeof(PlayerInteractionStation), nameof(PlayerInteractionStation.GetScaledFleeExecutionChance)),
            "flee");
}

[HarmonyPatch(typeof(AiTaskSeekEntity), nameof(AiTaskSeekEntity.ShouldExecute))]
public static class AnimalSeekResponseRatePatch
{
    [HarmonyTranspiler]
    public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) =>
        AnimalResponseRatePatch.TranspileExecutionChanceLoad(
            instructions,
            AccessTools.Method(typeof(PlayerInteractionStation), nameof(PlayerInteractionStation.GetScaledSeekExecutionChance)),
            "seek");
}
