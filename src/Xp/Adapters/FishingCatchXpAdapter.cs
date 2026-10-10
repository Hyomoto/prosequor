using Prosequor.Ability;
using Prosequor.Xp.Activity;
using Vintagestory.API.Common;
using Vintagestory.API.Server;

namespace Prosequor.Xp.Adapters;

/// <summary>
/// XP from successful fish catches. <c>harvested</c> + caller = pole + target = fish.
/// </summary>
public class FishingCatchXpAdapter
{
    public const string VerbFish = FishClassification.VerbFish;

    readonly ICoreServerAPI sapi;

    public FishingCatchXpAdapter(ICoreServerAPI sapi)
    {
        this.sapi = sapi;
    }

    public void NotifyCatch(IPlayer byPlayer, ItemStack caught)
    {
        if (!FishingCatchFacts.TryCreate(byPlayer, caught, out AbilityAction? fact)
            || fact == null
            || !FishingCatchFacts.IsFishCatch(fact))
        {
            return;
        }

        if (sapi.World.PlayerByUid(byPlayer.PlayerUID) is not IServerPlayer serverPlayer)
        {
            return;
        }

        string? pole = fact.Caller ?? fact.Held;
        string caller = string.IsNullOrWhiteSpace(pole) ? CallerIdentities.Hand : pole;

        string? fishCode = EventFactBuilder.CodeOf(caught);
        int count = Math.Max(0, caught.StackSize);
        IReadOnlyList<Deed.QuantityUnit>? outputs = null;
        if (!string.IsNullOrWhiteSpace(fishCode) && count > 0)
        {
            outputs = [new Deed.QuantityUnit(fishCode, count)];
        }

        sapi.Logger.VerboseDebug(
            "[prosequor] deed harvested {0} caller={1} by {2}",
            caught.Collectible?.Code,
            caller,
            serverPlayer.PlayerName);

        Deed.Emit(
            sapi,
            fact.ActorUid,
            DeedToken.Harvested,
            caller: caller,
            target: fact.Target,
            lastCraft: fact.LastCraft,
            ground: fact.Ground,
            position: fact.Position,
            outputs: outputs);
    }
}
