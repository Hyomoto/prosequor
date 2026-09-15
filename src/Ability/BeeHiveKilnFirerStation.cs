using System.Runtime.CompilerServices;
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace Prosequor.Ability;

/// <summary>
/// Beehive kiln session firer: last player to ignite fuel, frozen when heat starts,
/// cleared when heat ends. Stamps pottery in the chamber at session start.
/// </summary>
public static class BeeHiveKilnFirerStation
{
    public const string LastIgniterAttr = "prosequorKilnLastIgniter";
    public const string SessionFirerAttr = "prosequorKilnSessionFirer";

    static readonly ConditionalWeakTable<BlockEntity, Box> boxes = new();
    static readonly AccessTools.FieldRef<BlockEntityBeeHiveKiln, bool>? ReceivesHeatField =
        AccessTools.FieldRefAccess<BlockEntityBeeHiveKiln, bool>("receivesHeat");
    static readonly AccessTools.FieldRef<BlockEntityBeeHiveKiln, BlockPos[]?>? ParticlePositionsField =
        AccessTools.FieldRefAccess<BlockEntityBeeHiveKiln, BlockPos[]?>("particlePositions");

    sealed class Box
    {
        public string? LastIgniterUid;
        public string? SessionFirerUid;
        public bool PrevReceivesHeat;
        public bool HavePrev;
    }

    public static void NoteIgniter(BlockEntityBeeHiveKiln kiln, IPlayer? player)
    {
        if (kiln?.Api?.Side != EnumAppSide.Server || player?.PlayerUID == null)
        {
            return;
        }

        Box box = boxes.GetOrCreateValue(kiln);
        box.LastIgniterUid = player.PlayerUID;
        kiln.MarkDirty(redrawOnClient: false);
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
        if (kiln == null || !boxes.TryGetValue(kiln, out Box? box) || box == null)
        {
            return null;
        }

        return string.IsNullOrWhiteSpace(box.SessionFirerUid) ? null : box.SessionFirerUid;
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
        Box box = boxes.GetOrCreateValue(kiln);
        if (!box.HavePrev)
        {
            box.PrevReceivesHeat = receivesHeat;
            box.HavePrev = true;
            return;
        }

        if (!box.PrevReceivesHeat && receivesHeat)
        {
            box.SessionFirerUid = box.LastIgniterUid;
            StampChamberContents(kiln, box.SessionFirerUid);
            kiln.MarkDirty(redrawOnClient: false);
        }
        else if (box.PrevReceivesHeat && !receivesHeat)
        {
            box.SessionFirerUid = null;
            kiln.MarkDirty(redrawOnClient: false);
        }

        box.PrevReceivesHeat = receivesHeat;
    }

    public static void WriteToTree(BlockEntityBeeHiveKiln kiln, ITreeAttribute tree)
    {
        if (!boxes.TryGetValue(kiln, out Box? box) || box == null)
        {
            return;
        }

        if (!string.IsNullOrEmpty(box.LastIgniterUid))
        {
            tree.SetString(LastIgniterAttr, box.LastIgniterUid);
        }

        if (!string.IsNullOrEmpty(box.SessionFirerUid))
        {
            tree.SetString(SessionFirerAttr, box.SessionFirerUid);
        }
    }

    public static void ReadFromTree(BlockEntityBeeHiveKiln kiln, ITreeAttribute tree)
    {
        if (tree == null)
        {
            return;
        }

        bool hasLast = tree.HasAttribute(LastIgniterAttr);
        bool hasSession = tree.HasAttribute(SessionFirerAttr);
        if (!hasLast && !hasSession)
        {
            return;
        }

        Box box = boxes.GetOrCreateValue(kiln);
        box.LastIgniterUid = hasLast ? tree.GetString(LastIgniterAttr) : null;
        box.SessionFirerUid = hasSession ? tree.GetString(SessionFirerAttr) : null;
        if (string.IsNullOrWhiteSpace(box.LastIgniterUid))
        {
            box.LastIgniterUid = null;
        }

        if (string.IsNullOrWhiteSpace(box.SessionFirerUid))
        {
            box.SessionFirerUid = null;
        }

        if (ReceivesHeatField != null)
        {
            box.PrevReceivesHeat = ReceivesHeatField(kiln);
            box.HavePrev = true;
        }
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
        // Door is typically within a few blocks of the 3x3 fuel bed.
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
