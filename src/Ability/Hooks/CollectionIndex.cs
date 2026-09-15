using Vintagestory.API.Common;
using Vintagestory.API.Util;

namespace Prosequor.Ability.Hooks;

/// <summary>
/// Load-time membership index keyed by collection id. Codes are asset/entity strings.
/// Pools also carry weights for <see cref="PickFromPool"/>.
/// </summary>
public sealed class CollectionIndex
{
    readonly Dictionary<string, HashSet<string>> codesById = new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<string, List<WeightedCode>> weightedById =
        new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<string, List<string>> includePatternsById =
        new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<string, List<string>> unionMembersById =
        new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<string, List<string>> excludeIdsById =
        new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyCollection<string> Ids => codesById.Keys;

    public void ClearMembership()
    {
        foreach (KeyValuePair<string, HashSet<string>> kv in codesById)
        {
            kv.Value.Clear();
        }

        weightedById.Clear();
    }

    public void ClearAll()
    {
        codesById.Clear();
        weightedById.Clear();
        includePatternsById.Clear();
        unionMembersById.Clear();
        excludeIdsById.Clear();
    }

    /// <summary>Registers a collection key (membership may be empty until GameReady).</summary>
    public void EnsureKey(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return;
        }

        string key = id.Trim();
        if (!codesById.ContainsKey(key))
        {
            codesById[key] = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    public bool Exists(string id) =>
        !string.IsNullOrWhiteSpace(id) && codesById.ContainsKey(id.Trim());

    public void AddIncludePattern(string id, string pattern)
    {
        EnsureKey(id);
        if (string.IsNullOrWhiteSpace(pattern))
        {
            return;
        }

        string key = id.Trim();
        if (!includePatternsById.TryGetValue(key, out List<string>? list))
        {
            list = new List<string>();
            includePatternsById[key] = list;
        }

        string p = pattern.Trim();
        if (!list.Contains(p, StringComparer.OrdinalIgnoreCase))
        {
            list.Add(p);
        }
    }

    public IReadOnlyList<string> IncludePatterns(string id) =>
        includePatternsById.TryGetValue(id, out List<string>? list)
            ? list
            : Array.Empty<string>();

    /// <summary>
    /// Registers that <paramref name="id"/> should include every code from each member collection.
    /// Materialized in <see cref="MaterializeUnions"/>.
    /// </summary>
    public void AddUnion(string id, IEnumerable<string> memberIds)
    {
        EnsureKey(id);
        string key = id.Trim();
        if (!unionMembersById.TryGetValue(key, out List<string>? list))
        {
            list = new List<string>();
            unionMembersById[key] = list;
        }

        foreach (string? member in memberIds)
        {
            if (string.IsNullOrWhiteSpace(member))
            {
                continue;
            }

            string m = member.Trim();
            EnsureKey(m);
            if (!list.Contains(m, StringComparer.OrdinalIgnoreCase))
            {
                list.Add(m);
            }
        }
    }

    public IReadOnlyList<string> UnionMembers(string id) =>
        unionMembersById.TryGetValue(id, out List<string>? list)
            ? list
            : Array.Empty<string>();

    /// <summary>
    /// Registers that <paramref name="id"/> should drop every code in each excluded collection's
    /// positive membership (includes and fills). Applied in <see cref="ApplyExcludes"/> against a
    /// snapshot, before unions copy.
    /// </summary>
    public void AddExclude(string id, IEnumerable<string> excludeIds)
    {
        EnsureKey(id);
        string key = id.Trim();
        if (!excludeIdsById.TryGetValue(key, out List<string>? list))
        {
            list = new List<string>();
            excludeIdsById[key] = list;
        }

        foreach (string? excludeId in excludeIds)
        {
            if (string.IsNullOrWhiteSpace(excludeId))
            {
                continue;
            }

            string excluded = excludeId.Trim();
            if (!list.Contains(excluded, StringComparer.OrdinalIgnoreCase))
            {
                list.Add(excluded);
            }
        }
    }

    public IReadOnlyList<string> ExcludeIds(string id) =>
        excludeIdsById.TryGetValue(id, out List<string>? list)
            ? list
            : Array.Empty<string>();

    /// <summary>
    /// Subtracts excluded collections' positive membership (includes and C# fills already written
    /// into <see cref="Codes"/>). Reads a frozen snapshot, never the live or post-exclude set, so
    /// mutual excludes are order-independent. Union copies are not subtracted.
    /// </summary>
    public void ApplyExcludes(Action<string>? warn = null)
    {
        if (excludeIdsById.Count == 0)
        {
            return;
        }

        Dictionary<string, HashSet<string>> snapshot = new(StringComparer.OrdinalIgnoreCase);
        foreach (KeyValuePair<string, HashSet<string>> kv in codesById)
        {
            snapshot[kv.Key] = new HashSet<string>(kv.Value, StringComparer.OrdinalIgnoreCase);
        }

        foreach (KeyValuePair<string, List<string>> kv in excludeIdsById)
        {
            if (!codesById.TryGetValue(kv.Key, out HashSet<string>? dest))
            {
                continue;
            }

            foreach (string excludeId in kv.Value)
            {
                if (!snapshot.TryGetValue(excludeId, out HashSet<string>? source))
                {
                    warn?.Invoke(
                        $"[prosequor] Collection '{kv.Key}': unknown exclude '{excludeId}'.");
                    continue;
                }

                bool hasUnions = unionMembersById.TryGetValue(excludeId, out List<string>? unions)
                    && unions.Count > 0;
                if (hasUnions && source.Count == 0)
                {
                    warn?.Invoke(
                        $"[prosequor] Collection '{kv.Key}': exclude '{excludeId}' has no positive membership; union copies are not subtracted.");
                    continue;
                }

                foreach (string code in source)
                {
                    dest.Remove(code);
                    RemoveWeighted(kv.Key, code);
                }
            }
        }
    }

    public void AddCode(string id, string? code)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return;
        }

