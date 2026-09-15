using System.Text;
using Vintagestory.API.Datastructures;

namespace Prosequor.Ability;

/// <summary>
/// Immutable pedigree payload for one unit: maker plus weighted contributor shares
/// (<c>uid → weight</c>). Craft affixes, attribute mods, and quality rank are identity —
/// they join <see cref="ContentHash"/> so mixed quality cannot coalesce. Optional recipe,
/// friendliness-ready timestamp, and anvil split count are payload only (first wins).
/// </summary>
public sealed class ProsequorBlob : IEquatable<ProsequorBlob>
{
    public const string MakerKey = "maker";
    public const string ContributorsKey = "contributors";
    public const string ContributorCountKey = "n";
    public const string UidKey = "uid";
    public const string WeightKey = "w";
    public const string RecipeKey = "recipe";
    public const string FriendlinessReadyAtKey = "friendlinessReadyAt";
    public const string AnvilSplitsKey = "anvilSplits";
    public const string QualityRankKey = "qualityRank";
    public const string AffixesKey = "affixes";
    public const string ModsKey = "mods";

    public readonly record struct Share(string PlayerUid, int Weight);

    public readonly record struct ModFactor(string Key, float Factor);

    public string? MakerUid { get; }

    /// <summary>Weighted contributor shares (uid → weight). Order is not significant.</summary>
    public IReadOnlyList<Share> Contributors { get; }

    /// <summary>Ordered craft affix bag. Part of <see cref="ContentHash"/>.</summary>
    public IReadOnlyList<ItemAffixEntry> Affixes { get; }

    /// <summary>Craft attribute factors (key → factor). Part of <see cref="ContentHash"/>.</summary>
    public IReadOnlyList<ModFactor> Mods { get; }

    /// <summary>Craft receipt (e.g. clayforming recipe id). Not part of <see cref="ContentHash"/>.</summary>
    public string? Recipe { get; }

    /// <summary>
    /// World calendar <c>TotalHours</c> when the next friendliness gain may apply.
    /// Missing / ≤0 means always eligible. Not part of <see cref="ContentHash"/>.
    /// </summary>
    public double FriendlinessReadyAtTotalHours { get; }

    /// <summary>
    /// Metal anvil splits counted toward Metal Recovery. Not part of <see cref="ContentHash"/>.
    /// </summary>
    public int AnvilSplits { get; }

    /// <summary>
    /// Authored craft quality rank (table value from <c>prosequor:quality-rank</c>).
    /// Part of <see cref="ContentHash"/>. 0 = unset.
    /// </summary>
    public int QualityRank { get; }

    public string ContentHash { get; }

    public static ProsequorBlob Empty { get; } = new(null, Array.Empty<Share>(), null);

    public ProsequorBlob(string? makerUid, IReadOnlyList<Share>? contributors)
        : this(makerUid, contributors, recipe: null)
    {
    }

    /// <summary>Legacy ordered unique UIDs migrate to weight 1 each.</summary>
    public ProsequorBlob(string? makerUid, IReadOnlyList<string>? contributorUids, string? recipe)
        : this(makerUid, FromUidList(contributorUids), recipe)
    {
    }

    public ProsequorBlob(string? makerUid, IReadOnlyList<Share>? contributors, string? recipe)
        : this(makerUid, contributors, recipe, friendlinessReadyAtTotalHours: 0, anvilSplits: 0)
    {
    }

    public ProsequorBlob(
        string? makerUid,
        IReadOnlyList<Share>? contributors,
        string? recipe,
        double friendlinessReadyAtTotalHours)
        : this(makerUid, contributors, recipe, friendlinessReadyAtTotalHours, anvilSplits: 0)
    {
    }

    public ProsequorBlob(
        string? makerUid,
        IReadOnlyList<Share>? contributors,
        string? recipe,
        double friendlinessReadyAtTotalHours,
        int anvilSplits)
        : this(
            makerUid,
            contributors,
            recipe,
            friendlinessReadyAtTotalHours,
            anvilSplits,
            affixes: null,
            mods: null)
    {
    }

    public ProsequorBlob(
        string? makerUid,
        IReadOnlyList<Share>? contributors,
        string? recipe,
        double friendlinessReadyAtTotalHours,
        int anvilSplits,
        IReadOnlyList<ItemAffixEntry>? affixes,
        IReadOnlyList<ModFactor>? mods)
        : this(
            makerUid,
            contributors,
            recipe,
            friendlinessReadyAtTotalHours,
            anvilSplits,
            affixes,
            mods,
            qualityRank: 0)
    {
    }

