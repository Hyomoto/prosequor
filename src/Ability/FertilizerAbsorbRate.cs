using HarmonyLib;
using Prosequor.Ability.Hooks;
using Prosequor.Player;
using Vintagestory.API.Common;
using Vintagestory.GameContent;

namespace Prosequor.Ability;

/// <summary>
/// Soil Enrichment slow-release step. The multiplier lives on the farmland
/// pedigree box (<see cref="ProsequorBlockPedigreeStation"/>), not a side channel.
/// </summary>
public static class FertilizerAbsorbRate
{
    public const float VanillaSlowReleaseStep = 0.25f;

    const float MinMultiplier = ProsequorBlockPedigreeStation.DefaultAbsorbMultiplier;

    static readonly AccessTools.FieldRef<BlockEntitySoilNutrition, float[]>? SlowReleaseNutrientsField =
        AccessTools.FieldRefAccess<BlockEntitySoilNutrition, float[]>("slowReleaseNutrients");

    public static float GetAbsorbMultiplier(BlockEntity? be) =>
        ProsequorBlockPedigreeStation.GetAbsorbMultiplier(be);

    /// <summary>
    /// Slow-release transfer step for one fertility tick (replaces vanilla 0.25).
    /// </summary>
    public static float GetSlowReleaseStep(BlockEntitySoilNutrition be) =>
        VanillaSlowReleaseStep * GetAbsorbMultiplier(be);

    public static float SumSlowRelease(BlockEntitySoilNutrition soil)
    {
        float[]? slow = SlowReleaseNutrientsField?.Invoke(soil);
        if (slow == null || slow.Length == 0)
        {
            return 0f;
        }

        float sum = 0f;
        for (int i = 0; i < slow.Length; i++)
        {
            sum += slow[i];
        }

        return sum;
    }

    /// <summary>
    /// Runs fertilizer-absorb from 1; stamps the pedigree box when rules raise the multiplier.
    /// </summary>
    public static void TryStampOnFertilize(
        IWorldAccessor world,
        IPlayer player,
        BlockEntitySoilNutrition soil)
    {
        if (world?.Side != EnumAppSide.Server || player == null || soil == null)
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
            AbilityBootstrap.VerbFertilize,
            target: EventFactBuilder.CodeOf(soil.Block),
            position: soil.Pos.Copy());

        FertilizerAbsorbContext context = new()
        {
            World = world,
            Player = player,
            Progress = progress,
            Fact = fact,
            BaseValue = 1f
        };

        float result = mod.Pipeline.Run(
            HookIds.BlockInteraction,
            VerbIds.Fertilize,
            HookIds.Default,
            context,
            1f);
        result = Math.Max(result, MinMultiplier);
        if (Math.Abs(result - 1f) <= 0.0001f)
        {
            return;
        }

        ProsequorBlockPedigreeStation.StampAbsorbMultiplier(soil, result);
    }
}
