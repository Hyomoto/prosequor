namespace Prosequor.Data;

/// <summary>
/// Built-in attribute id constants for UI display order.
/// Runtime membership comes from loaded <c>stats/*.json</c> via <see cref="IAttributeStatRegistry"/>.
/// </summary>
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

    /// <summary>Canonical casing for a UI catalog id, or null when unknown.</summary>
    public static string? Canonicalize(string id) => Canonicalize(id, All);

    /// <summary>Canonical casing for an id in <paramref name="catalog"/>, or null when unknown.</summary>
    public static string? Canonicalize(string id, IReadOnlyList<string> catalog)
    {
        if (string.IsNullOrWhiteSpace(id) || catalog == null)
        {
            return null;
        }

        string trimmed = id.Trim();
        foreach (string known in catalog)
        {
            if (string.Equals(known, trimmed, StringComparison.OrdinalIgnoreCase))
            {
                return known;
            }
        }

        return null;
    }

    /// <summary>Whether <paramref name="id"/> is in <paramref name="catalog"/>.</summary>
    public static bool IsKnown(string id, IReadOnlyList<string> catalog) =>
        Canonicalize(id, catalog) != null;

    /// <summary>Ordered attribute ids from a loaded stat registry.</summary>
    public static IReadOnlyList<string> CatalogIds(IAttributeStatRegistry stats)
    {
        ArgumentNullException.ThrowIfNull(stats);
        if (stats.All.Count == 0)
        {
            return Array.Empty<string>();
        }

        string[] ids = new string[stats.All.Count];
        for (int i = 0; i < stats.All.Count; i++)
        {
            ids[i] = stats.All[i].Id;
        }

        return ids;
    }
}