    public ProsequorBlob(
        string? makerUid,
        IReadOnlyList<Share>? contributors,
        string? recipe,
        double friendlinessReadyAtTotalHours,
        int anvilSplits,
        IReadOnlyList<ItemAffixEntry>? affixes,
        IReadOnlyList<ModFactor>? mods,
        int qualityRank)
    {
        MakerUid = NormalizeUid(makerUid);
        Contributors = NormalizeShares(contributors);
        Recipe = NormalizeRecipe(recipe);
        FriendlinessReadyAtTotalHours = friendlinessReadyAtTotalHours > 0
            ? friendlinessReadyAtTotalHours
            : 0;
        AnvilSplits = anvilSplits > 0 ? anvilSplits : 0;
        Affixes = NormalizeAffixes(affixes);
        Mods = NormalizeMods(mods);
        QualityRank = NormalizeQualityRank(qualityRank);
        ContentHash = ComputeHash(MakerUid, Contributors, Affixes, Mods, QualityRank);
    }

    public bool IsAnonymous =>
        MakerUid == null && Contributors.Count == 0;

    /// <summary>Affixes, mods, or quality rank that must survive host changes even when attribution is empty.</summary>
    public bool HasSurface => Affixes.Count > 0 || Mods.Count > 0 || QualityRank > 0;

    /// <summary>Attribution and/or craft surface — write Live/Frozen and restore on peel.</summary>
    public bool HasPersistable => !IsAnonymous || HasSurface;

    public ProsequorBlob WithMaker(string? makerUid) =>
        new(makerUid, Contributors, Recipe, FriendlinessReadyAtTotalHours, AnvilSplits, Affixes, Mods, QualityRank);

    public ProsequorBlob WithRecipe(string? recipe) =>
        new(MakerUid, Contributors, recipe, FriendlinessReadyAtTotalHours, AnvilSplits, Affixes, Mods, QualityRank);

    public ProsequorBlob WithFriendlinessReadyAt(double totalHours) =>
        new(MakerUid, Contributors, Recipe, totalHours, AnvilSplits, Affixes, Mods, QualityRank);

    public ProsequorBlob WithAnvilSplits(int splits) =>
        new(MakerUid, Contributors, Recipe, FriendlinessReadyAtTotalHours, splits, Affixes, Mods, QualityRank);

    public ProsequorBlob WithQualityRank(int rank) =>
        new(MakerUid, Contributors, Recipe, FriendlinessReadyAtTotalHours, AnvilSplits, Affixes, Mods, rank);

    public ProsequorBlob WithAffixes(IReadOnlyList<ItemAffixEntry>? affixes) =>
        new(MakerUid, Contributors, Recipe, FriendlinessReadyAtTotalHours, AnvilSplits, affixes, Mods, QualityRank);

    public ProsequorBlob WithMods(IReadOnlyList<ModFactor>? mods) =>
        new(MakerUid, Contributors, Recipe, FriendlinessReadyAtTotalHours, AnvilSplits, Affixes, mods, QualityRank);

    /// <summary>Increments <paramref name="uid"/>'s weight by <paramref name="amount"/> (default 1).</summary>
    public ProsequorBlob WithContributor(string? uid, int amount = 1)
    {
        string? normalized = NormalizeUid(uid);
        if (normalized == null || amount <= 0)
        {
            return this;
        }

        List<Share> next = new(Contributors.Count + 1);
        bool found = false;
        for (int i = 0; i < Contributors.Count; i++)
        {
            Share share = Contributors[i];
            if (string.Equals(share.PlayerUid, normalized, StringComparison.Ordinal))
            {
                next.Add(new Share(share.PlayerUid, share.Weight + amount));
                found = true;
            }
            else
            {
                next.Add(share);
            }
        }

        if (!found)
        {
            next.Add(new Share(normalized, amount));
        }

        return new(
            MakerUid,
            next,
            Recipe,
            FriendlinessReadyAtTotalHours,
            AnvilSplits,
            Affixes,
            Mods,
            QualityRank);
    }

    /// <summary>Same maker / recipe / ready-at / splits / surface with an empty contributor bag.</summary>
    public ProsequorBlob WithClearedContributors() =>
        new(MakerUid, Array.Empty<Share>(), Recipe, FriendlinessReadyAtTotalHours, AnvilSplits, Affixes, Mods, QualityRank);

