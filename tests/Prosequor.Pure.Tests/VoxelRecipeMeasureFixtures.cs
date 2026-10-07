using Prosequor.Xp;
using Xunit;

namespace Prosequor.Ability;

/// <summary>Voxel pay lookup: clay span, smithing span, and a miss.</summary>
public static class VoxelRecipeMeasureFixtures
{
    public static void VerifyAll()
    {
        VerifyClayKeyUsesClaySpan();
        VerifySmithingKeyUsesSmithingSpan();
        VerifyUnknownKeyMeasuresNothing();
        VerifyClayWinsASharedKey();
    }

    static void VerifyClayKeyUsesClaySpan()
    {
        MapCatalog clay = new(4, 40, new Dictionary<string, int> { ["clay:bowl"] = 12 });
        MapCatalog smith = new(20, 400, new Dictionary<string, int> { ["game:plate"] = 200 });
        if (!VoxelRecipeMeasure.TryResolve("clay:bowl", clay, smith, knapping: null, out int voxels, out int min, out int max)
            || voxels != 12
            || min != 4
            || max != 40)
        {
            Assert.Fail("[prosequor] Voxel measure fixture failed (clay key uses the clay span).");
        }
    }

    static void VerifySmithingKeyUsesSmithingSpan()
    {
        MapCatalog clay = new(4, 40, new Dictionary<string, int> { ["clay:bowl"] = 12 });
        MapCatalog smith = new(20, 400, new Dictionary<string, int> { ["game:plate"] = 200 });
        if (!VoxelRecipeMeasure.TryResolve("game:plate", clay, smith, knapping: null, out int voxels, out int min, out int max)
            || voxels != 200
            || min != 20
            || max != 400)
        {
            Assert.Fail("[prosequor] Voxel measure fixture failed (smithing key uses the smithing span).");
        }
    }

    static void VerifyUnknownKeyMeasuresNothing()
    {
        MapCatalog clay = new(4, 40, new Dictionary<string, int> { ["clay:bowl"] = 12 });
        if (VoxelRecipeMeasure.TryResolve("missing", clay, smithing: null, knapping: null, out _, out _, out _))
        {
            Assert.Fail("[prosequor] Voxel measure fixture failed (unknown key measures nothing).");
        }
    }

    static void VerifyClayWinsASharedKey()
    {
        MapCatalog clay = new(4, 40, new Dictionary<string, int> { ["shared"] = 8 });
        MapCatalog smith = new(20, 400, new Dictionary<string, int> { ["shared"] = 80 });
        if (!VoxelRecipeMeasure.TryResolve("shared", clay, smith, knapping: null, out int voxels, out int min, out int max)
            || voxels != 8
            || min != 4
            || max != 40)
        {
            Assert.Fail("[prosequor] Voxel measure fixture failed (clay wins a shared key).");
        }
    }

    sealed class MapCatalog : IVoxelPerUnitCatalog
    {
        readonly Dictionary<string, int> rows;

        public MapCatalog(int min, int max, Dictionary<string, int> rows)
        {
            MinVoxelsPerUnit = min;
            MaxVoxelsPerUnit = max;
            this.rows = rows;
        }

        public int MinVoxelsPerUnit { get; }

        public int MaxVoxelsPerUnit { get; }

        public bool TryGetVoxelsPerUnit(string? recipeKey, out int voxelsPerUnit)
        {
            voxelsPerUnit = 0;
            return recipeKey != null && rows.TryGetValue(recipeKey, out voxelsPerUnit);
        }
    }
}
