using Prosequor.Ability;
using Prosequor.Ability.Hooks;
using Prosequor.Xp.Activity;
using Vintagestory.API.Common;

namespace Prosequor.Xp;

/// <summary>
/// Pays clayforming fire XP through <see cref="Deed.Emit"/> / FatherXp.
/// One <c>kiln-fired</c> emit per finished piece; rule <c>payee: contributors</c> splits shares.
/// Voxels-per-unit are read from the fired stack's recipe stamp inside Emit.
/// </summary>
public static class ClayFireXp
{
    /// <summary>
    /// Settle one finished ceramic stack. Single deed emit with pedigree contributor shares;
    /// amount tables use voxels-per-unit from the stamped recipe against the catalog min/max.
    /// </summary>
    public static void Settle(
        IWorldAccessor world,
        string kilnCaller,
        string? targetCode,
        ItemStack? stack = null,
        IReadOnlyList<Deed.ContributorShare>? contributors = null)
    {
        if (world?.Api == null || world.Side != EnumAppSide.Server)
        {
            return;
        }

        ProsequorModSystem? mod = ProsequorModSystem.For(world.Api);
        if (mod?.FatherXp == null || mod.XpRules == null)
        {
            return;
        }

        string caller = string.IsNullOrWhiteSpace(kilnCaller)
            ? CallerIdentities.Hand
            : kilnCaller.Trim();

        IReadOnlyList<Deed.ContributorShare> shares = contributors
            ?? Array.Empty<Deed.ContributorShare>();
        if (shares.Count == 0
            && ProsequorStackPedigree.TryGetPrimaryBlob(stack, out ProsequorBlob blob))
        {
            shares = ClayFireXpMath.SharesFromBlob(blob);
        }

        shares = Deed.NormalizeContributors(shares);
        if (shares.Count == 0)
        {
            return;
        }

        string? makerUid = CraftAttribution.TryGetMakerUid(stack);

        Deed.Emit(
            world.Api,
            playerUid: "",
            ClayFireXpMath.BuildTokens(),
            caller: caller,
            target: targetCode ?? EventFactBuilder.CodeOf(stack),
            contributors: shares,
            makerUid: makerUid,
            subject: stack);
    }
}
