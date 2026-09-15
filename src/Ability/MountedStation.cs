using Prosequor.Ability.Hooks;
using Prosequor.Player;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.GameContent;

namespace Prosequor.Ability;

/// <summary>
/// Resolves <c>prosequor:mounted</c> phases and tracks rideable drivers / recent dismounts.
/// </summary>
public static class MountedStation
{
    public const string VerbMounted = "prosequor:mounted";
    public const string StatHungerKey = "prosequor-restfulrider";
    public const string StatMeleeKey = "prosequor-jousting";
    public const string StatFallKey = "prosequor-breakfall";
    public const string TokenMoving = "moving";

    public const long RecentDismountWindowMs = 8000;
    const double MountMotionEpsilonSq = 0.0001;

    static readonly Dictionary<EntityBehaviorRideable, EntityAgent> Drivers = new();
    static readonly Dictionary<string, long> LastDismountMsByPlayerUid = new(StringComparer.Ordinal);

    public static void RememberMount(EntityBehaviorRideable behavior, EntityAgent entityAgent)
    {
        if (entityAgent != null && !Drivers.ContainsKey(behavior))
        {
            Drivers[behavior] = entityAgent;
        }
    }

    public static void RememberUnmount(EntityBehaviorRideable behavior, EntityAgent entityAgent)
    {
        if (Drivers.TryGetValue(behavior, out EntityAgent? driver) && driver == entityAgent)
        {
            Drivers.Remove(behavior);
        }

        if (entityAgent is EntityPlayer entityPlayer && entityPlayer.Player?.PlayerUID != null)
        {
            LastDismountMsByPlayerUid[entityPlayer.Player.PlayerUID] =
                entityPlayer.World.ElapsedMilliseconds;
        }
    }

    public static bool TryGetDriver(EntityBehaviorRideable behavior, out EntityAgent? driver) =>
        Drivers.TryGetValue(behavior, out driver);

    public static bool RecentlyDismounted(Entity entity, long windowMs = RecentDismountWindowMs)
    {
        if (entity is not EntityPlayer entityPlayer || entityPlayer.Player?.PlayerUID == null)
        {
            return false;
        }

        if (!LastDismountMsByPlayerUid.TryGetValue(entityPlayer.Player.PlayerUID, out long atMs))
        {
            return false;
        }

        return entity.World.ElapsedMilliseconds - atMs <= windowMs;
    }

    public static float ResolveMoveSpeed(IPlayer player, Entity? mount)
    {
        return RunFloat(player, mount, HookIds.MoveSpeed, 1f);
    }

    public static bool ResolveCanRide(IPlayer player, Entity? mount)
    {
        return RunFloat(player, mount, HookIds.CanRide, 0f) >= 1f;
    }

    public static float ResolveSaddleBreakChance(IPlayer player, Entity? mount)
    {
        return Math.Clamp(RunFloat(player, mount, HookIds.SaddleBreak, 0f), 0f, 1f);
    }

    public static float ResolveHungerReduction(IPlayer player, Entity? mount)
    {
        return Math.Clamp(RunFloat(player, mount, HookIds.HungerRate, 0f), 0f, 1f);
    }

    public static float ResolveFallDamageReduction(IPlayer player, Entity? mount)
    {
        return Math.Clamp(RunFloat(player, mount, HookIds.FallDamage, 0f), 0f, 1f);
    }

    public static float ResolveMeleeBonus(IPlayer player, Entity? mount)
    {
        return Math.Max(0f, RunFloat(player, mount, HookIds.MeleeDamage, 0f));
    }

    static float RunFloat(IPlayer player, Entity? mount, PhaseId phase, float baseValue)
    {
        if (player?.Entity == null)
        {
            return baseValue;
        }

        ProsequorModSystem? mod = ProsequorModSystem.For(player.Entity.Api);
        if (mod?.Pipeline == null)
        {
            return baseValue;
        }

        IPlayerProgress? progress = ProsequorModSystem.GetProgress(player);
        if (progress == null)
        {
            return baseValue;
        }

        List<string>? tokens = null;
        if (mount?.Pos != null && mount.Pos.Motion.LengthSq() > MountMotionEpsilonSq)
        {
            tokens = [TokenMoving];
        }

        AbilityAction fact = EventFactBuilder.ForPlayer(
            player,
            VerbMounted,
            target: EventFactBuilder.CodeOf(mount),
            ground: EventFactBuilder.GroundUnder(player.Entity),
            tokens: tokens);

        MountedContext context = new()
        {
            Player = player,
            Progress = progress,
            Fact = fact,
            Mount = mount
        };

        return mod.Pipeline.Run(HookIds.EntityInteraction, VerbIds.Mounted, phase, context, baseValue);
    }
}
