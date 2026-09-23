using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.GameContent;

namespace Prosequor.Ability;

/// <summary>
/// Captures idle stopRange before vanilla bakes generation into the field.
/// Player wake is owned by the alert meter; non-player stops use the authored base.
/// </summary>
public static class HusbandryFearAmplifierPatch
{
    static readonly FieldInfo IdleStopRangeField =
        AccessTools.Field(typeof(AiTaskIdle), "stopRange");

    static readonly ConditionalWeakTable<AiTaskIdle, StrongBox<float>> IdleBaseStopRanges = new();

    /// <summary>
    /// Rewrites <c>range * fearFactor</c> (either operand order) to
    /// <c>Adjust(self, fearFactor) * range</c>. Always consumes the <c>ldarg.0</c>
    /// that precedes <c>ldfld range</c> so the stack stays balanced.
    /// </summary>
    public static IEnumerable<CodeInstruction> TranspileFearFactorBeforeRangeMul(
        IEnumerable<CodeInstruction> instructions,
        FieldInfo rangeField,
        MethodInfo adjustHelper,
        string label,
        int wantHits)
    {
        List<CodeInstruction> original = [.. instructions];
        List<CodeInstruction> patched = new(original.Count + 8);
        int hits = 0;

        for (int i = 0; i < original.Count; i++)
        {
            CodeInstruction code = original[i];

            // ldloc*; ldarg.0; ldfld range; mul
            if (i + 3 < original.Count
                && IsLdLoc(code)
                && original[i + 1].opcode == OpCodes.Ldarg_0
                && original[i + 2].opcode == OpCodes.Ldfld
                && Equals(original[i + 2].operand, rangeField)
                && original[i + 3].opcode == OpCodes.Mul)
            {
                hits++;
                EmitAdjustedMul(patched, code, code, rangeField, adjustHelper);
                i += 3;
                continue;
            }

            // ldarg.0; ldfld range; ldloc*; mul
            if (i + 3 < original.Count
                && code.opcode == OpCodes.Ldarg_0
                && original[i + 1].opcode == OpCodes.Ldfld
                && Equals(original[i + 1].operand, rangeField)
                && IsLdLoc(original[i + 2])
                && original[i + 3].opcode == OpCodes.Mul)
            {
                hits++;
                EmitAdjustedMul(patched, code, original[i + 2], rangeField, adjustHelper);
                i += 3;
                continue;
            }

            patched.Add(code);
        }

        if (hits == wantHits)
        {
            return patched;
        }

        throw new InvalidOperationException(
            $"[prosequor] Husbandry fear transpiler failed ({label}). hits={hits} (want {wantHits}).");
    }

    static void EmitAdjustedMul(
        List<CodeInstruction> patched,
        CodeInstruction head,
        CodeInstruction factorLoad,
        FieldInfo rangeField,
        MethodInfo adjustHelper)
    {
        // Adjust(self, factor) * range
        patched.Add(new CodeInstruction(OpCodes.Ldarg_0).WithLabels(head.labels).WithBlocks(head.blocks));
        patched.Add(new CodeInstruction(factorLoad.opcode, factorLoad.operand));
        patched.Add(new CodeInstruction(OpCodes.Call, adjustHelper));
        patched.Add(new CodeInstruction(OpCodes.Ldarg_0));
        patched.Add(new CodeInstruction(OpCodes.Ldfld, rangeField));
        patched.Add(new CodeInstruction(OpCodes.Mul));
    }

    static bool IsLdLoc(CodeInstruction code) =>
        code.opcode == OpCodes.Ldloc
        || code.opcode == OpCodes.Ldloc_S
        || code.opcode == OpCodes.Ldloc_0
        || code.opcode == OpCodes.Ldloc_1
        || code.opcode == OpCodes.Ldloc_2
        || code.opcode == OpCodes.Ldloc_3;

    public static void RememberIdleBaseStopRange(AiTaskIdle self, JsonObject taskConfig)
    {
        if (self == null || taskConfig == null)
        {
            return;
        }

        float configBase = taskConfig["stopRange"].AsFloat(0f);
        IdleBaseStopRanges.GetOrCreateValue(self).Value = configBase;
    }

    public static float GetEffectiveIdleStopRange(AiTaskIdle self)
    {
        if (self == null)
        {
            return 0f;
        }

        if (IdleBaseStopRanges.TryGetValue(self, out StrongBox<float>? box))
        {
            return box.Value;
        }

        return IdleStopRangeField.GetValue(self) is float baked ? baked : 0f;
    }

    public static IEnumerable<CodeInstruction> TranspileIdleStopRangeLoad(
        IEnumerable<CodeInstruction> instructions)
    {
        FieldInfo field = IdleStopRangeField;
        MethodInfo helper = AccessTools.Method(
            typeof(HusbandryFearAmplifierPatch),
            nameof(GetEffectiveIdleStopRange));
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

        if (hits >= 1)
        {
            return patched;
        }

        throw new InvalidOperationException(
            $"[prosequor] Husbandry idle stopRange transpiler failed. hits={hits} (want >=1).");
    }
}

[HarmonyPatch(typeof(AiTaskFleeEntity), nameof(AiTaskFleeEntity.ShouldExecute))]
public static class HusbandryFleeFearPatch
{
    [HarmonyTranspiler]
    [HarmonyPriority(Priority.Low)]
    public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) =>
        HusbandryFearAmplifierPatch.TranspileFearFactorBeforeRangeMul(
            instructions,
            AccessTools.Field(typeof(AiTaskFleeEntity), "seekingRange"),
            AccessTools.Method(
                typeof(PlayerInteractionStation),
                nameof(PlayerInteractionStation.AdjustFleeFearReductionFactor)),
            "flee-fear",
            wantHits: 1);
}

[HarmonyPatch(typeof(AiTaskMeleeAttack), nameof(AiTaskMeleeAttack.ShouldExecute))]
public static class HusbandryMeleeFearPatch
{
    [HarmonyTranspiler]
    public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) =>
        HusbandryFearAmplifierPatch.TranspileFearFactorBeforeRangeMul(
            instructions,
            AccessTools.Field(typeof(AiTaskMeleeAttack), "attackRange"),
            AccessTools.Method(
                typeof(PlayerInteractionStation),
                nameof(PlayerInteractionStation.AdjustMeleeFearReductionFactor)),
            "melee-fear",
            wantHits: 2);
}

[HarmonyPatch(typeof(AiTaskIdle), MethodType.Constructor, typeof(EntityAgent), typeof(JsonObject), typeof(JsonObject))]
public static class HusbandryIdleConfigPatch
{
    /// <summary>
    /// Shipped essentials configure tasks in the ctor (no <c>LoadConfig</c>).
    /// Capture raw <c>stopRange</c> before vanilla bakes generation fear into the field.
    /// </summary>
    [HarmonyPostfix]
    public static void Postfix(AiTaskIdle __instance, JsonObject taskConfig) =>
        HusbandryFearAmplifierPatch.RememberIdleBaseStopRange(__instance, taskConfig);
}

[HarmonyPatch(typeof(AiTaskIdle), "entityInRange")]
public static class HusbandryIdleStopRangePatch
{
    [HarmonyTranspiler]
    public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) =>
        HusbandryFearAmplifierPatch.TranspileIdleStopRangeLoad(instructions);
}
