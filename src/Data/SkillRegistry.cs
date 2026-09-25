using Newtonsoft.Json.Linq;
using Prosequor.Ability;
using Prosequor.Ability.Hooks;
using Prosequor.Xp;
using Vintagestory.API.Common;
using Vintagestory.API.Config;

namespace Prosequor.Data;

public interface ISkillRegistry
{
    IReadOnlyList<SkillDef> All { get; }
    SkillMenuIndex MenuIndex { get; }
    bool TryGet(string id, out SkillDef def);
    bool TryResolve(string idOrDisplayName, string? languageCode, out SkillDef def);
    void Register(SkillDef def);
}

public class SkillRegistry : ISkillRegistry
{
    readonly List<SkillDef> ordered = new();
    readonly Dictionary<string, SkillDef> byId = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<SkillDef> All => ordered;

    public SkillMenuIndex MenuIndex { get; private set; } = SkillMenuIndex.Empty;

    public void Register(SkillDef def)
    {
        if (string.IsNullOrWhiteSpace(def.Id))
        {
            return;
        }

        def.Id = def.Id.Trim();
        if (def.MaxLevel <= 0)
        {
            def.MaxLevel = SkillKindPolicy.MaxLevelFor(def.Kind);
        }

        if (byId.TryGetValue(def.Id, out SkillDef? existing))
        {
            ordered.Remove(existing);
        }

        byId[def.Id] = def;
        ordered.Add(def);
    }

    public bool TryGet(string id, out SkillDef def)
    {
        if (byId.TryGetValue(id, out SkillDef? found) && found != null)
        {
            def = found;
            return true;
        }

        def = null!;
        return false;
    }

    /// <summary>
    /// Resolve an internal id or the localized name visible to the command caller.
    /// Ids take precedence; duplicate display names resolve in registration order.
    /// </summary>
    public bool TryResolve(string idOrDisplayName, string? languageCode, out SkillDef def)
    {
        string query = idOrDisplayName.Trim();
        if (TryGet(query, out def))
        {
            return true;
        }

        foreach (SkillDef candidate in ordered)
        {
            string displayName = DisplayName(candidate, languageCode);
            if (string.Equals(displayName, query, StringComparison.CurrentCultureIgnoreCase))
            {
                def = candidate;
                return true;
            }
        }

        def = null!;
        return false;
    }

    /// <summary>
    /// Discovers base skills and contribution grafts from every loaded mod:
    /// <list type="bullet">
    /// <item><c>config/prosequor/skills/*.json</c> — one <see cref="SkillDefJson"/> object per file
    /// (optional embedded <c>xpRules</c>).</item>
    /// <item><c>config/prosequor/contributions/*.json</c> — JSON arrays of <see cref="SkillContributionJson"/>;
    /// each entry may disable a whole skill (<c>"disable": "all"</c>), disable nodes, merge
    /// <c>xpRules</c>, replace nodes (<c>replaces</c>), and append new nodes.
    /// Unmet <c>dependsOn</c> skips the entry. Contributed node ids must be
    /// <c>assetDomain:localId</c>.</item>
    /// </list>
    /// Duplicate skill ids last-win with a warning. Disabled skills are not registered.
    /// </summary>
    public void LoadFromAssets(
        ICoreAPI api,
        IHookRegistry hooks,
        IAbilityActionRegistry actions,
        CollectionIndex collections,
        IAttributeStatRegistry? stats = null)
    {
        Dictionary<string, SkillDefJson> drafts = new(StringComparer.OrdinalIgnoreCase);
        List<KeyValuePair<AssetLocation, SkillDefJson>> skillAssets = api.Assets
            .GetMany<SkillDefJson>(api.Logger, "config/prosequor/skills/", null)
            .OrderBy(kv => kv.Key.Domain, StringComparer.OrdinalIgnoreCase)
            .ThenBy(kv => kv.Key.Path, StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (KeyValuePair<AssetLocation, SkillDefJson> kv in skillAssets)
        {
            SkillDefJson row = kv.Value;
            if (row == null || string.IsNullOrWhiteSpace(row.id))
            {
                api.Logger.Warning(
                    "[prosequor] Skipping skill asset {0}: missing id.",
                    kv.Key);
                continue;
            }

            string skillId = row.id.Trim();
            row.id = skillId;
            if (drafts.ContainsKey(skillId))
            {
                api.Logger.Warning(
                    "[prosequor] Skill '{0}' redefined by {1}; last definition wins.",
                    skillId,
                    kv.Key);
            }

            drafts[skillId] = row;
        }

        List<SkillContributionMerger.PendingEntry> pendingEntries = new();
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

            string domain = kv.Key.Domain;
            string path = kv.Key.Path;
            foreach (SkillContributionJson entry in entries)
            {
                if (entry == null)
                {
                    continue;
                }

                if (!ContributionDependsOn.IsSatisfied(entry.dependsOn, loadedModIds))
                {
                    api.Logger.VerboseDebug(
                        "[prosequor] Skipping contribution entry in {0}: unmet dependsOn ({1})",
                        kv.Key,
                        ContributionDependsOn.Format(entry.dependsOn));
                    continue;
                }

                // levelUps-only grafts are handled by LevelUpRegistry; ignore here.
                bool hasLevelUps = entry.levelUps != null;
                if (string.IsNullOrWhiteSpace(entry.skill))
                {
                    if (!hasLevelUps)
                    {
                        api.Logger.Warning(
                            "[prosequor] Skipping contribution entry in {0}: missing skill.",
                            kv.Key);
                    }

                    continue;
                }

                if (!entry.TryParseDisable(out bool disableAll, out string[] nodeIds, out string? disableError))
                {
                    api.Logger.Warning(
                        "[prosequor] Skipping contribution entry in {0} for skill '{1}': {2}",
                        kv.Key,
                        entry.skill,
                        disableError);
                    continue;
                }

                string skillId = entry.skill.Trim();
                bool hasNodes = entry.nodes is { Length: > 0 };
                bool hasXpRules = entry.xpRules is { Length: > 0 };
                if (!disableAll && nodeIds.Length == 0 && !hasNodes && !hasXpRules)
                {
                    continue;
                }

                pendingEntries.Add(new SkillContributionMerger.PendingEntry
                {
                    SourceDomain = domain,
                    SourcePath = path,
                    SkillId = skillId,
                    DisableAll = disableAll,
                    Disable = nodeIds,
                    Nodes = entry.nodes?
                        .Where(node => node != null)
                        .ToArray() ?? Array.Empty<SkillTreeNodeJson>(),
                    XpRules = entry.xpRules?
                        .Where(rule => rule != null)
                        .ToArray() ?? Array.Empty<XpRuleJson>()
                });
            }
        }

        SkillContributionMerger.Apply(drafts, pendingEntries, msg => api.Logger.Warning(msg));

        int sourceOrder = 0;
        foreach (SkillDefJson row in drafts.Values
                     .OrderBy(r => r.id, StringComparer.OrdinalIgnoreCase))
        {
            CompileAndRegister(api, hooks, actions, collections, row, ref sourceOrder, stats);
        }

        MenuIndex = SkillMenuIndex.Build(ordered);
        api.Logger.Notification("[prosequor] Registered {0} skill(s).", ordered.Count);
    }

