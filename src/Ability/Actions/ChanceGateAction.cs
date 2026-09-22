using System;
using Newtonsoft.Json.Linq;
using Prosequor.Ability.Hooks;
using Vintagestory.API.Common;

namespace Prosequor.Ability.Actions;

public sealed class ChanceGateParams
{
    public required ChanceProducer Chance { get; init; }
    public NestedActionRef? OnSuccess { get; init; }
    public NestedActionRef? OnFailure { get; init; }
}

/// <summary>Shared parse/apply for <c>prosequor:chance</c> across phases.</summary>
public static class ChanceGate
{
    public static bool TryParseParams(
        JObject? raw,
        IAbilityActionRegistry actions,
        HookId hook,
        VerbId verb,
        PhaseId phase,
        out ChanceGateParams? parameters,
        out string error)
    {
        parameters = null;
        if (raw == null)
        {
            error = "params are required.";
            return false;
        }

        if (!ChanceProducer.TryParse(
                raw["chance"],
                actions,
                hook,
                verb,
                progressForValidation: null,
                out ChanceProducer? chance,
                out error)
            || chance == null)
        {
            return false;
        }

        NestedActionRef? onSuccess = null;
        if (raw["onSuccess"] != null)
        {
            if (!AbilityNestedActions.TryParseSamePhase(
                    raw["onSuccess"],
                    actions,
                    hook,
                    verb,
                    phase,
                    out onSuccess,
                    out error))
            {
                return false;
            }
        }

        NestedActionRef? onFailure = null;
        if (raw["onFailure"] != null)
        {
            if (!AbilityNestedActions.TryParseSamePhase(
                    raw["onFailure"],
                    actions,
                    hook,
                    verb,
                    phase,
                    out onFailure,
                    out error))
            {
                return false;
            }
        }

        parameters = new ChanceGateParams
        {
            Chance = chance,
            OnSuccess = onSuccess,
            OnFailure = onFailure
        };
        error = "";
        return true;
    }

    public static TValue Apply<TValue>(
        IHookContext context,
        TValue value,
        ChanceGateParams parameters,
        AbilityRuleSource source)
    {
        float p = parameters.Chance.Evaluate(context, source);
        Random rand = ResolveRand(context);
        bool success = p > 0f && rand.NextDouble() < p;
        if (success)
        {
            return parameters.OnSuccess == null
                ? value
                : AbilityNestedActions.Run(parameters.OnSuccess, context, value, source);
        }

        return parameters.OnFailure == null
            ? value
            : AbilityNestedActions.Run(parameters.OnFailure, context, value, source);
    }

    static Random ResolveRand(IHookContext context)
    {
        IWorldAccessor? world = context switch
        {
            DropsContext drops => drops.World,
            ItemDurabilityContext usage => usage.World,
            SuccessChanceContext success => success.World,
            MutateProcessContext processed => processed.World,
            ConsumeBaitContext bait => bait.World,
            CraftMutateOutputContext craft => craft.World,
            ReinforceContext reinforce => reinforce.World,
            HeatStructureDamageContext heat => heat.World,
            _ => null
        };

        // Pure tests may omit World; fall back to a seeded RNG so chance gates stay deterministic.
        return world?.Rand ?? new Random(0);
    }
}

public sealed class ChanceDropsQuantityAction
    : AbilityActionHandler<DropsContext, float, ChanceGateParams>
{
    readonly IAbilityActionRegistry actions;

    public ChanceDropsQuantityAction(IAbilityActionRegistry actions) => this.actions = actions;

    public override ActionId Id => ActionIds.Chance;
    public override HookId Hook => HookIds.BlockInteraction;
    public override VerbId Verb => VerbIds.MutateDrops;
    public override PhaseId Phase => HookIds.Quantity;

    protected override bool TryParse(JObject? raw, out ChanceGateParams? parameters, out string error) =>
        ChanceGate.TryParseParams(raw, actions, Hook, Verb, Phase, out parameters, out error);

    protected override float Apply(
        DropsContext context,
        float value,
        ChanceGateParams parameters,
        AbilityRuleSource source) =>
        ChanceGate.Apply(context, value, parameters, source);
}

