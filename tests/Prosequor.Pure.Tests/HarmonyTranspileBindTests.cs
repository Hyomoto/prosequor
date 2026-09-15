using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Prosequor.Ability;
using Vintagestory.API.Common;
using Vintagestory.GameContent;
using Xunit;

namespace Prosequor.Pure.Tests;

/// <summary>
/// Verifies Harmony transpilers actually rewrite the expected IL sites on the
/// shipped VS survival binaries. Missed binds used to log and no-op; they now throw,
/// and these tests catch that before a game boot.
/// </summary>
public class HarmonyTranspileBindTests
{
    [Fact]
    [Trait("Layer", "Harmony")]
    [Trait("Kind", "TranspileBind")]
    public void MeleeFear_Should_RewriteAttackRangeMuls()
    {
        MethodInfo method = AccessTools.DeclaredMethod(
            typeof(AiTaskMeleeAttack),
            nameof(AiTaskMeleeAttack.ShouldExecute));
        Assert.NotNull(method);

        MethodInfo helper = AccessTools.Method(
            typeof(PlayerInteractionStation),
            nameof(PlayerInteractionStation.AdjustMeleeFearReductionFactor));
        FieldInfo range = AccessTools.Field(typeof(AiTaskMeleeAttack), "attackRange");

        List<CodeInstruction> patched =
        [
            .. HusbandryFearAmplifierPatch.TranspileFearFactorBeforeRangeMul(
                PatchProcessor.GetOriginalInstructions(method),
                range,
                helper,
                "melee-fear-test",
                wantHits: 2)
        ];

        AssertContainsCall(patched, helper, expectedCalls: 2);
    }

    [Fact]
    [Trait("Layer", "Harmony")]
    [Trait("Kind", "TranspileBind")]
    public void FleeFear_Should_RewriteSeekingRangeMul()
    {
        MethodInfo method = AccessTools.DeclaredMethod(
            typeof(AiTaskFleeEntity),
            nameof(AiTaskFleeEntity.ShouldExecute));
        Assert.NotNull(method);

        MethodInfo helper = AccessTools.Method(
            typeof(PlayerInteractionStation),
            nameof(PlayerInteractionStation.AdjustFleeFearReductionFactor));
        FieldInfo range = AccessTools.Field(typeof(AiTaskFleeEntity), "seekingRange");

        List<CodeInstruction> patched =
        [
            .. HusbandryFearAmplifierPatch.TranspileFearFactorBeforeRangeMul(
                PatchProcessor.GetOriginalInstructions(method),
                range,
                helper,
                "flee-fear-test",
                wantHits: 1)
        ];

        AssertContainsCall(patched, helper, expectedCalls: 1);
    }

    [Fact]
    [Trait("Layer", "Harmony")]
    [Trait("Kind", "TranspileBind")]
    public void IdleStopRange_Should_RewriteStopRangeLoads()
    {
        MethodInfo method = AccessTools.DeclaredMethod(typeof(AiTaskIdle), "entityInRange");
        Assert.NotNull(method);

        MethodInfo helper = AccessTools.Method(
            typeof(HusbandryFearAmplifierPatch),
            nameof(HusbandryFearAmplifierPatch.GetEffectiveIdleStopRange));

        List<CodeInstruction> patched =
        [
            .. HusbandryFearAmplifierPatch.TranspileIdleStopRangeLoad(
                PatchProcessor.GetOriginalInstructions(method))
        ];

        // entityInRange loads stopRange twice in shipped essentials.
        AssertContainsCall(patched, helper, expectedCalls: 2);
    }

    [Fact]
    [Trait("Layer", "Harmony")]
    [Trait("Kind", "TranspileBind")]
    public void IdleConfig_Should_ResolveCtorWithTaskJson()
    {
        ConstructorInfo? ctor = AccessTools.DeclaredConstructor(
            typeof(AiTaskIdle),
            [typeof(EntityAgent), typeof(Vintagestory.API.Datastructures.JsonObject), typeof(Vintagestory.API.Datastructures.JsonObject)]);
        Assert.NotNull(ctor);
    }

    [Fact]
    [Trait("Layer", "Harmony")]
    [Trait("Kind", "TranspileBind")]
    public void FleeResponseRate_Should_RewriteExecutionChanceLoad()
    {
        MethodInfo method = AccessTools.DeclaredMethod(
            typeof(AiTaskFleeEntity),
            nameof(AiTaskFleeEntity.ShouldExecute));
        Assert.NotNull(method);

        MethodInfo helper = AccessTools.Method(
            typeof(PlayerInteractionStation),
            nameof(PlayerInteractionStation.GetScaledFleeExecutionChance));
        Assert.Equal(typeof(double), helper!.ReturnType);

        List<CodeInstruction> patched =
        [
            .. AnimalResponseRatePatch.TranspileExecutionChanceLoad(
                PatchProcessor.GetOriginalInstructions(method),
                helper,
                "flee-response-test")
        ];

        AssertContainsCall(patched, helper, expectedCalls: 1);
    }

