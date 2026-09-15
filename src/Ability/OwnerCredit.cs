using System.Text;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;

namespace Prosequor.Ability;

/// <summary>
/// Shared ownership credit lines for tooltips and block info
/// (<c>Created By</c>, <c>Planted By</c>, …). Callers supply the lang key; style matches
/// the muted italic tooltip footer.
/// </summary>
public static class OwnerCredit
{
    public const string CreatedByLang = "prosequor:created-by";
    public const string PreparedByLang = "prosequor:prepared-by";
    public const string PlantedByLang = "prosequor:planted-by";
    public const string GrownByLang = "prosequor:grown-by";
    public const string FavoriteSeraphLang = "prosequor:favorite-seraph";

    /// <summary>
    /// Stack attr selecting which ownership lang key <see cref="AppendForStack"/> uses
    /// (defaults to <see cref="CreatedByLang"/> when absent).
    /// </summary>
    public const string CreditLangAttr = "prosequorCreditLang";

    /// <summary>Muted subtitle color shared with item-tooltip parentheticals.</summary>
    public const string MutedColor = "#A89B88";

    /// <summary>Online player name, else the raw UID.</summary>
    public static string ResolveDisplayName(IWorldAccessor? world, string ownerUid)
    {
        string displayName = world?.PlayerByUid(ownerUid)?.PlayerName ?? "";
        return string.IsNullOrWhiteSpace(displayName) ? ownerUid : displayName;
    }

    /// <summary>
    /// Plain localized credit (<c>Created By: Name</c>). Null/blank owner or key → false.
    /// </summary>
    public static bool TryFormat(
        IWorldAccessor? world,
        string? ownerUid,
        string? langKey,
        out string line)
    {
        line = "";
        if (string.IsNullOrWhiteSpace(ownerUid) || string.IsNullOrWhiteSpace(langKey))
        {
            return false;
        }

        string displayName = ResolveDisplayName(world, ownerUid.Trim());
        try
        {
            line = Lang.Get(langKey, displayName);
        }
        catch
        {
            // Pure fixtures / early boot may lack a loaded lang table.
            line = langKey + " " + displayName;
        }

        return !string.IsNullOrWhiteSpace(line);
    }

    /// <summary>Muted italic VTML credit line (tooltip / block-info footer style).</summary>
    public static bool TryFormatStyled(
        IWorldAccessor? world,
        string? ownerUid,
        string? langKey,
        out string styled)
    {
        styled = "";
        if (!TryFormat(world, ownerUid, langKey, out string line))
        {
            return false;
        }

        styled = Style(line);
        return true;
    }

    /// <summary>Wrap a plain credit line in the shared muted italic VTML.</summary>
    public static string Style(string plainLine) =>
        "<font color=\"" + MutedColor + "\"><i>" + EscapeVtml(plainLine) + "</i></font>";

    /// <summary>Append styled credit once to a block-info <see cref="StringBuilder"/>.</summary>
    public static void Append(
        StringBuilder? target,
        IWorldAccessor? world,
        string? ownerUid,
        string langKey)
    {
        if (target == null || !TryFormatStyled(world, ownerUid, langKey, out string styled))
        {
            return;
        }

        string existing = target.ToString();
        if (existing.IndexOf(styled, StringComparison.Ordinal) >= 0)
        {
            return;
        }

        target.AppendLine(styled);
    }

    /// <summary>Append styled credit once onto a <see cref="Block.GetPlacedBlockInfo"/> result.</summary>
    public static void Append(
        ref string info,
        IWorldAccessor? world,
        string? ownerUid,
        string langKey)
    {
        if (!TryFormatStyled(world, ownerUid, langKey, out string styled))
        {
            return;
        }

        if (!string.IsNullOrEmpty(info) && info.IndexOf(styled, StringComparison.Ordinal) >= 0)
        {
            return;
        }

        info = string.IsNullOrEmpty(info) ? styled : info + "\n" + styled;
    }

    /// <summary>
    /// Append styled credit to a tooltip/description body. No-op when owner is missing.
    /// </summary>
    public static string AppendToBody(
        string? body,
        IWorldAccessor? world,
        string? ownerUid,
        string langKey)
    {
        if (!TryFormatStyled(world, ownerUid, langKey, out string styled))
        {
            return body ?? "";
        }

        if (string.IsNullOrEmpty(body))
        {
            return styled;
        }

        if (body.IndexOf(styled, StringComparison.Ordinal) >= 0)
        {
            return body;
        }

        return body + "\n" + styled;
    }

