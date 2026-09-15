using System.Runtime.CompilerServices;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace Prosequor.Xp;

/// <summary>
/// Collect-XP bool on block entities (ground-laid eggs). Persists via BE tree attrs;
/// copied onto item drops / pick stacks before the pickup gate runs.
/// </summary>
public static class CollectXpBlockStampStation
{
    static readonly ConditionalWeakTable<BlockEntity, Box> boxes = new();

    sealed class Box
    {
        public bool Stamped;
    }

    public static bool Has(BlockEntity? be) =>
        be != null && boxes.TryGetValue(be, out Box? box) && box is { Stamped: true };

    public static void Set(BlockEntity? be)
    {
        if (be == null)
        {
            return;
        }

        boxes.GetOrCreateValue(be).Stamped = true;
        be.MarkDirty(redrawOnClient: false);
    }

    public static void Clear(BlockEntity? be)
    {
        if (be == null || !boxes.TryGetValue(be, out Box? box) || box == null)
        {
            return;
        }

        box.Stamped = false;
        be.MarkDirty(redrawOnClient: false);
    }

    public static void SetAtPos(IWorldAccessor? world, BlockPos? pos)
    {
        if (world?.BlockAccessor == null || pos == null)
        {
            return;
        }

        BlockEntity? be = world.BlockAccessor.GetBlockEntity(pos);
        if (be != null)
        {
            Set(be);
        }
    }

    public static void WriteToTree(BlockEntity? be, ITreeAttribute? tree)
    {
        if (be == null || tree == null || !Has(be))
        {
            return;
        }

        CollectXpStamp.Set(tree);
    }

    public static void ReadFromTree(BlockEntity? be, ITreeAttribute? tree)
    {
        if (be == null || tree == null || !CollectXpStamp.Has(tree))
        {
            return;
        }

        boxes.GetOrCreateValue(be).Stamped = true;
    }

    public static void ApplyToStacks(BlockEntity? be, ItemStack[]? stacks) =>
        CollectXpStamp.ApplyToStacks(Has(be), stacks);

    public static void ApplyToStack(BlockEntity? be, ItemStack? stack) =>
        CollectXpStamp.ApplyToStack(Has(be), stack);

    public static void ApplyAtPos(IWorldAccessor? world, BlockPos? pos, ItemStack[]? stacks)
    {
        if (world == null || pos == null)
        {
            return;
        }

        ApplyToStacks(world.BlockAccessor?.GetBlockEntity(pos), stacks);
    }

    public static void ApplyAtPos(IWorldAccessor? world, BlockPos? pos, ItemStack? stack)
    {
        if (world == null || pos == null)
        {
            return;
        }

        ApplyToStack(world.BlockAccessor?.GetBlockEntity(pos), stack);
    }
}
