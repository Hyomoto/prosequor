using HarmonyLib;
using Prosequor.Xp.Adapters;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace Prosequor.Ability;

/// <summary>
/// Thread-scoped buffer for harvest paths that yield via
/// <see cref="BlockDropItemStack.GetNextItemStack"/> instead of returning a list.
/// Each rolled stack is run through per-stack quantity/stack; list-post runs on flush.
/// </summary>
public static class DropHarvestScope
{
    [ThreadStatic]
    static AbilityAction? activeFact;

    [ThreadStatic]
    static IPlayer? activePlayer;

    [ThreadStatic]
    static Block? activeBlock;

    [ThreadStatic]
    static BlockPos? activePos;

    [ThreadStatic]
    static List<ItemStack>? buffered;

    [ThreadStatic]
    static List<ItemStack>? originals;

    [ThreadStatic]
    static int depth;

    public static void Begin(IPlayer player, AbilityAction fact, Block block, BlockPos pos)
    {
        depth++;
        if (depth == 1)
        {
            activePlayer = player;
            activeFact = fact;
            activeBlock = block;
            activePos = pos.Copy();
            buffered = new List<ItemStack>();
            originals = new List<ItemStack>();
        }
    }

    public static void Pop()
    {
        depth--;
        if (depth > 0)
        {
            return;
        }

        depth = 0;
        try
        {
            if (activePlayer != null
                && activeFact != null
                && activeBlock != null
                && activePos != null
                && buffered != null
                && originals != null
                && activePlayer.Entity?.World is IWorldAccessor world
                && world.Side == EnumAppSide.Server)
            {
                ItemStack[] final = DropsStation.RunStacksOnly(
                    world,
                    activePlayer,
                    buffered.ToArray(),
                    activeFact,
                    originals,
                    activeBlock,
                    activePos);

                // Interact harvests that consume via GetNextItemStack already took the
                // vanilla stack; we cannot re-inject list appends into that path here.
                // Per-stack quantity/stack already mutated each buffered entry in place
                // via ApplyRolledStack. List-post appends (crop seed style) are given.
                if (final.Length > buffered.Count)
                {
                    for (int i = buffered.Count; i < final.Length; i++)
                    {
                        ItemStack extra = final[i];
                        if (extra == null || extra.StackSize <= 0)
                        {
                            continue;
                        }

                        if (!activePlayer.InventoryManager.TryGiveItemstack(extra, slotNotifyEffect: true))
                        {
                            world.SpawnItemEntity(extra, activePos.ToVec3d().Add(0.5, 0.5, 0.5));
                        }
                    }
                }

                HarvestXp.NotifyInteractHarvest(
                    world.Api,
                    activePlayer,
                    activeBlock,
                    activePos,
                    final);
            }
        }
        finally
        {
            activePlayer = null;
            activeFact = null;
            activeBlock = null;
            activePos = null;
            buffered = null;
            originals = null;
        }
    }

    /// <summary>
    /// Mutates <paramref name="stack"/> through quantity+stack and records it for list-post.
    /// </summary>
    public static void ApplyRolledStack(ref ItemStack? stack)
    {
        if (depth <= 0
            || activePlayer == null
            || activeFact == null
            || activeBlock == null
            || activePos == null
            || buffered == null
            || originals == null
            || stack == null)
        {
            return;
        }

        IWorldAccessor? world = activePlayer.Entity?.World;
        if (world == null || world.Side != EnumAppSide.Server)
        {
            return;
        }

        originals.Add(stack.Clone());
        ItemStack? next = DropsStation.RunOneStack(
            world,
            activePlayer,
            stack,
            activeFact,
            activeBlock,
            activePos);

        if (next == null || next.StackSize <= 0)
        {
            stack = null;
            return;
        }

        if (!ReferenceEquals(next, stack))
        {
            stack.SetFrom(next);
        }

        if (OwnerCredit.TryResolvePlanter(world, activeBlock, activePos, out string? planterUid))
        {
            OwnerCredit.StampGrownBy(stack, planterUid);
        }

        buffered.Add(stack.Clone());
    }
}

/// <summary>
/// Applies <see cref="DropHarvestScope"/> to vanilla drop rolls used by berry / fruit-tree
/// interact harvest (and similar non-GetDrops paths).
/// </summary>
[HarmonyPatch(typeof(BlockDropItemStack), nameof(BlockDropItemStack.GetNextItemStack))]
public static class BlockDropItemStackHarvestScopePatch
{
    [HarmonyPostfix]
    public static void Postfix(ref ItemStack? __result)
    {
        DropHarvestScope.ApplyRolledStack(ref __result);
    }
}
