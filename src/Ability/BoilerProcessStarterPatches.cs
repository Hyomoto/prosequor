using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace Prosequor.Ability;

/// <summary>
/// Still (boiler): sole process-starter contributor is whoever pours liquid in.
/// Distill quality resolves that uid (parked or online) at the condenser, rolls
/// once, and locks the result on the still until more liquid is added.
/// </summary>
public static class BoilerProcessStarterPatches
{
    public sealed class PourState
    {
        public int ContentSizeBefore;
        public bool IsBoiler;
    }

    /// <summary>
    /// After a successful pour into a boiler, replace the sole contributor with the pourer.
    /// Top-ups restamp (same contract as fruit-press mash fill).
    /// </summary>
    [HarmonyPatch(
        typeof(BlockLiquidContainerBase),
        nameof(BlockLiquidContainerBase.OnBlockInteractStart))]
    public static class BoilerPourContributorPatch
    {
        [HarmonyPrefix]
        public static void Prefix(
            IWorldAccessor world,
            BlockSelection blockSel,
            out PourState __state)
        {
            __state = new PourState();
            if (world?.Side != EnumAppSide.Server || blockSel?.Position == null)
            {
                return;
            }

            if (world.BlockAccessor.GetBlockEntity(blockSel.Position) is not BlockEntityBoiler boiler)
            {
                return;
            }

            __state.IsBoiler = true;
            ItemStack? content = boiler.GetContent();
            __state.ContentSizeBefore = content?.StackSize ?? 0;
        }

        [HarmonyPostfix]
        public static void Postfix(
            IWorldAccessor world,
            IPlayer byPlayer,
            BlockSelection blockSel,
            PourState __state)
        {
            if (!__state.IsBoiler
                || world?.Side != EnumAppSide.Server
                || byPlayer?.PlayerUID == null
                || blockSel?.Position == null)
            {
                return;
            }

            if (world.BlockAccessor.GetBlockEntity(blockSel.Position) is not BlockEntityBoiler boiler)
            {
                return;
            }

            ItemStack? content = boiler.GetContent();
            int after = content?.StackSize ?? 0;
            if (after <= __state.ContentSizeBefore)
            {
                return;
            }

            ProsequorBlockPedigreeStation.StampSoleContributor(boiler, byPlayer.PlayerUID);
            // New mash dilutes the liquid elsewhere; this load is a new batch.
            BoilerDistillBatch.Clear(boiler);
        }
    }

    /// <summary>
    /// Boiler overrides tree attrs; keep distill batch across chunk save/load.
    /// Pedigree rides on chunk moddata (<see cref="ProsequorChunkPedigree"/>).
    /// </summary>


    /// <summary>
    /// Condenser’s source slot belongs to an adjacent boiler — resolve that BE for the
    /// process-starter uid used by distill quality.
    /// </summary>
    public static bool TryResolveBoilerForSourceSlot(
        BlockEntityCondenser? condenser,
        ItemSlot? sourceSlot,
        out BlockEntityBoiler? boiler)
    {
        boiler = null;
        if (condenser?.Api?.World?.BlockAccessor == null || sourceSlot == null)
        {
            return false;
        }

        BlockPos pos = condenser.Pos;
        for (int i = 0; i < 4; i++)
        {
            BlockPos neighbor = pos.AddCopy(BlockFacing.HORIZONTALS[i]);
            if (condenser.Api.World.BlockAccessor.GetBlockEntity(neighbor)
                is not BlockEntityBoiler candidate)
            {
                continue;
            }

            if (candidate.Inventory == null || candidate.Inventory.Count < 1)
            {
                continue;
            }

            if (!ReferenceEquals(candidate.Inventory[0], sourceSlot))
            {
                continue;
            }

            boiler = candidate;
            return true;
        }

        return false;
    }

    public static string? TryGetDistillQualityUid(
        BlockEntityCondenser? condenser,
        ItemSlot? sourceSlot,
        string? mashMakerUid)
    {
        if (TryResolveBoilerForSourceSlot(condenser, sourceSlot, out BlockEntityBoiler? boiler)
            && ProsequorBlockPedigreeStation.TryGetBlob(boiler, out ProsequorBlob blob))
        {
            if (blob.TryGetSoleContributor(out string? uid) && !string.IsNullOrWhiteSpace(uid))
            {
                return uid;
            }

            if (!string.IsNullOrWhiteSpace(blob.MakerUid))
            {
                return blob.MakerUid;
            }
        }

        return string.IsNullOrWhiteSpace(mashMakerUid) ? null : mashMakerUid.Trim();
    }

    /// <summary>
    /// Placed liquid containers hide <c>api</c>. Used so a pour can clear the still's batch.
    /// </summary>
    public static BlockEntityBoiler? TryGetBoiler(BlockLiquidContainerBase? container, BlockPos? pos)
    {
        if (container == null || pos == null)
        {
            return null;
        }

        object? apiObj = AccessTools.Field(typeof(CollectibleObject), "api")?.GetValue(container);
        IWorldAccessor? world = apiObj switch
        {
            IWorldAccessor direct => direct,
            ICoreAPI api => api.World,
            _ => null
        };
        return world?.BlockAccessor?.GetBlockEntity(pos) as BlockEntityBoiler;
    }
}
