using Prosequor.Ability.Hooks;
using Prosequor.Player;
using Vintagestory.API.Common;

namespace Prosequor.Ability;

/// <summary>Resolves <c>player-interaction</c> / <c>cat-eyes</c> via the ability pipeline.</summary>
public static class CatEyesStation
{
    public const string VerbCatEyes = "prosequor:cat-eyes";

    /// <summary>
    /// Blend-weight ceiling for the client response pass (seed 0; providers add).
    /// Attribute-agnostic: whatever rules target this verb's default phase contribute.
    /// </summary>
    public static float ResolveCapacity(IPlayer player)
    {
        if (player?.Entity == null)
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

        AbilityAction fact = new()
        {
            Verb = VerbCatEyes,
            ActorUid = player.PlayerUID
        };

        CatEyesContext context = new()
        {
            Player = player,
            Progress = progress,
            Fact = fact
        };

        float capacity = mod.Pipeline.Run(HookIds.PlayerInteraction, VerbIds.CatEyes, HookIds.Default, context, 0f);
        return Math.Clamp(capacity, 0f, 1f);
    }
}
