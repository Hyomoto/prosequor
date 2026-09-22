using Newtonsoft.Json.Linq;
using Prosequor.Ability.Actions;
using Prosequor.Ability.Hooks;
using Prosequor.Client;
using Prosequor.Data;
using Prosequor.Player;
using Prosequor.Progress;
using Prosequor.Xp;
using Vintagestory.API.Common;
using Xunit;

namespace Prosequor.Ability;

/// <summary>
/// xUnit registration / compiler / pipeline checks (xUnit pure tests).
/// </summary>
public static class AbilityFixtures
{
    public static void VerifyAll()
    {
        HookRegistry hooks = new();
        AbilityActionRegistry actions = new(hooks);
        AbilityBootstrap.RegisterBuiltIns(hooks, actions);

        VerifyRegistrationGuards();
        VerifyCompilerGuards(hooks, actions);
        VerifyPipelineBehavior();
        VerifySkillXpAndSpecialization(hooks, actions);
        VerifySkillContributions(hooks, actions);
        VerifyContributionDisableReplace(hooks, actions);
        VerifyContributionDisableAllAndXp();
        VerifyRequiresAndExcludes(hooks, actions);
        VerifyCatalogAlternateExclusion();
        VerifyOreGradeUpgrade();
        VerifyRuleIndex();
        VerifyItemFreshnessAction();
        VerifyChanceGateCompile(hooks, actions);
        VerifyFieldExpertisePipeline();
        VerifyVoxelWorkPipeline();
        VerifyOnProcessedPipeline();
        VerifyPerfectPresserPipeline();
        VerifyCookServePedigreeCopy();
        VerifyMealEatMods();
        VerifyMountedPipeline();
        VerifyAnimalBehaviorRegistration(hooks, actions);
        VerifyBoatingPipeline();
        VerifyOnRepairPipeline();
        VerifyOnCraftPipeline();
        VerifyCraftAttributePipeline();
        VerifyApplyQualityPipeline();
        VerifyMashRankAddsToDistilledQualityBase();
        VerifyCraftRefundPipeline();
        VerifyLastCraftPipeline();
        VerifyComposeMemo();
        CollectionOrFixtures.VerifyAll();
        XpBucketFixtures.VerifyAll();
        LevelUpHudFixtures.VerifyAll();
        SkillWaitingHintFixtures.VerifyAll();
        ActivityWatchFixtures.VerifyAll();
        AttributeGrowthFixtures.VerifyAll();
        AttributeBucketAxisFixtures.VerifyAll();
        LevelUpRuleFixtures.VerifyAll();
        AttributeStatFixtures.VerifyAll();
        TraitAttributeFixtures.VerifyAll();
        CreateCharacterClassFixtures.VerifyAll();
        HobbySkillFixtures.VerifyAll();
        CollectXpFixtures.VerifyAll();
        VerifyCropClimateWindowMath();
        VerifyTroughContributionWeights();
    }

    static void VerifyCropClimateWindowMath()
    {
        // Carrot-like span 42 (−10…32); fraction 0.1 → δ = 2.1
        float d100 = CropClimateWindow.ComputeHalfDeltaFromFraction(-10f, 32f, 0.1f);
        float d0 = CropClimateWindow.ComputeHalfDeltaFromFraction(-10f, 32f, 0f);
        float d1 = CropClimateWindow.ComputeHalfDeltaFromFraction(-10f, 32f, 0.001f);
        if (Math.Abs(d100 - 2.1f) > 0.0001f
            || d0 != 0f
            || Math.Abs(d1 - 0.021f) > 0.0001f
            || CropClimateWindow.ComputeHalfDeltaFromFraction(10f, 10f, 0.5f) != 0f
            || CropClimateWindow.ComputeHalfDeltaFromFraction(20f, 10f, 0.5f) != 0f)
        {
            Assert.Fail(
                $"[prosequor] CropClimateWindow math failed: d100={d100} d0={d0} d1={d1}");
        }

        if (Math.Abs(CropClimateWindow.AdjustColdThreshold(-10f, null!) - (-10f)) > 0.0001f
            || Math.Abs(CropClimateWindow.AdjustHeatThreshold(32f, null!) - 32f) > 0.0001f)
        {
            Assert.Fail("[prosequor] CropClimateWindow adjust with no stamp should be identity.");
        }
    }

    static void VerifyTroughContributionWeights()
    {
        Dictionary<string, int> weights = new(StringComparer.Ordinal);
        TroughContributionStation.AddContribution(weights, "alice", 2);
        TroughContributionStation.AddContribution(weights, "bob", 1);
        if (TroughContributionStation.TotalWeight(weights) != 3
            || !weights.TryGetValue("alice", out int alice)
            || alice != 2
            || !weights.TryGetValue("bob", out int bob)
            || bob != 1)
        {
            Assert.Fail("[prosequor] TroughContributionStation weight add failed.");
        }

        Random rand = new(1);
        int tookAlice = 0;
        int tookBob = 0;
        for (int i = 0; i < 3; i++)
        {
            if (!TroughContributionStation.TryTakeContribution(weights, out string? uid, rand)
                || string.IsNullOrEmpty(uid))
            {
                Assert.Fail("[prosequor] TroughContributionStation should take until empty.");
                return;
            }

            if (uid == "alice")
            {
                tookAlice++;
            }
            else if (uid == "bob")
            {
                tookBob++;
            }
            else
            {
                Assert.Fail($"[prosequor] unexpected trough contributor {uid}");
                return;
            }
        }

        if (tookAlice != 2 || tookBob != 1 || weights.Count != 0
            || TroughContributionStation.TryTakeContribution(weights, out _))
        {
            Assert.Fail("[prosequor] TroughContributionStation weighted take failed.");
        }

        TroughContributionStation.AddContribution(weights, null, 5);
        TroughContributionStation.AddContribution(weights, "", 5);
        if (weights.Count != 0)
        {
            Assert.Fail("[prosequor] TroughContributionStation should ignore blank uids.");
        }
    }

    static CollectionIndex NewCollections(params string[] keys)
    {
        CollectionIndex collections = new();
        foreach (string key in keys)
        {
            collections.EnsureKey(key);
        }

        return collections;
    }

    static CollectionIndex WithCodes(CollectionIndex collections, params (string Id, string Code)[] entries)
    {
        foreach ((string id, string code) in entries)
        {
            collections.AddCode(id, code);
        }

        return collections;
    }

    static AbilityWhenFilter CompileWhen(
        CollectionIndex collections,
        params string[] tags)
    {
        AbilityWhenJson json = new() { tags = tags.Length == 0 ? null : tags };
        if (!AbilityRuleCompiler.TryCompileWhen("fixture", json, collections, out AbilityWhenFilter? filter, out string error)
            || filter == null)
        {
            throw new InvalidOperationException(error);
        }

        return filter;
    }

    static NumberSpec ParseNumberSpec(string json)
    {
        if (!NumberSpec.TryParse(JObject.Parse(json), out NumberSpec? spec, out string error) || spec == null)
        {
            throw new InvalidOperationException(error);
        }

        return spec;
    }

    static void VerifyRequiresAndExcludes(
        IHookRegistry hooks,
        IAbilityActionRegistry actions)
    {
        int order = 0;
        SkillTreeCompiler.CompileResult andResult = SkillTreeCompiler.Compile(
            "req-and",
            100,
            new SkillTreeJson
            {
                nodes =
                [
                    new SkillTreeNodeJson { id = "a", requires = [] },
                    new SkillTreeNodeJson { id = "b", requires = [] },
                    new SkillTreeNodeJson { id = "c", requires = ["a", "b"] }
                ]
            },
            hooks,
            actions,
            NewCollections(),
            ref order);
        if (!andResult.Success
            || andResult.Tree == null
            || andResult.Tree.ById["c"].RequireGroups.Count != 2
            || andResult.Tree.ById["c"].RequireGroups[0].Alternatives.Count != 1
            || andResult.Tree.ById["c"].RequireGroups[1].Alternatives[0] != "b")
        {
            Assert.Fail("[prosequor] Requires fixture failed (flat AND groups).");
            return;
        }

        order = 0;
        SkillTreeCompiler.CompileResult orResult = SkillTreeCompiler.Compile(
            "req-or",
            100,
            new SkillTreeJson
            {
                nodes =
                [
                    new SkillTreeNodeJson { id = "a", requires = [] },
                    new SkillTreeNodeJson { id = "b", requires = [] },
                    new SkillTreeNodeJson { id = "c", requires = [] },
                    new SkillTreeNodeJson
                    {
                        id = "child",
                        requires = SkillRequireJson.Mixed(new[] { "a", "b" }, "c")
                    }
                ]
            },
            hooks,
            actions,
            NewCollections(),
            ref order);
        if (!orResult.Success
            || orResult.Tree == null
            || orResult.Tree.ById["child"].RequireGroups.Count != 2
            || orResult.Tree.ById["child"].RequireGroups[0].Alternatives.Count != 2
            || orResult.Tree.ById["child"].AllRequirementIds.Count != 3)
        {
            Assert.Fail("[prosequor] Requires fixture failed (nested OR groups).");
            return;
        }

        SkillDef orSkill = new()
        {
            Id = "req-or",
            MaxLevel = 100,
            Tree = orResult.Tree
        };
        FixtureProgress progress = new();
        progress.SetSkillLevel("req-or", 100);
        progress.SetUnlockPoints(10);

        if (SkillTreeEligibility.Evaluate(orSkill, progress, "child", out _)
            != UnlockPurchaseStatus.MissingPrerequisite)
        {
            Assert.Fail("[prosequor] Requires fixture failed (OR child locked with no parents).");
            return;
        }

        progress.SetUnlockTier("req-or", "c", 1);
        if (SkillTreeEligibility.Evaluate(orSkill, progress, "child", out _)
            != UnlockPurchaseStatus.MissingPrerequisite)
        {
            Assert.Fail("[prosequor] Requires fixture failed (OR child locked with only AND parent).");
            return;
        }

        progress.SetUnlockTier("req-or", "a", 1);
        if (SkillTreeEligibility.Evaluate(orSkill, progress, "child", out _)
            != UnlockPurchaseStatus.Ok)
        {
            Assert.Fail("[prosequor] Requires fixture failed (OR child unlocked with a+c).");
            return;
        }

        order = 0;
        SkillTreeCompiler.CompileResult emptyOr = SkillTreeCompiler.Compile(
            "req-empty-or",
            100,
            new SkillTreeJson
            {
                nodes =
                [
                    new SkillTreeNodeJson { id = "a", requires = [] },
                    new SkillTreeNodeJson
                    {
                        id = "bad",
                        // Explicit empty nested array: Mixed(string[]) can be consumed as
                        // params object[] itself and become an empty requires list instead.
                        requires = [new JArray()]
                    }
                ]
            },
            hooks,
            actions,
            NewCollections(),
            ref order);
        if (emptyOr.Success)
        {
            Assert.Fail("[prosequor] Requires fixture failed (empty OR group should reject).");
            return;
        }

        order = 0;
        SkillTreeCompiler.CompileResult exclResult = SkillTreeCompiler.Compile(
            "req-excl",
            100,
            new SkillTreeJson
            {
                nodes =
                [
                    new SkillTreeNodeJson { id = "river", requires = [], excludes = ["coast"] },
                    new SkillTreeNodeJson { id = "coast", requires = [] }
                ]
            },
            hooks,
            actions,
            NewCollections(),
            ref order);
        if (!exclResult.Success
            || exclResult.Tree == null
            || !exclResult.Tree.ById["river"].Excludes.Contains("coast", StringComparer.OrdinalIgnoreCase)
            || !exclResult.Tree.ById["coast"].Excludes.Contains("river", StringComparer.OrdinalIgnoreCase)
            || exclResult.Warnings.Count == 0)
        {
            Assert.Fail("[prosequor] Excludes fixture failed (symmetrize one-sided exclude).");
            return;
        }

        SkillDef exclSkill = new()
        {
            Id = "req-excl",
            MaxLevel = 100,
            Tree = exclResult.Tree
        };
        FixtureProgress exclProgress = new();
        exclProgress.SetSkillLevel("req-excl", 100);
        exclProgress.SetUnlockPoints(10);
        exclProgress.SetUnlockTier("req-excl", "river", 1);
        if (SkillTreeEligibility.Evaluate(exclSkill, exclProgress, "coast", out _)
            != UnlockPurchaseStatus.BlockedByExclusive)
        {
            Assert.Fail("[prosequor] Excludes fixture failed (coast blocked after river).");
        }
    }

    static void VerifyRegistrationGuards()
    {
        HookRegistry hooks = new();
        AbilityActionRegistry actions = new(hooks);
        hooks.RegisterHook(HookIds.BlockInteraction);
        hooks.RegisterPhase(HookIds.BlockInteraction, VerbIds.InteractionSpeed, HookIds.Default, typeof(InteractionSpeedContext), typeof(float));

        try
        {
            hooks.RegisterHook(HookIds.BlockInteraction);
            Assert.Fail("[prosequor] Hook fixture failed (duplicate hook should throw).");
        }
        catch (InvalidOperationException)
        {
            // expected
        }

        try
        {
            hooks.RegisterPhase(HookIds.BlockInteraction, VerbIds.InteractionSpeed, HookIds.Default, typeof(InteractionSpeedContext), typeof(float));
            Assert.Fail("[prosequor] Hook fixture failed (duplicate phase should throw).");
        }
        catch (InvalidOperationException)
        {
            // expected
        }

        actions.Register(new NumberInteractionSpeedAction());
        try
        {
            actions.Register(new NumberInteractionSpeedAction());
            Assert.Fail("[prosequor] Action fixture failed (duplicate action should throw).");
        }
        catch (InvalidOperationException)
        {
            // expected
        }

        try
        {
            actions.Register(new WrongShapeFloatAction());
            Assert.Fail("[prosequor] Action fixture failed (wrong-shaped binding should throw).");
        }
        catch (InvalidOperationException)
        {
            // expected
        }

        try
        {
            actions.Register(new UnknownHookAction());
            Assert.Fail("[prosequor] Action fixture failed (unknown hook action should throw).");
        }
        catch (InvalidOperationException)
        {
            // expected
        }
    }

    static void VerifyCompilerGuards(IHookRegistry hooks, IAbilityActionRegistry actions)
    {
        int order = 0;
        List<string> errors = new();

        if (AbilityRuleCompiler.CompileEffects(
                "fixture",
                null,
                null,
                [new AbilityEffectJson { type = "miningSpeed", @params = new JObject { ["op"] = "scale", ["base"] = 0, ["perSkillLevel"] = 0.001 } }],
                hooks,
                actions,
                NewCollections("soil", "leaves", "sapling", "immature-crop", "farmland", "fish", "obsidian"),
                errors,
                ref order) != null)
        {
            Assert.Fail("[prosequor] Compiler fixture failed (legacy type should reject).");
        }

        errors.Clear();
        if (AbilityRuleCompiler.CompileEffects(
                "fixture",
                null,
                null,
                [
                    new AbilityEffectJson
                    {
                        hook = "prosequor:missing-hook",
                        phase = "value",
                        action = "prosequor:number",
                        @params = new JObject { ["op"] = "scale", ["base"] = 0, ["perSkillLevel"] = 0.001 }
                    }
                ],
                hooks,
                actions,
                NewCollections("soil", "leaves", "sapling", "immature-crop", "farmland", "fish", "obsidian"),
                errors,
                ref order) != null)
        {
            Assert.Fail("[prosequor] Compiler fixture failed (unknown hook should reject).");
        }

        errors.Clear();
        if (AbilityRuleCompiler.CompileEffects(
                "fixture",
                null,
                null,
                [
                    new AbilityEffectJson
                    {
                        hook = "prosequor:block-interaction",
                    verb = "prosequor:interaction-speed",
                        phase = "missing",
                        action = "prosequor:number",
                        @params = new JObject { ["op"] = "scale", ["base"] = 0, ["perSkillLevel"] = 0.001 }
                    }
                ],
                hooks,
                actions,
                NewCollections("soil", "leaves", "sapling", "immature-crop", "farmland", "fish", "obsidian"),
                errors,
                ref order) != null)
        {
            Assert.Fail("[prosequor] Compiler fixture failed (unknown phase should reject).");
        }

        errors.Clear();
        if (AbilityRuleCompiler.CompileEffects(
                "fixture",
                null,
                null,
                [
                    new AbilityEffectJson
                    {
                        hook = "prosequor:block-interaction",
                    verb = "prosequor:interaction-speed",
                        action = "prosequor:does-not-exist",
                        @params = new JObject { ["op"] = "scale", ["base"] = 0, ["perSkillLevel"] = 0.001 }
                    }
                ],
                hooks,
                actions,
                NewCollections("soil", "leaves", "sapling", "immature-crop", "farmland", "fish", "obsidian"),
                errors,
                ref order) != null)
        {
            Assert.Fail("[prosequor] Compiler fixture failed (unknown action should reject).");
        }

        errors.Clear();
        if (AbilityRuleCompiler.CompileEffects(
                "fixture",
                null,
                null,
                [
                    new AbilityEffectJson
                    {
                        hook = "prosequor:block-interaction",
                    verb = "prosequor:interaction-speed",
                        action = "prosequor:number",
                        when = new AbilityWhenJson { tags = ["target:<not-a-real-collection>"] },
                        @params = new JObject { ["op"] = "scale", ["base"] = 0, ["perSkillLevel"] = 0.001 }
                    }
                ],
                hooks,
                actions,
                NewCollections("soil"),
                errors,
                ref order) != null)
        {
            Assert.Fail("[prosequor] Compiler fixture failed (unknown tag should reject).");
        }

        errors.Clear();
        if (AbilityRuleCompiler.CompileEffects(
                "fixture",
                null,
                null,
                [
                    new AbilityEffectJson
                    {
                        hook = "prosequor:block-interaction",
                    verb = "prosequor:interaction-speed",
                        action = "prosequor:number",
                        when = new AbilityWhenJson { tags = ["target:<soil>"] },
                        @params = new JObject { ["op"] = "scale", ["ofBase"] = true, ["base"] = 0, ["perSkillLevel"] = 0.001 }
                    }
                ],
                hooks,
                actions,
                NewCollections("soil", "leaves", "sapling", "immature-crop", "farmland", "fish", "obsidian"),
                errors,
                ref order) != null)
        {
            Assert.Fail("[prosequor] Compiler fixture failed (bad params should reject).");
        }

        errors.Clear();
        if (AbilityRuleCompiler.CompileEffects(
                "fixture",
                null,
                null,
                [
                    new AbilityEffectJson
                    {
                        hook = "prosequor:block-interaction",
                    verb = "prosequor:interaction-speed",
                        action = "prosequor:add-skill-scaled-percent",
                        @params = new JObject { ["base"] = 10, ["perSkillLevel"] = 2, ["cap"] = 30 }
                    }
                ],
                hooks,
                actions,
                NewCollections("soil", "leaves", "sapling", "immature-crop", "farmland", "fish", "obsidian"),
                errors,
                ref order) != null)
        {
            Assert.Fail("[prosequor] Compiler fixture failed (action/phase mismatch should reject).");
        }

        errors.Clear();
        List<AbilityRule>? ok = AbilityRuleCompiler.CompileEffects(
            "fixture",
            null,
            null,
            [
                new AbilityEffectJson
                {
                    hook = "prosequor:block-interaction",
                    verb = "prosequor:interaction-speed",
                    action = "prosequor:number",
                    when = new AbilityWhenJson { tags = ["target:<soil>"] },
                    @params = new JObject { ["op"] = "scale", ["base"] = 0, ["perSkillLevel"] = 0.001 }
                }
            ],
            hooks,
            actions,
            NewCollections("soil", "leaves", "sapling", "immature-crop", "farmland", "fish", "obsidian"),
            errors,
            ref order);
        if (ok == null || ok.Count != 1 || errors.Count > 0)
        {
            Assert.Fail("[prosequor] Compiler fixture failed (valid interaction-speed rule).");
        }

        errors.Clear();
        List<AbilityRule>? taggedOk = AbilityRuleCompiler.CompileEffects(
            "fixture",
            null,
            null,
            [
                new AbilityEffectJson
                {
                    hook = "prosequor:block-interaction",
                    verb = "prosequor:mutate-drops",
                    phase = "quantity",
                    action = "prosequor:number",
                    when = new AbilityWhenJson { tags = ["target:<leaves>", "drop:<sapling>"] },
                    @params = new JObject
                    {
                        ["op"] = "add",
                        ["base"] = 0.10,
                        ["perSkillLevel"] = 0,
                        ["cap"] = 0
                    }
                }
            ],
            hooks,
            actions,
            NewCollections("soil", "leaves", "sapling", "immature-crop", "farmland", "fish", "obsidian"),
            errors,
            ref order);
        if (taggedOk == null || taggedOk.Count != 1 || errors.Count > 0)
        {
            Assert.Fail("[prosequor] Compiler fixture failed (drop-tagged quantity number).");
        }

        errors.Clear();
        List<AbilityRule>? cropSeedOk = AbilityRuleCompiler.CompileEffects(
            "fixture",
            null,
            null,
            [
                new AbilityEffectJson
                {
                    hook = "prosequor:block-interaction",
                    verb = "prosequor:mutate-drops",
                    phase = "stacks",
                    action = "prosequor:add-crop-seed",
                    when = new AbilityWhenJson { tags = ["target:<immature-crop>"] }
                }
            ],
            hooks,
            actions,
            NewCollections("soil", "leaves", "sapling", "immature-crop", "farmland", "fish", "obsidian"),
            errors,
            ref order);
        if (cropSeedOk == null || cropSeedOk.Count != 1 || errors.Count > 0)
        {
            Assert.Fail("[prosequor] Compiler fixture failed (add-crop-seed).");
        }

        errors.Clear();
        List<AbilityRule>? enrichSoilOk = AbilityRuleCompiler.CompileEffects(
            "fixture",
            null,
            null,
            [
                new AbilityEffectJson
                {
                    hook = "prosequor:block-interaction",
                    verb = "prosequor:mutate-drops",
                    phase = "stacks",
                    action = "prosequor:enrich-soil",
                    when = new AbilityWhenJson
                    {
                        tags = ["target:<farmland>"]
                    },
                    @params = new JObject { ["maxFertility"] = 65 }
                }
            ],
            hooks,
            actions,
            NewCollections("soil", "leaves", "sapling", "immature-crop", "farmland", "fish", "obsidian"),
            errors,
            ref order);
        if (enrichSoilOk == null || enrichSoilOk.Count != 1 || errors.Count > 0)
        {
            Assert.Fail("[prosequor] Compiler fixture failed (enrich-soil).");
        }

        errors.Clear();
        if (AbilityRuleCompiler.CompileEffects(
                "fixture",
                null,
                null,
                [
                    new AbilityEffectJson
                    {
                        hook = "prosequor:block-interaction",
                        verb = "prosequor:mutate-drops",
                        phase = "quantity",
                        action = "prosequor:number",
                        when = new AbilityWhenJson { tags = ["target:<leaves>", "drop:<sapling>"] },
                        @params = new JObject { ["base"] = 10, ["perSkillLevel"] = 0 }
                    }
                ],
                hooks,
                actions,
                NewCollections("soil", "leaves", "sapling", "immature-crop", "farmland", "fish", "obsidian"),
                errors,
                ref order) != null)
        {
            Assert.Fail("[prosequor] Compiler fixture failed (number missing op should reject).");
        }
    }

