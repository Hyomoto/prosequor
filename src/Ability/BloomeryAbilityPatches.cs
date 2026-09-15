using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace Prosequor.Ability;

/// <summary>
/// Bloomery harvest: break-with-bloom XP, and Bloom Brigand sneak+RMB extract.
/// </summary>
public static class BloomeryAbilityPatches
{
    /// <summary>
    /// Before inventory drops: if a finished bloom is present, pay XP to the breaker.
    /// </summary>
    [HarmonyPatch(typeof(BlockEntityBloomery), nameof(BlockEntityBloomery.OnBlockBroken))]
    public static class BloomeryBreakHarvestXpPatch
    {
        [HarmonyPrefix]
        public static void Prefix(BlockEntityBloomery __instance, IPlayer byPlayer)
        {
            if (!BloomeryHarvestXp.HasFinishedBloom(__instance) || byPlayer == null)
            {
                return;
            }

            ItemStack? bloom = BloomeryHarvestXp.TryGetOutStack(__instance)?.Clone();
            if (bloom == null)
            {
                return;
            }

            BloomeryHarvestXp.Settle(byPlayer, bloom, __instance.Block);
        }
    }

    /// <summary>
    /// Sneak + empty hand + Bloom Brigand: extract finished bloom (or shatter it).
    /// Plain right-click stays vanilla fuel/ore/chimney.
    /// </summary>
    [HarmonyPatch(typeof(BlockBloomery), nameof(BlockBloomery.OnBlockInteractStart))]
    public static class BloomeryHarvestInteractPatch
    {
        [HarmonyPrefix]
        public static bool Prefix(
            BlockBloomery __instance,
            IWorldAccessor world,
            IPlayer byPlayer,
            BlockSelection blockSel,
            ref bool __result)
        {
            if (world.Side != EnumAppSide.Server
                || byPlayer == null
                || blockSel == null
                || !byPlayer.Entity.Controls.Sneak)
            {
                return true;
            }

            // Chimney placement and fuel/ore add keep vanilla.
            ItemStack? hotbar = byPlayer.InventoryManager.ActiveHotbarSlot?.Itemstack;
            if (hotbar != null)
            {
                return true;
            }

            if (world.BlockAccessor.GetBlockEntity(blockSel.Position) is not BlockEntityBloomery be
                || !BloomeryHarvestXp.HasFinishedBloom(be))
            {
                return true;
            }

            if (!BloomeryHarvestStation.ResolveAllowRightClickHarvest(
                    byPlayer,
                    __instance,
                    blockSel.Position))
            {
                return true;
            }

            ItemSlot? outSlot = BloomeryHarvestXp.TryGetOutSlot(be);
            ItemStack? bloomStack = outSlot?.Itemstack;
            if (outSlot == null || bloomStack == null)
            {
                return true;
            }

            float breakChance = BloomeryHarvestStation.ResolveRightClickBreakChance(
                byPlayer,
                __instance,
                blockSel.Position);

            ItemStack bloom = bloomStack.Clone();
            outSlot.Itemstack = null;
            be.MarkDirty(true);

            if (breakChance > 0f && world.Rand.NextDouble() < breakChance)
            {
                world.PlaySoundAt(
                    new AssetLocation("sounds/effect/anvilhit"),
                    blockSel.Position,
                    0,
                    byPlayer);
                __result = true;
                return false;
            }

            BloomeryHarvestXp.Settle(byPlayer, bloom, __instance);

            if (!byPlayer.InventoryManager.TryGiveItemstack(bloom, true))
            {
                world.SpawnItemEntity(
                    bloom,
                    blockSel.Position.ToVec3d().Add(0.5, 0.5, 0.5));
            }

            world.PlaySoundAt(
                new AssetLocation("sounds/block/loosestone"),
                blockSel.Position,
                0,
                byPlayer);
            __result = true;
            return false;
        }
    }
}
