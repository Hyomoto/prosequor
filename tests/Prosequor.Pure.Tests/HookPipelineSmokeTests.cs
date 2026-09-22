using Newtonsoft.Json.Linq;
using Prosequor.Ability;
using Prosequor.Ability.Actions;
using Prosequor.Ability.Hooks;
using Prosequor.Data;
using Vintagestory.API.Common;
using Xunit;

namespace Prosequor.Pure.Tests;

/// <summary>
/// One synthetic Pipeline.Run smoke per hook family — proves each registered hook
/// can inject a built-in action without Atlas or shipped balance oracles.
/// </summary>
public class HookPipelineSmokeTests
{
    const string SkillId = "smoke-skill";
    const string AttrId = "perception";

    public static IEnumerable<object[]> HookCases()
    {
        yield return new object[] { "mutate-drops", HookIds.BlockInteraction, VerbIds.MutateDrops, HookIds.Quantity };
        yield return new object[] { "mutate-drops-stack", HookIds.BlockInteraction, VerbIds.MutateDrops, HookIds.Stack };
        yield return new object[] { "interaction-speed", HookIds.BlockInteraction, VerbIds.InteractionSpeed, HookIds.Default };
        yield return new object[] { "item-damage", HookIds.ItemInteraction, VerbIds.ItemDamage, HookIds.Amount };
        yield return new object[] { "block-damaged", HookIds.ItemInteraction, VerbIds.BlockDamaged, HookIds.Amount };
        yield return new object[] { "craft-damaged", HookIds.ItemInteraction, VerbIds.CraftDamaged, HookIds.Amount };
        yield return new object[] { "plant-sapling", HookIds.BlockInteraction, VerbIds.PlantSapling, HookIds.Default };
        yield return new object[] { "plant-growth", HookIds.BlockInteraction, VerbIds.PlantSapling, new PhaseId("growth") };
        yield return new object[] { "fertilize", HookIds.BlockInteraction, VerbIds.Fertilize, HookIds.Default };
        yield return new object[] { "field-work", HookIds.BlockInteraction, VerbIds.FieldWork, HookIds.Size };
        yield return new object[] { "scythe-multibreak", HookIds.BlockInteraction, VerbIds.ScytheMultibreak, HookIds.Quantity };
        yield return new object[] { "clay-form", HookIds.ItemInteraction, VerbIds.ClayForm, HookIds.AssistRadius };
        yield return new object[] { "anvil-heavy-hit-slag", HookIds.ItemInteraction, VerbIds.AnvilHeavyHit, HookIds.SlagRadius };
        yield return new object[] { "anvil-heavy-hit-assist", HookIds.ItemInteraction, VerbIds.AnvilHeavyHit, HookIds.AssistRadius };
        yield return new object[] { "anvil-heavy-hit-move", HookIds.ItemInteraction, VerbIds.AnvilHeavyHit, HookIds.MoveCount };
        yield return new object[] { "anvil-split-bits-refund", HookIds.ItemInteraction, VerbIds.AnvilSplit, HookIds.BitsRefund };
        yield return new object[] { "anvil-strike-decay-shrink", HookIds.ItemInteraction, VerbIds.AnvilStrike, HookIds.DecayShrink };
        yield return new object[] { "mutate-process", HookIds.BlockInteraction, VerbIds.MutateProcess, HookIds.Quantity };
        yield return new object[] { "heat-structure-damage", HookIds.BlockInteraction, VerbIds.HeatStructureDamage, HookIds.Skip };
        yield return new object[] { "reinforce", HookIds.ItemInteraction, VerbIds.Reinforce, HookIds.Strength };
        yield return new object[] { "mounted", HookIds.EntityInteraction, VerbIds.Mounted, HookIds.MoveSpeed };
        yield return new object[] { "repair", HookIds.ItemInteraction, VerbIds.Repair, HookIds.AddDurability };
        yield return new object[] { "mutate-output", HookIds.CraftingInteraction, VerbIds.MutateOutput, HookIds.Quantity };
        yield return new object[] { "apply-quality-base", HookIds.CraftingInteraction, VerbIds.ApplyQuality, HookIds.QualityBase };
        yield return new object[] { "consume-bait", HookIds.ItemInteraction, VerbIds.ConsumeBait, HookIds.Restock };
        yield return new object[] { "skill-xp", HookIds.Progress, VerbIds.SkillXp, HookIds.Amount };
        yield return new object[] { "skill-bucket", HookIds.Progress, VerbIds.SkillBucket, HookIds.Cap };
        yield return new object[] { "melee-damage", HookIds.PlayerInteraction, VerbIds.MeleeDamage, HookIds.Default };
        yield return new object[] { "cat-eyes", HookIds.PlayerInteraction, VerbIds.CatEyes, HookIds.Default };
        yield return new object[] { "on-damage", HookIds.PlayerInteraction, VerbIds.OnDamage, HookIds.Amount };
        yield return new object[] { "on-damage-last-stand", HookIds.PlayerInteraction, VerbIds.OnDamage, HookIds.LastStand };
    }

