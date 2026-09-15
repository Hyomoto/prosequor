using Vintagestory.API.Common;
using Vintagestory.GameContent;

namespace Prosequor.Ability;

/// <summary>
/// Vessel maker is the potter. The cook is a contributor only while a meal is in the
/// pot, bowl, or crock. Removing the meal keeps the maker and drops the cook.
/// </summary>
public static class MealHostCredit
{
    public static bool IsMealVessel(ItemStack? stack) =>
        stack?.Collectible is BlockCookedContainer or BlockMeal or BlockCrock;

    public static bool IsMealCreditHost(ItemStack? stack) => IsMealVessel(stack);

    /// <summary>True while the stack still holds a meal (servings left).</summary>
    public static bool StillHasMeal(ItemStack? stack)
    {
        if (stack?.Attributes == null)
        {
            return false;
        }

        return stack.Attributes.GetDecimal("quantityServings", 0.0) > 0.001;
    }

    /// <summary>
    /// Keep dest maker, else stamp <paramref name="vesselMaker"/>. Copy quality from
    /// <paramref name="source"/> only when dest has none. Add source cooks and
    /// <paramref name="cookUid"/> at weight 1 if absent. Never copies source maker.
    /// </summary>
    public static ProsequorBlob AttachMeal(
        ProsequorBlob dest,
        ProsequorBlob source,
        string? vesselMaker,
        string? cookUid = null)
    {
        ProsequorBlob next = dest;
        if (!dest.HasSurface && source.HasSurface)
        {
            next = next
                .WithAffixes(source.Affixes)
                .WithMods(source.Mods)
                .WithQualityRank(source.QualityRank);
        }

        if (string.IsNullOrEmpty(next.MakerUid) && IsPlayerUid(vesselMaker))
        {
            next = next.WithMaker(vesselMaker);
        }

        next = AddCookShare(next, cookUid);
        IReadOnlyList<ProsequorBlob.Share> shares = source.Contributors;
        for (int i = 0; i < shares.Count; i++)
        {
            next = AddCookShare(next, shares[i].PlayerUid);
        }

        return next;
    }

    /// <summary>Drop cook credit and meal surface. Maker stays.</summary>
    public static ProsequorBlob DetachMeal(ProsequorBlob blob) =>
        blob.WithClearedContributors()
            .WithAffixes(Array.Empty<ItemAffixEntry>())
            .WithMods(Array.Empty<ProsequorBlob.ModFactor>())
            .WithQualityRank(0);

    public static ProsequorBlob MergeServe(ProsequorBlob dest, ProsequorBlob source, string? vesselMaker) =>
        AttachMeal(dest, source, vesselMaker);

    /// <summary>Add the cooker at weight 1 if absent. No-op on non-vessels and blank / <c>@</c> uids.</summary>
    public static void AddCook(ItemStack? stack, string? cookerUid)
    {
        if (!IsMealVessel(stack) || !IsPlayerUid(cookerUid))
        {
            return;
        }

        if (ProsequorStackPedigree.TryGetPrimaryBlob(stack, out ProsequorBlob blob)
            && HasContributor(blob, cookerUid))
        {
            return;
        }

        ProsequorStackPedigree.AddContributor(stack, cookerUid, 1);
    }

    public static void AttachToStack(
        ItemStack? meal,
        ProsequorBlob source,
        string? vesselMaker,
        IWorldAccessor? world,
        string? cookUid = null)
    {
        if (meal?.Collectible == null || meal.Attributes == null)
        {
            return;
        }

        ProsequorBlob dest = ProsequorStackPedigree.TryGetPrimaryBlob(meal, out ProsequorBlob existing)
            ? existing
            : ProsequorBlob.Empty;
        ProsequorBlob next = AttachMeal(dest, source, vesselMaker, cookUid);
        bool copiedSurface = !dest.HasSurface && next.HasSurface;
        if (next.HasPersistable && !next.Equals(dest))
        {
            ProsequorStackPedigree.ApplyUnitBlob(meal, next);
        }

        if (copiedSurface)
        {
            AbilityBootstrap.AttributeMutators.Rematerialize(meal, world);
        }
    }

    public static void AttachToBe(
        BlockEntity? mealBe,
        ProsequorBlob source,
        string? vesselMaker,
        IWorldAccessor? world,
        string? cookUid = null)
    {
        if (mealBe == null)
        {
            return;
        }

        ProsequorBlob dest = ProsequorBlockPedigreeStation.TryGetBlob(mealBe, out ProsequorBlob existing)
            ? existing
            : ProsequorBlob.Empty;
        ProsequorBlob next = AttachMeal(dest, source, vesselMaker, cookUid);
        if (!next.HasPersistable || next.Equals(dest))
        {
            return;
        }

        ProsequorBlockPedigreeStation.ApplyBlob(mealBe, next);
        if (dest.HasSurface || !next.HasSurface || mealBe is not BlockEntityContainer container
            || container.Inventory == null)
        {
            return;
        }

        for (int i = 0; i < container.Inventory.Count; i++)
        {
            ItemStack? stack = container.Inventory[i]?.Itemstack;
            if (stack == null)
            {
                continue;
            }

            AbilityBootstrap.AttributeMutators.Rematerialize(stack, world);
        }
    }

    public static void ApplyServeToStack(
        ItemStack? meal,
        ProsequorBlob source,
        string? vesselMaker,
        IWorldAccessor? world) =>
        AttachToStack(meal, source, vesselMaker, world);

