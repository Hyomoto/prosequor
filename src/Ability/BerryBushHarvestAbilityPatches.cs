using HarmonyLib;
using Prosequor.Xp.Adapters;
using Vintagestory.API.Common;
using Vintagestory.GameContent;

namespace Prosequor.Ability;

/// <summary>
/// Berry Grower and sap scoop for <see cref="BlockBehaviorHarvestable"/>
/// interact harvests.
/// </summary>
[HarmonyPatch(typeof(BlockBehaviorHarvestable), nameof(BlockBehaviorHarvestable.OnBlockInteractStop))]
public static class BlockBehaviorHarvestableAbilityPatch
{
    [HarmonyPrefix]
    public static void Prefix(
        BlockBehaviorHarvestable __instance,
        IWorldAccessor world,
        IPlayer byPlayer,
        BlockSelection blockSel)
    {
        Block? block = __instance.block;
        if (block == null
            || blockSel == null
            || (!AbilityBootstrap.IsBerryBushBlock(block) && !ForageBlocks.IsSap(block)))
        {
            return;
        }

        AbilityAction fact = DropsFactBuilder.ForHarvest(byPlayer, block, blockSel.Position);
        DropHarvestScope.Begin(byPlayer, fact, block, blockSel.Position);
    }

    [HarmonyPostfix]
    public static void Postfix() => DropHarvestScope.Pop();
}

/// <summary>
/// Berry Grower for modern fruiting-bush interact harvests.
/// </summary>
[HarmonyPatch(typeof(BEBehaviorFruitingBush), nameof(BEBehaviorFruitingBush.OnBlockInteractStop))]
public static class FruitingBushInteractAbilityPatch
{
    [HarmonyPrefix]
    public static void Prefix(
        BEBehaviorFruitingBush __instance,
        IWorldAccessor world,
        IPlayer byPlayer,
        BlockSelection blockSel)
    {
        Block? block = world?.BlockAccessor?.GetBlock(blockSel.Position)
            ?? __instance.Blockentity?.Block;
        if (block == null || blockSel == null || byPlayer == null)
        {
            return;
        }

        AbilityAction fact = DropsFactBuilder.ForHarvest(byPlayer, block, blockSel.Position);
        DropHarvestScope.Begin(byPlayer, fact, block, blockSel.Position);
    }

    [HarmonyPostfix]
    public static void Postfix() => DropHarvestScope.Pop();
}

/// <summary>
/// Fruiting-bush ripe drop lists (break / API): mutate-drops, Grown By, harvest XP.
/// </summary>
[HarmonyPatch(typeof(BEBehaviorFruitingBush), nameof(BEBehaviorFruitingBush.GetRipeDrops))]
public static class FruitingBushRipeDropsAbilityPatch
{
    [HarmonyPostfix]
    public static void Postfix(
        BEBehaviorFruitingBush __instance,
        IPlayer byPlayer,
        ref ItemStack[] __result)
    {
        BlockEntity? be = __instance?.Blockentity;
        IWorldAccessor? world = be?.Api?.World;
        Block? block = be?.Block;
        if (world?.Side != EnumAppSide.Server || byPlayer == null || block == null || be == null)
        {
            return;
        }

        AbilityAction fact = DropsFactBuilder.ForHarvest(byPlayer, block, be.Pos);
        __result = DropsStation.Run(world, byPlayer, __result, fact, block, be.Pos, dropTable: null);
        OwnerCredit.StampGrownByDrops(world, block, be.Pos, __result);
        HarvestXp.NotifyInteractHarvest(world.Api, byPlayer, block, be.Pos, __result);
    }
}
