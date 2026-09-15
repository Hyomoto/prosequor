using Newtonsoft.Json.Linq;
using Vintagestory.API.Common;

namespace Prosequor.Data;

/// <summary>
/// Discovers shared affix lists from every loaded mod under
/// <c>config/prosequor/affixes.json</c> (and <c>config/prosequor/affixes/*.json</c>).
/// List ids must be <c>assetDomain:localId</c>. Duplicate ids replace the whole list (last-win).
/// </summary>
public sealed class AffixListRegistry
{
    readonly Dictionary<string, AffixListDef> byId = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyCollection<AffixListDef> All => byId.Values;

    public bool TryGet(string id, out AffixListDef def)
    {
        if (byId.TryGetValue(id, out AffixListDef? found) && found != null)
        {
            def = found;
            return true;
        }

        def = null!;
        return false;
    }

    /// <summary>Test / seed helper: replace all lists.</summary>
    public void ReplaceAll(IEnumerable<AffixListDef> lists)
    {
        byId.Clear();
        foreach (AffixListDef list in lists)
        {
            if (list == null || string.IsNullOrWhiteSpace(list.Id))
            {
                continue;
            }

            byId[list.Id.Trim()] = list;
        }
    }

    public bool TryResolve(
        string listId,
        JToken? itemToken,
        out AffixListEntryDef entry,
        out string error)
    {
        entry = null!;
        if (string.IsNullOrWhiteSpace(listId))
        {
            error = "list is required.";
            return false;
        }

        string id = listId.Trim();
        if (!TryGet(id, out AffixListDef list) || list.Entries.Count == 0)
        {
            error = $"unknown or empty affix list '{id}'.";
            return false;
        }

        if (itemToken == null || itemToken.Type == JTokenType.Null)
        {
            error = "item is required when list is set.";
            return false;
        }

        if (itemToken.Type == JTokenType.Integer
            || itemToken.Type == JTokenType.Float)
        {
            int index = itemToken.Value<int>();
            if (index < 0 || index >= list.Entries.Count)
            {
                error = $"affix list '{id}' has no item index {index} (count {list.Entries.Count}).";
                return false;
            }

            entry = list.Entries[index];
            error = "";
            return true;
        }

        if (itemToken.Type == JTokenType.String)
        {
            string? code = itemToken.Value<string>()?.Trim();
            if (string.IsNullOrWhiteSpace(code))
            {
                error = "item code must be non-empty.";
                return false;
            }

            foreach (AffixListEntryDef candidate in list.Entries)
            {
                if (string.Equals(candidate.Code, code, StringComparison.OrdinalIgnoreCase))
                {
                    entry = candidate;
                    error = "";
                    return true;
                }
            }

            error = $"affix list '{id}' has no entry code '{code}'.";
            return false;
        }

        error = "item must be a 0-based index or an entry code string.";
        return false;
    }

    public void LoadFromAssets(ICoreAPI api)
    {
        byId.Clear();

        List<KeyValuePair<AssetLocation, AffixListJson[]>> assets = api.Assets
            .GetMany<AffixListJson[]>(api.Logger, "config/prosequor/affixes", null)
            .OrderBy(kv => kv.Key.Domain, StringComparer.OrdinalIgnoreCase)
            .ThenBy(kv => kv.Key.Path, StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (KeyValuePair<AssetLocation, AffixListJson[]> kv in assets)
        {
            IngestRows(kv.Value, kv.Key.Domain, kv.Key.ToString(), msg => api.Logger.Warning(msg));
        }

        api.Logger.Notification(
            "[prosequor] Loaded {0} affix list(s) from assets.",
            byId.Count);
    }

    /// <summary>
    /// Offline / test load for one domain: reads <c>affixes.json</c> and <c>affixes/*.json</c>
    /// under <paramref name="configDir"/> (e.g. <c>assets/prosequor/config/prosequor</c>).
    /// </summary>
    public void LoadFromConfigDirectory(string configDir, string assetDomain, Action<string>? warn = null)
    {
        byId.Clear();
        if (string.IsNullOrWhiteSpace(configDir) || !Directory.Exists(configDir))
        {
            return;
        }

        List<string> paths = new();
        string rootFile = Path.Combine(configDir, "affixes.json");
        if (File.Exists(rootFile))
        {
            paths.Add(rootFile);
        }

        string nested = Path.Combine(configDir, "affixes");
        if (Directory.Exists(nested))
        {
            paths.AddRange(
                Directory.GetFiles(nested, "*.json", SearchOption.TopDirectoryOnly)
                    .OrderBy(p => p, StringComparer.OrdinalIgnoreCase));
        }

        foreach (string path in paths)
        {
            AffixListJson[]? rows;
            try
            {
                rows = Newtonsoft.Json.JsonConvert.DeserializeObject<AffixListJson[]>(
                    File.ReadAllText(path));
            }
            catch (Exception ex)
            {
                warn?.Invoke($"[prosequor] Failed to parse affix lists in {path}: {ex.Message}");
                continue;
            }

            IngestRows(rows, assetDomain, path, warn);
        }
    }

    void IngestRows(
        AffixListJson[]? rows,
        string domain,
        string sourceLabel,
        Action<string>? warn)
    {
        if (rows == null || rows.Length == 0)
        {
            return;
        }

        foreach (AffixListJson row in rows)
        {
            if (row == null || string.IsNullOrWhiteSpace(row.id))
            {
                warn?.Invoke($"[prosequor] Skipping affix list in {sourceLabel}: missing id.");
                continue;
            }

            string listId = row.id.Trim();
            if (!TryValidateNamespacedId(listId, domain, out string? idError))
            {
                warn?.Invoke(
                    $"[prosequor] Skipping affix list '{listId}' in {sourceLabel}: {idError}");
                continue;
            }

            List<AffixListEntryDef> entries = new();
            if (row.entries != null)
            {
                foreach (AffixListEntryJson? entry in row.entries)
                {
                    if (entry == null)
                    {
                        continue;
                    }

                    string code = entry.code?.Trim() ?? "";
                    string lang = entry.lang?.Trim() ?? "";
                    if (code.Length == 0 || lang.Length == 0)
                    {
                        warn?.Invoke(
                            $"[prosequor] Skipping empty affix entry in '{listId}' ({sourceLabel}).");
                        continue;
                    }

                    string? color = entry.color?.Trim();
                    if (string.IsNullOrWhiteSpace(color))
                    {
                        color = null;
                    }

                    entries.Add(new AffixListEntryDef
                    {
                        Code = code,
                        Lang = lang,
                        Color = color
                    });
                }
            }

            if (entries.Count == 0)
            {
                warn?.Invoke(
                    $"[prosequor] Affix list '{listId}' in {sourceLabel} has no usable entries.");
                continue;
            }

            byId[listId] = new AffixListDef
            {
                Id = listId,
                Entries = entries
            };
        }
    }

    internal static bool TryValidateNamespacedId(string id, string assetDomain, out string? error)
    {
        int colon = id.IndexOf(':');
        if (colon <= 0 || colon >= id.Length - 1)
        {
            error = "id must be namespaced as domain:localId.";
            return false;
        }

        string domain = id[..colon];
        if (!string.Equals(domain, assetDomain, StringComparison.OrdinalIgnoreCase))
        {
            error = $"id domain '{domain}' must match asset domain '{assetDomain}'.";
            return false;
        }

        error = null;
        return true;
    }
}