public sealed class ChanceDropsStackAction
    : AbilityActionHandler<DropsContext, ItemStack, ChanceGateParams>
{
    readonly IAbilityActionRegistry actions;

    public ChanceDropsStackAction(IAbilityActionRegistry actions) => this.actions = actions;

    public override ActionId Id => ActionIds.Chance;
    public override HookId Hook => HookIds.BlockInteraction;
    public override VerbId Verb => VerbIds.MutateDrops;
    public override PhaseId Phase => HookIds.Stack;

    protected override bool TryParse(JObject? raw, out ChanceGateParams? parameters, out string error) =>
        ChanceGate.TryParseParams(raw, actions, Hook, Verb, Phase, out parameters, out error);

    protected override ItemStack Apply(
        DropsContext context,
        ItemStack value,
        ChanceGateParams parameters,
        AbilityRuleSource source) =>
        ChanceGate.Apply(context, value, parameters, source);
}

public sealed class ChanceItemUsageAmountAction
    : AbilityActionHandler<ItemDurabilityContext, int, ChanceGateParams>
{
    readonly IAbilityActionRegistry actions;
    readonly VerbId verb;

    public ChanceItemUsageAmountAction(IAbilityActionRegistry actions, VerbId verb)
    {
        this.actions = actions;
        this.verb = verb;
    }

    public override ActionId Id => ActionIds.Chance;
    public override HookId Hook => HookIds.ItemInteraction;
    public override VerbId Verb => verb;
    public override PhaseId Phase => HookIds.Amount;

    protected override bool TryParse(JObject? raw, out ChanceGateParams? parameters, out string error) =>
        ChanceGate.TryParseParams(raw, actions, Hook, Verb, Phase, out parameters, out error);

    protected override int Apply(
        ItemDurabilityContext context,
        int value,
        ChanceGateParams parameters,
        AbilityRuleSource source) =>
        ChanceGate.Apply(context, value, parameters, source);
}

public sealed class ChanceOnBaitRestockAction
    : AbilityActionHandler<ConsumeBaitContext, int, ChanceGateParams>
{
    readonly IAbilityActionRegistry actions;

    public ChanceOnBaitRestockAction(IAbilityActionRegistry actions) => this.actions = actions;

    public override ActionId Id => ActionIds.Chance;
    public override HookId Hook => HookIds.ItemInteraction;
    public override VerbId Verb => VerbIds.ConsumeBait;
    public override PhaseId Phase => HookIds.Restock;

    protected override bool TryParse(JObject? raw, out ChanceGateParams? parameters, out string error) =>
        ChanceGate.TryParseParams(raw, actions, Hook, Verb, Phase, out parameters, out error);

    protected override int Apply(
        ConsumeBaitContext context,
        int value,
        ChanceGateParams parameters,
        AbilityRuleSource source) =>
        ChanceGate.Apply(context, value, parameters, source);
}

public sealed class ChanceDropsStacksAction
    : AbilityActionHandler<DropsContext, IReadOnlyList<ItemStack>, ChanceGateParams>
{
    readonly IAbilityActionRegistry actions;

    public ChanceDropsStacksAction(IAbilityActionRegistry actions) => this.actions = actions;

    public override ActionId Id => ActionIds.Chance;
    public override HookId Hook => HookIds.BlockInteraction;
    public override VerbId Verb => VerbIds.MutateDrops;
    public override PhaseId Phase => HookIds.Stacks;

    protected override bool TryParse(JObject? raw, out ChanceGateParams? parameters, out string error) =>
        ChanceGate.TryParseParams(raw, actions, Hook, Verb, Phase, out parameters, out error);

    protected override IReadOnlyList<ItemStack> Apply(
        DropsContext context,
        IReadOnlyList<ItemStack> value,
        ChanceGateParams parameters,
        AbilityRuleSource source) =>
        ChanceGate.Apply(context, value, parameters, source);
}

public sealed class ChanceMutateProcessStacksAction
    : AbilityActionHandler<MutateProcessContext, IReadOnlyList<ItemStack>, ChanceGateParams>
{
    readonly IAbilityActionRegistry actions;

    public ChanceMutateProcessStacksAction(IAbilityActionRegistry actions) => this.actions = actions;

    public override ActionId Id => ActionIds.Chance;
    public override HookId Hook => HookIds.BlockInteraction;
    public override VerbId Verb => VerbIds.MutateProcess;
    public override PhaseId Phase => HookIds.Stacks;

    protected override bool TryParse(JObject? raw, out ChanceGateParams? parameters, out string error) =>
        ChanceGate.TryParseParams(raw, actions, Hook, Verb, Phase, out parameters, out error);

    protected override IReadOnlyList<ItemStack> Apply(
        MutateProcessContext context,
        IReadOnlyList<ItemStack> value,
        ChanceGateParams parameters,
        AbilityRuleSource source) =>
        ChanceGate.Apply(context, value, parameters, source);
}

