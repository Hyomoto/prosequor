using Newtonsoft.Json.Linq;

namespace Prosequor.Data;

/// <summary>
/// Rewrites authored <c>requires</c> and <c>excludes</c> when nodes are disabled or replaced.
/// </summary>
public static class SkillTreeRequiresRewriter
{
    public static void StripIds(SkillTreeNodeJson node, IReadOnlyCollection<string> removedIds)
    {
        if (removedIds.Count == 0)
        {
            return;
        }

        HashSet<string> removed = new(removedIds, StringComparer.OrdinalIgnoreCase);
        node.requires = RewriteRequires(node.requires, removed, null);
        node.excludes = RewriteExcludes(node.excludes, removed, null);
    }

    public static void ApplyReplacementMap(
        SkillTreeNodeJson node,
        IReadOnlyDictionary<string, string> replacements)
    {
        if (replacements.Count == 0)
        {
            return;
        }

        node.requires = RewriteRequires(node.requires, null, replacements);
        node.excludes = RewriteExcludes(node.excludes, null, replacements);
    }

    public static JToken[]? RewriteRequires(
        JToken[]? rawRequires,
        IReadOnlySet<string>? stripIds,
        IReadOnlyDictionary<string, string>? replacements)
    {
        if (rawRequires == null || rawRequires.Length == 0)
        {
            return rawRequires;
        }

        List<JToken> rewritten = new();
        foreach (JToken token in rawRequires)
        {
            if (token == null || token.Type == JTokenType.Null)
            {
                continue;
            }

            if (token.Type == JTokenType.String)
            {
                string? mapped = MapId(token.Value<string>()?.Trim() ?? "", stripIds, replacements);
                if (mapped != null)
                {
                    rewritten.Add(mapped);
                }

                continue;
            }

            if (token is JArray array)
            {
                List<string> alternatives = new();
                foreach (JToken alt in array)
                {
                    if (alt?.Type != JTokenType.String)
                    {
                        continue;
                    }

                    string? mapped = MapId(alt.Value<string>()?.Trim() ?? "", stripIds, replacements);
                    if (mapped != null && !alternatives.Contains(mapped, StringComparer.OrdinalIgnoreCase))
                    {
                        alternatives.Add(mapped);
                    }
                }

                if (alternatives.Count > 0)
                {
                    rewritten.Add(new JArray(alternatives.Cast<object>().ToArray()));
                }

                continue;
            }
        }

        return rewritten.Count == 0 ? Array.Empty<JToken>() : rewritten.ToArray();
    }

    public static string[]? RewriteExcludes(
        string[]? excludes,
        IReadOnlySet<string>? stripIds,
        IReadOnlyDictionary<string, string>? replacements)
    {
        if (excludes == null || excludes.Length == 0)
        {
            return excludes;
        }

        List<string> rewritten = new();
        foreach (string raw in excludes)
        {
            string? mapped = MapId(raw?.Trim() ?? "", stripIds, replacements);
            if (mapped != null && !rewritten.Contains(mapped, StringComparer.OrdinalIgnoreCase))
            {
                rewritten.Add(mapped);
            }
        }

        return rewritten.Count == 0 ? null : rewritten.ToArray();
    }

    static string? MapId(
        string id,
        IReadOnlySet<string>? stripIds,
        IReadOnlyDictionary<string, string>? replacements)
    {
        if (id.Length == 0)
        {
            return null;
        }

        if (replacements != null
            && replacements.TryGetValue(id, out string? replacement)
            && replacement.Length > 0)
        {
            id = replacement;
        }

        if (stripIds != null && stripIds.Contains(id))
        {
            return null;
        }

        return id;
    }
}