    void CompileAndRegister(
        ICoreAPI api,
        IHookRegistry hooks,
        IAbilityActionRegistry actions,
        CollectionIndex collections,
        SkillDefJson row,
        ref int sourceOrder,
        IAttributeStatRegistry? stats)
    {
        string skillId = row.id.Trim();
        List<string> rootErrors = new();
        List<AbilityRule>? rootRules = AbilityRuleCompiler.CompileEffects(
            skillId,
            nodeId: null,
            tier: null,
            row.effects,
            hooks,
            actions,
            collections,
            rootErrors,
            ref sourceOrder);

        List<string> attributeScoreErrors = new();
        IReadOnlyList<AttributeScoreEntry> attributeScores = AttributeScoreCompiler.Compile(
            skillId,
            row.attributeScores,
            attributeScoreErrors,
            stats);

        SkillKind kind = SkillKindPolicy.ClassifyDraft(row);
        int maxLevel = SkillKindPolicy.MaxLevelFor(kind);
        if (row.maxLevel is int authoredMax && authoredMax > 0)
        {
            api.Logger.Warning(
                "[prosequor] Skill '{0}' authored maxLevel {1} is ignored; cap is {2} from kind {3}.",
                skillId,
                authoredMax,
                maxLevel,
                kind);
        }

        SkillDef def = new()
        {
            Id = skillId,
            NameLang = string.IsNullOrWhiteSpace(row.nameLang)
                ? SkillTreeCompiler.DefaultSkillNameLang(skillId)
                : row.nameLang,
            DescriptionLang = row.descriptionLang?.Trim() ?? "",
            Icon = row.icon?.Trim() ?? "",
            Kind = kind,
            MaxLevel = maxLevel,
            IsOptional = row.optional,
            Rules = rootRules ?? new List<AbilityRule>(),
            RootEffectSnapshots = SkillDescriptionResolver.SnapshotEffects(row.effects),
            XpRules = XpRuleCompiler.CompileAll(
                row.xpRules,
                skillId,
                collections,
                ref sourceOrder,
                msg => api.Logger.Warning(msg)),
            AttributeScores = attributeScores
        };

        foreach (string error in rootErrors)
        {
            api.Logger.Error("[prosequor] {0}", error);
        }

        foreach (string error in attributeScoreErrors)
        {
            api.Logger.Error("{0}", error);
        }

        SkillTreeJson? treeJson = row.tree;
        if (kind == SkillKind.Hobby)
        {
            treeJson = SkillKindPolicy.StripSpecializationFlags(row.tree);
        }

        SkillTreeCompileRepair.RepairResult treeRepair = SkillTreeCompileRepair.CompileWithOrphanRepair(
            def.Id,
            def.MaxLevel,
            treeJson,
            row.effects ?? Array.Empty<AbilityEffectJson>(),
            hooks,
            actions,
            collections,
            ref sourceOrder);
        SkillTreeCompiler.CompileResult treeResult = treeRepair.Compile;
        foreach (string warning in treeResult.Warnings)
        {
            api.Logger.Warning("[prosequor] {0}", warning);
        }

        foreach ((string nodeId, string cause) in treeRepair.Orphans)
        {
            api.Logger.Warning(
                "[prosequor] Skill '{0}' orphaned node '{1}' due to: {2}",
                def.Id,
                nodeId,
                cause);
        }

        if (treeResult.Errors.Count > 0 || rootRules == null)
        {
            foreach (string error in treeResult.Errors)
            {
                api.Logger.Error("[prosequor] {0}", error);
            }

            if (treeResult.Errors.Count > 0)
            {
                api.Logger.Error(
                    "[prosequor] Skill '{0}' kept without a tree due to validation errors.",
                    def.Id);
                def.Tree = null;
            }
        }
        else
        {
            def.Tree = treeResult.Tree;
            if (def.Tree != null)
            {
                foreach (SkillTreeNodeDef node in def.Tree.Nodes)
                {
                    for (int t = 0; t < node.Tiers.Count; t++)
                    {
                        def.Rules.AddRange(node.Tiers[t].Rules);
                    }
                }

                if (treeRepair.Orphans.Count > 0)
                {
                    api.Logger.Notification(
                        "[prosequor] Skill '{0}' tree compiled ({1} node(s), {2} rule(s)) after orphaning {3} node(s).",
                        def.Id,
                        def.Tree.Nodes.Count,
                        def.Rules.Count,
                        treeRepair.Orphans.Count);
                }
                else
                {
                    api.Logger.Notification(
                        "[prosequor] Skill '{0}' tree compiled ({1} node(s), {2} rule(s)).",
                        def.Id,
                        def.Tree.Nodes.Count,
                        def.Rules.Count);
                }
            }
        }

        JToken[] skillDescParams = row.descriptionParams ?? [];
        if (skillDescParams.Length > 0)
        {
            List<string> descErrors = new();
            if (SkillDescriptionResolver.TryValidate(
                    def.Id,
                    skillDescParams,
                    def.RootEffectSnapshots,
                    def.Tree,
                    descErrors))
            {
                def.DescriptionParamSpecs = skillDescParams;
            }
            else
            {
                foreach (string error in descErrors)
                {
                    api.Logger.Error("[prosequor] {0}", error);
                }
            }
        }

        Register(def);
    }

