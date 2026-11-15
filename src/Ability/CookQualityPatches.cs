using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace Prosequor.Ability;

/// <summary>
/// Firepit smelt and oven bake completion → cook quality stamp.
/// </summary>
public static class CookQualityPatches
{
    [HarmonyPatch(typeof(BlockEntityFirepit), "smeltItems")]
    public static class FirepitSmeltItemsQualityPatch
    {
        [HarmonyPrefix]
        public static void Prefix(BlockEntityFirepit __instance) =>
            CookQualityStation.NoteInput(__instance);

        [HarmonyPostfix]
        public static void Postfix(BlockEntityFirepit __instance) =>
            CookQualityStation.OnAfterFirepitSmelt(__instance);
    }

    [HarmonyPatch(typeof(BlockEntityOven), "IncrementallyBake")]
    public static class OvenIncrementallyBakeQualityPatch
    {
        public sealed class BakeState
        {
            public AssetLocation? BeforeCode;
            public int SlotIndex;
            public string? VesselMaker;
        }

        [HarmonyPrefix]
        public static void Prefix(BlockEntityOven __instance, int slotIndex, out BakeState __state)
        {
            __state = new BakeState { SlotIndex = slotIndex };
            ItemSlot? slot = __instance.Inventory?[slotIndex];
            __state.BeforeCode = slot?.Itemstack?.Collectible?.Code;
            __state.VesselMaker = CraftAttribution.TryGetMakerUid(slot?.Itemstack);
        }

        [HarmonyPostfix]
        public static void Postfix(BlockEntityOven __instance, BakeState __state)
        {
            if (__instance?.Api?.World?.Side != EnumAppSide.Server)
            {
                return;
            }

            ItemSlot? slot = __instance.Inventory?[__state.SlotIndex];
            ItemStack? after = slot?.Itemstack;
            if (after?.Collectible?.Code == null)
            {
                return;
            }

            if (__state.BeforeCode != null
                && after.Collectible.Code.Equals(__state.BeforeCode))
            {
                return;
            }

            CookQualityStation.NoteBakeVesselMaker(__state.VesselMaker);
            CookQualityStation.OnAfterOvenBake(__instance, after);
        }
    }

    [HarmonyPatch(typeof(BlockClayOven), nameof(BlockClayOven.OnBlockInteractStart))]
    public static class ClayOvenInteractNotePatch
    {
        [HarmonyPostfix]
        public static void Postfix(
            IWorldAccessor world,
            IPlayer byPlayer,
            BlockSelection bs)
        {
            if (world?.Side != EnumAppSide.Server
                || byPlayer == null
                || bs?.Position == null)
            {
                return;
            }

            if (world.BlockAccessor.GetBlockEntity(bs.Position) is not BlockEntityOven oven)
            {
                return;
            }

            OvenCookStarterStation.NoteInteractor(oven, byPlayer);
        }
    }


}
