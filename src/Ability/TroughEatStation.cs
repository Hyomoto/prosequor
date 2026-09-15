using Prosequor.Ability.Hooks;
using Prosequor.Player;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;

namespace Prosequor.Ability;

/// <summary>
/// Resolves <c>prosequor:trough-eaten</c> / <c>chance</c> for a contributor uid
/// (online live progress or parked).
/// </summary>
public static class TroughEatStation
{
    public const float BaseFriendlinessChance = 0.05f;

    public static float ResolveFriendlinessChance(
        ICoreAPI? api,
        string? playerUid,
        Entity? animal)
    {
        if (api == null)
        {
            return BaseFriendlinessChance;
        }

        ProsequorModSystem? mod = ProsequorModSystem.For(api);
        if (mod?.Pipeline == null)
        {
            return BaseFriendlinessChance;
        }

        IPlayerProgress? progress = ProsequorModSystem.GetProgress(api, playerUid);
        if (progress == null)
        {
            return BaseFriendlinessChance;
        }

        IPlayer? player = null;
        if (!string.IsNullOrWhiteSpace(playerUid))
        {
            player = api.World?.PlayerByUid(playerUid.Trim());
        }

        AbilityAction fact = EventFactBuilder.Build(
            AbilityBootstrap.VerbTroughEaten,
            playerUid?.Trim() ?? "",
            held: null,
            target: EventFactBuilder.CodeOf(animal),
            lastCraft: null,
            ground: null,
            op: null,
            tokens: null,
            damage: null,
            inputs: null,
            position: null);

        AnimalBehaviorContext context = new()
        {
            Player = player,
            Progress = progress,
            Fact = fact,
            Animal = animal,
            BaseValue = BaseFriendlinessChance
        };

        float result = mod.Pipeline.Run(
            HookIds.EntityInteraction,
            VerbIds.TroughEaten,
            HookIds.Chance,
            context,
            BaseFriendlinessChance);
        return Math.Clamp(result, 0f, 1f);
    }
}
