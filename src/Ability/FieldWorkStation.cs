using Prosequor.Ability.Hooks;
using Prosequor.Player;
using Vintagestory.API.Common;

namespace Prosequor.Ability;

/// <summary>Resolves Field Expertise area side length (1 = single cell / no ability).</summary>
public static class FieldWorkStation
{
    public const int DefaultSize = 1;

    public static int Run(IPlayer player)
    {
        if (player?.Entity == null)
        {
            return DefaultSize;
        }

        ProsequorModSystem? mod = ProsequorModSystem.For(player.Entity.Api);
        if (mod?.Pipeline == null)
        {
            return DefaultSize;
        }

        IPlayerProgress? progress = ProsequorModSystem.GetProgress(player);
        if (progress == null)
        {
            return DefaultSize;
        }

        AbilityAction fact = EventFactBuilder.ForPlayer(
            player,
            AbilityBootstrap.VerbFieldWork);

        FieldWorkContext context = new()
        {
            Player = player,
            Progress = progress,
            Fact = fact,
            BaseValue = DefaultSize
        };

        int result = mod.Pipeline.Run(HookIds.BlockInteraction, VerbIds.FieldWork, HookIds.Size, context, DefaultSize);
        return Math.Max(DefaultSize, result);
    }
}
