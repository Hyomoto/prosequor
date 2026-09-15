using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Prosequor.Ability;
using Prosequor.Ability.Hooks;
using Prosequor.Data;
using Vintagestory.API.Common;
using Xunit;

namespace Prosequor.Pure.Tests;

/// <summary>
/// Strong Spirits / Prized Liquors: mutator Accepts + shipped cooking-node compile.
/// </summary>
public static class LiquorQualityStampFixtures
{
    public static void VerifyAll()
    {
        VerifyMutatorsRejectNonPortions();
        VerifyCookingNodesCompile();
    }

    static void VerifyMutatorsRejectNonPortions()
    {
        if (!AbilityBootstrap.AttributeMutators.TryGet(
                IntoxicationAttributeMutator.KeyName,
                out ICraftAttributeMutator? intox)
            || intox == null
            || !AbilityBootstrap.AttributeMutators.TryGet(
                PriceAttributeMutator.KeyName,
                out ICraftAttributeMutator? price)
            || price == null)
        {
            Assert.Fail("[prosequor] Liquor quality mutators must be registered.");
            return;
        }

        ItemStack empty = new();
        if (intox.Accepts(empty) || price.Accepts(empty))
        {
            Assert.Fail("[prosequor] Liquor mutators must reject non-portion stacks.");
        }
    }

    static void VerifyCookingNodesCompile()
    {
        string repoRoot = FindRepoRoot();
        string cookingPath = Path.Combine(
            repoRoot,
            "assets",
            "prosequor",
            "config",
            "prosequor",
            "skills",
            "cooking.json");
        Assert.True(File.Exists(cookingPath), "Missing cooking.json: " + cookingPath);

        SkillDefJson? skill = JsonConvert.DeserializeObject<SkillDefJson>(File.ReadAllText(cookingPath));
        Assert.NotNull(skill);
        Assert.NotNull(skill.tree?.nodes);

        HookRegistry hooks = new();
        AbilityActionRegistry actions = new(hooks);
        AffixListRegistry affixLists = new();
        affixLists.LoadFromConfigDirectory(
            Path.Combine(repoRoot, "assets", "prosequor", "config", "prosequor"),
            assetDomain: "prosequor");
        AbilityBootstrap.RegisterBuiltIns(hooks, actions, affixLists);

        CollectionIndex collections = new();
        collections.EnsureKey("distilled");

        VerifyNodeEffects(
            skill,
            "strong-spirits",
            hooks,
            actions,
            collections,
            expectedKey: "intoxication",
            expectedAffixes: "prosequor:intoxication");
        VerifyNodeEffects(
            skill,
            "prized-liquors",
            hooks,
            actions,
            collections,
            expectedKey: "price",
            expectedAffixes: "prosequor:liquor-stock");
    }

    static void VerifyNodeEffects(
        SkillDefJson skill,
        string nodeId,
        IHookRegistry hooks,
        IAbilityActionRegistry actions,
        CollectionIndex collections,
        string expectedKey,
        string expectedAffixes)
    {
        SkillTreeNodeJson? node = skill.tree!.nodes!
            .FirstOrDefault(n => string.Equals(n.id, nodeId, StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(node);
        Assert.NotNull(node.tiers);
        Assert.True(node.tiers.Length >= 2, nodeId + " should have two tiers.");

        AbilityEffectJson[]? r1 = node.tiers[0].effects;
        Assert.NotNull(r1);
        Assert.True(r1.Length >= 1, nodeId + " R1 should have a quality effect.");

        JObject? paramsObj = r1[0].@params;
        Assert.NotNull(paramsObj);
        Assert.Equal(expectedKey, paramsObj.Value<string>("key"));
        Assert.Equal(expectedAffixes, paramsObj.Value<string>("affixes"));

        int order = 0;
        List<string> errors = new();
        List<AbilityRule>? rules = AbilityRuleCompiler.CompileEffects(
            "cooking",
            nodeId,
            tier: 1,
            r1,
            hooks,
            actions,
            collections,
            errors,
            ref order);
        if (rules == null || rules.Count == 0 || errors.Count > 0)
        {
            Assert.Fail(string.Format(
                "[prosequor] {0} R1 failed to compile: {1}",
                nodeId,
                string.Join("; ", errors)));
            return;
        }

        AbilityEffectJson[]? r2 = node.tiers[1].effects;
        Assert.NotNull(r2);
        Assert.True(r2.Length >= 1, nodeId + " R2 should replicate the quality effect.");

        JObject? r2Params = r2[0].@params;
        Assert.NotNull(r2Params);
        Assert.Equal(0, r2[0].replicate);
        JArray? table = r2Params["table"] as JArray;
        Assert.NotNull(table);
        Assert.Equal(2, table.Count);
        Assert.Equal(0.1, table[0]!.Value<double>(), 3);
        Assert.Equal(0.33, table[1]!.Value<double>(), 3);
        JArray? range = r2Params["affixRange"] as JArray;
        Assert.NotNull(range);
        Assert.Equal(1, range[0]!.Value<int>());
        Assert.Equal(4, range[1]!.Value<int>());
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

        Assert.Fail("[prosequor] Could not locate repo root for cooking.json.");
        return "";
    }
}
