using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Prosequor.Ability;
using Prosequor.Ability.Hooks;
using Prosequor.Data;
using Prosequor.Xp;
using Xunit;

namespace Prosequor.Pure.Tests;

/// <summary>
/// Compiles every shipped skill JSON the same way runtime <see cref="SkillRegistry.LoadFromAssets"/> does.
/// Failures include the compiler/orphan messages so layout and authoring mistakes surface before boot.
/// </summary>
public class ShippedSkillCompileTests
{
    [Fact]
    [Trait("Layer", "Content")]
    [Trait("Kind", "SkillCompile")]
    public void AllShippedSkills_Should_CompileWithoutErrorsOrOrphans()
    {
        string repoRoot = FindRepoRoot();
        string skillsDir = Path.Combine(repoRoot, "assets", "prosequor", "config", "prosequor", "skills");
        Assert.True(Directory.Exists(skillsDir), $"Missing skills directory: {skillsDir}");

        HookRegistry hooks = new();
        AbilityActionRegistry actions = new(hooks);
        AffixListRegistry affixLists = new();
        affixLists.LoadFromConfigDirectory(
            Path.Combine(repoRoot, "assets", "prosequor", "config", "prosequor"),
            assetDomain: "prosequor");
        AbilityBootstrap.RegisterBuiltIns(hooks, actions, affixLists);
        CollectionIndex collections = BuildCollections(repoRoot);

        List<string> failures = new();
        string[] files = Directory.GetFiles(skillsDir, "*.json", SearchOption.TopDirectoryOnly)
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        Assert.NotEmpty(files);

        int sourceOrder = 0;
        foreach (string path in files)
        {
            SkillDefJson? row;
            try
            {
                row = JsonConvert.DeserializeObject<SkillDefJson>(File.ReadAllText(path));
            }
            catch (Exception ex)
            {
                failures.Add($"{Path.GetFileName(path)}: JSON parse failed: {ex.Message}");
                continue;
            }

            if (row == null || string.IsNullOrWhiteSpace(row.id))
            {
                failures.Add($"{Path.GetFileName(path)}: missing skill id.");
                continue;
            }

            string? report = CompileSkill(row, hooks, actions, collections, ref sourceOrder);
            if (report != null)
            {
                failures.Add(report);
            }
        }

        if (failures.Count > 0)
        {
            Assert.Fail(
                $"[prosequor] Shipped skill compile failed ({failures.Count} skill(s)):\n\n"
                + string.Join("\n\n", failures));
        }
    }

