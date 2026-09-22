using System;
using Prosequor.Ability.Hooks;
using Prosequor.Player;
using Prosequor.Xp;
using Prosequor.Xp.Activity;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace Prosequor.Ability;

/// <summary>
/// Plumb-and-square reinforcement: fold material strength, then emit a reinforced deed.
/// </summary>
public static class ReinforceStation
{
    /// <summary>
    /// Folds reinforce / strength for <paramref name="strength"/> (material base).
    /// Returns the stochastic-rounded strength to apply.
    /// </summary>
    public static int ResolveStrength(IPlayer player, int strength)
    {
        if (player?.Entity == null || strength <= 0)
        {
            return strength;
        }

        IWorldAccessor? world = player.Entity.World ?? player.Entity.Api?.World;
        if (world?.Side != EnumAppSide.Server)
        {
            return strength;
        }

        ProsequorModSystem? mod = ProsequorModSystem.For(player.Entity.Api);
        if (mod?.Pipeline == null)
        {
            return strength;
        }

        IPlayerProgress? progress = ProsequorModSystem.GetProgress(player);
        if (progress == null)
        {
            return strength;
        }

        AbilityAction fact = EventFactBuilder.ForPlayer(
            player,
            VerbIds.Reinforce.Value,
            held: EventFactBuilder.HeldCode(player),
            includeLastCraft: false);

        ReinforceContext context = new()
        {
            Player = player,
            Progress = progress,
            Fact = fact,
            World = world
        };

        float folded = mod.Pipeline.Run(
            HookIds.ItemInteraction,
            VerbIds.Reinforce,
            HookIds.Strength,
            context,
            (float)strength);

        Random rand = world.Rand ?? new Random(0);
        int next = AbilityFormulas.StochasticRound(folded, rand);
        return Math.Max(1, next);
    }

    public static void EmitReinforced(IPlayer player, BlockPos? pos)
    {
        if (player?.PlayerUID == null)
        {
            return;
        }

        ICoreAPI? api = player.Entity?.Api ?? player.Entity?.World?.Api;
        if (api == null)
        {
            return;
        }

        Deed.Emit(
            api,
            player.PlayerUID,
            DeedToken.Reinforced,
            position: pos);
    }
}
