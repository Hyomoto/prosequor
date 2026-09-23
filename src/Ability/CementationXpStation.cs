using System.Runtime.CompilerServices;
using HarmonyLib;
using Prosequor.Xp;
using Prosequor.Xp.Activity;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace Prosequor.Ability;

/// <summary>
/// Thread-local player while <see cref="BlockEntityStoneCoffin.Interact"/> loads the coffin.
/// Vanilla AddIngot / AddCoal do not take a player.
/// </summary>
public static class CementationLoadScope
{
    [ThreadStatic]
    static IPlayer? currentPlayer;

    public static IPlayer? CurrentPlayer => currentPlayer;

    public static void Begin(IPlayer? player) => currentPlayer = player;

    public static void End() => currentPlayer = null;
}

/// <summary>
/// Cementation furnace XP: BE contributor bag (loaders + fuel), settle once when
/// <c>processComplete</c> rises. Paid flag persists on <see cref="ProsequorChunkPedigree"/>
/// so chunk reload cannot repay. Rising-edge baseline is ephemeral.
/// </summary>
public static class CementationXpStation
{
    public const string PaidAttr = "prosequorCementationPaid";
    public const string WasCompleteAttr = "prosequorCementationWasComplete";

    /// <summary>Vanilla stone coffin ingot inventory slot.</summary>
    public const int IngotSlotIndex = 1;

    static readonly AccessTools.FieldRef<BlockEntityStoneCoffin, bool>? ProcessCompleteField =
        AccessTools.FieldRefAccess<BlockEntityStoneCoffin, bool>("processComplete");

    static readonly ConditionalWeakTable<BlockEntity, EdgeBox> edges = new();

    sealed class EdgeBox
    {
        public bool PrevComplete;
        public bool HavePrev;
    }

    public static bool IsProcessComplete(BlockEntityStoneCoffin? coffin) =>
        coffin != null && ProcessCompleteField != null && ProcessCompleteField(coffin);

    /// <summary>
    /// Rising-edge settle decision. When complete falls, clears paid and adopts state.
    /// When complete rises with contributors and not yet paid → pay.
    /// </summary>
    public static bool TrySettleRisingEdge(
        bool processComplete,
        bool hasContributors,
        ref bool wasComplete,
        ref bool paid)
    {
        if (!processComplete)
        {
            paid = false;
            wasComplete = false;
            return false;
        }

        bool rising = processComplete && !wasComplete;
        wasComplete = true;
        if (!rising || paid || !hasContributors)
        {
            return false;
        }

        paid = true;
        return true;
    }

    public static void Contribute(BlockEntityStoneCoffin? coffin, string? uid)
    {
        if (coffin?.Api?.Side != EnumAppSide.Server || string.IsNullOrWhiteSpace(uid))
        {
            return;
        }

        ProsequorBlockPedigreeStation.AddContributor(coffin, uid.Trim(), 1);
    }

    public static void ContributeFromLoad(BlockEntityStoneCoffin? coffin)
    {
        Contribute(coffin, CementationLoadScope.CurrentPlayer?.PlayerUID);
    }

    public static void ContributeAtFuel(IWorldAccessor world, BlockPos fuelPos, IPlayer? player)
    {
        if (world?.Side != EnumAppSide.Server || player?.PlayerUID == null || fuelPos == null)
        {
            return;
        }

        BlockEntityStoneCoffin? coffin = FindCoffinForFuel(world, fuelPos);
        if (coffin != null)
        {
            Contribute(coffin, player.PlayerUID);
        }
    }

    /// <summary>After each 3s server tick: detect processComplete rising edge and settle.</summary>
    public static void OnAfterServerTick(BlockEntityStoneCoffin coffin)
    {
        if (coffin?.Api?.Side != EnumAppSide.Server || ProcessCompleteField == null)
        {
            return;
        }

        EdgeBox edge = edges.GetOrCreateValue(coffin);
        bool complete = ProcessCompleteField(coffin);
        bool paid = ProsequorBlockPedigreeStation.TryGetBox(coffin, out ProsequorChunkPedigree.Box host)
            && host.CementationPaid;

        if (!edge.HavePrev)
        {
            edge.PrevComplete = complete;
            edge.HavePrev = true;
            if (complete && !paid)
            {
                ProsequorBlockPedigreeStation.Mutate(coffin, b => b.CementationPaid = true);
            }

            return;
        }

        bool hasBlob = ProsequorBlockPedigreeStation.TryGetBlob(coffin, out ProsequorBlob blob);
        IReadOnlyList<Deed.ContributorShare> shares = hasBlob
            ? ClayFireXpMath.SharesFromBlob(blob)
            : Array.Empty<Deed.ContributorShare>();
        bool hasContributors = shares.Count > 0;

        if (!TrySettleRisingEdge(
                complete,
                hasContributors,
                ref edge.PrevComplete,
                ref paid))
        {
            if (!complete && paid)
            {
                ProsequorBlockPedigreeStation.Mutate(coffin, b => b.CementationPaid = false);
            }

            return;
        }

        ProsequorBlockPedigreeStation.Mutate(coffin, b => b.CementationPaid = true);
        CementationXp.Settle(coffin, shares);
    }

    public static int BlisterQuantity(BlockEntityStoneCoffin? coffin)
    {
        if (coffin?.Inventory == null || coffin.Inventory.Count <= IngotSlotIndex)
        {
            return Math.Max(0, coffin?.IngotCount ?? 0);
        }

        ItemStack? stack = coffin.Inventory[IngotSlotIndex]?.Itemstack;
        int size = stack?.StackSize ?? 0;
        return size > 0 ? size : Math.Max(0, coffin.IngotCount);
    }

    static BlockEntityStoneCoffin? FindCoffinForFuel(IWorldAccessor world, BlockPos fuelPos)
    {
        BlockPos min = fuelPos.AddCopy(-6, -2, -6);
        BlockPos max = fuelPos.AddCopy(6, 6, 6);
        BlockEntityStoneCoffin? found = null;
        world.BlockAccessor.WalkBlocks(min, max, (block, x, y, z) =>
        {
            if (found != null)
            {
                return;
            }

            BlockPos pos = new(x, y, z, fuelPos.dimension);
            if (world.BlockAccessor.GetBlockEntity(pos) is not BlockEntityStoneCoffin coffin)
            {
                return;
            }

            if (IsFuelPosForCoffin(coffin, fuelPos))
            {
                found = coffin;
            }
        });

        return found;
    }

    static bool IsFuelPosForCoffin(BlockEntityStoneCoffin coffin, BlockPos fuelPos)
    {
        BlockPos[]? positions = coffin.FuelPositions;
        if (positions == null)
        {
            return false;
        }

        for (int i = 0; i < positions.Length; i++)
        {
            if (positions[i] != null && positions[i].Equals(fuelPos))
            {
                return true;
            }
        }

        return false;
    }
}