    static string? CompileSkill(
        SkillDefJson row,
        IHookRegistry hooks,
        IAbilityActionRegistry actions,
        CollectionIndex collections,
        ref int sourceOrder)
    {
        string skillId = row.id.Trim();
        List<string> problems = new();

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
        problems.AddRange(rootErrors.Select(e => $"root effect: {e}"));
        if (rootRules == null)
        {
            problems.Add("root effects failed to compile (null rule list).");
        }

        List<string> xpWarnings = new();
        _ = XpRuleCompiler.CompileAll(
            row.xpRules,
            skillId,
            collections,
            ref sourceOrder,
            msg => xpWarnings.Add(msg));
        // XP warnings are still authoring mistakes worth failing the gate on.
        problems.AddRange(xpWarnings.Select(w => $"xp: {w}"));

        List<string> attributeScoreErrors = new();
        _ = AttributeScoreCompiler.Compile(skillId, row.attributeScores, attributeScoreErrors);
        problems.AddRange(attributeScoreErrors.Select(e => $"attributeScores: {e}"));

        SkillKind kind = SkillKindPolicy.ClassifyDraft(row);
        int maxLevel = SkillKindPolicy.MaxLevelFor(kind);
        SkillTreeJson? treeJson = row.tree;
        if (kind == SkillKind.Hobby)
        {
            treeJson = SkillKindPolicy.StripSpecializationFlags(row.tree);
        }

        SkillTreeCompileRepair.RepairResult treeRepair = SkillTreeCompileRepair.CompileWithOrphanRepair(
            skillId,
            maxLevel,
            treeJson,
            row.effects ?? Array.Empty<AbilityEffectJson>(),
            hooks,
            actions,
            collections,
            ref sourceOrder);

        SkillTreeCompiler.CompileResult tree = treeRepair.Compile;
        problems.AddRange(tree.Errors.Select(e => $"tree: {e}"));
        problems.AddRange(tree.Warnings.Select(w => $"tree warning: {w}"));
        foreach ((string nodeId, string cause) in treeRepair.Orphans)
        {
            problems.Add($"orphaned node '{nodeId}': {cause}");
        }

        bool authoredTree = row.tree?.nodes is { Length: > 0 };
        if (authoredTree && !tree.Success)
        {
            problems.Add("tree compile did not succeed.");
        }

        if (authoredTree && tree.Tree == null)
        {
            problems.Add("authored tree produced no compiled tree (runtime would keep skill without a tree).");
        }

        AbilityEffectJson[] rootSnapshots = SkillDescriptionResolver.SnapshotEffects(row.effects);
        JToken[] skillDescParams = row.descriptionParams ?? [];
        if (skillDescParams.Length > 0)
        {
            List<string> descErrors = new();
            if (!SkillDescriptionResolver.TryValidate(
                    skillId,
                    skillDescParams,
                    rootSnapshots,
                    tree.Tree,
                    descErrors))
            {
                problems.AddRange(descErrors.Select(e => $"skilldesc: {e}"));
            }
        }

        if (problems.Count == 0)
        {
            return null;
        }

        StringBuilder sb = new();
        sb.Append("skill '").Append(skillId).Append("' (").Append(problems.Count).Append(" issue(s)):");
        foreach (string problem in problems)
        {
            sb.Append("\n  - ").Append(problem);
        }

        return sb.ToString();
    }

    static CollectionIndex BuildCollections(string repoRoot)
    {
        CollectionRegistry registry = new();
        registry.EnsureBuiltinKeys();

        string collectionsPath = Path.Combine(
            repoRoot,
            "assets",
            "prosequor",
            "config",
            "prosequor",
            "collections.json");
        if (File.Exists(collectionsPath))
        {
            CollectionJson[]? rows = JsonConvert.DeserializeObject<CollectionJson[]>(
                File.ReadAllText(collectionsPath));
            if (rows != null)
            {
                foreach (CollectionJson row in rows)
                {
                    if (string.IsNullOrWhiteSpace(row.id))
                    {
                        continue;
                    }

                    string id = row.id.Trim();
                    registry.Index.EnsureKey(id);
                    if (row.unions is { Length: > 0 })
                    {
                        registry.Index.AddUnion(id, row.unions);
                    }
                }
            }
        }

        string poolsPath = Path.Combine(
            repoRoot,
            "assets",
            "prosequor",
            "config",
            "prosequor",
            "pools.json");
        if (File.Exists(poolsPath))
        {
            JArray? pools = JsonConvert.DeserializeObject<JArray>(File.ReadAllText(poolsPath));
            if (pools != null)
            {
                foreach (JToken entry in pools)
                {
                    string? id = entry.Value<string>("id")?.Trim();
                    if (!string.IsNullOrWhiteSpace(id))
                    {
                        registry.Index.EnsureKey(id);
                    }
                }
            }
        }

        return registry.Index;
    }

    static string FindRepoRoot()
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir != null)
        {
            string probe = Path.Combine(dir.FullName, "assets", "prosequor", "config", "prosequor", "skills");
            if (Directory.Exists(probe))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        Assert.Fail(
            "[prosequor] Could not locate repo root from "
            + AppContext.BaseDirectory
            + " (expected assets/prosequor/config/prosequor/skills).");
        return "";
    }
}
