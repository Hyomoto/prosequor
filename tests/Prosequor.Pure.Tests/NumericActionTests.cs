using Prosequor.Ability;
using Prosequor.Ability.Actions;
using Prosequor.Ability.Hooks;
using Prosequor.Data;
using Xunit;

namespace Prosequor.Pure.Tests;

/// <summary>
/// Known-in → expected-out for pure numeric actions. One case per math shape;
/// phase bindings that share the same Apply body are not duplicated.
/// Stack / World / Entity actions are deferred.
/// </summary>
public class NumericActionTests
{
    const string SkillId = "test-skill";
    const string AttrId = "strength";

    static AbilityRuleSource SkillSource() => new() { SkillId = SkillId };

    static AbilityRuleSource AttrSource() => new()
    {
        SkillId = AttrId,
        AttributeId = AttrId
    };

    static AbilityRuleSource EmptyAttrSource() => new() { SkillId = AttrId };

    static NumberSpec MustNumber(string json)
    {
        Assert.True(
            NumberSpec.TryParse(Newtonsoft.Json.Linq.JObject.Parse(json), out NumberSpec? spec, out string error),
            error);
        return spec!;
    }

    [Fact]
    [Trait("Layer", "Action")]
    public void AddMappedNumberInt_AddsMappedDelta()
    {
        var progress = new ActionTestProgress();
        progress.SetAttribute(AttrId, 10);
        var action = new AddMappedNumberIntAction(VerbIds.MeleeDamage);
        var context = new PlayerInteractionContext { Progress = progress };
        var parameters = new MappedNumberParams
        {
            FromScore = 0,
            FromValue = 0f,
            ToScore = 20,
            ToValue = 100f,
            Round = "ceil"
        };

        // Midpoint of 0→100 over 0→20 at score 10 → 50.
        object result = action.Apply(context, 7, parameters, AttrSource());
        Assert.Equal(57, (int)result);
    }

    [Fact]
    [Trait("Layer", "Action")]
    public void AddMappedNumberInt_NoAttributeId_LeavesValue()
    {
        var progress = new ActionTestProgress();
        progress.SetAttribute(AttrId, 18);
        var action = new AddMappedNumberIntAction(VerbIds.BasicSlots);
        var context = new PlayerInteractionContext { Progress = progress };
        var parameters = new MappedNumberParams
        {
            FromScore = 0,
            FromValue = 0f,
            ToScore = 18,
            ToValue = 5f,
            Round = "ceil"
        };

        object result = action.Apply(context, 3, parameters, EmptyAttrSource());
        Assert.Equal(3, (int)result);
    }

    [Fact]
    [Trait("Layer", "Action")]
    public void AddMappedNumberFloat_AddsMappedDelta()
    {
        var progress = new ActionTestProgress();
        progress.SetAttribute(AttrId, 14);
        var action = new AddMappedNumberFloatAction(VerbIds.CatEyes);
        var context = new CatEyesContext { Progress = progress };
        var parameters = new MappedNumberParams
        {
            FromScore = 10,
            FromValue = 0f,
            ToScore = 18,
            ToValue = 1f
        };

        // t = (14-10)/(18-10) = 0.5 → delta 0.5
        object result = action.Apply(context, 0.25f, parameters, AttrSource());
        Assert.Equal(0.75f, (float)result, precision: 4);
    }

    [Fact]
    [Trait("Layer", "Action")]
    public void AddMappedNumberOnDamage_Scale_MultipliesByMappedFraction()
    {
        var progress = new ActionTestProgress();
        progress.SetAttribute(AttrId, 10);
        var action = new AddMappedNumberOnDamageAction(HookIds.Amount);
        // Entity required by type; action only reads Progress + score.
        var context = new TakeDamageContext
        {
            Progress = progress,
            Entity = null!,
            CurrentHealth = 20f,
            DamageSource = null!
        };
        var parameters = new MappedNumberParams
        {
            Op = "scale",
            FromScore = 0,
            FromValue = 1.0f,
            ToScore = 20,
            ToValue = 0.5f
        };

        // score 10 → mapped 0.75; 10 * 0.75 = 7.5
        object result = action.Apply(context, 10f, parameters, AttrSource());
        Assert.Equal(7.5f, (float)result, precision: 4);
    }

