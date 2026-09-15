using Vintagestory.API.Common;

namespace Prosequor.Data;

public interface ITraitAttributeRegistry
{
    IReadOnlyDictionary<string, TraitAttributeMapping> ByCode { get; }
    IReadOnlyDictionary<string, Dictionary<string, int>> ClassStartingScores { get; }
    bool TryGet(string code, out TraitAttributeMapping mapping);
    void SetClassStartingScores(string classCode, Dictionary<string, int> scores);
    void ClearClassStartingScores();
}

/// <summary>
/// Loads trait→attribute lookups from <c>config/prosequor/trait-attributes</c> (array JSON; last-win by code).
/// </summary>
public sealed class TraitAttributeRegistry : ITraitAttributeRegistry
{
    readonly Dictionary<string, TraitAttributeMapping> byCode = new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<string, Dictionary<string, int>> classStartingScores =
        new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyDictionary<string, TraitAttributeMapping> ByCode => byCode;

    public IReadOnlyDictionary<string, Dictionary<string, int>> ClassStartingScores =>
        classStartingScores;

    public bool TryGet(string code, out TraitAttributeMapping mapping)
    {
        if (!string.IsNullOrWhiteSpace(code) && byCode.TryGetValue(code.Trim(), out TraitAttributeMapping? found) && found != null)
        {
            mapping = found;
            return true;
        }

        mapping = null!;
        return false;
    }

    public void SetClassStartingScores(string classCode, Dictionary<string, int> scores)
    {
        if (string.IsNullOrWhiteSpace(classCode) || scores == null)
        {
            return;
        }

        classStartingScores[classCode.Trim()] = scores;
    }

    public void ClearClassStartingScores() => classStartingScores.Clear();

    public void Register(TraitAttributeMapping mapping)
    {
        if (mapping == null || string.IsNullOrWhiteSpace(mapping.Code))
        {
            return;
        }

        byCode[mapping.Code.Trim()] = mapping;
    }

    public void LoadFromAssets(ICoreAPI api)
    {
        byCode.Clear();

        List<KeyValuePair<AssetLocation, TraitAttributeMappingJson[]>> assets = api.Assets
            .GetMany<TraitAttributeMappingJson[]>(api.Logger, "config/prosequor/trait-attributes", null)
            .OrderBy(kv => kv.Key.Domain, StringComparer.OrdinalIgnoreCase)
            .ThenBy(kv => kv.Key.Path, StringComparer.OrdinalIgnoreCase)
            .ToList();

        int loaded = 0;
        foreach (KeyValuePair<AssetLocation, TraitAttributeMappingJson[]> kv in assets)
        {
            TraitAttributeMappingJson[]? list = kv.Value;
            if (list == null || list.Length == 0)
            {
                continue;
            }

            foreach (TraitAttributeMappingJson row in list)
            {
                if (row == null || string.IsNullOrWhiteSpace(row.code))
                {
                    api.Logger.Warning(
                        "[prosequor] Skipping trait-attribute row in {0}: missing code.",
                        kv.Key);
                    continue;
                }

                string code = row.code.Trim();
                Dictionary<string, int> deltas = new(StringComparer.OrdinalIgnoreCase);
                if (row.attributes != null)
                {
                    foreach (KeyValuePair<string, int> pair in row.attributes)
                    {
                        string? attrId = AttributeIds.Canonicalize(pair.Key);
                        if (attrId == null)
                        {
                            api.Logger.Warning(
                                "[prosequor] Trait '{0}' in {1}: unknown attribute '{2}'.",
                                code,
                                kv.Key,
                                pair.Key);
                            continue;
                        }

                        deltas[attrId] = pair.Value;
                    }
                }

                if (byCode.ContainsKey(code))
                {
                    api.Logger.Warning(
                        "[prosequor] Trait-attribute '{0}' redefined by {1}; last-win.",
                        code,
                        kv.Key);
                }

                byCode[code] = new TraitAttributeMapping
                {
                    Code = code,
                    Attributes = deltas,
                    RetainTrait = row.retainTrait
                };
                loaded++;
            }
        }

        api.Logger.Notification(
            "[prosequor] Loaded {0} trait-attribute mapping(s) from {1} file(s).",
            byCode.Count,
            assets.Count);
    }
}
