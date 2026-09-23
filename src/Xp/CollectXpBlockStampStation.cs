using Prosequor.Ability;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace Prosequor.Xp;

/// <summary>
/// Collect-XP bool on block entities (ground-laid eggs). Persists on
/// <see cref="ProsequorChunkPedigree"/>; copied onto item drops / pick stacks
/// before the pickup gate runs.
/// </summary>
public static class CollectXpBlockStampStation
{
    public static bool Has(BlockEntity? be) =>
        be != null
        && ProsequorBlockPedigreeStation.TryGetBox(be, out ProsequorChunkPedigree.Box box)
        && box.CollectXp;

    public static void Set(BlockEntity? be)
    {
        if (be == null)
        {
            return;
        }

        ProsequorBlockPedigreeStation.Mutate(be, box => box.CollectXp = true);
    }

    public static void Clear(BlockEntity? be)
    {
        if (be == null
            || !ProsequorBlockPedigreeStation.TryGetBox(be, out ProsequorChunkPedigree.Box box)
            || !box.CollectXp)
        {
            return;
        }

        ProsequorBlockPedigreeStation.Mutate(be, b => b.CollectXp = false);
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
