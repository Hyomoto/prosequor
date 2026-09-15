using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace Prosequor.Ability;

/// <summary>
/// Liquid pour / scoop / distill / equal-fill jug stack — pedigree reduce path.
/// Portion merges always dilute; solids keep the Frozen bag via
/// <see cref="ProsequorStackPedigreePatches"/>.
/// </summary>
public static class ProsequorLiquidPedigreePatches
{
    public sealed class PutLiquidState
    {
        public int SinkQtyBefore;
        public ProsequorBlob SourceBlob = ProsequorBlob.Empty;
        public bool HadSink;
    }

    public sealed class ContainerMergeState
    {
        public int SinkJugsBefore;
        public int ContentItemsPerJug;
        public ProsequorBlob SourceBlob = ProsequorBlob.Empty;
        public bool HadEqualFillContents;
    }

    public sealed class DistillateState
    {
        public int SinkQtyBefore;
        public ProsequorBlob SourceBlob = ProsequorBlob.Empty;
        public int MashQualityRank;
        public bool HadBucket;
        public ItemStack? BucketStack;
        public string? QualityUid;
    }

    [HarmonyPatch(
        typeof(BlockLiquidContainerBase),
        nameof(BlockLiquidContainerBase.TryPutLiquid),
        typeof(ItemStack),
        typeof(ItemStack),
        typeof(float))]
    public static class TryPutLiquidItemStackPatch
    {
        [HarmonyPrefix]
        public static void Prefix(
            BlockLiquidContainerBase __instance,
            ItemStack containerStack,
            ItemStack liquidStack,
            ref PutLiquidState __state)
        {
            __state = CapturePutState(__instance, containerStack, liquidStack);
        }

        [HarmonyPostfix]
        public static void Postfix(
            BlockLiquidContainerBase __instance,
            ItemStack containerStack,
            int __result,
            PutLiquidState __state)
        {
            if (__result <= 0 || __state == null || !__state.HadSink)
            {
                return;
            }

            ItemStack? content = __instance.GetContent(containerStack);
            if (content == null)
            {
                return;
            }

            ProsequorLiquidPedigree.ApplyMerge(
                content,
                __state.SourceBlob,
                __state.SinkQtyBefore,
                __result);
            __instance.SetContent(containerStack, content);
        }
    }

    [HarmonyPatch(
        typeof(BlockLiquidContainerBase),
        nameof(BlockLiquidContainerBase.TryPutLiquid),
        typeof(BlockPos),
        typeof(ItemStack),
        typeof(float))]
    public static class TryPutLiquidBlockPosPatch
    {
        [HarmonyPrefix]
        public static void Prefix(
            BlockLiquidContainerBase __instance,
            BlockPos pos,
            ItemStack liquidStack,
            ref PutLiquidState __state)
        {
            __state = new PutLiquidState();
            // Capture the pour even when the vessel is empty. Placed SetContent
            // goes through TryPutInto, which can drop pedigree on the stored stack.
            if (liquidStack != null)
            {
                ProsequorStackPedigree.AbsorbLegacySurface(liquidStack);
                ProsequorLiquidPedigree.EnsureHomogeneous(liquidStack);
                _ = ProsequorStackPedigree.TryGetPrimaryBlob(liquidStack, out __state.SourceBlob);
            }

            ItemStack? content = __instance.GetContent(pos);
            if (content == null)
            {
                return;
            }

            __state.HadSink = true;
            __state.SinkQtyBefore = content.StackSize;
        }

        [HarmonyPostfix]
        public static void Postfix(
            BlockLiquidContainerBase __instance,
            BlockPos pos,
            int __result,
            PutLiquidState __state)
        {
            if (__result <= 0 || __state == null || pos == null)
            {
                return;
            }

            // New liquid dilutes the mash; the next distill must roll a fresh batch.
            BoilerDistillBatch.Clear(BoilerProcessStarterPatches.TryGetBoiler(__instance, pos));

            ItemStack? content = __instance.GetContent(pos);
            if (content == null)
            {
                return;
            }

            if (!__state.HadSink)
            {
                if (__state.SourceBlob.HasPersistable)
                {
                    ProsequorLiquidPedigree.WriteFullBlob(content, __state.SourceBlob);
                }
            }
            else
            {
                ProsequorLiquidPedigree.ApplyMerge(
                    content,
                    __state.SourceBlob,
                    __state.SinkQtyBefore,
                    __result);
            }
        }
    }