    /// <summary>
    /// Homogeneous stack credit lines. Maker uses <see cref="CreditLangAttr"/> when set
    /// (e.g. Grown By), else Created By. Meal cook credit is not appended here — it is
    /// inserted under the serving line. Mixed Frozen bags are skipped.
    /// </summary>
    public static string AppendForStack(
        string? body,
        IWorldAccessor? world,
        ItemStack? stack,
        string? langKey = null)
    {
        if (stack == null || !ProsequorStackPedigree.IsHomogeneous(stack))
        {
            return body ?? "";
        }

        string key = langKey
            ?? TryGetCreditLang(stack)
            ?? CreatedByLang;
        return AppendToBody(body, world, CraftAttribution.TryGetMakerUid(stack), key);
    }

    /// <summary>
    /// Meal-vessel cook credit. The cook is listed even when they are also the maker.
    /// Skips blank and <c>@</c> uids. Not a generic contributor line.
    /// </summary>
    public static string AppendPreparedBy(string? body, IWorldAccessor? world, ProsequorBlob blob)
    {
        if (!TryFormatPreparedBy(world, blob, out string styled))
        {
            return body ?? "";
        }

        if (string.IsNullOrEmpty(body))
        {
            return styled;
        }

        if (body.IndexOf(styled, StringComparison.Ordinal) >= 0)
        {
            return body;
        }

        return body + "\n" + styled;
    }

    /// <summary>
    /// Styled Prepared By line for a meal blob. False when no player cook is listed.
    /// </summary>
    public static bool TryFormatPreparedBy(
        IWorldAccessor? world,
        ProsequorBlob blob,
        out string styled)
    {
        styled = "";
        string? names = FormatPreparedByNames(world, blob);
        if (names == null)
        {
            return false;
        }

        // Names are already resolved. TryFormat would look them up as a uid and fall back
        // to the same string, which is what we want for the lang arg.
        return TryFormatStyled(world, names, PreparedByLang, out styled);
    }

    static string? FormatPreparedByNames(IWorldAccessor? world, ProsequorBlob blob)
    {
        var names = new List<string>();
        IReadOnlyList<ProsequorBlob.Share> shares = blob.Contributors;
        for (int i = 0; i < shares.Count; i++)
        {
            ProsequorBlob.Share share = shares[i];
            if (share.Weight <= 0 || !MealHostCredit.IsPlayerUid(share.PlayerUid))
            {
                continue;
            }

            names.Add(ResolveDisplayName(world, share.PlayerUid));
        }

        return names.Count == 0 ? null : string.Join(", ", names);
    }

    /// <summary>
    /// Stamp planter as maker and mark the stack for <see cref="GrownByLang"/> tooltips.
    /// World-spawned / anonymous planters are a no-op.
    /// </summary>
    public static void StampGrownBy(ItemStack? stack, string? planterUid)
    {
        if (stack?.Attributes == null || string.IsNullOrWhiteSpace(planterUid))
        {
            return;
        }

        CraftAttribution.StampMakerUid(stack, planterUid);
        stack.Attributes.SetString(CreditLangAttr, GrownByLang);
    }

    /// <summary>Stamp Grown By onto each drop from the planter at <paramref name="pos"/>.</summary>
    public static void StampGrownByDrops(
        IWorldAccessor? world,
        Block? block,
        BlockPos? pos,
        ItemStack[]? drops)
    {
        if (drops == null
            || drops.Length == 0
            || !TryResolvePlanter(world, block, pos, out string? planterUid))
        {
            return;
        }

        for (int i = 0; i < drops.Length; i++)
        {
            StampGrownBy(drops[i], planterUid);
        }
    }

    public static string? TryGetCreditLang(ItemStack? stack)
    {
        string? key = stack?.Attributes?.GetString(CreditLangAttr);
        return string.IsNullOrWhiteSpace(key) ? null : key.Trim();
    }

    public static bool TryResolvePlanter(
        IWorldAccessor? world,
        Block? block,
        BlockPos? pos,
        out string? planterUid)
    {
        planterUid = null;
        if (world == null || block == null || pos == null)
        {
            return false;
        }

        if (AbilityBootstrap.IsCropBlock(block))
        {
            return ProsequorBlockPedigreeStation.TryGetCropPlanter(world, pos, out planterUid);
        }

        if (AbilityBootstrap.IsBerryBushBlock(block))
        {
            return ProsequorBlockPedigreeStation.TryGetBushPlanter(world, pos, out planterUid);
        }

        if (AbilityBootstrap.IsFruitTreeBlock(block))
        {
            return ProsequorBlockPedigreeStation.TryGetFruitTreePlanter(world, pos, out planterUid);
        }

        return false;
    }

    static string EscapeVtml(string text) =>
        text.Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal);
}
