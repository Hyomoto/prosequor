using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace Prosequor.Ability;

/// <summary>
/// Notes firepit interactors and stamps the process starter when cook/smelt begins.
/// </summary>
public static class FirepitProcessStarterPatches
{
    [HarmonyPatch(typeof(BlockFirepit), nameof(BlockFirepit.OnBlockInteractStart))]
    public static class FirepitBlockInteractNotePatch
    {
        [HarmonyPostfix]
        public static void Postfix(
            IWorldAccessor world,
            IPlayer byPlayer,
            BlockSelection blockSel)
        {
            if (world?.Side != EnumAppSide.Server
                || byPlayer == null
                || blockSel?.Position == null)
            {
                return;
            }

            if (world.BlockAccessor.GetBlockEntity(blockSel.Position) is not BlockEntityFirepit firepit)
            {
                return;
            }

            FirepitProcessStarterStation.NoteInteractor(firepit, byPlayer);
            FirepitProcessStarterStation.OnAfterTick(firepit);
        }
    }

    [HarmonyPatch(typeof(BlockEntityFirepit), nameof(BlockEntityFirepit.OnPlayerRightClick))]
    public static class FirepitRightClickNotePatch
    {
        [HarmonyPostfix]
        public static void Postfix(BlockEntityFirepit __instance, IPlayer byPlayer)
        {
            FirepitProcessStarterStation.NoteInteractor(__instance, byPlayer);
            FirepitProcessStarterStation.OnAfterTick(__instance);
        }
    }

    [HarmonyPatch(typeof(BlockFirepit), nameof(BlockFirepit.OnTryIgniteBlockOver))]
    public static class FirepitIgniteNotePatch
    {
        [HarmonyPostfix]
        public static void Postfix(EntityAgent byEntity, BlockPos pos)
        {
            if (byEntity?.World?.Side != EnumAppSide.Server || pos == null)
            {
                return;
            }

            if (byEntity.World.BlockAccessor.GetBlockEntity(pos) is not BlockEntityFirepit firepit)
            {
                return;
            }

            if (byEntity is EntityPlayer ep && !string.IsNullOrEmpty(ep.PlayerUID))
            {
                FirepitProcessStarterStation.NoteInteractor(firepit, ep.PlayerUID);
            }

            FirepitProcessStarterStation.OnAfterTick(firepit);
        }
    }

    [HarmonyPatch(typeof(BlockEntityFirepit), "OnBurnTick")]
    public static class FirepitBurnTickProcessStartPatch
    {
        [HarmonyPostfix]
        public static void Postfix(BlockEntityFirepit __instance) =>
            FirepitProcessStarterStation.OnAfterTick(__instance);
    }

    [HarmonyPatch(typeof(BlockEntityFirepit), nameof(BlockEntityFirepit.ToTreeAttributes))]
    public static class FirepitProcessStarterToTreePatch
    {
        [HarmonyPostfix]
        public static void Postfix(BlockEntityFirepit __instance, ITreeAttribute tree) =>
            FirepitProcessStarterStation.WriteToTree(__instance, tree);
    }

    [HarmonyPatch(typeof(BlockEntityFirepit), nameof(BlockEntityFirepit.FromTreeAttributes))]
    public static class FirepitProcessStarterFromTreePatch
    {
        [HarmonyPostfix]
        public static void Postfix(BlockEntityFirepit __instance, ITreeAttribute tree) =>
            FirepitProcessStarterStation.ReadFromTree(__instance, tree);
    }
}
