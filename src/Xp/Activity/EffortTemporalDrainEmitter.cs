using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.GameContent;

namespace Prosequor.Xp.Activity;

/// <summary>
/// Adaptation effort: <c>temporal-drain</c> while local temporal stability is below 1
/// (the vanilla drain branch). No stamp when the world disables temporal stability.
/// </summary>
public static class EffortTemporalDrainEmitter
{
    public static EffortPollResult? TryPoll(IPlayer player, EntityAgent entity)
    {
        if (player == null || entity?.Api == null || entity.World == null)
        {
            return null;
        }

        if (!entity.World.Config.GetAsBool("temporalStability", true))
        {
            return null;
        }

        SystemTemporalStability? stability = entity.Api.ModLoader.GetModSystem<SystemTemporalStability>();
        if (stability == null)
        {
            return null;
        }

        float here = stability.GetTemporalStability(entity.Pos.X, entity.Pos.Y, entity.Pos.Z);
        if (here >= 1f)
        {
            return null;
        }

        return new EffortPollResult(
            [EffortTokenTags.TemporalDrain],
            Channel: EffortTokenTags.TemporalDrain);
    }
}
