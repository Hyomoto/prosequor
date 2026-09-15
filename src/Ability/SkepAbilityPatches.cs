using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace Prosequor.Ability;

/// <summary>
/// Skep break: prefix/postfix <c>skep-harvest</c> XP bookends; transpiler folds
/// <c>spawn-bees-chance</c> over vanilla <c>beemobSpawnChance</c> (spawn body stays vanilla).
/// </summary>
[HarmonyPatch(typeof(BlockSkep), nameof(BlockSkep.OnBlockBroken))]
public static class SkepBeeSpawnPatch
{
    public static readonly FieldInfo BeemobSpawnChanceField =
        AccessTools.Field(typeof(BlockSkep), "beemobSpawnChance")
        ?? throw new InvalidOperationException("[prosequor] BlockSkep.beemobSpawnChance field missing.");

    [HarmonyPrefix]
    public static void Prefix(
        BlockSkep __instance,
        IWorldAccessor world,
        BlockPos pos,
        IPlayer byPlayer)
    {
        BlockEntityBeehive? beh = world.BlockAccessor.GetBlockEntity(pos) as BlockEntityBeehive;
        SkepHarvestXp.PrepareBreak(beh, __instance, pos, byPlayer);
    }

    [HarmonyPostfix]
    public static void Postfix(
        BlockSkep __instance,
        IWorldAccessor world,
        BlockPos pos,
        IPlayer byPlayer)
    {
        SkepHarvestXp.CompleteBreak(world.Api, byPlayer, __instance, pos);
    }

    [HarmonyTranspiler]
    public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) =>
        TranspileBeemobSpawnChanceLoad(
            instructions,
            AccessTools.Method(
                typeof(SkepBeeSpawnStation),
                nameof(SkepBeeSpawnStation.ResolveSpawnChanceForBreak)));

    /// <summary>
    /// Replace <c>ldarg.0; ldfld beemobSpawnChance</c> with
    /// <c>ldarg.0; ldarg.3; ldarg.2; call ResolveSpawnChanceForBreak</c>.
    /// </summary>
    public static IEnumerable<CodeInstruction> TranspileBeemobSpawnChanceLoad(
        IEnumerable<CodeInstruction> instructions,
        MethodInfo helper)
    {
        FieldInfo field = BeemobSpawnChanceField;
        List<CodeInstruction> original = [.. instructions];
        List<CodeInstruction> patched = new(original.Count + 2);
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
                patched.Add(new CodeInstruction(OpCodes.Ldarg_3));
                patched.Add(new CodeInstruction(OpCodes.Ldarg_2));
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
            $"[prosequor] Skep bee-spawn transpiler failed. hits={hits} (want 1).");
    }
}

/// <summary>
/// Harvestable skep sneak+right-click: when allow-right-click-harvest, extract honey
/// (or break the skep). Plain right-click stays vanilla pickup.
/// </summary>
[HarmonyPatch(typeof(BlockSkep), nameof(BlockSkep.OnBlockInteractStart))]
public static class SkepHarvestInteractPatch
{
    static readonly FieldInfo? HarvestableAtHoursField =
        AccessTools.Field(typeof(BlockEntityBeehive), "harvestableAtTotalHours");

