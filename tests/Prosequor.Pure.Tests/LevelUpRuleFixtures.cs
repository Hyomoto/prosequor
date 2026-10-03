using Prosequor.Data;
using Xunit;

namespace Prosequor.Progress;

/// <summary>Compiled level-up rule matching and attribute grants.</summary>
public static class LevelUpRuleFixtures
{
    public static void VerifyAll()
    {
        VerifyRetiredGrantsDoNotPay();
        VerifyExplicitAttributeLevels();
        VerifySkippingLevelsAppliesAll();
        VerifyNamedAttributeGrant();
        VerifyBucketsGrant();
        VerifyContributionDisableReplaceAdd();
        VerifyInvalidRowsRejected();
    }

    /// <summary>Attribute schedule used by fixtures. Skill points and slots are not level-up rules.</summary>
    public static IReadOnlyList<LevelUpRuleDef> DefaultRules()
    {
        Dictionary<string, LevelUpRuleJson> drafts = new(StringComparer.OrdinalIgnoreCase)
        {
            ["prosequor:earn-attribute"] = new LevelUpRuleJson
            {
                id = "prosequor:earn-attribute",
                levels = [10, 20, 28, 35, 40, 44, 47, 50],
                action = "prosequor:earn-attribute",
                @params = new Newtonsoft.Json.Linq.JObject
                {
                    ["key"] = "buckets",
                    ["value"] = 1
                }
            }
        };

        return LevelUpRegistry.CompileDrafts(drafts, _ => { });
    }

    static PlayerProgressState FreshState()
    {
        PlayerProgressState state = new();
        PlayerProgressState.EnsureAttributeEntries(state);
        return state;
    }

    static void VerifyRetiredGrantsDoNotPay()
    {
        Dictionary<string, LevelUpRuleJson> drafts = new(StringComparer.OrdinalIgnoreCase)
        {
            ["prosequor:earn-skill-point"] = new LevelUpRuleJson
            {
                id = "prosequor:earn-skill-point",
                every = 1,
                action = "prosequor:earn-skill-point"
            },
            ["prosequor:earn-specialization-point"] = new LevelUpRuleJson
            {
                id = "prosequor:earn-specialization-point",
                every = 10,
                action = "prosequor:earn-specialization-point"
            }
        };
        List<string> warnings = new();
        IReadOnlyList<LevelUpRuleDef> rules = LevelUpRegistry.CompileDrafts(
            drafts,
            _ => { },
            warnings.Add);
        PlayerProgressState state = FreshState();
        LevelUpRules.Apply(state, rules, beforeLevel: 1, afterLevel: 50, new Random(1));
        if (rules.Count != 0 || warnings.Count != 2 || state.UnlockPoints != 0)
        {
            Assert.Fail(
                $"[prosequor] Level-up fixture failed (retired grants; rules={rules.Count} warnings={warnings.Count} points={state.UnlockPoints}).");
        }
    }

    static void VerifyExplicitAttributeLevels()
    {
        PlayerProgressState state = FreshState();
        foreach (string id in AttributeIds.All)
        {
            state.Attributes[id] = 0;
        }

        // Lead by one growth point so Resilience always wins the bucket grant.
        state.Attributes[AttributeIds.Resilience] = 1;
        IReadOnlyList<string> winners = LevelUpRules.Apply(
            state,
            DefaultRules(),
            beforeLevel: 9,
            afterLevel: 10,
            new Random(1));
        if (winners.Count != 1
            || winners[0] != AttributeIds.Resilience
            || state.Attributes[AttributeIds.Resilience] != 2
            || state.UnlockPoints != 0)
        {
            Assert.Fail("[prosequor] Level-up fixture failed (explicit attribute at level 10).");
        }
    }

    static void VerifySkippingLevelsAppliesAll()
    {
        PlayerProgressState state = FreshState();
        foreach (string id in AttributeIds.All)
        {
            // Keep others below Resilience so every bucket grant sticks on Resilience
            // without hitting MaxScore before all 8 schedule levels fire.
            state.Attributes[id] = -1;
        }

        state.Attributes[AttributeIds.Resilience] = 0;
        state.UnlockPoints = 0;
        IReadOnlyList<string> winners = LevelUpRules.Apply(
            state,
            DefaultRules(),
            beforeLevel: 1,
            afterLevel: 50,
            new Random(11));
        const int attributeGrants = 8;
        if (winners.Count != attributeGrants
            || state.Attributes[AttributeIds.Resilience] != attributeGrants
            || state.UnlockPoints != 0)
        {
            Assert.Fail(string.Format(
                "[prosequor] Level-up fixture failed (1→50; resilience={0} winners={1} points={2}).",
                state.Attributes[AttributeIds.Resilience],
                winners.Count,
                state.UnlockPoints));
        }
    }

    static void VerifyNamedAttributeGrant()
    {
        LevelUpRuleJson row = new()
        {
            id = "test:str",
            levels = [5],
            action = "prosequor:earn-attribute",
            @params = new Newtonsoft.Json.Linq.JObject
            {
                ["key"] = "strength",
                ["value"] = 2
            }
        };
        Dictionary<string, LevelUpRuleJson> drafts = new(StringComparer.OrdinalIgnoreCase)
        {
            [row.id!] = row
        };
        IReadOnlyList<LevelUpRuleDef> rules = LevelUpRegistry.CompileDrafts(drafts, _ => { });
        PlayerProgressState state = FreshState();
        LevelUpRules.Apply(state, rules, beforeLevel: 4, afterLevel: 5, new Random(1));
        if (state.Attributes[AttributeIds.Strength] != 2
            || state.AttributeBuckets[AttributeIds.Strength] != 0f)
        {
            Assert.Fail("[prosequor] Level-up fixture failed (named attribute grant).");
        }
    }