        EnsureKey(id);
        codesById[id.Trim()].Add(code.Trim());
    }

    public void AddWeighted(string id, string? code, float weight = 1f)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return;
        }

        string key = id.Trim();
        string c = code.Trim();
        AddCode(key, c);
        float w = weight > 0f ? weight : 1f;
        if (!weightedById.TryGetValue(key, out List<WeightedCode>? list))
        {
            list = new List<WeightedCode>();
            weightedById[key] = list;
        }

        for (int i = 0; i < list.Count; i++)
        {
            if (string.Equals(list[i].Code, c, StringComparison.OrdinalIgnoreCase))
            {
                list[i] = new WeightedCode(c, w);
                return;
            }
        }

        list.Add(new WeightedCode(c, w));
    }

    void RemoveWeighted(string id, string code)
    {
        if (!weightedById.TryGetValue(id, out List<WeightedCode>? list))
        {
            return;
        }

        for (int i = list.Count - 1; i >= 0; i--)
        {
            if (string.Equals(list[i].Code, code, StringComparison.OrdinalIgnoreCase))
            {
                list.RemoveAt(i);
            }
        }
    }

    public bool Contains(string id, string? code) =>
        !string.IsNullOrWhiteSpace(code)
        && codesById.TryGetValue(id, out HashSet<string>? set)
        && set.Contains(code);

    public bool ContainsAny(IReadOnlyList<string> ids, string? code)
    {
        if (string.IsNullOrWhiteSpace(code) || ids.Count == 0)
        {
            return false;
        }

        for (int i = 0; i < ids.Count; i++)
        {
            if (Contains(ids[i], code))
            {
                return true;
            }
        }

        return false;
    }

    public int CodeCount(string id) =>
        codesById.TryGetValue(id, out HashSet<string>? set) ? set.Count : 0;

    /// <summary>Current membership codes for <paramref name="id"/> (empty when unknown or unbound).</summary>
    public IReadOnlyCollection<string> Codes(string id) =>
        !string.IsNullOrWhiteSpace(id) && codesById.TryGetValue(id.Trim(), out HashSet<string>? set)
            ? set
            : Array.Empty<string>();

    /// <summary>
    /// Copies member-collection codes into union keys. Multi-pass until no growth so
    /// union-of-unions works when members are themselves unions.
    /// </summary>
    public void MaterializeUnions(Action<string>? warn = null)
    {
        if (unionMembersById.Count == 0)
        {
            return;
        }

        bool grew;
        do
        {
            grew = false;
            foreach (KeyValuePair<string, List<string>> kv in unionMembersById)
            {
                string unionId = kv.Key;
                EnsureKey(unionId);
                HashSet<string> dest = codesById[unionId];
                foreach (string memberId in kv.Value)
                {
                    if (!codesById.TryGetValue(memberId, out HashSet<string>? source))
                    {
                        warn?.Invoke(
                            $"[prosequor] Collection '{unionId}': unknown union member '{memberId}'.");
                        continue;
                    }

                    foreach (string code in source)
                    {
                        if (dest.Add(code))
                        {
                            grew = true;
                        }
                    }
                }
            }
        }
        while (grew);
    }

    public bool StackMatches(string id, ItemStack? stack)
    {
        string? code = stack?.Collectible?.Code?.ToString();
        return Contains(id, code);
    }

    /// <summary>
    /// Weighted pick from a pool collection. Optional <paramref name="excludeItemIds"/> skips those items.
    /// </summary>
    public Item? PickFromPool(
        IWorldAccessor world,
        string id,
        Random rand,
        IReadOnlySet<int>? excludeItemIds = null)
    {
        if (!weightedById.TryGetValue(id, out List<WeightedCode>? pool) || pool.Count == 0)
        {
            return null;
        }

        List<(Item Item, float Weight)> candidates = new();
        foreach (WeightedCode entry in pool)
        {
            Item? item = world.GetItem(new AssetLocation(entry.Code));
            if (item == null || item.Id == 0)
            {
                continue;
            }

            if (excludeItemIds != null && excludeItemIds.Contains(item.Id))
            {
                continue;
            }

            candidates.Add((item, entry.Weight));
        }

        if (candidates.Count == 0)
        {
            return null;
        }

        double total = 0;
        for (int i = 0; i < candidates.Count; i++)
        {
            total += candidates[i].Weight;
        }

        double roll = rand.NextDouble() * total;
        double cursor = 0;
        for (int i = 0; i < candidates.Count; i++)
        {
            cursor += candidates[i].Weight;
            if (roll < cursor)
            {
                return candidates[i].Item;
            }
        }

        return candidates[^1].Item;
    }

    /// <summary>Expands registered include patterns against all world blocks and items.</summary>
    public void ExpandPatterns(ICoreAPI api)
    {
        foreach (KeyValuePair<string, List<string>> kv in includePatternsById)
        {
            string id = kv.Key;
            foreach (string pattern in kv.Value)
            {
                foreach (Block block in api.World.Blocks)
                {
                    string? code = block?.Code?.ToString();
                    if (code != null && MatchesPattern(pattern, code))
                    {
                        AddCode(id, code);
                    }
                }

                foreach (Item item in api.World.Items)
                {
                    string? code = item?.Code?.ToString();
                    if (code != null && MatchesPattern(pattern, code))
                    {
                        AddCode(id, code);
                    }
                }
            }
        }
    }

    public static bool MatchesPattern(string pattern, string code)
    {
        if (string.IsNullOrWhiteSpace(pattern) || string.IsNullOrWhiteSpace(code))
        {
            return false;
        }

        if (pattern.IndexOf('*') < 0)
        {
            return string.Equals(pattern, code, StringComparison.OrdinalIgnoreCase);
        }

        return WildcardUtil.Match(pattern, code);
    }

    readonly record struct WeightedCode(string Code, float Weight);
}
