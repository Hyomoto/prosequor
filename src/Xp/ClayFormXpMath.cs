namespace Prosequor.Xp;

/// <summary>
/// Pure clay-form XP math: good-voxel count and high-water deltas.
/// A good voxel is a recipe-wanted cell that is currently filled.
/// </summary>
public static class ClayFormXpMath
{
    /// <summary>
    /// Count filled cells that the recipe wants within <paramref name="layers"/>.
    /// Extras (filled where recipe is empty) and empty recipe cells do not count.
    /// </summary>
    public static int CountGood(bool[,,] have, bool[,,] want, int layers)
    {
        if (have == null || want == null || layers <= 0)
        {
            return 0;
        }

        int xSize = Math.Min(have.GetLength(0), want.GetLength(0));
        int ySize = Math.Min(Math.Min(have.GetLength(1), want.GetLength(1)), layers);
        int zSize = Math.Min(have.GetLength(2), want.GetLength(2));
        int good = 0;
        for (int x = 0; x < xSize; x++)
        {
            for (int y = 0; y < ySize; y++)
            {
                for (int z = 0; z < zSize; z++)
                {
                    if (want[x, y, z] && have[x, y, z])
                    {
                        good++;
                    }
                }
            }
        }

        return good;
    }

    /// <summary>
    /// Anvil: recipe-wanted cells whose voxel is <paramref name="metalValue"/> (Metal).
    /// Slag, empty, and extras do not count.
    /// </summary>
    public static int CountGood(byte[,,] have, bool[,,] want, int layers, byte metalValue)
    {
        if (have == null || want == null || layers <= 0)
        {
            return 0;
        }

        int xSize = Math.Min(have.GetLength(0), want.GetLength(0));
        int ySize = Math.Min(Math.Min(have.GetLength(1), want.GetLength(1)), layers);
        int zSize = Math.Min(have.GetLength(2), want.GetLength(2));
        int good = 0;
        for (int x = 0; x < xSize; x++)
        {
            for (int y = 0; y < ySize; y++)
            {
                for (int z = 0; z < zSize; z++)
                {
                    if (want[x, y, z] && have[x, y, z] == metalValue)
                    {
                        good++;
                    }
                }
            }
        }

        return good;
    }

    /// <summary>
    /// Advance high-water for <paramref name="currentKey"/>. Recipe-key change resets the mark
    /// to <paramref name="good"/> and pays 0. Same key pays <c>max(0, good - highWater)</c>.
    /// </summary>
    public static int TakeDelta(
        int good,
        int highWater,
        string? storedKey,
        string? currentKey,
        out int newHighWater,
        out string? newRecipeKey)
    {
        good = Math.Max(0, good);
        currentKey = string.IsNullOrWhiteSpace(currentKey) ? null : currentKey.Trim();
        storedKey = string.IsNullOrWhiteSpace(storedKey) ? null : storedKey.Trim();
        newRecipeKey = currentKey;

        if (currentKey == null)
        {
            newHighWater = Math.Max(0, highWater);
            return 0;
        }

        // Recipe swap (not first bind): adopt current good, pay nothing.
        if (storedKey != null
            && !string.Equals(storedKey, currentKey, StringComparison.OrdinalIgnoreCase))
        {
            newHighWater = good;
            return 0;
        }

        int paid = Math.Max(0, good - Math.Max(0, highWater));
        newHighWater = Math.Max(Math.Max(0, highWater), good);
        return paid;
    }
}
