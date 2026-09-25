using Newtonsoft.Json.Linq;
using Prosequor.Ability;
using Prosequor.Ability.Actions;
using Prosequor.Ability.Hooks;
using Prosequor.Data;
using Prosequor.Player;
using Prosequor.Xp;
using Vintagestory.API.Common;
using Xunit;

namespace Prosequor.Progress;

/// <summary>Attribute-stat compile / pipeline / index / memo fixtures (no world required).</summary>
public static class AttributeStatFixtures
{
    public static void VerifyAll()
    {
        VerifyCompilerAndIndex();
        VerifyCatalogDefinesAttributes();
        VerifyPipelineDeltas();
        VerifySatietyAndHungerDelay();
        VerifyMappedIntRegisteredForAllPlayerStatPhases();
        VerifyMappedFloatRegisteredForCatEyesCapacity();
        VerifyOnDamageActionsRegistered();
        VerifyMemoHitAndScopedInvalidation();
        VerifyAttributeMappedIntFormulaMechanism();
        VerifyAttributeMappedIntHingeFormulaMechanism();
        VerifyAttributeMappedFloatFormulaMechanism();
        VerifyShippedMappedIntRulesMatchPipeline();
        VerifyCatEyesCapacityPipeline();
    }

    /// <summary>
    /// A registered stat id is the attribute catalog: canonicalize, score compile, and ensure.
    /// </summary>
    static void VerifyCatalogDefinesAttributes()
    {
        AttributeStatRegistry stats = new();
        stats.Register(new AttributeStatDef { Id = "strength", Rules = new() });
        stats.Register(new AttributeStatDef { Id = "Luck", Rules = new() });
        stats.Register(new AttributeStatDef { Id = "luck", Rules = new() }); // last-win casing

        if (stats.Canonicalize("LUCK") != "luck"
            || stats.Canonicalize("missing") != null
            || stats.All.Count != 2
            || stats.All[1].Id != "luck")
        {
            Assert.Fail(
                $"[prosequor] Stat registry catalog fixture failed (canonicalize/last-win; count={stats.All.Count}).");
            return;
        }

        List<string> errors = new();
        IReadOnlyList<AttributeScoreEntry> scores = AttributeScoreCompiler.Compile(
            "fixture",
            [
                new AttributeScoreJson { id = "Luck", value = 0.4f },
                new AttributeScoreJson { id = "strength", value = 0.1f }
            ],
            errors,
            stats);
        if (errors.Count != 0
            || scores.Count != 2
            || scores[0].Id != "strength"
            || scores[1].Id != "luck")
        {
            Assert.Fail(
                $"[prosequor] Stat registry catalog fixture failed (attributeScores; errors={errors.Count} count={scores.Count}).");
            return;
        }

        errors.Clear();
        IReadOnlyList<AttributeScoreEntry> unknown = AttributeScoreCompiler.Compile(
            "fixture",
            [new AttributeScoreJson { id = "perception", value = 1f }],
            errors,
            stats);
        if (unknown.Count != 0 || errors.Count != 1)
        {
            Assert.Fail(
                $"[prosequor] Stat registry catalog fixture failed (unknown vs catalog; count={unknown.Count} errors={errors.Count}).");
            return;
        }

        PlayerProgressState state = new();
        IReadOnlyList<string> catalog = AttributeIds.CatalogIds(stats);
        PlayerProgressState.EnsureAttributeEntries(state, catalog);
        if (!state.Attributes.ContainsKey("luck")
            || !state.Attributes.ContainsKey("strength")
            || state.Attributes.ContainsKey(AttributeIds.Perception))
        {
            Assert.Fail("[prosequor] Stat registry catalog fixture failed (EnsureAttributeEntries).");
        }

        TraitAttributeRegistry traits = new();
        traits.Register(new TraitAttributeMapping
        {
            Code = "lucky",
            Attributes = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                ["luck"] = 2
            }
        });
        // Load path uses Canonicalize against catalog; emulate by resolving with ResolveScores catalog.
        Dictionary<string, int> resolved = TraitAttributeConverter.ResolveScores(
            traits,
            ["lucky"],
            catalog);
        if (resolved["luck"] != AttributeGrowth.DefaultScore + 2
            || resolved["strength"] != AttributeGrowth.DefaultScore)
        {
            Assert.Fail(
                $"[prosequor] Stat registry catalog fixture failed (trait delta; luck={resolved["luck"]}).");
        }
    }

    static NumberSpec ParseNumberSpec(string json)
    {
        Assert.True(NumberSpec.TryParse(JObject.Parse(json), out NumberSpec? spec, out string error), error);
        return spec!;
    }

    /// <summary>
    /// Mechanism only: synthetic endpoints, clamps, rounding, and relative change.
    /// Balance numbers from shipped JSON do not belong here.
    /// </summary>
    static void VerifyAttributeMappedIntFormulaMechanism()
    {
        const int fromScore = 0;
        const int toScore = 20;
        const int fromValue = 100;
        const int toValue = 150; // +50% at the high end
        const string round = "ceil";

        int atFrom = AbilityFormulas.AttributeMappedInt(
            fromScore, fromScore, fromValue, toScore, toValue, round);
        int atTo = AbilityFormulas.AttributeMappedInt(
            toScore, fromScore, fromValue, toScore, toValue, round);
        if (atFrom != fromValue || atTo != toValue)
        {
            Assert.Fail(string.Format(
                "[prosequor] Mapped-int endpoints failed. got {0}/{1} want {2}/{3}.",
                atFrom,
                atTo,
                fromValue,
                toValue));
        }

        int below = AbilityFormulas.AttributeMappedInt(
            fromScore - 5, fromScore, fromValue, toScore, toValue, round);
        int above = AbilityFormulas.AttributeMappedInt(
            toScore + 5, fromScore, fromValue, toScore, toValue, round);
        if (below != fromValue || above != toValue)
        {
            Assert.Fail(string.Format(
                "[prosequor] Mapped-int clamp failed. got {0}/{1} want {2}/{3}.",
                below,
                above,
                fromValue,
                toValue));
        }

        int mid = AbilityFormulas.AttributeMappedInt(
            (fromScore + toScore) / 2, fromScore, fromValue, toScore, toValue, round);
        if (mid < fromValue || mid > toValue)
        {
            Assert.Fail(string.Format(
                "[prosequor] Mapped-int midpoint left the [from,to] band. mid={0} band={1}..{2}.",
                mid,
                fromValue,
                toValue));
        }

        // Rising score must not decrease an ascending curve.
        int prev = fromValue;
        for (int score = fromScore; score <= toScore; score++)
        {
            int v = AbilityFormulas.AttributeMappedInt(
                score, fromScore, fromValue, toScore, toValue, round);
            if (v < prev)
            {
                Assert.Fail(string.Format(
                    "[prosequor] Mapped-int ascending curve decreased at score {0}: {1} -> {2}.",
                    score,
                    prev,
                    v));
            }

            prev = v;
        }

        // Descending curve (armor-style): high score → lower value.
        const int hi = 200;
        const int lo = 25;
        int dFrom = AbilityFormulas.AttributeMappedInt(0, 0, hi, 18, lo, round);
        int dTo = AbilityFormulas.AttributeMappedInt(18, 0, hi, 18, lo, round);
        if (dFrom != hi || dTo != lo || dTo >= dFrom)
        {
            Assert.Fail(string.Format(
                "[prosequor] Mapped-int descending endpoints failed. got {0}/{1} want {2}/{3}.",
                dFrom,
                dTo,
                hi,
                lo));
        }

        // Hinge / minScore-style: below fromScore stays at fromValue.
        const int gate = 13;
        int gatedLow = AbilityFormulas.AttributeMappedInt(10, gate, 0, 18, 50, round);
        int gatedAt = AbilityFormulas.AttributeMappedInt(gate, gate, 0, 18, 50, round);
        if (gatedLow != 0 || gatedAt != 0)
        {
            Assert.Fail(string.Format(
                "[prosequor] Mapped-int below/at fromScore should stay at fromValue. got {0}/{1}.",
                gatedLow,
                gatedAt));
        }
    }

    static void VerifyAttributeMappedIntHingeFormulaMechanism()
    {
        const int fromScore = 0;
        const int midScore = 10;
        const int toScore = 18;
        const int fromValue = 70;
        const int midValue = 100;
        const int toValue = 120;

        int atFrom = AbilityFormulas.AttributeMappedInt(
            fromScore, fromScore, fromValue, toScore, toValue, "ceil", midScore, midValue);
        int atMid = AbilityFormulas.AttributeMappedInt(
            midScore, fromScore, fromValue, toScore, toValue, "ceil", midScore, midValue);
        int atTo = AbilityFormulas.AttributeMappedInt(
            toScore, fromScore, fromValue, toScore, toValue, "ceil", midScore, midValue);
        if (atFrom != fromValue || atMid != midValue || atTo != toValue)
        {
            Assert.Fail(string.Format(
                "[prosequor] Mapped-int hinge endpoints failed. got {0}/{1}/{2} want {3}/{4}/{5}.",
                atFrom,
                atMid,
                atTo,
                fromValue,
                midValue,
                toValue));
        }
    }

    static void VerifyAttributeMappedFloatFormulaMechanism()
    {
        const int fromScore = 14;
        const int toScore = 18;
        const float fromValue = 0.1f;
        const float toValue = 0.5f; // 5x at the high end relative to from

        float atFrom = AbilityFormulas.AttributeMappedFloat(
            fromScore, fromScore, fromValue, toScore, toValue);
        float atTo = AbilityFormulas.AttributeMappedFloat(
            toScore, fromScore, fromValue, toScore, toValue);
        float atMid = AbilityFormulas.AttributeMappedFloat(
            (fromScore + toScore) / 2, fromScore, fromValue, toScore, toValue);
        float expectedMid = (fromValue + toValue) * 0.5f;
        if (Math.Abs(atFrom - fromValue) > 1e-5f
            || Math.Abs(atTo - toValue) > 1e-5f
            || Math.Abs(atMid - expectedMid) > 1e-5f)
        {
            Assert.Fail(string.Format(
                "[prosequor] Mapped-float mechanism failed. got {0}/{1}/{2} want {3}/{4}/{5}.",
                atFrom,
                atMid,
                atTo,
                fromValue,
                expectedMid,
                toValue));
        }
    }

    /// <summary>
    /// Inferred: for every shipped stats/*.json mapped-int player-stats rule, the live pipeline
    /// at a score must equal AbilityFormulas evaluated with that rule's own params.
    /// Changing balance JSON updates the expected values automatically.
    /// </summary>
    static void VerifyShippedMappedIntRulesMatchPipeline()
    {
        string dir = Path.Combine(AppContext.BaseDirectory, "fixtures", "stats");
        if (!Directory.Exists(dir))
        {
            Assert.Fail(string.Format("[prosequor] Missing shipped stats fixtures at {0}.", dir));
            return;
        }

        string[] files = Directory.GetFiles(dir, "*.json");
        if (files.Length == 0)
        {
            Assert.Fail("[prosequor] No stats/*.json fixtures were copied to the test output.");
            return;
        }

        HookRegistry hooks = new();
        AbilityActionRegistry actions = new(hooks);
        AbilityBootstrap.RegisterBuiltIns(hooks, actions);

        int rulesChecked = 0;
        foreach (string file in files)
        {
            AttributeStatDefJson? row = Newtonsoft.Json.JsonConvert.DeserializeObject<AttributeStatDefJson>(
                File.ReadAllText(file));
            if (row?.rules == null || string.IsNullOrWhiteSpace(row.id))
            {
                Assert.Fail(string.Format("[prosequor] Invalid attribute stat asset {0}.", file));
                return;
            }

            string attributeId = row.id.Trim();
            foreach (AttributeStatRuleJson rule in row.rules)
            {
                if (rule == null
                    || !string.Equals(rule.hook, "prosequor:player-interaction", StringComparison.OrdinalIgnoreCase)
                    || !string.Equals(
                        rule.action,
                        "prosequor:add-mapped-number",
                        StringComparison.OrdinalIgnoreCase)
                    || rule.@params == null
                    || string.IsNullOrWhiteSpace(rule.@params.Value<string>("round")))
                {
                    continue;
                }

                if (!TryReadMappedIntParams(
                        rule.@params,
                        out int fromScore,
                        out float fromValue,
                        out int toScore,
                        out float toValue,
                        out string round,
                        out int? midScore,
                        out float? midValue,
                        out string? paramError))
                {
                    Assert.Fail(string.Format(
                        "[prosequor] Bad mapped-number params in {0} rule '{1}': {2}",
                        attributeId,
                        rule.id,
                        paramError));
                    return;
                }

                List<string> errors = new();
                int sourceOrder = 0;
                CollectionIndex collections = new();
                List<AbilityRule>? compiled = AttributeRuleCompiler.CompileRules(
                    attributeId,
                    [rule],
                    hooks,
                    actions,
                    collections,
                    errors,
                    ref sourceOrder);
                if (compiled == null || compiled.Count != 1 || errors.Count > 0)
                {
                    Assert.Fail(string.Format(
                        "[prosequor] Failed compiling shipped rule {0}/{1}: {2}",
                        attributeId,
                        rule.id,
                        string.Join("; ", errors)));
                    return;
                }

                if (!hooks.TryGet(HookIds.PlayerInteraction, out _)
                    || string.IsNullOrWhiteSpace(rule.verb))
                {
                    Assert.Fail(string.Format(
                        "[prosequor] Missing player-interaction verb on {0}/{1}.",
                        attributeId,
                        rule.id));
                    return;
                }

                VerbId verb = VerbId.Normalize(rule.verb);
                PhaseId phase = string.IsNullOrWhiteSpace(rule.phase)
                    ? HookIds.Default
                    : PhaseId.Normalize(rule.phase);

                FixtureSkillRegistry skills = new();
                AttributeStatRegistry attributeStats = new();
                attributeStats.Register(new AttributeStatDef
                {
                    Id = attributeId,
                    Rules = compiled
                });
                AbilityPipeline pipeline = new(actions, skills, attributeStats);
                AttributeFixtureProgress progress = new();

                int minScore = rule.minScore ?? int.MinValue;
                int[] probeScores =
                [
                    fromScore - 1,
                    fromScore,
                    (fromScore + toScore) / 2,
                    toScore,
                    toScore + 1,
                    minScore - 1,
                    minScore
                ];

                foreach (int score in probeScores.Distinct())
                {
                    int actual = RunPhase(pipeline, progress, attributeId, verb, score, phase);
                    int expected = score < minScore
                        ? 0
                        : AbilityFormulas.AttributeMappedInt(
                            score,
                            fromScore,
                            fromValue,
                            toScore,
                            toValue,
                            round,
                            midScore,
                            midValue);
                    if (actual != expected)
                    {
                        Assert.Fail(string.Format(
                            "[prosequor] Shipped rule {0}/{1} at score {2}: pipeline={3} formula={4} (params {5}→{6} over {7}→{8}).",
                            attributeId,
                            rule.id,
                            score,
                            actual,
                            expected,
                            fromValue,
                            toValue,
                            fromScore,
                            toScore));
                        return;
                    }
                }

                rulesChecked++;
            }
        }

        if (rulesChecked == 0)
        {
            Assert.Fail("[prosequor] No player-interaction rounded mapped-number rules found in shipped stats fixtures.");
        }
    }

    static bool TryReadMappedIntParams(
        Newtonsoft.Json.Linq.JObject raw,
        out int fromScore,
        out float fromValue,
        out int toScore,
        out float toValue,
        out string round,
        out int? midScore,
        out float? midValue,
        out string? error)
    {
        fromScore = 0;
        fromValue = 0f;
        toScore = 0;
        toValue = 0f;
        round = "ceil";
        midScore = null;
        midValue = null;
        error = null;

        int? fs = raw.Value<int?>("fromScore");
        float? fv = raw.Value<float?>("fromValue");
        int? ts = raw.Value<int?>("toScore");
        float? tv = raw.Value<float?>("toValue");
        if (fs is null || fv is null || ts is null || tv is null)
        {
            error = "fromScore/fromValue/toScore/toValue required.";
            return false;
        }

        fromScore = fs.Value;
        fromValue = fv.Value;
        toScore = ts.Value;
        toValue = tv.Value;
        round = raw.Value<string>("round") ?? "ceil";
        midScore = raw.Value<int?>("midScore");
        midValue = raw.Value<float?>("midValue");
        return true;
    }

    static void VerifyMappedIntRegisteredForAllPlayerStatPhases()
    {
        HookRegistry hooks = new();
        AbilityActionRegistry actions = new(hooks);
        AbilityBootstrap.RegisterBuiltIns(hooks, actions);

        VerbId[] verbs =
        [
            VerbIds.Health,
            VerbIds.Satiety,
            VerbIds.HungerDelay,
            VerbIds.ArmorWalk,
            VerbIds.MeleeDamage,
            VerbIds.BasicSlots,
            VerbIds.RangedSpeed,
            VerbIds.RangedAcc,
            VerbIds.FallDamageFactor,
            VerbIds.FallDamageThreshold,
            VerbIds.TemporalRecoverRate,
            VerbIds.TemporalDrainRate,
            VerbIds.AnimalThreat,
            VerbIds.CritChance,
            VerbIds.WholeVesselLootChance
        ];
        foreach (VerbId verb in verbs)
        {
            if (!actions.TryGet(ActionIds.AddMappedNumber, HookIds.PlayerInteraction, verb, HookIds.Default, out _))
            {
                Assert.Fail(string.Format("[prosequor] Mapped-number not registered for player-interaction/{0}.",
                    verb));
            }
        }
    }

    static void VerifyMappedFloatRegisteredForCatEyesCapacity()
    {
        HookRegistry hooks = new();
        AbilityActionRegistry actions = new(hooks);
        AbilityBootstrap.RegisterBuiltIns(hooks, actions);

        if (!actions.TryGet(ActionIds.AddMappedNumber, HookIds.PlayerInteraction, VerbIds.CatEyes, HookIds.Default, out _))
        {
            Assert.Fail("[prosequor] Mapped-number not registered for player-interaction/cat-eyes.");
        }
    }

    static void VerifyOnDamageActionsRegistered()
    {
        HookRegistry hooks = new();
        AbilityActionRegistry actions = new(hooks);
        AbilityBootstrap.RegisterBuiltIns(hooks, actions);

        if (!actions.TryGet(ActionIds.AddMappedNumber, HookIds.PlayerInteraction, VerbIds.OnDamage, HookIds.Amount, out _))
        {
            Assert.Fail("[prosequor] Mapped-number not registered for on-damage/amount.");
        }

        if (!actions.TryGet(ActionIds.AddMappedNumber, HookIds.PlayerInteraction, VerbIds.OnDamage, HookIds.LastStand, out _))
        {
            Assert.Fail("[prosequor] Mapped-number not registered for on-damage/last-stand.");
        }

        AttributeStatRuleJson[] rows =
        [
            new()
            {
                id = "prosequor:res-frost-damage",
                minScore = 0,
                hook = "prosequor:player-interaction",
                verb = "prosequor:on-damage",
                phase = "amount",
                action = "prosequor:add-mapped-number",
                when = new AbilityWhenJson
                {
                    tags =
                    [
                        "damage:" + TakeDamageStation.TagDamageFrost,
                        "damage:" + TakeDamageStation.TagDamageWeather
                    ]
                },
                @params = new Newtonsoft.Json.Linq.JObject
                {
                    ["op"] = "scale",
                    ["fromScore"] = 0,
                    ["fromValue"] = 2.5,
                    ["toScore"] = 18,
                    ["toValue"] = 0.5
                }
            },
            new()
            {
                id = "prosequor:res-last-stand",
                minScore = 14,
                hook = "prosequor:player-interaction",
                verb = "prosequor:on-damage",
                phase = "last-stand",
                action = "prosequor:add-mapped-number",
                @params = new Newtonsoft.Json.Linq.JObject
                {
                    ["fromScore"] = 14,
                    ["fromValue"] = 10,
                    ["toScore"] = 18,
                    ["toValue"] = 2,
                    ["round"] = "ceil"
                }
            }
        ];

        List<string> errors = new();
        int sourceOrder = 0;
        CollectionIndex collections = new();
        List<AbilityRule>? compiled = AttributeRuleCompiler.CompileRules(
            AttributeIds.Resilience,
            rows,
            hooks,
            actions,
            collections,
            errors,
            ref sourceOrder);

        if (compiled == null || compiled.Count != 2 || errors.Count > 0)
        {
            Assert.Fail(string.Format("[prosequor] On-damage resilience compiler fixture failed. errors={0}",
                string.Join("; ", errors)));
        }
    }

    static void VerifyCompilerAndIndex()
    {
        HookRegistry hooks = new();
        AbilityActionRegistry actions = new(hooks);
        AbilityBootstrap.RegisterBuiltIns(hooks, actions);

        AttributeStatRuleJson[] rows =
        [
            MappedRuleJson("prosequor:con-health", "prosequor:health", 0, -5, 18, 5),
            MappedRuleJson("prosequor:con-satiety", "prosequor:satiety", 0, 0, 18, 800),
            MappedRuleJson("prosequor:con-hunger-delay", "prosequor:hunger-delay", 13, 0, 18, 50)
        ];

        List<string> errors = new();
        int sourceOrder = 0;
        CollectionIndex collections = new();
        List<AbilityRule>? compiled = AttributeRuleCompiler.CompileRules(
            AttributeIds.Constitution,
            rows,
            hooks,
            actions,
            collections,
            errors,
            ref sourceOrder);

        if (compiled == null || compiled.Count != 3 || errors.Count > 0)
        {
            Assert.Fail(string.Format("[prosequor] Attribute-stat compiler fixture failed. errors={0}",
                string.Join("; ", errors)));
            return;
        }

        AttributeStatRegistry registry = new();
        registry.Register(new AttributeStatDef
        {
            Id = AttributeIds.Constitution,
            Rules = compiled
        });

        AttributeEffectIndex index = AttributeEffectIndex.Build(registry);
        IReadOnlyList<(HookId Hook, VerbId Verb, PhaseId Phase)> conPhases = index.ForAttribute(AttributeIds.Constitution);
        IReadOnlyList<(HookId Hook, VerbId Verb, PhaseId Phase)> strPhases = index.ForAttribute(AttributeIds.Strength);

        if (conPhases.Count != 3 || strPhases.Count != 0)
        {
            Assert.Fail(string.Format("[prosequor] Attribute effect index fixture failed. con={0} str={1}.",
                conPhases.Count,
                strPhases.Count));
            return;
        }

        HashSet<string> verbs = new(StringComparer.OrdinalIgnoreCase);
        foreach ((_, VerbId verb, _) in conPhases)
        {
            verbs.Add(verb.Value);
        }

        if (!verbs.Contains("prosequor:health") || !verbs.Contains("prosequor:satiety") || !verbs.Contains("prosequor:hunger-delay"))
        {
            Assert.Fail("[prosequor] Attribute effect index missing expected verbs.");
        }
    }

    static AttributeStatRuleJson MappedRuleJson(
        string id,
        string verb,
        int fromScore,
        int fromValue,
        int toScore,
        int toValue) =>
        new()
        {
            id = id,
            minScore = 0,
            hook = "prosequor:player-interaction",
            verb = verb,
            action = "prosequor:add-mapped-number",
            @params = new Newtonsoft.Json.Linq.JObject
            {
                ["fromScore"] = fromScore,
                ["fromValue"] = fromValue,
                ["toScore"] = toScore,
                ["toValue"] = toValue,
                ["round"] = "ceil"
            }
        };

    static AbilityRule MappedRule(
        string ruleId,
        VerbId verb,
        int fromScore,
        int fromValue,
        int toScore,
        int toValue,
        int sourceOrder) =>
        new()
        {
            RuleId = ruleId,
            Hook = HookIds.PlayerInteraction,
            Verb = verb,
            Phase = HookIds.Default,
            Action = ActionIds.AddMappedNumber,
            When = new AbilityWhenFilter(),
            Parameters = new MappedNumberParams
            {
                FromScore = fromScore,
                FromValue = fromValue,
                ToScore = toScore,
                ToValue = toValue,
                Round = "ceil"
            },
            Source = new AbilityRuleSource
            {
                SkillId = AttributeIds.Constitution,
                AttributeId = AttributeIds.Constitution,
                MinAttributeScore = 0
            },
            Priority = 0,
            SourceOrder = sourceOrder
        };

    static void VerifyPipelineDeltas()
    {
        const int fromScore = 0;
        const int toScore = 18;
        const int fromValue = -5;
        const int toValue = 5;

        HookRegistry hooks = new();
        AbilityActionRegistry actions = new(hooks);
        AbilityBootstrap.RegisterBuiltIns(hooks, actions);

        FixtureSkillRegistry skills = new();
        AttributeStatRegistry attributeStats = new();
        attributeStats.Register(new AttributeStatDef
        {
            Id = AttributeIds.Constitution,
            Rules =
            [
                MappedRule(
                    "prosequor:fixture-health",
                    VerbIds.Health,
                    fromScore,
                    fromValue,
                    toScore,
                    toValue,
                    1)
            ]
        });

        AbilityPipeline pipeline = new(actions, skills, attributeStats);
        AttributeFixtureProgress progress = new();

        int dFrom = RunPhase(pipeline, progress, AttributeIds.Constitution, VerbIds.Health, fromScore);
        int dMid = RunPhase(
            pipeline,
            progress,
            AttributeIds.Constitution,
            VerbIds.Health,
            (fromScore + toScore) / 2);
        int dTo = RunPhase(pipeline, progress, AttributeIds.Constitution, VerbIds.Health, toScore);

        int wantFrom = AbilityFormulas.AttributeMappedInt(
            fromScore, fromScore, fromValue, toScore, toValue, "ceil");
        int wantMid = AbilityFormulas.AttributeMappedInt(
            (fromScore + toScore) / 2, fromScore, fromValue, toScore, toValue, "ceil");
        int wantTo = AbilityFormulas.AttributeMappedInt(
            toScore, fromScore, fromValue, toScore, toValue, "ceil");

        if (dFrom != wantFrom || dMid != wantMid || dTo != wantTo)
        {
            Assert.Fail(string.Format(
                "[prosequor] Attribute health pipeline fixture failed. got {0}/{1}/{2} want {3}/{4}/{5}.",
                dFrom,
                dMid,
                dTo,
                wantFrom,
                wantMid,
                wantTo));
        }
    }

    static void VerifySatietyAndHungerDelay()
    {
        const int satFromScore = 0;
        const int satToScore = 18;
        const int satFromValue = 0;
        const int satToValue = 800;

        const int delayFromScore = 13;
        const int delayToScore = 18;
        const int delayFromValue = 0;
        const int delayToValue = 50;

        HookRegistry hooks = new();
        AbilityActionRegistry actions = new(hooks);
        AbilityBootstrap.RegisterBuiltIns(hooks, actions);

        FixtureSkillRegistry skills = new();
        AttributeStatRegistry attributeStats = new();
        attributeStats.Register(new AttributeStatDef
        {
            Id = AttributeIds.Constitution,
            Rules =
            [
                MappedRule(
                    "prosequor:fixture-satiety",
                    VerbIds.Satiety,
                    satFromScore,
                    satFromValue,
                    satToScore,
                    satToValue,
                    1),
                MappedRule(
                    "prosequor:fixture-hunger-delay",
                    VerbIds.HungerDelay,
                    delayFromScore,
                    delayFromValue,
                    delayToScore,
                    delayToValue,
                    2)
            ]
        });

        AbilityPipeline pipeline = new(actions, skills, attributeStats);
        AttributeFixtureProgress progress = new();

        foreach (int score in new[] { satFromScore, (satFromScore + satToScore) / 2, satToScore })
        {
            int actual = RunPhase(pipeline, progress, AttributeIds.Constitution, VerbIds.Satiety, score);
            int want = AbilityFormulas.AttributeMappedInt(
                score, satFromScore, satFromValue, satToScore, satToValue, "ceil");
            if (actual != want)
            {
                Assert.Fail(string.Format(
                    "[prosequor] Satiety pipeline at {0}: got {1} want {2}.",
                    score,
                    actual,
                    want));
                return;
            }
        }

        foreach (int score in new[] { delayFromScore - 3, delayFromScore, delayFromScore + 1, delayToScore })
        {
            int actual = RunPhase(
                pipeline,
                progress,
                AttributeIds.Constitution,
                VerbIds.HungerDelay,
                score);
            int want = AbilityFormulas.AttributeMappedInt(
                score, delayFromScore, delayFromValue, delayToScore, delayToValue, "ceil");
            if (actual != want)
            {
                Assert.Fail(string.Format(
                    "[prosequor] Hunger-delay pipeline at {0}: got {1} want {2}.",
                    score,
                    actual,
                    want));
                return;
            }
        }
    }

    static void VerifyMemoHitAndScopedInvalidation()
    {
        const int fromScore = 0;
        const int toScore = 18;
        const int fromValue = -5;
        const int toValue = 5;

        HookRegistry hooks = new();
        AbilityActionRegistry actions = new(hooks);
        AbilityBootstrap.RegisterBuiltIns(hooks, actions);

        FixtureSkillRegistry skills = new();
        skills.Register(new SkillDef
        {
            Id = "riding",
            Rules =
            [
                new AbilityRule
                {
                    RuleId = "root-speed",
                    Hook = HookIds.EntityInteraction,
                    Verb = VerbIds.Mounted,
                    Phase = HookIds.MoveSpeed,
                    Action = ActionIds.Number,
                    When = new AbilityWhenFilter(),
                    Parameters = ParseNumberSpec(
                        "{\"op\":\"add\",\"base\":0,\"perSkillLevel\":0.002,\"cap\":0.1}"),
                    Source = new AbilityRuleSource { SkillId = "riding" },
                    Priority = 0,
                    SourceOrder = 0
                }
            ]
        });

        AttributeStatRegistry attributeStats = new();
        attributeStats.Register(new AttributeStatDef
        {
            Id = AttributeIds.Constitution,
            Rules =
            [
                MappedRule(
                    "prosequor:fixture-health",
                    VerbIds.Health,
                    fromScore,
                    fromValue,
                    toScore,
                    toValue,
                    1)
            ]
        });

        AbilityPipeline pipeline = new(actions, skills, attributeStats);
        AttributeMemoFixtureProgress progress = new();
        progress.SetAttribute(AttributeIds.Constitution, 10);
        progress.SetSkillLevel("riding", 50);

        int wantAt10 = AbilityFormulas.AttributeMappedInt(
            10, fromScore, fromValue, toScore, toValue, "ceil");
        int wantAt18 = AbilityFormulas.AttributeMappedInt(
            18, fromScore, fromValue, toScore, toValue, "ceil");

        int first = RunPhase(pipeline, progress, AttributeIds.Constitution, VerbIds.Health, score: 10);
        int hitsBefore = progress.ComposeMemo.HitCount;
        int second = RunPhase(pipeline, progress, AttributeIds.Constitution, VerbIds.Health, score: 10);
        if (first != wantAt10 || second != wantAt10 || progress.ComposeMemo.HitCount != hitsBefore + 1)
        {
            Assert.Fail(string.Format(
                "[prosequor] Attribute health memo hit fixture failed. first={0} second={1} hits={2} wantValue={3}.",
                first,
                second,
                progress.ComposeMemo.HitCount,
                wantAt10));
            return;
        }

        MountedContext mountedContext = new()
        {
            Player = null,
            Progress = progress,
            Fact = new AbilityAction { Verb = MountedStation.VerbMounted, ActorUid = "fixture" }
        };
        float mounted = pipeline.Run(HookIds.EntityInteraction, VerbIds.Mounted, HookIds.MoveSpeed, mountedContext, 1f);
        int hitsAfterMountedMiss = progress.ComposeMemo.HitCount;

        progress.ComposeMemo.InvalidatePhase(HookIds.PlayerInteraction, VerbIds.Health, HookIds.Default);

        int afterInvalidate = RunPhase(
            pipeline,
            progress,
            AttributeIds.Constitution,
            VerbIds.Health,
            score: 10);
        float mountedAgain = pipeline.Run(HookIds.EntityInteraction, VerbIds.Mounted, HookIds.MoveSpeed, mountedContext, 1f);

        if (afterInvalidate != wantAt10
            || Math.Abs(mounted - mountedAgain) > 0.0001f
            || progress.ComposeMemo.HitCount != hitsAfterMountedMiss + 1)
        {
            Assert.Fail(string.Format("[prosequor] Attribute scoped memo invalidation fixture failed. " +
                "health={0} mounted={1}/{2} hits={3} want {4}.",
                afterInvalidate,
                mounted,
                mountedAgain,
                progress.ComposeMemo.HitCount,
                hitsAfterMountedMiss + 1));
            return;
        }

        // Fact fingerprints ignore attribute scores: without bump/invalidate, score change is stale.
        int at10 = RunPhase(pipeline, progress, AttributeIds.Constitution, VerbIds.Health, score: 10);
        int staleAt18 = RunPhase(pipeline, progress, AttributeIds.Constitution, VerbIds.Health, score: 18);
        if (at10 != wantAt10 || staleAt18 != wantAt10)
        {
            Assert.Fail(string.Format(
                "[prosequor] Attribute memo stale-without-bump fixture failed. at10={0} staleAt18={1} want {2}/{2}.",
                at10,
                staleAt18,
                wantAt10));
            return;
        }

        progress.BumpProgressRevision();
        int freshAt18 = RunPhase(pipeline, progress, AttributeIds.Constitution, VerbIds.Health, score: 18);
        if (freshAt18 != wantAt18)
        {
            Assert.Fail(string.Format(
                "[prosequor] Attribute memo bump-after-score-change fixture failed. got {0} want {1}.",
                freshAt18,
                wantAt18));
        }
    }

    static int RunPhase(
        AbilityPipeline pipeline,
        AttributeFixtureProgress progress,
        string attributeId,
        VerbId verb,
        int score,
        PhaseId? phase = null)
    {
        progress.SetAttribute(attributeId, score);
        PhaseId runPhase = phase ?? HookIds.Default;
        AbilityAction fact = new()
        {
            Verb = verb.Value,
            ActorUid = "fixture"
        };

        if (verb.Equals(VerbIds.OnDamage))
        {
            TakeDamageContext damageContext = new()
            {
                Player = null,
                Progress = progress,
                Fact = fact,
                Entity = null!,
                CurrentHealth = 20f,
                DamageSource = null!
            };
            return (int)pipeline.Run(HookIds.PlayerInteraction, verb, runPhase, damageContext, 0f);
        }

        PlayerInteractionContext context = new()
        {
            Player = null,
            Progress = progress,
            Fact = fact
        };

        return pipeline.Run(HookIds.PlayerInteraction, verb, runPhase, context, 0);
    }

    static void VerifyCatEyesCapacityPipeline()
    {
        const float from = 1f / 18f;
        const float to = 5f / 18f;

        HookRegistry hooks = new();
        AbilityActionRegistry actions = new(hooks);
        AbilityBootstrap.RegisterBuiltIns(hooks, actions);

        FixtureSkillRegistry skills = new();
        AttributeStatRegistry attributeStats = new();
        attributeStats.Register(new AttributeStatDef
        {
            Id = AttributeIds.Perception,
            Rules =
            [
                new AbilityRule
                {
                    RuleId = "prosequor:per-cat-eyes-capacity",
                    Hook = HookIds.PlayerInteraction,
                    Verb = VerbIds.CatEyes,
                    Phase = HookIds.Default,
                    Action = ActionIds.AddMappedNumber,
                    When = new AbilityWhenFilter(),
                    Parameters = new MappedNumberParams
                    {
                        FromScore = 14,
                        FromValue = from,
                        ToScore = 18,
                        ToValue = to
                    },
                    Source = new AbilityRuleSource
                    {
                        SkillId = AttributeIds.Perception,
                        AttributeId = AttributeIds.Perception,
                        MinAttributeScore = 14
                    },
                    Priority = 0,
                    SourceOrder = 1
                }
            ]
        });

        AbilityPipeline pipeline = new(actions, skills, attributeStats);
        AttributeFixtureProgress progress = new();

        float c13 = RunCatEyesCapacity(pipeline, progress, score: 13);
        float c14 = RunCatEyesCapacity(pipeline, progress, score: 14);
        float c18 = RunCatEyesCapacity(pipeline, progress, score: 18);

        if (c13 != 0f || Math.Abs(c14 - from) > 1e-5f || Math.Abs(c18 - to) > 1e-5f)
        {
            Assert.Fail(string.Format("[prosequor] Cat-eyes capacity pipeline fixture failed. got {0}/{1}/{2} want 0/{3}/{4}.",
                c13,
                c14,
                c18,
                from,
                to));
        }
    }

    static float RunCatEyesCapacity(AbilityPipeline pipeline, AttributeFixtureProgress progress, int score)
    {
        progress.SetAttribute(AttributeIds.Perception, score);
        CatEyesContext context = new()
        {
            Player = null,
            Progress = progress,
            Fact = new AbilityAction
            {
                Verb = VerbIds.CatEyes.Value,
                ActorUid = "fixture"
            }
        };

        return Math.Clamp(pipeline.Run(HookIds.PlayerInteraction, VerbIds.CatEyes, HookIds.Default, context, 0f), 0f, 1f);
    }

    sealed class FixtureSkillRegistry : ISkillRegistry
    {
        readonly List<SkillDef> all = new();

        public IReadOnlyList<SkillDef> All => all;
        public SkillMenuIndex MenuIndex { get; private set; } = SkillMenuIndex.Empty;

        public void Register(SkillDef def)
        {
            all.RemoveAll(s => string.Equals(s.Id, def.Id, StringComparison.OrdinalIgnoreCase));
            all.Add(def);
            MenuIndex = SkillMenuIndex.Build(all);
        }

        public bool TryGet(string id, out SkillDef def)
        {
            def = all.FirstOrDefault(s => string.Equals(s.Id, id, StringComparison.OrdinalIgnoreCase))!;
            return def != null;
        }

        public bool TryResolve(string idOrDisplayName, string? languageCode, out SkillDef def) =>
            TryGet(idOrDisplayName, out def);
    }

    class AttributeFixtureProgress : IPlayerProgress
    {
        readonly Dictionary<string, int> skillLevels = new(StringComparer.OrdinalIgnoreCase);
        readonly Dictionary<string, int> attributes = new(StringComparer.OrdinalIgnoreCase);

        public event Action? Changed;

        public int PlayerLevel => 1;
        public float PlayerXp => 0;
        public int UnlockPoints => 0;
        public float PlayerXpUntilNext => 0;

        public void SetSkillLevel(string skillId, int level)
        {
            skillLevels[skillId] = level;
            Changed?.Invoke();
        }

        public void SetAttribute(string id, int score)
        {
            attributes[id] = score;
            Changed?.Invoke();
        }

        public int GetSkillLevel(string skillId) =>
            skillLevels.TryGetValue(skillId, out int level) ? level : 0;

        public float GetSkillXp(string skillId) => 0;
        public IReadOnlyList<string> GetUnlocks(string skillId) => Array.Empty<string>();
        public bool HasUnlock(string skillId, string code) => false;
        public int GetUnlockTier(string skillId, string nodeId) => 0;

        public int GetAttribute(string id) =>
            attributes.TryGetValue(id, out int score) ? score : AttributeGrowth.DefaultScore;

        public float GetAttributeBucket(string id) => 0f;
        public bool HasSkillAccess(string skillId) => true;

        public void GetPlayerBar(out float intoLevel, out int needForNext, out int level)
        {
            intoLevel = 0;
            needForNext = 1;
            level = PlayerLevel;
        }

        public void GetSkillBar(string skillId, out float intoLevel, out int needForNext, out int level)
        {
            intoLevel = 0;
            needForNext = 1;
            level = GetSkillLevel(skillId);
        }

        public void AddPlayerXp(float amount, XpAwardMode mode = XpAwardMode.Earn) { }
        public void AddSkillXp(
            string skillId,
            float amount,
            AbilityAction? fact = null,
            XpAwardMode mode = XpAwardMode.Earn) { }
        public void AddUnlockPoints(int amount) { }
        public void SetPlayerLevel(int level) { }
        public void AddAttributeBucket(string id, float amount) { }
        public bool GrantUnlock(string skillId, string code, int cost = 1) => false;
        public bool RevokeUnlock(string skillId, string code, int refund = 1) => false;
        public UnlockPurchaseStatus TryPurchaseNode(string skillId, string nodeId) =>
            UnlockPurchaseStatus.UnknownNode;
    }

    sealed class AttributeMemoFixtureProgress : AttributeFixtureProgress, IAbilityComposeCache
    {
        readonly ComposeMemo composeMemo = new();

        public ComposeMemo ComposeMemo => composeMemo;

        public void BumpProgressRevision() => composeMemo.BumpProgressRevision();

        public bool TryGetActiveRules(
            HookId hook,
            VerbId verb,
            PhaseId phase,
            out IReadOnlyList<AbilityRule> rules)
        {
            rules = Array.Empty<AbilityRule>();
            return false;
        }
    }
}
