using Prosequor.Ability;
using Prosequor.Xp.Activity;
using Vintagestory.API.Common;
using Vintagestory.API.Server;

namespace Prosequor.Xp.Adapters;

/// <summary>
/// XP from successful fish catches. <c>fishing-catch</c> + caller = pole + target = fish.
/// </summary>
public class FishingCatchXpAdapter
{
    public const string VerbFish = FishClassification.VerbFish;

    readonly ICoreServerAPI sapi;

    public FishingCatchXpAdapter(ICoreServerAPI sapi, XpActionDispatcher dispatcher)
    {
        this.sapi = sapi;
        _ = dispatcher;
    }

    public void Start()
    {
    }

    public void Dispose()
    {
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

        sapi.Logger.VerboseDebug(
            "[prosequor] deed fish {0} caller={1} by {2}",
            caught.Collectible?.Code,
            caller,
            serverPlayer.PlayerName);

        Deed.Emit(
            sapi,
            fact.ActorUid,
            DeedToken.FishingCatch,
            caller: caller,
            target: fact.Target,
            lastCraft: fact.LastCraft,
            ground: fact.Ground,
            position: fact.Position);
    }
}
