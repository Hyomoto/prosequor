using Prosequor.Ability;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace Prosequor.Xp.Activity;

/// <summary>
/// Mount/boat effort poll: mounted + riding/boating (+ helmsman) with mount/ground codes.
/// Registered as <see cref="Effort.PollIdMount"/>.
/// </summary>
public static class EffortMountEmitter
{
    static readonly string[] BoatCodeHints =
    [
        "boat", "raft", "ship", "sail", "canoe", "skiff", "kayak", "barge"
    ];

    public static EffortPollResult? TryPoll(IPlayer player, EntityAgent entity)
    {
        if (player == null || entity?.MountedOn == null)
        {
            return null;
        }

        Entity? mount = ResolveMountEntity(entity);
        if (mount == null)
        {
            return null;
        }

        string? mountCode = mount.Code?.ToString();
        if (IsBoat(mount))
        {
            List<string> tokens =
            [
                EffortTokenTags.Mounted,
                EffortTokenTags.Boating
            ];
            if (mount is EntityBoat boat && BoatingStation.IsHelmsman(player, boat))
            {
                tokens.Add(EffortTokenTags.Helmsman);
            }

            string? ground = null;
            if (mount.Pos != null)
            {
                BlockPos waterPos = mount.Pos.AsBlockPos.DownCopy(1);
                Block? water = mount.World.BlockAccessor.GetBlock(waterPos);
                if (water != null && water.Id != 0)
                {
                    ground = water.Code?.ToString();
                }
            }

            return new EffortPollResult(
                tokens,
                Mount: mountCode,
                Ground: ground,
                Channel: EffortTokenTags.Boating);
        }

        return new EffortPollResult(
            [EffortTokenTags.Mounted, EffortTokenTags.Riding],
            Mount: mountCode,
            Channel: EffortTokenTags.Riding);
    }

    public static Entity? ResolveMountEntity(EntityAgent entity)
    {
        IMountableSeat? seat = entity.MountedOn;
        if (seat == null)
        {
            return null;
        }

        if (seat.Entity != null)
        {
            return seat.Entity;
        }

        if (seat.MountSupplier is EntityBehavior behavior)
        {
            return behavior.entity;
        }

        return seat.MountSupplier as Entity;
    }

    public static bool IsBoat(Entity mount)
    {
        if (mount is EntityBoat)
        {
            return true;
        }

        string? code = mount.Code?.ToString()?.ToLowerInvariant();
        if (string.IsNullOrEmpty(code))
        {
            return false;
        }

        foreach (string hint in BoatCodeHints)
        {
            if (code.Contains(hint, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}
