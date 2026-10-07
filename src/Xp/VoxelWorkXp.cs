using Vintagestory.API.Common;

namespace Prosequor.Xp;

/// <summary>
/// High-water mark for one workpiece and one recipe.
/// <see cref="Note"/> records the grid before an edit. <see cref="Apply"/> pays voxels added since.
/// </summary>
public static class VoxelWorkXp
{
    public readonly record struct Mark(int HighWater, string? RecipeKey);

    public static string? RecipeKeyOf(AssetLocation? name)
    {
        if (name == null)
        {
            return null;
        }

        string key = name.ToShortString();
        return string.IsNullOrWhiteSpace(key) ? null : key;
    }

    /// <summary>
    /// Bind <paramref name="currentKey"/> to the correct-voxel count already on the grid.
    /// The same recipe leaves the mark where it is, so a later <see cref="Apply"/> can pay the edit.
    /// </summary>
    public static Mark Note(int good, string? currentKey, Mark mark)
    {
        currentKey = string.IsNullOrWhiteSpace(currentKey) ? null : currentKey.Trim();
        if (currentKey == null)
        {
            return mark;
        }

        string? stored = string.IsNullOrWhiteSpace(mark.RecipeKey) ? null : mark.RecipeKey.Trim();
        if (stored != null && string.Equals(stored, currentKey, StringComparison.OrdinalIgnoreCase))
        {
            return mark;
        }

        return new Mark(Math.Max(0, good), currentKey);
    }

    /// <summary>
    /// Pay newly reached correct voxels and advance the mark. Returns how many to award.
    /// </summary>
    public static int Apply(int good, string? currentKey, Mark mark, out Mark next)
    {
        int paid = VoxelWorkXpMath.TakeDelta(
            good,
            mark.HighWater,
            mark.RecipeKey,
            currentKey,
            out int highWater,
            out string? recipeKey);
        next = new Mark(highWater, recipeKey);
        return paid;
    }
}
