using System.Reflection;
using HarmonyLib;
using Prosequor.Ability.Hooks;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace Prosequor.Ability;

/// <summary>
/// Skep break: fold <c>spawn-bees-chance</c> then spawn bees; skip vanilla spawn body.
/// Also prepares / completes <c>skep-harvest</c> XP around base break.
/// </summary>
[HarmonyPatch(typeof(BlockSkep), nameof(BlockSkep.OnBlockBroken))]
public static class SkepBeeSpawnPatch
{
    static readonly MethodInfo? BaseOnBlockBroken =
        AccessTools.Method(
            typeof(Block),
            nameof(Block.OnBlockBroken),
            new[] { typeof(IWorldAccessor), typeof(BlockPos), typeof(IPlayer), typeof(float) });

    [HarmonyPrefix]
    public static bool Prefix(
        BlockSkep __instance,
        IWorldAccessor world,
        BlockPos pos,
        IPlayer byPlayer,
        float dropQuantityMultiplier)
    {
        BlockEntityBeehive? beh = world.BlockAccessor.GetBlockEntity(pos) as BlockEntityBeehive;
        SkepHarvestXp.PrepareBreak(beh, __instance, pos, byPlayer);

        BaseOnBlockBroken?.Invoke(
            __instance,
            [world, pos, byPlayer, dropQuantityMultiplier]);

        SkepHarvestXp.CompleteBreak(world.Api, byPlayer, __instance, pos);

        if (world.Side != EnumAppSide.Server || __instance.IsEmpty())
        {
            return false;
        }

        float chance = SkepBeeSpawnStation.ResolveSpawnChance(byPlayer, __instance, pos);
        if (chance <= 0f || world.Rand.NextDouble() >= chance)
        {
            return false;
        }

        EntityProperties? type = world.GetEntityType(new AssetLocation("beemob"));
        if (type == null)
        {
            return false;
        }

        Entity? entity = world.ClassRegistry.CreateEntity(type);
        if (entity == null)
        {
            return false;
        }

        entity.Pos.X = pos.X + 0.5f;
        entity.Pos.Y = pos.Y + 0.5f;
        entity.Pos.Z = pos.Z + 0.5f;
        entity.Pos.Yaw = (float)world.Rand.NextDouble() * 2 * GameMath.PI;
        entity.Attributes.SetString("origin", "brokenbeehive");
        world.SpawnEntity(entity);
        return false;
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

        // GetDrops already ran DropsStation (Sticky Fingers). Do not mutate again.
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
