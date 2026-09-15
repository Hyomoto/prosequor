using Newtonsoft.Json.Linq;
using Prosequor.Ability;
using Prosequor.Ability.Actions;
using Prosequor.Ability.Hooks;
using Prosequor.Data;
using Prosequor.Player;
using Vintagestory.API.Common;
using Xunit;

namespace Prosequor.Pure.Tests;

/// <summary>
/// Cross-cutting AbilityPipeline.Run contracts: null/empty, when-tags, unlock tiers,
/// attribute minScore, and priority/source-order folding.
/// </summary>
public class PipelineMechanismTests
{
    const string SkillId = "pipeline-skill";
    const string AttrId = "strength";
    const string SoilCode = "game:soil-low-none";
    const string WoodCode = "game:log-oak";

    [Fact]
    [Trait("Layer", "Pipeline")]
    public void NullProgress_ReturnsSeedUnchanged()
    {
        (AbilityPipeline pipeline, CollectionIndex collections) = BuildPipeline(WithRootMultiply());
        InteractionSpeedContext context = SpeedContext(progress: null, SoilCode);

        float result = pipeline.Run(HookIds.BlockInteraction, VerbIds.InteractionSpeed, HookIds.Default, context, 1f);
        Assert.Equal(1f, result);
    }

    [Fact]
    [Trait("Layer", "Pipeline")]
    public void EmptyIndex_ReturnsSeedUnchanged()
    {
        AbilityPipeline pipeline = BuildPipeline(skills: new PipelineSkillRegistry()).Pipeline;
        ActionTestProgress progress = new();
        InteractionSpeedContext context = SpeedContext(progress, SoilCode);

        float result = pipeline.Run(HookIds.BlockInteraction, VerbIds.InteractionSpeed, HookIds.Default, context, 1f);
        Assert.Equal(1f, result);
    }

    [Fact]
    [Trait("Layer", "Pipeline")]
    public void WhenTagsMatch_AppliesRule()
    {
        (AbilityPipeline pipeline, _) = BuildPipeline(WithRootMultiply());
        ActionTestProgress progress = new();
        progress.SetSkillLevel(SkillId, 10);
        InteractionSpeedContext context = SpeedContext(progress, SoilCode);

        // 1 * (1 + 0.1 * 10) = 2
        float result = pipeline.Run(HookIds.BlockInteraction, VerbIds.InteractionSpeed, HookIds.Default, context, 1f);
        Assert.Equal(2f, result);
    }

    [Fact]
    [Trait("Layer", "Pipeline")]
    public void WhenTagsMiss_LeavesSeedUnchanged()
    {
        (AbilityPipeline pipeline, _) = BuildPipeline(WithRootMultiply());
        ActionTestProgress progress = new();
        progress.SetSkillLevel(SkillId, 10);
        InteractionSpeedContext context = SpeedContext(progress, WoodCode);

        float result = pipeline.Run(HookIds.BlockInteraction, VerbIds.InteractionSpeed, HookIds.Default, context, 1f);
        Assert.Equal(1f, result);
    }

    [Fact]
    [Trait("Layer", "Pipeline")]
    public void TreeUnlockLocked_SkipsTier_RootStillRuns()
    {
        (AbilityPipeline pipeline, _) = BuildPipeline(WithRootAndTierAdd());
        ActionTestProgress progress = new();
        progress.SetSkillLevel(SkillId, 10);
        // Node unlocked at 0 → tier rule inactive; root still matches.
        InteractionSpeedContext context = SpeedContext(progress, SoilCode);

        float result = pipeline.Run(HookIds.BlockInteraction, VerbIds.InteractionSpeed, HookIds.Default, context, 1f);
        Assert.Equal(2f, result);
    }

    [Fact]
    [Trait("Layer", "Pipeline")]
    public void TreeUnlockOwned_AppliesTierRule()
    {
        (AbilityPipeline pipeline, _) = BuildPipeline(WithRootAndTierAdd());
        ActionTestProgress progress = new();
        progress.SetSkillLevel(SkillId, 10);
        progress.SetUnlockTier(SkillId, "node", 1);
        InteractionSpeedContext context = SpeedContext(progress, SoilCode);

        // Root *2 then +1 = 3
        float result = pipeline.Run(HookIds.BlockInteraction, VerbIds.InteractionSpeed, HookIds.Default, context, 1f);
        Assert.Equal(3f, result);
    }