    [Fact]
    [Trait("Layer", "Harmony")]
    [Trait("Kind", "TranspileBind")]
    public void SeekResponseRate_Should_RewriteExecutionChanceLoad()
    {
        MethodInfo method = AccessTools.DeclaredMethod(
            typeof(AiTaskSeekEntity),
            nameof(AiTaskSeekEntity.ShouldExecute));
        Assert.NotNull(method);

        MethodInfo helper = AccessTools.Method(
            typeof(PlayerInteractionStation),
            nameof(PlayerInteractionStation.GetScaledSeekExecutionChance));
        Assert.Equal(typeof(double), helper!.ReturnType);

        List<CodeInstruction> patched =
        [
            .. AnimalResponseRatePatch.TranspileExecutionChanceLoad(
                PatchProcessor.GetOriginalInstructions(method),
                helper,
                "seek-response-test")
        ];

        AssertContainsCall(patched, helper, expectedCalls: 1);
    }

    [Fact]
    [Trait("Layer", "Harmony")]
    [Trait("Kind", "TranspileBind")]
    public void TemporalStability_Should_RewriteRecoverAndDrainDivisors()
    {
        MethodInfo method = AccessTools.DeclaredMethod(
            typeof(EntityBehaviorTemporalStabilityAffected),
            nameof(EntityBehaviorTemporalStabilityAffected.OnGameTick));
        Assert.NotNull(method);

        MethodInfo recover = AccessTools.Method(
            typeof(PlayerInteractionStation),
            nameof(PlayerInteractionStation.GetRecoveryDivisor));
        MethodInfo drain = AccessTools.Method(
            typeof(PlayerInteractionStation),
            nameof(PlayerInteractionStation.GetDrainDivisor));

        List<CodeInstruction> patched =
        [
            .. TemporalStabilityRatePatch.Transpiler(PatchProcessor.GetOriginalInstructions(method))
        ];

        AssertContainsCall(patched, recover, expectedCalls: 1);
        AssertContainsCall(patched, drain, expectedCalls: 1);
    }

    [Fact]
    [Trait("Layer", "Harmony")]
    [Trait("Kind", "TranspileBind")]
    public void CropClimateWindow_Should_RewriteColdAndHeatThresholdLoads()
    {
        MethodInfo method = AccessTools.DeclaredMethod(
            typeof(BlockEntityFarmland),
            "updateCropDamage");
        Assert.NotNull(method);

        MethodInfo adjustCold = AccessTools.Method(
            typeof(CropClimateWindow),
            nameof(CropClimateWindow.AdjustColdThreshold));
        MethodInfo adjustHeat = AccessTools.Method(
            typeof(CropClimateWindow),
            nameof(CropClimateWindow.AdjustHeatThreshold));

        List<CodeInstruction> patched =
        [
            .. CropClimateWindowPatches.FarmlandUpdateCropDamageClimateWindowPatch
                .TranspileThresholdLoads(PatchProcessor.GetOriginalInstructions(method))
        ];

        AssertContainsCall(patched, adjustCold, expectedCalls: 1);
        AssertContainsCall(patched, adjustHeat, expectedCalls: 1);
    }

    [Fact]
    [Trait("Layer", "Harmony")]
    [Trait("Kind", "TranspileBind")]
    public void SkepBeeSpawn_Should_RewriteBeemobSpawnChanceLoad()
    {
        MethodInfo method = AccessTools.DeclaredMethod(
            typeof(BlockSkep),
            nameof(BlockSkep.OnBlockBroken));
        Assert.NotNull(method);

        MethodInfo helper = AccessTools.Method(
            typeof(SkepBeeSpawnStation),
            nameof(SkepBeeSpawnStation.ResolveSpawnChanceForBreak));
        Assert.NotNull(helper);

        List<CodeInstruction> original = [.. PatchProcessor.GetOriginalInstructions(method)];
        Assert.Contains(
            original,
            code => code.opcode == OpCodes.Ldfld
                && Equals(code.operand, SkepBeeSpawnPatch.BeemobSpawnChanceField));

        List<CodeInstruction> patched =
        [
            .. SkepBeeSpawnPatch.TranspileBeemobSpawnChanceLoad(original, helper!)
        ];

        AssertContainsCall(patched, helper!, expectedCalls: 1);
        Assert.DoesNotContain(
            patched,
            code => code.opcode == OpCodes.Ldfld
                && Equals(code.operand, SkepBeeSpawnPatch.BeemobSpawnChanceField));
    }

    static void AssertContainsCall(List<CodeInstruction> instructions, MethodInfo helper, int expectedCalls)
    {
        int calls = 0;
        foreach (CodeInstruction code in instructions)
        {
            if (code.opcode == OpCodes.Call && Equals(code.operand, helper))
            {
                calls++;
            }
        }

        Assert.Equal(expectedCalls, calls);
    }
}
