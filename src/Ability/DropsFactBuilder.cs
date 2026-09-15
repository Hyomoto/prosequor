using Prosequor.Ability.Hooks;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;

namespace Prosequor.Ability;

/// <summary>
/// Builds drop match facts for block harvest / break adapters.
/// Verb is always <c>mutate-drops</c>; match with <c>caller:</c> / <c>target:</c> / <c>drop:</c>.
/// Caller is the held tool or <c>@hand</c> (never a non-tool stack).
/// </summary>
public static class DropsFactBuilder
{
    public static AbilityAction ForBlock(IPlayer player, Block block, BlockPos pos) =>
        EventFactBuilder.ForPlayer(
            player,
            VerbIds.MutateDrops.Value,
            target: EventFactBuilder.CodeOf(block),
            held: EventFactBuilder.CallerOrHand(player),
            position: pos.Copy());

    /// <summary>
    /// Harvest interact / ripe-drop facts (same roles as break; no classification token).
    /// </summary>
    public static AbilityAction ForHarvest(IPlayer player, Block block, BlockPos pos) =>
        ForBlock(player, block, pos);

    /// <summary>Knife / rip harvest of a dead animal. Match with <c>target:</c> / <c>drop:</c>.</summary>
    public static AbilityAction ForEntity(IPlayer player, Entity entity) =>
        EventFactBuilder.ForPlayer(
            player,
            VerbIds.MutateDrops.Value,
            target: EventFactBuilder.CodeOf(entity),
            position: entity.Pos?.AsBlockPos?.Copy());
}
