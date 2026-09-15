using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace Prosequor.Ability;

/// <summary>
/// Thread-scoped farmland nutrient snapshot for GetDrops. Prefix pushes while the BE
/// is still alive; drop-list actions read via <see cref="TryGet"/>.
/// </summary>
public static class FarmlandNutrientScope
{
    [ThreadStatic]
    static float[]? nutrients;

    [ThreadStatic]
    static int[]? originalFertility;

    [ThreadStatic]
    static int depth;

    public static void Push(float[] sourceNutrients, int[] sourceOriginalFertility)
    {
        depth++;
        nutrients = (float[])sourceNutrients.Clone();
        originalFertility = (int[])sourceOriginalFertility.Clone();
    }

    public static void Pop()
    {
        depth--;
        if (depth <= 0)
        {
            depth = 0;
            nutrients = null;
            originalFertility = null;
        }
    }

    public static bool TryGet(out float[] snapshotNutrients, out int[] snapshotOriginal)
    {
        if (nutrients != null
            && nutrients.Length > 0
            && originalFertility != null
            && originalFertility.Length > 0)
        {
            snapshotNutrients = nutrients;
            snapshotOriginal = originalFertility;
            return true;
        }

        snapshotNutrients = Array.Empty<float>();
        snapshotOriginal = Array.Empty<int>();
        return false;
    }

    /// <summary>
    /// When <paramref name="block"/> is farmland with a usable BE, push a nutrient
    /// snapshot and return true so the caller can Pop after drops finish.
    /// </summary>
    public static bool TryPushFromBlock(IWorldAccessor world, Block? block, BlockPos? pos)
    {
        if (world == null
            || pos == null
            || block == null
            || !AbilityBootstrap.IsFarmlandBlock(block))
        {
            return false;
        }

        if (world.BlockAccessor.GetBlockEntity(pos) is not IFarmlandBlockEntity farmland)
        {
            return false;
        }

        float[]? sourceNutrients = farmland.Nutrients;
        int[]? sourceOriginal = farmland.OriginalFertility;
        if (sourceNutrients == null
            || sourceNutrients.Length == 0
            || sourceOriginal == null
            || sourceOriginal.Length == 0)
        {
            return false;
        }

        Push(sourceNutrients, sourceOriginal);
        return true;
    }
}
