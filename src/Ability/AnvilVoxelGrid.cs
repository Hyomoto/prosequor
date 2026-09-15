namespace Prosequor.Ability;

/// <summary>
/// Pure anvil voxel helpers (16×6×16). Materials match <c>EnumVoxelMaterial</c>:
/// Empty=0, Metal=1, Slag=2.
/// </summary>
public static class AnvilVoxelGrid
{
    public const int SizeX = 16;
    public const int SizeY = 6;
    public const int SizeZ = 16;

    public const byte Empty = 0;
    public const byte Metal = 1;
    public const byte Slag = 2;

    /// <summary>Seed / unset: skill inactive.</summary>
    public const int RadiusOff = -1;

    /// <summary>Vanilla heavy-hit brush half-extent (3×3 on the strike layer).</summary>
    public const int HitBrushRadius = 1;

    public static bool InBounds(int x, int y, int z) =>
        x >= 0 && x < SizeX && y >= 0 && y < SizeY && z >= 0 && z < SizeZ;

    public static int Chebyshev(int ax, int ay, int az, int bx, int by, int bz)
    {
        int dx = Math.Abs(ax - bx);
        int dy = Math.Abs(ay - by);
        int dz = Math.Abs(az - bz);
        return Math.Max(dx, Math.Max(dy, dz));
    }

    /// <summary>
    /// Clears slag within Chebyshev <paramref name="radius"/> of the strike (full 3D).
    /// No-op when <paramref name="radius"/> &lt; 0. Returns voxels cleared.
    /// </summary>
    public static int ClearSlag(byte[,,] voxels, int sx, int sy, int sz, int radius)
    {
        if (voxels == null || radius < 0 || !InBounds(sx, sy, sz))
        {
            return 0;
        }

        int cleared = 0;
        ForEachInRadius(sx, sy, sz, radius, yMin: 0, yMax: SizeY - 1, (x, y, z) =>
        {
            if (voxels[x, y, z] == Slag)
            {
                voxels[x, y, z] = Empty;
                cleared++;
            }
        });
        return cleared;
    }

    /// <summary>
    /// Hammer Mastery: of misplaced metal in the vanilla 3×3 hit brush (strike Y),
    /// place up to <paramref name="moveCount"/> into empty recipe cells within
    /// Chebyshev <paramref name="radius"/> of the strike, only when
    /// <c>dest.Y &lt; source.Y</c> (downward; X/Z may change). Prefers nearer dests.
    /// Returns moves performed.
    /// </summary>
    public static int MoveTowardRecipe(
        byte[,,] voxels,
        bool[,,]? recipe,
        int recipeLayers,
        int sx,
        int sy,
        int sz,
        int radius,
        int moveCount)
    {
        if (voxels == null
            || recipe == null
            || radius < 0
            || moveCount <= 0
            || !InBounds(sx, sy, sz))
        {
            return 0;
        }

        int layers = Math.Clamp(recipeLayers, 0, SizeY);
        List<VoxelRef> sources = new();

        for (int dx = -HitBrushRadius; dx <= HitBrushRadius; dx++)
        {
            for (int dz = -HitBrushRadius; dz <= HitBrushRadius; dz++)
            {
                int x = sx + dx;
                int z = sz + dz;
                if (!InBounds(x, sy, z) || voxels[x, sy, z] != Metal)
                {
                    continue;
                }

                bool want = sy < layers && recipe[x, sy, z];
                if (want)
                {
                    continue;
                }

                sources.Add(new VoxelRef(x, sy, z, Chebyshev(sx, sy, sz, x, sy, z)));
            }
        }

        if (sources.Count == 0)
        {
            return 0;
        }

        List<VoxelRef> dests = new();
        ForEachInRadius(sx, sy, sz, radius, yMin: 0, yMax: Math.Min(SizeY - 1, layers - 1), (x, y, z) =>
        {
            if (y >= layers || !recipe[x, y, z] || voxels[x, y, z] != Empty)
            {
                return;
            }

            dests.Add(new VoxelRef(x, y, z, Chebyshev(sx, sy, sz, x, y, z)));
        });

        if (dests.Count == 0)
        {
            return 0;
        }

        sources.Sort(static (a, b) => a.Dist.CompareTo(b.Dist));

        int moved = 0;
        bool[] destUsed = new bool[dests.Count];
        for (int s = 0; s < sources.Count && moved < moveCount; s++)
        {
            VoxelRef src = sources[s];
            if (voxels[src.X, src.Y, src.Z] != Metal)
            {
                continue;
            }

            int best = -1;
            int bestDist = int.MaxValue;
            for (int d = 0; d < dests.Count; d++)
            {
                if (destUsed[d])
                {
                    continue;
                }

                VoxelRef dest = dests[d];
                if (dest.Y >= src.Y)
                {
                    continue;
                }

                int dist = Chebyshev(src.X, src.Y, src.Z, dest.X, dest.Y, dest.Z);
                if (dist < bestDist)
                {
                    bestDist = dist;
                    best = d;
                }
            }

            if (best < 0)
            {
                continue;
            }

            VoxelRef chosen = dests[best];
            destUsed[best] = true;
            voxels[src.X, src.Y, src.Z] = Empty;
            voxels[chosen.X, chosen.Y, chosen.Z] = Metal;
            moved++;
        }

        return moved;
    }