    [HarmonyPrefix]
    public static bool Prefix(
        BlockSkep __instance,
        IWorldAccessor world,
        IPlayer byPlayer,
        BlockSelection blockSel,
        ref bool __result)
    {
        if (world.Side != EnumAppSide.Server
            || byPlayer == null
            || blockSel == null
            || __instance.IsEmpty()
            || !byPlayer.Entity.Controls.Sneak)
        {
            return true;
        }

        CollectibleObject? collObj = byPlayer.InventoryManager.ActiveHotbarSlot?.Itemstack?.Collectible;
        if (collObj is ItemClosedBeenade or ItemOpenedBeenade)
        {
            return true;
        }

        if (world.BlockAccessor.GetBlockEntity(blockSel.Position) is not BlockEntityBeehive beh
            || !beh.Harvestable)
        {
            return true;
        }

        if (!SkepHarvestStation.ResolveAllowRightClickHarvest(byPlayer, __instance, blockSel.Position))
        {
            return true;
        }

        float breakChance = SkepHarvestStation.ResolveRightClickBreakChance(
            byPlayer,
            __instance,
            blockSel.Position);

        if (breakChance > 0f && world.Rand.NextDouble() < breakChance)
        {
            // Break path owns skep-harvest XP (PrepareBreak / CompleteBreak).
            world.BlockAccessor.BreakBlock(blockSel.Position, byPlayer);
            __result = true;
            return false;
        }

        ItemStack[]? drops = __instance.GetDrops(world, blockSel.Position, byPlayer);
        List<ItemStack> honeycomb = new();
        if (drops != null)
        {
            foreach (ItemStack? stack in drops)
            {
                if (SkepHarvestXp.IsHoneycomb(stack))
                {
                    honeycomb.Add(stack.Clone());
                }
            }
        }

        // GetDrops already ran DropsStation via SkepGetDropsAbilityPatch (Sticky Fingers).
        ItemStack[] mutated = honeycomb.ToArray();

        if (mutated.Length > 0)
        {
            SkepHarvestXp.SettleExtract(beh, __instance, blockSel.Position, byPlayer, mutated);

            foreach (ItemStack stack in mutated)
            {
                if (!byPlayer.InventoryManager.TryGiveItemstack(stack, true))
                {
                    world.SpawnItemEntity(stack, blockSel.Position.ToVec3d().Add(0.5, 0.5, 0.5));
                }
            }
        }

        beh.Harvestable = false;
        if (HarvestableAtHoursField != null)
        {
            double next = world.Calendar.TotalHours + 12.0 * (3.0 + world.Rand.NextDouble() * 8.0);
            HarvestableAtHoursField.SetValue(beh, next);
        }

        beh.MarkDirty(true);
        world.PlaySoundAt(new AssetLocation("sounds/block/planks"), blockSel.Position, -0.5, byPlayer, false);
        __result = true;
        return false;
    }
}

/// <summary>
/// <see cref="BlockSkep"/> overrides <see cref="Block.GetDrops"/>; run mutate-drops
/// and note honeycomb for break-path <c>skep-harvest</c> XP (same pattern as coating).
/// </summary>
[HarmonyPatch(typeof(BlockSkep), nameof(BlockSkep.GetDrops))]
public static class SkepGetDropsAbilityPatch
{
    [HarmonyPostfix]
    public static void Postfix(
        BlockSkep __instance,
        IWorldAccessor world,
        BlockPos pos,
        IPlayer byPlayer,
        float dropQuantityMultiplier,
        ref ItemStack[] __result)
    {
        if (BlockGetDropsAbilityPatch.SuppressMutate)
        {
            return;
        }

        __result = BlockGetDropsAbilityPatch.RunDrops(
            __instance,
            world,
            pos,
            byPlayer,
            __result,
            dropQuantityMultiplier);
        SkepHarvestXp.NoteBreakDrops(__instance, pos, __result);
    }
}

/// <summary>
/// Skep place: remember placer on <see cref="Block.DoPlaceBlock"/>, then stamp
/// contributor after stack capture on <see cref="Block.OnBlockPlaced"/>.
/// </summary>
public static class SkepPlaceContributorPatch
{
    [ThreadStatic]
    static string? pendingPlacerUid;

    [ThreadStatic]
    static BlockPos? pendingPos;

    [HarmonyPatch(typeof(Block), nameof(Block.DoPlaceBlock))]
    public static class DoPlaceBlockRememberPlacer
    {
        [HarmonyPostfix]
        public static void Postfix(
            bool __result,
            IWorldAccessor world,
            IPlayer byPlayer,
            BlockSelection blockSel)
        {
            pendingPlacerUid = null;
            pendingPos = null;
            if (!__result
                || world?.Side != EnumAppSide.Server
                || byPlayer?.PlayerUID == null
                || blockSel?.Position == null)
            {
                return;
            }

            Block? block = world.BlockAccessor.GetBlock(blockSel.Position);
            if (!SkepHarvestXp.IsSkepBlock(block))
            {
                return;
            }

            pendingPlacerUid = byPlayer.PlayerUID;
            pendingPos = blockSel.Position.Copy();
        }
    }

    [HarmonyPatch(typeof(Block), nameof(Block.OnBlockPlaced))]
    public static class OnBlockPlacedAddContributor
    {
        [HarmonyPostfix]
        [HarmonyPriority(Priority.Last)]
        public static void Postfix(IWorldAccessor world, BlockPos blockPos)
        {
            string? uid = pendingPlacerUid;
            BlockPos? expected = pendingPos;
            pendingPlacerUid = null;
            pendingPos = null;

            if (uid == null
                || expected == null
                || world?.Side != EnumAppSide.Server
                || blockPos == null
                || !expected.Equals(blockPos))
            {
                return;
            }

            BlockEntity? be = world.BlockAccessor.GetBlockEntity(blockPos);
            SkepHarvestXp.OnSkepPlaced(be, uid);
        }
    }
}