    [Theory]
    [MemberData(nameof(HookCases))]
    [Trait("Layer", "Pipeline")]
    public void SyntheticRootRule_TransformsSeed(string name, HookId hook, VerbId verb, PhaseId phase)
    {
        try
        {
            AssertTransforms(hook, verb, phase);
        }
        catch (Exception ex)
        {
            throw new Xunit.Sdk.XunitException($"Hook smoke failed for '{name}': {ex.Message}", ex);
        }
    }

    static void AssertTransforms(HookId hook, VerbId verb, PhaseId phase)
    {
        HookRegistry hooks = new();
        AbilityActionRegistry actions = new(hooks);
        AbilityBootstrap.RegisterBuiltIns(hooks, actions);

        ActionTestProgress progress = new();
        progress.SetSkillLevel(SkillId, 10);
        progress.SetAttribute(AttrId, 10);

        PipelineSkillRegistry skills = new();
        AttributeStatRegistry? attributeStats = null;

        AbilityRule rule;
        if (hook.Equals(HookIds.BlockInteraction)
            && verb.Equals(VerbIds.MutateDrops)
            && phase.Equals(HookIds.Stack))
        {
            ActionId bumpId = new("prosequor:smoke-bump-stack");
            actions.Register(new SmokeBumpStackAction(bumpId));
            rule = Rule(hook, verb, phase, bumpId, new AbilityRuleSource { SkillId = SkillId }, new object());
        }
        else
        {
            rule = BuildRule(hook, verb, phase, actions);
        }

        if (rule.Source.IsAttributeRule)
        {
            attributeStats = new AttributeStatRegistry();
            attributeStats.Register(new AttributeStatDef
            {
                Id = AttrId,
                Rules = [rule]
            });
        }
        else
        {
            skills.Register(new SkillDef
            {
                Id = SkillId,
                Rules = [rule]
            });
        }

        AbilityPipeline pipeline = new(actions, skills, attributeStats);

        if (hook.Equals(HookIds.BlockInteraction)
            && verb.Equals(VerbIds.MutateProcess)
            && phase.Equals(HookIds.Quantity))
        {
            float result = pipeline.Run(
                hook,
                verb,
                phase,
                new MutateProcessContext
                {
                    World = null!,
                    OutputSlot = null!,
                    Progress = progress,
                    Tags = null!,
                    Variants = new CollectibleVariantTable()
                },
                100f);
            Assert.True(
                Math.Abs(result - 100f) > 0.0001f,
                $"Expected quantity seed 100 to change for {hook}/{verb}/{phase}, got {result}.");
            return;
        }

        if (hook.Equals(HookIds.BlockInteraction)
            && verb.Equals(VerbIds.MutateDrops)
            && phase.Equals(HookIds.Stack))
        {
            ItemStack seed = new() { StackSize = 3 };
            ItemStack result = pipeline.Run(
                hook,
                verb,
                phase,
                new DropsContext { World = null!, Tags = null!, Progress = progress },
                seed);
            Assert.True(
                result.StackSize != 3,
                $"Expected stack seed size 3 to change for {hook}/{verb}/{phase}, got {result.StackSize}.");
            return;
        }

        if ((hook.Equals(HookIds.BlockInteraction) && verb.Equals(VerbIds.FieldWork))
            || (hook.Equals(HookIds.BlockInteraction) && verb.Equals(VerbIds.ScytheMultibreak))
            || (hook.Equals(HookIds.ItemInteraction) && verb.Equals(VerbIds.ClayForm))
            || (hook.Equals(HookIds.ItemInteraction) && verb.Equals(VerbIds.AnvilHeavyHit))
            || (hook.Equals(HookIds.ItemInteraction) && verb.Equals(VerbIds.AnvilSplit))
            || (hook.Equals(HookIds.ItemInteraction)
                && (verb.Equals(VerbIds.ItemDamage)
                    || verb.Equals(VerbIds.BlockDamaged)
                    || verb.Equals(VerbIds.CraftDamaged)))
            || (hook.Equals(HookIds.ItemInteraction) && verb.Equals(VerbIds.ConsumeBait))
            || (hook.Equals(HookIds.PlayerInteraction) && verb.Equals(VerbIds.MeleeDamage)))
        {
            int seed = verb.Equals(VerbIds.ItemDamage)
                || verb.Equals(VerbIds.BlockDamaged)
                || verb.Equals(VerbIds.CraftDamaged)
                || verb.Equals(VerbIds.ConsumeBait)
                ? 5
                : (verb.Equals(VerbIds.FieldWork)
                    || verb.Equals(VerbIds.AnvilHeavyHit)
                    || verb.Equals(VerbIds.AnvilSplit)
                    ? 1
                    : 2);
            int result = RunInt(pipeline, hook, verb, phase, progress, seed);
            Assert.True(result != seed, $"Expected int seed {seed} to change for {hook}/{verb}/{phase}, got {result}.");
            return;
        }

        float fSeed = 1f;
        if (verb.Equals(VerbIds.OnDamage) && phase.Equals(HookIds.Amount))
        {
            fSeed = 10f;
        }
        else if (verb.Equals(VerbIds.OnDamage) && phase.Equals(HookIds.LastStand))
        {
            fSeed = 0f;
        }

        float fResult = RunFloat(pipeline, hook, verb, phase, progress, fSeed);
        Assert.True(
            Math.Abs(fResult - fSeed) > 0.0001f,
            $"Expected float seed {fSeed} to change for {hook}/{verb}/{phase}, got {fResult}.");
    }

