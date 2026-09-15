using System.Runtime.CompilerServices;
using Prosequor.Ability.Hooks;
using Prosequor.Player;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.GameContent;

namespace Prosequor.Ability;

/// <summary>
/// Plant-time climate-window half-delta (°C) on farmland. Farming ability
/// <c>adjust-plant-climate-value</c> widens crop cold/heat damage bands
/// symmetrically; applied when <c>updateCropDamage</c> reads thresholds (transpiler).
/// </summary>
public static class CropClimateWindow
{
    public const string HalfDeltaAttr = "prosequorClimateHalfDelta";

    static readonly ConditionalWeakTable<BlockEntity, DeltaBox> runtimeDeltas = new();

    sealed class DeltaBox
    {
        public float HalfDelta;
    }

    /// <summary>
    /// <c>δ = span × expandFraction / 2</c>. Zero when fraction or span invalid.
    /// </summary>
    public static float ComputeHalfDeltaFromFraction(
        float coldDamageBelow,
        float heatDamageAbove,
        float expandFraction)
    {
        if (expandFraction <= 0f)
        {
            return 0f;
        }

        float span = heatDamageAbove - coldDamageBelow;
        if (span <= 0f)
        {
            return 0f;
        }

        return span * expandFraction / 2f;
    }

    public static float GetHalfDelta(BlockEntity? be)
    {
        if (be != null && runtimeDeltas.TryGetValue(be, out DeltaBox? box))
        {
            return Math.Max(0f, box.HalfDelta);
        }

        return 0f;
    }

    public static void StampHalfDelta(BlockEntity be, float halfDelta)
    {
        DeltaBox box = runtimeDeltas.GetOrCreateValue(be);
        box.HalfDelta = Math.Max(0f, halfDelta);
        be.MarkDirty(redrawOnClient: false);
    }

    public static void Clear(BlockEntity? be)
    {
        if (be == null || !runtimeDeltas.TryGetValue(be, out DeltaBox? box))
        {
            return;
        }

        box.HalfDelta = 0f;
        be.MarkDirty(redrawOnClient: false);
    }

    public static void WriteToTree(BlockEntity? be, ITreeAttribute? tree)
    {
        if (be == null || tree == null)
        {
            return;
        }

        float delta = GetHalfDelta(be);
        if (delta <= 0.0001f)
        {
            return;
        }

        tree.SetFloat(HalfDeltaAttr, delta);
    }

    public static void ReadFromTree(BlockEntity? be, ITreeAttribute? tree)
    {
        if (be == null || tree == null || !tree.HasAttribute(HalfDeltaAttr))
        {
            return;
        }

        StampHalfDelta(be, tree.GetFloat(HalfDeltaAttr));
    }

    /// <summary>
    /// Transpiler helper: after threshold <c>ldfld</c> + injected <c>ldarg.0</c>,
    /// stack is <c>(threshold, farmland)</c>.
    /// </summary>
    public static float AdjustColdThreshold(float cold, BlockEntityFarmland? be) =>
        cold - GetHalfDelta(be);

    /// <summary>
    /// Transpiler helper: after threshold <c>ldfld</c> + injected <c>ldarg.0</c>,
    /// stack is <c>(threshold, farmland)</c>.
    /// </summary>
    public static float AdjustHeatThreshold(float heat, BlockEntityFarmland? be) =>
        heat + GetHalfDelta(be);

    public static void TryStampOnPlant(
        IWorldAccessor world,
        IPlayer player,
        BlockEntityFarmland farmland,
        Block? cropBlock)
    {
        if (world?.Side != EnumAppSide.Server
            || player == null
            || farmland == null
            || cropBlock?.CropProps == null)
        {
            return;
        }

        IPlayerProgress? progress = ProsequorModSystem.GetProgress(player);
        ProsequorModSystem? mod = ProsequorModSystem.For(world.Api);
        if (progress == null || mod?.Pipeline == null)
        {
            return;
        }

        AbilityAction fact = EventFactBuilder.ForPlayer(
            player,
            AbilityBootstrap.VerbPlantCrop,
            target: EventFactBuilder.CodeOf(cropBlock),
            position: farmland.Pos.Copy());

        PlantCropClimateContext context = new()
        {
            World = world,
            Player = player,
            Progress = progress,
            Fact = fact,
            BaseValue = 0f
        };

        float fraction = Math.Max(
            0f,
            mod.Pipeline.Run(
                HookIds.BlockInteraction,
                VerbIds.PlantCrop,
                HookIds.Default,
                context,
                0f));

        float delta = ComputeHalfDeltaFromFraction(
            cropBlock.CropProps.ColdDamageBelow,
            cropBlock.CropProps.HeatDamageAbove,
            fraction);
        if (delta <= 0.0001f)
        {
            Clear(farmland);
            return;
        }

        StampHalfDelta(farmland, delta);
    }
}
