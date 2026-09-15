using Prosequor.Ability.Hooks;
using Prosequor.Player;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace Prosequor.Ability;

/// <summary>
/// Resolves boat-facing <c>prosequor:mounted</c> phases and tracks boat captains.
/// </summary>
public static class BoatingStation
{
    public const string TagRaft = "raft";
    public const string TagSailboat = "sailboat";
    public const string TagFreshwater = "freshwater";
    public const string TagSaltwater = "saltwater";
    public const string TokenMoving = "moving";
    public const string TokenHelmsman = "helmsman";

    const double BoatMotionEpsilonSq = 0.0001;

    static readonly Dictionary<EntityBoat, EntityAgent> Captains = new();

    public static void RememberMount(EntityBoat boat, EntityAgent entityAgent)
    {
        if (entityAgent != null && !Captains.ContainsKey(boat))
        {
            Captains[boat] = entityAgent;
        }
    }

    public static void RememberUnmount(EntityBoat boat, EntityAgent entityAgent)
    {
        if (Captains.TryGetValue(boat, out EntityAgent? captain) && captain == entityAgent)
        {
            Captains.Remove(boat);
        }
    }

    public static bool TryGetCaptain(EntityBoat boat, out EntityAgent? captain) =>
        Captains.TryGetValue(boat, out captain);

    public static bool IsHelmsman(IPlayer player, EntityBoat boat)
    {
        if (player?.Entity == null || !TryGetCaptain(boat, out EntityAgent? captain))
        {
            return false;
        }

        return captain != null && captain.EntityId == player.Entity.EntityId;
    }

    public static float ResolveForwardSpeed(IPlayer player, EntityBoat boat) =>
        RunFloat(player, boat, HookIds.MoveSpeed, 1f);

    public static float ResolveTurnSpeed(IPlayer player, EntityBoat boat) =>
        RunFloat(player, boat, HookIds.TurnSpeed, 1f);

    public static float ResolveRatlineDrainReduction(IPlayer player) =>
        Math.Clamp(RunFloat(player, null, HookIds.RatlineStamina, 0f), 0f, 1f);

    static float RunFloat(IPlayer player, EntityBoat? boat, PhaseId phase, float baseValue)
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

        List<string> tokens = new();
        if (boat?.Pos != null && boat.Pos.Motion.LengthSq() > BoatMotionEpsilonSq)
        {
            tokens.Add(TokenMoving);
        }

        if (boat != null && IsHelmsman(player, boat))
        {
            tokens.Add(TokenHelmsman);
        }

        AbilityAction fact = EventFactBuilder.ForPlayer(
            player,
            VerbIds.Mounted.Value,
            target: EventFactBuilder.CodeOf(boat),
            ground: ResolveWaterCode(boat),
            tokens: tokens.Count > 0 ? tokens : null);

        MountedContext context = new()
        {
            Player = player,
            Progress = progress,
            Fact = fact,
            Mount = boat
        };

        return mod.Pipeline.Run(HookIds.EntityInteraction, VerbIds.Mounted, phase, context, baseValue);
    }

    static string? ResolveWaterCode(EntityBoat? boat)
    {
        if (boat?.Pos == null || boat.World == null)
        {
            return null;
        }

        try
        {
            BlockPos pos = boat.Pos.AsBlockPos.DownCopy(1);
            Block? block = boat.World.BlockAccessor.GetBlock(pos);
            return block != null && block.Id != 0 ? EventFactBuilder.CodeOf(block) : null;
        }
        catch
        {
            return null;
        }
    }
}