    static AbilityRule BuildRule(HookId hook, VerbId verb, PhaseId phase, IAbilityActionRegistry actions)
    {
        AbilityRuleSource skillSource = new() { SkillId = SkillId };
        AbilityRuleSource attrSource = new()
        {
            SkillId = AttrId,
            AttributeId = AttrId,
            MinAttributeScore = 0
        };

        return (hook.Value, verb.Value, phase.Value) switch
        {
            ("prosequor:block-interaction", "prosequor:mutate-drops", "quantity") => Rule(
                hook, verb, phase, ActionIds.Number, skillSource,
                ParseNumber("{\"op\":\"scale\",\"base\":0.50,\"perSkillLevel\":0,\"cap\":0.50}")),
            ("prosequor:block-interaction", "prosequor:interaction-speed", "default") => Rule(
                hook, verb, phase, ActionIds.Number, skillSource,
                ParseNumber("{\"op\":\"scale\",\"base\":0,\"perSkillLevel\":0.1}")),
            ("prosequor:item-interaction", "prosequor:item-damage", "amount") => Rule(
                hook, verb, phase, ActionIds.Number, skillSource,
                ParseNumber("{\"op\":\"set\",\"value\":0}")),
            ("prosequor:item-interaction", "prosequor:block-damaged", "amount") => Rule(
                hook, verb, phase, ActionIds.Number, skillSource,
                ParseNumber("{\"op\":\"set\",\"value\":0}")),
            ("prosequor:item-interaction", "prosequor:craft-damaged", "amount") => Rule(
                hook, verb, phase, ActionIds.Number, skillSource,
                ParseNumber("{\"op\":\"set\",\"value\":0}")),
            ("prosequor:block-interaction", "prosequor:plant-sapling", "default") => Rule(
                hook, verb, phase, ActionIds.Number, skillSource,
                ParseNumber("{\"op\":\"scale\",\"value\":0.25}")),
            ("prosequor:block-interaction", "prosequor:plant-sapling", "growth") => Rule(
                hook, verb, phase, ActionIds.Number, skillSource,
                ParseNumber("{\"op\":\"scale\",\"value\":-0.15}")),
            ("prosequor:block-interaction", "prosequor:fertilize", "default") => Rule(
                hook, verb, phase, ActionIds.Number, skillSource,
                ParseNumber("{\"op\":\"add\",\"base\":0.50,\"perSkillLevel\":0,\"cap\":0.50}")),
            ("prosequor:block-interaction", "prosequor:field-work", "size") => Rule(
                hook, verb, phase, ActionIds.Number, skillSource,
                ParseNumber("{\"op\":\"set\",\"value\":3}")),
            ("prosequor:block-interaction", "prosequor:scythe-multibreak", "quantity") => Rule(
                hook, verb, phase, ActionIds.Number, skillSource,
                ParseNumber("{\"op\":\"scale\",\"base\":0.50,\"perSkillLevel\":0,\"cap\":0.50}")),
            ("prosequor:item-interaction", "prosequor:clay-form", "assist-radius") => Rule(
                hook, verb, phase, ActionIds.Number, skillSource,
                ParseNumber("{\"op\":\"set\",\"value\":4}")),
            ("prosequor:item-interaction", "prosequor:anvil-heavy-hit", "slag-radius") => Rule(
                hook, verb, phase, ActionIds.Number, skillSource,
                ParseNumber("{\"op\":\"set\",\"value\":3}")),
            ("prosequor:item-interaction", "prosequor:anvil-heavy-hit", "assist-radius") => Rule(
                hook, verb, phase, ActionIds.Number, skillSource,
                ParseNumber("{\"op\":\"set\",\"value\":3}")),
            ("prosequor:item-interaction", "prosequor:anvil-heavy-hit", "move-count") => Rule(
                hook, verb, phase, ActionIds.Number, skillSource,
                ParseNumber("{\"op\":\"set\",\"value\":4}")),
            ("prosequor:item-interaction", "prosequor:anvil-split", "bits-refund") => Rule(
                hook, verb, phase, ActionIds.Number, skillSource,
                ParseNumber("{\"op\":\"set\",\"value\":6}")),
            ("prosequor:item-interaction", "prosequor:anvil-strike", "decay-shrink") => Rule(
                hook, verb, phase, ActionIds.Number, skillSource,
                ParseNumber("{\"op\":\"set\",\"value\":0.02}")),
            ("prosequor:block-interaction", "prosequor:mutate-process", "quantity") => Rule(
                hook, verb, phase, ActionIds.Number, skillSource,
                ParseNumber("{\"op\":\"scale\",\"base\":0.50,\"perSkillLevel\":0,\"cap\":0.50}")),
            ("prosequor:block-interaction", "prosequor:heat-structure-damage", "skip") => Rule(
                hook, verb, phase, ActionIds.Number, skillSource,
                ParseNumber("{\"op\":\"add\",\"value\":0.25}")),
            ("prosequor:item-interaction", "prosequor:reinforce", "strength") => Rule(
                hook, verb, phase, ActionIds.Number, skillSource,
                ParseNumber("{\"op\":\"scale\",\"base\":0.10,\"perSkillLevel\":0,\"cap\":0.10}")),
            ("prosequor:entity-interaction", "prosequor:mounted", "move-speed") => Rule(
                hook, verb, phase, ActionIds.Number, skillSource,
                ParseNumber("{\"op\":\"add\",\"base\":0.50,\"perSkillLevel\":0,\"cap\":0.50}")),
            ("prosequor:item-interaction", "prosequor:repair", "add-durability") => Rule(
                hook, verb, phase, ActionIds.Number, skillSource,
                ParseNumber("{\"op\":\"add\",\"base\":0.50,\"perSkillLevel\":0,\"cap\":0.50}")),
            ("prosequor:crafting-interaction", "prosequor:mutate-output", "quantity") => Rule(
                hook, verb, phase, ActionIds.Number, skillSource,
                ParseNumber("{\"op\":\"scale\",\"base\":0.50,\"perSkillLevel\":0,\"cap\":0.50}")),
            ("prosequor:crafting-interaction", "prosequor:apply-quality", "quality-base") => Rule(
                hook, verb, phase, ActionIds.Number, skillSource,
                ParseNumber("{\"op\":\"add\",\"value\":50}")),
            ("prosequor:item-interaction", "prosequor:consume-bait", "restock") => Rule(
                hook, verb, phase, ActionIds.RestoreConsumedBait, skillSource, new object()),
            ("prosequor:progress", "prosequor:skill-xp", "amount") => Rule(
                hook, verb, phase, ActionIds.Number, skillSource,
                ParseNumber("{\"op\":\"scale\",\"value\":0.5}")),
            ("prosequor:progress", "prosequor:skill-bucket", "cap") => Rule(
                hook, verb, phase, ActionIds.Number, skillSource,
                ParseNumber("{\"op\":\"scale\",\"value\":1}")),
            ("prosequor:player-interaction", "prosequor:melee-damage", "default") => Rule(
                hook, verb, phase, ActionIds.AddMappedNumber, attrSource,
                new MappedNumberParams
                {
                    FromScore = 0,
                    FromValue = 0f,
                    ToScore = 20,
                    ToValue = 100f,
                    Round = "ceil"
                }),
            ("prosequor:player-interaction", "prosequor:cat-eyes", "default") => Rule(
                hook, verb, phase, ActionIds.AddMappedNumber, attrSource,
                new MappedNumberParams
                {
                    FromScore = 0,
                    FromValue = 0f,
                    ToScore = 20,
                    ToValue = 1f
                }),
            ("prosequor:player-interaction", "prosequor:on-damage", "amount") => Rule(
                hook, verb, phase, ActionIds.AddMappedNumber, attrSource,
                new MappedNumberParams
                {
                    Op = "scale",
                    FromScore = 0,
                    FromValue = 1.0f,
                    ToScore = 20,
                    ToValue = 0.5f
                }),
            ("prosequor:player-interaction", "prosequor:on-damage", "last-stand") => Rule(
                hook, verb, phase, ActionIds.AddMappedNumber, attrSource,
                new MappedNumberParams
                {
                    FromScore = 0,
                    FromValue = 0f,
                    ToScore = 20,
                    ToValue = 10f,
                    Round = "ceil"
                }),
            _ => throw new InvalidOperationException($"No smoke rule for {hook}/{verb}/{phase}.")
        };
    }