    [HarmonyPatch(
        typeof(BlockLiquidContainerBase),
        nameof(BlockLiquidContainerBase.TryTakeContent),
        typeof(ItemStack),
        typeof(int))]
    public static class TryTakeContentItemStackPatch
    {
        [HarmonyPostfix]
        public static void Postfix(
            BlockLiquidContainerBase __instance,
            ItemStack containerStack,
            ItemStack __result)
        {
            if (__result != null)
            {
                ProsequorLiquidPedigree.EnsureHomogeneous(__result);
            }

            ItemStack? remain = __instance.GetContent(containerStack);
            if (remain != null)
            {
                ProsequorLiquidPedigree.EnsureHomogeneous(remain);
                __instance.SetContent(containerStack, remain);
            }
        }
    }

    [HarmonyPatch(
        typeof(BlockLiquidContainerBase),
        nameof(BlockLiquidContainerBase.TryTakeContent),
        typeof(BlockPos),
        typeof(int))]
    public static class TryTakeContentBlockPosPatch
    {
        [HarmonyPostfix]
        public static void Postfix(
            BlockLiquidContainerBase __instance,
            BlockPos pos,
            ItemStack __result)
        {
            if (__result != null)
            {
                ProsequorLiquidPedigree.EnsureHomogeneous(__result);
            }

            ItemStack? remain = __instance.GetContent(pos);
            if (remain != null)
            {
                ProsequorLiquidPedigree.EnsureHomogeneous(remain);
            }
        }
    }

    /// <summary>
    /// Equal-fill jug stack: containers merge while nested content stays one shared stack.
    /// Dilute that nested portion by jug×litre weights so different prestige becomes a well.
    /// </summary>
    [HarmonyPatch(typeof(BlockLiquidContainerBase), nameof(BlockLiquidContainerBase.TryMergeStacks))]
    public static class LiquidContainerTryMergeStacksPatch
    {
        [HarmonyPrefix]
        public static void Prefix(ItemStackMergeOperation op, ref ContainerMergeState __state)
        {
            __state = new ContainerMergeState();
            ItemStack? sink = op?.SinkSlot?.Itemstack;
            ItemStack? source = op?.SourceSlot?.Itemstack;
            if (sink == null
                || source == null
                || sink.Collectible is not BlockLiquidContainerBase sinkBlock
                || source.Collectible is not BlockLiquidContainerBase)
            {
                return;
            }

            ItemStack? sinkContent = sinkBlock.GetContent(sink);
            ItemStack? sourceContent = sinkBlock.GetContent(source);
            if (sinkContent == null || sourceContent == null)
            {
                return;
            }

            // Equal-fill path only (same litres per jug). Unequal pour uses TryPutLiquid.
            if (Math.Abs(
                    sinkBlock.GetCurrentLitres(sink)
                    - sinkBlock.GetCurrentLitres(source))
                > 0.0001f)
            {
                return;
            }

            __state.HadEqualFillContents = true;
            __state.SinkJugsBefore = sink.StackSize;
            __state.ContentItemsPerJug = sinkContent.StackSize;
            ProsequorStackPedigree.AbsorbLegacySurface(sourceContent);
            ProsequorLiquidPedigree.EnsureHomogeneous(sourceContent);
            _ = ProsequorStackPedigree.TryGetPrimaryBlob(sourceContent, out __state.SourceBlob);
        }

        [HarmonyPostfix]
        public static void Postfix(ItemStackMergeOperation op, ContainerMergeState __state)
        {
            if (op == null
                || __state == null
                || !__state.HadEqualFillContents
                || op.MovedQuantity <= 0)
            {
                return;
            }

            ItemStack? sink = op.SinkSlot?.Itemstack;
            if (sink?.Collectible is not BlockLiquidContainerBase sinkBlock)
            {
                return;
            }

            ItemStack? content = sinkBlock.GetContent(sink);
            if (content == null || __state.ContentItemsPerJug <= 0)
            {
                return;
            }

            int sinkQtyBefore = __state.SinkJugsBefore * __state.ContentItemsPerJug;
            int movedQty = op.MovedQuantity * __state.ContentItemsPerJug;
            ProsequorLiquidPedigree.ApplyMerge(content, __state.SourceBlob, sinkQtyBefore, movedQty);
            sinkBlock.SetContent(sink, content);
        }
    }