/// <summary>Chance gate on craft-grid ingredient refund.</summary>
public sealed class ChanceCraftRefundAction
    : AbilityActionHandler<CraftMutateOutputContext, int, ChanceGateParams>
{
    readonly IAbilityActionRegistry actions;

    public ChanceCraftRefundAction(IAbilityActionRegistry actions) => this.actions = actions;

    public override ActionId Id => ActionIds.Chance;
    public override HookId Hook => HookIds.CraftingInteraction;
    public override VerbId Verb => VerbIds.MutateOutput;
    public override PhaseId Phase => HookIds.Refund;

    protected override bool TryParse(JObject? raw, out ChanceGateParams? parameters, out string error) =>
        ChanceGate.TryParseParams(raw, actions, Hook, Verb, Phase, out parameters, out error);

    protected override int Apply(
        CraftMutateOutputContext context,
        int value,
        ChanceGateParams parameters,
        AbilityRuleSource source) =>
        ChanceGate.Apply(context, value, parameters, source);
}

/// <summary>Chance gate for item-interaction mutate-drops quantity.</summary>
public sealed class ChanceItemMutateDropsQuantityAction
    : AbilityActionHandler<DropsContext, float, ChanceGateParams>
{
    readonly IAbilityActionRegistry actions;

    public ChanceItemMutateDropsQuantityAction(IAbilityActionRegistry actions) => this.actions = actions;

    public override ActionId Id => ActionIds.Chance;
    public override HookId Hook => HookIds.ItemInteraction;
    public override VerbId Verb => VerbIds.MutateDrops;
    public override PhaseId Phase => HookIds.Quantity;

    protected override bool TryParse(JObject? raw, out ChanceGateParams? parameters, out string error) =>
        ChanceGate.TryParseParams(raw, actions, Hook, Verb, Phase, out parameters, out error);

    protected override float Apply(
        DropsContext context,
        float value,
        ChanceGateParams parameters,
        AbilityRuleSource source) =>
        ChanceGate.Apply(context, value, parameters, source);
}

/// <summary>Chance gate for item-interaction mutate-drops stacks (e.g. panning Double Dipper).</summary>
public sealed class ChanceItemMutateDropsStacksAction
    : AbilityActionHandler<DropsContext, IReadOnlyList<ItemStack>, ChanceGateParams>
{
    readonly IAbilityActionRegistry actions;

    public ChanceItemMutateDropsStacksAction(IAbilityActionRegistry actions) => this.actions = actions;

    public override ActionId Id => ActionIds.Chance;
    public override HookId Hook => HookIds.ItemInteraction;
    public override VerbId Verb => VerbIds.MutateDrops;
    public override PhaseId Phase => HookIds.Stacks;

    protected override bool TryParse(JObject? raw, out ChanceGateParams? parameters, out string error) =>
        ChanceGate.TryParseParams(raw, actions, Hook, Verb, Phase, out parameters, out error);

    protected override IReadOnlyList<ItemStack> Apply(
        DropsContext context,
        IReadOnlyList<ItemStack> value,
        ChanceGateParams parameters,
        AbilityRuleSource source) =>
        ChanceGate.Apply(context, value, parameters, source);
}

/// <summary>Chance gate for entity-interaction animal-pet / default (friendliness gain).</summary>
public sealed class ChanceAnimalPetDefaultAction
    : AbilityActionHandler<AnimalBehaviorContext, int, ChanceGateParams>
{
    readonly IAbilityActionRegistry actions;

    public ChanceAnimalPetDefaultAction(IAbilityActionRegistry actions) => this.actions = actions;

    public override ActionId Id => ActionIds.Chance;
    public override HookId Hook => HookIds.EntityInteraction;
    public override VerbId Verb => VerbIds.AnimalPet;
    public override PhaseId Phase => HookIds.Default;

    protected override bool TryParse(JObject? raw, out ChanceGateParams? parameters, out string error) =>
        ChanceGate.TryParseParams(raw, actions, Hook, Verb, Phase, out parameters, out error);

    protected override int Apply(
        AnimalBehaviorContext context,
        int value,
        ChanceGateParams parameters,
        AbilityRuleSource source) =>
        ChanceGate.Apply(context, value, parameters, source);
}
