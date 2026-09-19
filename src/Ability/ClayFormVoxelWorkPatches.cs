using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace Prosequor.Ability;

[HarmonyPatch(
    typeof(BlockEntityClayForm),
    nameof(BlockEntityClayForm.OnUseOver),
    typeof(IPlayer),
    typeof(Vec3i),
    typeof(BlockFacing),
    typeof(bool))]
public static class ClayFormOnUseOverScopePatch
{
    [HarmonyPrefix]
    [HarmonyPriority(Priority.First)]
    public static void Prefix(IPlayer byPlayer) => ClayFormScope.Begin(byPlayer);

    [HarmonyFinalizer]
    public static void Finalizer() => ClayFormScope.End();
}

[HarmonyPatch(typeof(BlockEntityClayForm), "OnAdd", typeof(int), typeof(Vec3i), typeof(int))]
public static class ClayFormOnAddPatch
{
    [HarmonyPrefix]
    public static bool Prefix(
        BlockEntityClayForm __instance,
        int layer,
        Vec3i voxelPos,
        int radius,
        ref bool __result)
    {
        IPlayer? player = ClayFormScope.CurrentPlayer;
        if (player == null)
        {
            return true;
        }

        __result = ClayFormVoxelOps.OnAdd(__instance, layer, voxelPos, radius, player);
        return false;
    }
}

[HarmonyPatch(
    typeof(BlockEntityClayForm),
    "OnRemove",
    typeof(int),
    typeof(Vec3i),
    typeof(BlockFacing),
    typeof(int))]
public static class ClayFormOnRemovePatch
{
    [HarmonyPrefix]
    public static bool Prefix(
        BlockEntityClayForm __instance,
        int layer,
        Vec3i voxelPos,
        BlockFacing facing,
        int radius,
        ref bool __result)
    {
        IPlayer? player = ClayFormScope.CurrentPlayer;
        if (player == null)
        {
            return true;
        }

        __result = ClayFormVoxelOps.OnRemove(__instance, layer, voxelPos, facing, radius, player);
        return false;
    }
}

[HarmonyPatch(typeof(BlockEntityClayForm), "OnCopyLayer", typeof(int))]
public static class ClayFormOnCopyLayerPatch
{
    [HarmonyPrefix]
    public static bool Prefix(BlockEntityClayForm __instance, int layer, ref bool __result)
    {
        IPlayer? player = ClayFormScope.CurrentPlayer;
        if (player == null)
        {
            return true;
        }

        __result = ClayFormVoxelOps.OnCopyLayer(__instance, layer, player);
        return false;
    }
}

[HarmonyPatch(typeof(BlockEntityClayForm), nameof(BlockEntityClayForm.CheckIfFinished))]
public static class ClayFormCheckIfFinishedPatch
{
    [HarmonyPrefix]
    public static void Prefix(BlockEntityClayForm __instance, IPlayer byPlayer)
    {
        if (byPlayer == null || __instance.Api.Side != EnumAppSide.Server)
        {
            return;
        }

        ClayFormVoxelOps.TryAutoFinish(__instance, byPlayer);
    }

    [HarmonyPostfix]
    public static void Postfix(BlockEntityClayForm __instance, IPlayer byPlayer)
    {
        ClayFormVoxelOps.TryAwardProgress(__instance, byPlayer);
        ClayFormCraftAttribution.StampGroundStorageOutputs(__instance, byPlayer);
    }
}

[HarmonyPatch(typeof(BlockEntityClayForm), nameof(BlockEntityClayForm.ToTreeAttributes))]
public static class ClayFormXpToTreePatch
{
    [HarmonyPostfix]
    public static void Postfix(BlockEntityClayForm __instance, ITreeAttribute tree)
    {
        ClayFormXpStation.WriteToTree(__instance, tree);
    }
}

[HarmonyPatch(typeof(BlockEntityClayForm), nameof(BlockEntityClayForm.FromTreeAttributes))]
public static class ClayFormXpFromTreePatch
{
    [HarmonyPostfix]
    public static void Postfix(BlockEntityClayForm __instance, ITreeAttribute tree)
    {
        ClayFormXpStation.ReadFromTree(__instance, tree);
    }
}

[HarmonyPatch(typeof(ItemClay), nameof(ItemClay.OnHeldInteractStop))]
public static class ItemClayRefillPatch
{
    [HarmonyPrefix]
    public static void Prefix(ItemSlot slot, EntityAgent byEntity, BlockSelection blockSel)
    {
        if (blockSel == null || byEntity?.World == null)
        {
            return;
        }

        if (byEntity.World.BlockAccessor.GetBlockEntity(blockSel.Position) is BlockEntityClayForm form)
        {
            ClayFormVoxelOps.TryRefill(form, slot, byEntity);
        }
    }
}
