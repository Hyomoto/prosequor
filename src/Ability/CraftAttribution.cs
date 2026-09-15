using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;

namespace Prosequor.Ability;

/// <summary>
/// Facade for craft/fire attribution on ItemStacks. New stamps go to Live/Frozen pedigree;
/// legacy flat <c>prosequorMadeBy</c> / <c>prosequorFiredBy</c> are compat-read and promoted.
/// Firer stamps add contribution weight (not a singular “last firer” role).
/// </summary>
public static class CraftAttribution
{
    /// <summary>Legacy flat maker key (compat-read / promote only; not written by new stamps).</summary>
    public const string MakerAttr = "prosequorMadeBy";

    /// <summary>Legacy flat firer key (compat-read / promote only; not written by new stamps).</summary>
    public const string FirerAttr = "prosequorFiredBy";

    public static void StampMaker(ItemStack? stack, IPlayer? player)
    {
        if (player?.PlayerUID == null)
        {
            return;
        }

        StampMakerUid(stack, player.PlayerUID);
    }

    public static void StampMakerUid(ItemStack? stack, string? makerUid)
    {
        if (stack == null || string.IsNullOrWhiteSpace(makerUid))
        {
            return;
        }

        // Fresh recipe outputs often have a null attribute tree. Allocating here is what
        // makes a stamp visible (Created By reads the pedigree, not a side channel).
        stack.Attributes ??= new TreeAttribute();
        ProsequorStackPedigree.StampMaker(stack, makerUid.Trim());
        ClearLegacyFlats(stack, clearMaker: true, clearFirer: false);
    }

    public static void StampRecipe(ItemStack? stack, string? recipeKey) =>
        ProsequorStackPedigree.StampRecipe(stack, recipeKey);

    public static void StampFirer(ItemStack? stack, IPlayer? player)
    {
        if (player?.PlayerUID == null)
        {
            return;
        }

        StampFirerUid(stack, player.PlayerUID);
    }

    /// <summary>Adds contribution weight for the firer (default +1).</summary>
    public static void StampFirerUid(ItemStack? stack, string? firerUid, int amount = 1)
    {
        if (stack?.Attributes == null || string.IsNullOrWhiteSpace(firerUid) || amount <= 0)
        {
            return;
        }

        ProsequorStackPedigree.AddContributor(stack, firerUid.Trim(), amount);
        ClearLegacyFlats(stack, clearMaker: false, clearFirer: true);
    }

    /// <summary>Adds contribution weight on kiln content (place materials / fuel / light).</summary>
    public static void AddContribution(ItemStack? stack, IPlayer? player, int amount = 1)
    {
        if (player?.PlayerUID == null)
        {
            return;
        }

        AddContributionUid(stack, player.PlayerUID, amount);
    }

    public static void AddContributionUid(ItemStack? stack, string? uid, int amount = 1)
    {
        if (stack?.Attributes == null || string.IsNullOrWhiteSpace(uid) || amount <= 0)
        {
            return;
        }

        ProsequorStackPedigree.AddContributor(stack, uid.Trim(), amount);
        ClearLegacyFlats(stack, clearMaker: false, clearFirer: true);
    }

    public static IPlayer? ResolveMaker(IWorldAccessor world, ItemStack? stack)
    {
        string? uid = TryGetMakerUid(stack);
        return string.IsNullOrEmpty(uid) || world == null ? null : world.PlayerByUid(uid);
    }

    /// <summary>
    /// Homogeneous stack with a maker UID → plain localized Created By line.
    /// Prefer <see cref="OwnerCredit.AppendForStack"/> for tooltip/block chrome.
    /// </summary>
    public static bool TryFormatCreatedByLine(IWorldAccessor? world, ItemStack? stack, out string line)
    {
        line = "";
        if (stack == null || !ProsequorStackPedigree.IsHomogeneous(stack))
        {
            return false;
        }

        return OwnerCredit.TryFormat(
            world,
            TryGetMakerUid(stack),
            OwnerCredit.CreatedByLang,
            out line);
    }

