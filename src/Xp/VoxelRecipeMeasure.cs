namespace Prosequor.Xp;

/// <summary>Voxels-per-unit rows for one craft's recipe catalog.</summary>
public interface IVoxelPerUnitCatalog
{
    int MinVoxelsPerUnit { get; }

    int MaxVoxelsPerUnit { get; }

    bool TryGetVoxelsPerUnit(string? recipeKey, out int voxelsPerUnit);
}

/// <summary>
/// Resolves a stamped recipe key to voxels-per-unit and that craft's lerp span.
/// Clay wins when the same key exists in more than one catalog.
/// </summary>
public static class VoxelRecipeMeasure
{
    public static bool TryResolve(
        string? recipeKey,
        IVoxelPerUnitCatalog? clay,
        IVoxelPerUnitCatalog? smithing,
        IVoxelPerUnitCatalog? knapping,
        out int voxelsPerUnit,
        out int minVoxelsPerUnit,
        out int maxVoxelsPerUnit)
    {
        if (TryHit(clay, recipeKey, out voxelsPerUnit, out minVoxelsPerUnit, out maxVoxelsPerUnit)
            || TryHit(smithing, recipeKey, out voxelsPerUnit, out minVoxelsPerUnit, out maxVoxelsPerUnit)
            || TryHit(knapping, recipeKey, out voxelsPerUnit, out minVoxelsPerUnit, out maxVoxelsPerUnit))
        {
            return true;
        }

        voxelsPerUnit = 0;
        minVoxelsPerUnit = 0;
        maxVoxelsPerUnit = 0;
        return false;
    }

    static bool TryHit(
        IVoxelPerUnitCatalog? catalog,
        string? recipeKey,
        out int voxelsPerUnit,
        out int minVoxelsPerUnit,
        out int maxVoxelsPerUnit)
    {
        voxelsPerUnit = 0;
        minVoxelsPerUnit = 0;
        maxVoxelsPerUnit = 0;
        if (catalog == null || !catalog.TryGetVoxelsPerUnit(recipeKey, out int perUnit))
        {
            return false;
        }

        voxelsPerUnit = perUnit;
        minVoxelsPerUnit = catalog.MinVoxelsPerUnit;
        maxVoxelsPerUnit = catalog.MaxVoxelsPerUnit;
        return true;
    }
}
