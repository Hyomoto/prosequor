using System.Text;
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace Prosequor.Ability;

/// <summary>
/// Held and placed liquid vessels: show portion affixes / quality / Created By under the
/// vanilla litres line.
/// </summary>
public static class LiquidContentTooltipPatches
{
    [HarmonyPatch(typeof(BlockLiquidContainerBase), nameof(BlockLiquidContainerBase.GetContentInfo))]
    public static class GetContentInfoChromePatch
    {
        [HarmonyPrefix]
        public static void Prefix(StringBuilder dsc, out int __state) =>
            __state = dsc?.Length ?? 0;

        [HarmonyPostfix]
        public static void Postfix(
            BlockLiquidContainerBase __instance,
            ItemSlot inSlot,
            StringBuilder dsc,
            IWorldAccessor world,
            int __state)
        {
            if (__instance == null || inSlot?.Itemstack == null || dsc == null)
            {
                return;
            }

            ItemStack? content = __instance.GetContent(inSlot.Itemstack);
            if (!LiquidContentChrome.TryFormat(world, content, out string chrome))
            {
                return;
            }

            // First line written by vanilla is "{0} litres of {1}" (or Empty — skipped above).
            LiquidContentChrome.InsertAfterNthLine(dsc, __state, 1, chrome);
        }
    }

    [HarmonyPatch(
        typeof(BlockLiquidContainerBase),
        nameof(BlockLiquidContainerBase.GetPlacedBlockInfo))]
    public static class GetPlacedBlockInfoChromePatch
    {
        [HarmonyPostfix]
        public static void Postfix(
            BlockLiquidContainerBase __instance,
            IWorldAccessor world,
            BlockPos pos,
            ref string __result)
        {
            if (__instance == null || world == null || pos == null)
            {
                return;
            }

            if (__instance.GetCurrentLitres(pos) <= 0f)
            {
                return;
            }

            ItemStack? content = __instance.GetContent(pos);
            if (!LiquidContentChrome.TryFormat(world, content, out string chrome))
            {
                return;
            }

            // "Contents:" then " {0} litres of {1}".
            LiquidContentChrome.InsertAfterNthLine(ref __result, 0, 2, chrome);
        }
    }
}
