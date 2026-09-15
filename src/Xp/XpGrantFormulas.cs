namespace Prosequor.Xp;

/// <summary>
/// Pure grant math helpers for craft adapters (consume count / units-per-craft).
/// Amount × quantity is resolved in <see cref="Activity.Deed.ResolveGrant"/>.
/// </summary>
public static class XpGrantFormulas
{
    /// <summary>
    /// How many recipe completions a take moved when MovedQuantity is trustworthy
    /// (single craft). Prefer <see cref="CraftTakeScope.ConsumeCount"/> for shift multi-craft —
    /// vanilla <c>CraftMany</c> leaves MovedQuantity as the last iteration only.
    /// </summary>
    public static int CraftCount(int movedQuantity, int outputStackSizePerCraft)
    {
        if (movedQuantity <= 0)
        {
            return 0;
        }

        int perCraft = Math.Max(1, outputStackSizePerCraft);
        int count = movedQuantity / perCraft;
        return count > 0 ? count : 1;
    }

    /// <summary>
    /// Resolves craft repetitions: consume-based count wins when present (multi-craft).
    /// </summary>
    public static int ResolveCraftCount(int consumeCount, int movedQuantity, int outputStackSizePerCraft)
    {
        if (consumeCount > 0)
        {
            return consumeCount;
        }

        return CraftCount(movedQuantity, outputStackSizePerCraft);
    }

    /// <summary>Ingredient units consumed attributed to one craft completion.</summary>
    public static int UnitsPerCraft(int unitsConsumed, int craftCount)
    {
        int reps = Math.Max(1, craftCount);
        int consumed = Math.Max(0, unitsConsumed);
        return consumed / reps;
    }
}
