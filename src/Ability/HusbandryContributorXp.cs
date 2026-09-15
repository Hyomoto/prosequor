using Prosequor.Ability.Hooks;
using Prosequor.Xp.Activity;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;

namespace Prosequor.Ability;

/// <summary>
/// Husbandry milestone XP: friendly animal ages up / gives birth → contributors payout,
/// then wipe care weights (favorite <c>MakerUid</c> kept).
/// </summary>
public static class HusbandryContributorXp
{
    /// <summary>
    /// If the animal is friendly, emit <paramref name="milestoneToken"/> + <c>friendly</c>
    /// with real contributor shares, then clear contributor weights.
    /// </summary>
    public static bool TrySettleAndWipe(Entity? entity, string milestoneToken)
    {
        if (entity?.World == null
            || entity.World.Side != EnumAppSide.Server
            || entity.Api == null
            || string.IsNullOrWhiteSpace(milestoneToken)
            || !HusbandryFriendliness.IsFriendly(entity)
            || !ProsequorEntityPedigreeStation.TryGetBlob(entity, out ProsequorBlob blob))
        {
            return false;
        }

        IReadOnlyList<Deed.ContributorShare> shares = RealContributorShares(blob);
        string[] tokens = HusbandryFriendliness.WithFriendlyToken(entity, milestoneToken.Trim());

        Deed.Emit(
            entity.Api,
            playerUid: "",
            tokens: tokens,
            caller: CallerIdentities.Hand,
            target: EventFactBuilder.CodeOf(entity),
            position: entity.Pos?.AsBlockPos?.Copy(),
            contributors: shares,
            makerUid: blob.MakerUid);

        ProsequorEntityPedigreeStation.ClearContributors(entity);
        return true;
    }

    /// <summary>Weighted shares excluding sentinel uids (<c>@…</c>).</summary>
    public static IReadOnlyList<Deed.ContributorShare> RealContributorShares(ProsequorBlob blob)
    {
        if (blob.Contributors.Count == 0)
        {
            return Array.Empty<Deed.ContributorShare>();
        }

        List<Deed.ContributorShare> list = new(blob.Contributors.Count);
        for (int i = 0; i < blob.Contributors.Count; i++)
        {
            ProsequorBlob.Share share = blob.Contributors[i];
            if (share.Weight <= 0
                || string.IsNullOrEmpty(share.PlayerUid)
                || share.PlayerUid[0] == '@')
            {
                continue;
            }

            list.Add(new Deed.ContributorShare(share.PlayerUid, share.Weight));
        }

        return list;
    }
}
