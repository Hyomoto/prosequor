using Prosequor.Ability.Hooks;
using Prosequor.Player;
using Prosequor.Xp.Adapters;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace Prosequor.Ability;

/// <summary>
/// Source-agnostic station for mutate-drops: snapshot originals, per-stack
/// quantity then stack, then list-post stacks. Adapters collect the vanilla list
/// first; fortune stays vanilla.
/// </summary>
public static class DropsStation
{
    /// <summary>
    /// Runs mutate-drops on a vanilla drop list. Quantity seeds each stack's
    /// <see cref="ItemStack.StackSize"/>; size &lt;= 0 after conversion drops the stack.
    /// </summary>
    public static ItemStack[] Run(
        IWorldAccessor world,
        IPlayer player,
        ItemStack[]? drops,
        AbilityAction fact,
        Block? block = null,
        BlockPos? pos = null,
        IDropTable? dropTable = null,
        HookId? hook = null)
    {
        HookId surface = hook ?? HookIds.BlockInteraction;
        if (block != null && pos != null && ForagePlayerPlaced.IsWild(world, block, pos))
        {
            fact = EventFactBuilder.WithToken(fact, HarvestXp.TokenUndomesticated);
        }

        ItemStack[] current = drops ?? Array.Empty<ItemStack>();
        if (!TryCreateContext(world, player, fact, block, pos, dropTable, surface, out DropsContext context))
        {
            return current;
        }

        ProsequorModSystem mod = ProsequorModSystem.For(world.Api)!;
        List<ItemStack> originals = new(current.Length);
        for (int i = 0; i < current.Length; i++)
        {
            ItemStack? stack = current[i];
            if (stack != null)
            {
                originals.Add(stack.Clone());
            }
        }

        context.OriginalDrops = originals;

        List<ItemStack> afterPerStack = new(current.Length);
        for (int i = 0; i < current.Length; i++)
        {
            ItemStack? stack = current[i];
            if (stack == null || stack.StackSize <= 0)
            {
                continue;
            }

            string? dropCode = EventFactBuilder.CodeOf(stack);
            AbilityAction stackFact = EventFactBuilder.WithDrop(fact, dropCode);
            context.Fact = stackFact;

            float qty = mod.Pipeline!.Run(
                surface,
                VerbIds.MutateDrops,
                HookIds.Quantity,
                context,
                (float)stack.StackSize);

            int size = AbilityFormulas.StochasticRound(qty, world.Rand);
            if (size <= 0)
            {
                continue;
            }

            ItemStack working = stack.Clone();
            working.StackSize = size;
            // Break/luck qty change: keep attributed shortfall (do not pad or duplicate).
            ProsequorStackPedigree.EnsureFrozenMatchesStackSize(working);

            ItemStack next = mod.Pipeline.Run(
                surface,
                VerbIds.MutateDrops,
                HookIds.Stack,
                context,
                working);

            if (next != null && next.StackSize > 0)
            {
                afterPerStack.Add(next);
            }
        }

        // List-post uses the break/catch fact without a per-stack Drop stamp.
        context.Fact = fact;
        IReadOnlyList<ItemStack> finalList = mod.Pipeline!.Run(
            surface,
            VerbIds.MutateDrops,
            HookIds.Stacks,
            context,
            (IReadOnlyList<ItemStack>)afterPerStack);

        return ReferenceEquals(finalList, afterPerStack)
            ? afterPerStack.ToArray()
            : finalList as ItemStack[] ?? finalList.ToArray();
    }

