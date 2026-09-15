using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.GameContent;

namespace Prosequor.Ability;

/// <summary>
/// Plant stamps climate half-delta; <c>updateCropDamage</c> threshold loads expand by stamp.
/// </summary>
public static class CropClimateWindowPatches
{
    [HarmonyPatch(
        typeof(BlockEntityFarmland),
        nameof(BlockEntityFarmland.TryPlant),
        [typeof(Block), typeof(ItemSlot), typeof(EntityAgent), typeof(BlockSelection)])]
    public static class FarmlandTryPlantClimateWindowPatch
    {
        [HarmonyPostfix]
        public static void Postfix(
            BlockEntityFarmland __instance,
            bool __result,
            Block block,
            EntityAgent byEntity)
        {
            if (!__result
                || __instance?.Api?.Side != EnumAppSide.Server
                || byEntity is not EntityPlayer entityPlayer
                || entityPlayer.Player == null)
            {
                return;
            }

            CropClimateWindow.TryStampOnPlant(
                __instance.Api.World,
                entityPlayer.Player,
                __instance,
                block);
        }
    }

    [HarmonyPatch(typeof(BlockEntityFarmland), nameof(BlockEntityFarmland.OnCropBlockBroken))]
    public static class FarmlandOnCropBrokenClimateWindowPatch
    {
        [HarmonyPostfix]
        public static void Postfix(BlockEntityFarmland __instance)
        {
            if (__instance?.Api?.Side != EnumAppSide.Server)
            {
                return;
            }

            CropClimateWindow.Clear(__instance);
        }
    }

    [HarmonyPatch(typeof(BlockEntityFarmland), nameof(BlockEntityFarmland.ToTreeAttributes))]
    public static class FarmlandToTreeClimateWindowPatch
    {
        [HarmonyPostfix]
        public static void Postfix(BlockEntityFarmland __instance, ITreeAttribute tree) =>
            CropClimateWindow.WriteToTree(__instance, tree);
    }

    [HarmonyPatch(typeof(BlockEntityFarmland), nameof(BlockEntityFarmland.FromTreeAttributes))]
    public static class FarmlandFromTreeClimateWindowPatch
    {
        [HarmonyPostfix]
        public static void Postfix(BlockEntityFarmland __instance, ITreeAttribute tree) =>
            CropClimateWindow.ReadFromTree(__instance, tree);
    }

    [HarmonyPatch(typeof(BlockEntityFarmland), "updateCropDamage")]
    public static class FarmlandUpdateCropDamageClimateWindowPatch
    {
        [HarmonyTranspiler]
        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) =>
            TranspileThresholdLoads(instructions);

        /// <summary>
        /// After each <c>ColdDamageBelow</c> / <c>HeatDamageAbove</c> load, inject
        /// <c>ldarg.0</c> + adjust helper (<c>float, farmland</c> call convention).
        /// </summary>
        public static IEnumerable<CodeInstruction> TranspileThresholdLoads(
            IEnumerable<CodeInstruction> instructions)
        {
            FieldInfo? coldField = AccessTools.Field(
                typeof(BlockCropProperties),
                nameof(BlockCropProperties.ColdDamageBelow));
            FieldInfo? heatField = AccessTools.Field(
                typeof(BlockCropProperties),
                nameof(BlockCropProperties.HeatDamageAbove));
            MethodInfo? adjustCold = AccessTools.Method(
                typeof(CropClimateWindow),
                nameof(CropClimateWindow.AdjustColdThreshold));
            MethodInfo? adjustHeat = AccessTools.Method(
                typeof(CropClimateWindow),
                nameof(CropClimateWindow.AdjustHeatThreshold));

            if (coldField == null || heatField == null || adjustCold == null || adjustHeat == null)
            {
                throw new InvalidOperationException(
                    "[prosequor] Crop climate window transpiler: missing field/helper.");
            }

            List<CodeInstruction> original = [.. instructions];
            List<CodeInstruction> patched = new(original.Count + 8);
            int coldHits = 0;
            int heatHits = 0;

            for (int i = 0; i < original.Count; i++)
            {
                CodeInstruction code = original[i];
                patched.Add(code);

                if (code.opcode != OpCodes.Ldfld || code.operand is not FieldInfo field)
                {
                    continue;
                }

                if (Equals(field, coldField))
                {
                    coldHits++;
                    patched.Add(new CodeInstruction(OpCodes.Ldarg_0));
                    patched.Add(new CodeInstruction(OpCodes.Call, adjustCold));
                }
                else if (Equals(field, heatField))
                {
                    heatHits++;
                    patched.Add(new CodeInstruction(OpCodes.Ldarg_0));
                    patched.Add(new CodeInstruction(OpCodes.Call, adjustHeat));
                }
            }

            if (coldHits == 1 && heatHits == 1)
            {
                return patched;
            }

            throw new InvalidOperationException(
                $"[prosequor] Crop climate window transpiler failed. coldHits={coldHits} heatHits={heatHits} (want 1 each).");
        }
    }
}
