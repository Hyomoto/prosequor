namespace Prosequor.Data;

/// <summary>Stable attribute id strings used in progress state and mirrors.</summary>
public static class AttributeIds
{
    public const string Strength = "strength";
    public const string Perception = "perception";
    public const string Constitution = "constitution";
    public const string Inconspicuity = "inconspicuity";
    public const string Resilience = "resilience";

    /// <summary>Display / iteration order.</summary>
    public static readonly string[] All =
    [
        Strength,
        Perception,
        Constitution,
        Inconspicuity,
        Resilience
    ];

    public static bool IsKnown(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return false;
        }

        foreach (string known in All)
        {
            if (string.Equals(known, id, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Canonical casing for a known id, or null when unknown.</summary>
    public static string? Canonicalize(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return null;
        }

        foreach (string known in All)
        {
            if (string.Equals(known, id, StringComparison.OrdinalIgnoreCase))
            {
                return known;
            }
        }

        return null;
    }
}
