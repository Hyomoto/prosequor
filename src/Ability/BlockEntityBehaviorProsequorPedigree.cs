using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;

namespace Prosequor.Ability;

/// <summary>
/// Client view of <see cref="ProsequorChunkPedigree"/>. Chunk moddata is the save
/// record and is not resent by <see cref="BlockEntity.MarkDirty"/>; this behavior
/// rides that packet so tooltips can read the same box.
/// </summary>
public sealed class BlockEntityBehaviorProsequorPedigree : BlockEntityBehavior
{
    public const string MirrorAttr = "prosequorPedigreeMirror";

    public ProsequorChunkPedigree.Box Box = new();

    /// <summary>True after a server publish. An empty box then means cleared, not "unknown".</summary>
    public bool HasMirror;

    public BlockEntityBehaviorProsequorPedigree(BlockEntity blockentity)
        : base(blockentity)
    {
    }

    public override void ToTreeAttributes(ITreeAttribute tree)
    {
        if (!HasMirror)
        {
            return;
        }

        tree.SetBool(MirrorAttr, true);
        if (Box.HasPersistable)
        {
            Box.WriteTo(tree);
        }
    }

    public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldAccessForResolve)
    {
        if (!tree.GetBool(MirrorAttr))
        {
            return;
        }

        ProsequorChunkPedigree.Box box = new();
        box.ReadFrom(tree);
        Box = box;
        HasMirror = true;
    }
}