    [Fact]
    [Trait("Layer", "Pipeline")]
    public void AttributeMinScore_BelowGateSkipped_AtOrAboveApplies()
    {
        HookRegistry hooks = new();
        AbilityActionRegistry actions = new(hooks);
        AbilityBootstrap.RegisterBuiltIns(hooks, actions);

        PipelineSkillRegistry skills = new();
        AttributeStatRegistry attributeStats = new();
        attributeStats.Register(new AttributeStatDef
        {
            Id = AttrId,
            Rules =
            [
                new AbilityRule
                {
                    RuleId = "melee",
                    Hook = HookIds.PlayerInteraction,
                    Verb = VerbIds.MeleeDamage,
                    Phase = HookIds.Default,
                    Action = ActionIds.AddMappedNumber,
                    When = new AbilityWhenFilter(),
                    Parameters = new MappedNumberParams
                    {
                        FromScore = 0,
                        FromValue = 0f,
                        ToScore = 20,
                        ToValue = 100f,
                        Round = "ceil"
                    },
                    Source = new AbilityRuleSource
                    {
                        SkillId = AttrId,
                        AttributeId = AttrId,
                        MinAttributeScore = 12
                    },
                    Priority = 0,
                    SourceOrder = 1
                }
            ]
        });

        AbilityPipeline pipeline = new(actions, skills, attributeStats);
        ActionTestProgress progress = new();

        PlayerInteractionContext below = new() { Progress = progress };
        progress.SetAttribute(AttrId, 11);
        Assert.Equal(0, pipeline.Run(HookIds.PlayerInteraction, VerbIds.MeleeDamage, HookIds.Default, below, 0));

        progress.SetAttribute(AttrId, 12);
        // Mapped midpoint-ish: score 12 over 0→20 maps to 60; seed 0 → 60
        int atGate = pipeline.Run(HookIds.PlayerInteraction, VerbIds.MeleeDamage, HookIds.Default, below, 0);
        Assert.Equal(
            AbilityFormulas.AttributeMappedInt(12, 0, 0, 20, 100, "ceil"),
            atGate);
    }