    static AbilityRule Rule(
        HookId hook,
        VerbId verb,
        PhaseId phase,
        ActionId action,
        AbilityRuleSource source,
        object parameters) =>
        new()
        {
            RuleId = $"smoke-{hook.Value}-{verb.Value}-{phase.Value}",
            Hook = hook,
            Verb = verb,
            Phase = phase,
            Action = action,
            When = new AbilityWhenFilter(),
            Parameters = parameters,
            Source = source,
            Priority = 0,
            SourceOrder = 1
        };

    static float RunFloat(
        AbilityPipeline pipeline,
        HookId hook,
        VerbId verb,
        PhaseId phase,
        ActionTestProgress progress,
        float seed) =>
        (hook.Value, verb.Value) switch
        {
            ("prosequor:block-interaction", "prosequor:mutate-drops") => pipeline.Run(
                hook, verb, phase,
                new DropsContext { World = null!, Tags = null!, Progress = progress },
                seed),
            ("prosequor:block-interaction", "prosequor:interaction-speed") => pipeline.Run(
                hook, verb, phase,
                new InteractionSpeedContext { Progress = progress, BaseValue = seed },
                seed),
            ("prosequor:block-interaction", "prosequor:plant-sapling") when phase.Value == "default" => pipeline.Run(
                hook, verb, phase,
                new SuccessChanceContext { World = null!, Progress = progress, BaseValue = seed },
                seed),
            ("prosequor:block-interaction", "prosequor:plant-sapling") when phase.Value == "growth" => pipeline.Run(
                hook, verb, phase,
                new GrowthDurationContext { World = null!, Progress = progress, BaseValue = seed },
                seed),
            ("prosequor:block-interaction", "prosequor:fertilize") => pipeline.Run(
                hook, verb, phase,
                new FertilizerAbsorbContext { World = null!, Progress = progress, BaseValue = seed },
                seed),
            ("prosequor:block-interaction", "prosequor:heat-structure-damage") => pipeline.Run(
                hook, verb, phase,
                new HeatStructureDamageContext { Progress = progress },
                seed),
            ("prosequor:item-interaction", "prosequor:reinforce") => pipeline.Run(
                hook, verb, phase,
                new ReinforceContext { Progress = progress },
                seed),
            ("prosequor:entity-interaction", "prosequor:mounted") => pipeline.Run(
                hook, verb, phase,
                new MountedContext { Progress = progress },
                seed),
            ("prosequor:item-interaction", "prosequor:repair") => pipeline.Run(
                hook, verb, phase,
                new RepairContext { Progress = progress },
                seed),
            ("prosequor:item-interaction", "prosequor:anvil-strike") => pipeline.Run(
                hook, verb, phase,
                new VoxelWorkContext { World = null!, Progress = progress },
                seed),
            ("prosequor:crafting-interaction", "prosequor:mutate-output") => pipeline.Run(
                hook, verb, phase,
                new CraftMutateOutputContext { Progress = progress },
                seed),
            ("prosequor:crafting-interaction", "prosequor:apply-quality") => pipeline.Run(
                hook, verb, phase,
                new CraftMutateOutputContext { Progress = progress },
                seed),
            ("prosequor:progress", "prosequor:skill-xp") => pipeline.Run(
                hook, verb, phase,
                new SkillXpContext { Progress = progress, SkillId = SkillId, BaseAmount = seed },
                seed),
            ("prosequor:progress", "prosequor:skill-bucket") => pipeline.Run(
                hook, verb, phase,
                new SkillBucketCapContext { Progress = progress, SkillId = SkillId },
                seed),
            ("prosequor:player-interaction", "prosequor:cat-eyes") => pipeline.Run(
                hook, verb, phase,
                new CatEyesContext { Progress = progress },
                seed),
            ("prosequor:player-interaction", "prosequor:on-damage") => pipeline.Run(
                hook, verb, phase,
                new TakeDamageContext
                {
                    Progress = progress,
                    Entity = null!,
                    CurrentHealth = 20f,
                    DamageSource = null!
                },
                seed),
            _ => throw new InvalidOperationException($"No float context for {hook}/{verb}/{phase}.")
        };

