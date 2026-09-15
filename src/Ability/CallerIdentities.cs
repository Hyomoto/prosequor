namespace Prosequor.Ability;

/// <summary>
/// Reserved <c>caller:</c> identities. Prefixed with <c>@</c> so they can never collide with asset codes.
/// </summary>
public static class CallerIdentities
{
    public const string Hand = "@hand";
    public const string Grid = "@grid";

    /// <summary>Animal ate from a trough.</summary>
    public const string Trough = "@trough";

    /// <summary>Animal ate a farmland crop.</summary>
    public const string Crop = "@crop";

    /// <summary>Animal ate a dropped ground stack.</summary>
    public const string Loose = "@loose";

    /// <summary>Liquid metal mold (tool / ingot) casting settle.</summary>
    public const string Mold = "@mold";

    public static bool IsReserved(string? identity)
    {
        if (string.IsNullOrWhiteSpace(identity))
        {
            return false;
        }

        string t = identity.Trim();
        return t.Equals(Hand, StringComparison.OrdinalIgnoreCase)
            || t.Equals(Grid, StringComparison.OrdinalIgnoreCase)
            || t.Equals(Trough, StringComparison.OrdinalIgnoreCase)
            || t.Equals(Crop, StringComparison.OrdinalIgnoreCase)
            || t.Equals(Loose, StringComparison.OrdinalIgnoreCase)
            || t.Equals(Mold, StringComparison.OrdinalIgnoreCase);
    }

    public static bool TryNormalizeReserved(string? raw, out string identity)
    {
        identity = string.Empty;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        string t = raw.Trim();
        if (t.Equals(Hand, StringComparison.OrdinalIgnoreCase))
        {
            identity = Hand;
            return true;
        }

        if (t.Equals(Grid, StringComparison.OrdinalIgnoreCase))
        {
            identity = Grid;
            return true;
        }

        if (t.Equals(Trough, StringComparison.OrdinalIgnoreCase))
        {
            identity = Trough;
            return true;
        }

        if (t.Equals(Crop, StringComparison.OrdinalIgnoreCase))
        {
            identity = Crop;
            return true;
        }

        if (t.Equals(Loose, StringComparison.OrdinalIgnoreCase))
        {
            identity = Loose;
            return true;
        }

        if (t.Equals(Mold, StringComparison.OrdinalIgnoreCase))
        {
            identity = Mold;
            return true;
        }

        return false;
    }

    /// <summary>Deed emit: blank caller becomes <see cref="Hand"/>.</summary>
    public static string ForDeed(string? caller) =>
        string.IsNullOrWhiteSpace(caller) ? Hand : caller.Trim();
}
