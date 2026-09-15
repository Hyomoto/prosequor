using Prosequor.Ability.Hooks;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace Prosequor.Ability;

/// <summary>
/// Builds ability match facts for items received during a scoped fishing catch.
/// Verb is <c>mutate-drops</c>; match with <c>caller:&lt;fishingpole&gt;</c> / <c>target:&lt;fish&gt;</c>.
/// </summary>
public static class FishingCatchFacts
{
    public static bool TryCreate(IPlayer byPlayer, ItemStack caught, out AbilityAction? fact)
    {
        fact = null;
        if (byPlayer == null || caught?.Collectible == null)
        {
            return false;
        }

        BlockPos? pos = null;
        if (byPlayer is IServerPlayer serverPlayer)
        {
            pos = serverPlayer.Entity?.Pos?.AsBlockPos?.Copy();
        }

        fact = EventFactBuilder.ForPlayer(
            byPlayer,
            VerbIds.MutateDrops.Value,
            target: EventFactBuilder.CodeOf(caught),
            held: EventFactBuilder.CodeOf(byPlayer.InventoryManager?.ActiveHotbarSlot?.Itemstack?.Collectible),
            position: pos);
        return true;
    }

    /// <summary>True when the catch fact's target is a fish item (for XP; not an ability tag).</summary>
    public static bool IsFishCatch(AbilityAction? fact) =>
        fact != null && FishClassification.IsFishCode(fact.Target);
}