    static void VerifyBucketsGrant()
    {
        LevelUpRuleJson row = new()
        {
            id = "test:buckets",
            levels = [10],
            action = "prosequor:earn-attribute",
            @params = new Newtonsoft.Json.Linq.JObject
            {
                ["key"] = "buckets",
                ["value"] = 1
            }
        };
        IReadOnlyList<LevelUpRuleDef> rules = LevelUpRegistry.CompileDrafts(
            new Dictionary<string, LevelUpRuleJson>(StringComparer.OrdinalIgnoreCase) { [row.id!] = row },
            _ => { });
        PlayerProgressState state = FreshState();
        state.AttributeBuckets[AttributeIds.Perception] = 12f;
        state.AttributeBuckets[AttributeIds.Strength] = 3f;
        IReadOnlyList<string> winners = LevelUpRules.Apply(
            state,
            rules,
            beforeLevel: 9,
            afterLevel: 10,
            new Random(1));
        if (winners.Count != 1
            || winners[0] != AttributeIds.Perception
            || state.AttributeBuckets[AttributeIds.Perception] != 0f
            || state.AttributeBuckets[AttributeIds.Strength] != 3f)
        {
            Assert.Fail("[prosequor] Level-up fixture failed (buckets grant).");
        }
    }

    static void VerifyContributionDisableReplaceAdd()
    {
        Dictionary<string, LevelUpRuleJson> drafts = new(StringComparer.OrdinalIgnoreCase)
        {
            ["prosequor:earn-skill-point"] = new LevelUpRuleJson
            {
                id = "prosequor:earn-skill-point",
                every = 1,
                action = "prosequor:earn-skill-point"
            },
            ["prosequor:earn-specialization-point"] = new LevelUpRuleJson
            {
                id = "prosequor:earn-specialization-point",
                every = 10,
                action = "prosequor:earn-specialization-point"
            }
        };

        drafts.Remove("prosequor:earn-specialization-point");
        drafts["prosequor:earn-skill-point"] = new LevelUpRuleJson
        {
            id = "prosequor:earn-skill-point",
            every = 2,
            action = "prosequor:earn-skill-point",
            @params = new Newtonsoft.Json.Linq.JObject { ["value"] = 2 }
        };
        drafts["mymod:strength-at-5"] = new LevelUpRuleJson
        {
            id = "mymod:strength-at-5",
            levels = [5],
            action = "prosequor:earn-attribute",
            @params = new Newtonsoft.Json.Linq.JObject
            {
                ["key"] = "strength",
                ["value"] = 1
            }
        };

        List<string> warnings = new();
        IReadOnlyList<LevelUpRuleDef> rules = LevelUpRegistry.CompileDrafts(drafts, _ => { }, warnings.Add);
        if (rules.Count != 1 || warnings.Count != 1)
        {
            Assert.Fail(
                $"[prosequor] Level-up fixture failed (retired skill-point rule; rules={rules.Count} warnings={warnings.Count}).");
        }

        PlayerProgressState state = FreshState();
        LevelUpRules.Apply(state, rules, beforeLevel: 1, afterLevel: 5, new Random(1));
        if (state.UnlockPoints != 0
            || state.Attributes[AttributeIds.Strength] != 1)
        {
            Assert.Fail(string.Format(
                "[prosequor] Level-up fixture failed (contrib replace/add; points={0} strength={1}).",
                state.UnlockPoints,
                state.Attributes[AttributeIds.Strength]));
        }
    }

    static void VerifyInvalidRowsRejected()
    {
        List<string> errors = new();
        int order = 0;
        LevelUpRuleDef? both = LevelUpRuleCompiler.Compile(
            new LevelUpRuleJson
            {
                id = "bad:both",
                every = 1,
                levels = [10],
                action = "prosequor:earn-skill-point"
            },
            ref order,
            errors);
        if (both != null || errors.Count == 0)
        {
            Assert.Fail("[prosequor] Level-up fixture failed (every+levels should reject).");
        }

        errors.Clear();
        order = 0;
        LevelUpRuleDef? noKey = LevelUpRuleCompiler.Compile(
            new LevelUpRuleJson
            {
                id = "bad:attr",
                levels = [10],
                action = "prosequor:earn-attribute"
            },
            ref order,
            errors);
        if (noKey != null || errors.Count == 0)
        {
            Assert.Fail("[prosequor] Level-up fixture failed (earn-attribute without key should reject).");
        }

        errors.Clear();
        order = 0;
        LevelUpRuleDef? badAttr = LevelUpRuleCompiler.Compile(
            new LevelUpRuleJson
            {
                id = "bad:attr2",
                levels = [10],
                action = "prosequor:earn-attribute",
                @params = new Newtonsoft.Json.Linq.JObject { ["key"] = "luck" }
            },
            ref order,
            errors);
        if (badAttr != null || errors.Count == 0)
        {
            Assert.Fail("[prosequor] Level-up fixture failed (unknown attribute key should reject).");
        }
    }
}
