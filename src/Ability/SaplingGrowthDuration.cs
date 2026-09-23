using System.Reflection;
using HarmonyLib;
using Prosequor.Ability.Hooks;
using Prosequor.Player;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace Prosequor.Ability;

/// <summary>
/// Place-time growth-duration pipeline for saplings, plus remaining-hour scaling
/// when a multiplier was stamped on the pedigree host.
/// </summary>
public static class SaplingGrowthDuration
{
    public const string GrowthTimeMultiplierAttr = "prosequorGrowthTimeMultiplier";

    const float MinMultiplier = 0.01f;

    static readonly FieldInfo? TotalHoursTillGrowthField =
        AccessTools.Field(typeof(BlockEntitySapling), "totalHoursTillGrowth");

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
    /// Runs growth-duration from 1; stamps and scales remaining hours when rules change it.
    /// </summary>
    public static void TryStampOnPlace(
        IWorldAccessor world,
        IPlayer player,
        BlockEntitySapling be)
    {
        if (world?.Side != EnumAppSide.Server || player == null || be == null)
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
            AbilityBootstrap.VerbPlantSapling,
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
            VerbIds.PlantSapling,
            new PhaseId("growth"),
            context,
            1f);
        result = Math.Max(result, MinMultiplier);
        if (Math.Abs(result - 1f) <= 0.0001f)
        {
            return;
        }

        StampMultiplier(be, result);
        ScaleRemainingHours(be, result);
    }

    public static void ApplyAfterStageChange(
        BlockEntitySapling be,
        EnumTreeGrowthStage previousStage,
        EnumTreeGrowthStage currentStage)
    {
        if (previousStage == currentStage
            || !TryGetMultiplier(be, out float multiplier))
        {
            return;
        }

        ScaleRemainingHours(be, multiplier);
    }

    public static void ScaleRemainingHours(BlockEntitySapling be, float multiplier)
    {
        if (TotalHoursTillGrowthField == null || be.Api?.World == null)
        {
            return;
        }

        double totalHours = (double)TotalHoursTillGrowthField.GetValue(be)!;
        double now = be.Api.World.Calendar.TotalHours;
        double remaining = Math.Max(0.0, totalHours - now);
        TotalHoursTillGrowthField.SetValue(be, now + remaining * multiplier);
        be.MarkDirty(redrawOnClient: false);
    }
}
