using Prosequor.Ability.Hooks;
using Prosequor.Player;
using Vintagestory.API.Common;
using Vintagestory.GameContent;

namespace Prosequor.Ability;

/// <summary>
/// Plant-time climate-window half-delta (°C) on farmland. Farming ability
/// <c>adjust-plant-climate-value</c> widens crop cold/heat damage bands
/// symmetrically; applied when <c>updateCropDamage</c> reads thresholds (transpiler).
/// Persists on <see cref="ProsequorChunkPedigree"/>.
/// </summary>
public static class CropClimateWindow
{
    public const string HalfDeltaAttr = "prosequorClimateHalfDelta";

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
        if (be != null
            && ProsequorBlockPedigreeStation.TryGetBox(be, out ProsequorChunkPedigree.Box box))
        {
            return Math.Max(0f, box.ClimateHalfDelta);
        }

        return 0f;
    }

    public static void StampHalfDelta(BlockEntity be, float halfDelta)
    {
        if (be == null)
        {
            return;
        }

        ProsequorBlockPedigreeStation.Mutate(be, box => box.ClimateHalfDelta = Math.Max(0f, halfDelta));
    }

    public static void Clear(BlockEntity? be)
    {
        if (be == null
            || !ProsequorBlockPedigreeStation.TryGetBox(be, out ProsequorChunkPedigree.Box box)
            || box.ClimateHalfDelta <= 0.0001f)
        {
            return;
        }

        ProsequorBlockPedigreeStation.Mutate(be, b => b.ClimateHalfDelta = 0f);
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