    [Fact]
    [Trait("Layer", "Action")]
    public void Number_AddLiteral_AddsValue()
    {
        var action = new NumberDropsQuantityAction();
        var context = new DropsContext
        {
            World = null!,
            Tags = new CollectionIndex()
        };
        Assert.True(NumberSpec.TryParse(
            Newtonsoft.Json.Linq.JObject.Parse("{\"op\":\"add\",\"value\":0.25}"),
            out NumberSpec? parameters,
            out _),
            "parse");

        object result = action.Apply(context, 2f, parameters!, SkillSource());
        Assert.Equal(2.25f, (float)result, precision: 4);
    }

    [Fact]
    [Trait("Layer", "Action")]
    public void Number_ScaleLiteral_ScalesValue()
    {
        var action = new NumberDropsQuantityAction();
        var context = new DropsContext
        {
            World = null!,
            Tags = new CollectionIndex()
        };
        Assert.True(NumberSpec.TryParse(
            Newtonsoft.Json.Linq.JObject.Parse("{\"op\":\"scale\",\"value\":0.25}"),
            out NumberSpec? parameters,
            out _),
            "parse");

        object result = action.Apply(context, 2f, parameters!, SkillSource());
        Assert.Equal(2.5f, (float)result, precision: 4);
    }

    [Fact]
    [Trait("Layer", "Action")]
    public void NumberSpec_DefaultOp_AllowsOmittingOp()
    {
        Assert.True(NumberSpec.TryParse(
            Newtonsoft.Json.Linq.JObject.Parse(
                "{\"base\":0.05,\"perSkillLevel\":0.01,\"cap\":0.25}"),
            defaultOp: "add",
            out NumberSpec? spec,
            out string error),
            error);
        Assert.NotNull(spec);

        var progress = new ActionTestProgress();
        progress.SetSkillLevel(SkillId, 0);
        var context = new DropsContext
        {
            World = null!,
            Tags = new CollectionIndex(),
            Progress = progress
        };
        Assert.Equal(0.05f, spec!.Apply(0f, context, SkillSource()), precision: 4);
    }

    [Fact]
    [Trait("Layer", "Action")]
    public void ChanceProducer_NumberSpec_OmitsOp_UsesUnitProbability()
    {
        HookRegistry hooks = new();
        AbilityActionRegistry actions = new(hooks);
        AbilityBootstrap.RegisterBuiltIns(hooks, actions, new AffixListRegistry());

        Assert.True(ChanceProducer.TryParse(
            Newtonsoft.Json.Linq.JObject.Parse(
                "{\"base\":0.05,\"perSkillLevel\":0.01,\"cap\":0.25}"),
            actions,
            HookIds.BlockInteraction,
            VerbIds.MutateDrops,
            progressForValidation: null,
            out ChanceProducer? chance,
            out string error),
            error);
        Assert.NotNull(chance);

        var progress = new ActionTestProgress();
        progress.SetSkillLevel(SkillId, 0);
        var context = new DropsContext
        {
            World = null!,
            Tags = new CollectionIndex(),
            Progress = progress
        };
        Assert.Equal(0.05f, chance!.Evaluate(context, SkillSource()), precision: 4);

        Assert.False(ChanceProducer.TryParse(
            Newtonsoft.Json.Linq.JObject.Parse(
                "{\"op\":\"scale\",\"base\":0.05,\"perSkillLevel\":0,\"cap\":0.05}"),
            actions,
            HookIds.BlockInteraction,
            VerbIds.MutateDrops,
            progressForValidation: null,
            out _,
            out _));
    }

