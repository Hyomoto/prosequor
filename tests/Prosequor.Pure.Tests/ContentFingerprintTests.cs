using Newtonsoft.Json.Linq;
using Prosequor.Ability;
using Prosequor.Ability.Hooks;
using Prosequor.Data;
using Prosequor.Xp;
using Xunit;

namespace Prosequor.Pure.Tests;

public class ContentFingerprintTests
{
    [Fact]
    [Trait("Layer", "Data")]
    public void SameTrees_InDifferentRegisterOrder_HashEqual()
    {
        SkillDef cheap = NodeSkill("digging", "root", cost: 1);
        SkillDef other = NodeSkill("fishing", "cast", cost: 2);

        SkillRegistry a = new();
        a.Register(cheap);
        a.Register(other);

        SkillRegistry b = new();
        b.Register(other);
        b.Register(cheap);

        Assert.Equal(ContentFingerprint.Compute(a).Hash, ContentFingerprint.Compute(b).Hash);
    }

    [Fact]
    [Trait("Layer", "Data")]
    public void DifferentNodeCost_ChangesHash()
    {
        SkillRegistry a = new();
        a.Register(NodeSkill("digging", "root", cost: 1));
        SkillRegistry b = new();
        b.Register(NodeSkill("digging", "root", cost: 2));

        Assert.NotEqual(ContentFingerprint.Compute(a).Hash, ContentFingerprint.Compute(b).Hash);
    }

    [Fact]
    [Trait("Layer", "Data")]
    public void DifferentNumberParam_ChangesHash()
    {
        ContentFingerprint slow = ContentFingerprint.Compute(CompileSpeedSkill(0.001f));
        ContentFingerprint fast = ContentFingerprint.Compute(CompileSpeedSkill(0.05f));

        Assert.NotEqual(slow.Hash, fast.Hash);
    }

    [Fact]
    [Trait("Layer", "Data")]
    public void CollectionIncludeChange_ChangesHash()
    {
        CollectionIndex clay = new();
        clay.EnsureKey("stone");
        clay.AddIncludePattern("stone", "game:rock-*");

        CollectionIndex ore = new();
        ore.EnsureKey("stone");
        ore.AddIncludePattern("stone", "game:ore-*");

        SkillRegistry skills = new();
        Assert.NotEqual(
            ContentFingerprint.Compute(skills, collections: clay).Hash,
            ContentFingerprint.Compute(skills, collections: ore).Hash);
    }

    [Fact]
    [Trait("Layer", "Data")]
    public void Compare_MatchAndMismatch()
    {
        SkillRegistry skills = new();
        skills.Register(NodeSkill("digging", "root", cost: 1));
        ContentFingerprint local = ContentFingerprint.Compute(skills);

        ContentFingerprintReport ok = ContentFingerprint.Compare(local, local.Version, local.Hash);
        ContentFingerprintReport bad = ContentFingerprint.Compare(local, local.Version, "deadbeef");
        ContentFingerprintReport schema = ContentFingerprint.Compare(local, local.Version + 1, local.Hash);

        Assert.True(ok.Match);
        Assert.False(bad.Match);
        Assert.False(schema.Match);
        Assert.Equal("deadbeef", ContentFingerprint.Abbreviate("deadbeef", 16));
        Assert.Equal("0123456789abcdef", ContentFingerprint.Abbreviate("0123456789abcdef9999", 16));
    }

    [Fact]
    [Trait("Layer", "Data")]
    public void CanonicalText_UsesUnixNewlines()
    {
        SkillRegistry skills = new();
        skills.Register(NodeSkill("digging", "root", cost: 1));
        string text = ContentFingerprint.BuildCanonical(skills, null, null, null, null, null);
        Assert.DoesNotContain("\r", text);
        Assert.StartsWith("v1\n", text);
        Assert.Equal(ContentFingerprint.HashText(text), ContentFingerprint.Compute(skills).Hash);
    }

    static SkillDef NodeSkill(string skillId, string nodeId, int cost)
    {
        SkillTreeNodeDef node = new()
        {
            Id = nodeId,
            Tiers = [new SkillTreeTierDef { Cost = cost }]
        };
        return new SkillDef
        {
            Id = skillId,
            Kind = SkillKind.Minor,
            MaxLevel = XpCurves.MinorMaxLevel,
            Tree = new SkillTreeDef
            {
                Nodes = [node],
                ById = new Dictionary<string, SkillTreeNodeDef>(StringComparer.OrdinalIgnoreCase)
                {
                    [nodeId] = node
                }
            }
        };
    }

    static SkillRegistry CompileSpeedSkill(float perSkillLevel)
    {
        HookRegistry hooks = new();
        AbilityActionRegistry actions = new(hooks);
        AbilityBootstrap.RegisterBuiltIns(hooks, actions);
        CollectionIndex collections = new();
        collections.EnsureKey("stone");
        collections.EnsureKey("pickaxe");

        int sourceOrder = 0;
        List<string> errors = new();
        List<AbilityRule>? rules = AbilityRuleCompiler.CompileEffects(
            "mining",
            nodeId: null,
            tier: null,
            [
                new AbilityEffectJson
                {
                    hook = "prosequor:block-interaction",
                    verb = "prosequor:interaction-speed",
                    action = "prosequor:number",
                    when = new AbilityWhenJson { tags = ["target:<stone>", "held:<pickaxe>"] },
                    @params = new JObject
                    {
                        ["op"] = "scale",
                        ["base"] = 0,
                        ["perSkillLevel"] = perSkillLevel
                    }
                }
            ],
            hooks,
            actions,
            collections,
            errors,
            ref sourceOrder);

        Assert.True(rules != null && errors.Count == 0, string.Join("; ", errors));

        SkillRegistry registry = new();
        registry.Register(new SkillDef
        {
            Id = "mining",
            Kind = SkillKind.Specialization,
            MaxLevel = XpCurves.SkillMaxLevel,
            Rules = rules!
        });
        return registry;
    }
}
