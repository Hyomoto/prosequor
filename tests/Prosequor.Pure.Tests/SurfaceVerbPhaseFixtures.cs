using Prosequor.Ability;
using Prosequor.Ability.Actions;
using Prosequor.Ability.Hooks;
using Prosequor.Data;
using Xunit;

namespace Prosequor.Pure.Tests;

/// <summary>
/// Small contracts for surface→verb→phase addressing: index keys, omitted phase → default,
/// and mutate-drops classification via when.tags tokens.
/// </summary>
public class SurfaceVerbPhaseFixtures
{
    const string SkillId = "surface-fixture";

    [Fact]
    [Trait("Layer", "Pipeline")]
    public void AbilityRuleIndex_KeysOnHookVerbPhase()
    {
        PipelineSkillRegistry skills = new();
        skills.Register(new SkillDef
        {
            Id = SkillId,
            Rules =
            [
                new AbilityRule
                {
                    RuleId = "qty",
                    Hook = HookIds.BlockInteraction,
                    Verb = VerbIds.MutateDrops,
                    Phase = HookIds.Quantity,
                    Action = ActionIds.Number,
                    When = new AbilityWhenFilter(),
                    Parameters = ParseNumberSpec("{\"op\":\"scale\",\"base\":0.10,\"perSkillLevel\":0,\"cap\":0.10}"),
                    Source = new AbilityRuleSource { SkillId = SkillId },
                    Priority = 0,
                    SourceOrder = 1
                }
            ]
        });

        AbilityRuleIndex index = AbilityRuleIndex.Build(skills);
        Assert.Single(index.Get(HookIds.BlockInteraction, VerbIds.MutateDrops, HookIds.Quantity));
        Assert.Empty(index.Get(HookIds.BlockInteraction, VerbIds.MutateDrops, HookIds.Stacks));
        Assert.Empty(index.Get(HookIds.BlockInteraction, VerbIds.MutateDrops, HookIds.Stack));
        Assert.Empty(index.Get(HookIds.BlockInteraction, VerbIds.InteractionSpeed, HookIds.Quantity));
        Assert.Empty(index.Get(HookIds.ItemInteraction, VerbIds.MutateDrops, HookIds.Quantity));
    }

    [Fact]
    [Trait("Layer", "Compiler")]
    public void OmitPhase_DefaultsViaCompiler()
    {
        HookRegistry hooks = new();
        AbilityActionRegistry actions = new(hooks);
        AbilityBootstrap.RegisterBuiltIns(hooks, actions);

        List<string> errors = new();
        int sourceOrder = 0;
        List<AbilityRule>? compiled = AbilityRuleCompiler.CompileEffects(
            SkillId,
            nodeId: null,
            tier: null,
            [
                new AbilityEffectJson
                {
                    hook = "prosequor:player-interaction",
                    verb = "prosequor:melee-damage",
                    // phase omitted → default
                    action = "prosequor:add-mapped-number",
                    @params = new Newtonsoft.Json.Linq.JObject
                    {
                        ["fromScore"] = 0,
                        ["fromValue"] = 0,
                        ["toScore"] = 20,
                        ["toValue"] = 100,
                        ["round"] = "ceil"
                    }
                }
            ],
            hooks,
            actions,
            new CollectionIndex(),
            errors,
            ref sourceOrder);

        Assert.True(compiled != null && compiled.Count == 1 && errors.Count == 0, string.Join("; ", errors));
        Assert.Equal(HookIds.PlayerInteraction, compiled![0].Hook);
        Assert.Equal(VerbIds.MeleeDamage, compiled[0].Verb);
        Assert.Equal(HookIds.Default, compiled[0].Phase);
    }

