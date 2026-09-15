using Vintagestory.API.Common;

namespace Prosequor.Data;

/// <summary>
/// One engine-style <c>dependsOn</c> clause. Same JSON shape as Vintage Story
/// JSON-patch <c>dependsOn</c>: the named mod must be loaded, or must not be
/// loaded when <see cref="invert"/> is true.
/// </summary>
public sealed class ContributionModDependence
{
    public string? modid { get; set; }

    public bool invert { get; set; }
}

/// <summary>
/// Evaluates contribution and collection <c>dependsOn</c> the same way
/// <c>ModJsonPatchLoader</c> evaluates patch <c>dependsOn</c>: every clause
/// must satisfy <c>loaded XOR invert</c>.
/// </summary>
public static class ContributionDependsOn
{
    public static HashSet<string> LoadedModIds(ICoreAPI api)
    {
        HashSet<string> ids = [];
        if (api.ModLoader?.Mods == null)
        {
            return ids;
        }

        foreach (Mod mod in api.ModLoader.Mods)
        {
            string? id = mod?.Info?.ModID;
            if (!string.IsNullOrWhiteSpace(id))
            {
                ids.Add(id);
            }
        }

        return ids;
    }

    /// <summary>
    /// Null or empty <paramref name="dependsOn"/> is satisfied. Otherwise AND
    /// every clause: the mod id is in <paramref name="loadedModIds"/> XOR
    /// <see cref="ContributionModDependence.invert"/>.
    /// </summary>
    public static bool IsSatisfied(
        IReadOnlyList<ContributionModDependence>? dependsOn,
        ISet<string> loadedModIds)
    {
        if (dependsOn == null || dependsOn.Count == 0)
        {
            return true;
        }

        foreach (ContributionModDependence? clause in dependsOn)
        {
            if (clause == null)
            {
                return false;
            }

            bool loaded = clause.modid != null && loadedModIds.Contains(clause.modid);
            if (!(loaded ^ clause.invert))
            {
                return false;
            }
        }

        return true;
    }

    public static string Format(IReadOnlyList<ContributionModDependence>? dependsOn)
    {
        if (dependsOn == null || dependsOn.Count == 0)
        {
            return "";
        }

        return string.Join(
            ",",
            dependsOn
                .Where(clause => clause != null)
                .Select(clause => (clause.invert ? "!" : "") + clause.modid));
    }
}
