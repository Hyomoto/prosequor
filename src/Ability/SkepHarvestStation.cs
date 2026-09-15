using Prosequor.Ability.Hooks;
using Prosequor.Player;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace Prosequor.Ability;

/// <summary>
/// Resolves <c>prosequor:harvest-skep</c> allow + right-click break chance.
/// </summary>
public static class SkepHarvestStation
{
    public static bool ResolveAllowRightClickHarvest(IPlayer? player, BlockSkep? skep, BlockPos? pos = null)
    {
        if (player?.Entity == null || skep == null)
        {
            return false;
        }

        ProsequorModSystem? mod = ProsequorModSystem.For(player.Entity.Api);
        if (mod?.Pipeline == null)
        {
            return false;
        }

        IPlayerProgress? progress = ProsequorModSystem.GetProgress(player);
        if (progress == null)
        {
            return false;
        }

        AbilityAction fact = EventFactBuilder.ForPlayer(
            player,
            AbilityBootstrap.VerbHarvestSkep,
            target: EventFactBuilder.CodeOf(skep),
            position: pos);

        SkepHarvestContext context = new()
        {
            Player = player,
            Progress = progress,
            Fact = fact,
            Block = skep,
            BaseValue = 0f
        };

        int allowed = mod.Pipeline.Run(
            HookIds.BlockInteraction,
            VerbIds.HarvestSkep,
            HookIds.AllowRightClickHarvest,
            context,
            0);
        return allowed >= 1;
    }

    public static float ResolveRightClickBreakChance(IPlayer? player, BlockSkep? skep, BlockPos? pos = null)
    {
        if (player?.Entity == null || skep == null)
        {
            return 0f;
        }

        ProsequorModSystem? mod = ProsequorModSystem.For(player.Entity.Api);
        if (mod?.Pipeline == null)
        {
            return 0f;
        }

        IPlayerProgress? progress = ProsequorModSystem.GetProgress(player);
        if (progress == null)
        {
            return 0f;
        }

        AbilityAction fact = EventFactBuilder.ForPlayer(
            player,
            AbilityBootstrap.VerbHarvestSkep,
            target: EventFactBuilder.CodeOf(skep),
            position: pos);

        SkepHarvestContext context = new()
        {
            Player = player,
            Progress = progress,
            Fact = fact,
            Block = skep,
            BaseValue = 0f
        };

        float chance = mod.Pipeline.Run(
            HookIds.BlockInteraction,
            VerbIds.HarvestSkep,
            HookIds.RightClickHarvestBreakChance,
            context,
            0f);
        return Math.Clamp(chance, 0f, 1f);
    }
}
