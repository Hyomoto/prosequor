using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace Prosequor.Ability;

/// <summary>Thread-local player for anvil <c>OnHit</c> assists during <c>OnUseOver</c>.</summary>
public static class AnvilHitScope
{
    [ThreadStatic]
    static IPlayer? currentPlayer;

    public static IPlayer? CurrentPlayer => currentPlayer;

    public static void Begin(IPlayer player) => currentPlayer = player;

    public static void End() => currentPlayer = null;
}

/// <summary>
/// Hammer Mastery runs before vanilla <c>OnHit</c>: up to N misplaced voxels in the
/// 3×3 hit brush drop into nearer empty recipe cells (downward only). Heavy Hits slag
/// clear runs after.
/// </summary>
public static class AnvilHeavyHitOps
{
    public static void ApplyMastery(BlockEntityAnvil anvil, IPlayer player, Vec3i voxelPos)
    {
        if (anvil?.Voxels == null || player == null || voxelPos == null)
        {
            return;
        }

        int assistRadius = AnvilWorkStation.ResolveAssistRadius(player);
        int moveCount = AnvilWorkStation.ResolveMoveCount(player);
        if (assistRadius < 0 || moveCount <= 0)
        {
            return;
        }

        bool[,,]? recipe = anvil.recipeVoxels;
        int layers = anvil.SelectedRecipe?.QuantityLayers ?? 0;
        AnvilVoxelGrid.MoveTowardRecipe(
            anvil.Voxels,
            recipe,
            layers,
            voxelPos.X,
            voxelPos.Y,
            voxelPos.Z,
            assistRadius,
            moveCount);
    }

    public static void ApplySlagClear(BlockEntityAnvil anvil, IPlayer player, Vec3i voxelPos)
    {
        if (anvil?.Voxels == null || player == null || voxelPos == null)
        {
            return;
        }

        int slagRadius = AnvilWorkStation.ResolveSlagRadius(player);
        if (slagRadius < 0)
        {
            return;
        }

        AnvilVoxelGrid.ClearSlag(anvil.Voxels, voxelPos.X, voxelPos.Y, voxelPos.Z, slagRadius);
    }
}