    public static void ApplyServeToBe(
        BlockEntity? mealBe,
        ProsequorBlob source,
        string? vesselMaker,
        IWorldAccessor? world) =>
        AttachToBe(mealBe, source, vesselMaker, world);

    /// <summary>
    /// Put <paramref name="snapshotMaker"/> back on <paramref name="stack"/>.
    /// Empty snapshot clears a maker a serve wrote. No-op when unchanged.
    /// </summary>
    public static void RestoreMaker(ItemStack? stack, string? snapshotMaker)
    {
        if (stack?.Attributes == null)
        {
            return;
        }

        string? now = CraftAttribution.TryGetMakerUid(stack);
        string? wanted = IsPlayerUid(snapshotMaker) ? snapshotMaker!.Trim() : null;
        if (string.Equals(now, wanted, StringComparison.Ordinal))
        {
            return;
        }

        if (wanted == null)
        {
            ProsequorStackPedigree.ClearMaker(stack);
            return;
        }

        ProsequorStackPedigree.StampMaker(stack, wanted);
    }

    public static void RestoreBeMaker(BlockEntity? be, string? snapshotMaker, ProsequorBlob source)
    {
        if (be == null)
        {
            return;
        }

        bool has = ProsequorBlockPedigreeStation.TryGetBlob(be, out ProsequorBlob current);
        string? now = has ? current.MakerUid : null;
        string? wanted = IsPlayerUid(snapshotMaker) ? snapshotMaker!.Trim() : null;
        if (string.Equals(now, wanted, StringComparison.Ordinal) && (has || wanted == null))
        {
            return;
        }

        ProsequorBlob restored = (has ? current : source).WithMaker(wanted);
        if (!restored.HasPersistable)
        {
            ProsequorBlockPedigreeStation.Clear(be);
            return;
        }

        ProsequorBlockPedigreeStation.ApplyBlob(be, restored);
    }

    /// <summary>Stamp <paramref name="makerUid"/> onto a replacement empty vessel if it has none.</summary>
    public static void RetainMaker(ItemStack? stack, string? makerUid)
    {
        if (stack?.Attributes == null || !IsPlayerUid(makerUid))
        {
            return;
        }

        if (!string.IsNullOrEmpty(CraftAttribution.TryGetMakerUid(stack)))
        {
            return;
        }

        ProsequorStackPedigree.StampMaker(stack, makerUid!.Trim());
    }

    public static void RetainBeMaker(BlockEntity? be, string? makerUid)
    {
        if (be == null || !IsPlayerUid(makerUid))
        {
            return;
        }

        if (ProsequorBlockPedigreeStation.TryGetBlob(be, out ProsequorBlob blob)
            && !string.IsNullOrEmpty(blob.MakerUid))
        {
            return;
        }

        ProsequorBlockPedigreeStation.StampPlanter(be, makerUid);
    }

    public static void ApplyDetach(ItemStack? stack)
    {
        if (stack?.Attributes == null
            || !ProsequorStackPedigree.TryGetPrimaryBlob(stack, out ProsequorBlob blob))
        {
            return;
        }

        ProsequorBlob next = DetachMeal(blob);
        if (next.Equals(blob))
        {
            return;
        }

        if (!next.HasPersistable)
        {
            ProsequorStackPedigree.ClearAll(stack);
            return;
        }

        ProsequorStackPedigree.ApplyUnitBlob(stack, next);
    }

    public static void ApplyDetach(BlockEntity? be)
    {
        if (be == null || !ProsequorBlockPedigreeStation.TryGetBlob(be, out ProsequorBlob blob))
        {
            return;
        }

        ProsequorBlob next = DetachMeal(blob);
        if (next.Equals(blob))
        {
            return;
        }

        if (!next.HasPersistable)
        {
            ProsequorBlockPedigreeStation.Clear(be);
            return;
        }

        ProsequorBlockPedigreeStation.ApplyBlob(be, next);
    }

    /// <summary>
    /// Meal left <paramref name="after"/>. Same stack: strip cook credit. Replaced stack:
    /// stamp the prior maker only.
    /// </summary>
    public static void AfterMealGone(ItemStack? before, ItemStack? after, string? makerUid = null)
    {
        if (after == null || StillHasMeal(after))
        {
            return;
        }

        string? maker = IsPlayerUid(makerUid) ? makerUid : CraftAttribution.TryGetMakerUid(before);
        if (ReferenceEquals(before, after))
        {
            ApplyDetach(after);
            RetainMaker(after, maker);
            return;
        }

        RetainMaker(after, maker);
        ApplyDetach(after);
    }

    public static bool HasContributor(ProsequorBlob blob, string? uid)
    {
        if (!IsPlayerUid(uid))
        {
            return false;
        }

        string trimmed = uid!.Trim();
        IReadOnlyList<ProsequorBlob.Share> shares = blob.Contributors;
        for (int i = 0; i < shares.Count; i++)
        {
            if (string.Equals(shares[i].PlayerUid, trimmed, StringComparison.Ordinal) && shares[i].Weight > 0)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>True for a real player uid. Blank and <c>@</c> system tokens are not credit.</summary>
    public static bool IsPlayerUid(string? uid)
    {
        if (string.IsNullOrWhiteSpace(uid))
        {
            return false;
        }

        string trimmed = uid.Trim();
        return trimmed.Length > 0 && trimmed[0] != '@';
    }

    static ProsequorBlob AddCookShare(ProsequorBlob blob, string? uid)
    {
        if (!IsPlayerUid(uid) || HasContributor(blob, uid))
        {
            return blob;
        }

        return blob.WithContributor(uid, 1);
    }
}