    [Fact]
    [Trait("Layer", "Action")]
    public void Number_CraftQuantity_ScalesRecipeBase()
    {
        var action = new NumberCraftQuantityAction();
        var context = new CraftMutateOutputContext();
        Assert.True(NumberSpec.TryParse(
            Newtonsoft.Json.Linq.JObject.Parse(
                "{\"op\":\"scale\",\"base\":0.15,\"perSkillLevel\":0,\"cap\":0.15}"),
            out NumberSpec? parameters,
            out _),
            "parse");

        object result = action.Apply(context, 100f, parameters!, SkillSource());
        Assert.Equal(115f, (float)result, precision: 4);
    }

    [Fact]
    [Trait("Layer", "Action")]
    public void Number_ProcessQuantity_ScalesOutputCount()
    {
        var action = new NumberProcessQuantityAction();
        var context = new MutateProcessContext
        {
            World = null!,
            OutputSlot = null!,
            Tags = null!,
            Variants = new CollectibleVariantTable()
        };
        Assert.True(NumberSpec.TryParse(
            Newtonsoft.Json.Linq.JObject.Parse(
                "{\"op\":\"scale\",\"base\":0.15,\"perSkillLevel\":0,\"cap\":0.15}"),
            out NumberSpec? parameters,
            out _),
            "parse");

        object result = action.Apply(context, 100f, parameters!, SkillSource());
        Assert.Equal(115f, (float)result, precision: 4);
    }

    [Fact]
    [Trait("Layer", "Action")]
    public void NumberMounted_AddLiteral_SetsChanceFraction()
    {
        var action = new NumberMountedAction(HookIds.HungerRate);
        var context = new MountedContext();
        NumberSpec parameters = MustNumber("{\"op\":\"add\",\"value\":0.25}");

        object result = action.Apply(context, 0f, parameters, SkillSource());
        Assert.Equal(0.25f, (float)result, precision: 4);
    }

    [Fact]
    [Trait("Layer", "Action")]
    public void NumberMounted_AddLiteral_AddsMoveSpeedFraction()
    {
        var action = new NumberMountedAction(HookIds.MoveSpeed);
        var context = new MountedContext();
        NumberSpec parameters = MustNumber("{\"op\":\"add\",\"value\":0.20}");

        object result = action.Apply(context, 1.1f, parameters, SkillSource());
        Assert.Equal(1.3f, (float)result, precision: 4);
    }

    [Fact]
    [Trait("Layer", "Action")]
    public void NumberSuccessChance_ScaleLiteral_ScalesValue()
    {
        var action = new NumberSuccessChanceAction(VerbIds.PlantSapling);
        var context = new SuccessChanceContext
        {
            World = null!,
            BaseValue = 0.5f
        };
        NumberSpec parameters = MustNumber("{\"op\":\"scale\",\"value\":0.5}");

        object result = action.Apply(context, 0.4f, parameters, SkillSource());
        Assert.Equal(0.6f, (float)result, precision: 4);
    }

    [Fact]
    [Trait("Layer", "Action")]
    public void Number_AddSkillScaled_AddsRawOperand()
    {
        var progress = new ActionTestProgress();
        progress.SetSkillLevel(SkillId, 5);
        var action = new NumberDropsQuantityAction();
        var context = new DropsContext
        {
            World = null!,
            Progress = progress,
            Tags = new CollectionIndex()
        };
        Assert.True(NumberSpec.TryParse(
            Newtonsoft.Json.Linq.JObject.Parse(
                "{\"op\":\"add\",\"base\":0.10,\"perSkillLevel\":0.02,\"cap\":0.50}"),
            out NumberSpec? parameters,
            out _),
            "parse");

        // min(0.50, 0.10+0.10) = 0.2 → 1 + 0.2 = 1.2
        object result = action.Apply(context, 1f, parameters!, SkillSource());
        Assert.Equal(1.2f, (float)result, precision: 4);
    }