    /// <summary>
    /// Replaces the contributor bag with a single share (weight 1). Process starters
    /// (firepit cook/smelt, barrel seal) use this — never <see cref="WithContributor"/>.
    /// </summary>
    public ProsequorBlob WithSoleContributor(string? uid)
    {
        string? normalized = NormalizeUid(uid);
        if (normalized == null)
        {
            return WithClearedContributors();
        }

        return new(
            MakerUid,
            [new Share(normalized, 1)],
            Recipe,
            FriendlinessReadyAtTotalHours,
            AnvilSplits,
            Affixes,
            Mods,
            QualityRank);
    }

    /// <summary>True only when the bag has exactly one contributor share.</summary>
    public bool TryGetSoleContributor(out string? uid)
    {
        uid = null;
        if (Contributors.Count != 1)
        {
            return false;
        }

        Share share = Contributors[0];
        if (string.IsNullOrWhiteSpace(share.PlayerUid) || share.Weight <= 0)
        {
            return false;
        }

        uid = share.PlayerUid;
        return true;
    }

    /// <summary>Deed emit shares from this blob.</summary>
    public IReadOnlyList<Xp.Activity.Deed.ContributorShare> ToDeedShares()
    {
        if (Contributors.Count == 0)
        {
            return Array.Empty<Xp.Activity.Deed.ContributorShare>();
        }

        List<Xp.Activity.Deed.ContributorShare> list = new(Contributors.Count);
        for (int i = 0; i < Contributors.Count; i++)
        {
            Share share = Contributors[i];
            list.Add(new Xp.Activity.Deed.ContributorShare(share.PlayerUid, share.Weight));
        }

        return list;
    }

    public bool TryGetContributorWeight(string? uid, out int weight)
    {
        weight = 0;
        string? normalized = NormalizeUid(uid);
        if (normalized == null)
        {
            return false;
        }

        for (int i = 0; i < Contributors.Count; i++)
        {
            Share share = Contributors[i];
            if (string.Equals(share.PlayerUid, normalized, StringComparison.Ordinal))
            {
                weight = share.Weight;
                return weight > 0;
            }
        }

        return false;
    }

    public void WriteTo(ITreeAttribute tree)
    {
        if (tree == null)
        {
            return;
        }

        if (MakerUid != null)
        {
            tree.SetString(MakerKey, MakerUid);
        }
        else if (tree.HasAttribute(MakerKey))
        {
            tree.RemoveAttribute(MakerKey);
        }

        if (Recipe != null)
        {
            tree.SetString(RecipeKey, Recipe);
        }
        else if (tree.HasAttribute(RecipeKey))
        {
            tree.RemoveAttribute(RecipeKey);
        }

        if (FriendlinessReadyAtTotalHours > 0)
        {
            tree.SetDouble(FriendlinessReadyAtKey, FriendlinessReadyAtTotalHours);
        }
        else if (tree.HasAttribute(FriendlinessReadyAtKey))
        {
            tree.RemoveAttribute(FriendlinessReadyAtKey);
        }

        if (AnvilSplits > 0)
        {
            tree.SetInt(AnvilSplitsKey, AnvilSplits);
        }
        else if (tree.HasAttribute(AnvilSplitsKey))
        {
            tree.RemoveAttribute(AnvilSplitsKey);
        }

        if (QualityRank > 0)
        {
            tree.SetInt(QualityRankKey, QualityRank);
        }
        else if (tree.HasAttribute(QualityRankKey))
        {
            tree.RemoveAttribute(QualityRankKey);
        }

        ITreeAttribute contrib = tree.GetOrAddTreeAttribute(ContributorsKey);
        int oldN = contrib.GetInt(ContributorCountKey, 0);
        for (int i = 0; i < Math.Max(oldN, Contributors.Count) + 4; i++)
        {
            contrib.RemoveAttribute(i.ToString());
        }

        for (int i = 0; i < Contributors.Count; i++)
        {
            Share share = Contributors[i];
            ITreeAttribute entry = contrib.GetOrAddTreeAttribute(i.ToString());
            entry.SetString(UidKey, share.PlayerUid);
            entry.SetInt(WeightKey, share.Weight);
        }

        contrib.SetInt(ContributorCountKey, Contributors.Count);

        WriteAffixes(tree);
        WriteMods(tree);
    }

