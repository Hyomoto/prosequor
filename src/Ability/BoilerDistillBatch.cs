using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;

namespace Prosequor.Ability;

/// <summary>
/// One quality roll per still load. The first distill after a pour locks the spirit
/// blob on the boiler Live pedigree; later ticks copy it. Adding liquid clears the lock.
/// </summary>
public static class BoilerDistillBatch
{
    public const string Attr = "prosequorDistillBatch";
    public const string LockedKey = "locked";

    public static bool TryGet(BlockEntity? be, out ProsequorBlob blob)
    {
        blob = ProsequorBlob.Empty;
        if (be == null
            || !ProsequorBlockPedigreeStation.TryGetBox(be, out ProsequorChunkPedigree.Box box)
            || !box.DistillLocked)
        {
            return false;
        }

        blob = box.Blob;
        return true;
    }

    /// <summary>
    /// Locks <paramref name="blob"/> on the still. Returns the blob actually stored,
    /// including the pourer's sole contributor when the roll only stamped a maker.
    /// </summary>
    public static ProsequorBlob Store(BlockEntity? be, ProsequorBlob blob)
    {
        ProsequorBlob next = blob ?? ProsequorBlob.Empty;
        if (be == null)
        {
            return next;
        }

        if (ProsequorBlockPedigreeStation.TryGetBox(be, out ProsequorChunkPedigree.Box box)
            && box.Blob.TryGetSoleContributor(out string? sole)
            && !next.TryGetSoleContributor(out _))
        {
            next = next.WithSoleContributor(sole);
        }

        ProsequorBlob committed = next;
        ProsequorBlockPedigreeStation.Mutate(be, b =>
        {
            b.DistillLocked = true;
            b.Blob = committed;
        });
        return committed;
    }

    public static void Clear(BlockEntity? be)
    {
        if (be == null
            || !ProsequorBlockPedigreeStation.TryGetBox(be, out ProsequorChunkPedigree.Box box)
            || !box.DistillLocked)
        {
            return;
        }

        ProsequorBlockPedigreeStation.Mutate(be, b =>
        {
            b.DistillLocked = false;
            b.Blob = ProsequorBlob.Empty;
        });
    }

    /// <summary>Tree helpers for pure tests / legacy distill side-table shape.</summary>
    public static void WriteLocked(ITreeAttribute tree, ProsequorBlob blob)
    {
        if (tree == null)
        {
            return;
        }

        ITreeAttribute node = tree.GetOrAddTreeAttribute(Attr);
        node.SetBool(LockedKey, true);
        blob.WriteTo(node);
    }

    /// <summary>
    /// True when a batch is locked, including a roll that stamped nothing.
    /// </summary>
    public static bool TryReadLocked(ITreeAttribute? tree, out ProsequorBlob blob)
    {
        blob = ProsequorBlob.Empty;
        ITreeAttribute? node = tree?.GetTreeAttribute(Attr);
        if (node == null || !node.GetBool(LockedKey, defaultValue: false))
        {
            return false;
        }

        blob = ProsequorBlob.ReadFrom(node);
        return true;
    }

    /// <summary>
    /// First distill after a pour rolls once and locks the result on the still.
    /// A later tick returns that lock and does not call apply-quality.
    /// </summary>
    public static ProsequorBlob Resolve(
        BlockEntity? boiler,
        IWorldAccessor? world,
        ItemStack? spiritTemplate,
        string? qualityUid,
        ProsequorBlob mashBlob,
        float mashBonus,
        ProsequorBlob occupiedSinkBlob,
        bool sinkOccupied)
    {
        if (TryGet(boiler, out ProsequorBlob stored))
        {
            return stored;
        }

        if (boiler == null && sinkOccupied && occupiedSinkBlob.HasPersistable)
        {
            return occupiedSinkBlob;
        }

        ProsequorBlob rolled = Roll(world, spiritTemplate, qualityUid, mashBlob, mashBonus);
        Store(boiler, rolled);
        // Store may graft the pourer's sole contributor onto the lock; stamp that blob.
        return TryGet(boiler, out ProsequorBlob locked) ? locked : rolled;
    }

    /// <summary>
    /// Stamp the locked batch onto the distillate. Empty sink is a full write;
    /// an occupied sink merges the same blob so prestige cannot drift per tick.
    /// </summary>
    public static void ApplyToPortion(
        ItemStack? content,
        ProsequorBlob batch,
        int sinkQtyBefore,
        int moved)
    {
        if (content?.Attributes == null || moved <= 0 || !batch.HasPersistable)
        {
            return;
        }

        if (sinkQtyBefore <= 0 || !SinkHasPrestige(content))
        {
            ProsequorLiquidPedigree.WriteFullBlob(content, batch);
            return;
        }

        ItemStack incoming = content.Clone();
        incoming.StackSize = moved;
        ProsequorLiquidPedigree.WriteFullBlob(incoming, batch);
        ProsequorBlob incomingBlob = ProsequorBlob.Empty;
        _ = ProsequorStackPedigree.TryGetPrimaryBlob(incoming, out incomingBlob);
        ProsequorLiquidPedigree.ApplyMerge(content, incomingBlob, sinkQtyBefore, moved);
    }

    static bool SinkHasPrestige(ItemStack content)
    {
        return ProsequorStackPedigree.TryGetPrimaryBlob(content, out ProsequorBlob sink)
            && (!string.IsNullOrEmpty(sink.MakerUid) || sink.Affixes.Count > 0);
    }

    static ProsequorBlob Roll(
        IWorldAccessor? world,
        ItemStack? spiritTemplate,
        string? qualityUid,
        ProsequorBlob mashBlob,
        float mashBonus)
    {
        if (spiritTemplate == null)
        {
            return WithQualityMaker(mashBlob, qualityUid);
        }

        ItemStack scratch = spiritTemplate.Clone();
        scratch.StackSize = 1;
        scratch.Attributes ??= new TreeAttribute();
        ProsequorStackPedigree.ClearAll(scratch);
        ItemAffixes.WriteAll(scratch, Array.Empty<ItemAffixEntry>());
        CraftAttributeMods.WriteAll(scratch, Array.Empty<ProsequorBlob.ModFactor>());

        if (mashBlob.HasPersistable)
        {
            ProsequorLiquidPedigree.WriteFullBlob(scratch, mashBlob);
        }

        if (!string.IsNullOrEmpty(qualityUid))
        {
            CraftAttribution.StampMakerUid(scratch, qualityUid);
        }

        if (world != null && !string.IsNullOrEmpty(qualityUid))
        {
            _ = CraftMutateOutputStation.TryApplyQuality(world, qualityUid, scratch, mashBonus);
        }

        if (!string.IsNullOrEmpty(qualityUid))
        {
            CraftAttribution.StampMakerUid(scratch, qualityUid);
        }

        ProsequorLiquidPedigree.StripQualityAndRank(scratch);

        ProsequorBlob batch = ProsequorBlob.Empty;
        _ = ProsequorStackPedigree.TryGetPrimaryBlob(scratch, out batch);
        return WithQualityMaker(batch, qualityUid);
    }

    static ProsequorBlob WithQualityMaker(ProsequorBlob blob, string? qualityUid)
    {
        if (string.IsNullOrEmpty(qualityUid) || !string.IsNullOrEmpty(blob.MakerUid))
        {
            return blob;
        }

        return blob.WithMaker(qualityUid);
    }
}
