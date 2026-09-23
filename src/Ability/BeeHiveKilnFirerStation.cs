using System.Runtime.CompilerServices;
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace Prosequor.Ability;

/// <summary>
/// Beehive kiln session firer: last player to ignite fuel, frozen when heat starts,
/// cleared when heat ends. Stamps pottery in the chamber at session start.
/// Persists on <see cref="ProsequorChunkPedigree"/>; heat edge baseline is ephemeral.
/// </summary>
public static class BeeHiveKilnFirerStation
{
    public const string LastIgniterAttr = "prosequorKilnLastIgniter";
    public const string SessionFirerAttr = "prosequorKilnSessionFirer";

    static readonly ConditionalWeakTable<BlockEntity, EdgeBox> edges = new();
    static readonly AccessTools.FieldRef<BlockEntityBeeHiveKiln, bool>? ReceivesHeatField =
        AccessTools.FieldRefAccess<BlockEntityBeeHiveKiln, bool>("receivesHeat");
    static readonly AccessTools.FieldRef<BlockEntityBeeHiveKiln, BlockPos[]?>? ParticlePositionsField =
        AccessTools.FieldRefAccess<BlockEntityBeeHiveKiln, BlockPos[]?>("particlePositions");

    sealed class EdgeBox
    {
        public bool PrevReceivesHeat;
        public bool HavePrev;
    }

    public static void NoteIgniter(BlockEntityBeeHiveKiln kiln, IPlayer? player)
    {
        if (kiln?.Api?.Side != EnumAppSide.Server || player?.PlayerUID == null)
        {
            return;
        }

        ProsequorBlockPedigreeStation.Mutate(
            kiln,
            box => box.KilnLastIgniterUid = player.PlayerUID);
    }

    public static void NoteIgniterAtFuel(IWorldAccessor world, BlockPos fuelPos, IPlayer? player)
    {
        if (world?.Side != EnumAppSide.Server || player?.PlayerUID == null || fuelPos == null)
        {
            return;
        }

        BlockEntityBeeHiveKiln? kiln = FindKilnForFuel(world, fuelPos);
        if (kiln != null)
        {
            NoteIgniter(kiln, player);
        }
    }

    public static string? TryGetSessionFirerUid(BlockEntityBeeHiveKiln? kiln)
    {
        if (kiln != null
            && ProsequorBlockPedigreeStation.TryGetBox(kiln, out ProsequorChunkPedigree.Box box)
            && !string.IsNullOrWhiteSpace(box.KilnSessionFirerUid))
        {
            return box.KilnSessionFirerUid;
        }

        return null;
    }

    public static void EnsureFirerOnStack(BlockEntityBeeHiveKiln kiln, ItemStack? stack)
    {
        string? firerUid = TryGetSessionFirerUid(kiln);
        if (stack == null || string.IsNullOrWhiteSpace(firerUid))
        {
            return;
        }

        if (CraftAttribution.HasContributor(stack, firerUid))
        {
            return;
        }

        CraftAttribution.StampFirerUid(stack, firerUid);
    }

    /// <summary>Adding fuel under a beehive kiln contributes on chamber pottery.</summary>
    public static void ContributeChamberAtFuel(IWorldAccessor world, BlockPos fuelPos, IPlayer? player)
    {
        if (world?.Side != EnumAppSide.Server || player?.PlayerUID == null || fuelPos == null)
        {
            return;
        }

        BlockEntityBeeHiveKiln? kiln = FindKilnForFuel(world, fuelPos);
        if (kiln != null)
        {
            StampChamberContents(kiln, player.PlayerUID);
        }
    }

    /// <summary>After each server tick: detect heat session start/end.</summary>
    public static void OnAfterServerTick(BlockEntityBeeHiveKiln kiln)
    {
        if (kiln?.Api?.Side != EnumAppSide.Server || ReceivesHeatField == null)
        {
            return;
        }

        bool receivesHeat = ReceivesHeatField(kiln);
        EdgeBox edge = edges.GetOrCreateValue(kiln);
        if (!edge.HavePrev)
        {
            edge.PrevReceivesHeat = receivesHeat;
            edge.HavePrev = true;
            return;
        }

        if (!edge.PrevReceivesHeat && receivesHeat)
        {
            string? session = null;
            if (ProsequorBlockPedigreeStation.TryGetBox(kiln, out ProsequorChunkPedigree.Box box))
            {
                session = box.KilnLastIgniterUid;
            }

            ProsequorBlockPedigreeStation.Mutate(kiln, b => b.KilnSessionFirerUid = session);
            StampChamberContents(kiln, session);
        }
        else if (edge.PrevReceivesHeat && !receivesHeat)
        {
            ProsequorBlockPedigreeStation.Mutate(kiln, b => b.KilnSessionFirerUid = null);
        }

        edge.PrevReceivesHeat = receivesHeat;
    }

    static void StampChamberContents(BlockEntityBeeHiveKiln kiln, string? firerUid)
    {
        if (string.IsNullOrWhiteSpace(firerUid) || ParticlePositionsField == null)
        {
            return;
        }

        BlockPos[]? positions = ParticlePositionsField(kiln);
        if (positions == null || positions.Length < 9)
        {
            return;
        }

        for (int j = 0; j < 9; j++)
        {
            for (int i = 1; i < 4; i++)
            {
                BlockPos pos = positions[j].UpCopy(i);
                if (kiln.Api.World.BlockAccessor.GetBlockEntity(pos) is not BlockEntityGroundStorage storage)
                {
                    continue;
                }

                bool dirty = false;
                foreach (ItemSlot slot in storage.Inventory)
                {
                    if (slot.Itemstack == null)
                    {
                        continue;
                    }

                    CraftAttribution.StampFirerUid(slot.Itemstack, firerUid);
                    slot.MarkDirty();
                    dirty = true;
                }

                if (dirty)
                {
                    storage.MarkDirty(redrawOnClient: false);
                }
            }
        }
    }

    static BlockEntityBeeHiveKiln? FindKilnForFuel(IWorldAccessor world, BlockPos fuelPos)
    {
        BlockPos min = fuelPos.AddCopy(-5, -2, -5);
        BlockPos max = fuelPos.AddCopy(5, 4, 5);
        BlockEntityBeeHiveKiln? found = null;
        world.BlockAccessor.WalkBlocks(min, max, (block, x, y, z) =>
        {
            if (found != null)
            {
                return;
            }

            BlockPos pos = new(x, y, z, fuelPos.dimension);
            if (world.BlockAccessor.GetBlockEntity(pos) is not BlockEntityBeeHiveKiln kiln)
            {
                return;
            }

            if (IsFuelPosForKiln(kiln, fuelPos))
            {
                found = kiln;
            }
        });

        return found;
    }

    static bool IsFuelPosForKiln(BlockEntityBeeHiveKiln kiln, BlockPos fuelPos)
    {
        if (ParticlePositionsField == null)
        {
            return false;
        }

        BlockPos[]? positions = ParticlePositionsField(kiln);
        if (positions == null)
        {
            return false;
        }

        for (int j = 0; j < Math.Min(9, positions.Length); j++)
        {
            BlockPos expected = positions[j].DownCopy();
            if (expected.Equals(fuelPos))
            {
                return true;
            }
        }

        return false;
    }
}