    [Fact]
    [Trait("Layer", "Action")]
    public void Number_AddSkillScaled_RespectsCap()
    {
        var progress = new ActionTestProgress();
        progress.SetSkillLevel(SkillId, 100);
        var action = new NumberDropsQuantityAction();
        var context = new DropsContext
        {
            World = null!,
            Progress = progress,
            Tags = new CollectionIndex()
        };
        Assert.True(NumberSpec.TryParse(
            Newtonsoft.Json.Linq.JObject.Parse(
                "{\"op\":\"add\",\"base\":0.10,\"perSkillLevel\":0.02,\"cap\":0.30}"),
            out NumberSpec? parameters,
            out _),
            "parse");

        object result = action.Apply(context, 1f, parameters!, SkillSource());
        Assert.Equal(1.3f, (float)result, precision: 4);
    }

    [Fact]
    [Trait("Layer", "Action")]
    public void Number_HasUnlock_SecondAdd_OnCrystal()
    {
        var progress = new ActionTestProgress();
        progress.SetSkillLevel(SkillId, 5);
        progress.SetUnlockTier(SkillId, "crystalseeker", 1);
        HookRegistry hooks = new();
        AbilityActionRegistry actions = new(hooks);
        AbilityBootstrap.RegisterBuiltIns(hooks, actions);
        var gate = new HasUnlockDropsQuantityAction(actions);
        Assert.True(gate.TryParseParams(
            Newtonsoft.Json.Linq.JObject.Parse(
                "{\"unlock\":\"crystalseeker\",\"onSuccess\":{\"action\":\"prosequor:number\",\"params\":{\"op\":\"add\",\"base\":0.15,\"perSkillLevel\":0,\"cap\":0.15}}}"),
            out object parameters,
            out string error),
            error);

        var context = new DropsContext
        {
            World = null!,
            Progress = progress,
            Tags = new CollectionIndex()
        };

        // Seed 1 + 0.15 (from prior Ore Miner) + 0.15 (gated) = 1.30
        object result = gate.Apply(context, 1.15f, parameters, SkillSource());
        Assert.Equal(1.3f, (float)result, precision: 4);
    }

    [Fact]
    [Trait("Layer", "Action")]
    public void NumberGrowthDuration_ScaleLiteral_MultipliesByFraction()
    {
        var action = new NumberGrowthDurationAction(VerbIds.PlantSapling);
        var context = new GrowthDurationContext
        {
            World = null!,
            BaseValue = 1f
        };
        NumberSpec parameters = MustNumber("{\"op\":\"scale\",\"value\":-0.15}");

        object result = action.Apply(context, 10f, parameters, SkillSource());
        Assert.Equal(8.5f, (float)result, precision: 4);
    }

    [Fact]
    [Trait("Layer", "Action")]
    public void NumberSpec_SetLiteral_ReplacesValue()
    {
        var action = new NumberMountedAction(HookIds.MoveSpeed);
        var context = new MountedContext();
        NumberSpec parameters = MustNumber("{\"op\":\"set\",\"value\":1.25}");

        object result = action.Apply(context, 9f, parameters, SkillSource());
        Assert.Equal(1.25f, (float)result, precision: 4);
    }

