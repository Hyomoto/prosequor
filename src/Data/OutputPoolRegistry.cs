using Prosequor.Ability.Hooks;
using Vintagestory.API.Common;

namespace Prosequor.Data;

/// <summary>
/// Discovers explicit item output pools from every loaded mod under
/// <c>config/prosequor/pools.json</c> (and <c>config/prosequor/pools/*.json</c>).
/// Pool ids must be <c>assetDomain:localId</c>. Duplicate ids merge entries by code (later weight wins).
/// </summary>
public sealed class OutputPoolRegistry
{
    readonly Dictionary<string, OutputPoolDef> byId = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyCollection<OutputPoolDef> All => byId.Values;

    public bool TryGet(string id, out OutputPoolDef def)
    {
        if (byId.TryGetValue(id, out OutputPoolDef? found) && found != null)
        {
            def = found;
            return true;
        }

        def = null!;
        return false;
    }

    public void LoadFromAssets(ICoreAPI api, CollectionIndex collections)
    {
        byId.Clear();

        Dictionary<string, Dictionary<string, OutputPoolEntryDef>> drafts =
            new(StringComparer.OrdinalIgnoreCase);

        List<KeyValuePair<AssetLocation, OutputPoolJson[]>> assets = api.Assets
            .GetMany<OutputPoolJson[]>(api.Logger, "config/prosequor/pools", null)
            .OrderBy(kv => kv.Key.Domain, StringComparer.OrdinalIgnoreCase)
            .ThenBy(kv => kv.Key.Path, StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (KeyValuePair<AssetLocation, OutputPoolJson[]> kv in assets)
        {
            OutputPoolJson[]? list = kv.Value;
            if (list == null || list.Length == 0)
            {
                continue;
            }

            string domain = kv.Key.Domain;
            foreach (OutputPoolJson row in list)
            {
                if (row == null || string.IsNullOrWhiteSpace(row.id))
                {
                    api.Logger.Warning(
                        "[prosequor] Skipping output pool in {0}: missing id.",
                        kv.Key);
                    continue;
                }

                string poolId = row.id.Trim();
                if (!TryValidateNamespacedId(poolId, domain, out string? idError))
                {
                    api.Logger.Warning(
                        "[prosequor] Skipping output pool '{0}' in {1}: {2}",
                        poolId,
                        kv.Key,
                        idError);
                    continue;
                }

                if (!drafts.TryGetValue(poolId, out Dictionary<string, OutputPoolEntryDef>? entries))
                {
                    entries = new Dictionary<string, OutputPoolEntryDef>(StringComparer.OrdinalIgnoreCase);
                    drafts[poolId] = entries;
                }

                if (row.entries == null)
                {
                    continue;
                }

                foreach (OutputPoolEntryJson? entry in row.entries)
                {
                    if (entry == null || string.IsNullOrWhiteSpace(entry.code))
                    {
                        api.Logger.Warning(
                            "[prosequor] Skipping empty pool entry in '{0}' ({1}).",
                            poolId,
                            kv.Key);
                        continue;
                    }

                    string code = entry.code.Trim();
                    float weight = entry.weight > 0f ? entry.weight : 1f;
                    entries[code] = new OutputPoolEntryDef
                    {
                        Code = code,
                        Weight = weight
                    };
                }
            }
        }

        foreach (KeyValuePair<string, Dictionary<string, OutputPoolEntryDef>> kv in drafts)
        {
            OutputPoolDef def = new()
            {
                Id = kv.Key,
                Entries = kv.Value.Values.ToList()
            };
            byId[def.Id] = def;
            collections.EnsureKey(def.Id);
        }

        api.Logger.Notification(
            "[prosequor] Loaded {0} output pool(s) from assets.",
            byId.Count);
    }

    /// <summary>Resolves pool item codes into the collection index (weighted picks).</summary>
    public void ApplyToIndex(ICoreAPI api, CollectionIndex index)
    {
        int resolved = 0;
        int missing = 0;
        foreach (OutputPoolDef pool in byId.Values)
        {
            index.EnsureKey(pool.Id);
            foreach (OutputPoolEntryDef entry in pool.Entries)
            {
                Item? item = api.World.GetItem(new AssetLocation(entry.Code));
                if (item == null)
                {
                    missing++;
                    api.Logger.Warning(
                        "[prosequor] Output pool '{0}': unknown item '{1}'.",
                        pool.Id,
                        entry.Code);
                    continue;
                }

                index.AddWeighted(pool.Id, item.Code?.ToString() ?? entry.Code, entry.Weight);
                resolved++;
            }
        }

        api.Logger.Notification(
            "[prosequor] Output pools indexed: {0} item(s) resolved, {1} missing.",
            resolved,
            missing);
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