    static void VerifyRuleIndex()
    {
        FixtureSkillRegistry skills = new();
        skills.Register(new SkillDef
        {
            Id = "fixture-index-a",
            Rules =
            [
                new AbilityRule
                {
                    RuleId = "a-speed",
                    Hook = HookIds.BlockInteraction,
                    Verb = VerbIds.InteractionSpeed,
                    Phase = HookIds.Default,
                    Action = ActionIds.Number,
                    When = new AbilityWhenFilter(),
                    Priority = 0,
                    SourceOrder = 0,
                    Source = new AbilityRuleSource { SkillId = "fixture-index-a", NodeId = "n1", Tier = 1 },
                    Parameters = ParseNumberSpec("{\"op\":\"scale\",\"value\":0.1}")
                },
                new AbilityRule
                {
                    RuleId = "a-drops",
                    Hook = HookIds.BlockInteraction,
                    Verb = VerbIds.MutateDrops,
                    Phase = HookIds.Quantity,
                    Action = ActionIds.Number,
                    When = new AbilityWhenFilter(),
                    Priority = 0,
                    SourceOrder = 1,
                    Source = new AbilityRuleSource { SkillId = "fixture-index-a", NodeId = "n1", Tier = 1 },
                    Parameters = ParseNumberSpec("{\"op\":\"scale\",\"value\":0.1}")
                }
            ]
        });
        skills.Register(new SkillDef
        {
            Id = "fixture-index-b",
            Rules =
            [
                new AbilityRule
                {
                    RuleId = "b-speed",
                    Hook = HookIds.BlockInteraction,
                    Verb = VerbIds.InteractionSpeed,
                    Phase = HookIds.Default,
                    Action = ActionIds.Number,
                    When = new AbilityWhenFilter(),
                    Priority = 10,
                    SourceOrder = 2,
                    Source = new AbilityRuleSource { SkillId = "fixture-index-b", NodeId = "n1", Tier = 1 },
                    Parameters = ParseNumberSpec("{\"op\":\"scale\",\"value\":0.2}")
                }
            ]
        });

        AbilityRuleIndex index = AbilityRuleIndex.Build(skills);
        IReadOnlyList<AbilityRule> interactionSpeed = index.Get(HookIds.BlockInteraction, VerbIds.InteractionSpeed, HookIds.Default);
        IReadOnlyList<AbilityRule> dropQuantity = index.Get(HookIds.BlockInteraction, VerbIds.MutateDrops, HookIds.Quantity);
        if (index.TotalRuleCount <= 0
            || interactionSpeed.Count <= 0
            || interactionSpeed.Count >= index.TotalRuleCount)
        {
            Assert.Fail(string.Format("[prosequor] Rule index fixture failed (bucket sizing), total={0}, interaction={1}.",
                index.TotalRuleCount,
                interactionSpeed.Count));
            return;
        }

        if (dropQuantity.Count <= 0)
        {
            Assert.Fail("[prosequor] Rule index fixture failed (drops bucket empty).");
            return;
        }

        for (int i = 1; i < interactionSpeed.Count; i++)
        {
            if (AbilityRuleIndex.CompareExecutionOrder(interactionSpeed[i - 1], interactionSpeed[i]) > 0)
            {
                Assert.Fail("[prosequor] Rule index fixture failed (interaction-speed sort order).");
                return;
            }
        }

        FixtureProgress progress = new();
        ActiveAbilityRuleCache cache = ActiveAbilityRuleCache.Rebuild(index, progress);
        if (cache.TryGet(HookIds.BlockInteraction, VerbIds.InteractionSpeed, HookIds.Default, out IReadOnlyList<AbilityRule> active)
            && active.Count >= interactionSpeed.Count)
        {
            Assert.Fail("[prosequor] Rule index fixture failed (active cache should drop unowned tiers).");
        }
    }

    static void VerifyFieldExpertisePipeline()
    {
        HookRegistry hooks = new();
        AbilityActionRegistry actions = new(hooks);
        AbilityBootstrap.RegisterBuiltIns(hooks, actions);

        if (!hooks.TryGet(HookIds.BlockInteraction, out _)
            || !hooks.TryGet(HookIds.BlockInteraction, out _)
            || !actions.TryGet(
                ActionIds.Number,
                HookIds.BlockInteraction,
                VerbIds.FieldWork,
                HookIds.Size,
                out _)
            || !actions.TryGet(
                ActionIds.Number,
                HookIds.BlockInteraction,
                VerbIds.ScytheMultibreak,
                HookIds.Quantity,
                out _))
        {
            Assert.Fail("[prosequor] Field Expertise fixture failed (hook/action registration).");
            return;
        }

        FixtureSkillRegistry skills = new();
        skills.Register(new SkillDef
        {
            Id = "farming",
            Rules =
            [
                new AbilityRule
                {
                    RuleId = "field-t1",
                    Hook = HookIds.BlockInteraction,
                    Verb = VerbIds.FieldWork,
                    Phase = HookIds.Size,
                    Action = ActionIds.Number,
                    When = new AbilityWhenFilter(),
                    Parameters = ParseNumberSpec("{\"op\":\"set\",\"value\":2}"),
                    Source = new AbilityRuleSource
                    {
                        SkillId = "farming",
                        NodeId = "fieldexpertise",
                        Tier = 1
                    },
                    Priority = 0,
                    SourceOrder = 1
                },
                new AbilityRule
                {
                    RuleId = "field-t2",
                    Hook = HookIds.BlockInteraction,
                    Verb = VerbIds.FieldWork,
                    Phase = HookIds.Size,
                    Action = ActionIds.Number,
                    When = new AbilityWhenFilter(),
                    Parameters = ParseNumberSpec("{\"op\":\"set\",\"value\":3}"),
                    Source = new AbilityRuleSource
                    {
                        SkillId = "farming",
                        NodeId = "fieldexpertise",
                        Tier = 2
                    },
                    Priority = 0,
                    SourceOrder = 2
                },
                new AbilityRule
                {
                    RuleId = "scythe-t1",
                    Hook = HookIds.BlockInteraction,
                    Verb = VerbIds.ScytheMultibreak,
                    Phase = HookIds.Quantity,
                    Action = ActionIds.Number,
                    When = new AbilityWhenFilter(),
                    Parameters = ParseNumberSpec("{\"op\":\"scale\",\"base\":0.4,\"perSkillLevel\":0,\"cap\":0}"),
                    Source = new AbilityRuleSource
                    {
                        SkillId = "farming",
                        NodeId = "fieldexpertise",
                        Tier = 1
                    },
                    Priority = 0,
                    SourceOrder = 3
                },
                new AbilityRule
                {
                    RuleId = "scythe-t2",
                    Hook = HookIds.BlockInteraction,
                    Verb = VerbIds.ScytheMultibreak,
                    Phase = HookIds.Quantity,
                    Action = ActionIds.Number,
                    When = new AbilityWhenFilter(),
                    Parameters = ParseNumberSpec("{\"op\":\"scale\",\"base\":0.8,\"perSkillLevel\":0,\"cap\":0}"),
                    Source = new AbilityRuleSource
                    {
                        SkillId = "farming",
                        NodeId = "fieldexpertise",
                        Tier = 2
                    },
                    Priority = 0,
                    SourceOrder = 4
                }
            ]
        });

        AbilityPipeline pipeline = new(actions, skills);
        FixtureProgress progress = new();
        progress.SetSkillLevel("farming", 50);
        progress.SetUnlockTier("farming", "fieldexpertise", 1);

        FieldWorkContext areaCtx = new()
        {
            Progress = progress,
            Fact = new AbilityAction
            {
                Verb = AbilityBootstrap.VerbFieldWork,
                ActorUid = "fixture"
            },
            BaseValue = 1
        };
        int area = pipeline.Run(HookIds.BlockInteraction, VerbIds.FieldWork, HookIds.Size, areaCtx, 1);
        if (area != 2)
        {
            Assert.Fail(string.Format("[prosequor] Field Expertise fixture failed (tier1 area). Got {0}, expected 2.",
                area));
            return;
        }

        ScytheMultiBreakContext breakCtx = new()
        {
            Progress = progress,
            Fact = new AbilityAction
            {
                Verb = AbilityBootstrap.VerbScytheMultibreak,
                ActorUid = "fixture"
            },
            BaseValue = 5
        };
        int qty = pipeline.Run(HookIds.BlockInteraction, VerbIds.ScytheMultibreak, HookIds.Quantity, breakCtx, 5);
        if (qty != 7)
        {
            Assert.Fail(string.Format("[prosequor] Field Expertise fixture failed (tier1 multi-break). Got {0}, expected 7.",
                qty));
            return;
        }

        progress.SetUnlockTier("farming", "fieldexpertise", 2);
        area = pipeline.Run(HookIds.BlockInteraction, VerbIds.FieldWork, HookIds.Size, areaCtx, 1);
        qty = pipeline.Run(HookIds.BlockInteraction, VerbIds.ScytheMultibreak, HookIds.Quantity, breakCtx, 5);
        if (area != 3 || qty != 9)
        {
            Assert.Fail(string.Format("[prosequor] Field Expertise fixture failed (tier2). area={0} (want 3), qty={1} (want 9).",
                area,
                qty));
        }
    }

    static void VerifyVoxelWorkPipeline()
    {
        HookRegistry hooks = new();
        AbilityActionRegistry actions = new(hooks);
        AbilityBootstrap.RegisterBuiltIns(hooks, actions);

        if (!hooks.TryGet(HookIds.ItemInteraction, out _)
            || !actions.TryGet(
                ActionIds.Number,
                HookIds.ItemInteraction,
                VerbIds.ClayForm,
                HookIds.AssistRadius,
                out _)
            || !actions.TryGet(
                ActionIds.Number,
                HookIds.ItemInteraction,
                VerbIds.VoxelCopy,
                HookIds.Default,
                out _)
            || !actions.TryGet(
                ActionIds.Number,
                HookIds.ItemInteraction,
                VerbIds.VoxelRefill,
                HookIds.Default,
                out _)
            || !actions.TryGet(
                ActionIds.Number,
                HookIds.ItemInteraction,
                VerbIds.ClayForm,
                HookIds.AutoFinish,
                out _)
            || !actions.TryGet(
                ActionIds.Number,
                HookIds.ItemInteraction,
                VerbIds.ClayForm,
                HookIds.PlaceConservation,
                out _))
        {
            Assert.Fail("[prosequor] Voxel-work fixture failed (hook/action registration).");
            return;
        }

        FixtureSkillRegistry skills = new();
        skills.Register(new SkillDef
        {
            Id = "clayforming",
            Rules =
            [
                new AbilityRule
                {
                    RuleId = "place-conservation",
                    Hook = HookIds.ItemInteraction,
                    Verb = VerbIds.ClayForm,
                    Phase = HookIds.PlaceConservation,
                    Action = ActionIds.Number,
                    When = CompileWhen(NewCollections(), AbilityBootstrap.OpPlaceTag),
                    Parameters = ParseNumberSpec("{\"op\":\"add\",\"base\":0,\"perSkillLevel\":0.001,\"cap\":0}"),
                    Source = new AbilityRuleSource { SkillId = "clayforming" },
                    Priority = 0,
                    SourceOrder = 0
                },
                new AbilityRule
                {
                    RuleId = "refill",
                    Hook = HookIds.ItemInteraction,
                    Verb = VerbIds.VoxelRefill,
                    Phase = HookIds.Default,
                    Action = ActionIds.Number,
                    When = new AbilityWhenFilter(),
                    Parameters = ParseNumberSpec("{\"op\":\"set\",\"value\":3}"),
                    Source = new AbilityRuleSource
                    {
                        SkillId = "clayforming",
                        NodeId = "claysaver",
                        Tier = 1
                    },
                    Priority = 0,
                    SourceOrder = 1
                },
                new AbilityRule
                {
                    RuleId = "radius",
                    Hook = HookIds.ItemInteraction,
                    Verb = VerbIds.ClayForm,
                    Phase = HookIds.AssistRadius,
                    Action = ActionIds.Number,
                    When = CompileWhen(NewCollections(), AbilityBootstrap.OpPlaceTag),
                    Parameters = ParseNumberSpec("{\"op\":\"set\",\"value\":0}"),
                    Source = new AbilityRuleSource
                    {
                        SkillId = "clayforming",
                        NodeId = "carefulhand",
                        Tier = 1
                    },
                    Priority = 0,
                    SourceOrder = 2
                },
                new AbilityRule
                {
                    RuleId = "copy",
                    Hook = HookIds.ItemInteraction,
                    Verb = VerbIds.VoxelCopy,
                    Phase = HookIds.Default,
                    Action = ActionIds.Number,
                    When = new AbilityWhenFilter(),
                    Parameters = ParseNumberSpec("{\"op\":\"set\",\"value\":1}"),
                    Source = new AbilityRuleSource
                    {
                        SkillId = "clayforming",
                        NodeId = "quicklayer",
                        Tier = 1
                    },
                    Priority = 0,
                    SourceOrder = 3
                },
                new AbilityRule
                {
                    RuleId = "finish",
                    Hook = HookIds.ItemInteraction,
                    Verb = VerbIds.ClayForm,
                    Phase = HookIds.AutoFinish,
                    Action = ActionIds.Number,
                    When = CompileWhen(NewCollections(), AbilityBootstrap.OpFinishTag),
                    Parameters = ParseNumberSpec("{\"op\":\"add\",\"base\":0.01,\"perSkillLevel\":0.001,\"cap\":0.02}"),
                    Source = new AbilityRuleSource
                    {
                        SkillId = "clayforming",
                        NodeId = "flowstate",
                        Tier = 1
                    },
                    Priority = 0,
                    SourceOrder = 4
                }
            ]
        });

        AbilityPipeline pipeline = new(actions, skills);
        FixtureProgress progress = new();
        progress.SetSkillLevel("clayforming", 50);
        progress.SetUnlockTier("clayforming", "claysaver", 1);
        progress.SetUnlockTier("clayforming", "carefulhand", 1);
        progress.SetUnlockTier("clayforming", "quicklayer", 1);
        progress.SetUnlockTier("clayforming", "flowstate", 1);

        VoxelWorkContext Ctx(string opTag) => new()
        {
            World = null!,
            Progress = progress,
            Fact = new AbilityAction
            {
                Verb = AbilityBootstrap.VerbClayForm,
                ActorUid = "fixture",
                Op = VoxelWorkStation.NormalizeOp(opTag)
            }
        };

        int refill = pipeline.Run(HookIds.ItemInteraction, VerbIds.VoxelRefill, HookIds.Default, new VoxelWorkContext
            {
                World = null!,
                Progress = progress,
                Fact = new AbilityAction
                {
                    Verb = VerbIds.VoxelRefill.Value,
                    ActorUid = "fixture"
                }
            },
            0);
        int radius = pipeline.Run(HookIds.ItemInteraction, VerbIds.ClayForm, HookIds.AssistRadius, Ctx(AbilityBootstrap.OpPlaceTag),
            VoxelWorkStation.AssistRadiusOff);
        int copy = pipeline.Run(HookIds.ItemInteraction, VerbIds.VoxelCopy, HookIds.Default, new VoxelWorkContext
            {
                World = null!,
                Progress = progress,
                Fact = new AbilityAction
                {
                    Verb = VerbIds.VoxelCopy.Value,
                    ActorUid = "fixture"
                }
            },
            0);
        float finish = pipeline.Run(HookIds.ItemInteraction, VerbIds.ClayForm, HookIds.AutoFinish, Ctx(AbilityBootstrap.OpFinishTag),
            0f);
        float conserve = pipeline.Run(HookIds.ItemInteraction, VerbIds.ClayForm, HookIds.PlaceConservation, Ctx(AbilityBootstrap.OpPlaceTag),
            0f);

        if (refill != 3 || radius != 0 || copy != 1 || Math.Abs(finish - 0.02f) > 0.0001f
            || Math.Abs(conserve - 0.05f) > 0.0001f)
        {
            Assert.Fail(string.Format("[prosequor] Voxel-work fixture failed. refill={0} radius={1} copy={2} finish={3} conserve={4} (want 3/0/1/0.02/0.05).",
                refill,
                radius,
                copy,
                finish,
                conserve));
        }
    }

    static void VerifyOnProcessedPipeline()
    {
        HookRegistry hooks = new();
        AbilityActionRegistry actions = new(hooks);
        AbilityBootstrap.RegisterBuiltIns(hooks, actions);

        if (!hooks.TryGet(HookIds.BlockInteraction, out _)
            || !actions.TryGet(
                ActionIds.Number,
                HookIds.BlockInteraction,
                VerbIds.MutateProcess,
                HookIds.Quantity,
                out _)
            || !actions.TryGet(
                ActionIds.Chance,
                HookIds.BlockInteraction,
                VerbIds.MutateProcess,
                HookIds.Stacks,
                out _)
            || !actions.TryGet(
                ActionIds.ReplaceWithVariant,
                HookIds.BlockInteraction,
                VerbIds.MutateProcess,
                HookIds.Stacks,
                out _))
        {
            Assert.Fail("[prosequor] On-processed fixture failed (hook/action registration).");
            return;
        }

        int order = 0;
        List<string> errors = new();
        List<AbilityRule>? rules = AbilityRuleCompiler.CompileEffects(
            "clayforming",
            "makersmark",
            1,
            [
                new AbilityEffectJson
                {
                    hook = "prosequor:block-interaction",
                    verb = "prosequor:mutate-process",
                    phase = "stacks",
                    action = "prosequor:chance",
                    when = new AbilityWhenJson { tags = ["fire-pottery"] },
                    @params = JObject.Parse(
                        """
                        {
                          "chance": { "base": 0.1, "perSkillLevel": 0, "cap": 0 },
                          "onSuccess": {
                            "action": "prosequor:replace-with-variant",
                            "params": { "variant": "decorative" }
                          }
                        }
                        """)
                }
            ],
            hooks,
            actions,
            NewCollections(),
            errors,
            ref order);
        if (rules == null || rules.Count != 1 || errors.Count > 0
            || rules[0].Parameters is not ChanceGateParams gate
            || gate.OnSuccess == null)
        {
            Assert.Fail(string.Format(
                "[prosequor] On-processed fixture failed (makers-mark compile): {0}",
                string.Join("; ", errors)));
            return;
        }

        FixtureProgress progress = new();
        progress.SetSkillLevel("clayforming", 10);
        progress.SetUnlockTier("clayforming", "makersmark", 1);

        MutateProcessContext context = new()
        {
            World = null!,
            OutputSlot = new DummySlot(),
            Progress = progress,
            Fact = new AbilityAction
            {
                Verb = VerbIds.MutateProcess.Value,
                ActorUid = "fixture",
                Target = "game:storagevessel-fired",
                Tokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "fire-pottery" }
            },
            Tags = new CollectionIndex(),
            Variants = new CollectibleVariantTable()
        };

        float chance = gate.Chance.Evaluate(context, rules[0].Source);
        if (Math.Abs(chance - 0.10f) > 0.0001f)
        {
            Assert.Fail(string.Format("[prosequor] On-processed fixture failed. chance={0} (want 0.10).",
                chance));
        }

        CollectionIndex leatherCollections = WithCodes(
            NewCollections(AbilityBootstrap.LeatherTag),
            (AbilityBootstrap.LeatherTag, "game:leather-normal-plain"));

        FixtureSkillRegistry skills = new();
        skills.Register(new SkillDef
        {
            Id = "tailoring",
            Rules =
            [
                new AbilityRule
                {
                    RuleId = "tanning",
                    Hook = HookIds.BlockInteraction,
                    Verb = VerbIds.MutateProcess,
                    Phase = HookIds.Quantity,
                    Action = ActionIds.Number,
                    When = CompileWhen(
                        leatherCollections,
                        "barrel",
                        "target:<leather>"),
                    Parameters = ParseNumberSpec(
                        "{\"op\":\"scale\",\"base\":0.15,\"perSkillLevel\":0,\"cap\":0.15}"),
                    Source = new AbilityRuleSource
                    {
                        SkillId = "tailoring",
                        NodeId = "tanning",
                        Tier = 1
                    },
                    Priority = 0,
                    SourceOrder = 0
                }
            ]
        });

        AbilityPipeline pipeline = new(actions, skills);
        FixtureProgress tanningProgress = new();
        tanningProgress.SetSkillLevel("tailoring", 1);
        tanningProgress.SetUnlockTier("tailoring", "tanning", 1);

