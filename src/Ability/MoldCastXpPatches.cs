using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace Prosequor.Ability;

/// <summary>
/// Crucible pour scope, mold pourer stamps, and harden rising-edge XP settle.
/// </summary>
public static class MoldCastXpPatches
{
    [HarmonyPatch(typeof(BlockSmeltedContainer), nameof(BlockSmeltedContainer.OnHeldInteractStart))]
    public static class SmeltedContainerPourScopeStartPatch
    {
        [HarmonyPrefix]
        public static void Prefix(EntityAgent byEntity, BlockSelection blockSel)
        {
            if (byEntity?.World?.Side != EnumAppSide.Server || blockSel?.Position == null)
            {
                return;
            }

            if (byEntity is not EntityPlayer ep || ep.Player == null)
            {
                return;
            }

            BlockEntity? be = byEntity.World.BlockAccessor.GetBlockEntity(blockSel.Position);
            if (be is BlockEntityToolMold or BlockEntityIngotMold)
            {
                MoldPourScope.Begin(ep.Player);
            }
        }

        [HarmonyFinalizer]
        public static void Finalizer() => MoldPourScope.End();
    }

    [HarmonyPatch(typeof(BlockSmeltedContainer), nameof(BlockSmeltedContainer.OnHeldInteractStep))]
    public static class SmeltedContainerPourScopeStepPatch
    {
        [HarmonyPrefix]
        public static void Prefix(EntityAgent byEntity, BlockSelection blockSel)
        {
            if (byEntity?.World?.Side != EnumAppSide.Server || blockSel?.Position == null)
            {
                return;
            }

            if (byEntity is not EntityPlayer ep || ep.Player == null)
            {
                return;
            }

            BlockEntity? be = byEntity.World.BlockAccessor.GetBlockEntity(blockSel.Position);
            if (be is BlockEntityToolMold or BlockEntityIngotMold)
            {
                MoldPourScope.Begin(ep.Player);
            }
        }

        [HarmonyFinalizer]
        public static void Finalizer() => MoldPourScope.End();
    }

    [HarmonyPatch(typeof(BlockEntityToolMold), nameof(BlockEntityToolMold.ReceiveLiquidMetal))]
    public static class ToolMoldReceiveMetalPatch
    {
        [HarmonyPostfix]
        public static void Postfix(BlockEntityToolMold __instance)
        {
            IPlayer? pourer = MoldPourScope.CurrentPlayer;
            if (pourer?.PlayerUID == null || __instance?.Api?.Side != EnumAppSide.Server)
            {
                return;
            }

            MoldCastXpStation.StampToolPourer(__instance, pourer.PlayerUID);
        }
    }

    [HarmonyPatch(typeof(BlockEntityIngotMold), nameof(BlockEntityIngotMold.ReceiveLiquidMetal))]
    public static class IngotMoldReceiveMetalPatch
    {
        [HarmonyPostfix]
        public static void Postfix(BlockEntityIngotMold __instance)
        {
            IPlayer? pourer = MoldPourScope.CurrentPlayer;
            if (pourer?.PlayerUID == null || __instance?.Api?.Side != EnumAppSide.Server)
            {
                return;
            }

            MoldCastXpStation.StampIngotPourer(
                __instance,
                right: __instance.IsRightSideSelected,
                pourer.PlayerUID);
        }
    }

    [HarmonyPatch(typeof(BlockEntityToolMold), "OnGameTick")]
    public static class ToolMoldTickPatch
    {
        [HarmonyPostfix]
        public static void Postfix(BlockEntityToolMold __instance) =>
            MoldCastXpStation.OnToolTick(__instance);
    }

    [HarmonyPatch(typeof(BlockEntityIngotMold), "OnGameTick")]
    public static class IngotMoldTickPatch
    {
        [HarmonyPostfix]
        public static void Postfix(BlockEntityIngotMold __instance) =>
            MoldCastXpStation.OnIngotTick(__instance);
    }

    [HarmonyPatch(typeof(BlockEntityToolMold), nameof(BlockEntityToolMold.ToTreeAttributes))]
    public static class ToolMoldXpToTreePatch
    {
        [HarmonyPostfix]
        public static void Postfix(BlockEntityToolMold __instance, ITreeAttribute tree) =>
            MoldCastXpStation.WriteToolToTree(__instance, tree);
    }

    [HarmonyPatch(typeof(BlockEntityToolMold), nameof(BlockEntityToolMold.FromTreeAttributes))]
    public static class ToolMoldXpFromTreePatch
    {
        [HarmonyPostfix]
        public static void Postfix(BlockEntityToolMold __instance, ITreeAttribute tree) =>
            MoldCastXpStation.ReadToolFromTree(__instance, tree);
    }

    [HarmonyPatch(typeof(BlockEntityIngotMold), nameof(BlockEntityIngotMold.ToTreeAttributes))]
    public static class IngotMoldXpToTreePatch
    {
        [HarmonyPostfix]
        public static void Postfix(BlockEntityIngotMold __instance, ITreeAttribute tree) =>
            MoldCastXpStation.WriteIngotToTree(__instance, tree);
    }

    [HarmonyPatch(typeof(BlockEntityIngotMold), nameof(BlockEntityIngotMold.FromTreeAttributes))]
    public static class IngotMoldXpFromTreePatch
    {
        [HarmonyPostfix]
        public static void Postfix(BlockEntityIngotMold __instance, ITreeAttribute tree) =>
            MoldCastXpStation.ReadIngotFromTree(__instance, tree);
    }
}
