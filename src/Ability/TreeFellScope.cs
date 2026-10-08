using Vintagestory.API.MathTools;

namespace Prosequor.Ability;

/// <summary>
/// Positions broken by the current axe tree fell. Open only around
/// <c>ItemAxe.OnBlockBrokenWith</c>, so <c>FindTree</c> calls during the swing
/// do not mark drops. Nested fells keep their own set.
/// </summary>
public static class TreeFellScope
{
    /// <summary>Mutate-drops fact token for a block broken as part of felling a tree.</summary>
    public const string Token = "felled";

    [ThreadStatic]
    static Stack<HashSet<BlockPos>>? scopes;

    public static void Begin()
    {
        scopes ??= new Stack<HashSet<BlockPos>>();
        scopes.Push(new HashSet<BlockPos>());
    }

    public static void End()
    {
        if (scopes == null || scopes.Count == 0)
        {
            return;
        }

        scopes.Pop();
    }

    /// <summary>Record the walk from the active fell. No-op when no fell is open.</summary>
    public static void Note(IEnumerable<BlockPos>? positions)
    {
        if (positions == null || scopes == null || scopes.Count == 0)
        {
            return;
        }

        HashSet<BlockPos> set = scopes.Peek();
        foreach (BlockPos pos in positions)
        {
            if (pos != null)
            {
                set.Add(pos.Copy());
            }
        }
    }

    public static bool Contains(BlockPos? pos)
    {
        if (pos == null || scopes == null || scopes.Count == 0)
        {
            return false;
        }

        return scopes.Peek().Contains(pos);
    }
}