    [HarmonyPatch(typeof(BlockEntityCondenser), nameof(BlockEntityCondenser.ReceiveDistillate))]
    public static class CondenserReceiveDistillatePatch
    {
        [HarmonyPrefix]
        public static void Prefix(
            BlockEntityCondenser __instance,
            ItemSlot sourceSlot,
            DistillationProps props,
            ref DistillateState __state)
        {
            __state = new DistillateState();
            _ = props;
            if (__instance.Inventory.Count < 2 || __instance.Inventory[1].Empty)
            {
                return;
            }

            ItemStack? bucket = __instance.Inventory[1].Itemstack;
            if (bucket?.Collectible is not BlockLiquidContainerTopOpened open)
            {
                return;
            }

            __state.HadBucket = true;
            __state.BucketStack = bucket;
            ItemStack? content = open.GetContent(bucket);
            __state.SinkQtyBefore = content?.StackSize ?? 0;

            // Mash pedigree: spend rank as a quality-base addend; spirit keeps maker / other affixes.
            ItemStack? mash = sourceSlot?.Itemstack;
            if (mash != null)
            {
                ProsequorStackPedigree.AbsorbLegacySurface(mash);
                if (ProsequorStackPedigree.TryGetPrimaryBlob(mash, out ProsequorBlob mashBlob))
                {
                    __state.MashQualityRank = mashBlob.QualityRank;
                    __state.SourceBlob = ProsequorLiquidPedigree.ForDistillate(mashBlob);
                }
            }

            // Quality actor = still pourer (boiler sole contributor), else mash MakerUid.
            // First distill locks the roll on the still; later ticks copy that batch.
            __state.QualityUid = BoilerProcessStarterPatches.TryGetDistillQualityUid(
                __instance,
                sourceSlot,
                __state.SourceBlob.MakerUid);
            if (!string.IsNullOrEmpty(__state.QualityUid)
                && string.IsNullOrEmpty(__state.SourceBlob.MakerUid))
            {
                __state.SourceBlob = __state.SourceBlob.WithMaker(__state.QualityUid);
            }
        }

        [HarmonyPostfix]
        public static void Postfix(
            BlockEntityCondenser __instance,
            ItemSlot sourceSlot,
            bool __result,
            DistillateState __state)
        {
            if (!__result || __state == null || !__state.HadBucket || __state.BucketStack == null)
            {
                return;
            }

            if (__state.BucketStack.Collectible is not BlockLiquidContainerTopOpened open)
            {
                return;
            }

            ItemStack? content = open.GetContent(__state.BucketStack);
            if (content == null || content.StackSize <= __state.SinkQtyBefore)
            {
                // No litres added (client-only path, full, or mismatch).
                return;
            }

            int moved = content.StackSize - __state.SinkQtyBefore;
            float mashBonus = ProsequorLiquidPedigree.QualityBaseBonus(__state.MashQualityRank);
            ProsequorBlob occupied = ProsequorBlob.Empty;
            bool sinkOccupied = __state.SinkQtyBefore > 0;
            if (sinkOccupied)
            {
                _ = ProsequorStackPedigree.TryGetPrimaryBlob(content, out occupied);
            }

            _ = BoilerProcessStarterPatches.TryResolveBoilerForSourceSlot(
                __instance,
                sourceSlot,
                out BlockEntityBoiler? boiler);
            ProsequorBlob batch = BoilerDistillBatch.Resolve(
                boiler,
                __instance.Api.World,
                content,
                __state.QualityUid,
                __state.SourceBlob,
                mashBonus,
                occupied,
                sinkOccupied);
            BoilerDistillBatch.ApplyToPortion(content, batch, __state.SinkQtyBefore, moved);
            ProsequorLiquidPedigree.StripQualityAndRank(content);
            open.SetContent(__state.BucketStack, content);
            // Pay the new drops only. The bucket total would compound each tick.
            CraftedProductXp.Emit(__instance.Api.World, __state.QualityUid, content, moved);
        }
    }

    static PutLiquidState CapturePutState(
        BlockLiquidContainerBase container,
        ItemStack containerStack,
        ItemStack? liquidStack)
    {
        PutLiquidState state = new();
        ItemStack? content = container.GetContent(containerStack);
        if (content == null)
        {
            return state;
        }

        state.HadSink = true;
        state.SinkQtyBefore = content.StackSize;
        if (liquidStack != null)
        {
            ProsequorStackPedigree.AbsorbLegacySurface(liquidStack);
            ProsequorLiquidPedigree.EnsureHomogeneous(liquidStack);
            _ = ProsequorStackPedigree.TryGetPrimaryBlob(liquidStack, out state.SourceBlob);
        }

        return state;
    }
}
