using Prosequor.Ability.Hooks;
using Vintagestory.API.Common;

namespace Prosequor.Data;

public class CollectionJson
{
    public string id { get; set; } = "";
    public string[]? includes { get; set; }

    /// <summary>Other collection ids whose membership is copied into this one at GameReady.</summary>
    public string[]? unions { get; set; }

    /// <summary>
    /// Other collection ids whose positive membership (includes and fills) is subtracted after
    /// every include is built, and before unions copy. Union copies are not subtracted.
    /// </summary>
    public string[]? excludes { get; set; }

    /// <summary>
    /// Engine-style mod gates. The whole row is skipped unless every clause is met.
    /// Same shape as JSON-patch / contribution <c>dependsOn</c>.
    /// </summary>
    public ContributionModDependence[]? dependsOn { get; set; }
}

/// <summary>
/// Discovers collection definitions from <c>config/prosequor/collections.json</c>
/// (and <c>collections/*.json</c>). Unmet <c>dependsOn</c> skips that row.
/// Keys are available at AssetsFinalize; membership is filled at GameReady.
/// </summary>
public sealed class CollectionRegistry
{
    public CollectionIndex Index { get; } = new();

    public void LoadFromAssets(ICoreAPI api)
    {
        Index.ClearAll();

        HashSet<string> loadedModIds = ContributionDependsOn.LoadedModIds(api);
        List<KeyValuePair<AssetLocation, CollectionJson[]>> assets = api.Assets
            .GetMany<CollectionJson[]>(api.Logger, "config/prosequor/collections", null)
            .OrderBy(kv => kv.Key.Domain, StringComparer.OrdinalIgnoreCase)
            .ThenBy(kv => kv.Key.Path, StringComparer.OrdinalIgnoreCase)
            .ToList();

        int loaded = 0;
        foreach (KeyValuePair<AssetLocation, CollectionJson[]> kv in assets)
        {
            CollectionJson[]? list = kv.Value;
            if (list == null || list.Length == 0)
            {
                continue;
            }

            foreach (CollectionJson row in list)
            {
                if (row == null || string.IsNullOrWhiteSpace(row.id))
                {
                    api.Logger.Warning(
                        "[prosequor] Skipping collection in {0}: missing id.",
                        kv.Key);
                    continue;
                }

                if (!ContributionDependsOn.IsSatisfied(row.dependsOn, loadedModIds))
                {
                    api.Logger.VerboseDebug(
                        "[prosequor] Skipping collection '{0}' in {1}: unmet dependsOn ({2})",
                        row.id.Trim(),
                        kv.Key,
                        ContributionDependsOn.Format(row.dependsOn));
                    continue;
                }

                string id = row.id.Trim();
                Index.EnsureKey(id);
                if (row.includes != null)
                {
                    foreach (string? include in row.includes)
                    {
                        if (!string.IsNullOrWhiteSpace(include))
                        {
                            Index.AddIncludePattern(id, include);
                        }
                    }
                }

                if (row.unions != null && row.unions.Length > 0)
                {
                    Index.AddUnion(id, row.unions);
                }

                if (row.excludes != null && row.excludes.Length > 0)
                {
                    Index.AddExclude(id, row.excludes);
                }

                loaded++;
            }
        }

        api.Logger.Notification(
            "[prosequor] Loaded {0} collection definition(s) from assets.",
            loaded);
    }

    /// <summary>
    /// Kept for tests that register keys before loading <c>collections.json</c>.
    /// Shipped membership lives in that file; do not re-add unions here or a patch cannot remove them.
    /// </summary>
    public void EnsureBuiltinKeys()
    {
    }

    public void RebuildMembership(ICoreAPI api)
    {
        Index.ClearMembership();
        Index.ExpandPatterns(api);
        Ability.AbilityBootstrap.FillBuiltinCollections(api, Index);
        Index.ApplyExcludes(msg => api.Logger.Warning(msg));
        Index.MaterializeUnions(msg => api.Logger.Warning(msg));
    }
}
