using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;

namespace Prosequor.Ability;

/// <summary>
/// Size-1 Live pedigree on <see cref="Entity.WatchedAttributes"/> under
/// <see cref="ProsequorStackPedigree.LiveAttr"/>. Persist and client sync are free;
/// crate catch/release carries the bag via <c>ToBytes</c>.
/// </summary>
public static class ProsequorEntityPedigreeStation
{
    /// <summary>
    /// Live blob from watched attrs, or false when missing/anonymous.
    /// </summary>
    public static bool TryGetBlob(Entity? entity, out ProsequorBlob blob)
    {
        blob = ProsequorBlob.Empty;
        if (entity?.WatchedAttributes == null)
        {
            return false;
        }

        return TryReadLive(entity.WatchedAttributes, out blob);
    }

    /// <summary>
    /// Sets maker, preserving existing contributors and recipe (stack-style stamp).
    /// </summary>
    public static void StampMaker(Entity? entity, string? makerUid)
    {
        if (entity?.WatchedAttributes == null || string.IsNullOrWhiteSpace(makerUid))
        {
            return;
        }

        ProsequorBlob current = ReadOrEmpty(entity.WatchedAttributes);
        WriteLive(entity, current.WithMaker(makerUid.Trim()));
    }

    /// <summary>
    /// Clear maker only. Contributors stay. Anonymous after clear → removes Live.
    /// </summary>
    public static void ClearMaker(Entity? entity)
    {
        if (entity?.WatchedAttributes == null
            || !TryGetBlob(entity, out ProsequorBlob blob))
        {
            return;
        }

        ApplyBlob(entity, blob.WithMaker(null));
    }

    /// <summary>Increments contributor weight (default +1).</summary>
    public static void AddContributor(Entity? entity, string? contributorUid, int amount = 1)
    {
        if (entity?.WatchedAttributes == null
            || string.IsNullOrWhiteSpace(contributorUid)
            || amount <= 0)
        {
            return;
        }

        ProsequorBlob current = ReadOrEmpty(entity.WatchedAttributes);
        WriteLive(entity, current.WithContributor(contributorUid.Trim(), amount));
    }

    /// <summary>
    /// Clears contributor weights while preserving maker, recipe, and friendliness-ready.
    /// Anonymous after clear → removes Live.
    /// </summary>
    public static void ClearContributors(Entity? entity)
    {
        if (entity?.WatchedAttributes == null
            || !TryGetBlob(entity, out ProsequorBlob blob))
        {
            return;
        }

        ApplyBlob(entity, blob.WithClearedContributors());
    }

    /// <summary>Removes Live pedigree from watched attrs.</summary>
    public static void Clear(Entity? entity)
    {
        if (entity?.WatchedAttributes == null
            || !entity.WatchedAttributes.HasAttribute(ProsequorStackPedigree.LiveAttr))
        {
            return;
        }

        entity.WatchedAttributes.RemoveAttribute(ProsequorStackPedigree.LiveAttr);
        entity.WatchedAttributes.MarkPathDirty(ProsequorStackPedigree.LiveAttr);
    }

    /// <summary>
    /// Copy primary stack blob onto the entity (place / release). No-op when empty.
    /// </summary>
    public static void CaptureFromStack(Entity? entity, ItemStack? stack)
    {
        if (entity?.WatchedAttributes == null)
        {
            return;
        }

        ProsequorStackPedigree.AbsorbLegacySurface(stack);
        if (!ProsequorStackPedigree.TryGetPrimaryBlob(stack, out ProsequorBlob blob))
        {
            return;
        }

        WriteLive(entity, blob);
    }

    /// <summary>
    /// Stamp entity blob onto a stack with no Live/Frozen (pickup / drop). Skips stacks
    /// that already carry pedigree.
    /// </summary>
    public static void ApplyToStack(Entity? entity, ItemStack? stack)
    {
        if (stack?.Attributes == null
            || stack.StackSize <= 0
            || ProsequorStackPedigree.HasLive(stack)
            || ProsequorStackPedigree.HasFrozen(stack)
            || !TryGetBlob(entity, out ProsequorBlob blob))
        {
            return;
        }

        ProsequorStackPedigree.ApplyUnitBlob(stack, blob);
    }

    /// <summary>
    /// Identity-preserving host replace (e.g. grow child → adult). No-op when source
    /// is anonymous or either entity is null.
    /// </summary>
    public static void Copy(Entity? from, Entity? to)
    {
        if (to?.WatchedAttributes == null || !TryGetBlob(from, out ProsequorBlob blob))
        {
            return;
        }

        WriteLive(to, blob);
    }

    /// <summary>
    /// Write a known blob onto the entity (e.g. ItemCreature place after TakeOut consumed
    /// the stack). Anonymous → clear.
    /// </summary>
    public static void ApplyBlob(Entity? entity, ProsequorBlob blob)
    {
        if (entity?.WatchedAttributes == null)
        {
            return;
        }

        if (blob.IsAnonymous)
        {
            Clear(entity);
            return;
        }

        WriteLive(entity, blob);
    }

    /// <summary>Tree-contract write used by fixtures (same shape as WatchedAttributes Live).</summary>
    public static void WriteToTree(ITreeAttribute? tree, ProsequorBlob blob)
    {
        if (tree == null || blob.IsAnonymous)
        {
            return;
        }

        blob.WriteTo(tree.GetOrAddTreeAttribute(ProsequorStackPedigree.LiveAttr));
    }

    /// <summary>Tree-contract read used by fixtures.</summary>
    public static bool TryReadFromTree(ITreeAttribute? tree, out ProsequorBlob blob) =>
        TryReadLive(tree, out blob);

    static bool TryReadLive(ITreeAttribute? attrs, out ProsequorBlob blob)
    {
        blob = ProsequorBlob.Empty;
        if (attrs == null)
        {
            return false;
        }

        ITreeAttribute? live = attrs.GetTreeAttribute(ProsequorStackPedigree.LiveAttr);
        if (live == null)
        {
            return false;
        }

        blob = ProsequorBlob.ReadFrom(live);
        return !blob.IsAnonymous;
    }

    static ProsequorBlob ReadOrEmpty(ITreeAttribute attrs)
    {
        ITreeAttribute? live = attrs.GetTreeAttribute(ProsequorStackPedigree.LiveAttr);
        return live == null ? ProsequorBlob.Empty : ProsequorBlob.ReadFrom(live);
    }

    static void WriteLive(Entity entity, ProsequorBlob blob)
    {
        if (blob.IsAnonymous)
        {
            Clear(entity);
            return;
        }

        ITreeAttribute live = entity.WatchedAttributes.GetOrAddTreeAttribute(ProsequorStackPedigree.LiveAttr);
        blob.WriteTo(live);
        entity.WatchedAttributes.MarkPathDirty(ProsequorStackPedigree.LiveAttr);
    }
}