    public static string? TryGetMakerUid(ItemStack? stack)
    {
        if (stack?.Attributes == null)
        {
            return null;
        }

        if (ProsequorStackPedigree.TryGetPrimaryBlob(stack, out ProsequorBlob blob)
            && !string.IsNullOrEmpty(blob.MakerUid))
        {
            return blob.MakerUid;
        }

        string? legacy = NormalizeUid(stack.Attributes.GetString(MakerAttr));
        if (legacy == null)
        {
            return null;
        }

        PromoteLegacyFlats(stack);
        return legacy;
    }

    /// <summary>
    /// True when <paramref name="uid"/> has positive contribution weight on the stack pedigree
    /// (after promoting legacy flat firer if needed).
    /// </summary>
    public static bool HasContributor(ItemStack? stack, string? uid)
    {
        string? normalized = NormalizeUid(uid);
        if (stack?.Attributes == null || normalized == null)
        {
            return false;
        }

        if (ProsequorStackPedigree.TryGetPrimaryBlob(stack, out ProsequorBlob blob)
            && blob.TryGetContributorWeight(normalized, out _))
        {
            return true;
        }

        string? legacy = NormalizeUid(stack.Attributes.GetString(FirerAttr));
        if (legacy == null || !string.Equals(legacy, normalized, StringComparison.Ordinal))
        {
            return false;
        }

        PromoteLegacyFlats(stack);
        return ProsequorStackPedigree.TryGetPrimaryBlob(stack, out ProsequorBlob after)
            && after.TryGetContributorWeight(normalized, out _);
    }

    /// <summary>
    /// Compat read: legacy flat firer, else sole contributor when exactly one share exists.
    /// Prefer <see cref="HasContributor"/> / pedigree shares for kiln XP.
    /// </summary>
    public static string? TryGetFirerUid(ItemStack? stack)
    {
        if (stack?.Attributes == null)
        {
            return null;
        }

        if (ProsequorStackPedigree.TryGetPrimaryBlob(stack, out ProsequorBlob blob)
            && blob.Contributors.Count == 1)
        {
            return blob.Contributors[0].PlayerUid;
        }

        string? legacy = NormalizeUid(stack.Attributes.GetString(FirerAttr));
        if (legacy == null)
        {
            return null;
        }

        PromoteLegacyFlats(stack);
        return legacy;
    }

    /// <summary>
    /// One-shot: copy any legacy flat maker/firer into pedigree, then remove those flats.
    /// </summary>
    public static void PromoteLegacyFlats(ItemStack? stack)
    {
        if (stack?.Attributes == null)
        {
            return;
        }

        string? legacyMaker = NormalizeUid(stack.Attributes.GetString(MakerAttr));
        string? legacyFirer = NormalizeUid(stack.Attributes.GetString(FirerAttr));
        if (legacyMaker == null && legacyFirer == null)
        {
            return;
        }

        bool hasMaker = ProsequorStackPedigree.TryGetPrimaryBlob(stack, out ProsequorBlob blob)
            && !string.IsNullOrEmpty(blob.MakerUid);

        if (legacyMaker != null && !hasMaker)
        {
            ProsequorStackPedigree.StampMaker(stack, legacyMaker);
        }

        if (legacyFirer != null)
        {
            bool already = ProsequorStackPedigree.TryGetPrimaryBlob(stack, out ProsequorBlob after)
                && after.TryGetContributorWeight(legacyFirer, out _);
            if (!already)
            {
                ProsequorStackPedigree.AddContributor(stack, legacyFirer, 1);
            }
        }

        ClearLegacyFlats(stack, clearMaker: true, clearFirer: true);
    }

    static void ClearLegacyFlats(ItemStack stack, bool clearMaker, bool clearFirer)
    {
        if (clearMaker && stack.Attributes.HasAttribute(MakerAttr))
        {
            stack.Attributes.RemoveAttribute(MakerAttr);
        }

        if (clearFirer && stack.Attributes.HasAttribute(FirerAttr))
        {
            stack.Attributes.RemoveAttribute(FirerAttr);
        }
    }

    static string? NormalizeUid(string? uid)
    {
        if (string.IsNullOrWhiteSpace(uid))
        {
            return null;
        }

        string trimmed = uid.Trim();
        return trimmed.Length == 0 ? null : trimmed;
    }
}