    static int RunInt(
        AbilityPipeline pipeline,
        HookId hook,
        VerbId verb,
        PhaseId phase,
        ActionTestProgress progress,
        int seed) =>
        (hook.Value, verb.Value) switch
        {
            ("prosequor:item-interaction", "prosequor:item-damage") => pipeline.Run(
                hook, verb, phase,
                new ItemDurabilityContext
                {
                    World = null!,
                    ByEntity = null!,
                    ItemSlot = null!,
                    Collectible = null!,
                    Progress = progress
                },
                seed),
            ("prosequor:item-interaction", "prosequor:block-damaged") => pipeline.Run(
                hook, verb, phase,
                new ItemDurabilityContext
                {
                    World = null!,
                    ByEntity = null!,
                    ItemSlot = null!,
                    Collectible = null!,
                    Progress = progress
                },
                seed),
            ("prosequor:item-interaction", "prosequor:craft-damaged") => pipeline.Run(
                hook, verb, phase,
                new ItemDurabilityContext
                {
                    World = null!,
                    ByEntity = null!,
                    ItemSlot = null!,
                    Collectible = null!,
                    Progress = progress
                },
                seed),
            ("prosequor:item-interaction", "prosequor:consume-bait") => pipeline.Run(
                hook, verb, phase,
                new ConsumeBaitContext
                {
                    World = null!,
                    Progress = progress,
                    ConsumedBait = new ItemStack()
                },
                seed),
            ("prosequor:block-interaction", "prosequor:field-work") => pipeline.Run(
                hook, verb, phase,
                new FieldWorkContext { Progress = progress, BaseValue = seed },
                seed),
            ("prosequor:block-interaction", "prosequor:scythe-multibreak") => pipeline.Run(
                hook, verb, phase,
                new ScytheMultiBreakContext { Progress = progress, BaseValue = seed },
                seed),
            ("prosequor:item-interaction", "prosequor:clay-form") => pipeline.Run(
                hook, verb, phase,
                new VoxelWorkContext { World = null!, Progress = progress },
                seed),
            ("prosequor:item-interaction", "prosequor:anvil-heavy-hit") => pipeline.Run(
                hook, verb, phase,
                new VoxelWorkContext { World = null!, Progress = progress },
                seed),
            ("prosequor:item-interaction", "prosequor:anvil-split") => pipeline.Run(
                hook, verb, phase,
                new VoxelWorkContext { World = null!, Progress = progress },
                seed),
            ("prosequor:player-interaction", "prosequor:melee-damage") => pipeline.Run(
                hook, verb, phase,
                new PlayerInteractionContext { Progress = progress },
                seed),
            _ => throw new InvalidOperationException($"No int context for {hook}/{verb}.")
        };

    static NumberSpec ParseNumber(string json)
    {
        Assert.True(NumberSpec.TryParse(JObject.Parse(json), out NumberSpec? spec, out string error), error);
        return spec!;
    }

    /// <summary>Smoke-only stack mutator: bumps StackSize so the pipeline has a observable seed change.</summary>
    sealed class SmokeBumpStackAction : AbilityActionHandler<DropsContext, ItemStack, object>
    {
        readonly ActionId id;

        public SmokeBumpStackAction(ActionId id) => this.id = id;

        public override ActionId Id => id;
        public override HookId Hook => HookIds.BlockInteraction;
        public override VerbId Verb => VerbIds.MutateDrops;
        public override PhaseId Phase => HookIds.Stack;

        protected override bool TryParse(JObject? raw, out object? parameters, out string error)
        {
            parameters = new object();
            error = "";
            return true;
        }

        protected override ItemStack Apply(
            DropsContext context,
            ItemStack value,
            object parameters,
            AbilityRuleSource source)
        {
            _ = context;
            _ = parameters;
            _ = source;
            value.StackSize += 1;
            return value;
        }
    }
}
