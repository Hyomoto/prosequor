using Prosequor.Ability;
using Prosequor.Xp.Activity;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace Prosequor.Xp.Adapters;

/// <summary>
/// Fertilizer slow-release drain: bank transferred nutrient percent, then emit
/// <c>fertilizer-absorbed</c> once a whole percent lands. Farmland pays its own
/// block; a planted bush pays the bush above its nutrition store. Empty bag
/// banks the remainder and pays nobody.
/// </summary>
public static class FertilizerAbsorbXp
{
    const float Epsilon = 0.0001f;

    /// <summary>
    /// Bank <paramref name="transferred"/> percent and emit whole percents when the
    /// care bag is non-empty. Returns the quantity emitted (0 when nothing paid).
    /// </summary>
    public static int OnAbsorbed(BlockEntitySoilNutrition? soil, float transferred)
    {
        if (soil == null || transferred <= Epsilon)
        {
            return 0;
        }

        ProsequorBlockPedigreeStation.AddAbsorbRemainder(soil, transferred);
        if (!TryRealShares(soil, out IReadOnlyList<Deed.ContributorShare> shares)
            || ProsequorBlockPedigreeStation.GetAbsorbRemainder(soil) < 1f
            || soil.Api?.Side != EnumAppSide.Server
            || soil.Api.World == null)
        {
            return 0;
        }

        Block? target = ResolveAbsorbTarget(soil);
        if (target == null)
        {
            return 0;
        }

        int whole = ProsequorBlockPedigreeStation.TakeAbsorbPercents(soil);
        if (whole <= 0)
        {
            return 0;
        }

        soil.Api.Logger.VerboseDebug(
            "[prosequor] deed fertilizer-absorbed {0} quantity={1} contributors={2}",
            target.Code,
            whole,
            shares.Count);

        Deed.Emit(
            soil.Api,
            playerUid: "",
            DeedToken.FertilizerAbsorbed,
            caller: CallerIdentities.Hand,
            target: EventFactBuilder.CodeOf(target),
            craftCount: whole,
            position: soil.Pos?.Copy(),
            contributors: shares);
        return whole;
    }

    /// <summary>
    /// Farmland emits its own block. A bush nutrition store emits the bush above it;
    /// gone bush means no target, so leftover drain does not pay.
    /// </summary>
    static Block? ResolveAbsorbTarget(BlockEntitySoilNutrition soil)
    {
        if (soil is not BlockEntityBerryBushFarmland)
        {
            return soil.Block;
        }

        BlockPos? above = soil.Pos?.UpCopy();
        if (above == null)
        {
            return null;
        }

        Block bush = soil.Api.World.BlockAccessor.GetBlock(above);
        return AbilityBootstrap.IsBerryBushBlock(bush) ? bush : null;
    }

    static bool TryRealShares(
        BlockEntitySoilNutrition soil,
        out IReadOnlyList<Deed.ContributorShare> shares)
    {
        shares = Array.Empty<Deed.ContributorShare>();
        if (!ProsequorBlockPedigreeStation.TryGetBlob(soil, out ProsequorBlob blob))
        {
            return false;
        }

        shares = HusbandryContributorXp.RealContributorShares(blob);
        return shares.Count > 0;
    }
}