    [Fact]
    [Trait("Layer", "Action")]
    public void Number_OfBase_RejectedOnScale()
    {
        Assert.False(NumberSpec.TryParse(
            Newtonsoft.Json.Linq.JObject.Parse(
                "{\"op\":\"scale\",\"ofBase\":true,\"base\":0,\"perSkillLevel\":0.1}"),
            out _,
            out string error));
        Assert.Contains("ofBase", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    [Trait("Layer", "Action")]
    public void Number_InteractionSpeed_Scale_MatchesFormerMultiply()
    {
        var progress = new ActionTestProgress();
        progress.SetSkillLevel(SkillId, 4);
        var action = new NumberInteractionSpeedAction();
        var context = new InteractionSpeedContext
        {
            Progress = progress,
            BaseValue = 1f
        };
        Assert.True(NumberSpec.TryParse(
            Newtonsoft.Json.Linq.JObject.Parse(
                "{\"op\":\"scale\",\"base\":0,\"perSkillLevel\":0.1}"),
            out NumberSpec? parameters,
            out _),
            "parse");

        // 2 * (1 + 0.1*4) = 2.8
        object result = action.Apply(context, 2f, parameters!, SkillSource());
        Assert.Equal(2.8f, (float)result, precision: 4);
    }

    [Fact]
    [Trait("Layer", "Action")]
    public void Number_InteractionSpeed_Scale_ZeroLevel_LeavesValue()
    {
        var action = new NumberInteractionSpeedAction();
        var context = new InteractionSpeedContext
        {
            Progress = new ActionTestProgress(),
            BaseValue = 1f
        };
        Assert.True(NumberSpec.TryParse(
            Newtonsoft.Json.Linq.JObject.Parse(
                "{\"op\":\"scale\",\"base\":0,\"perSkillLevel\":0.1}"),
            out NumberSpec? parameters,
            out _),
            "parse");

        object result = action.Apply(context, 2f, parameters!, SkillSource());
        Assert.Equal(2f, (float)result, precision: 4);
    }

    [Fact]
    [Trait("Layer", "Action")]
    public void Number_InteractionSpeed_OfBaseAdd_UsesBaseNotCurrent()
    {
        var progress = new ActionTestProgress();
        progress.SetSkillLevel(SkillId, 3);
        var action = new NumberInteractionSpeedAction();
        var context = new InteractionSpeedContext
        {
            Progress = progress,
            BaseValue = 10f
        };
        Assert.True(NumberSpec.TryParse(
            Newtonsoft.Json.Linq.JObject.Parse(
                "{\"op\":\"add\",\"ofBase\":true,\"base\":0,\"perSkillLevel\":0.2}"),
            out NumberSpec? parameters,
            out _),
            "parse");

        // seed already compounded to 20; addend uses BaseValue: 20 + 10*0.2*3 = 26
        object result = action.Apply(context, 20f, parameters!, SkillSource());
        Assert.Equal(26f, (float)result, precision: 4);
    }

    [Fact]
    [Trait("Layer", "Action")]
    public void NumberMounted_SetSkillScaled_ReplacesWithMultiplier()
    {
        var progress = new ActionTestProgress();
        progress.SetSkillLevel(SkillId, 5);
        var action = new NumberMountedAction(HookIds.MoveSpeed);
        var context = new MountedContext { Progress = progress };
        NumberSpec parameters = MustNumber(
            "{\"op\":\"set\",\"base\":1.1,\"perSkillLevel\":0.02,\"cap\":1.5}");

        // ignores input 9; min(1.5, 1.1+0.1) = 1.2
        object result = action.Apply(context, 9f, parameters, SkillSource());
        Assert.Equal(1.2f, (float)result, precision: 4);
    }

    [Fact]
    [Trait("Layer", "Action")]
    public void NumberScytheMultiBreak_ScaleSkillScaled_MultipliesThenTruncates()
    {
        var progress = new ActionTestProgress();
        progress.SetSkillLevel(SkillId, 10);
        var action = new NumberScytheMultiBreakQuantityAction();
        var context = new ScytheMultiBreakContext
        {
            Progress = progress,
            BaseValue = 1
        };
        NumberSpec parameters = MustNumber(
            "{\"op\":\"scale\",\"base\":0,\"perSkillLevel\":0.1,\"cap\":1}");

        // multiplier 2.0; (int)(5 * 2) = 10
        object result = action.Apply(context, 5, parameters, SkillSource());
        Assert.Equal(10, (int)result);
    }

    [Fact]
    [Trait("Layer", "Action")]
    public void Number_SetLiteral_FieldWorkSize()
    {
        var action = new NumberFieldWorkSizeAction();
        var context = new FieldWorkContext { BaseValue = 1 };
        NumberSpec parameters = MustNumber("{\"op\":\"set\",\"value\":3}");

        object result = action.Apply(context, 1, parameters, SkillSource());
        Assert.Equal(3, (int)result);
    }

    [Fact]
    [Trait("Layer", "Action")]
    public void Number_SetLiteral_ItemDamageAmountZero()
    {
        var action = new NumberItemDurabilityAmountAction(VerbIds.ItemDamage);
        var context = new ItemDurabilityContext
        {
            World = null!,
            ByEntity = null!,
            ItemSlot = null!,
            Collectible = null!
        };
        NumberSpec parameters = MustNumber("{\"op\":\"set\",\"value\":0}");

        object result = action.Apply(context, 4, parameters, SkillSource());
        Assert.Equal(0, (int)result);
    }

    [Fact]
    [Trait("Layer", "Action")]
    public void AllowAnimalPet_RequiresFriendlinessAboveThreshold()
    {
        var action = new AllowAnimalPetAction();
        Assert.True(action.TryParseParams(
            new Newtonsoft.Json.Linq.JObject { ["minFriendliness"] = 5 },
            out object parameters,
            out string error),
            error);

        var noAnimal = new AnimalBehaviorContext();
        Assert.Equal(0, (int)action.Apply(noAnimal, 0, parameters, SkillSource()));
    }

    [Fact]
    [Trait("Layer", "Action")]
    public void AllowMountedRideWithoutSaddle_ReturnsOne()
    {
        var action = new AllowMountedRideWithoutSaddleAction();
        var context = new MountedContext();

        object result = action.Apply(context, 0f, new object(), SkillSource());
        Assert.Equal(1f, (float)result, precision: 4);
    }

    [Fact]
    [Trait("Layer", "Action")]
    public void NumberSkillXp_OnlyMatchingSkill()
    {
        var action = new NumberSkillXpAmountAction();
        NumberSpec parameters = MustNumber("{\"op\":\"scale\",\"value\":0.5}");
        var context = new SkillXpContext
        {
            SkillId = SkillId,
            BaseAmount = 10f
        };

        Assert.Equal(
            15f,
            (float)action.Apply(context, 10f, parameters, SkillSource()),
            precision: 4);

        var other = new SkillXpContext { SkillId = "other", BaseAmount = 10f };
        Assert.Equal(
            10f,
            (float)action.Apply(other, 10f, parameters, SkillSource()),
            precision: 4);
    }

    [Fact]
    [Trait("Layer", "Action")]
    public void NumberSkillXp_ScalesBySkillLevel()
    {
        var progress = new ActionTestProgress();
        progress.SetSkillLevel(SkillId, 4);
        var action = new NumberSkillXpAmountAction();
        NumberSpec parameters = MustNumber("{\"op\":\"scale\",\"base\":0,\"perSkillLevel\":0.25}");
        var context = new SkillXpContext
        {
            Progress = progress,
            SkillId = SkillId,
            BaseAmount = 10f
        };

        // 10 * (1 + 0.25*4) = 20
        object result = action.Apply(context, 10f, parameters, SkillSource());
        Assert.Equal(20f, (float)result, precision: 4);
    }

    [Fact]
    [Trait("Layer", "Action")]
    public void NumberSkillBucketCap_OnlyMatchingSkill()
    {
        var action = new NumberSkillBucketCapAction();
        NumberSpec parameters = MustNumber("{\"op\":\"scale\",\"value\":1}");
        var context = new SkillBucketCapContext { SkillId = SkillId };

        Assert.Equal(
            200f,
            (float)action.Apply(context, 100f, parameters, SkillSource()),
            precision: 4);

        var other = new SkillBucketCapContext { SkillId = "other" };
        Assert.Equal(
            100f,
            (float)action.Apply(other, 100f, parameters, SkillSource()),
            precision: 4);
    }
}
