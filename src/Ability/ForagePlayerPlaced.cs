using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace Prosequor.Ability;

/// <summary>
/// Player-placed forage marks. Ground storage is always placed.
/// Blocks without a block entity (loose sticks, mushrooms, reeds) persist a
/// packed local position in chunk moddata. Worldgen never writes the mark.
/// </summary>
public static class ForagePlayerPlaced
{
    public const string ModDataKey = "prosequorPlayerPlaced";

    public static bool IsPlaced(IWorldAccessor? world, BlockPos? pos)
    {
        if (world == null || pos == null)
        {
            return false;
        }

        if (world.BlockAccessor.GetBlockEntity(pos) is BlockEntityGroundStorage)
        {
            return true;
        }

        return IsChunkMarked(world, pos);
    }

    public static void Mark(IWorldAccessor? world, BlockPos? pos)
    {
        if (world?.Side != EnumAppSide.Server || pos == null)
        {
            return;
        }

        Mutate(world, pos, add: true);
    }

    public static void Clear(IWorldAccessor? world, BlockPos? pos)
    {
        if (world?.Side != EnumAppSide.Server || pos == null)
        {
            return;
        }

        Mutate(world, pos, add: false);
    }

    /// <summary>Drop the mark when the position no longer holds a placeable forage block.</summary>
    public static void ClearIfGone(IWorldAccessor? world, BlockPos? pos)
    {
        if (world == null || pos == null)
        {
            return;
        }

        Block now = world.BlockAccessor.GetBlock(pos);
        if (!ForageBlocks.IsPlayerPlaceableForage(now))
        {
            Clear(world, pos);
        }
    }

    /// <summary>
    /// Wild forage for XP and drop bonuses. Crops and berry bushes use planter
    /// pedigree; mushrooms, reeds, and loose sticks use the place mark.
    /// </summary>
    public static bool IsWild(IWorldAccessor? world, Block? block, BlockPos? pos)
    {
        if (world == null || block == null || block.Id == 0 || pos == null)
        {
            return false;
        }

        if (world.BlockAccessor.GetBlockEntity(pos) is BlockEntityGroundStorage)
        {
            return false;
        }

        if (AbilityBootstrap.IsCropBlock(block) || AbilityBootstrap.IsBerryBushBlock(block))
        {
            return !OwnerCredit.TryResolvePlanter(world, block, pos, out _);
        }

        if (!ForageBlocks.IsPlayerPlaceableForage(block))
        {
            return false;
        }

        return !IsChunkMarked(world, pos);
    }

    static bool IsChunkMarked(IWorldAccessor world, BlockPos pos)
    {
        if (!TryRead(world, pos, out int[] marks, out _))
        {
            return false;
        }

        return Array.BinarySearch(marks, Pack(pos)) >= 0;
    }

    static void Mutate(IWorldAccessor world, BlockPos pos, bool add)
    {
        if (!TryRead(world, pos, out int[] marks, out IWorldChunk? chunk) || chunk == null)
        {
            return;
        }

        int packed = Pack(pos);
        int index = Array.BinarySearch(marks, packed);
        if (add)
        {
            if (index >= 0)
            {
                return;
            }

            int insert = ~index;
            int[] next = new int[marks.Length + 1];
            if (insert > 0)
            {
                Array.Copy(marks, 0, next, 0, insert);
            }

            next[insert] = packed;
            if (insert < marks.Length)
            {
                Array.Copy(marks, insert, next, insert + 1, marks.Length - insert);
            }

            Write(chunk, next);
            return;
        }

        if (index < 0)
        {
            return;
        }

        int[] trimmed = new int[marks.Length - 1];
        if (index > 0)
        {
            Array.Copy(marks, 0, trimmed, 0, index);
        }

        if (index < marks.Length - 1)
        {
            Array.Copy(marks, index + 1, trimmed, index, marks.Length - index - 1);
        }

        Write(chunk, trimmed);
    }

    static bool TryRead(IWorldAccessor world, BlockPos pos, out int[] marks, out IWorldChunk? chunk)
    {
        marks = Array.Empty<int>();
        chunk = world.BlockAccessor.GetChunkAtBlockPos(pos);
        if (chunk == null)
        {
            return false;
        }

        byte[]? raw = chunk.GetModdata(ModDataKey);
        if (raw == null || raw.Length < 4 || (raw.Length % 4) != 0)
        {
            return true;
        }

        int count = raw.Length / 4;
        marks = new int[count];
        Buffer.BlockCopy(raw, 0, marks, 0, raw.Length);
        Array.Sort(marks);
        return true;
    }

    static void Write(IWorldChunk chunk, int[] marks)
    {
        if (marks.Length == 0)
        {
            chunk.RemoveModdata(ModDataKey);
        }
        else
        {
            byte[] raw = new byte[marks.Length * 4];
            Buffer.BlockCopy(marks, 0, raw, 0, raw.Length);
            chunk.SetModdata(ModDataKey, raw);
        }

        chunk.MarkModified();
    }

    /// <summary>Local X/Z (5 bits) plus Y in the high 16 bits. Chunk-relative.</summary>
    public static int Pack(BlockPos pos) =>
        ((pos.Y & 0xFFFF) << 16) | ((pos.Z & 31) << 8) | (pos.X & 31);
}
