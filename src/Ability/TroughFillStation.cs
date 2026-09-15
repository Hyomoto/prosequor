using Prosequor.Ability.Hooks;
using Prosequor.Player;
using Vintagestory.API.Common;

namespace Prosequor.Ability;

/// <summary>Resolves <c>prosequor:trough-fill</c> / <c>quantity</c> for the filling player.</summary>
public static class TroughFillStation
{
    public const int DefaultQuantity = 1;

    public static int ResolveFillQuantity(IPlayer player)
    {
        if (player?.Entity == null)
        {
            return DefaultQuantity;
        }

        ProsequorModSystem? mod = ProsequorModSystem.For(player.Entity.Api);
        if (mod?.Pipeline == null)
        {
            return DefaultQuantity;
        }

        IPlayerProgress? progress = ProsequorModSystem.GetProgress(player);
        if (progress == null)
        {
            return DefaultQuantity;
        }

        AbilityAction fact = EventFactBuilder.ForPlayer(player, AbilityBootstrap.VerbTroughFill);

        TroughFillContext context = new()
        {
            Player = player,
            Progress = progress,
            Fact = fact,
            BaseValue = DefaultQuantity
        };

        int result = mod.Pipeline.Run(
            HookIds.BlockInteraction,
            VerbIds.TroughFill,
            HookIds.Quantity,
            context,
            DefaultQuantity);
        return Math.Max(DefaultQuantity, result);
    }
}