    public static ProsequorBlob ReadFrom(ITreeAttribute? tree)
    {
        if (tree == null)
        {
            return Empty;
        }

        string? maker = NormalizeUid(tree.GetString(MakerKey));
        string? recipe = NormalizeRecipe(tree.GetString(RecipeKey));
        double readyAt = tree.GetDouble(FriendlinessReadyAtKey, 0);
        int anvilSplits = tree.GetInt(AnvilSplitsKey, 0);
        int qualityRank = tree.GetInt(QualityRankKey, 0);
        IReadOnlyList<ItemAffixEntry> affixes = ItemAffixes.ReadAll(tree.GetTreeAttribute(AffixesKey));
        IReadOnlyList<ModFactor> mods = ReadMods(tree.GetTreeAttribute(ModsKey));
        ITreeAttribute? contrib = tree.GetTreeAttribute(ContributorsKey);
        if (contrib == null)
        {
            return new ProsequorBlob(
                maker,
                Array.Empty<Share>(),
                recipe,
                readyAt,
                anvilSplits,
                affixes,
                mods,
                qualityRank);
        }

        int n = contrib.GetInt(ContributorCountKey, 0);
        List<Share> list = new(Math.Max(0, n));
        for (int i = 0; i < n; i++)
        {
            string key = i.ToString();
            ITreeAttribute? entry = contrib.GetTreeAttribute(key);
            if (entry != null)
            {
                string? uid = NormalizeUid(entry.GetString(UidKey));
                int weight = entry.GetInt(WeightKey, 0);
                if (uid != null && weight > 0)
                {
                    list.Add(new Share(uid, weight));
                }

                continue;
            }

            // Legacy ordered unique-UID list → weight 1.
            string? legacyUid = NormalizeUid(contrib.GetString(key));
            if (legacyUid != null)
            {
                list.Add(new Share(legacyUid, 1));
            }
        }

        return new ProsequorBlob(maker, list, recipe, readyAt, anvilSplits, affixes, mods, qualityRank);
    }

    public bool Equals(ProsequorBlob? other) =>
        other != null
        && string.Equals(ContentHash, other.ContentHash, StringComparison.Ordinal);

    public override bool Equals(object? obj) => Equals(obj as ProsequorBlob);

    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(ContentHash);

    public static bool operator ==(ProsequorBlob? a, ProsequorBlob? b) =>
        ReferenceEquals(a, b) || (a is not null && a.Equals(b));

    public static bool operator !=(ProsequorBlob? a, ProsequorBlob? b) => !(a == b);

    static IReadOnlyList<Share> FromUidList(IReadOnlyList<string>? uids)
    {
        if (uids == null || uids.Count == 0)
        {
            return Array.Empty<Share>();
        }

        List<Share> list = new(uids.Count);
        HashSet<string> seen = new(StringComparer.Ordinal);
        for (int i = 0; i < uids.Count; i++)
        {
            string? uid = NormalizeUid(uids[i]);
            if (uid == null || !seen.Add(uid))
            {
                continue;
            }

            list.Add(new Share(uid, 1));
        }

        return list;
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

    static int NormalizeQualityRank(int rank) => rank > 0 ? rank : 0;

    static string? NormalizeRecipe(string? recipe)
    {
        if (string.IsNullOrWhiteSpace(recipe))
        {
            return null;
        }

        string trimmed = recipe.Trim();
        return trimmed.Length == 0 ? null : trimmed;
    }

    static IReadOnlyList<Share> NormalizeShares(IReadOnlyList<Share>? contributors)
    {
        if (contributors == null || contributors.Count == 0)
        {
            return Array.Empty<Share>();
        }

        Dictionary<string, int> map = new(StringComparer.Ordinal);
        for (int i = 0; i < contributors.Count; i++)
        {
            Share share = contributors[i];
            string? uid = NormalizeUid(share.PlayerUid);
            if (uid == null || share.Weight <= 0)
            {
                continue;
            }

            map.TryGetValue(uid, out int current);
            map[uid] = current + share.Weight;
        }

        if (map.Count == 0)
        {
            return Array.Empty<Share>();
        }

        List<Share> list = new(map.Count);
        foreach (KeyValuePair<string, int> pair in map.OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            list.Add(new Share(pair.Key, pair.Value));
        }

        return list;
    }

    static IReadOnlyList<ItemAffixEntry> NormalizeAffixes(IReadOnlyList<ItemAffixEntry>? affixes)
    {
        if (affixes == null || affixes.Count == 0)
        {
            return Array.Empty<ItemAffixEntry>();
        }

        List<ItemAffixEntry> list = new(affixes.Count);
        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < affixes.Count; i++)
        {
            ItemAffixEntry entry = affixes[i];
            string code = entry.Code?.Trim() ?? "";
            string lang = entry.LangKey?.Trim() ?? "";
            if (code.Length == 0 || lang.Length == 0 || !seen.Add(code))
            {
                continue;
            }

            string? color = string.IsNullOrWhiteSpace(entry.Color) ? null : entry.Color.Trim();
            list.Add(new ItemAffixEntry(code, lang, color));
        }

        return list.Count == 0 ? Array.Empty<ItemAffixEntry>() : list;
    }

