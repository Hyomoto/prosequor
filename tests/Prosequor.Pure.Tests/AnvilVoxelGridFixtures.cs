using Prosequor.Ability;
using Xunit;

namespace Prosequor.Pure.Tests;

/// <summary>Pure anvil voxel-grid math for Heavy Hits / Hammer Mastery.</summary>
public static class AnvilVoxelGridFixtures
{
    public static void VerifyAll()
    {
        ClearSlag_Should_ClearWithinChebyshevRadius();
        ClearSlag_Should_NoOpWhenRadiusOff();
        ClearSlag_Should_AllowHeavyHitOnSlagCell();
        MoveTowardRecipe_Should_DropBrushMetalDownIntoRecipe();
        MoveTowardRecipe_Should_AllowLateralOffsetWhenGoingDown();
        MoveTowardRecipe_Should_NotTeleportSameY();
        MoveTowardRecipe_Should_IgnoreMetalOutsideHitBrush();
        MoveTowardRecipe_Should_RespectMoveCount();
        MoveTowardRecipe_Should_PreferCloserDest();
    }

    static byte[,,] EmptyGrid() =>
        new byte[AnvilVoxelGrid.SizeX, AnvilVoxelGrid.SizeY, AnvilVoxelGrid.SizeZ];

    static bool[,,] EmptyRecipe() =>
        new bool[AnvilVoxelGrid.SizeX, AnvilVoxelGrid.SizeY, AnvilVoxelGrid.SizeZ];

    static void ClearSlag_Should_ClearWithinChebyshevRadius()
    {
        byte[,,] voxels = EmptyGrid();
        voxels[8, 2, 8] = AnvilVoxelGrid.Slag;
        voxels[10, 2, 8] = AnvilVoxelGrid.Slag;
        voxels[12, 2, 8] = AnvilVoxelGrid.Slag;

        int cleared = AnvilVoxelGrid.ClearSlag(voxels, 8, 2, 8, radius: 2);
        if (cleared < 2 || voxels[8, 2, 8] != AnvilVoxelGrid.Empty || voxels[10, 2, 8] != AnvilVoxelGrid.Empty)
        {
            Assert.Fail(
                $"[prosequor] ClearSlag radius-2 failed. cleared={cleared} hit={voxels[8, 2, 8]} near={voxels[10, 2, 8]} far={voxels[12, 2, 8]}.");
        }

        if (voxels[12, 2, 8] != AnvilVoxelGrid.Slag)
        {
            Assert.Fail("[prosequor] ClearSlag should leave slag outside radius.");
        }
    }

    static void ClearSlag_Should_NoOpWhenRadiusOff()
    {
        byte[,,] voxels = EmptyGrid();
        voxels[4, 1, 4] = AnvilVoxelGrid.Slag;
        int cleared = AnvilVoxelGrid.ClearSlag(voxels, 4, 1, 4, AnvilVoxelGrid.RadiusOff);
        if (cleared != 0 || voxels[4, 1, 4] != AnvilVoxelGrid.Slag)
        {
            Assert.Fail("[prosequor] ClearSlag radius-off should not mutate.");
        }
    }

    static void ClearSlag_Should_AllowHeavyHitOnSlagCell()
    {
        byte[,,] voxels = EmptyGrid();
        voxels[5, 0, 5] = AnvilVoxelGrid.Slag;
        voxels[6, 0, 5] = AnvilVoxelGrid.Slag;
        int cleared = AnvilVoxelGrid.ClearSlag(voxels, 5, 0, 5, radius: 1);
        if (cleared != 2
            || voxels[5, 0, 5] != AnvilVoxelGrid.Empty
            || voxels[6, 0, 5] != AnvilVoxelGrid.Empty)
        {
            Assert.Fail($"[prosequor] Slag-cell heavy hit clear failed. cleared={cleared}.");
        }
    }

    static void MoveTowardRecipe_Should_DropBrushMetalDownIntoRecipe()
    {
        byte[,,] voxels = EmptyGrid();
        bool[,,] recipe = EmptyRecipe();
        recipe[8, 0, 8] = true;
        voxels[8, 1, 8] = AnvilVoxelGrid.Metal;

        int moved = AnvilVoxelGrid.MoveTowardRecipe(
            voxels,
            recipe,
            recipeLayers: 2,
            sx: 8,
            sy: 1,
            sz: 8,
            radius: 2,
            moveCount: 1);

        if (moved != 1
            || voxels[8, 1, 8] != AnvilVoxelGrid.Empty
            || voxels[8, 0, 8] != AnvilVoxelGrid.Metal)
        {
            Assert.Fail(
                $"[prosequor] Downward mastery drop failed. moved={moved} src={voxels[8, 1, 8]} dst={voxels[8, 0, 8]}.");
        }
    }

