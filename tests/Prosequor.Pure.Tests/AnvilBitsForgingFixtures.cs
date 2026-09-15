using Prosequor.Ability;
using Xunit;

namespace Prosequor.Pure.Tests;

/// <summary>Pure Bits Forging placement rules (no world / anvil).</summary>
public static class AnvilBitsForgingFixtures
{
    public static void VerifyAll()
    {
        Place_Should_PutAboveMetalSupport();
        Place_Should_RejectY0();
        Place_Should_RejectFloating();
        Place_Should_PreferNearestSupportedExtras();
        Place_Should_AllOrNothingWhenNoRoom();
        Place_Should_IgnoreSlagAsSupport();
    }

    static byte[,,] EmptyGrid() =>
        new byte[AnvilVoxelGrid.SizeX, AnvilVoxelGrid.SizeY, AnvilVoxelGrid.SizeZ];

    static void Place_Should_PutAboveMetalSupport()
    {
        byte[,,] voxels = EmptyGrid();
        voxels[8, 0, 8] = AnvilVoxelGrid.Metal;

        int placed = AnvilVoxelGrid.TryPlaceBitVoxels(voxels, 8, 0, 8, count: 1);
        if (placed != 1 || voxels[8, 1, 8] != AnvilVoxelGrid.Metal)
        {
            Assert.Fail($"[prosequor] Expected place above support. placed={placed} cell={voxels[8, 1, 8]}.");
        }
    }

    static void Place_Should_RejectY0()
    {
        // No metal at y=-1; cannot place at y=0 as a "supported" cell.
        byte[,,] voxels = EmptyGrid();
        voxels[4, 0, 4] = AnvilVoxelGrid.Metal;
        // Aiming the metal itself tries place at y=1 — OK. Force IsSupportedEmpty at y=0:
        if (AnvilVoxelGrid.IsSupportedEmpty(voxels, 5, 0, 4))
        {
            Assert.Fail("[prosequor] Y=0 must never count as supported empty.");
        }
    }

    static void Place_Should_RejectFloating()
    {
        byte[,,] voxels = EmptyGrid();
        voxels[8, 0, 8] = AnvilVoxelGrid.Metal;
        // Aim metal but block the cell above with slag so place fails; empty neighbor has no support.
        voxels[8, 1, 8] = AnvilVoxelGrid.Slag;

        int placed = AnvilVoxelGrid.TryPlaceBitVoxels(voxels, 8, 0, 8, count: 1);
        if (placed != 0)
        {
            Assert.Fail("[prosequor] Blocked aim-above must not place floating voxels.");
        }
    }

    static void Place_Should_PreferNearestSupportedExtras()
    {
        byte[,,] voxels = EmptyGrid();
        voxels[8, 0, 8] = AnvilVoxelGrid.Metal;
        voxels[9, 0, 8] = AnvilVoxelGrid.Metal;
        voxels[12, 0, 8] = AnvilVoxelGrid.Metal;

        int placed = AnvilVoxelGrid.TryPlaceBitVoxels(voxels, 8, 0, 8, count: 2);
        if (placed != 2
            || voxels[8, 1, 8] != AnvilVoxelGrid.Metal
            || voxels[9, 1, 8] != AnvilVoxelGrid.Metal
            || voxels[12, 1, 8] != AnvilVoxelGrid.Empty)
        {
            Assert.Fail(
                $"[prosequor] Extra voxel should prefer nearer support. near={voxels[9, 1, 8]} far={voxels[12, 1, 8]}.");
        }
    }

    static void Place_Should_AllOrNothingWhenNoRoom()
    {
        byte[,,] voxels = EmptyGrid();
        voxels[8, 0, 8] = AnvilVoxelGrid.Metal;
        // Only one supported empty (above aim); count 2 must leave grid untouched.
        int placed = AnvilVoxelGrid.TryPlaceBitVoxels(voxels, 8, 0, 8, count: 2);
        if (placed != 0 || voxels[8, 1, 8] != AnvilVoxelGrid.Empty)
        {
            Assert.Fail($"[prosequor] All-or-nothing failed. placed={placed} above={voxels[8, 1, 8]}.");
        }
    }

    static void Place_Should_IgnoreSlagAsSupport()
    {
        byte[,,] voxels = EmptyGrid();
        voxels[8, 0, 8] = AnvilVoxelGrid.Slag;
        int placed = AnvilVoxelGrid.TryPlaceBitVoxels(voxels, 8, 0, 8, count: 1);
        if (placed != 0 || AnvilVoxelGrid.IsSupportedEmpty(voxels, 8, 1, 8))
        {
            Assert.Fail("[prosequor] Slag must not count as metal support.");
        }
    }
}