    /// <summary>
    /// Applies quantity + stack to a single stack (e.g. GetNextItemStack paths).
    /// Does not run list-post <c>stacks</c>; callers that need list rules must buffer.
    /// </summary>
    public static ItemStack? RunOneStack(
        IWorldAccessor world,
        IPlayer player,
        ItemStack? stack,
        AbilityAction fact,
        Block? block = null,
        BlockPos? pos = null,
        HookId? hook = null)
    {
        HookId surface = hook ?? HookIds.BlockInteraction;
        if (stack == null || stack.StackSize <= 0)
        {
            return null;
        }

        if (!TryCreateContext(world, player, fact, block, pos, dropTable: null, surface, out DropsContext context))
        {
            return stack;
        }

        ProsequorModSystem mod = ProsequorModSystem.For(world.Api)!;
        context.OriginalDrops = new[] { stack.Clone() };

        string? dropCode = EventFactBuilder.CodeOf(stack);
        context.Fact = EventFactBuilder.WithDrop(fact, dropCode);

        float qty = mod.Pipeline!.Run(
            surface,
            VerbIds.MutateDrops,
            HookIds.Quantity,
            context,
            (float)stack.StackSize);

        int size = AbilityFormulas.StochasticRound(qty, world.Rand);
        if (size <= 0)
        {
            return null;
        }

        ItemStack working = stack.Clone();
        working.StackSize = size;
        ProsequorStackPedigree.EnsureFrozenMatchesStackSize(working);

        ItemStack next = mod.Pipeline.Run(
            surface,
            VerbIds.MutateDrops,
            HookIds.Stack,
            context,
            working);

        return next != null && next.StackSize > 0 ? next : null;
    }

    /// <summary>
    /// Runs only the list-post <c>stacks</c> phase on an already per-stack-processed list.
    /// </summary>
    public static ItemStack[] RunStacksOnly(
        IWorldAccessor world,
        IPlayer player,
        ItemStack[]? drops,
        AbilityAction fact,
        IReadOnlyList<ItemStack>? originals = null,
        Block? block = null,
        BlockPos? pos = null,
        IDropTable? dropTable = null,
        HookId? hook = null)
    {
        HookId surface = hook ?? HookIds.BlockInteraction;
        ItemStack[] current = drops ?? Array.Empty<ItemStack>();
        if (!TryCreateContext(world, player, fact, block, pos, dropTable, surface, out DropsContext context))
        {
            return current;
        }

        if (originals != null)
        {
            context.OriginalDrops = originals;
        }
        else
        {
            List<ItemStack> snap = new(current.Length);
            for (int i = 0; i < current.Length; i++)
            {
                if (current[i] != null)
                {
                    snap.Add(current[i].Clone());
                }
            }

            context.OriginalDrops = snap;
        }

        ProsequorModSystem mod = ProsequorModSystem.For(world.Api)!;
        IReadOnlyList<ItemStack> next = mod.Pipeline!.Run(
            surface,
            VerbIds.MutateDrops,
            HookIds.Stacks,
            context,
            (IReadOnlyList<ItemStack>)current);

        return ReferenceEquals(next, current)
            ? current
            : next as ItemStack[] ?? next.ToArray();
    }

    static bool TryCreateContext(
        IWorldAccessor world,
        IPlayer player,
        AbilityAction fact,
        Block? block,
        BlockPos? pos,
        IDropTable? dropTable,
        HookId hook,
        out DropsContext context)
    {
        context = null!;
        if (world?.Side != EnumAppSide.Server || player == null || fact == null)
        {
            return false;
        }

        ProsequorModSystem? mod = ProsequorModSystem.For(world.Api);
        if (mod?.Pipeline == null || mod.Collections?.Index == null)
        {
            return false;
        }

        IPlayerProgress? progress = ProsequorModSystem.GetProgress(player);
        if (progress == null)
        {
            return false;
        }

        float[]? farmlandNutrients = null;
        int[]? farmlandOriginal = null;
        if (FarmlandNutrientScope.TryGet(out float[] nutrients, out int[] original))
        {
            farmlandNutrients = nutrients;
            farmlandOriginal = original;
        }

        context = new DropsContext
        {
            Hook = hook,
            World = world,
            Block = block,
            Pos = pos,
            Player = player,
            Progress = progress,
            Fact = fact,
            Tags = mod.Collections.Index,
            DropTable = dropTable,
            FarmlandNutrients = farmlandNutrients,
            FarmlandOriginalFertility = farmlandOriginal
        };
        return true;
    }
}