    static void MoveTowardRecipe_Should_AllowLateralOffsetWhenGoingDown()
    {
        byte[,,] voxels = EmptyGrid();
        bool[,,] recipe = EmptyRecipe();
        recipe[10, 0, 8] = true;
        voxels[8, 1, 8] = AnvilVoxelGrid.Metal;

        int moved = AnvilVoxelGrid.MoveTowardRecipe(
            voxels,
            recipe,
            recipeLayers: 2,
            sx: 8,
            sy: 1,
            sz: 8,
            radius: 2,
            moveCount: 1);

        if (moved != 1
            || voxels[8, 1, 8] != AnvilVoxelGrid.Empty
            || voxels[10, 0, 8] != AnvilVoxelGrid.Metal)
        {
            Assert.Fail(
                $"[prosequor] Diagonal-down mastery failed. moved={moved} src={voxels[8, 1, 8]} dst={voxels[10, 0, 8]}.");
        }
    }

    static void MoveTowardRecipe_Should_NotTeleportSameY()
    {
        // X A X O on same layer: striking A must not jump metal to O.
        byte[,,] voxels = EmptyGrid();
        bool[,,] recipe = EmptyRecipe();
        recipe[11, 1, 8] = true;
        voxels[8, 1, 8] = AnvilVoxelGrid.Metal;

        int moved = AnvilVoxelGrid.MoveTowardRecipe(
            voxels,
            recipe,
            recipeLayers: 2,
            sx: 8,
            sy: 1,
            sz: 8,
            radius: 3,
            moveCount: 4);

        if (moved != 0
            || voxels[8, 1, 8] != AnvilVoxelGrid.Metal
            || voxels[11, 1, 8] != AnvilVoxelGrid.Empty)
        {
            Assert.Fail("[prosequor] Same-Y lateral mastery teleport must not occur.");
        }
    }

    static void MoveTowardRecipe_Should_IgnoreMetalOutsideHitBrush()
    {
        byte[,,] voxels = EmptyGrid();
        bool[,,] recipe = EmptyRecipe();
        recipe[8, 0, 8] = true;
        // Two cells east of strike — outside 3×3 brush.
        voxels[10, 1, 8] = AnvilVoxelGrid.Metal;

        int moved = AnvilVoxelGrid.MoveTowardRecipe(
            voxels,
            recipe,
            recipeLayers: 2,
            sx: 8,
            sy: 1,
            sz: 8,
            radius: 3,
            moveCount: 4);

        if (moved != 0 || voxels[10, 1, 8] != AnvilVoxelGrid.Metal)
        {
            Assert.Fail("[prosequor] Mastery sources are the 3×3 hit brush only.");
        }
    }

    static void MoveTowardRecipe_Should_RespectMoveCount()
    {
        byte[,,] voxels = EmptyGrid();
        bool[,,] recipe = EmptyRecipe();
        recipe[7, 0, 8] = true;
        recipe[8, 0, 8] = true;
        recipe[9, 0, 8] = true;
        voxels[7, 1, 8] = AnvilVoxelGrid.Metal;
        voxels[8, 1, 8] = AnvilVoxelGrid.Metal;
        voxels[9, 1, 8] = AnvilVoxelGrid.Metal;

        int moved = AnvilVoxelGrid.MoveTowardRecipe(
            voxels,
            recipe,
            recipeLayers: 2,
            sx: 8,
            sy: 1,
            sz: 8,
            radius: 2,
            moveCount: 2);

        int onRecipe = 0;
        if (voxels[7, 0, 8] == AnvilVoxelGrid.Metal)
        {
            onRecipe++;
        }

        if (voxels[8, 0, 8] == AnvilVoxelGrid.Metal)
        {
            onRecipe++;
        }

        if (voxels[9, 0, 8] == AnvilVoxelGrid.Metal)
        {
            onRecipe++;
        }

        if (moved != 2 || onRecipe != 2)
        {
            Assert.Fail($"[prosequor] Move count cap failed. moved={moved} onRecipe={onRecipe}.");
        }
    }

    static void MoveTowardRecipe_Should_PreferCloserDest()
    {
        byte[,,] voxels = EmptyGrid();
        bool[,,] recipe = EmptyRecipe();
        recipe[8, 0, 8] = true;
        recipe[10, 0, 8] = true;
        voxels[8, 1, 8] = AnvilVoxelGrid.Metal;

        int moved = AnvilVoxelGrid.MoveTowardRecipe(
            voxels,
            recipe,
            recipeLayers: 2,
            sx: 8,
            sy: 1,
            sz: 8,
            radius: 2,
            moveCount: 1);

        if (moved != 1
            || voxels[8, 0, 8] != AnvilVoxelGrid.Metal
            || voxels[10, 0, 8] != AnvilVoxelGrid.Empty)
        {
            Assert.Fail("[prosequor] Mastery should prefer the nearer downward recipe cell.");
        }
    }
}
