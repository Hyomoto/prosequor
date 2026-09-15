using HarmonyLib;
using Prosequor.Ability.Hooks;
using Prosequor.Xp.Adapters;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace Prosequor.Ability;

/// <summary>
/// Block.GetDrops adapter: leave vanilla quantity alone, run mutate-drops on the
/// returned list (per-stack quantity/stack then list-post stacks). Attaches a
/// VS-backed <see cref="IDropTable"/> for ambient re-rolls.
/// </summary>
[HarmonyPatch(typeof(Block), nameof(Block.GetDrops))]
public static class BlockGetDropsAbilityPatch
{
    [ThreadStatic]
    static int suppressMutateDepth;

    /// <summary>True while a drop-table re-roll is calling vanilla GetDrops.</summary>
    public static bool SuppressMutate => suppressMutateDepth > 0;

    public static void PushSuppressMutate() => suppressMutateDepth++;

    public static void PopSuppressMutate()
    {
        if (suppressMutateDepth > 0)
        {
            suppressMutateDepth--;
        }
    }

    [HarmonyPrefix]
    public static void Prefix(
        Block __instance,
        IWorldAccessor world,
        BlockPos pos,
        IPlayer byPlayer,
        out bool __state)
    {
        __state = !SuppressMutate && FarmlandNutrientScope.TryPushFromBlock(world, __instance, pos);
    }

    [HarmonyPostfix]
    public static void Postfix(
        Block __instance,
        IWorldAccessor world,
        BlockPos pos,
        IPlayer byPlayer,
        float dropQuantityMultiplier,
        bool __state,
        ref ItemStack[] __result)
    {
        try
        {
            if (SuppressMutate)
            {
                return;
            }

            __result = RunDrops(__instance, world, pos, byPlayer, __result, dropQuantityMultiplier);
            SkepHarvestXp.NoteBreakDrops(__instance, pos, __result);
        }
        finally
        {
            if (__state)
            {
                FarmlandNutrientScope.Pop();
            }
        }
    }

    internal static ItemStack[] RunDrops(
        Block block,
        IWorldAccessor world,
        BlockPos pos,
        IPlayer byPlayer,
        ItemStack[]? drops,
        float dropQuantityMultiplier = 1f)
    {
        if (world?.Side != EnumAppSide.Server || byPlayer == null || block == null)
        {
            return drops ?? Array.Empty<ItemStack>();
        }

        if (ProsequorModSystem.For(world.Api)?.Collections?.Index == null)
        {
            return drops ?? Array.Empty<ItemStack>();
        }

        AbilityAction fact = DropsFactBuilder.ForBlock(byPlayer, block, pos);
        IDropTable dropTable = new BlockGetDropsDropTable(block, world, pos, byPlayer, dropQuantityMultiplier);
        return RunDropsWithFact(block, world, pos, byPlayer, drops, fact, dropTable);
    }

    internal static ItemStack[] RunDropsWithFact(
        Block block,
        IWorldAccessor world,
        BlockPos pos,
        IPlayer byPlayer,
        ItemStack[]? drops,
        AbilityAction fact,
        IDropTable? dropTable)
    {
        return DropsStation.Run(world, byPlayer, drops, fact, block, pos, dropTable);
    }
}

/// <summary>
/// BlockFullCoating overrides Block.GetDrops; run the same typed station on its result.
/// </summary>
[HarmonyPatch(typeof(BlockFullCoating), nameof(BlockFullCoating.GetDrops))]
public static class FullCoatingGetDropsAbilityPatch
{
    [HarmonyPostfix]
    public static void Postfix(
        BlockFullCoating __instance,
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

        if (world?.Side == EnumAppSide.Server)
        {
            HarvestXp.NoteBreakDrops(__instance, pos, __result);
        }
    }
}

/// <summary>
/// Re-rolls vanilla <see cref="Block.GetDrops"/> under mutate suppress and picks one stack.
/// </summary>
public sealed class BlockGetDropsDropTable : IDropTable
{
    readonly Block block;
    readonly IWorldAccessor world;
    readonly BlockPos pos;
    readonly IPlayer byPlayer;
    readonly float dropQuantityMultiplier;

    public BlockGetDropsDropTable(
        Block block,
        IWorldAccessor world,
        BlockPos pos,
        IPlayer byPlayer,
        float dropQuantityMultiplier)
    {
        this.block = block;
        this.world = world;
        this.pos = pos.Copy();
        this.byPlayer = byPlayer;
        this.dropQuantityMultiplier = dropQuantityMultiplier;
    }

    public ItemStack? TryRollOne()
    {
        BlockGetDropsAbilityPatch.PushSuppressMutate();
        ItemStack[]? rolled;
        try
        {
            rolled = block.GetDrops(world, pos, byPlayer, dropQuantityMultiplier);
        }
        finally
        {
            BlockGetDropsAbilityPatch.PopSuppressMutate();
        }

        if (rolled == null || rolled.Length == 0)
        {
            return null;
        }

        List<ItemStack> candidates = new(rolled.Length);
        for (int i = 0; i < rolled.Length; i++)
        {
            ItemStack? stack = rolled[i];
            if (stack != null && stack.StackSize > 0)
            {
                candidates.Add(stack);
            }
        }

        if (candidates.Count == 0)
        {
            return null;
        }

        int pick = world.Rand.Next(candidates.Count);
        return candidates[pick].Clone();
    }
}
