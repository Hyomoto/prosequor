using System.Runtime.CompilerServices;
using Prosequor.Ability.Hooks;
using Prosequor.Player;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace Prosequor.Ability;

/// <summary>
/// Place-time growth-duration pipeline for fruiting-bush cuttings
/// (<see cref="BEBehaviorFruitingBushCutting"/>).
/// </summary>
public static class BushCuttingGrowthDuration
{
    public const string GrowthTimeMultiplierAttr = "prosequorGrowthTimeMultiplier";

    const float MinMultiplier = 0.01f;

    static readonly ConditionalWeakTable<BlockEntity, MultiplierBox> runtimeMultipliers = new();

    sealed class MultiplierBox
    {
        public float Multiplier;
    }

    public static bool TryGetMultiplier(BlockEntity? be, out float multiplier)
    {
        multiplier = 1f;
        if (be == null)
        {
            return false;
        }

        if (runtimeMultipliers.TryGetValue(be, out MultiplierBox? box))
        {
            multiplier = box.Multiplier;
            return true;
        }

        return false;
    }

    public static void StampMultiplier(BlockEntity be, float multiplier)
    {
        MultiplierBox box = runtimeMultipliers.GetOrCreateValue(be);
        box.Multiplier = GameMath.Clamp(multiplier, MinMultiplier, float.MaxValue);
        be.MarkDirty(redrawOnClient: false);
    }

    public static void WriteToTree(BlockEntity be, ITreeAttribute tree)
    {
        if (runtimeMultipliers.TryGetValue(be, out MultiplierBox? box))
        {
            tree.SetFloat(GrowthTimeMultiplierAttr, box.Multiplier);
        }
    }

    public static void ReadFromTree(BlockEntity be, ITreeAttribute tree)
    {
        if (!tree.HasAttribute(GrowthTimeMultiplierAttr))
        {
            return;
        }

        MultiplierBox box = runtimeMultipliers.GetOrCreateValue(be);
        box.Multiplier = GameMath.Clamp(
            tree.GetFloat(GrowthTimeMultiplierAttr),
            MinMultiplier,
            float.MaxValue);
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