    [Fact]
    [Trait("Layer", "Compiler")]
    public void MissingVerb_FailsCompile()
    {
        HookRegistry hooks = new();
        AbilityActionRegistry actions = new(hooks);
        AbilityBootstrap.RegisterBuiltIns(hooks, actions);

        List<string> errors = new();
        int sourceOrder = 0;
        List<AbilityRule>? compiled = AbilityRuleCompiler.CompileEffects(
            SkillId,
            nodeId: null,
            tier: null,
            [
                new AbilityEffectJson
                {
                    hook = "prosequor:block-interaction",
                    action = "prosequor:add-skill-scaled-percent",
                    @params = new Newtonsoft.Json.Linq.JObject
                    {
                        ["base"] = 10,
                        ["perSkillLevel"] = 0,
                        ["cap"] = 10
                    }
                }
            ],
            hooks,
            actions,
            new CollectionIndex(),
            errors,
            ref sourceOrder);

        Assert.Null(compiled);
        Assert.Contains(errors, e => e.Contains("verb is required", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("Layer", "Compiler")]
    public void UnknownHook_FailsCompile()
    {
        HookRegistry hooks = new();
        AbilityActionRegistry actions = new(hooks);
        AbilityBootstrap.RegisterBuiltIns(hooks, actions);

        List<string> errors = new();
        int sourceOrder = 0;
        List<AbilityRule>? compiled = AbilityRuleCompiler.CompileEffects(
            SkillId,
            nodeId: null,
            tier: null,
            [
                new AbilityEffectJson
                {
                    hook = "prosequor:drops",
                    verb = "prosequor:mutate-drops",
                    phase = "quantity",
                    action = "prosequor:add-skill-scaled-percent",
                    @params = new Newtonsoft.Json.Linq.JObject
                    {
                        ["base"] = 10,
                        ["perSkillLevel"] = 0,
                        ["cap"] = 10
                    }
                }
            ],
            hooks,
            actions,
            new CollectionIndex(),
            errors,
            ref sourceOrder);

        Assert.Null(compiled);
        Assert.Contains(errors, e => e.Contains("unknown hook", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("Layer", "Compiler")]
    public void WhenVerb_FailsCompile()
    {
        HookRegistry hooks = new();
        AbilityActionRegistry actions = new(hooks);
        AbilityBootstrap.RegisterBuiltIns(hooks, actions);

        List<string> errors = new();
        int sourceOrder = 0;
        List<AbilityRule>? compiled = AbilityRuleCompiler.CompileEffects(
            SkillId,
            nodeId: null,
            tier: null,
            [
                new AbilityEffectJson
                {
                    hook = "prosequor:block-interaction",
                    verb = "prosequor:mutate-drops",
                    phase = "quantity",
                    action = "prosequor:add-skill-scaled-percent",
                    when = new AbilityWhenJson { verb = "dig", tags = ["target:<clay>"] },
                    @params = new Newtonsoft.Json.Linq.JObject
                    {
                        ["base"] = 10,
                        ["perSkillLevel"] = 0,
                        ["cap"] = 10
                    }
                }
            ],
            hooks,
            actions,
            BuildClayCollections(),
            errors,
            ref sourceOrder);

        Assert.Null(compiled);
        Assert.Contains(errors, e => e.Contains("when.verb is removed", StringComparison.Ordinal));
    }

    static CollectionIndex BuildClayCollections()
    {
        CollectionIndex collections = new();
        collections.EnsureKey("clay");
        collections.AddCode("clay", "game:clay-blue-raw");
        return collections;
    }

    [Fact]
    [Trait("Layer", "Pipeline")]
    public void MutateDrops_TargetTagMatchesWithoutClassToken()
    {
        HookRegistry hooks = new();
        AbilityActionRegistry actions = new(hooks);
        AbilityBootstrap.RegisterBuiltIns(hooks, actions);

        CollectionIndex collections = new();
        Assert.True(
            AbilityRuleCompiler.TryCompileWhen(
                "surface-fixture",
                new AbilityWhenJson { tags = ["target:game:crop-spelt-9"] },
                collections,
                out AbilityWhenFilter? when,
                out string whenError),
            whenError);

        PipelineSkillRegistry skills = new();
        skills.Register(new SkillDef
        {
            Id = SkillId,
            Rules =
            [
                new AbilityRule
                {
                    RuleId = "crop-qty",
                    Hook = HookIds.BlockInteraction,
                    Verb = VerbIds.MutateDrops,
                    Phase = HookIds.Quantity,
                    Action = ActionIds.Number,
                    When = when!,
                    Parameters = ParseNumberSpec("{\"op\":\"scale\",\"base\":0.50,\"perSkillLevel\":0,\"cap\":0.50}"),
                    Source = new AbilityRuleSource { SkillId = SkillId },
                    Priority = 0,
                    SourceOrder = 1
                }
            ]
        });

        AbilityPipeline pipeline = new(actions, skills);
        ActionTestProgress progress = new();
        progress.SetSkillLevel(SkillId, 1);

        DropsContext crop = new()
        {
            World = null!,
            Tags = collections,
            Progress = progress,
            Fact = new AbilityAction
            {
                Verb = VerbIds.MutateDrops.Value,
                ActorUid = "fixture",
                Target = "game:crop-spelt-9"
            }
        };
        // 1 * (1 + 0.5) = 1.5
        Assert.Equal(1.5f, pipeline.Run(
            HookIds.BlockInteraction,
            VerbIds.MutateDrops,
            HookIds.Quantity,
            crop,
            1f),
            precision: 4);

        DropsContext other = new()
        {
            World = null!,
            Tags = collections,
            Progress = progress,
            Fact = new AbilityAction
            {
                Verb = VerbIds.MutateDrops.Value,
                ActorUid = "fixture",
                Target = "game:soil-medium-none"
            }
        };
        Assert.Equal(1f, pipeline.Run(
            HookIds.BlockInteraction,
            VerbIds.MutateDrops,
            HookIds.Quantity,
            other,
            1f),
            precision: 4);
    }

    static NumberSpec ParseNumberSpec(string json)
    {
        Assert.True(
            NumberSpec.TryParse(
                Newtonsoft.Json.Linq.JObject.Parse(json),
                out NumberSpec? spec,
                out string error),
            error);
        return spec!;
    }
}
