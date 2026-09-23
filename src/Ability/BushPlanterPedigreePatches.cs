using System.Text;
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace Prosequor.Ability;

/// <summary>
/// Berry / fruiting-bush planter pedigree on the bush BE.
/// Place stamps the planting player; cutting maturity (<c>OnMatureTick</c> → SetBlock) and
/// harvest ExchangeBlock keep planter identity. Cleared when the bush BE is removed.
/// </summary>
public static class BushPlanterPedigreePatches
{
    [ThreadStatic]
    static string? pendingGrownPlanterUid;

    [ThreadStatic]
    static BlockPos? pendingGrownPos;

    [HarmonyPatch(typeof(Block), nameof(Block.DoPlaceBlock))]
    public static class BlockDoPlaceBlockBushPlanterPatch
    {
        [HarmonyPostfix]
        public static void Postfix(
            bool __result,
            IWorldAccessor world,
            IPlayer byPlayer,
            BlockSelection blockSel)
        {
            if (!__result
                || world?.Side != EnumAppSide.Server
                || byPlayer?.PlayerUID == null
                || blockSel?.Position == null)
            {
                return;
            }

            Block? block = world.BlockAccessor.GetBlock(blockSel.Position);
            if (!AbilityBootstrap.IsBerryBushBlock(block))
            {
                return;
            }

            BlockEntity? be = world.BlockAccessor.GetBlockEntity(blockSel.Position);
            if (be == null)
            {
                return;
            }

            ProsequorBlockPedigreeStation.StampPlanter(be, byPlayer.PlayerUID);
        }
    }

    /// <summary>
    /// Cutting maturity <c>SetBlock</c>s a new bush BE, then calls
    /// <see cref="BEBehaviorFruitingBush.OnGrownFromCutting"/> on it. Carry MakerUid across
    /// that replace (hook is private <c>OnMatureTick</c> on the cutting behavior).
    /// </summary>
    [HarmonyPatch(typeof(BEBehaviorFruitingBushCutting), "OnMatureTick")]
    public static class FruitingBushCuttingMaturePlanterPatch
    {
        [HarmonyPrefix]
        public static void Prefix(BEBehaviorFruitingBushCutting __instance)
        {
            pendingGrownPlanterUid = null;
            pendingGrownPos = null;
            BlockEntity? be = __instance?.Blockentity;
            if (be == null)
            {
                return;
            }

            pendingGrownPos = be.Pos?.Copy();
            if (ProsequorBlockPedigreeStation.TryGetPlanter(be, out string? uid)
                && !string.IsNullOrWhiteSpace(uid))
            {
                pendingGrownPlanterUid = uid;
            }
        }

        [HarmonyPostfix]
        public static void Postfix(BEBehaviorFruitingBushCutting __instance)
        {
            string? uid = pendingGrownPlanterUid;
            BlockPos? pos = pendingGrownPos;
            pendingGrownPlanterUid = null;
            pendingGrownPos = null;
            if (string.IsNullOrWhiteSpace(uid) || pos == null)
            {
                return;
            }

            IWorldAccessor? world = __instance?.Api?.World ?? __instance?.Blockentity?.Api?.World;
            BlockEntity? be = world?.BlockAccessor.GetBlockEntity(pos);
            if (be != null)
            {
                ProsequorBlockPedigreeStation.StampPlanter(be, uid);
            }
        }
    }

    [HarmonyPatch(typeof(BlockEntityBerryBush), nameof(BlockEntityBerryBush.GetBlockInfo))]
    public static class BerryBushGetBlockInfoPlanterPatch
    {
        [HarmonyPostfix]
        public static void Postfix(BlockEntityBerryBush __instance, StringBuilder sb) =>
            AppendBushPlantedBy(__instance, sb);
    }

    [HarmonyPatch(typeof(BlockEntityWildFruitingBush), nameof(BlockEntityWildFruitingBush.GetBlockInfo))]
    public static class WildFruitingBushGetBlockInfoPlanterPatch
    {
        [HarmonyPostfix]
        public static void Postfix(BlockEntityWildFruitingBush __instance, StringBuilder dsc) =>
            AppendBushPlantedBy(__instance, dsc);
    }

    [HarmonyPatch(typeof(BEBehaviorFruitingBush), nameof(BEBehaviorFruitingBush.GetBlockInfo))]
    public static class FruitingBushGetBlockInfoPlanterPatch
    {
        [HarmonyPostfix]
        public static void Postfix(BEBehaviorFruitingBush __instance, StringBuilder dsc) =>
            AppendBushPlantedBy(__instance?.Blockentity, dsc);
    }

    [HarmonyPatch(typeof(BEBehaviorFruitingBushCutting), nameof(BEBehaviorFruitingBushCutting.GetBlockInfo))]
    public static class FruitingBushCuttingGetBlockInfoPlanterPatch
    {
        [HarmonyPostfix]
        public static void Postfix(BEBehaviorFruitingBushCutting __instance, StringBuilder dsc) =>
            AppendBushPlantedBy(__instance?.Blockentity, dsc);
    }

    static void AppendBushPlantedBy(BlockEntity? be, StringBuilder? dsc)
    {
        if (!ProsequorBlockPedigreeStation.TryGetPlanter(be, out string? uid))
        {
            return;
        }

        OwnerCredit.Append(dsc, be?.Api?.World, uid, OwnerCredit.PlantedByLang);
    }
}
