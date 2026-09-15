using System.Text;
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace Prosequor.Ability;

/// <summary>
/// Crop planter pedigree: stamp planting player as MakerUid on farmland at <c>TryPlant</c>
/// (preserves existing care credits) and add plant care credit (contributor weight 1);
/// clear when the crop is removed (<c>OnCropBlockBroken</c>). Growth stage changes
/// keep the farmland BE, so no per-stage copy is required.
/// </summary>
public static class CropPlanterPedigreePatches
{
    [HarmonyPatch(
        typeof(BlockEntityFarmland),
        nameof(BlockEntityFarmland.TryPlant),
        [typeof(Block), typeof(ItemSlot), typeof(EntityAgent), typeof(BlockSelection)])]
    public static class FarmlandTryPlantPedigreePatch
    {
        [HarmonyPostfix]
        public static void Postfix(
            BlockEntityFarmland __instance,
            bool __result,
            EntityAgent byEntity)
        {
            if (!__result
                || __instance?.Api?.Side != EnumAppSide.Server
                || byEntity is not EntityPlayer entityPlayer
                || entityPlayer.Player?.PlayerUID == null)
            {
                return;
            }

            ProsequorBlockPedigreeStation.StampPlanter(__instance, entityPlayer.Player.PlayerUID);
            ProsequorBlockPedigreeStation.TryAddCareCredit(
                __instance,
                entityPlayer.Player.PlayerUID,
                FarmlandCareKind.Plant);
        }
    }

    [HarmonyPatch(typeof(BlockEntityFarmland), nameof(BlockEntityFarmland.OnCropBlockBroken))]
    public static class FarmlandOnCropBlockBrokenPedigreePatch
    {
        [HarmonyPostfix]
        public static void Postfix(BlockEntityFarmland __instance)
        {
            if (__instance?.Api?.Side != EnumAppSide.Server)
            {
                return;
            }

            ProsequorBlockPedigreeStation.ClearPlanter(__instance);
        }
    }

    /// <summary>
    /// Farmland overrides tree attrs without always hitting <see cref="BlockEntity"/> base patches.
    /// </summary>
    [HarmonyPatch(typeof(BlockEntityFarmland), nameof(BlockEntityFarmland.ToTreeAttributes))]
    public static class FarmlandToTreePlanterPatch
    {
        [HarmonyPostfix]
        public static void Postfix(BlockEntityFarmland __instance, ITreeAttribute tree) =>
            ProsequorBlockPedigreeStation.WriteToTree(__instance, tree);
    }

    [HarmonyPatch(typeof(BlockEntityFarmland), nameof(BlockEntityFarmland.FromTreeAttributes))]
    public static class FarmlandFromTreePlanterPatch
    {
        [HarmonyPostfix]
        public static void Postfix(BlockEntityFarmland __instance, ITreeAttribute tree) =>
            ProsequorBlockPedigreeStation.ReadFromTree(__instance, tree);
    }

    [HarmonyPatch(typeof(BlockEntityFarmland), nameof(BlockEntityFarmland.GetBlockInfo))]
    public static class FarmlandGetBlockInfoPlanterPatch
    {
        [HarmonyPostfix]
        public static void Postfix(BlockEntityFarmland __instance, StringBuilder dsc)
        {
            if (!ProsequorBlockPedigreeStation.TryGetPlanter(__instance, out string? uid))
            {
                return;
            }

            OwnerCredit.Append(dsc, __instance.Api?.World, uid, OwnerCredit.PlantedByLang);
        }
    }

    [HarmonyPatch(typeof(BlockCrop), nameof(BlockCrop.GetPlacedBlockInfo))]
    public static class BlockCropGetPlacedBlockInfoPlanterPatch
    {
        [HarmonyPostfix]
        public static void Postfix(
            IWorldAccessor world,
            BlockPos pos,
            ref string __result)
        {
            if (!ProsequorBlockPedigreeStation.TryGetCropPlanter(world, pos, out string? uid))
            {
                return;
            }

            OwnerCredit.Append(ref __result, world, uid, OwnerCredit.PlantedByLang);
        }
    }
}
