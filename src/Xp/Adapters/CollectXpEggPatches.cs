using HarmonyLib;
using Prosequor.Ability;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace Prosequor.Xp.Adapters;

/// <summary>
/// Friendly hens stamp collect-XP on nest eggs (item) and ground egg blocks (BE).
/// BE stamp is copied onto drops / pick stacks before the pickup gate.
/// </summary>
public static class CollectXpEggPatches
{
    static readonly AccessTools.FieldRef<AiTaskSeekBlockAndLay, EntityAgent>? LayEntityField =
        AccessTools.FieldRefAccess<AiTaskSeekBlockAndLay, EntityAgent>("entity");

    static readonly AccessTools.FieldRef<AiTaskSeekBlockAndLayR, EntityAgent>? LayREntityField =
        AccessTools.FieldRefAccess<AiTaskSeekBlockAndLayR, EntityAgent>("entity");

    [HarmonyPatch(typeof(BlockEntityHenBox), nameof(BlockEntityHenBox.TryAddEgg))]
    public static class HenBoxTryAddEggCollectXpPatch
    {
        [HarmonyPostfix]
        public static void Postfix(BlockEntityHenBox __instance, ItemStack egg, bool __result)
        {
            if (!__result
                || egg == null
                || __instance?.Api?.Side != EnumAppSide.Server)
            {
                return;
            }

            Entity? hen = __instance.occupier;
            if (!HusbandryFriendliness.IsFriendly(hen))
            {
                return;
            }

            CollectXpStamp.Set(egg);
        }
    }

    [HarmonyPatch(typeof(AiTaskSeekBlockAndLay), "TryPlace")]
    public static class SeekBlockAndLayTryPlaceCollectXpPatch
    {
        [HarmonyPostfix]
        public static void Postfix(
            AiTaskSeekBlockAndLay __instance,
            Block block,
            int dx,
            int dy,
            int dz,
            bool __result)
        {
            EntityAgent? entity = LayEntityField != null ? LayEntityField(__instance) : null;
            OnGroundEggPlaced(entity, block, dx, dy, dz, __result);
        }
    }

    [HarmonyPatch(typeof(AiTaskSeekBlockAndLayR), "TryPlace")]
    public static class SeekBlockAndLayRTryPlaceCollectXpPatch
    {
        [HarmonyPostfix]
        public static void Postfix(
            AiTaskSeekBlockAndLayR __instance,
            Block block,
            int dx,
            int dy,
            int dz,
            bool __result)
        {
            EntityAgent? entity = LayREntityField != null ? LayREntityField(__instance) : null;
            OnGroundEggPlaced(entity, block, dx, dy, dz, __result);
        }
    }

    static void OnGroundEggPlaced(
        EntityAgent? entity,
        Block? block,
        int dx,
        int dy,
        int dz,
        bool placed)
    {
        if (!placed
            || entity?.World?.Side != EnumAppSide.Server
            || block == null
            || !HusbandryFriendliness.IsFriendly(entity))
        {
            return;
        }

        BlockPos pos = entity.Pos.XYZ.AsBlockPos.Add(dx, dy, dz);
        CollectXpBlockStampStation.SetAtPos(entity.World, pos);
    }

    [HarmonyPatch(typeof(BlockEntity), nameof(BlockEntity.ToTreeAttributes))]
    public static class BlockEntityToTreeCollectXpPatch
    {
        [HarmonyPostfix]
        public static void Postfix(BlockEntity __instance, ITreeAttribute tree) =>
            CollectXpBlockStampStation.WriteToTree(__instance, tree);
    }

    [HarmonyPatch(typeof(BlockEntity), nameof(BlockEntity.FromTreeAttributes))]
    public static class BlockEntityFromTreeCollectXpPatch
    {
        [HarmonyPostfix]
        public static void Postfix(BlockEntity __instance, ITreeAttribute tree) =>
            CollectXpBlockStampStation.ReadFromTree(__instance, tree);
    }

    [HarmonyPatch(typeof(Block), nameof(Block.GetDrops))]
    public static class BlockGetDropsCollectXpPatch
    {
        [HarmonyPostfix]
        [HarmonyPriority(Priority.First)]
        public static void Postfix(
            Block __instance,
            IWorldAccessor world,
            BlockPos pos,
            ref ItemStack[] __result)
        {
            if (!IsEggBlock(__instance))
            {
                return;
            }

            CollectXpBlockStampStation.ApplyAtPos(world, pos, __result);
        }
    }

    [HarmonyPatch(typeof(Block), nameof(Block.OnPickBlock))]
    public static class BlockOnPickBlockCollectXpPatch
    {
        [HarmonyPostfix]
        [HarmonyPriority(Priority.Last)]
        public static void Postfix(
            Block __instance,
            IWorldAccessor world,
            BlockPos pos,
            ref ItemStack __result)
        {
            if (!IsEggBlock(__instance))
            {
                return;
            }

            CollectXpBlockStampStation.ApplyAtPos(world, pos, __result);
        }
    }

    static bool IsEggBlock(Block? block)
    {
        string? path = block?.Code?.Path;
        return path != null
            && path.StartsWith("egg-chicken", StringComparison.OrdinalIgnoreCase)
            && !path.Contains("broken", StringComparison.OrdinalIgnoreCase);
    }
}
