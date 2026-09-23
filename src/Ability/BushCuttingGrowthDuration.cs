using Prosequor.Ability.Hooks;
using Prosequor.Player;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace Prosequor.Ability;

/// <summary>
/// Place-time growth-duration pipeline for fruiting-bush cuttings
/// (<see cref="BEBehaviorFruitingBushCutting"/>). Persists on <see cref="ProsequorChunkPedigree"/>.
/// </summary>
public static class BushCuttingGrowthDuration
{
    public const string GrowthTimeMultiplierAttr = "prosequorGrowthTimeMultiplier";

    const float MinMultiplier = 0.01f;

    public static bool TryGetMultiplier(BlockEntity? be, out float multiplier)
    {
        multiplier = 1f;
        if (be == null
            || !ProsequorBlockPedigreeStation.TryGetBox(be, out ProsequorChunkPedigree.Box box)
            || box.GrowthTimeMultiplier < MinMultiplier)
        {
            return false;
        }

        multiplier = box.GrowthTimeMultiplier;
        return true;
    }

    public static void StampMultiplier(BlockEntity be, float multiplier)
    {
        if (be == null)
        {
            return;
        }

        ProsequorBlockPedigreeStation.Mutate(
            be,
            box => box.GrowthTimeMultiplier = GameMath.Clamp(multiplier, MinMultiplier, float.MaxValue));
    }

    /// <summary>
    /// Runs growth-duration from 1; stamps and scales remaining mature days when rules change it.
    /// </summary>
    public static void TryStampOnPlace(
        IWorldAccessor world,
        IPlayer player,
        BlockEntity be,
        BEBehaviorFruitingBushCutting cutting)
    {
        if (world?.Side != EnumAppSide.Server
            || player == null
            || be == null
            || cutting == null)
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
            AbilityBootstrap.VerbPlantBushCutting,
            target: EventFactBuilder.CodeOf(be.Block),
            position: be.Pos.Copy());

        GrowthDurationContext context = new()
        {
            World = world,
            Player = player,
            Progress = progress,
            Fact = fact,
            BaseValue = 1f
        };

        float result = mod.Pipeline.Run(
            HookIds.BlockInteraction,
            VerbIds.PlantBushCutting,
            new PhaseId("growth"),
            context,
            1f);
        result = Math.Max(result, MinMultiplier);
        if (Math.Abs(result - 1f) <= 0.0001f)
        {
            return;
        }

        StampMultiplier(be, result);
        ScaleRemainingDays(cutting, world, result);
    }

    public static void ScaleRemainingDays(
        BEBehaviorFruitingBushCutting cutting,
        IWorldAccessor world,
        float multiplier)
    {
        if (cutting == null || world == null)
        {
            return;
        }

        double now = world.Calendar.TotalDays;
        double remaining = Math.Max(0.0, cutting.matureTotalDays - now);
        cutting.matureTotalDays = now + remaining * multiplier;
        cutting.Blockentity?.MarkDirty(redrawOnClient: false);
    }
}
