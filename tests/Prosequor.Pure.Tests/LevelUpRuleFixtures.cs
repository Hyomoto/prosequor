using Prosequor.Data;
using Xunit;

namespace Prosequor.Progress;

/// <summary>Compiled level-up rule matching, grants, and specialization capacity.</summary>
public static class LevelUpRuleFixtures
{
    public static void VerifyAll()
    {
        VerifyEveryOneSkillPoints();
        VerifySpecializationEveryTen();
        VerifyExplicitAttributeLevels();
        VerifySkippingLevelsAppliesAll();
        VerifyNamedAttributeGrant();
        VerifyBucketsGrant();
        VerifyContributionDisableReplaceAdd();
        VerifyInvalidRowsRejected();
    }

    /// <summary>Shipped-equivalent default rules for fixtures that need the stock schedule.</summary>
    public static IReadOnlyList<LevelUpRuleDef> DefaultRules()
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
            },
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

    static void VerifyEveryOneSkillPoints()
    {
        PlayerProgressState state = FreshState();
        state.UnlockPoints = 0;
        LevelUpRules.Apply(state, DefaultRules(), beforeLevel: 1, afterLevel: 4, new Random(1));
        if (state.UnlockPoints != 3)
        {
            Assert.Fail(
                $"[prosequor] Level-up fixture failed (every-1 skill points; got {state.UnlockPoints}, expected 3).");
        }
    }

    static void VerifySpecializationEveryTen()
    {
        IReadOnlyList<LevelUpRuleDef> rules = DefaultRules();
        if (SpecializationPolicy.AllowedSlots(9, rules) != 0
            || SpecializationPolicy.AllowedSlots(10, rules) != 1
            || SpecializationPolicy.AllowedSlots(19, rules) != 1
            || SpecializationPolicy.AllowedSlots(20, rules) != 2
            || SpecializationPolicy.AllowedSlots(50, rules) != 5)
        {
            Assert.Fail("[prosequor] Level-up fixture failed (spec slots 9/10/19/20/50).");
        }
    }

    static void VerifyExplicitAttributeLevels()
    {
        PlayerProgressState state = FreshState();
        foreach (string id in AttributeIds.All)
        {
            if (id != AttributeIds.Resilience)
            {
                state.Attributes[id] = 0;
            }
        }

        state.Attributes[AttributeIds.Resilience] = AttributeGrowth.DefaultScore;
        IReadOnlyList<string> winners = LevelUpRules.Apply(
            state,
            DefaultRules(),
            beforeLevel: 9,
            afterLevel: 10,
            new Random(1));
        if (winners.Count != 1
            || winners[0] != AttributeIds.Resilience
            || state.Attributes[AttributeIds.Resilience] != AttributeGrowth.DefaultScore + 1
            || state.UnlockPoints != 1)
        {
            Assert.Fail("[prosequor] Level-up fixture failed (explicit attribute at level 10).");
        }
    }

    static void VerifySkippingLevelsAppliesAll()
    {
        PlayerProgressState state = FreshState();
        foreach (string id in AttributeIds.All)
        {
            if (id != AttributeIds.Resilience)
            {
                state.Attributes[id] = 0;
            }
        }

        state.Attributes[AttributeIds.Resilience] = AttributeGrowth.DefaultScore;
        state.UnlockPoints = 0;
        IReadOnlyList<string> winners = LevelUpRules.Apply(
            state,
            DefaultRules(),
            beforeLevel: 1,
            afterLevel: 50,
            new Random(11));
        const int attributeGrants = 8;
        int expectedScore = AttributeGrowth.DefaultScore + attributeGrants;
        if (winners.Count != attributeGrants
            || state.Attributes[AttributeIds.Resilience] != expectedScore
            || state.UnlockPoints != 49)
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
        if (state.Attributes[AttributeIds.Strength] != AttributeGrowth.DefaultScore + 2
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

        IReadOnlyList<LevelUpRuleDef> rules = LevelUpRegistry.CompileDrafts(drafts, _ => { });
        if (SpecializationPolicy.AllowedSlots(20, rules) != 0)
        {
            Assert.Fail("[prosequor] Level-up fixture failed (contrib disable spec).");
        }

        PlayerProgressState state = FreshState();
        LevelUpRules.Apply(state, rules, beforeLevel: 1, afterLevel: 5, new Random(1));
        // Levels 2 and 4 fire every-2 with value 2 → +4 points; level 5 grants strength.
        if (state.UnlockPoints != 4
            || state.Attributes[AttributeIds.Strength] != AttributeGrowth.DefaultScore + 1)
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