    /// <summary>
    /// Bits Forging: place up to <paramref name="count"/> metal voxels resting on metal.
    /// Aim is a solid metal support cell; the first voxel goes directly above it
    /// (<c>Y ≥ 1</c>). Remaining voxels pick the nearest empty cells that also have
    /// metal below (Chebyshev to the first place). All-or-nothing: returns 0 and
    /// leaves the grid unchanged when fewer than <paramref name="count"/> fit.
    /// </summary>
    public static int TryPlaceBitVoxels(
        byte[,,] voxels,
        int supportX,
        int supportY,
        int supportZ,
        int count)
    {
        if (voxels == null
            || count <= 0
            || !InBounds(supportX, supportY, supportZ)
            || voxels[supportX, supportY, supportZ] != Metal)
        {
            return 0;
        }

        int firstX = supportX;
        int firstY = supportY + 1;
        int firstZ = supportZ;
        if (!IsSupportedEmpty(voxels, firstX, firstY, firstZ))
        {
            return 0;
        }

        if (count == 1)
        {
            voxels[firstX, firstY, firstZ] = Metal;
            return 1;
        }

        List<VoxelRef> extras = new();
        for (int y = 1; y < SizeY; y++)
        {
            for (int x = 0; x < SizeX; x++)
            {
                for (int z = 0; z < SizeZ; z++)
                {
                    if (x == firstX && y == firstY && z == firstZ)
                    {
                        continue;
                    }

                    if (!IsSupportedEmpty(voxels, x, y, z))
                    {
                        continue;
                    }

                    extras.Add(new VoxelRef(x, y, z, Chebyshev(firstX, firstY, firstZ, x, y, z)));
                }
            }
        }

        extras.Sort(static (a, b) => a.Dist.CompareTo(b.Dist));
        int need = count - 1;
        if (extras.Count < need)
        {
            return 0;
        }

        voxels[firstX, firstY, firstZ] = Metal;
        for (int i = 0; i < need; i++)
        {
            VoxelRef cell = extras[i];
            voxels[cell.X, cell.Y, cell.Z] = Metal;
        }

        return count;
    }

    /// <summary>Empty cell at Y≥1 with metal directly below.</summary>
    public static bool IsSupportedEmpty(byte[,,] voxels, int x, int y, int z)
    {
        if (voxels == null || y < 1 || !InBounds(x, y, z) || voxels[x, y, z] != Empty)
        {
            return false;
        }

        return voxels[x, y - 1, z] == Metal;
    }

    /// <summary>
    /// Iterates voxels in Chebyshev ball, clipped to the grid and optional Y band.
    /// </summary>
    public static void ForEachInRadius(
        int sx,
        int sy,
        int sz,
        int radius,
        int yMin,
        int yMax,
        Action<int, int, int> visit)
    {
        if (radius < 0 || visit == null || yMax < yMin)
        {
            return;
        }

        int x0 = Math.Max(0, sx - radius);
        int x1 = Math.Min(SizeX - 1, sx + radius);
        int y0 = Math.Max(0, Math.Max(yMin, sy - radius));
        int y1 = Math.Min(SizeY - 1, Math.Min(yMax, sy + radius));
        int z0 = Math.Max(0, sz - radius);
        int z1 = Math.Min(SizeZ - 1, sz + radius);

        for (int y = y0; y <= y1; y++)
        {
            for (int x = x0; x <= x1; x++)
            {
                for (int z = z0; z <= z1; z++)
                {
                    if (Chebyshev(sx, sy, sz, x, y, z) <= radius)
                    {
                        visit(x, y, z);
                    }
                }
            }
        }
    }

    readonly struct VoxelRef
    {
        public readonly int X;
        public readonly int Y;
        public readonly int Z;
        public readonly int Dist;

        public VoxelRef(int x, int y, int z, int dist)
        {
            X = x;
            Y = y;
            Z = z;
            Dist = dist;
        }
    }
}
