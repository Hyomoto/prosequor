using Vintagestory.API.Common;

namespace Prosequor.Data;

/// <summary>One <c>config/prosequor/options</c> file. Other mods say what they need left alone.</summary>
public sealed class OptionsFileJson
{
    /// <summary>When true, leave the vanilla traits tab visible.</summary>
    public bool traitsTab { get; set; }

    /// <summary>Skip this file unless every clause is met. Same shape as contribution <c>dependsOn</c>.</summary>
    public ContributionModDependence[]? dependsOn { get; set; }
}

/// <summary>
/// Loads <c>config/prosequor/options.json</c> and <c>options/*.json</c>.
/// <see cref="TraitsTab"/> is true when any applied file sets <c>traitsTab</c>.
/// Not part of the content fingerprint.
/// </summary>
public sealed class OptionsRegistry
{
    public bool TraitsTab { get; private set; }

    /// <summary>
    /// Hide the vanilla traits tab unless the player turned it on or any options file asked for it.
    /// A later <c>false</c> does not cancel an earlier <c>true</c>.
    /// </summary>
    public static bool SuppressVanillaTraitsTab(bool userEnabled, bool anyContributionEnabled) =>
        !userEnabled && !anyContributionEnabled;

    public void LoadFromAssets(ICoreAPI api)
    {
        TraitsTab = false;
        HashSet<string> loadedModIds = ContributionDependsOn.LoadedModIds(api);
        List<KeyValuePair<AssetLocation, OptionsFileJson>> assets = api.Assets
            .GetMany<OptionsFileJson>(api.Logger, "config/prosequor/options", null)
            .OrderBy(kv => kv.Key.Domain, StringComparer.OrdinalIgnoreCase)
            .ThenBy(kv => kv.Key.Path, StringComparer.OrdinalIgnoreCase)
            .ToList();

        int loaded = 0;
        foreach (KeyValuePair<AssetLocation, OptionsFileJson> kv in assets)
        {
            if (kv.Value == null)
            {
                api.Logger.Warning("[prosequor] Skipping options file {0}: not an object.", kv.Key);
                continue;
            }

            if (!Consider(kv.Value, loadedModIds))
            {
                api.Logger.VerboseDebug(
                    "[prosequor] Skipping options file {0}: unmet dependsOn ({1})",
                    kv.Key,
                    ContributionDependsOn.Format(kv.Value.dependsOn));
                continue;
            }

            loaded++;
        }

        api.Logger.Notification(
            "[prosequor] Loaded {0} options file(s). traitsTab: {1}.",
            loaded,
            TraitsTab);
    }

    /// <summary>
    /// Fold one file. Returns false when <c>dependsOn</c> skips it.
    /// A skipped file does not change <see cref="TraitsTab"/>.
    /// </summary>
    public bool Consider(OptionsFileJson? file, ISet<string> loadedModIds)
    {
        if (file == null || !ContributionDependsOn.IsSatisfied(file.dependsOn, loadedModIds))
        {
            return false;
        }

        if (file.traitsTab)
        {
            TraitsTab = true;
        }

        return true;
    }
}
