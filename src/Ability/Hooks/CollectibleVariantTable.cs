using Vintagestory.API.Common;

namespace Prosequor.Ability.Hooks;

/// <summary>
/// Startup-built map from <c>{firstCodePart}-{family}</c> keys to replacement collectibles.
/// Populators register entries at GameReady; actions query cheaply at runtime.
/// </summary>
public sealed class CollectibleVariantTable
{
    readonly Dictionary<string, List<CollectibleObject>> byKey =
        new(StringComparer.OrdinalIgnoreCase);

    public void Clear() => byKey.Clear();

    public void Add(string key, CollectibleObject replacement)
    {
        if (string.IsNullOrWhiteSpace(key) || replacement == null || replacement.Id == 0)
        {
            return;
        }

        if (!byKey.TryGetValue(key, out List<CollectibleObject>? list))
        {
            list = new List<CollectibleObject>();
            byKey[key] = list;
        }

        for (int i = 0; i < list.Count; i++)
        {
            if (list[i].Id == replacement.Id)
            {
                return;
            }
        }

        list.Add(replacement);
    }

    public IReadOnlyList<CollectibleObject> Get(string key)
    {
        if (!byKey.TryGetValue(key, out List<CollectibleObject>? list) || list.Count == 0)
        {
            return Array.Empty<CollectibleObject>();
        }

        return list;
    }

    public int Count(string key) =>
        byKey.TryGetValue(key, out List<CollectibleObject>? list) ? list.Count : 0;

    /// <summary>
    /// Uniform pick from the list for <paramref name="key"/>, skipping <paramref name="exclude"/>
    /// when present. Returns false when no eligible replacements remain.
    /// </summary>
    public bool TryPick(
        string key,
        CollectibleObject? exclude,
        Random rand,
        out CollectibleObject pick)
    {
        pick = null!;
        IReadOnlyList<CollectibleObject> list = Get(key);
        if (list.Count == 0)
        {
            return false;
        }

        if (exclude == null || exclude.Id == 0)
        {
            pick = list[rand.Next(list.Count)];
            return true;
        }

        int eligible = 0;
        for (int i = 0; i < list.Count; i++)
        {
            if (list[i].Id != exclude.Id)
            {
                eligible++;
            }
        }

        if (eligible == 0)
        {
            return false;
        }

        int roll = rand.Next(eligible);
        for (int i = 0; i < list.Count; i++)
        {
            if (list[i].Id == exclude.Id)
            {
                continue;
            }

            if (roll == 0)
            {
                pick = list[i];
                return true;
            }

            roll--;
        }

        return false;
    }

    public static string Key(string firstCodePart, string family) =>
        firstCodePart + "-" + family;
}
