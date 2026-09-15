using Prosequor.Ability;
using Prosequor.Ability.Hooks;

namespace Prosequor.Xp;

/// <summary>
/// Compiled <c>exclude</c> entries for quantity filtering: exact codes and/or collection ids.
/// A code is excluded if it equals any exact entry or is in any listed collection.
/// </summary>
public sealed class XpQuantityExclude
{
    public static readonly XpQuantityExclude Empty = new(Array.Empty<string>(), Array.Empty<string>());

    readonly HashSet<string> exactCodes;
    readonly IReadOnlyList<string> collectionIds;

    public XpQuantityExclude(IReadOnlyList<string> exactCodes, IReadOnlyList<string> collectionIds)
    {
        this.exactCodes = new HashSet<string>(
            exactCodes ?? Array.Empty<string>(),
            StringComparer.OrdinalIgnoreCase);
        this.collectionIds = collectionIds ?? Array.Empty<string>();
    }

    public bool IsEmpty => exactCodes.Count == 0 && collectionIds.Count == 0;

    public IReadOnlyCollection<string> ExactCodes => exactCodes;

    public IReadOnlyList<string> CollectionIds => collectionIds;

    public bool Matches(string? code, CollectionIndex collections)
    {
        if (IsEmpty || string.IsNullOrWhiteSpace(code))
        {
            return false;
        }

        string trimmed = code.Trim();
        if (exactCodes.Contains(trimmed))
        {
            return true;
        }

        collections ??= new CollectionIndex();
        for (int i = 0; i < collectionIds.Count; i++)
        {
            if (collections.Contains(collectionIds[i], trimmed))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Parse one exclude entry: <c>&lt;collection&gt;</c> / <c>&lt;a, b&gt;</c> or an exact asset code.
    /// </summary>
    public static bool TryParseEntry(
        string? raw,
        CollectionIndex collections,
        List<string> exactOut,
        List<string> collectionOut,
        out string error)
    {
        error = "";
        if (string.IsNullOrWhiteSpace(raw))
        {
            error = "exclude entry must not be empty";
            return false;
        }

        string trimmed = raw.Trim();
        if (TagCriterionParser.TryParseCollectionRef(trimmed, out List<string>? ids, out string? refError))
        {
            if (ids == null)
            {
                error = refError ?? "invalid collection ref in exclude";
                return false;
            }

            foreach (string collectionId in ids)
            {
                if (!collections.Exists(collectionId))
                {
                    error = $"unknown collection '<{collectionId}>' in exclude";
                    return false;
                }

                collectionOut.Add(collectionId);
            }

            return true;
        }

        if (trimmed.Contains('*'))
        {
            error = $"wildcard '{trimmed}' is not allowed in exclude; use a collection";
            return false;
        }

        exactOut.Add(trimmed);
        return true;
    }

    public string Canonical()
    {
        if (IsEmpty)
        {
            return "-";
        }

        List<string> parts = new(exactCodes.Count + collectionIds.Count);
        foreach (string code in exactCodes.OrderBy(c => c, StringComparer.OrdinalIgnoreCase))
        {
            parts.Add(code);
        }

        foreach (string id in collectionIds.OrderBy(c => c, StringComparer.OrdinalIgnoreCase))
        {
            parts.Add("<" + id + ">");
        }

        return string.Join(',', parts);
    }
}
