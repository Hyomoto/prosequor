using System.Collections.Concurrent;
using Prosequor.Ability;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;

namespace Prosequor.Xp.Activity;

/// <summary>
/// On-foot locomotion effort. Emits <c>sprinting</c> / <c>swimming</c> / <c>sneaking</c>
/// only while that mode is active and the player has displaced since the last sample.
/// Does not touch the mount moving path.
/// </summary>
public static class EffortFootEmitter
{
    /// <summary>Same squared threshold as mount travel in <see cref="ActivityWatchService"/>.</summary>
    public const double TravelSq = 0.04;

    static readonly ConcurrentDictionary<string, Vec3d> lastPosByPlayer = new(StringComparer.Ordinal);

    public static EffortPollResult? TryPoll(IPlayer player, EntityAgent entity)
    {
        if (player?.PlayerUID == null || entity == null)
        {
            return null;
        }

        if (entity.MountedOn != null)
        {
            lastPosByPlayer.TryRemove(player.PlayerUID, out _);
            return null;
        }

        Vec3d now = entity.Pos.XYZ;
        bool moved = false;
        if (lastPosByPlayer.TryGetValue(player.PlayerUID, out Vec3d? previous) && previous != null)
        {
            moved = HasDisplaced(previous, now);
        }

        lastPosByPlayer[player.PlayerUID] = now.Clone();
        if (!moved)
        {
            return null;
        }

        EntityControls controls = entity.ServerControls;
        string? token = TokenFor(
            PlayerInteractionStation.ClassifyFootLocomotion(
                mounted: false,
                entity.Swimming,
                entity.FeetInLiquid,
                controls.Sprint,
                controls.Sneak),
            entity.Swimming);
        if (token == null)
        {
            return null;
        }

        return new EffortPollResult([token], Channel: token);
    }

    /// <summary>
    /// Token for a displaced sample. Swim mode only trains when actually swimming
    /// (wading in liquid is the swim speed path, not swimming XP).
    /// </summary>
    public static string? TokenFor(FootLocomotion mode, bool swimming) => mode switch
    {
        FootLocomotion.Sprint => EffortTokenTags.Sprinting,
        FootLocomotion.Swim when swimming => EffortTokenTags.Swimming,
        FootLocomotion.Sneak => EffortTokenTags.Sneaking,
        _ => null
    };

    public static bool HasDisplaced(Vec3d previous, Vec3d now)
    {
        double dx = now.X - previous.X;
        double dy = now.Y - previous.Y;
        double dz = now.Z - previous.Z;
        return dx * dx + dy * dy + dz * dz >= TravelSq;
    }
}