    public static string DisplayName(SkillDef def)
    {
        return DisplayName(def, null);
    }

    public static string DisplayName(SkillDef def, string? languageCode)
    {
        string key = def.NameLang;
        if (string.IsNullOrWhiteSpace(key))
        {
            return def.Id;
        }

        if (!key.Contains(':'))
        {
            key = "prosequor:" + key;
        }

        string translated = string.IsNullOrWhiteSpace(languageCode)
            ? Lang.Get(key)
            : Lang.GetL(languageCode, key);
        return string.IsNullOrEmpty(translated) || string.Equals(translated, key, StringComparison.Ordinal)
            ? def.Id
            : translated;
    }

    public static string Description(SkillDef def, string? languageCode = null) =>
        Description(def, progress: null, languageCode);

    /// <summary>
    /// List-hover description with optional progress-aware descriptionParams resolve.
    /// </summary>
    public static string Description(
        SkillDef def,
        IPlayerProgress? progress,
        string? languageCode = null)
    {
        string key = def.DescriptionLang;
        if (string.IsNullOrWhiteSpace(key))
        {
            return "";
        }

        if (!key.Contains(':'))
        {
            key = "prosequor:" + key;
        }

        IReadOnlyList<object> args = SkillDescriptionResolver.Resolve(def, progress);
        string translated;
        if (args.Count == 0)
        {
            translated = string.IsNullOrWhiteSpace(languageCode)
                ? Lang.Get(key)
                : Lang.GetL(languageCode, key);
        }
        else if (string.IsNullOrWhiteSpace(languageCode))
        {
            translated = SkillDescriptionRender.RenderLang(def.DescriptionLang, args, vtml: false);
        }
        else
        {
            object[] plainArgs = new object[args.Count];
            for (int i = 0; i < args.Count; i++)
            {
                plainArgs[i] = args[i] is FormattedDescriptionArg formatted
                    ? SkillDescriptionRender.FormatPercentPlain(formatted)
                    : args[i];
            }

            translated = Lang.GetL(languageCode, key, plainArgs);
        }

        return string.IsNullOrEmpty(translated) || string.Equals(translated, key, StringComparison.Ordinal)
            ? ""
            : translated;
    }

    public static AssetLocation? IconLocation(SkillDef def)
    {
        if (string.IsNullOrWhiteSpace(def.Icon))
        {
            return null;
        }

        string path = def.Icon.Trim().Replace('\\', '/').TrimStart('/');
        if (path.Contains(':'))
        {
            return new AssetLocation(path);
        }

        return new AssetLocation("prosequor", path);
    }
}