    static IReadOnlyList<ModFactor> NormalizeMods(IReadOnlyList<ModFactor>? mods)
    {
        if (mods == null || mods.Count == 0)
        {
            return Array.Empty<ModFactor>();
        }

        Dictionary<string, float> map = new(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < mods.Count; i++)
        {
            ModFactor mod = mods[i];
            string? key = NormalizeRecipe(mod.Key);
            if (key == null || !float.IsFinite(mod.Factor) || mod.Factor <= 0f)
            {
                continue;
            }

            map[key] = mod.Factor;
        }

        if (map.Count == 0)
        {
            return Array.Empty<ModFactor>();
        }

        List<ModFactor> list = new(map.Count);
        foreach (KeyValuePair<string, float> pair in map.OrderBy(p => p.Key, StringComparer.OrdinalIgnoreCase))
        {
            list.Add(new ModFactor(pair.Key, pair.Value));
        }

        return list;
    }

    void WriteAffixes(ITreeAttribute tree)
    {
        if (Affixes.Count == 0)
        {
            if (tree.HasAttribute(AffixesKey))
            {
                tree.RemoveAttribute(AffixesKey);
            }

            return;
        }

        ItemAffixes.WriteAll(tree.GetOrAddTreeAttribute(AffixesKey), Affixes);
    }

    void WriteMods(ITreeAttribute tree)
    {
        if (Mods.Count == 0)
        {
            if (tree.HasAttribute(ModsKey))
            {
                tree.RemoveAttribute(ModsKey);
            }

            return;
        }

        ITreeAttribute mods = tree.GetOrAddTreeAttribute(ModsKey);
        List<string> stale = new();
        foreach (KeyValuePair<string, IAttribute> kv in mods)
        {
            stale.Add(kv.Key);
        }

        for (int i = 0; i < stale.Count; i++)
        {
            mods.RemoveAttribute(stale[i]);
        }

        for (int i = 0; i < Mods.Count; i++)
        {
            ModFactor mod = Mods[i];
            mods.SetFloat(mod.Key, mod.Factor);
        }
    }

    static IReadOnlyList<ModFactor> ReadMods(ITreeAttribute? tree)
    {
        if (tree == null)
        {
            return Array.Empty<ModFactor>();
        }

        List<ModFactor> list = new();
        foreach (KeyValuePair<string, IAttribute> kv in tree)
        {
            if (string.IsNullOrWhiteSpace(kv.Key))
            {
                continue;
            }

            float factor = tree.GetFloat(kv.Key, 0f);
            if (!float.IsFinite(factor) || factor <= 0f)
            {
                continue;
            }

            list.Add(new ModFactor(kv.Key.Trim(), factor));
        }

        return list;
    }

    static string ComputeHash(
        string? makerUid,
        IReadOnlyList<Share> contributors,
        IReadOnlyList<ItemAffixEntry> affixes,
        IReadOnlyList<ModFactor> mods,
        int qualityRank)
    {
        StringBuilder sb = new(64);
        sb.Append(makerUid ?? "");
        sb.Append('\u001f');
        for (int i = 0; i < contributors.Count; i++)
        {
            if (i > 0)
            {
                sb.Append('\u001e');
            }

            Share share = contributors[i];
            sb.Append(share.PlayerUid);
            sb.Append('\u001d');
            sb.Append(share.Weight.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        if (affixes.Count > 0 || mods.Count > 0)
        {
            sb.Append('\u001c');
            for (int i = 0; i < affixes.Count; i++)
            {
                if (i > 0)
                {
                    sb.Append('\u001e');
                }

                ItemAffixEntry affix = affixes[i];
                sb.Append(affix.Code);
                sb.Append('\u001d');
                sb.Append(affix.LangKey);
                sb.Append('\u001d');
                sb.Append(affix.Color ?? "");
            }

            sb.Append('\u001c');
            for (int i = 0; i < mods.Count; i++)
            {
                if (i > 0)
                {
                    sb.Append('\u001e');
                }

                ModFactor mod = mods[i];
                sb.Append(mod.Key);
                sb.Append('\u001d');
                sb.Append(mod.Factor.ToString("R", System.Globalization.CultureInfo.InvariantCulture));
            }
        }

        if (qualityRank > 0)
        {
            sb.Append('\u001b');
            sb.Append(qualityRank.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        ulong hash = 14695981039346656037UL;
        foreach (byte b in Encoding.UTF8.GetBytes(sb.ToString()))
        {
            hash ^= b;
            hash *= 1099511628211UL;
        }

        return hash.ToString("x16");
    }
}
