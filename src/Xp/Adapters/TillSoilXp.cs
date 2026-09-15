using Prosequor.Ability;
using Prosequor.Ability.Hooks;
using Prosequor.Xp.Activity;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace Prosequor.Xp.Adapters;

/// <summary>
/// Hoe soil→farmland: stamp tiller as farmland contributor weight 1 and emit
/// <c>till-soil</c> (one emit per converted tile; Field Expertise stacks by tile count).
/// </summary>
public static class TillSoilXp
{
    /// <summary>
    /// After a successful conversion: stamp contributor and emit farming till XP.
    /// </summary>
    public static void OnSoilConverted(ICoreAPI? api, IPlayer? player, BlockPos? pos, Block? soil)
    {
        if (api?.Side != EnumAppSide.Server
            || api.World == null
            || player?.PlayerUID == null
            || pos == null)
        {
            return;
        }

        if (api.World.BlockAccessor.GetBlockEntity(pos) is not BlockEntityFarmland farmland)
        {
            return;
        }

        ProsequorBlockPedigreeStation.TryAddCareCredit(
            farmland,
            player.PlayerUID,
            FarmlandCareKind.Till);

        api.Logger.VerboseDebug(
            "[prosequor] deed till-soil {0} actor={1}",
            soil?.Code ?? farmland.Block?.Code,
            player.PlayerUID);

        Deed.Emit(
            api,
            player.PlayerUID,
            DeedToken.TillSoil,
            caller: EventFactBuilder.HeldCode(player),
            target: EventFactBuilder.CodeOf(soil) ?? EventFactBuilder.CodeOf(farmland.Block),
            position: pos.Copy());
    }
}