    [Fact]
    [Trait("Layer", "Pipeline")]
    public void PriorityThenSourceOrder_FoldsInExactOrder()
    {
        CollectionIndex collections = SoilCollections();
        HookRegistry hooks = new();
        AbilityActionRegistry actions = new(hooks);
        AbilityBootstrap.RegisterBuiltIns(hooks, actions);
        actions.Register(new FixtureAddFloatAction("prosequor:fixture-add-a"));
        actions.Register(new FixtureAddFloatAction("prosequor:fixture-add-b"));

        PipelineSkillRegistry skills = new();
        skills.Register(new SkillDef
        {
            Id = SkillId,
            Rules =
            [
                new AbilityRule
                {
                    RuleId = "root",
                    Hook = HookIds.BlockInteraction,
                    Verb = VerbIds.InteractionSpeed,
                    Phase = HookIds.Default,
                    Action = ActionIds.Number,
                    When = WhenTargetSoil(collections),
                    Parameters = ParseNumberSpec("{\"op\":\"scale\",\"base\":0,\"perSkillLevel\":0.1}"),
                    Source = new AbilityRuleSource { SkillId = SkillId },
                    Priority = 0,
                    SourceOrder = 1
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
                        SkillId = SkillId,
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
                        SkillId = SkillId,
                        NodeId = "chain",
                        Tier = 1
                    },
                    Priority = 0,
                    SourceOrder = 100
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
                        SkillId = SkillId,
                        NodeId = "node",
                        Tier = 1
                    },
                    Priority = 10,
                    SourceOrder = 2
                }
            ]
        });

        AbilityPipeline pipeline = new(actions, skills);
        ActionTestProgress progress = new();
        progress.SetSkillLevel(SkillId, 10);
        progress.SetUnlockTier(SkillId, "chain", 1);
        progress.SetUnlockTier(SkillId, "node", 1);

        InteractionSpeedContext context = SpeedContext(progress, SoilCode);
        // Sorted: root p0/o1, chain-high p0/o100, chain-low p1/o99, tier1 p10/o2
        // 1 → *2 = 2 → +10 = 12 → +1 = 13 → +1 = 14
        float result = pipeline.Run(HookIds.BlockInteraction, VerbIds.InteractionSpeed, HookIds.Default, context, 1f);
        Assert.Equal(14f, result);
    }

    static (AbilityPipeline Pipeline, CollectionIndex Collections) BuildPipeline(SkillDef skill)
    {
        PipelineSkillRegistry skills = new();
        skills.Register(skill);
        return BuildPipeline(skills);
    }

    static (AbilityPipeline Pipeline, CollectionIndex Collections) BuildPipeline(PipelineSkillRegistry skills)
    {
        HookRegistry hooks = new();
        AbilityActionRegistry actions = new(hooks);
        AbilityBootstrap.RegisterBuiltIns(hooks, actions);
        actions.Register(new FixtureAddFloatAction("prosequor:fixture-add-a"));
        return (new AbilityPipeline(actions, skills), SoilCollections());
    }

    static CollectionIndex SoilCollections()
    {
        CollectionIndex collections = new();
        collections.EnsureKey("soil");
        collections.AddCode("soil", SoilCode);
        return collections;
    }

    static AbilityWhenFilter WhenTargetSoil(CollectionIndex collections)
    {
        Assert.True(
            AbilityRuleCompiler.TryCompileWhen(
                "pipeline",
                new AbilityWhenJson { tags = ["target:<soil>"] },
                collections,
                out AbilityWhenFilter? filter,
                out string error),
            error);
        return filter!;
    }

    static SkillDef WithRootMultiply()
    {
        CollectionIndex collections = SoilCollections();
        return new()
        {
            Id = SkillId,
            Rules =
            [
                new AbilityRule
                {
                    RuleId = "root",
                    Hook = HookIds.BlockInteraction,
                    Verb = VerbIds.InteractionSpeed,
                    Phase = HookIds.Default,
                    Action = ActionIds.Number,
                    When = WhenTargetSoil(collections),
                    Parameters = ParseNumberSpec("{\"op\":\"scale\",\"base\":0,\"perSkillLevel\":0.1}"),
                    Source = new AbilityRuleSource { SkillId = SkillId },
                    Priority = 0,
                    SourceOrder = 1
                }
            ]
        };
    }

    static SkillDef WithRootAndTierAdd()
    {
        CollectionIndex collections = SoilCollections();
        return new()
        {
            Id = SkillId,
            Rules =
            [
                new AbilityRule
                {
                    RuleId = "root",
                    Hook = HookIds.BlockInteraction,
                    Verb = VerbIds.InteractionSpeed,
                    Phase = HookIds.Default,
                    Action = ActionIds.Number,
                    When = WhenTargetSoil(collections),
                    Parameters = ParseNumberSpec("{\"op\":\"scale\",\"base\":0,\"perSkillLevel\":0.1}"),
                    Source = new AbilityRuleSource { SkillId = SkillId },
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
                        SkillId = SkillId,
                        NodeId = "node",
                        Tier = 1
                    },
                    Priority = 10,
                    SourceOrder = 2
                }
            ]
        };
    }

    
    static NumberSpec ParseNumberSpec(string json)
    {
        Assert.True(NumberSpec.TryParse(JObject.Parse(json), out NumberSpec? spec, out string error), error);
        return spec!;
    }

    static InteractionSpeedContext SpeedContext(IPlayerProgress? progress, string targetCode) =>
        new()
        {
            Progress = progress,
            Fact = new AbilityAction
            {
                Verb = "prosequor:mine",
                ActorUid = "fixture",
                Target = targetCode
            },
            Material = EnumBlockMaterial.Soil,
            BaseValue = 1f
        };

    /// <summary>Adds Parameters (float) to interaction-speed value for ordering fixtures.</summary>
    sealed class FixtureAddFloatAction : AbilityActionHandler<InteractionSpeedContext, float, float>
    {
        readonly ActionId id;

        public FixtureAddFloatAction(string id) => this.id = new ActionId(id);

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
}
