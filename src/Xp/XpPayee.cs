namespace Prosequor.Xp;

/// <summary>
/// Who receives a deed amount grant. Omitted JSON <c>payee</c> is <see cref="User"/>.
/// Missing role data never falls back to another role — no pay.
/// </summary>
public enum XpPayee
{
    /// <summary>Emit actor (<c>playerUid</c>).</summary>
    User = 0,

    /// <summary>Emit maker uid. Missing → no pay.</summary>
    Maker = 1,

    /// <summary>Emit selected contributor uid (callsite-chosen). Missing → no pay.</summary>
    Contributor = 2,

    /// <summary>
    /// Every weighted contributor; each gets <c>grant × weight / sum</c>. Empty → no pay.
    /// </summary>
    Contributors = 3
}

/// <summary>Parse / format helpers for <see cref="XpPayee"/> JSON.</summary>
public static class XpPayees
{
    public const string User = "user";
    public const string Maker = "maker";
    public const string Contributor = "contributor";
    public const string Contributors = "contributors";

    public static bool TryParse(string? raw, out XpPayee payee, out string? error)
    {
        payee = XpPayee.User;
        error = null;
        if (string.IsNullOrWhiteSpace(raw))
        {
            error = "payee must not be empty";
            return false;
        }

        string key = raw.Trim();
        if (key.Equals(User, StringComparison.OrdinalIgnoreCase))
        {
            payee = XpPayee.User;
            return true;
        }

        if (key.Equals(Maker, StringComparison.OrdinalIgnoreCase))
        {
            payee = XpPayee.Maker;
            return true;
        }

        if (key.Equals(Contributor, StringComparison.OrdinalIgnoreCase))
        {
            payee = XpPayee.Contributor;
            return true;
        }

        if (key.Equals(Contributors, StringComparison.OrdinalIgnoreCase))
        {
            payee = XpPayee.Contributors;
            return true;
        }

        error = $"unknown payee '{key}'";
        return false;
    }

    public static string Canonical(XpPayee payee) => payee switch
    {
        XpPayee.Maker => Maker,
        XpPayee.Contributor => Contributor,
        XpPayee.Contributors => Contributors,
        _ => User
    };

    public static bool NeedsActor(XpPayee payee) => payee == XpPayee.User;
}
