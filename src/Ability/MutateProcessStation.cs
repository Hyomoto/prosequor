using Prosequor.Ability.Hooks;
using Prosequor.Player;
using Vintagestory.API.Common;

namespace Prosequor.Ability;

/// <summary>
/// Process-completion egress. Runs <c>quantity</c> then <c>stacks</c>.
/// Chance gates belong in nested <c>prosequor:chance</c> actions on stacks.
/// Resolves skills from a process-starter uid (online or parked progress).
/// </summary>
public static class MutateProcessStation
{
    public static bool TryApply(IWorldAccessor world, IPlayer maker, ItemSlot outputSlot, string verb)
    {
        if (maker == null)
        {
            return false;
        }

        return TryApply(world, maker.PlayerUID, outputSlot, verb);
    }

    public static bool TryApply(IWorldAccessor world, string? playerUid, ItemSlot outputSlot, string verb)
    {
        if (world?.Side != EnumAppSide.Server
            || string.IsNullOrWhiteSpace(playerUid)
            || outputSlot?.Itemstack?.Collectible?.Code == null)
        {
            return false;
        }

        if (!TryCreateContext(world, playerUid.Trim(), outputSlot, verb, out MutateProcessContext? context))
        {
            return false;
        }

        ProsequorModSystem mod = ProsequorModSystem.For(world.Api)!;
        bool changed = false;

        ItemStack stack = outputSlot.Itemstack;
        int seedSize = stack.StackSize;
        if (seedSize > 0)
        {
            float qty = mod.Pipeline!.Run(
                HookIds.BlockInteraction,
                VerbIds.MutateProcess,
                HookIds.Quantity,
                context,
                (float)seedSize);
            int size = AbilityFormulas.StochasticRound(qty, world.Rand);
            if (size < 1)
            {
                size = 1;
            }

            if (size != seedSize)
            {
                ItemStack resized = stack.Clone();
                resized.StackSize = size;
                outputSlot.Itemstack = resized;
                stack = resized;
                changed = true;
            }
        }

        ItemStack[] current = [stack];
        IReadOnlyList<ItemStack> next = mod.Pipeline!.Run(
            HookIds.BlockInteraction,
            VerbIds.MutateProcess,
            HookIds.Stacks,
            context,
            (IReadOnlyList<ItemStack>)current);

        if (next.Count > 0
            && next[0] != null
            && (next[0] != outputSlot.Itemstack || !ReferenceEquals(next, current)))
        {
            if (next[0] != outputSlot.Itemstack
                || next[0].StackSize != outputSlot.Itemstack.StackSize)
            {
                outputSlot.Itemstack = next[0];
                changed = true;
            }
        }

        if (changed)
        {
            outputSlot.MarkDirty();
        }

        return changed;
    }

    /// <summary>
    /// Same <c>mutate-process</c> / <c>quantity</c> fold as <see cref="TryApply"/>, but returns
    /// the continuous float (no slot rewrite / stochastic round). Used for liquid litres etc.
    /// </summary>
    public static float ResolveQuantity(
        IWorldAccessor world,
        string? playerUid,
        ItemStack? targetStack,
        string token,
        float seed)
    {
        if (world?.Side != EnumAppSide.Server
            || string.IsNullOrWhiteSpace(playerUid)
            || targetStack?.Collectible?.Code == null
            || !float.IsFinite(seed)
            || seed <= 0f)
        {
            return seed > 0f && float.IsFinite(seed) ? seed : 0f;
        }

        ItemSlot slot = new DummySlot(targetStack);
        if (!TryCreateContext(world, playerUid.Trim(), slot, token, out MutateProcessContext? context))
        {
            return seed;
        }

        ProsequorModSystem? mod = ProsequorModSystem.For(world.Api);
        if (mod?.Pipeline == null)
        {
            return seed;
        }

        float qty = mod.Pipeline.Run(
            HookIds.BlockInteraction,
            VerbIds.MutateProcess,
            HookIds.Quantity,
            context,
            seed);
        if (!float.IsFinite(qty) || qty < 0f)
        {
            return 0f;
        }

        return qty;
    }

    static bool TryCreateContext(
        IWorldAccessor world,
        string playerUid,
        ItemSlot outputSlot,
        string verb,
        out MutateProcessContext context)
    {
        context = null!;
        ProsequorModSystem? mod = ProsequorModSystem.For(world.Api);
        IPlayerProgress? progress = ProsequorModSystem.GetProgress(world.Api, playerUid);
        if (mod?.Pipeline == null
            || mod.Collections?.Index == null
            || mod.VariantTable == null
            || progress == null)
        {
            return false;
        }

        string classToken = verb;
        if (classToken.Contains(':'))
        {
            int colon = classToken.LastIndexOf(':');
            classToken = classToken[(colon + 1)..];
        }

        IPlayer? player = world.PlayerByUid(playerUid);
        AbilityAction fact = EventFactBuilder.Build(
            VerbIds.MutateProcess.Value,
            playerUid,
            held: EventFactBuilder.HeldCode(player),
            target: EventFactBuilder.CodeOf(outputSlot.Itemstack),
            lastCraft: EventFactBuilder.LastCraftCode(playerUid),
            tokens: string.IsNullOrWhiteSpace(classToken) ? null : [classToken]);

        context = new MutateProcessContext
        {
            World = world,
            OutputSlot = outputSlot,
            Player = player,
            Progress = progress,
            Fact = fact,
            Tags = mod.Collections.Index,
            Variants = mod.VariantTable
        };
        return true;
    }
}