        MutateProcessContext tanningContext = new()
        {
            World = null!,
            OutputSlot = new DummySlot(),
            Progress = tanningProgress,
            Fact = new AbilityAction
            {
                Verb = VerbIds.MutateProcess.Value,
                ActorUid = "fixture",
                Target = "game:leather-normal-plain",
                Tokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "barrel" }
            },
            Tags = leatherCollections,
            Variants = new CollectibleVariantTable()
        };

        float scaled = pipeline.Run(
            HookIds.BlockInteraction,
            VerbIds.MutateProcess,
            HookIds.Quantity,
            tanningContext,
            100f);
        if (Math.Abs(scaled - 115f) > 0.0001f)
        {
            Assert.Fail(string.Format(
                "[prosequor] On-processed fixture failed (tanning). got={0} want=115.",
                scaled));
        }

        MutateProcessContext hideContext = new()
        {
            World = null!,
            OutputSlot = new DummySlot(),
            Progress = tanningProgress,
            Fact = new AbilityAction
            {
                Verb = VerbIds.MutateProcess.Value,
                ActorUid = "fixture",
                Target = "game:hide-prepared-large-plain",
                Tokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "barrel" }
            },
            Tags = leatherCollections,
            Variants = new CollectibleVariantTable()
        };

        float hideScaled = pipeline.Run(
            HookIds.BlockInteraction,
            VerbIds.MutateProcess,
            HookIds.Quantity,
            hideContext,
            100f);
        if (Math.Abs(hideScaled - 100f) > 0.0001f)
        {
            Assert.Fail(string.Format(
                "[prosequor] On-processed fixture failed (hide isolated from tanning). got={0} want=100.",
                hideScaled));
        }
    }

    static void VerifyMealEatMods()
    {
        ItemStack meal = new() { StackSize = 1 };
        meal.Attributes = new Vintagestory.API.Datastructures.TreeAttribute();
        CraftAttributeMods.SetFactor(meal, SatietyAttributeMutator.KeyName, 1.25f);
        CraftAttributeMods.SetFactor(meal, HungerDelayAttributeMutator.KeyName, 1.25f);

        float saturation = 100f;
        float delay = 40f;
        float nutritionMul = 1f;
        MealEatMods.Apply(meal, ref saturation, ref delay, ref nutritionMul);

        if (Math.Abs(saturation - 125f) > 0.0001f
            || Math.Abs(nutritionMul - 0.8f) > 0.0001f
            || Math.Abs(delay - 50f) > 0.0001f)
        {
            Assert.Fail(string.Format(
                "[prosequor] Meal-eat mods fixture failed. sat={0} nutriMul={1} delay={2}.",
                saturation,
                nutritionMul,
                delay));
            return;
        }

        // Nutrition intake proxy: sat * nutriMul unchanged vs baseline 100 * 1.
        if (Math.Abs(saturation * nutritionMul - 100f) > 0.0001f)
        {
            Assert.Fail("[prosequor] Meal-eat mods fixture failed (nutrition product should stay 100).");
        }

        float untouchedSat = 100f;
        float untouchedDelay = 40f;
        float untouchedMul = 1f;
        MealEatMods.Apply(null, ref untouchedSat, ref untouchedDelay, ref untouchedMul);
        if (Math.Abs(untouchedSat - 100f) > 0.0001f
            || Math.Abs(untouchedDelay - 40f) > 0.0001f
            || Math.Abs(untouchedMul - 1f) > 0.0001f)
        {
            Assert.Fail("[prosequor] Meal-eat mods fixture failed (null meal should no-op).");
        }
    }

    static void VerifyCookServePedigreeCopy()
    {
        ItemStack pot = new() { StackSize = 1 };
        pot.Attributes = new Vintagestory.API.Datastructures.TreeAttribute();
        CraftAttribution.StampMakerUid(pot, "cooker-uid");
        ProsequorStackPedigree.StampQualityRank(pot, 7);
        CraftAttributeMods.SetFactor(pot, FreshnessAttributeMutator.KeyName, 1.25f);

        if (!ProsequorStackPedigree.TryGetPrimaryBlob(pot, out ProsequorBlob potBlob)
            || !potBlob.HasPersistable)
        {
            Assert.Fail("[prosequor] Cook-serve fixture failed (pot blob missing).");
            return;
        }

        ItemStack meal = new() { StackSize = 1 };
        meal.Attributes = new Vintagestory.API.Datastructures.TreeAttribute();
        ProsequorStackPedigree.ApplyUnitBlob(meal, potBlob);

        if (!string.Equals(CraftAttribution.TryGetMakerUid(meal), "cooker-uid", StringComparison.Ordinal)
            || !ProsequorStackPedigree.TryGetQualityRank(meal, out int rank)
            || rank != 7
            || Math.Abs(CraftAttributeMods.GetFactor(meal, FreshnessAttributeMutator.KeyName) - 1.25f) > 0.0001f)
        {
            Assert.Fail(string.Format(
                "[prosequor] Cook-serve fixture failed (meal copy). maker={0} rank={1} freshness={2}.",
                CraftAttribution.TryGetMakerUid(meal) ?? "(null)",
                ProsequorStackPedigree.TryGetQualityRank(meal, out int r) ? r : 0,
                CraftAttributeMods.GetFactor(meal, FreshnessAttributeMutator.KeyName)));
        }
    }

    static void VerifyPerfectPresserPipeline()
    {
        HookRegistry hooks = new();
        AbilityActionRegistry actions = new(hooks);
        AbilityBootstrap.RegisterBuiltIns(hooks, actions);

        CollectionIndex juiceCollections = WithCodes(
            NewCollections("juice"),
            ("juice", "game:juiceportion-apple"));

        FixtureSkillRegistry skills = new();
        skills.Register(new SkillDef
        {
            Id = "cooking",
            Rules =
            [
                new AbilityRule
                {
                    RuleId = "perfect-presser",
                    Hook = HookIds.BlockInteraction,
                    Verb = VerbIds.MutateProcess,
                    Phase = HookIds.Quantity,
                    Action = ActionIds.Number,
                    When = CompileWhen(
                        juiceCollections,
                        AbilityBootstrap.TokenFruitPress,
                        "target:<juice>"),
                    Parameters = ParseNumberSpec(
                        "{\"op\":\"scale\",\"base\":0,\"perSkillLevel\":0.0025,\"cap\":0.2}"),
                    Source = new AbilityRuleSource
                    {
                        SkillId = "cooking",
                        NodeId = "perfect-presser",
                        Tier = 1
                    },
                    Priority = 0,
                    SourceOrder = 0
                }
            ]
        });

        AbilityPipeline pipeline = new(actions, skills);
        FixtureProgress progress = new();
        progress.SetSkillLevel("cooking", 20);
        progress.SetUnlockTier("cooking", "perfect-presser", 1);

        MutateProcessContext juiceContext = new()
        {
            World = null!,
            OutputSlot = new DummySlot(),
            Progress = progress,
            Fact = new AbilityAction
            {
                Verb = VerbIds.MutateProcess.Value,
                ActorUid = "fixture",
                Target = "game:juiceportion-apple",
                Tokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    AbilityBootstrap.TokenFruitPress
                }
            },
            Tags = juiceCollections,
            Variants = new CollectibleVariantTable()
        };

        // level 20 × 0.0025 = 0.05 → seed * 1.05
        float scaled = pipeline.Run(
            HookIds.BlockInteraction,
            VerbIds.MutateProcess,
            HookIds.Quantity,
            juiceContext,
            1f);
        if (Math.Abs(scaled - 1.05f) > 0.0001f)
        {
            Assert.Fail(string.Format(
                "[prosequor] Perfect Presser fixture failed (juice). got={0} want=1.05.",
                scaled));
        }

        MutateProcessContext ciderContext = new()
        {
            World = null!,
            OutputSlot = new DummySlot(),
            Progress = progress,
            Fact = new AbilityAction
            {
                Verb = VerbIds.MutateProcess.Value,
                ActorUid = "fixture",
                Target = "game:ciderportion-apple",
                Tokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    AbilityBootstrap.TokenFruitPress
                }
            },
            Tags = juiceCollections,
            Variants = new CollectibleVariantTable()
        };

        float ciderScaled = pipeline.Run(
            HookIds.BlockInteraction,
            VerbIds.MutateProcess,
            HookIds.Quantity,
            ciderContext,
            1f);
        if (Math.Abs(ciderScaled - 1f) > 0.0001f)
        {
            Assert.Fail(string.Format(
                "[prosequor] Perfect Presser fixture failed (cider isolated). got={0} want=1.",
                ciderScaled));
        }
    }

    static void VerifyMountedPipeline()
    {
        HookRegistry hooks = new();
        AbilityActionRegistry actions = new(hooks);
        AbilityBootstrap.RegisterBuiltIns(hooks, actions);

        if (!hooks.TryGet(HookIds.EntityInteraction, out _)
            || !actions.TryGet(
                ActionIds.Number,
                HookIds.EntityInteraction,
                VerbIds.Mounted,
                HookIds.MoveSpeed,
                out _)
            || !actions.TryGet(
                ActionIds.Number,
                HookIds.EntityInteraction,
                VerbIds.Mounted,
                HookIds.MoveSpeed,
                out _)
            || !actions.TryGet(
                ActionIds.AllowMountedRideWithoutSaddle,
                HookIds.EntityInteraction,
                VerbIds.Mounted,
                HookIds.CanRide,
                out _)
            || !actions.TryGet(
                ActionIds.Number,
                HookIds.EntityInteraction,
                VerbIds.Mounted,
                HookIds.SaddleBreak,
                out _)
            || !actions.TryGet(
                ActionIds.Number,
                HookIds.EntityInteraction,
                VerbIds.Mounted,
                HookIds.HungerRate,
                out _)
            || !actions.TryGet(
                ActionIds.Number,
                HookIds.EntityInteraction,
                VerbIds.Mounted,
                HookIds.FallDamage,
                out _)
            || !actions.TryGet(
                ActionIds.Number,
                HookIds.EntityInteraction,
                VerbIds.Mounted,
                HookIds.MeleeDamage,
                out _))
        {
            Assert.Fail("[prosequor] Mounted fixture failed (hook/action registration).");
            return;
        }

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
                    Parameters = ParseNumberSpec("{\"op\":\"add\",\"base\":0,\"perSkillLevel\":0.002,\"cap\":0.1}"),
                    Source = new AbilityRuleSource { SkillId = "riding" },
                    Priority = 0,
                    SourceOrder = 0
                },
                new AbilityRule
                {
                    RuleId = "tireless-trot",
                    Hook = HookIds.EntityInteraction,
                    Verb = VerbIds.Mounted,
                    Phase = HookIds.MoveSpeed,
                    Action = ActionIds.Number,
                    When = new AbilityWhenFilter(),
                    Parameters = ParseNumberSpec("{\"op\":\"set\",\"base\":1,\"perSkillLevel\":0.0025,\"cap\":1.2}"),
                    Source = new AbilityRuleSource
                    {
                        SkillId = "riding",
                        NodeId = "tirelesstrot",
                        Tier = 1
                    },
                    Priority = 0,
                    SourceOrder = 1
                },
                new AbilityRule
                {
                    RuleId = "light-tack",
                    Hook = HookIds.EntityInteraction,
                    Verb = VerbIds.Mounted,
                    Phase = HookIds.CanRide,
                    Action = ActionIds.AllowMountedRideWithoutSaddle,
                    When = new AbilityWhenFilter(),
                    Parameters = new object(),
                    Source = new AbilityRuleSource
                    {
                        SkillId = "riding",
                        NodeId = "lighttack",
                        Tier = 1
                    },
                    Priority = 0,
                    SourceOrder = 2
                },
                new AbilityRule
                {
                    RuleId = "mount-breaking",
                    Hook = HookIds.EntityInteraction,
                    Verb = VerbIds.Mounted,
                    Phase = HookIds.SaddleBreak,
                    Action = ActionIds.Number,
                    When = new AbilityWhenFilter(),
                    Parameters = ParseNumberSpec("{\"op\":\"add\",\"value\":0.15}"),
                    Source = new AbilityRuleSource
                    {
                        SkillId = "riding",
                        NodeId = "mountbreaking",
                        Tier = 1
                    },
                    Priority = 0,
                    SourceOrder = 3
                }
            ]
        });

        AbilityPipeline pipeline = new(actions, skills);
        FixtureProgress progress = new();
        progress.SetSkillLevel("riding", 50);

        MountedContext context = new()
        {
            Progress = progress,
            Fact = new AbilityAction
            {
                Verb = MountedStation.VerbMounted,
                ActorUid = "fixture"
            }
        };

        float rootSpeed = pipeline.Run(HookIds.EntityInteraction, VerbIds.Mounted, HookIds.MoveSpeed, context, 1f);
        // 0.2% * 50 = 10%, capped at 10% → 1.10
        if (Math.Abs(rootSpeed - 1.10f) > 0.0001f)
        {
            Assert.Fail(string.Format("[prosequor] Mounted fixture failed (root speed). got={0} want=1.10.",
                rootSpeed));
        }

        progress.SetUnlockTier("riding", "tirelesstrot", 1);
        float upgraded = pipeline.Run(HookIds.EntityInteraction, VerbIds.Mounted, HookIds.MoveSpeed, context, 1f);
        // set replaces: 0.25% * 50 = 12.5% → 1.125
        if (Math.Abs(upgraded - 1.125f) > 0.0001f)
        {
            Assert.Fail(string.Format("[prosequor] Mounted fixture failed (tireless trot). got={0} want=1.125.",
                upgraded));
        }

        progress.SetUnlockTier("riding", "lighttack", 1);
        float canRide = pipeline.Run(HookIds.EntityInteraction, VerbIds.Mounted, HookIds.CanRide, context, 0f);
        if (Math.Abs(canRide - 1f) > 0.0001f)
        {
            Assert.Fail(string.Format("[prosequor] Mounted fixture failed (can-ride). got={0} want=1.",
                canRide));
        }

        progress.SetUnlockTier("riding", "mountbreaking", 1);
        float saddle = pipeline.Run(HookIds.EntityInteraction, VerbIds.Mounted, HookIds.SaddleBreak, context, 0f);
        if (Math.Abs(saddle - 0.15f) > 0.0001f)
        {
            Assert.Fail(string.Format("[prosequor] Mounted fixture failed (saddle-break). got={0} want=0.15.",
                saddle));
        }
    }

    static void VerifyAnimalBehaviorRegistration(HookRegistry hooks, AbilityActionRegistry actions)
    {
        if (!hooks.TryGet(HookIds.EntityInteraction, out _)
            || !actions.TryGet(
                ActionIds.AllowAnimalPet,
                HookIds.EntityInteraction,
                VerbIds.AnimalPet,
                HookIds.Default,
                out _)
            || !actions.TryGet(
                ActionIds.Number,
                HookIds.EntityInteraction,
                VerbIds.AnimalFlee,
                HookIds.Multiplier,
                out _)
            || !actions.TryGet(
                ActionIds.AddFriendliness, HookIds.EntityInteraction, VerbIds.AnimalPet, HookIds.Default,
                out _)
            || !actions.TryGet(
                ActionIds.Number,
                HookIds.EntityInteraction,
                VerbIds.AnimalFlee,
                HookIds.Chance,
                out _))
        {
            Assert.Fail("[prosequor] Animal-behavior fixture failed (hook/action registration).");
        }
    }

    static void VerifyComposeMemo()
    {
        HookRegistry hooks = new();
        AbilityActionRegistry actions = new(hooks);
        AbilityBootstrap.RegisterBuiltIns(hooks, actions);
        actions.Register(new FixtureAddMountedAction("prosequor:fixture-grass-bonus"));

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
                    Parameters = ParseNumberSpec("{\"op\":\"add\",\"base\":0,\"perSkillLevel\":0.002,\"cap\":0.2}"),
                    Source = new AbilityRuleSource { SkillId = "riding" },
                    Priority = 0,
                    SourceOrder = 0
                },
                new AbilityRule
                {
                    RuleId = "grass-bonus",
                    Hook = HookIds.EntityInteraction,
                    Verb = VerbIds.Mounted,
                    Phase = HookIds.MoveSpeed,
                    Action = new ActionId("prosequor:fixture-grass-bonus"),
                    When = CompileWhen(NewCollections(), "target:grass"),
                    Parameters = 0.05f,
                    Source = new AbilityRuleSource { SkillId = "riding" },
                    Priority = 0,
                    SourceOrder = 1
                },
                new AbilityRule
                {
                    RuleId = "chance-qty",
                    Hook = HookIds.BlockInteraction,
                    Verb = VerbIds.MutateDrops,
                    Phase = HookIds.Quantity,
                    Action = ActionIds.Chance,
                    When = new AbilityWhenFilter(),
                    Parameters = BuildAlwaysChancePassThrough(
                        actions,
                        HookIds.BlockInteraction,
                        VerbIds.MutateDrops),
                    Source = new AbilityRuleSource { SkillId = "riding" },
                    Priority = 0,
                    SourceOrder = 2
                }
            ]
        });

        AbilityPipeline pipeline = new(actions, skills);
        MemoFixtureProgress progress = new();
        progress.SetSkillLevel("riding", 50);

        AbilityAction bareFact = new()
        {
            Verb = MountedStation.VerbMounted,
            ActorUid = "fixture"
        };
        AbilityAction grassFact = new()
        {
            Verb = MountedStation.VerbMounted,
            ActorUid = "fixture",
            Target = "grass"
        };

        MountedContext context = new() { Progress = progress, Fact = bareFact };
        float first = pipeline.Run(HookIds.EntityInteraction, VerbIds.Mounted, HookIds.MoveSpeed, context, 1f);
        float second = pipeline.Run(HookIds.EntityInteraction, VerbIds.Mounted, HookIds.MoveSpeed, context, 1f);
        if (progress.ComposeMemo.HitCount < 1 || progress.ComposeMemo.MissCount != 1)
        {
            Assert.Fail(string.Format("[prosequor] Compose memo fixture failed (repeat hit). hits={0} misses={1}.",
                progress.ComposeMemo.HitCount,
                progress.ComposeMemo.MissCount));
            return;
        }

        if (Math.Abs(first - second) > 0.0001f || Math.Abs(first - 1.10f) > 0.0001f)
        {
            Assert.Fail(string.Format("[prosequor] Compose memo fixture failed (repeat value). got={0}.",
                first));
            return;
        }

        int hitsBeforeGrass = progress.ComposeMemo.HitCount;
        int missesBeforeGrass = progress.ComposeMemo.MissCount;
        context = new MountedContext { Progress = progress, Fact = grassFact };
        float withGrass = pipeline.Run(HookIds.EntityInteraction, VerbIds.Mounted, HookIds.MoveSpeed, context, 1f);
        float grassAgain = pipeline.Run(HookIds.EntityInteraction, VerbIds.Mounted, HookIds.MoveSpeed, context, 1f);
        if (progress.ComposeMemo.MissCount != missesBeforeGrass + 1
            || progress.ComposeMemo.HitCount != hitsBeforeGrass + 1)
        {
            Assert.Fail(string.Format("[prosequor] Compose memo fixture failed (grass key). hits={0} misses={1}.",
                progress.ComposeMemo.HitCount,
                progress.ComposeMemo.MissCount));
            return;
        }

        if (Math.Abs(withGrass - 1.15f) > 0.0001f || Math.Abs(grassAgain - withGrass) > 0.0001f)
        {
            Assert.Fail(string.Format("[prosequor] Compose memo fixture failed (grass value). got={0} want=1.15.",
                withGrass));
            return;
        }

        progress.SetSkillLevel("riding", 60);
        context = new MountedContext { Progress = progress, Fact = bareFact };
        float afterLevel = pipeline.Run(HookIds.EntityInteraction, VerbIds.Mounted, HookIds.MoveSpeed, context, 1f);
        if (Math.Abs(afterLevel - 1.12f) > 0.0001f)
        {
            Assert.Fail(string.Format("[prosequor] Compose memo fixture failed (revision invalidate). got={0} want=1.12.",
                afterLevel));
            return;
        }

        int hitsBeforeChance = progress.ComposeMemo.HitCount;
        DropsContext dropCtx = new()
        {
            World = null!,
            Progress = progress,
            Fact = new AbilityAction { Verb = "prosequor:test", ActorUid = "fixture" },
            Tags = new CollectionIndex()
        };
        pipeline.Run(HookIds.BlockInteraction, VerbIds.MutateDrops, HookIds.Quantity, dropCtx, 1f);
        pipeline.Run(HookIds.BlockInteraction, VerbIds.MutateDrops, HookIds.Quantity, dropCtx, 1f);
        if (progress.ComposeMemo.HitCount != hitsBeforeChance)
        {
            Assert.Fail("[prosequor] Compose memo fixture failed (chance must not memo).");
        }
    }

    static void VerifyBoatingPipeline()
    {
        HookRegistry hooks = new();
        AbilityActionRegistry actions = new(hooks);
        AbilityBootstrap.RegisterBuiltIns(hooks, actions);

        if (!hooks.TryGet(HookIds.EntityInteraction, out _)
            || !actions.TryGet(
                ActionIds.Number,
                HookIds.EntityInteraction,
                VerbIds.Mounted,
                HookIds.MoveSpeed,
                out _)
            || !actions.TryGet(
                ActionIds.Number,
                HookIds.EntityInteraction,
                VerbIds.Mounted,
                HookIds.MoveSpeed,
                out _)
            || !actions.TryGet(
                ActionIds.Number,
                HookIds.EntityInteraction,
                VerbIds.Mounted,
                HookIds.TurnSpeed,
                out _)
            || !actions.TryGet(
                ActionIds.Number,
                HookIds.EntityInteraction,
                VerbIds.Mounted,
                HookIds.MoveSpeed,
                out _)
            || !actions.TryGet(
                ActionIds.Number,
                HookIds.EntityInteraction,
                VerbIds.Mounted,
                HookIds.RatlineStamina,
                out _))
        {
            Assert.Fail("[prosequor] Boating fixture failed (hook/action registration).");
            return;
        }

        CollectionIndex boatCollections = NewCollections("raft", "freshwater");
        boatCollections.AddCode("raft", "game:raft-plain");
        boatCollections.AddCode("freshwater", "game:water-still-7");

        FixtureSkillRegistry skills = new();
        skills.Register(new SkillDef
        {
            Id = "seamanship",
            Rules =
            [
                new AbilityRule
                {
                    RuleId = "root-forward",
                    Hook = HookIds.EntityInteraction,
                    Verb = VerbIds.Mounted,
                    Phase = HookIds.MoveSpeed,
                    Action = ActionIds.Number,
                    When = new AbilityWhenFilter(),
                    Parameters = ParseNumberSpec("{\"op\":\"add\",\"base\":0,\"perSkillLevel\":0.002,\"cap\":0.1}"),
                    Source = new AbilityRuleSource { SkillId = "seamanship" },
                    Priority = 0,
                    SourceOrder = 0
                },
                new AbilityRule
                {
                    RuleId = "oarsman",
                    Hook = HookIds.EntityInteraction,
                    Verb = VerbIds.Mounted,
                    Phase = HookIds.MoveSpeed,
                    Action = ActionIds.Number,
                    When = CompileWhen(boatCollections, "target:<raft>"),
                    Parameters = ParseNumberSpec("{\"op\":\"set\",\"base\":1,\"perSkillLevel\":0.0025,\"cap\":1.2}"),
                    Source = new AbilityRuleSource
                    {
                        SkillId = "seamanship",
                        NodeId = "oarsman",
                        Tier = 1
                    },
                    Priority = 0,
                    SourceOrder = 1
                },
                new AbilityRule
                {
                    RuleId = "river-raider",
                    Hook = HookIds.EntityInteraction,
                    Verb = VerbIds.Mounted,
                    Phase = HookIds.MoveSpeed,
                    Action = ActionIds.Number,
                    When = CompileWhen(boatCollections, "target:<raft>",
                        "ground:<freshwater>"),
                    Parameters = ParseNumberSpec("{\"op\":\"add\",\"value\":0.1}"),
                    Source = new AbilityRuleSource
                    {
                        SkillId = "seamanship",
                        NodeId = "riverraider",
                        Tier = 1
                    },
                    Priority = 0,
                    SourceOrder = 2
                },
                new AbilityRule
                {
                    RuleId = "rigger",
                    Hook = HookIds.EntityInteraction,
                    Verb = VerbIds.Mounted,
                    Phase = HookIds.RatlineStamina,
                    Action = ActionIds.Number,
                    When = new AbilityWhenFilter(),
                    Parameters = ParseNumberSpec("{\"op\":\"add\",\"value\":0.25}"),
                    Source = new AbilityRuleSource
                    {
                        SkillId = "seamanship",
                        NodeId = "rigger",
                        Tier = 1
                    },
                    Priority = 0,
                    SourceOrder = 3
                }
            ]
        });

        AbilityPipeline pipeline = new(actions, skills);
        FixtureProgress progress = new();
        progress.SetSkillLevel("seamanship", 50);

        MountedContext context = new()
        {
            Progress = progress,
            Fact = new AbilityAction
            {
                Verb = VerbIds.Mounted.Value,
                ActorUid = "fixture",
                Target = "game:raft-plain",
                Ground = "game:water-still-7"
            }
        };

        float rootForward = pipeline.Run(HookIds.EntityInteraction, VerbIds.Mounted, HookIds.MoveSpeed, context, 1f);
        if (Math.Abs(rootForward - 1.10f) > 0.0001f)
        {
            Assert.Fail(string.Format("[prosequor] Boating fixture failed (root forward). got={0} want=1.10.",
                rootForward));
        }

        progress.SetUnlockTier("seamanship", "oarsman", 1);
        float oarsman = pipeline.Run(HookIds.EntityInteraction, VerbIds.Mounted, HookIds.MoveSpeed, context, 1f);
        if (Math.Abs(oarsman - 1.125f) > 0.0001f)
        {
            Assert.Fail(string.Format("[prosequor] Boating fixture failed (oarsman). got={0} want=1.125.",
                oarsman));
        }

        progress.SetUnlockTier("seamanship", "riverraider", 1);
        float riverRaider = pipeline.Run(HookIds.EntityInteraction, VerbIds.Mounted, HookIds.MoveSpeed, context, 1f);
        if (Math.Abs(riverRaider - 1.225f) > 0.0001f)
        {
            Assert.Fail(string.Format("[prosequor] Boating fixture failed (river raider). got={0} want=1.225.",
                riverRaider));
        }

        progress.SetUnlockTier("seamanship", "rigger", 1);
        MountedContext ratlineContext = new()
        {
            Progress = progress,
            Fact = new AbilityAction
            {
                Verb = VerbIds.Mounted.Value,
                ActorUid = "fixture"
            }
        };
        float ratline = pipeline.Run(HookIds.EntityInteraction, VerbIds.Mounted, HookIds.RatlineStamina, ratlineContext, 0f);
        if (Math.Abs(ratline - 0.25f) > 0.0001f)
        {
            Assert.Fail(string.Format("[prosequor] Boating fixture failed (rigger). got={0} want=0.25.",
                ratline));
        }
    }

    static void VerifyOnRepairPipeline()
    {
        HookRegistry hooks = new();
        AbilityActionRegistry actions = new(hooks);
        AbilityBootstrap.RegisterBuiltIns(hooks, actions);

        if (!hooks.TryGet(HookIds.ItemInteraction, out _)
            || !actions.TryGet(
                ActionIds.Number,
                HookIds.ItemInteraction,
                VerbIds.Repair,
                HookIds.AddDurability,
                out _)
            || !actions.TryGet(
                ActionIds.Number,
                HookIds.ItemInteraction,
                VerbIds.Repair,
                HookIds.AddDurability,
                out _))
        {
            Assert.Fail("[prosequor] On-repair fixture failed (hook/action registration).");
            return;
        }

        FixtureSkillRegistry skills = new();
        skills.Register(new SkillDef
        {
            Id = "tailoring",
            Rules =
            [
                new AbilityRule
                {
                    RuleId = "root-clothing",
                    Hook = HookIds.ItemInteraction,
                    Verb = VerbIds.Repair,
                    Phase = HookIds.AddDurability,
                    Action = ActionIds.Number,
                    When = CompileWhen(
                        WithCodes(NewCollections("clothing", "armor"), ("clothing", "game:clothes-plain-shirt")),
                        "target:<clothing>"),
                    Parameters = ParseNumberSpec("{\"op\":\"add\",\"base\":0,\"perSkillLevel\":0.001,\"cap\":0.1}"),
                    Source = new AbilityRuleSource { SkillId = "tailoring" },
                    Priority = 0,
                    SourceOrder = 0
                },
                new AbilityRule
                {
                    RuleId = "root-armor",
                    Hook = HookIds.ItemInteraction,
                    Verb = VerbIds.Repair,
                    Phase = HookIds.AddDurability,
                    Action = ActionIds.Number,
                    When = CompileWhen(
                        WithCodes(NewCollections("clothing", "armor"), ("armor", "game:armor-body-leather")),
                        "target:<armor>"),
                    Parameters = ParseNumberSpec("{\"op\":\"add\",\"base\":0,\"perSkillLevel\":0.001,\"cap\":0.1}"),
                    Source = new AbilityRuleSource { SkillId = "tailoring" },
                    Priority = 0,
                    SourceOrder = 1
                },
                new AbilityRule
                {
                    RuleId = "clothes-mending",
                    Hook = HookIds.ItemInteraction,
                    Verb = VerbIds.Repair,
                    Phase = HookIds.AddDurability,
                    Action = ActionIds.Number,
                    When = CompileWhen(
                        WithCodes(NewCollections("clothing", "armor"), ("clothing", "game:clothes-plain-shirt")),
                        "target:<clothing>"),
                    Parameters = ParseNumberSpec("{\"op\":\"set\",\"base\":1,\"perSkillLevel\":0.002,\"cap\":1.2}"),
                    Source = new AbilityRuleSource
                    {
                        SkillId = "tailoring",
                        NodeId = "clothesmending",
                        Tier = 1
                    },
                    Priority = 0,
                    SourceOrder = 2
                }
            ]
        });

        AbilityPipeline pipeline = new(actions, skills);
        FixtureProgress progress = new();
        progress.SetSkillLevel("tailoring", 50);

        RepairContext clothingContext = new()
        {
            Progress = progress,
            Fact = new AbilityAction
            {
                Verb = RepairStation.VerbRepair,
                ActorUid = "fixture",
                Target = "game:clothes-plain-shirt"
            }
        };

        float latentClothing = pipeline.Run(HookIds.ItemInteraction, VerbIds.Repair, HookIds.AddDurability, clothingContext,
            1f);
        if (Math.Abs(latentClothing - 1.05f) > 0.0001f)
        {
            Assert.Fail(string.Format(
                "[prosequor] On-repair fixture failed (latent clothing). got={0} want=1.05.",
                latentClothing));
        }

        progress.SetUnlockTier("tailoring", "clothesmending", 1);
        float mending = pipeline.Run(HookIds.ItemInteraction, VerbIds.Repair, HookIds.AddDurability, clothingContext,
            1f);
        if (Math.Abs(mending - 1.10f) > 0.0001f)
        {
            Assert.Fail(string.Format(
                "[prosequor] On-repair fixture failed (clothes mending SET). got={0} want=1.10.",
                mending));
        }

        RepairContext armorContext = new()
        {
            Progress = progress,
            Fact = new AbilityAction
            {
                Verb = RepairStation.VerbRepair,
                ActorUid = "fixture",
                Target = "game:armor-body-leather"
            }
        };

        float armorOnly = pipeline.Run(HookIds.ItemInteraction, VerbIds.Repair, HookIds.AddDurability, armorContext,
            1f);
        if (Math.Abs(armorOnly - 1.05f) > 0.0001f)
        {
            Assert.Fail(string.Format(
                "[prosequor] On-repair fixture failed (armor latent; clothing SET isolated). got={0} want=1.05.",
                armorOnly));
        }
    }

    static void VerifyOnCraftPipeline()
    {
        HookRegistry hooks = new();
        AbilityActionRegistry actions = new(hooks);
        AbilityBootstrap.RegisterBuiltIns(hooks, actions);

        if (!hooks.TryGet(HookIds.CraftingInteraction, out _)
            || !actions.TryGet(
                ActionIds.Number,
                HookIds.CraftingInteraction,
                VerbIds.MutateOutput,
                HookIds.Quantity,
                out _))
        {
            Assert.Fail("[prosequor] On-craft fixture failed (hook/action registration).");
            return;
        }

        FixtureSkillRegistry skills = new();
        skills.Register(new SkillDef
        {
            Id = "tailoring",
            Rules =
            [
                new AbilityRule
                {
                    RuleId = "threadmaker",
                    Hook = HookIds.CraftingInteraction,
                    Verb = VerbIds.MutateOutput,
                    Phase = HookIds.Quantity,
                    Action = ActionIds.Number,
                    When = CompileWhen(
                        WithCodes(NewCollections("thread", "cloth"), ("thread", "game:flaxtwine")),
                        "target:<thread>"),
                    Parameters = ParseNumberSpec(
                        "{\"op\":\"scale\",\"base\":0.15,\"perSkillLevel\":0,\"cap\":0.15}"),
                    Source = new AbilityRuleSource
                    {
                        SkillId = "tailoring",
                        NodeId = "threadmaker",
                        Tier = 1
                    },
                    Priority = 0,
                    SourceOrder = 0
                },
                new AbilityRule
                {
                    RuleId = "clothweaver",
                    Hook = HookIds.CraftingInteraction,
                    Verb = VerbIds.MutateOutput,
                    Phase = HookIds.Quantity,
                    Action = ActionIds.Number,
                    When = CompileWhen(
                        WithCodes(NewCollections("thread", "cloth"), ("cloth", "game:linen-normal-down")),
                        "target:<cloth>"),
                    Parameters = ParseNumberSpec(
                        "{\"op\":\"scale\",\"base\":0.15,\"perSkillLevel\":0,\"cap\":0.15}"),
                    Source = new AbilityRuleSource
                    {
                        SkillId = "tailoring",
                        NodeId = "clothweaver",
                        Tier = 1
                    },
                    Priority = 0,
                    SourceOrder = 1
                }
            ]
        });

        AbilityPipeline pipeline = new(actions, skills);
        FixtureProgress progress = new();
        progress.SetSkillLevel("tailoring", 1);
        progress.SetUnlockTier("tailoring", "threadmaker", 1);

        CraftMutateOutputContext threadContext = new()
        {
            Progress = progress,
            Fact = new AbilityAction
            {
                Verb = CraftMutateOutputStation.VerbCraft,
                ActorUid = "fixture",
                Target = "game:flaxtwine"
            }
        };

        float threadQty = pipeline.Run(HookIds.CraftingInteraction, VerbIds.MutateOutput, HookIds.Quantity, threadContext, 100f);
        if (Math.Abs(threadQty - 115f) > 0.0001f)
        {
            Assert.Fail(string.Format(
                "[prosequor] On-craft fixture failed (threadmaker). got={0} want=115.",
                threadQty));
        }

        CraftMutateOutputContext clothContext = new()
        {
            Progress = progress,
            Fact = new AbilityAction
            {
                Verb = CraftMutateOutputStation.VerbCraft,
                ActorUid = "fixture",
                Target = "game:linen-normal-down"
            }
        };

        float clothIsolated = pipeline.Run(HookIds.CraftingInteraction, VerbIds.MutateOutput, HookIds.Quantity, clothContext, 100f);
        if (Math.Abs(clothIsolated - 100f) > 0.0001f)
        {
            Assert.Fail(string.Format(
                "[prosequor] On-craft fixture failed (cloth isolated from threadmaker). got={0} want=100.",
                clothIsolated));
        }

        progress.SetUnlockTier("tailoring", "clothweaver", 1);
        float clothQty = pipeline.Run(HookIds.CraftingInteraction, VerbIds.MutateOutput, HookIds.Quantity, clothContext, 100f);
        if (Math.Abs(clothQty - 115f) > 0.0001f)
        {
            Assert.Fail(string.Format(
                "[prosequor] On-craft fixture failed (clothweaver). got={0} want=115.",
                clothQty));
        }

        if (!CraftMutateOutputStation.IsSynthesis(["flaxtwine", "flaxtwine", "flaxtwine", "flaxtwine"], "linen-normal-down"))
        {
            Assert.Fail("[prosequor] On-craft fixture failed (twine→linen should be synthesis).");
        }

        if (CraftMutateOutputStation.IsSynthesis(["linen-normal-down", "stick"], "cloth-plain"))
        {
            Assert.Fail("[prosequor] On-craft fixture failed (linen→cloth should not be synthesis).");
        }

        if (CraftMutateOutputStation.IsSynthesis(["linen-normal-down", "sewingkit"], "linen-diamond-down"))
        {
            Assert.Fail("[prosequor] On-craft fixture failed (stitch convert should not be synthesis).");
        }

        if (CraftMutateOutputStation.IsSynthesis(["linen-diamond-down", "shears-iron"], "linen-normal-down"))
        {
            Assert.Fail("[prosequor] On-craft fixture failed (shear revert should not be synthesis).");
        }
    }

    static void VerifyCraftAttributePipeline()
    {
        HookRegistry hooks = new();
        AbilityActionRegistry actions = new(hooks);
        AffixListRegistry affixLists = new();
        affixLists.ReplaceAll(
        [
            new AffixListDef
            {
                Id = "prosequor:durability",
                Entries =
                [
                    new AffixListEntryDef
                    {
                        Code = "sturdy",
                        Lang = "prosequor:affix-sturdy",
                        Color = "#84ff84"
                    }
                ]
            }
        ]);
        AbilityBootstrap.RegisterBuiltIns(hooks, actions, affixLists);

        if (!hooks.TryGet(HookIds.CraftingInteraction, out _)
            || !actions.TryGet(
                ActionIds.ModifyAttribute,
                HookIds.CraftingInteraction,
                VerbIds.MutateOutput,
                HookIds.Attributes,
                out _))
        {
            Assert.Fail("[prosequor] Craft-attribute fixture failed (hook/action registration).");
            return;
        }

        if (!AbilityBootstrap.AttributeMutators.TryGet(
                DurabilityAttributeMutator.KeyName,
                out ICraftAttributeMutator? durabilityMutator)
            || durabilityMutator == null
            || !AbilityBootstrap.AttributeMutators.TryGet(WarmthAttributeMutator.KeyName, out _)
            || !AbilityBootstrap.AttributeMutators.TryGet(CoolingAttributeMutator.KeyName, out _)
            || !AbilityBootstrap.AttributeMutators.TryGet(ProtectionAttributeMutator.KeyName, out _)
            || !AbilityBootstrap.AttributeMutators.TryGet(FreshnessAttributeMutator.KeyName, out _)
            || !AbilityBootstrap.AttributeMutators.TryGet(SatietyAttributeMutator.KeyName, out _)
            || !AbilityBootstrap.AttributeMutators.TryGet(HungerDelayAttributeMutator.KeyName, out _)
            || !AbilityBootstrap.AttributeMutators.TryGet(IntoxicationAttributeMutator.KeyName, out _)
            || !AbilityBootstrap.AttributeMutators.TryGet(PriceAttributeMutator.KeyName, out _))
        {
            Assert.Fail("[prosequor] Craft-attribute fixture failed (mutator registry incomplete).");
            return;
        }

        FreshnessAttributeMutator freshnessMutator = new();
        if (freshnessMutator.Accepts(null!))
        {
            Assert.Fail("[prosequor] Freshness mutator should reject null stack.");
            return;
        }

        IntoxicationAttributeMutator intoxMutator = new();
        PriceAttributeMutator priceMutator = new();
        if (intoxMutator.Accepts(null!)
            || intoxMutator.Accepts(new ItemStack())
            || priceMutator.Accepts(null!)
            || priceMutator.Accepts(new ItemStack()))
        {
            Assert.Fail("[prosequor] Liquor mutators should reject null / non-portion stacks.");
            return;
        }

        if (AbilityBootstrap.AttributeMutators.Accepts(null, DurabilityAttributeMutator.KeyName)
            || AbilityBootstrap.AttributeMutators.Multiply(null, DurabilityAttributeMutator.KeyName, 1.15f)
            || AbilityBootstrap.AttributeMutators.Set(null, DurabilityAttributeMutator.KeyName, 1.15f))
        {
            Assert.Fail("[prosequor] Craft-attribute fixture failed (null stack should reject).");
            return;
        }

        if (AbilityBootstrap.AttributeMutators.Multiply(
                new ItemStack(),
                "warmth",
                1.2f)
            || AbilityBootstrap.AttributeMutators.Set(new ItemStack(), "warmth", 1.2f))
        {
            Assert.Fail("[prosequor] Craft-attribute fixture failed (unknown key should ignore).");
            return;
        }

        ItemStack factorProbe = new();
        CraftAttributeMods.MultiplyFactor(factorProbe, DurabilityAttributeMutator.KeyName, 1.15f);
        CraftAttributeMods.MultiplyFactor(factorProbe, DurabilityAttributeMutator.KeyName, 1.10f);
        float factor = CraftAttributeMods.GetFactor(factorProbe, DurabilityAttributeMutator.KeyName);
        if (Math.Abs(factor - 1.15f * 1.10f) > 0.0001f)
        {
            Assert.Fail(string.Format(
                "[prosequor] Craft-attribute fixture failed (factor accumulate). got={0} want={1}.",
                factor,
                1.15f * 1.10f));
            return;
        }

        CraftAttributeMods.SetFactor(factorProbe, DurabilityAttributeMutator.KeyName, 1.25f);
        factor = CraftAttributeMods.GetFactor(factorProbe, DurabilityAttributeMutator.KeyName);
        if (Math.Abs(factor - 1.25f) > 0.0001f)
        {
            Assert.Fail(string.Format(
                "[prosequor] Craft-attribute fixture failed (SetFactor). got={0} want={1}.",
                factor,
                1.25f));
            return;
        }

        ItemStack affixProbe = new();
        if (!ItemAffixes.Add(affixProbe, "sturdy", "prosequor:affix-sturdy", "#84ff84")
            || ItemAffixes.Add(affixProbe, "sturdy", "prosequor:affix-sturdy", "#84ff84")
            || !ItemAffixes.Add(affixProbe, "cool", "prosequor:affix-cool", "#99c9f9"))
        {
            Assert.Fail("[prosequor] Affix fixture failed (add / dedupe).");
            return;
        }

        if (!ItemAffixes.SetFront(
                affixProbe,
                ItemAffixes.QualityCode,
                "prosequor:affix-quality-2")
            || ItemAffixes.GetAll(affixProbe)[0].Code != ItemAffixes.QualityCode
            || ItemAffixes.GetAll(affixProbe).Count != 3)
        {
            Assert.Fail("[prosequor] Affix fixture failed (SetFront quality grade).");
            return;
        }

        if (!ItemAffixes.SetFront(
                affixProbe,
                ItemAffixes.QualityCode,
                "prosequor:affix-quality-4")
            || ItemAffixes.GetAll(affixProbe)[0].LangKey != "prosequor:affix-quality-4"
            || ItemAffixes.GetAll(affixProbe).Count != 3)
        {
            Assert.Fail("[prosequor] Affix fixture failed (SetFront replace grade).");
            return;
        }

        IReadOnlyList<ItemAffixEntry> affixes = ItemAffixes.GetAll(affixProbe);
        if (affixes.Count != 3
            || affixes[0].Code != ItemAffixes.QualityCode
            || affixes[1].Code != "sturdy"
            || affixes[2].Code != "cool"
            || affixes[1].Color != "#84ff84")
        {
            Assert.Fail("[prosequor] Affix fixture failed (ordered bag contents).");
            return;
        }

        if (ItemAffixes.FormatHeaderNames(null) != null
            || ItemAffixes.FormatHeaderNames(new ItemStack()) != null)
        {
            Assert.Fail("[prosequor] Affix fixture failed (FormatHeaderNames empty).");
            return;
        }

        string? header = ItemAffixes.FormatHeaderNames(affixProbe);
        if (header == null)
        {
            Assert.Fail("[prosequor] Affix fixture failed (header should keep non-quality affixes).");
            return;
        }

        if (header.Contains(ItemAffixes.QualityCode, StringComparison.OrdinalIgnoreCase)
            || header.Contains("affix-quality", StringComparison.OrdinalIgnoreCase))
        {
            Assert.Fail("[prosequor] Affix fixture failed (header must skip quality grade).");
            return;
        }

        if (!ItemAffixes.TryFormatQualityFooter(affixProbe, out string qualityFooter)
            || qualityFooter.IndexOf(OwnerCredit.MutedColor, StringComparison.Ordinal) < 0
            || qualityFooter.IndexOf("<i>", StringComparison.Ordinal) < 0
            || qualityFooter.IndexOf("affix-quality-4", StringComparison.OrdinalIgnoreCase) < 0)
        {
            // Offline Lang keeps the key; muted italic chrome is the contract.
            Assert.Fail("[prosequor] Affix fixture failed (quality footer style).");
            return;
        }

        string withQuality = ItemAffixes.AppendQualityFooter("body", affixProbe);
        if (!withQuality.StartsWith("body\n", StringComparison.Ordinal)
            || withQuality.IndexOf(qualityFooter, StringComparison.Ordinal) < 0)
        {
            Assert.Fail("[prosequor] Affix fixture failed (AppendQualityFooter).");
            return;
        }

        if (!actions.TryGet(
                ActionIds.AddAffix,
                HookIds.CraftingInteraction,
                VerbIds.MutateOutput,
                HookIds.Attributes,
                out _))
        {
            Assert.Fail("[prosequor] Affix fixture failed (add-affix action registration).");
            return;
        }

        List<string> affixErrors = new();
        int affixOrder = 0;
        CollectionIndex affixCollections = WithCodes(
            NewCollections(RepairStation.TagClothing),
            (RepairStation.TagClothing, "game:clothes-plain-shirt"));
        List<AbilityRule>? affixRules = AbilityRuleCompiler.CompileEffects(
            "tailoring",
            "clothingproficiency",
            1,
            [
                new AbilityEffectJson
                {
                    hook = "prosequor:crafting-interaction",
                    verb = "prosequor:mutate-output",
                    phase = "attributes",
                    action = "prosequor:add-affix",
                    when = new AbilityWhenJson { tags = ["target:<clothing>"] },
                    @params = JObject.Parse(
                        """{ "list": "prosequor:durability", "item": 0 }""")
                }
            ],
            hooks,
            actions,
            affixCollections,
            affixErrors,
            ref affixOrder);
        if (affixRules == null
            || affixRules.Count != 1
            || affixRules[0].Parameters is not AddAffixParams affixParams
            || affixParams.Code != "sturdy"
            || affixParams.Lang != "prosequor:affix-sturdy"
            || affixParams.Color != "#84ff84")
        {
            Assert.Fail(string.Format(
                "[prosequor] Affix fixture failed (list+item compile): {0}",
                string.Join("; ", affixErrors)));
            return;
        }

        List<string> affixByCodeErrors = new();
        int affixByCodeOrder = 0;
        List<AbilityRule>? affixByCodeRules = AbilityRuleCompiler.CompileEffects(
            "tailoring",
            "clothingproficiency",
            1,
            [
                new AbilityEffectJson
                {
                    hook = "prosequor:crafting-interaction",
                    verb = "prosequor:mutate-output",
                    phase = "attributes",
                    action = "prosequor:add-affix",
                    when = new AbilityWhenJson { tags = ["target:<clothing>"] },
                    @params = JObject.Parse(
                        """{ "list": "prosequor:durability", "item": "sturdy" }""")
                }
            ],
            hooks,
            actions,
            affixCollections,
            affixByCodeErrors,
            ref affixByCodeOrder);
        if (affixByCodeRules == null
            || affixByCodeRules.Count != 1
            || affixByCodeRules[0].Parameters is not AddAffixParams affixByCodeParams
            || affixByCodeParams.Code != "sturdy")
        {
            Assert.Fail(string.Format(
                "[prosequor] Affix fixture failed (list+code compile): {0}",
                string.Join("; ", affixByCodeErrors)));
            return;
        }

        List<string> affixInlineErrors = new();
        int affixInlineOrder = 0;
        List<AbilityRule>? affixInlineRules = AbilityRuleCompiler.CompileEffects(
            "tailoring",
            "summerweaver",
            1,
            [
                new AbilityEffectJson
                {
                    hook = "prosequor:crafting-interaction",
                    verb = "prosequor:mutate-output",
                    phase = "attributes",
                    action = "prosequor:add-affix",
                    when = new AbilityWhenJson { tags = ["target:<clothing>"] },
                    @params = JObject.Parse(
                        """{ "code": "cool", "lang": "prosequor:affix-cool", "color": "#99c9f9" }""")
                }
            ],
            hooks,
            actions,
            affixCollections,
            affixInlineErrors,
            ref affixInlineOrder);
        if (affixInlineRules == null
            || affixInlineRules.Count != 1
            || affixInlineRules[0].Parameters is not AddAffixParams affixInlineParams
            || affixInlineParams.Code != "cool"
            || affixInlineParams.Lang != "prosequor:affix-cool")
        {
            Assert.Fail(string.Format(
                "[prosequor] Affix fixture failed (inline compile): {0}",
                string.Join("; ", affixInlineErrors)));
            return;
        }

        int order = 0;
        List<string> errors = new();
        CollectionIndex clothingCollections = WithCodes(
            NewCollections(RepairStation.TagClothing),
            (RepairStation.TagClothing, "game:clothes-plain-shirt"));
        List<AbilityRule>? rules = AbilityRuleCompiler.CompileEffects(
            "tailoring",
            "clothingproficiency",
            1,
            [
                new AbilityEffectJson
                {
                    hook = "prosequor:crafting-interaction",
                    verb = "prosequor:mutate-output",
                    phase = "attributes",
                    action = "prosequor:modify-attribute",
                    when = new AbilityWhenJson
                    {
                        tags = ["target:<clothing>"]
                    },
                    @params = JObject.Parse(
                        """{ "key": "durability", "op": "scale", "base": 0.15 }""")
                }
            ],
            hooks,
            actions,
            clothingCollections,
            errors,
            ref order);
        if (rules == null || rules.Count != 1 || errors.Count > 0
            || rules[0].Parameters is not ModifyAttributeParams parsed
            || !string.Equals(parsed.Key, "durability", StringComparison.OrdinalIgnoreCase)
            || Math.Abs(parsed.Spec.Apply(
                1f,
                new CraftMutateOutputContext(),
                new AbilityRuleSource { SkillId = "tailoring", NodeId = "clothingproficiency", Tier = 1 }) - 1.15f) > 0.0001f)
        {
            Assert.Fail(string.Format(
                "[prosequor] Craft-attribute fixture failed (clothing proficiency compile): {0}",
                string.Join("; ", errors)));
            return;
        }

        FixtureSkillRegistry skills = new();
        skills.Register(new SkillDef
        {
            Id = "tailoring",
            Rules = [rules[0]]
        });

        AbilityPipeline pipeline = new(actions, skills);
        FixtureProgress progress = new();
        progress.SetSkillLevel("tailoring", 10);
        progress.SetUnlockTier("tailoring", "clothingproficiency", 1);

        // Empty collectible → Accepts false → multiply no-ops; still proves pipeline runs.
        ItemStack crafted = new();
        CraftMutateOutputContext context = new()
        {
            Progress = progress,
            Fact = new AbilityAction
            {
                Verb = CraftMutateOutputStation.VerbCraft,
                ActorUid = "fixture",
                Target = "game:clothes-plain-shirt"
            },
            Crafted = crafted
        };

        ItemStack result = pipeline.Run(HookIds.CraftingInteraction, VerbIds.MutateOutput, HookIds.Attributes, context, crafted);
        if (!ReferenceEquals(result, crafted))
        {
            Assert.Fail("[prosequor] Craft-attribute fixture failed (pipeline should return same stack).");
        }

        if (Math.Abs(CraftAttributeMods.GetFactor(crafted, DurabilityAttributeMutator.KeyName) - 1f) > 0.0001f)
        {
            Assert.Fail(
                "[prosequor] Craft-attribute fixture failed (empty collectible must not stamp durability).");
        }
    }

    static void VerifyApplyQualityPipeline()
    {
        HookRegistry hooks = new();
        AbilityActionRegistry actions = new(hooks);
        AffixListRegistry affixLists = new();
        affixLists.ReplaceAll(
        [
            new AffixListDef
            {
                Id = "prosequor:quality",
                Entries =
                [
                    new AffixListEntryDef { Code = "quality-1", Lang = "prosequor:affix-quality-1" },
                    new AffixListEntryDef { Code = "quality-2", Lang = "prosequor:affix-quality-2" },
                    new AffixListEntryDef { Code = "quality-3", Lang = "prosequor:affix-quality-3" },
                    new AffixListEntryDef { Code = "quality-4", Lang = "prosequor:affix-quality-4" },
                    new AffixListEntryDef { Code = "quality-5", Lang = "prosequor:affix-quality-5" }
                ]
            },
            new AffixListDef
            {
                Id = "prosequor:durability",
                Entries =
                [
                    new AffixListEntryDef
                    {
                        Code = "sturdy",
                        Lang = "prosequor:affix-sturdy",
                        Color = "#84ff84"
                    },
                    new AffixListEntryDef
                    {
                        Code = "reinforced",
                        Lang = "prosequor:affix-reinforced",
                        Color = "#84ff84"
                    },
                    new AffixListEntryDef
                    {
                        Code = "fortified",
                        Lang = "prosequor:affix-fortified",
                        Color = "#84ff84"
                    }
                ]
            }
        ]);
        AbilityBootstrap.RegisterBuiltIns(hooks, actions, affixLists);

        if (!actions.TryGet(
                ActionIds.Quality,
                HookIds.CraftingInteraction,
                VerbIds.ApplyQuality,
                HookIds.Attributes,
                out _)
            || !actions.TryGet(
                ActionIds.QualityRank,
                HookIds.CraftingInteraction,
                VerbIds.ApplyQuality,
                HookIds.Attributes,
                out _)
            || !actions.TryGet(
                ActionIds.Number,
                HookIds.CraftingInteraction,
                VerbIds.ApplyQuality,
                HookIds.QualityBase,
                out _))
        {
            Assert.Fail("[prosequor] Quality fixture failed (action registration).");
            return;
        }

        CollectionIndex armorCollections = WithCodes(
            NewCollections("armor"),
            ("armor", "game:armor-body-improvised-wood"));

        List<string> missingOpErrors = new();
        int missingOpOrder = 0;
        List<AbilityRule>? missingOp = AbilityRuleCompiler.CompileEffects(
            "tailoring",
            "armorproficiency",
            1,
            [
                new AbilityEffectJson
                {
                    hook = "prosequor:crafting-interaction",
                    verb = "prosequor:apply-quality",
                    phase = "attributes",
                    action = "prosequor:quality",
                    when = new AbilityWhenJson { tags = ["target:<armor>"] },
                    @params = JObject.Parse(
                        """{ "key": "protection", "table": [0, 0.05, 0.1], "affixes": "prosequor:durability" }""")
                }
            ],
            hooks,
            actions,
            armorCollections,
            missingOpErrors,
            ref missingOpOrder);
        if (missingOp != null || missingOpErrors.Count == 0)
        {
            Assert.Fail("[prosequor] Quality fixture failed (op should be required).");
            return;
        }

        List<string> badListErrors = new();
        int badListOrder = 0;
        List<AbilityRule>? badList = AbilityRuleCompiler.CompileEffects(
            "tailoring",
            "armorproficiency",
            1,
            [
                new AbilityEffectJson
                {
                    hook = "prosequor:crafting-interaction",
                    verb = "prosequor:apply-quality",
                    phase = "attributes",
                    action = "prosequor:quality",
                    when = new AbilityWhenJson { tags = ["target:<armor>"] },
                    @params = JObject.Parse(
                        """{ "key": "protection", "op": "scale", "table": [0, 0.1], "affixes": "prosequor:missing" }""")
                }
            ],
            hooks,
            actions,
            armorCollections,
            badListErrors,
            ref badListOrder);
        if (badList != null || badListErrors.Count == 0)
        {
            Assert.Fail("[prosequor] Quality fixture failed (unknown affix list should fail).");
            return;
        }

        List<string> compileErrors = new();
        int compileOrder = 0;
        List<AbilityRule>? qualityRules = AbilityRuleCompiler.CompileEffects(
            "tailoring",
            "armorproficiency",
            1,
            [
                new AbilityEffectJson
                {
                    hook = "prosequor:crafting-interaction",
                    verb = "prosequor:apply-quality",
                    phase = "attributes",
                    action = "prosequor:quality",
                    when = new AbilityWhenJson { tags = ["target:<armor>"] },
                    @params = JObject.Parse(
                        """{ "key": "protection", "op": "scale", "table": [0, 0.05, 0.1], "affixes": "prosequor:durability", "bonus": 0 }""")
                },
                new AbilityEffectJson
                {
                    hook = "prosequor:crafting-interaction",
                    verb = "prosequor:apply-quality",
                    phase = "quality-base",
                    action = "prosequor:number",
                    when = new AbilityWhenJson { tags = ["target:<armor>"] },
                    @params = JObject.Parse("""{ "op": "add", "value": 0 }""")
                }
            ],
            hooks,
            actions,
            armorCollections,
            compileErrors,
            ref compileOrder);
        if (qualityRules == null
            || qualityRules.Count != 2
            || qualityRules[0].Parameters is not ApplyQualityParams parsed
            || parsed.Table.Count != 3
            || Math.Abs(parsed.Bonus) > 0.0001f
            || parsed.AffixFrom != 0
            || parsed.AffixTo != 2
            || compileErrors.Count > 0)
        {
            Assert.Fail(string.Format(
                "[prosequor] Quality fixture failed (compile): {0}",
                string.Join("; ", compileErrors)));
            return;
        }

        List<string> rangeErrors = new();
        int rangeOrder = 0;
        List<AbilityRule>? ranged = AbilityRuleCompiler.CompileEffects(
            "tailoring",
            "armorproficiency",
            1,
            [
                new AbilityEffectJson
                {
                    hook = "prosequor:crafting-interaction",
                    verb = "prosequor:apply-quality",
                    phase = "attributes",
                    action = "prosequor:quality",
                    when = new AbilityWhenJson { tags = ["target:<armor>"] },
                    @params = JObject.Parse(
                        """{ "key": "protection", "op": "scale", "table": [0, 0.2], "affixes": "prosequor:durability", "affixRange": [0, 1] }""")
                }
            ],
            hooks,
            actions,
            armorCollections,
            rangeErrors,
            ref rangeOrder);
        if (ranged == null
            || ranged.Count != 1
            || ranged[0].Parameters is not ApplyQualityParams rangedParams
            || rangedParams.AffixFrom != 0
            || rangedParams.AffixTo != 1
            || rangeErrors.Count > 0)
        {
            Assert.Fail(string.Format(
                "[prosequor] Quality fixture failed (affixRange compile): {0}",
                string.Join("; ", rangeErrors)));
            return;
        }

        List<string> badRangeErrors = new();
        int badRangeOrder = 0;
        List<AbilityRule>? badRange = AbilityRuleCompiler.CompileEffects(
            "tailoring",
            "armorproficiency",
            1,
            [
                new AbilityEffectJson
                {
                    hook = "prosequor:crafting-interaction",
                    verb = "prosequor:apply-quality",
                    phase = "attributes",
                    action = "prosequor:quality",
                    when = new AbilityWhenJson { tags = ["target:<armor>"] },
                    @params = JObject.Parse(
                        """{ "key": "protection", "op": "scale", "table": [0, 0.2], "affixes": "prosequor:durability", "affixRange": [0, 9] }""")
                }
            ],
            hooks,
            actions,
            armorCollections,
            badRangeErrors,
            ref badRangeOrder);
        if (badRange != null || badRangeErrors.Count == 0)
        {
            Assert.Fail("[prosequor] Quality fixture failed (affixRange out of bounds should fail).");
            return;
        }

        List<string> noAffixErrors = new();
        int noAffixOrder = 0;
        List<AbilityRule>? noAffixRules = AbilityRuleCompiler.CompileEffects(
            "tailoring",
            "armorproficiency",
            1,
            [
                new AbilityEffectJson
                {
                    hook = "prosequor:crafting-interaction",
                    verb = "prosequor:apply-quality",
                    phase = "attributes",
                    action = "prosequor:quality",
                    when = new AbilityWhenJson { tags = ["target:<armor>"] },
                    @params = JObject.Parse(
                        """{ "key": "protection", "op": "scale", "table": [0, 0.1] }""")
                }
            ],
            hooks,
            actions,
            armorCollections,
            noAffixErrors,
            ref noAffixOrder);
        if (noAffixRules == null
            || noAffixRules.Count != 1
            || noAffixRules[0].Parameters is not ApplyQualityParams noAffixParsed
            || noAffixParsed.AffixList != null
            || noAffixErrors.Count > 0)
        {
            Assert.Fail(string.Format(
                "[prosequor] Quality fixture failed (affixes optional compile): {0}",
                string.Join("; ", noAffixErrors)));
            return;
        }

        List<string> rangeNeedsListErrors = new();
        int rangeNeedsListOrder = 0;
        List<AbilityRule>? rangeNeedsList = AbilityRuleCompiler.CompileEffects(
            "tailoring",
            "armorproficiency",
            1,
            [
                new AbilityEffectJson
                {
                    hook = "prosequor:crafting-interaction",
                    verb = "prosequor:apply-quality",
                    phase = "attributes",
                    action = "prosequor:quality",
                    when = new AbilityWhenJson { tags = ["target:<armor>"] },
                    @params = JObject.Parse(
                        """{ "key": "protection", "op": "scale", "table": [0, 0.1], "affixRange": [0, 1] }""")
                }
            ],
            hooks,
            actions,
            armorCollections,
            rangeNeedsListErrors,
            ref rangeNeedsListOrder);
        if (rangeNeedsList != null || rangeNeedsListErrors.Count == 0)
        {
            Assert.Fail("[prosequor] Quality fixture failed (affixRange without affixes should fail).");
            return;
        }

        FixtureSkillRegistry skills = new();
        skills.Register(new SkillDef
        {
            Id = "tailoring",
            Rules = [qualityRules[0], qualityRules[1]]
        });

        AbilityPipeline pipeline = new(actions, skills);
        MemoFixtureProgress progress = new();
        progress.SetSkillLevel("tailoring", 100);
        progress.SetUnlockTier("tailoring", "armorproficiency", 1);

        AbilityAction fact = new()
        {
            Verb = VerbIds.ApplyQuality.Value,
            ActorUid = "fixture",
            Held = CallerIdentities.Grid,
            Target = "game:armor-body-improvised-wood"
        };

        CraftMutateOutputContext knobContext = new()
        {
            Progress = progress,
            Fact = fact,
            Pipeline = pipeline
        };

        float baseSeed = QualityMath.BaseSeed(100);
        float firstBase = pipeline.Run(
            HookIds.CraftingInteraction,
            VerbIds.ApplyQuality,
            HookIds.QualityBase,
            knobContext,
            baseSeed);
        int hitsBefore = progress.ComposeMemo.HitCount;
        float secondBase = pipeline.Run(
            HookIds.CraftingInteraction,
            VerbIds.ApplyQuality,
            HookIds.QualityBase,
            knobContext,
            baseSeed);
        if (Math.Abs(firstBase - baseSeed) > 0.0001f
            || Math.Abs(secondBase - firstBase) > 0.0001f
            || progress.ComposeMemo.HitCount != hitsBefore + 1)
        {
            Assert.Fail(string.Format(
                "[prosequor] Quality fixture failed (knob memo). first={0} second={1} hits={2}.",
                firstBase,
                secondBase,
                progress.ComposeMemo.HitCount));
            return;
        }

        // Forced low roll + large negative bonus → skip (no affix).
        ItemStack skipped = new();
        // Override rule bonus via a second compiled rule with bonus -500.
        List<string> skipErrors = new();
        int skipOrder = 0;
        List<AbilityRule>? skipRules = AbilityRuleCompiler.CompileEffects(
            "tailoring",
            "armorproficiency",
            1,
            [
                new AbilityEffectJson
                {
                    hook = "prosequor:crafting-interaction",
                    verb = "prosequor:apply-quality",
                    phase = "attributes",
                    action = "prosequor:quality",
                    when = new AbilityWhenJson { tags = ["target:<armor>"] },
                    @params = JObject.Parse(
                        """{ "key": "protection", "op": "scale", "table": [0, 0.05, 0.1], "affixes": "prosequor:durability", "bonus": -500 }""")
                }
            ],
            hooks,
            actions,
            armorCollections,
            skipErrors,
            ref skipOrder);
        if (skipRules == null || skipRules.Count != 1)
        {
            Assert.Fail("[prosequor] Quality fixture failed (skip-rule compile).");
            return;
        }

        FixtureSkillRegistry skipSkills = new();
        skipSkills.Register(new SkillDef { Id = "tailoring", Rules = [skipRules[0]] });
        AbilityPipeline skipPipeline = new(actions, skipSkills);
        CraftMutateOutputContext skipContext = new()
        {
            Progress = progress,
            Fact = fact,
            Crafted = skipped,
            Pipeline = skipPipeline,
            Rand = new FixedDoubleRandom(0.0)
        };
        if (!CraftMutateOutputStation.TryWarmQualityKnobs(skipContext, skipPipeline, progress))
        {
            Assert.Fail("[prosequor] Quality fixture failed (skip WarmUp).");
            return;
        }

        skipPipeline.Run(
            HookIds.CraftingInteraction,
            VerbIds.ApplyQuality,
            HookIds.Attributes,
            skipContext,
            skipped);
        if (ItemAffixes.GetAll(skipped).Count != 0)
        {
            Assert.Fail("[prosequor] Quality fixture failed (negative bonus should skip affix).");
            return;
        }

        // High roll: base seed at level 100 is -100, window 200 → roll in [-100, 100].
        // Force NextDouble=1.0 → raw/mean 100 → points 10; grade Fine; rule affix by points.
        ItemStack stamped = new();
        CraftMutateOutputContext stampContext = new()
        {
            Progress = progress,
            Fact = fact,
            Crafted = stamped,
            Pipeline = pipeline,
            Rand = new FixedDoubleRandom(1.0)
        };
        if (!CraftMutateOutputStation.TryWarmQualityKnobs(stampContext, pipeline, progress))
        {
            Assert.Fail("[prosequor] Quality fixture failed (stamp WarmUp).");
            return;
        }

        pipeline.Run(
            HookIds.CraftingInteraction,
            VerbIds.ApplyQuality,
            HookIds.Attributes,
            stampContext,
            stamped);

        IReadOnlyList<ItemAffixEntry> stampedAffixes = ItemAffixes.GetAll(stamped);
        int wantIdx = QualityMath.AffixIndex(3, QualityMath.ToPoints(100f, 0f, 0f));
        string wantCode = wantIdx switch
        {
            0 => "sturdy",
            1 => "reinforced",
            _ => "fortified"
        };
        int wantGrade = QualityMath.GradeIndex(5, 100f);
        string wantGradeLang = $"prosequor:affix-quality-{wantGrade + 1}";
        if (stampedAffixes.Count != 2
            || !string.Equals(stampedAffixes[0].Code, ItemAffixes.QualityCode, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(stampedAffixes[0].LangKey, wantGradeLang, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(stampedAffixes[1].Code, wantCode, StringComparison.OrdinalIgnoreCase)
            || ProsequorStackPedigree.TryGetQualityRank(stamped, out _))
        {
            Assert.Fail(string.Format(
                "[prosequor] Quality fixture failed (grade+affix stamp). count={0} front={1}/{2} wantGrade={3} rule={4} wantRule={5}.",
                stampedAffixes.Count,
                stampedAffixes.Count > 0 ? stampedAffixes[0].Code : "(none)",
                stampedAffixes.Count > 0 ? stampedAffixes[0].LangKey : "(none)",
                wantGradeLang,
                stampedAffixes.Count > 1 ? stampedAffixes[1].Code : "(none)",
                wantCode));
            return;
        }

        FixtureSkillRegistry noAffixSkills = new();
        noAffixSkills.Register(new SkillDef { Id = "tailoring", Rules = noAffixRules });
        AbilityPipeline noAffixPipeline = new(actions, noAffixSkills);
        ItemStack gradeOnly = new();
        CraftMutateOutputContext gradeOnlyContext = new()
        {
            Progress = progress,
            Fact = fact,
            Crafted = gradeOnly,
            Pipeline = noAffixPipeline,
            Rand = new FixedDoubleRandom(1.0)
        };
        if (!CraftMutateOutputStation.TryWarmQualityKnobs(gradeOnlyContext, noAffixPipeline, progress))
        {
            Assert.Fail("[prosequor] Quality fixture failed (no-affix WarmUp).");
            return;
        }

        noAffixPipeline.Run(
            HookIds.CraftingInteraction,
            VerbIds.ApplyQuality,
            HookIds.Attributes,
            gradeOnlyContext,
            gradeOnly);
        IReadOnlyList<ItemAffixEntry> gradeOnlyAffixes = ItemAffixes.GetAll(gradeOnly);
        if (gradeOnlyAffixes.Count != 1
            || !string.Equals(gradeOnlyAffixes[0].Code, ItemAffixes.QualityCode, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(gradeOnlyAffixes[0].LangKey, wantGradeLang, StringComparison.OrdinalIgnoreCase))
        {
            Assert.Fail(string.Format(
                "[prosequor] Quality fixture failed (no-affix should stamp grade only). count={0} front={1}/{2}.",
                gradeOnlyAffixes.Count,
                gradeOnlyAffixes.Count > 0 ? gradeOnlyAffixes[0].Code : "(none)",
                gradeOnlyAffixes.Count > 0 ? gradeOnlyAffixes[0].LangKey : "(none)"));
            return;
        }

        // Second craft: WarmUp knob phases memo-hit; stamps only read context knobs.
        int hitsBeforeCraft = progress.ComposeMemo.HitCount;
        ItemStack stamped2 = new();
        CraftMutateOutputContext stampContext2 = new()
        {
            Progress = progress,
            Fact = fact,
            Crafted = stamped2,
            Pipeline = pipeline,
            Rand = new FixedDoubleRandom(1.0)
        };
        if (!CraftMutateOutputStation.TryWarmQualityKnobs(stampContext2, pipeline, progress))
        {
            Assert.Fail("[prosequor] Quality fixture failed (second WarmUp).");
            return;
        }

        if (progress.ComposeMemo.HitCount < hitsBeforeCraft + 1)
        {
            Assert.Fail(string.Format(
                "[prosequor] Quality fixture failed (second WarmUp should memo knobs). hitsBefore={0} hits={1}.",
                hitsBeforeCraft,
                progress.ComposeMemo.HitCount));
            return;
        }

        pipeline.Run(
            HookIds.CraftingInteraction,
            VerbIds.ApplyQuality,
            HookIds.Attributes,
            stampContext2,
            stamped2);

        // Multi-stamp: WarmUp once, then attributes fold with Pipeline cleared — stamps must not Run knobs.
        List<string> multiErrors = new();
        int multiOrder = 0;
        List<AbilityRule>? multiRules = AbilityRuleCompiler.CompileEffects(
            "tailoring",
            "armorproficiency",
            1,
            [
                new AbilityEffectJson
                {
                    hook = "prosequor:crafting-interaction",
                    verb = "prosequor:apply-quality",
                    phase = "attributes",
                    action = "prosequor:quality",
                    when = new AbilityWhenJson { tags = ["target:<armor>"] },
                    @params = JObject.Parse(
                        """{ "key": "protection", "op": "scale", "table": [0, 0.05, 0.1], "affixes": "prosequor:durability", "bonus": 0 }""")
                },
                new AbilityEffectJson
                {
                    hook = "prosequor:crafting-interaction",
                    verb = "prosequor:apply-quality",
                    phase = "attributes",
                    action = "prosequor:quality",
                    when = new AbilityWhenJson { tags = ["target:<armor>"] },
                    @params = JObject.Parse(
                        """{ "key": "durability", "op": "scale", "table": [0, 0.1], "affixes": "prosequor:durability", "bonus": 0 }""")
                }
            ],
            hooks,
            actions,
            armorCollections,
            multiErrors,
            ref multiOrder);
        if (multiRules == null || multiRules.Count != 2 || multiErrors.Count > 0)
        {
            Assert.Fail(string.Format(
                "[prosequor] Quality fixture failed (multi-stamp compile): {0}",
                string.Join("; ", multiErrors)));
            return;
        }

        FixtureSkillRegistry multiSkills = new();
        multiSkills.Register(new SkillDef { Id = "tailoring", Rules = multiRules });
        AbilityPipeline multiPipeline = new(actions, multiSkills);
        MemoFixtureProgress multiProgress = new();
        multiProgress.SetSkillLevel("tailoring", 100);
        multiProgress.SetUnlockTier("tailoring", "armorproficiency", 1);

        ItemStack multiStack = new();
        CraftMutateOutputContext multiContext = new()
        {
            Progress = multiProgress,
            Fact = fact,
            Crafted = multiStack,
            Pipeline = multiPipeline,
            Rand = new FixedDoubleRandom(1.0)
        };
        if (!CraftMutateOutputStation.TryWarmQualityKnobs(multiContext, multiPipeline, multiProgress))
        {
            Assert.Fail("[prosequor] Quality fixture failed (multi WarmUp).");
            return;
        }

        float warmedBase = multiContext.QualityBase;
        float warmedWindow = multiContext.QualityWindow;
        float warmedRolls = multiContext.QualityRolls;
        float warmedBonus = multiContext.QualityBonus;
        multiContext = new CraftMutateOutputContext
        {
            Progress = multiProgress,
            Fact = fact,
            Crafted = multiStack,
            Pipeline = null,
            Rand = new FixedDoubleRandom(1.0),
            QualityKnobsReady = true,
            QualityBase = warmedBase,
            QualityWindow = warmedWindow,
            QualityRolls = warmedRolls,
            QualityBonus = warmedBonus
        };

        multiPipeline.Run(
            HookIds.CraftingInteraction,
            VerbIds.ApplyQuality,
            HookIds.Attributes,
            multiContext,
            multiStack);

        IReadOnlyList<ItemAffixEntry> multiAffixes = ItemAffixes.GetAll(multiStack);
        // Two stamps share one WarmUp; with Pipeline cleared they still stamp grade + rule affixes.
        if (multiAffixes.Count < 2
            || !string.Equals(multiAffixes[0].Code, ItemAffixes.QualityCode, StringComparison.OrdinalIgnoreCase))
        {
            Assert.Fail(string.Format(
                "[prosequor] Quality fixture failed (multi-stamp without Pipeline). count={0} front={1}.",
                multiAffixes.Count,
                multiAffixes.Count > 0 ? multiAffixes[0].Code : "(none)"));
        }

        CollectionIndex mealCollections = WithCodes(
            NewCollections("meal"),
            ("meal", "game:bowl-meal"));

        List<string> badKeyErrors = new();
        int badKeyOrder = 0;
        List<AbilityRule>? badKey = AbilityRuleCompiler.CompileEffects(
            "cooking",
            nodeId: null,
            tier: null,
            [
                new AbilityEffectJson
                {
                    hook = "prosequor:crafting-interaction",
                    verb = "prosequor:apply-quality",
                    phase = "attributes",
                    action = "prosequor:quality-rank",
                    when = new AbilityWhenJson { tags = ["target:<meal>"] },
                    @params = JObject.Parse("""{ "key": "protection", "table": [0, 5] }""")
                }
            ],
            hooks,
            actions,
            mealCollections,
            badKeyErrors,
            ref badKeyOrder);
        if (badKey != null || badKeyErrors.Count == 0)
        {
            Assert.Fail("[prosequor] Quality-rank fixture failed (key must be qualityRank).");
            return;
        }

        List<string> rankErrors = new();
        int rankOrder = 0;
        List<AbilityRule>? rankRules = AbilityRuleCompiler.CompileEffects(
            "cooking",
            nodeId: null,
            tier: null,
            [
                new AbilityEffectJson
                {
                    hook = "prosequor:crafting-interaction",
                    verb = "prosequor:apply-quality",
                    phase = "attributes",
                    action = "prosequor:quality-rank",
                    when = new AbilityWhenJson { tags = ["target:<meal>"] },
                    @params = JObject.Parse("""{ "key": "rank", "table": [0, 5] }""")
                }
            ],
            hooks,
            actions,
            mealCollections,
            rankErrors,
            ref rankOrder);
        if (rankRules == null
            || rankRules.Count != 1
            || rankRules[0].Parameters is not ApplyQualityRankParams rankParsed
            || rankParsed.Table.Count != 2
            || rankErrors.Count > 0)
        {
            Assert.Fail(string.Format(
                "[prosequor] Quality-rank fixture failed (compile): {0}",
                string.Join("; ", rankErrors)));
            return;
        }

        FixtureSkillRegistry rankSkills = new();
        rankSkills.Register(new SkillDef { Id = "cooking", Rules = rankRules });
        AbilityPipeline rankPipeline = new(actions, rankSkills);
        MemoFixtureProgress rankProgress = new();
        rankProgress.SetSkillLevel("cooking", 100);
        AbilityAction rankFact = new()
        {
            Verb = VerbIds.ApplyQuality.Value,
            ActorUid = "fixture",
            Held = CallerIdentities.Grid,
            Target = "game:bowl-meal"
        };
        ItemStack rankStack = new();
        CraftMutateOutputContext rankContext = new()
        {
            Progress = rankProgress,
            Fact = rankFact,
            Crafted = rankStack,
            Pipeline = rankPipeline,
            Rand = new FixedDoubleRandom(1.0)
        };
        if (!CraftMutateOutputStation.TryWarmQualityKnobs(rankContext, rankPipeline, rankProgress))
        {
            Assert.Fail("[prosequor] Quality-rank fixture failed (WarmUp).");
            return;
        }

        rankPipeline.Run(
            HookIds.CraftingInteraction,
            VerbIds.ApplyQuality,
            HookIds.Attributes,
            rankContext,
            rankStack);

        int wantPoints = QualityMath.ToPoints(100f, 0f, 0f);
        int wantRank = (int)Math.Round(
            QualityMath.LerpTable(new float[] { 0f, 5f }, wantPoints),
            MidpointRounding.AwayFromZero);
        IReadOnlyList<ItemAffixEntry> rankAffixes = ItemAffixes.GetAll(rankStack);
        if (rankAffixes.Count != 1
            || !string.Equals(rankAffixes[0].Code, ItemAffixes.QualityCode, StringComparison.OrdinalIgnoreCase)
            || !ProsequorStackPedigree.TryGetQualityRank(rankStack, out int gotRank)
            || gotRank != wantRank)
        {
            Assert.Fail(string.Format(
                "[prosequor] Quality-rank fixture failed (stamp). count={0} front={1} rank={2} wantRank={3}.",
                rankAffixes.Count,
                rankAffixes.Count > 0 ? rankAffixes[0].Code : "(none)",
                ProsequorStackPedigree.TryGetQualityRank(rankStack, out int shown) ? shown : 0,
                wantRank));
        }
    }

    static void VerifyMashRankAddsToDistilledQualityBase()
    {
        HookRegistry hooks = new();
        AbilityActionRegistry actions = new(hooks);
        AffixListRegistry affixLists = new();
        affixLists.ReplaceAll(
        [
            new AffixListDef
            {
                Id = "prosequor:quality",
                Entries =
                [
                    new AffixListEntryDef { Code = "quality-1", Lang = "prosequor:affix-quality-1" },
                    new AffixListEntryDef { Code = "quality-2", Lang = "prosequor:affix-quality-2" },
                    new AffixListEntryDef { Code = "quality-3", Lang = "prosequor:affix-quality-3" },
                    new AffixListEntryDef { Code = "quality-4", Lang = "prosequor:affix-quality-4" },
                    new AffixListEntryDef { Code = "quality-5", Lang = "prosequor:affix-quality-5" }
                ]
            },
            new AffixListDef
            {
                Id = "prosequor:intoxication",
                Entries =
                [
                    new AffixListEntryDef
                    {
                        Code = "intoxication-1",
                        Lang = "prosequor:affix-intoxication-1"
                    },
                    new AffixListEntryDef
                    {
                        Code = "intoxication-5",
                        Lang = "prosequor:affix-intoxication-5"
                    }
                ]
            }
        ]);
        AbilityBootstrap.RegisterBuiltIns(hooks, actions, affixLists);

        CollectionIndex distilledCollections = WithCodes(
            NewCollections("distilled"),
            ("distilled", "game:alcoholportion"));

        List<string> errors = new();
        int order = 0;
        List<AbilityRule>? rules = AbilityRuleCompiler.CompileEffects(
            "cooking",
            nodeId: null,
            tier: null,
            [
                new AbilityEffectJson
                {
                    hook = "prosequor:crafting-interaction",
                    verb = "prosequor:apply-quality",
                    phase = "quality-base",
                    action = "prosequor:number",
                    when = new AbilityWhenJson { tags = ["target:<distilled>"] },
                    @params = JObject.Parse("""{ "op": "add", "value": -100 }""")
                },
                new AbilityEffectJson
                {
                    hook = "prosequor:crafting-interaction",
                    verb = "prosequor:apply-quality",
                    phase = "attributes",
                    action = "prosequor:quality",
                    when = new AbilityWhenJson { tags = ["target:<distilled>"] },
                    @params = JObject.Parse(
                        """{ "key": "intoxication", "op": "scale", "table": [0, 0.3], "affixes": "prosequor:intoxication" }""")
                }
            ],
            hooks,
            actions,
            distilledCollections,
            errors,
            ref order);
        if (rules == null || rules.Count != 2 || errors.Count > 0)
        {
            Assert.Fail(string.Format(
                "[prosequor] Mash-rank distilled fixture failed (compile): {0}",
                string.Join("; ", errors)));
            return;
        }

        FixtureSkillRegistry skills = new();
        skills.Register(new SkillDef { Id = "cooking", Rules = rules });
        AbilityPipeline pipeline = new(actions, skills);
        MemoFixtureProgress progress = new();
        progress.SetSkillLevel("cooking", 100);
        AbilityAction fact = new()
        {
            Verb = VerbIds.ApplyQuality.Value,
            ActorUid = "fixture",
            Held = CallerIdentities.Grid,
            Target = "game:alcoholportion"
        };

        ItemStack withoutRank = new();
        CraftMutateOutputContext skipContext = new()
        {
            Progress = progress,
            Fact = fact,
            Crafted = withoutRank,
            Pipeline = pipeline,
            Collections = distilledCollections,
            Rand = new FixedDoubleRandom(1.0)
        };
        if (!CraftMutateOutputStation.TryWarmQualityKnobs(skipContext, pipeline, progress))
        {
            Assert.Fail("[prosequor] Mash-rank distilled fixture failed (skip WarmUp).");
            return;
        }

        // Level 100 seed -200 + skill 100 + distilled -100 = -200; high roll → raw 0 → no stamp.
        if (Math.Abs(skipContext.QualityBase - (QualityMath.BaseSeed(100) - 100f)) > 0.0001f)
        {
            Assert.Fail(string.Format(
                "[prosequor] Mash-rank distilled fixture failed (base). got={0} want={1}.",
                skipContext.QualityBase,
                QualityMath.BaseSeed(100) - 100f));
            return;
        }

        pipeline.Run(
            HookIds.CraftingInteraction,
            VerbIds.ApplyQuality,
            HookIds.Attributes,
            skipContext,
            withoutRank);
        if (ItemAffixes.GetAll(withoutRank).Count != 0)
        {
            Assert.Fail("[prosequor] Distilled quality with no mash rank should skip at this seed.");
            return;
        }

        ItemStack withRank = new();
        CraftMutateOutputContext stampContext = new()
        {
            Progress = progress,
            Fact = fact,
            Crafted = withRank,
            Pipeline = pipeline,
            Collections = distilledCollections,
            Rand = new FixedDoubleRandom(1.0)
        };
        if (!CraftMutateOutputStation.TryWarmQualityKnobs(stampContext, pipeline, progress))
        {
            Assert.Fail("[prosequor] Mash-rank distilled fixture failed (stamp WarmUp).");
            return;
        }

        const int mashRank = 150;
        stampContext.QualityBase += ProsequorLiquidPedigree.QualityBaseBonus(mashRank);
        pipeline.Run(
            HookIds.CraftingInteraction,
            VerbIds.ApplyQuality,
            HookIds.Attributes,
            stampContext,
            withRank);
        ProsequorLiquidPedigree.StripQualityAndRank(withRank);

        IReadOnlyList<ItemAffixEntry> affixes = ItemAffixes.GetAll(withRank);
        float raw = stampContext.QualityBase + stampContext.QualityWindow;
        int wantPoints = QualityMath.ToPoints(raw, 0f, 0f);
        int wantAffix = QualityMath.AffixIndex(2, wantPoints);
        string wantCode = wantAffix == 0 ? "intoxication-1" : "intoxication-5";
        if (affixes.Count != 1
            || !string.Equals(affixes[0].Code, wantCode, StringComparison.OrdinalIgnoreCase)
            || string.Equals(affixes[0].Code, ItemAffixes.QualityCode, StringComparison.OrdinalIgnoreCase)
            || ProsequorStackPedigree.TryGetQualityRank(withRank, out _))
        {
            Assert.Fail(string.Format(
                "[prosequor] Mash rank must lift distilled quality-base; spirit keeps bonus affix, no grade/rank. count={0} code={1} want={2} rank={3}.",
                affixes.Count,
                affixes.Count > 0 ? affixes[0].Code : "(none)",
                wantCode,
                ProsequorStackPedigree.TryGetQualityRank(withRank, out int shown) ? shown : 0));
        }
    }

    sealed class FixedDoubleRandom : Random
    {
        readonly double value;

        public FixedDoubleRandom(double value) => this.value = value;

        public override double NextDouble() => value;
    }

    static void VerifyCraftRefundPipeline()
    {
        HookRegistry hooks = new();
        AbilityActionRegistry actions = new(hooks);
        AbilityBootstrap.RegisterBuiltIns(hooks, actions);

        if (!actions.TryGet(
                ActionIds.RefundIngredients,
                HookIds.CraftingInteraction,
                VerbIds.MutateOutput,
                HookIds.Refund,
                out _))
        {
            Assert.Fail("[prosequor] Craft-refund fixture failed (action registration).");
            return;
        }

        if (CraftMutateOutputStation.ComputeRefund(1, 1, 0) != 0
            || CraftMutateOutputStation.ComputeRefund(1, 1, 1) != 0
            || CraftMutateOutputStation.ComputeRefund(1, 1, 2) != 1
            || CraftMutateOutputStation.ComputeRefund(2, 1, 2) != 1
            || CraftMutateOutputStation.ComputeRefund(2, 1, 5) != 2
            || CraftMutateOutputStation.ComputeRefund(0, 1, 5) != 0
            || CraftMutateOutputStation.ComputeRefund(3, 0, 2) != 2
            || CraftMutateOutputStation.ComputeRefund(1, 0, 1) != 1)
        {
            Assert.Fail("[prosequor] Craft-refund fixture failed (ComputeRefund math).");
            return;
        }

        if (!actions.TryGet(
                ActionIds.Chance,
                HookIds.CraftingInteraction,
                VerbIds.MutateOutput,
                HookIds.Refund,
                out _))
        {
            Assert.Fail("[prosequor] Craft-refund fixture failed (chance gate registration).");
            return;
        }

        CollectionIndex collections = NewCollections("thread");
        collections.AddCode("thread", "game:flaxtwine");

        if (!TagCriterionParser.TryParse(
                "input:<thread>",
                collections,
                out TagCriterion? inputWhen,
                out string inputError)
            || inputWhen == null)
        {
            Assert.Fail(string.Format(
                "[prosequor] Craft-refund fixture failed (input when parse): {0}",
                inputError));
            return;
        }

        AbilityAction withThread = new()
        {
            Verb = CraftMutateOutputStation.VerbCraft,
            ActorUid = "fixture",
            Inputs = ["game:flaxtwine", "game:flaxfiber"]
        };
        AbilityAction withoutThread = new()
        {
            Verb = CraftMutateOutputStation.VerbCraft,
            ActorUid = "fixture",
            Inputs = ["game:flaxfiber"]
        };
        if (!inputWhen.Matches(withThread, collections)
            || inputWhen.Matches(withoutThread, collections))
        {
            Assert.Fail("[prosequor] Craft-refund fixture failed (input:<thread> match).");
            return;
        }

        int order = 0;
        List<string> errors = new();
        List<AbilityRule>? rules = AbilityRuleCompiler.CompileEffects(
            "tailoring",
            "spinthrifty",
            1,
            [
                new AbilityEffectJson
                {
                    hook = "prosequor:crafting-interaction",
                    verb = "prosequor:mutate-output",
                    phase = "refund",
                    action = "prosequor:refund-ingredients",
                    when = new AbilityWhenJson { tags = ["input:<thread>"] },
                    @params = JObject.Parse(
                        """{ "match": "<thread>", "amount": 1, "retain": 1 }""")
                }
            ],
            hooks,
            actions,
            collections,
            errors,
            ref order);
        if (rules == null || rules.Count != 1 || errors.Count > 0
            || rules[0].Parameters is not RefundIngredientsParams parsed
            || parsed.Amount != 1
            || parsed.Retain != 1
            || !string.Equals(parsed.Match, "thread", StringComparison.OrdinalIgnoreCase))
        {
            Assert.Fail(string.Format(
                "[prosequor] Craft-refund fixture failed (spinthrifty compile): {0}",
                string.Join("; ", errors)));
            return;
        }

        FixtureSkillRegistry skills = new();
        skills.Register(new SkillDef
        {
            Id = "tailoring",
            Rules = [rules[0]]
        });

        AbilityPipeline pipeline = new(actions, skills);
        FixtureProgress progress = new();
        progress.SetSkillLevel("tailoring", 40);
        progress.SetUnlockTier("tailoring", "spinthrifty", 1);

        CraftMutateOutputContext missContext = new()
        {
            Progress = progress,
            Fact = withoutThread,
            Collections = collections
        };
        if (pipeline.Run(HookIds.CraftingInteraction, VerbIds.MutateOutput, HookIds.Refund, missContext, 0) != 0)
        {
            Assert.Fail("[prosequor] Craft-refund fixture failed (when should miss without thread input).");
            return;
        }

        CraftMutateOutputContext hitContext = new()
        {
            Progress = progress,
            Fact = withThread,
            Collections = collections
        };
        // No craft inventory in pure fixtures: action no-ops and returns seed.
        if (pipeline.Run(HookIds.CraftingInteraction, VerbIds.MutateOutput, HookIds.Refund, hitContext, 0) != 0)
        {
            Assert.Fail("[prosequor] Craft-refund fixture failed (no-inventory refund should stay 0).");
        }

        order = 0;
        errors.Clear();
        collections.EnsureKey("construction-base");
        collections.AddCode("construction-base", "game:plank-oak");
        List<AbilityRule>? chanceRules = AbilityRuleCompiler.CompileEffects(
            "construction",
            null,
            null,
            [
                new AbilityEffectJson
                {
                    hook = "prosequor:crafting-interaction",
                    verb = "prosequor:mutate-output",
                    phase = "refund",
                    action = "prosequor:chance",
                    when = new AbilityWhenJson { tags = ["block", "input:<construction-base>"] },
                    @params = JObject.Parse(
                        """
                        {
                          "chance": { "base": 0, "perSkillLevel": 0.002, "cap": 0 },
                          "onSuccess": {
                            "action": "prosequor:refund-ingredients",
                            "params": { "match": "<construction-base>", "amount": 1, "retain": 0 }
                          }
                        }
                        """)
                }
            ],
            hooks,
            actions,
            collections,
            errors,
            ref order);
        if (chanceRules == null || chanceRules.Count != 1 || errors.Count > 0
            || chanceRules[0].Parameters is not ChanceGateParams chanceParsed
            || chanceParsed.OnSuccess == null)
        {
            Assert.Fail(string.Format(
                "[prosequor] Craft-refund fixture failed (construction chance compile): {0}",
                string.Join("; ", errors)));
        }
    }

    static void VerifyLastCraftPipeline()
    {
        HookRegistry hooks = new();
        AbilityActionRegistry actions = new(hooks);
        AbilityBootstrap.RegisterBuiltIns(hooks, actions);

        if (!actions.TryGet(
                ActionIds.Number,
                HookIds.ItemInteraction,
                VerbIds.CraftDamaged,
                HookIds.Amount,
                out _))
        {
            Assert.Fail("[prosequor] Last-craft fixture failed (action registration).");
            return;
        }

        if (!LastCraftStation.ClassifyProductTags("clothes-plain-shirt").Contains(RepairStation.TagClothing)
            || !LastCraftStation.ClassifyProductTags("armor-body-leather").Contains(RepairStation.TagArmor)
            || LastCraftStation.ClassifyProductTags("linen-normal-down").Count != 0)
        {
            Assert.Fail("[prosequor] Last-craft fixture failed (product tag classify).");
            return;
        }

        CollectionIndex collections = NewCollections("clothing", "armor");
        collections.AddCode("clothing", "game:clothes-plain-shirt");
        collections.AddCode("armor", "game:armor-body-leather");

        FixtureSkillRegistry skills = new();
        skills.Register(new SkillDef
        {
            Id = "tailoring",
            Rules =
            [
                new AbilityRule
                {
                    RuleId = "careful-stitching",
                    Hook = HookIds.ItemInteraction,
                    Verb = VerbIds.CraftDamaged,
                    Phase = HookIds.Amount,
                    Action = ActionIds.Number,
                    When = CompileWhen(
                        collections,
                        "last-craft:<clothing, armor>"),
                    Parameters = ParseNumberSpec("{\"op\":\"scale\",\"value\":-0.33}"),
                    Source = new AbilityRuleSource
                    {
                        SkillId = "tailoring",
                        NodeId = "careful-stitching",
                        Tier = 1
                    },
                    Priority = 0,
                    SourceOrder = 0
                }
            ]
        });

        AbilityPipeline pipeline = new(actions, skills);
        FixtureProgress progress = new();
        progress.SetSkillLevel("tailoring", 30);
        progress.SetUnlockTier("tailoring", "careful-stitching", 1);

        LastCraftStation.Remember(
            "fixture",
            new AbilityAction
            {
                Verb = LastCraftStation.VerbCraft,
                ActorUid = "fixture",
                Target = "game:clothes-plain-shirt"
            });

        ItemDurabilityContext craftUsage = new()
        {
            World = null!,
            ByEntity = null!,
            ItemSlot = null!,
            Collectible = null!,
            Progress = progress,
            Fact = new AbilityAction
            {
                Verb = VerbIds.CraftDamaged.Value,
                ActorUid = "fixture",
                LastCraft = "game:clothes-plain-shirt"
            }
        };

        int scaled = pipeline.Run(HookIds.ItemInteraction, VerbIds.CraftDamaged, HookIds.Amount, craftUsage, 100);
        int expected = AbilityFormulas.StochasticRound(100 * 0.67, new Random(0));
        if (scaled != expected)
        {
            Assert.Fail(string.Format(
                "[prosequor] Last-craft fixture failed (clothing scale). got={0} want={1}.",
                scaled,
                expected));
        }

        craftUsage = new()
        {
            World = null!,
            ByEntity = null!,
            ItemSlot = null!,
            Collectible = null!,
            Progress = progress,
            Fact = new AbilityAction
            {
                Verb = VerbIds.CraftDamaged.Value,
                ActorUid = "fixture",
                LastCraft = "game:armor-body-leather"
            }
        };

        int armorScaled = pipeline.Run(HookIds.ItemInteraction, VerbIds.CraftDamaged, HookIds.Amount, craftUsage, 100);
        if (armorScaled != expected)
        {
            Assert.Fail(string.Format(
                "[prosequor] Last-craft fixture failed (armor scale via OR). got={0} want={1}.",
                armorScaled,
                expected));
        }

        LastCraftStation.Remember(
            "fixture",
            new AbilityAction
            {
                Verb = LastCraftStation.VerbCraft,
                ActorUid = "fixture",
                Target = "game:linen-normal-down"
            });

        craftUsage = new()
        {
            World = null!,
            ByEntity = null!,
            ItemSlot = null!,
            Collectible = null!,
            Progress = progress,
            Fact = new AbilityAction
            {
                Verb = VerbIds.CraftDamaged.Value,
                ActorUid = "fixture",
                LastCraft = "game:linen-normal-down"
            }
        };

        int untouched = pipeline.Run(HookIds.ItemInteraction, VerbIds.CraftDamaged, HookIds.Amount, craftUsage, 100);
        if (untouched != 100)
        {
            Assert.Fail(string.Format(
                "[prosequor] Last-craft fixture failed (non-clothing should not scale). got={0} want=100.",
                untouched));
        }

        ItemDurabilityContext breakUsage = new()
        {
            World = null!,
            ByEntity = null!,
            ItemSlot = null!,
            Collectible = null!,
            Progress = progress,
            Fact = new AbilityAction
            {
                Verb = VerbIds.BlockDamaged.Value,
                ActorUid = "fixture",
                LastCraft = "game:clothes-plain-shirt"
            }
        };
        int breakUntouched = pipeline.Run(
            HookIds.ItemInteraction,
            VerbIds.BlockDamaged,
            HookIds.Amount,
            breakUsage,
            100);
        if (breakUntouched != 100)
        {
            Assert.Fail(string.Format(
                "[prosequor] Last-craft fixture failed (block-damaged must not match craft-damaged rules). got={0} want=100.",
                breakUntouched));
        }
    }

    static void VerifyItemFreshnessAction()
    {
        IncreaseFreshnessStackAction action = new();
        if (!action.TryParseParams(
                JObject.Parse("{\"op\":\"scale\",\"base\":0.25,\"perSkillLevel\":0,\"cap\":0.25}"),
                out object parameters,
                out _)
            || parameters is not NumberSpec)
        {
            Assert.Fail("[prosequor] Item freshness fixture failed (action params).");
            return;
        }

        if (action.Hook != HookIds.BlockInteraction
            || action.Verb != VerbIds.MutateDrops
            || action.Phase != HookIds.Stack
            || action.Id != ActionIds.IncreaseFreshness)
        {
            Assert.Fail("[prosequor] Item freshness fixture failed (hook registration).");
            return;
        }

        if (ItemFreshnessApplicator.CanImproveFreshness(null!, null)
            || ItemFreshnessApplicator.CanImproveFreshness(null!, null))
        {
            Assert.Fail("[prosequor] Item freshness fixture failed (null stack should reject).");
        }
    }

    static void VerifyPipelineBehavior()
    {
        HookRegistry hooks = new();
        AbilityActionRegistry actions = new(hooks);
        AbilityBootstrap.RegisterBuiltIns(hooks, actions);

        // Fixture-only chaining actions on interaction-speed / value.
        actions.Register(new FixtureAddAction("prosequor:fixture-add-a", amount: 1f, priorityUnused: 0));
        actions.Register(new FixtureAddAction("prosequor:fixture-add-b", amount: 10f, priorityUnused: 0));

        FixtureSkillRegistry skills = new();
        skills.Register(new SkillDef
        {
            Id = "fixture-skill",
            Rules =
            [
                new AbilityRule
                {
                    RuleId = "root",
                    Hook = HookIds.BlockInteraction,
                    Verb = VerbIds.InteractionSpeed,
                    Phase = HookIds.Default,
                    Action = ActionIds.Number,
                    When = CompileWhen(
                        WithCodes(NewCollections("soil", "shovel"), ("soil", "game:soil-low-none"), ("shovel", "game:shovel-copper")),
                        "target:<soil>",
                        "held:<shovel>"),
                    Parameters = ParseNumberSpec("{\"op\":\"scale\",\"base\":0,\"perSkillLevel\":0.1}"),
                    Source = new AbilityRuleSource { SkillId = "fixture-skill" },
                    Priority = 0,
                    SourceOrder = 1
                },
                new AbilityRule
                {
                    RuleId = "tier1",
                    Hook = HookIds.BlockInteraction,
                    Verb = VerbIds.InteractionSpeed,
                    Phase = HookIds.Default,
                    Action = new ActionId("prosequor:fixture-add-a"),
                    When = new AbilityWhenFilter(),
                    Parameters = 1f,
                    Source = new AbilityRuleSource
                    {
                        SkillId = "fixture-skill",
                        NodeId = "node",
                        Tier = 1
                    },
                    Priority = 10,
                    SourceOrder = 2
                },
                new AbilityRule
                {
                    RuleId = "tier2",
                    Hook = HookIds.BlockInteraction,
                    Verb = VerbIds.InteractionSpeed,
                    Phase = HookIds.Default,
                    Action = new ActionId("prosequor:fixture-add-b"),
                    When = new AbilityWhenFilter(),
                    Parameters = 10f,
                    Source = new AbilityRuleSource
                    {
                        SkillId = "fixture-skill",
                        NodeId = "node",
                        Tier = 2
                    },
                    Priority = 5,
                    SourceOrder = 3
                },
                new AbilityRule
                {
                    RuleId = "chain-low-prio-late",
                    Hook = HookIds.BlockInteraction,
                    Verb = VerbIds.InteractionSpeed,
                    Phase = HookIds.Default,
                    Action = new ActionId("prosequor:fixture-add-a"),
                    When = new AbilityWhenFilter(),
                    Parameters = 1f,
                    Source = new AbilityRuleSource
                    {
                        SkillId = "fixture-skill",
                        NodeId = "chain",
                        Tier = 1
                    },
                    Priority = 1,
                    SourceOrder = 99
                },
                new AbilityRule
                {
                    RuleId = "chain-high-prio-early",
                    Hook = HookIds.BlockInteraction,
                    Verb = VerbIds.InteractionSpeed,
                    Phase = HookIds.Default,
                    Action = new ActionId("prosequor:fixture-add-b"),
                    When = new AbilityWhenFilter(),
                    Parameters = 10f,
                    Source = new AbilityRuleSource
                    {
                        SkillId = "fixture-skill",
                        NodeId = "chain",
                        Tier = 1
                    },
                    Priority = 0,
                    SourceOrder = 100
                },
                new AbilityRule
                {
                    RuleId = "prevent-item-damage",
                    Hook = HookIds.ItemInteraction,
                    Verb = VerbIds.ItemDamage,
                    Phase = HookIds.Amount,
                    Action = ActionIds.Chance,
                    When = CompileWhen(
                        WithCodes(NewCollections("shovel"), ("shovel", "game:shovel-copper")),
                        "held:<shovel>"),
                    Parameters = BuildAlwaysNumberSetZero(actions),
                    Source = new AbilityRuleSource
                    {
                        SkillId = "fixture-skill",
                        NodeId = "careful",
                        Tier = 1
                    },
                    Priority = 0,
                    SourceOrder = 101
                },
                new AbilityRule
                {
                    RuleId = "base-scaled-speed",
                    Hook = HookIds.BlockInteraction,
                    Verb = VerbIds.InteractionSpeed,
                    Phase = HookIds.Default,
                    Action = ActionIds.Number,
                    When = CompileWhen(
                        WithCodes(NewCollections("soil", "shovel"), ("soil", "game:soil-low-none"), ("shovel", "game:shovel-copper")),
                        "target:<soil>",
                        "held:<shovel>"),
                    Parameters = ParseNumberSpec(
                        "{\"op\":\"add\",\"ofBase\":true,\"base\":0,\"perSkillLevel\":0.05}"),
                    Source = new AbilityRuleSource
                    {
                        SkillId = "fixture-skill",
                        NodeId = "expert",
                        Tier = 1
                    },
                    Priority = 0,
                    SourceOrder = 102
                }
            ]
        });

        AbilityPipeline pipeline = new(actions, skills);
        FixtureProgress progress = new();
        progress.SetSkillLevel("fixture-skill", 10);
        progress.SetUnlockTier("fixture-skill", "node", 1);
        progress.SetUnlockTier("fixture-skill", "chain", 1);
        progress.SetUnlockTier("fixture-skill", "careful", 1);
        // Expert stays locked until the base-scaled case; soil+shovel now matches root and expert.
        progress.SetUnlockTier("fixture-skill", "expert", 0);

        AbilityAction soilFact = new()
        {
            Verb = "prosequor:mine",
            ActorUid = "fixture",
            Target = "game:soil-low-none",
            Held = "game:shovel-copper"
        };

        InteractionSpeedContext context = new()
        {
            Progress = progress,
            Fact = soilFact,
            Material = EnumBlockMaterial.Soil,
            BaseValue = 1f
        };

        // Root multiply (1 + 0.1*10 = 2) then chain adds: priority 0 (+10) then priority 1 (+1) = 13.
        // Owned node tier 1 also adds +1 (priority 10). Tier 2 rule inactive.
        // Order: priority 0 (add 10), priority 1 (add 1), priority 10 (add 1) after multiply at priority 0 sourceOrder 1.
        // Matching sorted: root prio0/order1, chain-high prio0/order100, chain-low prio1/order99, tier1 prio10/order2.
        // Start 1 → *2 = 2 → +10 = 12 → +1 = 13 → +1 = 14.
        float result = pipeline.Run(HookIds.BlockInteraction, VerbIds.InteractionSpeed, HookIds.Default, context, 1f);
        if (Math.Abs(result - 14f) > 0.0001f)
        {
            Assert.Fail(string.Format("[prosequor] Pipeline fixture failed (owned tier / root / priority chaining). Got {0}, expected 14.",
                result));
        }

        // Locked node: only root multiply should apply when tags match.
        progress.SetUnlockTier("fixture-skill", "node", 0);
        progress.SetUnlockTier("fixture-skill", "chain", 0);
        float rootOnly = pipeline.Run(HookIds.BlockInteraction, VerbIds.InteractionSpeed, HookIds.Default, context, 1f);
        if (Math.Abs(rootOnly - 2f) > 0.0001f)
        {
            Assert.Fail(string.Format("[prosequor] Pipeline fixture failed (locked nodes). Got {0}, expected 2.",
                rootOnly));
        }

        // Rank replacement: owned tier 2 replaces tier 1.
        progress.SetUnlockTier("fixture-skill", "node", 2);
        float tier2 = pipeline.Run(HookIds.BlockInteraction, VerbIds.InteractionSpeed, HookIds.Default, context, 1f);
        if (Math.Abs(tier2 - 12f) > 0.0001f)
        {
            Assert.Fail(string.Format("[prosequor] Pipeline fixture failed (rank replacement). Got {0}, expected 12.",
                tier2));
        }

        // Non-matching tags: root mining rule skipped.
        context = new InteractionSpeedContext
        {
            Progress = progress,
            Fact = new AbilityAction
            {
                Verb = "prosequor:mine",
                ActorUid = "fixture",
                Target = "game:log-oak"
            },
            Material = EnumBlockMaterial.Wood,
            BaseValue = 1f
        };
        float wood = pipeline.Run(HookIds.BlockInteraction, VerbIds.InteractionSpeed, HookIds.Default, context, 1f);
        if (Math.Abs(wood - 11f) > 0.0001f)
        {
            Assert.Fail(string.Format("[prosequor] Pipeline fixture failed (tag filter). Got {0}, expected 11.",
                wood));
        }

        progress.SetUnlockTier("fixture-skill", "node", 0);
        progress.SetUnlockTier("fixture-skill", "expert", 1);
        context = new InteractionSpeedContext
        {
            Progress = progress,
            Fact = new AbilityAction
            {
                Verb = "prosequor:mine",
                ActorUid = "fixture",
                Target = "game:soil-low-none",
                Held = "game:shovel-copper"
            },
            Material = EnumBlockMaterial.Soil,
            BaseValue = 1f
        };
        float baseScaled = pipeline.Run(HookIds.BlockInteraction, VerbIds.InteractionSpeed, HookIds.Default, context, 1f);
        // Root doubles the value at level 10; expert adds 1 * 0.05 * 10 without compounding.
        if (Math.Abs(baseScaled - 2.5f) > 0.0001f)
        {
            Assert.Fail(string.Format("[prosequor] Pipeline fixture failed (base-scaled skill level). Got {0}, expected 2.5.",
                baseScaled));
        }

        ItemDurabilityContext damageContext = new()
        {
            World = null!,
            ByEntity = null!,
            ItemSlot = null!,
            Collectible = null!,
            Progress = progress,
            Fact = new AbilityAction
            {
                Verb = VerbIds.ItemDamage.Value,
                ActorUid = "fixture",
                Held = "game:shovel-copper"
            }
        };
        int prevented = pipeline.Run(HookIds.ItemInteraction, VerbIds.ItemDamage, HookIds.Amount, damageContext, 1);
        if (prevented != 0)
        {
            Assert.Fail(string.Format("[prosequor] Pipeline fixture failed (item damage prevention). Got {0}, expected 0.",
                prevented));
        }
    }

    static void VerifySkillXpAndSpecialization(
        IHookRegistry hooks,
        IAbilityActionRegistry actions)
    {
        FixtureSkillRegistry skills = new();
        SkillTreeNodeDef diggerNode = new()
        {
            Id = "digger",
            IsSpecialization = true,
            Tiers = [new SkillTreeTierDef { Cost = 1, MinSkillLevel = 1 }]
        };
        SkillTreeNodeDef foresterNode = new()
        {
            Id = "forester",
            IsSpecialization = true,
            Tiers = [new SkillTreeTierDef { Cost = 1, MinSkillLevel = 1 }]
        };

        SkillDef digging = new()
        {
            Id = "digging",
            Tree = new SkillTreeDef
            {
                Nodes = [diggerNode],
                ById = new Dictionary<string, SkillTreeNodeDef>(StringComparer.OrdinalIgnoreCase)
                {
                    [diggerNode.Id] = diggerNode
                }
            },
            Rules =
            [
                new AbilityRule
                {
                    RuleId = "digger-xp",
                    Hook = HookIds.Progress,
                    Verb = VerbIds.SkillXp,
                    Phase = HookIds.Amount,
                    Action = ActionIds.Number,
                    When = new AbilityWhenFilter(),
                    Parameters = ParseNumberSpec("{\"op\":\"scale\",\"value\":0.4}"),
                    Source = new AbilityRuleSource
                    {
                        SkillId = "digging",
                        NodeId = "digger",
                        Tier = 1
                    },
                    Priority = 0,
                    SourceOrder = 1
                }
            ]
        };
        SkillDef forestry = new()
        {
            Id = "forestry",
            Tree = new SkillTreeDef
            {
                Nodes = [foresterNode],
                ById = new Dictionary<string, SkillTreeNodeDef>(StringComparer.OrdinalIgnoreCase)
                {
                    [foresterNode.Id] = foresterNode
                }
            }
        };
        skills.Register(digging);
        skills.Register(forestry);

        AbilityPipeline pipeline = new(actions, skills);
        FixtureProgress progress = new();
        progress.SetUnlockTier("digging", "digger", 1);

        SkillXpContext digXp = new()
        {
            Progress = progress,
            Fact = new AbilityAction { Verb = "prosequor:dig", ActorUid = "fixture" },
            SkillId = "digging",
            BaseAmount = 10f
        };
        float boosted = pipeline.Run(HookIds.Progress, VerbIds.SkillXp, HookIds.Amount, digXp, 10f);
        if (Math.Abs(boosted - 14f) > 0.0001f)
        {
            Assert.Fail(string.Format("[prosequor] Skill-XP fixture failed (own-skill boost). Got {0}, expected 14.",
                boosted));
        }

        SkillXpContext forestXp = new()
        {
            Progress = progress,
            Fact = new AbilityAction { Verb = "prosequor:chop", ActorUid = "fixture" },
            SkillId = "forestry",
            BaseAmount = 10f
        };
        float isolated = pipeline.Run(HookIds.Progress, VerbIds.SkillXp, HookIds.Amount, forestXp, 10f);
        if (Math.Abs(isolated - 10f) > 0.0001f)
        {
            Assert.Fail(string.Format("[prosequor] Skill-XP fixture failed (cross-skill isolation). Got {0}, expected 10.",
                isolated));
        }

        SkillTreeNodeDef panningExpertNode = new()
        {
            Id = "panningexpert",
            Tiers = [new SkillTreeTierDef { Cost = 1, MinSkillLevel = 1 }]
        };
        SkillDef panning = new()
        {
            Id = "panning",
            Tree = new SkillTreeDef
            {
                Nodes = [panningExpertNode],
                ById = new Dictionary<string, SkillTreeNodeDef>(StringComparer.OrdinalIgnoreCase)
                {
                    [panningExpertNode.Id] = panningExpertNode
                }
            },
            Rules =
            [
                new AbilityRule
                {
                    RuleId = "panning-expert-xp",
                    Hook = HookIds.Progress,
                    Verb = VerbIds.SkillXp,
                    Phase = HookIds.Amount,
                    Action = ActionIds.Number,
                    When = new AbilityWhenFilter(),
                    Parameters = ParseNumberSpec("{\"op\":\"scale\",\"base\":0,\"perSkillLevel\":0.0001}"),
                    Source = new AbilityRuleSource
                    {
                        SkillId = "panning",
                        NodeId = "panningexpert",
                        Tier = 1
                    },
                    Priority = 0,
                    SourceOrder = 1
                },
                new AbilityRule
                {
                    RuleId = "panning-expert-bucket",
                    Hook = HookIds.Progress,
                    Verb = VerbIds.SkillBucket,
                    Phase = HookIds.Cap,
                    Action = ActionIds.Number,
                    When = new AbilityWhenFilter(),
                    Parameters = ParseNumberSpec("{\"op\":\"scale\",\"value\":1}"),
                    Source = new AbilityRuleSource
                    {
                        SkillId = "panning",
                        NodeId = "panningexpert",
                        Tier = 1
                    },
                    Priority = 0,
                    SourceOrder = 1
                }
            ]
        };
        skills.Register(panning);
        // RuleIndex is built at pipeline construction; rebuild so panning expert rules are visible.
        pipeline = new AbilityPipeline(actions, skills);
        progress.SetUnlockTier("panning", "panningexpert", 1);
        progress.SetSkillLevel("panning", 100);

        SkillBucketCapContext panCap = new()
        {
            Progress = progress,
            SkillId = "panning"
        };
        float capMult = pipeline.Run(HookIds.Progress, VerbIds.SkillBucket, HookIds.Cap, panCap, 1f);
        if (Math.Abs(capMult - 2f) > 0.0001f)
        {
            Assert.Fail(string.Format("[prosequor] Skill-bucket fixture failed (panning expert cap). Got {0}, expected 2.",
                capMult));
        }

        SkillXpContext panXp = new()
        {
            Progress = progress,
            Fact = new AbilityAction { Verb = "game:panning", ActorUid = "fixture" },
            SkillId = "panning",
            BaseAmount = 1f
        };
        float panBoost = pipeline.Run(HookIds.Progress, VerbIds.SkillXp, HookIds.Amount, panXp, 1f);
        if (Math.Abs(panBoost - 1.01f) > 0.0001f)
        {
            Assert.Fail(string.Format("[prosequor] Skill-XP fixture failed (skill-level scaling). Got {0}, expected 1.01.",
                panBoost));
        }

        IReadOnlyList<LevelUpRuleDef> levelUps = LevelUpRuleFixtures.DefaultRules();
        if (SpecializationPolicy.AllowedSlots(9, levelUps) != 0
            || SpecializationPolicy.AllowedSlots(10, levelUps) != 1
            || SpecializationPolicy.AllowedSlots(19, levelUps) != 1
            || SpecializationPolicy.AllowedSlots(20, levelUps) != 2)
        {
            Assert.Fail("[prosequor] Specialization fixture failed (slot boundaries 9/10/20).");
        }

        progress.SetPlayerLevel(10);
        progress.SetUnlockPoints(5);
        progress.SetSkillLevel("digging", 30);
        progress.SetSkillLevel("forestry", 30);
        progress.SetUnlockTier("digging", "digger", 0);
        progress.SetUnlockTier("forestry", "forester", 0);

        if (SpecializationPolicy.OwnedCount(progress, skills) != 0
            || !SpecializationPolicy.HasAvailableSlot(progress, skills, levelUps))
        {
            Assert.Fail("[prosequor] Specialization fixture failed (empty slot at level 10).");
        }

        UnlockPurchaseStatus ok = SkillTreeEligibility.Evaluate(
            digging, progress, skills, levelUps, "digger", out _);
        if (ok != UnlockPurchaseStatus.Ok)
        {
            Assert.Fail(string.Format("[prosequor] Specialization fixture failed (eligible at level 10). Status {0}.",
                ok));
        }

        progress.SetUnlockTier("digging", "digger", 1);
        if (SpecializationPolicy.OwnedCount(progress, skills) != 1
            || SpecializationPolicy.HasAvailableSlot(progress, skills, levelUps))
        {
            Assert.Fail("[prosequor] Specialization fixture failed (slot consumed).");
        }

        UnlockPurchaseStatus blocked = SkillTreeEligibility.Evaluate(
            forestry, progress, skills, levelUps, "forester", out _);
        if (blocked != UnlockPurchaseStatus.SpecializationLimitReached)
        {
            Assert.Fail(string.Format("[prosequor] Specialization fixture failed (cross-skill limit). Status {0}.",
                blocked));
        }

        progress.SetPlayerLevel(20);
        if (!SpecializationPolicy.HasAvailableSlot(progress, skills, levelUps)
            || SkillTreeEligibility.Evaluate(forestry, progress, skills, levelUps, "forester", out _)
                != UnlockPurchaseStatus.Ok)
        {
            Assert.Fail("[prosequor] Specialization fixture failed (second slot at level 20).");
        }

        progress.SetPlayerLevel(9);
        if (SkillTreeEligibility.Evaluate(forestry, progress, skills, levelUps, "forester", out _)
            != UnlockPurchaseStatus.SpecializationLimitReached)
        {
            Assert.Fail("[prosequor] Specialization fixture failed (no slots below level 10).");
        }

        int order = 0;
        SkillTreeJson multiTierSpec = new()
        {
            nodes =
            [
                new SkillTreeNodeJson
                {
                    id = "bad-spec",
                    specialization = true,
                    requires = [],
                    tiers =
                    [
                        new SkillTreeTierJson(),
                        new SkillTreeTierJson()
                    ]
                }
            ]
        };
        if (SkillTreeCompiler.Compile(
                "fixture-multi-tier-spec",
                100,
                multiTierSpec,
                hooks,
                actions,
                NewCollections(),
                ref order).Success)
        {
            Assert.Fail("[prosequor] Specialization fixture failed (multi-tier specialization should reject).");
        }

        SkillTreeJson goodSpec = new()
        {
            nodes =
            [
                new SkillTreeNodeJson
                {
                    id = "good-spec",
                    specialization = true,
                    descriptionLang = "spec",
                    descriptionParams =
                    [
                        new JObject
                        {
                            ["sum"] = new JArray("0.value"),
                            ["format"] = "fractionPercent"
                        }
                    ],
                    requires = [],
                    tiers =
                    [
                        new SkillTreeTierJson
                        {
                            effects =
                            [
                                new AbilityEffectJson
                                {
                                    hook = "prosequor:progress",
                    verb = "prosequor:skill-xp",
                                    phase = "amount",
                                    action = "prosequor:number",
                                    @params = new JObject { ["op"] = "scale", ["value"] = 0.4 }
                                }
                            ]
                        }
                    ]
                }
            ]
        };
        SkillTreeCompiler.CompileResult compiledSpec = SkillTreeCompiler.Compile(
            "fixture-good-spec",
            100,
            goodSpec,
            hooks,
            actions,
            NewCollections(),
            ref order);
        if (!compiledSpec.Success
            || compiledSpec.Tree == null
            || !compiledSpec.Tree.ById["good-spec"].IsSpecialization
            || compiledSpec.Tree.ById["good-spec"].MaxTier != 1
            || compiledSpec.Tree.ById["good-spec"].TierAt(1).DescriptionArgs.Count != 1
            || compiledSpec.Tree.ById["good-spec"].TierAt(1).DescriptionArgs[0]
                is not FormattedDescriptionArg { Value: 0.4m, Format: "fractionPercent" })
        {
            Assert.Fail("[prosequor] Specialization fixture failed (single-tier specialization compile).");
        }
    }

    static void VerifySkillContributions(
        IHookRegistry hooks,
        IAbilityActionRegistry actions)
    {
        Dictionary<string, SkillDefJson> drafts = new(StringComparer.OrdinalIgnoreCase)
        {
            ["digging"] = new SkillDefJson
            {
                id = "digging",
                tree = new SkillTreeJson
                {
                    nodes =
                    [
                        new SkillTreeNodeJson { id = "claydigger", requires = [] }
                    ]
                }
            }
        };

        List<string> warnings = new();
        List<SkillContributionMerger.PendingEntry> entries =
        [
            new()
            {
                SourceDomain = "mymod",
                SourcePath = "config/prosequor/contributions/a.json",
                SkillId = "digging",
                Nodes =
                [
                    new SkillTreeNodeJson
                    {
                        id = "mymod:deepclay",
                        requires = ["claydigger"]
                    },
                    new SkillTreeNodeJson
                    {
                        id = "mymod:deeperclay",
                        requires = ["mymod:deepclay"]
                    }
                ]
            },
            new()
            {
                SourceDomain = "mymod",
                SourcePath = "config/prosequor/contributions/bad.json",
                SkillId = "digging",
                Nodes =
                [
                    new SkillTreeNodeJson
                    {
                        id = "wrongmod:nope",
                        requires = ["claydigger"]
                    },
                    new SkillTreeNodeJson
                    {
                        id = "unprefixed",
                        requires = ["claydigger"]
                    }
                ]
            },
            new()
            {
                SourceDomain = "mymod",
                SourcePath = "config/prosequor/contributions/missing.json",
                SkillId = "nosuchskill",
                Nodes =
                [
                    new SkillTreeNodeJson
                    {
                        id = "mymod:orphan",
                        requires = []
                    }
                ]
            },
            new()
            {
                SourceDomain = "mymod",
                SourcePath = "config/prosequor/contributions/dup.json",
                SkillId = "digging",
                Nodes =
                [
                    new SkillTreeNodeJson
                    {
                        id = "mymod:deepclay",
                        requires = ["claydigger"]
                    }
                ]
            },
            new()
            {
                SourceDomain = "mymod",
                SourcePath = "config/prosequor/contributions/soft.json",
                SkillId = "digging",
                Nodes =
                [
                    new SkillTreeNodeJson
                    {
                        id = "mymod:needsmissing",
                        requires = ["does-not-exist"]
                    }
                ]
            }
        ];

        SkillContributionMerger.Apply(drafts, entries, warnings.Add);

        SkillTreeNodeJson[]? merged = drafts["digging"].tree?.nodes;
        if (merged == null
            || merged.Length != 3
            || merged.Count(n => n.id == "claydigger") != 1
            || merged.Count(n => n.id == "mymod:deepclay") != 1
            || merged.Count(n => n.id == "mymod:deeperclay") != 1)
        {
            Assert.Fail("[prosequor] Contribution fixture failed (merge two namespaced nodes onto Digging).");
        }

        if (!warnings.Any(w => w.Contains("wrongmod:nope", StringComparison.OrdinalIgnoreCase))
            || !warnings.Any(w => w.Contains("unprefixed", StringComparison.OrdinalIgnoreCase)))
        {
            Assert.Fail("[prosequor] Contribution fixture failed (namespace rejection warnings).");
        }

        if (!warnings.Any(w => w.Contains("nosuchskill", StringComparison.OrdinalIgnoreCase)))
        {
            Assert.Fail("[prosequor] Contribution fixture failed (unknown skill skip).");
        }

        if (!warnings.Any(w => w.Contains("duplicate node id", StringComparison.OrdinalIgnoreCase))
            || merged!.Count(n => string.Equals(n.id, "claydigger", StringComparison.OrdinalIgnoreCase))
                != 1)
        {
            Assert.Fail("[prosequor] Contribution fixture failed (duplicate id keeps original).");
        }

        if (!warnings.Any(w => w.Contains("mymod:needsmissing", StringComparison.OrdinalIgnoreCase)))
        {
            Assert.Fail("[prosequor] Contribution fixture failed (unmet requires soft-dep).");
        }

        int order = 0;
        SkillTreeCompiler.CompileResult compiled = SkillTreeCompiler.Compile(
            "digging",
            100,
            drafts["digging"].tree,
            hooks,
            actions,
            NewCollections(),
            ref order);
        if (!compiled.Success
            || compiled.Tree == null
            || !compiled.Tree.ById.ContainsKey("mymod:deepclay")
            || !compiled.Tree.ById.ContainsKey("mymod:deeperclay")
            || compiled.Tree.ById["mymod:deepclay"].AllRequirementIds[0] != "claydigger")
        {
            Assert.Fail("[prosequor] Contribution fixture failed (compiled grafted Digging tree).");
        }
    }

    static void VerifyContributionDisableReplace(
        IHookRegistry hooks,
        IAbilityActionRegistry actions)
    {
        Dictionary<string, SkillDefJson> drafts = new(StringComparer.OrdinalIgnoreCase)
        {
            ["fixture-disable"] = new SkillDefJson
            {
                id = "fixture-disable",
                tree = new SkillTreeJson
                {
                    nodes =
                    [
                        new SkillTreeNodeJson { id = "a", requires = [] },
                        new SkillTreeNodeJson { id = "b", requires = ["a"] },
                        new SkillTreeNodeJson { id = "e", requires = ["b"] },
                        new SkillTreeNodeJson
                        {
                            id = "c",
                            requires = SkillRequireJson.Mixed(new[] { "a", "b" }, "d")
                        },
                        new SkillTreeNodeJson { id = "d", requires = [] }
                    ]
                }
            },
            ["fixture-replace"] = new SkillDefJson
            {
                id = "fixture-replace",
                tree = new SkillTreeJson
                {
                    nodes =
                    [
                        new SkillTreeNodeJson { id = "root", requires = [] },
                        new SkillTreeNodeJson
                        {
                            id = "oldminer",
                            requires = ["root"],
                            excludes = ["rival"]
                        },
                        new SkillTreeNodeJson { id = "rival", requires = [], excludes = ["oldminer"] },
                        new SkillTreeNodeJson { id = "fan", requires = ["oldminer"] }
                    ]
                }
            }
        };

        List<string> warnings = new();
        SkillContributionMerger.Apply(
            drafts,
            [
                new SkillContributionMerger.PendingEntry
                {
                    SourceDomain = "mymod",
                    SourcePath = "disable.json",
                    SkillId = "fixture-disable",
                    Disable = ["b"]
                },
                new SkillContributionMerger.PendingEntry
                {
                    SourceDomain = "mymod",
                    SourcePath = "replace.json",
                    SkillId = "fixture-replace",
                    Nodes =
                    [
                        new SkillTreeNodeJson
                        {
                            id = "mymod:excavator",
                            replaces = "oldminer",
                            requires = ["root"]
                        }
                    ]
                }
            ],
            warnings.Add);

        SkillTreeNodeJson[]? disabled = drafts["fixture-disable"].tree?.nodes;
        SkillTreeNodeJson? nodeC = disabled?.FirstOrDefault(n =>
            string.Equals(n.id, "c", StringComparison.OrdinalIgnoreCase));
        if (disabled == null
            || disabled.Any(n => string.Equals(n.id, "b", StringComparison.OrdinalIgnoreCase))
            || nodeC?.requires == null
            || nodeC.requires.Length != 2
            || nodeC.requires[0] is not JArray orGroup
            || orGroup.Count != 1
            || orGroup[0]?.Value<string>() != "a"
            || nodeC.requires[1]?.Value<string>() != "d")
        {
            Assert.Fail("[prosequor] Contribution fixture failed (disable strips requires and OR groups).");
        }

        if (!warnings.Any(w => w.Contains("promoted to root", StringComparison.OrdinalIgnoreCase)))
        {
            Assert.Fail("[prosequor] Contribution fixture failed (disable root promotion warning).");
        }

        SkillTreeNodeJson[]? replaced = drafts["fixture-replace"].tree?.nodes;
        SkillTreeNodeJson? fan = replaced?.FirstOrDefault(n =>
            string.Equals(n.id, "fan", StringComparison.OrdinalIgnoreCase));
        SkillTreeNodeJson? oldMiner = replaced?.FirstOrDefault(n =>
            string.Equals(n.id, "oldminer", StringComparison.OrdinalIgnoreCase));
        SkillTreeNodeJson? excavator = replaced?.FirstOrDefault(n =>
            string.Equals(n.id, "mymod:excavator", StringComparison.OrdinalIgnoreCase));
        SkillTreeNodeJson? rival = replaced?.FirstOrDefault(n =>
            string.Equals(n.id, "rival", StringComparison.OrdinalIgnoreCase));
        if (replaced == null
            || oldMiner != null
            || excavator == null
            || fan?.requires?.Length != 1
            || fan.requires[0]?.Value<string>() != "mymod:excavator"
            || rival?.excludes?.Length != 1
            || rival.excludes[0] != "mymod:excavator")
        {
            Assert.Fail("[prosequor] Contribution fixture failed (replace rewires requires and excludes).");
        }

        int order = 0;
        SkillTreeCompiler.CompileResult compiled = SkillTreeCompiler.Compile(
            "fixture-replace",
            100,
            drafts["fixture-replace"].tree,
            hooks,
            actions,
            NewCollections(),
            ref order);
        if (!compiled.Success
            || compiled.Tree == null
            || !compiled.Tree.ById.ContainsKey("mymod:excavator")
            || compiled.Tree.ById["fan"].AllRequirementIds[0] != "mymod:excavator")
        {
            Assert.Fail("[prosequor] Contribution fixture failed (compiled replaced tree).");
        }
    }

    static void VerifyContributionDisableAllAndXp()
    {
        Dictionary<string, SkillDefJson> drafts = new(StringComparer.OrdinalIgnoreCase)
        {
            ["fixture-keep"] = new SkillDefJson
            {
                id = "fixture-keep",
                xpRules =
                [
                    new XpRuleJson
                    {
                        id = "fixture:keep-base",
                        amount = new JValue(1),
                        when = new XpRuleWhenJson { activity = "prosequor:dig" }
                    }
                ],
                tree = new SkillTreeJson
                {
                    nodes = [new SkillTreeNodeJson { id = "root", requires = [] }]
                }
            },
            ["fixture-scorch"] = new SkillDefJson
            {
                id = "fixture-scorch",
                xpRules =
                [
                    new XpRuleJson
                    {
                        id = "fixture:scorch-xp",
                        rate = 0.01f,
                        when = new XpRuleWhenJson { activity = "game:digging" }
                    }
                ],
                tree = new SkillTreeJson
                {
                    nodes = [new SkillTreeNodeJson { id = "gone", requires = [] }]
                }
            }
        };

        List<string> warnings = new();
        SkillContributionMerger.Apply(
            drafts,
            [
                new SkillContributionMerger.PendingEntry
                {
                    SourceDomain = "mymod",
                    SourcePath = "xp.json",
                    SkillId = "fixture-keep",
                    XpRules =
                    [
                        new XpRuleJson
                        {
                            id = "fixture:keep-base",
                            amount = new JValue(2),
                            when = new XpRuleWhenJson { activity = "prosequor:dig" }
                        },
                        new XpRuleJson
                        {
                            id = "mymod:extra",
                            rate = 0.5f,
                            when = new XpRuleWhenJson { activity = "game:digging" }
                        }
                    ]
                },
                new SkillContributionMerger.PendingEntry
                {
                    SourceDomain = "mymod",
                    SourcePath = "scorch.json",
                    SkillId = "fixture-scorch",
                    DisableAll = true
                },
                new SkillContributionMerger.PendingEntry
                {
                    SourceDomain = "mymod",
                    SourcePath = "after-scorch.json",
                    SkillId = "fixture-scorch",
                    Nodes =
                    [
                        new SkillTreeNodeJson { id = "mymod:late", requires = [] }
                    ]
                }
            ],
            warnings.Add);

        if (drafts.ContainsKey("fixture-scorch"))
        {
            Assert.Fail("[prosequor] Contribution fixture failed (disable all should remove skill draft).");
        }

        if (!warnings.Any(w =>
                w.Contains("unknown skill 'fixture-scorch'", StringComparison.OrdinalIgnoreCase)))
        {
            Assert.Fail("[prosequor] Contribution fixture failed (post-disable contribution should warn).");
        }

        XpRuleJson[]? keepRules = drafts["fixture-keep"].xpRules;
        XpRuleJson? overwritten = keepRules?.FirstOrDefault(r =>
            string.Equals(r.id, "fixture:keep-base", StringComparison.OrdinalIgnoreCase));
        XpRuleJson? extra = keepRules?.FirstOrDefault(r =>
            string.Equals(r.id, "mymod:extra", StringComparison.OrdinalIgnoreCase));
        if (keepRules == null
            || keepRules.Length != 2
            || overwritten == null
            || Math.Abs(overwritten.amount!.Value<float>() - 2f) > 0.0001f
            || extra == null
            || Math.Abs(extra.rate - 0.5f) > 0.0001f)
        {
            Assert.Fail("[prosequor] Contribution fixture failed (xpRules merge / last-win by id).");
        }

        int order = 0;
        List<XpRule> compiledKeep = XpRuleCompiler.CompileAll(
            drafts["fixture-keep"].xpRules,
            "fixture-keep",
            NewCollections(),
            ref order,
            _ => { });
        FixtureSkillRegistry skills = new();
        skills.Register(new SkillDef
        {
            Id = "fixture-keep",
            XpRules = compiledKeep
        });
        // Scorched skill is absent — its XP must not appear after LoadFromSkills.
        XpRuleRegistry registry = new();
        registry.LoadFromSkills(skills);
        if (registry.All.Count != 2
            || registry.All.Any(r =>
                string.Equals(r.Id, "fixture:scorch-xp", StringComparison.OrdinalIgnoreCase))
            || registry.All.All(r => r.SkillId != "fixture-keep"))
        {
            Assert.Fail("[prosequor] Contribution fixture failed (disabled skill XP absent from registry).");
        }
    }


    static void VerifyOreGradeUpgrade()
    {
        if (!OreGradeUpgrade.TryNextGradeCode(
                "game:ore-poor-nativecopper-basalt",
                out string next)
            || next != "game:ore-medium-nativecopper-basalt"
            || !OreGradeUpgrade.TryNextGradeCode(
                "game:crystalizedore-medium-hematite-granite",
                out next)
            || next != "game:crystalizedore-rich-hematite-granite"
            || !OreGradeUpgrade.TryNextGradeCode(
                "game:ore-rich-galena-andesite",
                out next)
            || next != "game:ore-bountiful-galena-andesite"
            || OreGradeUpgrade.TryNextGradeCode(
                "game:ore-bountiful-galena-andesite",
                out _)
            || OreGradeUpgrade.TryNextGradeCode("game:ore-coal-granite", out _)
            || OreGradeUpgrade.TryNextGradeCode("game:ore-low-diamond-basalt", out _)
            || !OreGradeUpgrade.IsGradedOrePath("ore-poor-nativecopper-basalt")
            || OreGradeUpgrade.IsGradedOrePath("ore-nativecopper-basalt"))
        {
            Assert.Fail("[prosequor] Ore grade upgrade fixture failed (path rewrite).");
        }

        HookRegistry hooks = new();
        AbilityActionRegistry actions = new(hooks);
        AbilityBootstrap.RegisterBuiltIns(hooks, actions);
        if (!actions.TryGet(
                ActionIds.UpgradeOreGrade,
                HookIds.BlockInteraction,
                VerbIds.MutateDrops,
                HookIds.Stack,
                out _))
        {
            Assert.Fail("[prosequor] Ore grade upgrade fixture failed (action not registered).");
        }
    }

    static void VerifyCatalogAlternateExclusion()
    {
        CollectionIndex catalog = new();
        catalog.EnsureKey("clay");
        // Can't construct real Items without the game; exercise empty-pool path only.
        Item? pick = catalog.PickFromPool(null!, "clay", new Random(1));
        if (pick != null)
        {
            Assert.Fail("[prosequor] Catalog fixture failed (empty pool should return null).");
        }

        if (catalog.CodeCount("clay") != 0 || !catalog.Exists("clay"))
        {
            Assert.Fail("[prosequor] Catalog fixture failed (empty membership / key).");
        }

        if (!AbilityBootstrap.IsRawClayBlockPath("rawclay-blue-none")
            || AbilityBootstrap.IsRawClayBlockPath("ore-nativecopper-claystone")
            || !AbilityBootstrap.IsPeatBlockPath("peat-verysparse")
            || AbilityBootstrap.IsPeatBlockPath("peatbrick")
            || !AbilityBootstrap.IsCharcoalBlockPath("charcoalpile-3")
            || AbilityBootstrap.IsCharcoalBlockPath("charcoal")
            || !AbilityBootstrap.IsSaltpeterBlockPath("saltpeter-nsd")
            || !AbilityBootstrap.IsNuggetItemPath("nugget-nativecopper")
            || AbilityBootstrap.IsNuggetItemPath("ore-poor-nativecopper-granite")
            || !AbilityBootstrap.IsCrystallizedOreItemPath(
                "crystalizedore-poor-nativecopper-granite")
            || AbilityBootstrap.IsCrystallizedOreItemPath(
                "ore-poor-nativecopper-granite"))
        {
            Assert.Fail("[prosequor] Catalog fixture failed (built-in target classifiers).");
        }

        if (!OutputPoolRegistry.TryValidateNamespacedId(
                "prosequor:clay-item",
                "prosequor",
                out _)
            || OutputPoolRegistry.TryValidateNamespacedId("clay-item", "prosequor", out _)
            || OutputPoolRegistry.TryValidateNamespacedId(
                "othermod:clay-item",
                "prosequor",
                out _))
        {
            Assert.Fail("[prosequor] Output pool fixture failed (namespaced id validation).");
        }

        if (!AbilityFormulas.TryParseIntRange(
                null, 1, 1, 1, "rolls", out int rMin, out int rMax, out bool rSpec, out _)
            || rMin != 1
            || rMax != 1
            || rSpec
            || !AbilityFormulas.TryParseIntRange(
                new JValue("2-5"), 1, 1, 1, "rolls", out rMin, out rMax, out rSpec, out _)
            || rMin != 2
            || rMax != 5
            || !rSpec
            || AbilityFormulas.TryParseIntRange(
                new JValue("5-2"), 1, 1, 1, "rolls", out _, out _, out _, out _)
            || AbilityFormulas.TryParseIntRange(
                new JValue(0), 1, 1, 1, "rolls", out _, out _, out _, out _)
            || !AbilityFormulas.TryParseQuantityRange(null, out int qMin, out int qMax, out _)
            || qMin != 1
            || qMax != 1
            || !AbilityFormulas.TryParseQuantityRange(new JValue(2), out qMin, out qMax, out _)
            || qMin != 2
            || qMax != 2
            || !AbilityFormulas.TryParseQuantityRange(new JValue("2-5"), out qMin, out qMax, out _)
            || qMin != 2
            || qMax != 5
            || AbilityFormulas.TryParseQuantityRange(new JValue("5-2"), out _, out _, out _)
            || AbilityFormulas.TryParseQuantityRange(new JValue(0), out _, out _, out _)
            || AbilityFormulas.RollIntRange(3, 3, new Random(1)) != 3
            || AbilityFormulas.RollQuantityRange(3, 3, new Random(1)) != 3)
        {
            Assert.Fail("[prosequor] Int/quantity range fixture failed.");
        }

        if (AbilityFormulas.StochasticRound(2.0, new Random(1)) != 2
            || AbilityFormulas.StochasticRound(0, new Random(1)) != 0
            || AbilityFormulas.YieldBonusFraction(10, 0, 0, 50) < 0.099f
            || AbilityFormulas.YieldBonusFraction(10, 0, 0, 50) > 0.101f)
        {
            Assert.Fail("[prosequor] Formula fixture failed (stochastic round / flat yield).");
        }

        // Craft quantity: preview shows floor of folded count; take rolls StochasticRound(qty).
        if (CraftMutateOutputStation.FloorQuantity(11.5f) != 11
            || CraftMutateOutputStation.FloorQuantity(4.32f) != 4
            || CraftMutateOutputStation.FloorQuantity(10f) != 10)
        {
            Assert.Fail("[prosequor] Craft quantity floor fixture failed.");
        }

        int desired115 = AbilityFormulas.StochasticRound(11.5, new Random(0));
        if (desired115 < 11 || desired115 > 12)
        {
            Assert.Fail("[prosequor] Craft quantity stochastic fixture failed (11.5).");
        }

        CollectionIndex stackCatalog = new();
        stackCatalog.EnsureKey("sapling");
        if (stackCatalog.StackMatches("sapling", null))
        {
            Assert.Fail("[prosequor] Catalog fixture failed (null stack should not match tag).");
        }

        if (!IsCodePart("log-grown-pine-ud", 2, "pine")
            || IsCodePart("log-grown-oak-ud", 2, "pine"))
        {
            Assert.Fail("[prosequor] Classification fixture failed (pine code segment).");
        }
    }

    static bool IsCodePart(string path, int index, string expected)
    {
        string[] parts = path.Split('-');
        return index < parts.Length
            && string.Equals(parts[index], expected, StringComparison.OrdinalIgnoreCase);
    }

    sealed class FixtureSkillRegistry : ISkillRegistry
    {
        readonly List<SkillDef> all = new();

        public IReadOnlyList<SkillDef> All => all;

        public SkillMenuIndex MenuIndex => SkillMenuIndex.Build(all);

        public void Register(SkillDef def) => all.Add(def);

        public bool TryGet(string id, out SkillDef def)
        {
            def = all.FirstOrDefault(s => string.Equals(s.Id, id, StringComparison.OrdinalIgnoreCase))!;
            return def != null;
        }

        public bool TryResolve(string idOrDisplayName, string? languageCode, out SkillDef def) =>
            TryGet(idOrDisplayName, out def);
    }

    class FixtureProgress : IPlayerProgress
    {
        readonly Dictionary<string, int> skillLevels = new(StringComparer.OrdinalIgnoreCase);
        readonly Dictionary<string, int> unlockTiers = new(StringComparer.OrdinalIgnoreCase);
        int playerLevel = 1;
        int unlockPoints;

        public event Action? Changed;

        public int PlayerLevel => playerLevel;
        public float PlayerXp => 0;
        public int UnlockPoints => unlockPoints;
        public float PlayerXpUntilNext => 0;

        public void SetSkillLevel(string skillId, int level)
        {
            skillLevels[skillId] = level;
            Changed?.Invoke();
        }

        public void SetUnlockTier(string skillId, string nodeId, int tier)
        {
            unlockTiers[$"{skillId}:{nodeId}"] = tier;
            Changed?.Invoke();
        }

        public void SetUnlockPoints(int points)
        {
            unlockPoints = points;
            Changed?.Invoke();
        }

        public int GetSkillLevel(string skillId) =>
            skillLevels.TryGetValue(skillId, out int level) ? level : 0;

        public float GetSkillXp(string skillId) => 0;
        public IReadOnlyList<string> GetUnlocks(string skillId) => Array.Empty<string>();
        public bool HasUnlock(string skillId, string code) => GetUnlockTier(skillId, code) > 0;

        public int GetUnlockTier(string skillId, string nodeId) =>
            unlockTiers.TryGetValue($"{skillId}:{nodeId}", out int tier) ? tier : 0;

        public int GetAttribute(string id) => AttributeGrowth.DefaultScore;
        public float GetAttributeBucket(string id) => 0f;

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
        public void AddUnlockPoints(int amount) => unlockPoints += amount;
        public void SetPlayerLevel(int level)
        {
            playerLevel = Math.Max(1, level);
            Changed?.Invoke();
        }

        public void AddAttributeBucket(string id, float amount) { }
        public void SetAttribute(string id, int score) { }

        public bool GrantUnlock(string skillId, string code, int cost = 1) => false;
        public bool RevokeUnlock(string skillId, string code, int refund = 1) => false;
        public UnlockPurchaseStatus TryPurchaseNode(string skillId, string nodeId) =>
            UnlockPurchaseStatus.UnknownNode;
    }

    sealed class MemoFixtureProgress : FixtureProgress, IAbilityComposeCache
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

        public new void SetSkillLevel(string skillId, int level)
        {
            int before = GetSkillLevel(skillId);
            base.SetSkillLevel(skillId, level);
            if (before != level)
            {
                BumpProgressRevision();
            }
        }
    }

    static void VerifyChanceGateCompile(
        IHookRegistry hooks,
        IAbilityActionRegistry actions)
    {
        int order = 0;
        List<string> errors = new();
        List<AbilityRule>? ok = AbilityRuleCompiler.CompileEffects(
            "fixture",
            "doublehook",
            1,
            [
                new AbilityEffectJson
                {
                    hook = "prosequor:block-interaction",
                    verb = "prosequor:mutate-drops",
                    phase = "quantity",
                    action = "prosequor:chance",
                    when = new AbilityWhenJson
                    {
                        tags = ["target:<fish>"]
                    },
                    @params = JObject.Parse(
                        """
                        {
                          "chance": { "base": 0.05, "perSkillLevel": 0.01, "cap": 0.25 },
                          "onSuccess": {
                            "action": "prosequor:number",
                            "params": { "op": "add", "value": 1 }
                          }
                        }
                        """)
                }
            ],
            hooks,
            actions,
            NewCollections("soil", "leaves", "sapling", "immature-crop", "farmland", "fish", "obsidian"),
            errors,
            ref order);
        if (ok == null || ok.Count != 1 || errors.Count > 0)
        {
            Assert.Fail(string.Format("[prosequor] Chance-gate fixture failed (doublehook-shaped compile): {0}",
                string.Join("; ", errors)));
            return;
        }

        errors.Clear();
        order = 0;
        if (AbilityRuleCompiler.CompileEffects(
                "fixture",
                "bad-nested",
                1,
                [
                    new AbilityEffectJson
                    {
                        hook = "prosequor:block-interaction",
                    verb = "prosequor:mutate-drops",
                        phase = "quantity",
                        action = "prosequor:chance",
                        @params = JObject.Parse(
                            """
                            {
                              "chance": { "percent": 50 },
                              "onSuccess": {
                                "action": "prosequor:restore-consumed-bait"
                              }
                            }
                            """)
                    }
                ],
                hooks,
                actions,
                NewCollections("soil", "leaves", "sapling", "immature-crop", "farmland", "fish", "obsidian"),
                errors,
                ref order) != null)
        {
            Assert.Fail("[prosequor] Chance-gate fixture failed (cross-phase onSuccess should reject).");
        }
    }

    static ChanceGateParams BuildAlwaysChancePassThrough(
        IAbilityActionRegistry actions,
        HookId preferredHook,
        VerbId preferredVerb)
    {
        if (!ChanceProducer.TryParse(
                new JObject { ["percent"] = 100 },
                actions,
                preferredHook,
                preferredVerb,
                progressForValidation: null,
                out ChanceProducer? chance,
                out _)
            || chance == null)
        {
            throw new InvalidOperationException("Failed to build fixture chance producer.");
        }

        return new ChanceGateParams
        {
            Chance = chance,
            OnSuccess = null,
            OnFailure = null
        };
    }

    static ChanceGateParams BuildAlwaysNumberSetZero(IAbilityActionRegistry actions)
    {
        if (!ChanceProducer.TryParse(
                new JObject { ["percent"] = 100 },
                actions,
                HookIds.ItemInteraction,
                VerbIds.ItemDamage,
                progressForValidation: null,
                out ChanceProducer? chance,
                out _)
            || chance == null)
        {
            throw new InvalidOperationException("Failed to build fixture chance producer.");
        }

        if (!actions.TryGet(
                ActionIds.Number,
                HookIds.ItemInteraction,
                VerbIds.ItemDamage,
                HookIds.Amount,
                out IAbilityActionHandler setZero))
        {
            throw new InvalidOperationException("number is not registered for item-damage amount.");
        }

        return new ChanceGateParams
        {
            Chance = chance,
            OnSuccess = new NestedActionRef
            {
                Action = ActionIds.Number,
                Parameters = ParseNumberSpec("{\"op\":\"set\",\"value\":0}"),
                Handler = setZero
            }
        };
    }

    /// <summary>Adds Parameters (float) to the value; used for ordering/chaining fixtures.</summary>
    sealed class FixtureAddAction : AbilityActionHandler<InteractionSpeedContext, float, float>
    {
        readonly ActionId id;

        public FixtureAddAction(string id, float amount, int priorityUnused)
        {
            this.id = new ActionId(id);
            _ = amount;
            _ = priorityUnused;
        }

        public override ActionId Id => id;
        public override HookId Hook => HookIds.BlockInteraction;
        public override VerbId Verb => VerbIds.InteractionSpeed;
        public override PhaseId Phase => HookIds.Default;

        protected override bool TryParse(JObject? raw, out float parameters, out string error)
        {
            parameters = raw?.Value<float?>("amount") ?? 0f;
            error = "";
            return true;
        }

        protected override float Apply(
            InteractionSpeedContext context,
            float value,
            float parameters,
            AbilityRuleSource source) =>
            value + parameters;
    }

    /// <summary>Mounted move-speed variant of <see cref="FixtureAddAction"/> for compose-memo fixtures.</summary>
    sealed class FixtureAddMountedAction : AbilityActionHandler<MountedContext, float, float>
    {
        readonly ActionId id;

        public FixtureAddMountedAction(string id) => this.id = new ActionId(id);

        public override ActionId Id => id;
        public override HookId Hook => HookIds.EntityInteraction;
        public override VerbId Verb => VerbIds.Mounted;
        public override PhaseId Phase => HookIds.MoveSpeed;

        protected override bool TryParse(JObject? raw, out float parameters, out string error)
        {
            parameters = raw?.Value<float?>("amount") ?? 0f;
            error = "";
            return true;
        }

        protected override float Apply(
            MountedContext context,
            float value,
            float parameters,
            AbilityRuleSource source) =>
            value + parameters;
    }

    sealed class WrongShapeFloatAction : AbilityActionHandler<DropsContext, float, float>
    {
        public override ActionId Id => new("prosequor:fixture-wrong-shape");
        public override HookId Hook => HookIds.BlockInteraction;
        public override VerbId Verb => VerbIds.InteractionSpeed;
        public override PhaseId Phase => HookIds.Default;

        protected override bool TryParse(JObject? raw, out float parameters, out string error)
        {
            parameters = 0f;
            error = "";
            return true;
        }

        protected override float Apply(
            DropsContext context,
            float value,
            float parameters,
            AbilityRuleSource source) =>
            value;
    }

    sealed class UnknownHookAction : AbilityActionHandler<InteractionSpeedContext, float, float>
    {
        public override ActionId Id => new("prosequor:fixture-unknown-hook");
        public override HookId Hook => new("prosequor:does-not-exist");
        public override VerbId Verb => VerbIds.InteractionSpeed;
        public override PhaseId Phase => HookIds.Default;

        protected override bool TryParse(JObject? raw, out float parameters, out string error)
        {
            parameters = 0f;
            error = "";
            return true;
        }

        protected override float Apply(
            InteractionSpeedContext context,
            float value,
            float parameters,
            AbilityRuleSource source) =>
            value;
    }
}
