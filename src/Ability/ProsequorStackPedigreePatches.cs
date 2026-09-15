using HarmonyLib;
using Vintagestory.API.Common;

namespace Prosequor.Ability;

/// <summary>
/// Keeps <see cref="ProsequorStackPedigree"/> in sync across vanilla merge and TakeOut.
/// </summary>
public static class ProsequorStackPedigreePatches
{
    public sealed class TakeOutState
    {
        public List<ProsequorStackPedigree.FrozenGroup>? Frozen;
        public bool HadLive;
        public ProsequorBlob? LiveBlob;
        public int SourceSizeBefore;
    }

    public sealed class MergeState
    {
        public List<ProsequorStackPedigree.FrozenGroup>? SourceFrozen;
        public bool SourceHadLive;
        public ProsequorBlob? SourceLiveBlob;
        public int SourceSizeBefore;
    }

    [HarmonyPatch(typeof(CollectibleObject), nameof(CollectibleObject.TryMergeStacks))]
    public static class CollectibleTryMergeStacksPedigreePatch
    {
        [HarmonyPrefix]
        public static void Prefix(ItemStackMergeOperation op, ref MergeState __state)
        {
            __state = new MergeState();
            ItemStack? source = op?.SourceSlot?.Itemstack;
            if (source == null)
            {
                return;
            }

            __state.SourceSizeBefore = source.StackSize;
            ProsequorStackPedigree.SnapshotForTakeOut(
                source,
                out List<ProsequorStackPedigree.FrozenGroup> frozen,
                out bool hadLive,
                out ProsequorBlob? liveBlob);
            __state.SourceFrozen = frozen;
            __state.SourceHadLive = hadLive;
            __state.SourceLiveBlob = liveBlob;
        }

        [HarmonyPostfix]
        public static void Postfix(ItemStackMergeOperation op, MergeState __state)
        {
            if (op == null || op.MovedQuantity <= 0 || __state == null)
            {
                return;
            }

            ItemStack? sink = op.SinkSlot?.Itemstack;
            ItemStack? sourceAfter = op.SourceSlot?.Itemstack;
            if (sink == null)
            {
                return;
            }

            ProsequorStackPedigree.MergeAfterMove(
                sink,
                sourceAfter,
                op.MovedQuantity,
                __state.SourceFrozen,
                __state.SourceHadLive,
                __state.SourceLiveBlob,
                __state.SourceSizeBefore);
        }
    }

    [HarmonyPatch(typeof(ItemSlot), nameof(ItemSlot.TakeOut), typeof(int))]
    public static class ItemSlotTakeOutPedigreePatch
    {
        [HarmonyPrefix]
        public static void Prefix(ItemSlot __instance, int quantity, ref TakeOutState __state)
        {
            ItemStack? stack = __instance?.Itemstack;
            __state = new TakeOutState { SourceSizeBefore = stack?.StackSize ?? 0 };
            if (stack == null || quantity <= 0)
            {
                return;
            }

            ProsequorStackPedigree.SnapshotForTakeOut(
                stack,
                out List<ProsequorStackPedigree.FrozenGroup> frozen,
                out bool hadLive,
                out ProsequorBlob? liveBlob);
            __state.Frozen = frozen;
            __state.HadLive = hadLive;
            __state.LiveBlob = liveBlob;
        }

        [HarmonyPostfix]
        public static void Postfix(ItemSlot __instance, int quantity, ItemStack __result, TakeOutState __state)
        {
            if (__result == null || __result.StackSize <= 0 || __state == null)
            {
                return;
            }

            if ((__state.Frozen == null || __state.Frozen.Count == 0) && !__state.HadLive)
            {
                return;
            }

            ItemStack? sourceAfter = __instance?.Itemstack;
            ProsequorStackPedigree.AfterTakeOut(
                sourceAfter,
                __result,
                __state.Frozen,
                __state.HadLive,
                __state.LiveBlob);
        }
    }
}
