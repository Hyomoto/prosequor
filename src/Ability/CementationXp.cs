using Prosequor.Xp;
using Prosequor.Xp.Activity;
using Vintagestory.API.Common;
using Vintagestory.GameContent;

namespace Prosequor.Ability;

/// <summary>
/// Pays metalworking cementation XP through <see cref="Deed.Emit"/>.
/// One <c>cementation-fired</c> emit per finished carburization; rule
/// <c>payee: contributors</c> splits shares; <c>pay: quantity</c> uses blister stack size.
/// </summary>
public static class CementationXp
{
    /// <summary>
    /// Settle one finished cementation run. Blank actor; contributor shares from the coffin BE.
    /// </summary>
    public static void Settle(
        BlockEntityStoneCoffin? coffin,
        IReadOnlyList<Deed.ContributorShare>? contributors = null)
    {
        if (coffin?.Api?.Side != EnumAppSide.Server)
        {
            return;
        }

        IReadOnlyList<Deed.ContributorShare> shares = contributors
            ?? Array.Empty<Deed.ContributorShare>();
        if (shares.Count == 0
            && ProsequorBlockPedigreeStation.TryGetBlob(coffin, out ProsequorBlob blob))
        {
            shares = ClayFireXpMath.SharesFromBlob(blob);
        }

        shares = Deed.NormalizeContributors(shares);
        if (shares.Count == 0)
        {
            return;
        }

        int quantity = CementationXpStation.BlisterQuantity(coffin);
        if (quantity <= 0)
        {
            return;
        }

        ItemStack? blister = null;
        if (coffin.Inventory != null
            && coffin.Inventory.Count > CementationXpStation.IngotSlotIndex)
        {
            blister = coffin.Inventory[CementationXpStation.IngotSlotIndex]?.Itemstack;
        }

        Deed.Emit(
            coffin.Api,
            playerUid: "",
            DeedToken.CementationFired,
            caller: CallerIdentities.Cementation,
            target: EventFactBuilder.CodeOf(blister) ?? EventFactBuilder.CodeOf(coffin.Block),
            craftCount: quantity,
            contributors: shares);
    }
}
