using Vintagestory.API.Common;

namespace Prosequor.Data;

public interface ILevelUpRegistry
{
    IReadOnlyList<LevelUpRuleDef> Rules { get; }
}

/// <summary>
/// Loads player level-up grant rules from <c>config/prosequor/level-ups</c>, then merges
/// <c>levelUps</c> grafts from <c>config/prosequor/contributions/*.json</c>
/// (unmet <c>dependsOn</c> skips the entry).
/// </summary>
public sealed class LevelUpRegistry : ILevelUpRegistry
{
    readonly List<LevelUpRuleDef> rules = new();

    public IReadOnlyList<LevelUpRuleDef> Rules => rules;

    /// <summary>
    /// Compiles a draft rule map (last-win by id preserves the later row's source order).
    /// </summary>
    public static List<LevelUpRuleDef> CompileDrafts(
        IDictionary<string, LevelUpRuleJson> drafts,
        Action<string> error,
        Action<string>? warn = null)
    {
        ArgumentNullException.ThrowIfNull(drafts);
        ArgumentNullException.ThrowIfNull(error);

        List<LevelUpRuleDef> compiled = new();
        int sourceOrder = 0;
        foreach (KeyValuePair<string, LevelUpRuleJson> kv in drafts
                     .OrderBy(d => d.Key, StringComparer.OrdinalIgnoreCase))
        {
            List<string> errors = new();
            LevelUpRuleDef? def = LevelUpRuleCompiler.Compile(kv.Value, ref sourceOrder, errors);
            if (def == null)
            {
                foreach (string msg in errors)
                {
                    error($"[prosequor] {msg}");
                }

                continue;
            }

            if (errors.Count > 0 && warn != null)
            {
                foreach (string msg in errors)
                {
                    warn($"[prosequor] {msg}");
                }
            }

            compiled.Add(def);
        }

        compiled.Sort((a, b) =>
        {
            int byPri = a.Priority.CompareTo(b.Priority);
            return byPri != 0 ? byPri : a.SourceOrder.CompareTo(b.SourceOrder);
        });
        return compiled;
    }

    public void LoadFromAssets(ICoreAPI api)
    {
        rules.Clear();

        Dictionary<string, LevelUpRuleJson> drafts = new(StringComparer.OrdinalIgnoreCase);
        List<KeyValuePair<AssetLocation, LevelUpFileJson>> assets = api.Assets
            .GetMany<LevelUpFileJson>(api.Logger, "config/prosequor/level-ups", null)
            .OrderBy(kv => kv.Key.Domain, StringComparer.OrdinalIgnoreCase)
            .ThenBy(kv => kv.Key.Path, StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (KeyValuePair<AssetLocation, LevelUpFileJson> kv in assets)
        {
            LevelUpFileJson? file = kv.Value;
            if (file?.rules == null || file.rules.Length == 0)
            {
                continue;
            }

            foreach (LevelUpRuleJson? row in file.rules)
            {
                if (row == null)
                {
                    continue;
                }

                string id = row.id?.Trim() ?? "";
                if (id.Length == 0)
                {
                    api.Logger.Warning(
                        "[prosequor] Skipping level-up rule in {0}: missing id.",
                        kv.Key);
                    continue;
                }

                row.id = id;
                if (drafts.ContainsKey(id))
                {
                    api.Logger.Warning(
                        "[prosequor] Level-up rule '{0}' redefined by {1}; last-win.",
                        id,
                        kv.Key);
                }

                drafts[id] = row;
            }
        }

        ApplyContributions(api, drafts);

        List<LevelUpRuleDef> compiled = CompileDrafts(
            drafts,
            msg => api.Logger.Error(msg),
            msg => api.Logger.Warning(msg));
        rules.AddRange(compiled);

        api.Logger.Notification(
            "[prosequor] Loaded {0} level-up rule(s) from {1} file(s).",
            rules.Count,
            assets.Count);
    }

    static void ApplyContributions(ICoreAPI api, Dictionary<string, LevelUpRuleJson> drafts)
    {
        HashSet<string> loadedModIds = ContributionDependsOn.LoadedModIds(api);
        List<KeyValuePair<AssetLocation, SkillContributionJson[]>> contribAssets = api.Assets
            .GetMany<SkillContributionJson[]>(api.Logger, "config/prosequor/contributions/", null)
            .OrderBy(kv => kv.Key.Domain, StringComparer.OrdinalIgnoreCase)
            .ThenBy(kv => kv.Key.Path, StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (KeyValuePair<AssetLocation, SkillContributionJson[]> kv in contribAssets)
        {
            SkillContributionJson[]? entries = kv.Value;
            if (entries == null || entries.Length == 0)
            {
                continue;
            }

            foreach (SkillContributionJson entry in entries)
            {
                if (entry?.levelUps == null)
                {
                    continue;
                }

                if (!ContributionDependsOn.IsSatisfied(entry.dependsOn, loadedModIds))
                {
                    api.Logger.VerboseDebug(
                        "[prosequor] Skipping levelUps contribution in {0}: unmet dependsOn ({1})",
                        kv.Key,
                        ContributionDependsOn.Format(entry.dependsOn));
                    continue;
                }

                LevelUpContributionJson graft = entry.levelUps;
                if (!graft.TryParseDisable(out bool disableAll, out string[] ruleIds, out string? disableError))
                {
                    api.Logger.Warning(
                        "[prosequor] Skipping levelUps contribution in {0}: {1}",
                        kv.Key,
                        disableError);
                    continue;
                }

                if (disableAll)
                {
                    drafts.Clear();
                }
                else
                {
                    foreach (string ruleId in ruleIds)
                    {
                        if (!drafts.Remove(ruleId))
                        {
                            api.Logger.Warning(
                                "[prosequor] levelUps disable unknown rule '{0}' from {1}.",
                                ruleId,
                                kv.Key);
                        }
                    }
                }

                if (graft.rules == null || graft.rules.Length == 0)
                {
                    continue;
                }

                foreach (LevelUpRuleJson? row in graft.rules)
                {
                    if (row == null)
                    {
                        continue;
                    }

                    string id = row.id?.Trim() ?? "";
                    if (id.Length == 0)
                    {
                        api.Logger.Warning(
                            "[prosequor] Skipping levelUps rule in {0}: missing id.",
                            kv.Key);
                        continue;
                    }

                    row.id = id;
                    if (drafts.ContainsKey(id))
                    {
                        api.Logger.Warning(
                            "[prosequor] Level-up rule '{0}' replaced by contribution {1}; last-win.",
                            id,
                            kv.Key);
                    }

                    drafts[id] = row;
                }
            }
        }
    }
}
