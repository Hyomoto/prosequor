using Newtonsoft.Json.Linq;
using Prosequor.Ability.Hooks;
using Vintagestory.API.Common;

namespace Prosequor.Ability.Actions;

public sealed class HasUnlockParams
{
    /// <summary>Skill that owns the unlock node; null = use the rule's owning skill at Apply.</summary>
    public string? SkillId { get; init; }
    public required string NodeId { get; init; }
    public NestedActionRef? OnSuccess { get; init; }
    public NestedActionRef? OnFailure { get; init; }
}

/// <summary>
/// Gate like chance: runs onSuccess when the player owns any tier of the named unlock node.
/// <c>unlock</c> is a node id; optional <c>skill</c> defaults to the owning skill at Apply.
/// </summary>
public static class HasUnlockGate
{
    public static bool TryParseParams(
        JObject? raw,
        IAbilityActionRegistry actions,
        HookId hook,
        VerbId verb,
        PhaseId phase,
        out HasUnlockParams? parameters,
        out string error)
    {
        parameters = null;
        if (raw == null)
        {
            error = "params are required.";
            return false;
        }

        string? unlock = raw.Value<string>("unlock")?.Trim();
        if (string.IsNullOrWhiteSpace(unlock))
        {
            error = "unlock (node id) is required.";
            return false;
        }

        string? skill = raw.Value<string>("skill")?.Trim();
        if (string.IsNullOrWhiteSpace(skill))
        {
            skill = null;
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

        parameters = new HasUnlockParams
        {
            SkillId = skill,
            NodeId = unlock,
            OnSuccess = onSuccess,
            OnFailure = onFailure
        };
        error = "";
        return true;
    }

    public static TValue Apply<TValue>(
        IHookContext context,
        TValue value,
        HasUnlockParams parameters,
        AbilityRuleSource source)
    {
        string skillId = parameters.SkillId ?? source.SkillId;
        bool owned = (context.Progress?.GetUnlockTier(skillId, parameters.NodeId) ?? 0) > 0;
        if (owned)
        {
            return parameters.OnSuccess == null
                ? value
                : AbilityNestedActions.Run(parameters.OnSuccess, context, value, source);
        }

        return parameters.OnFailure == null
            ? value
            : AbilityNestedActions.Run(parameters.OnFailure, context, value, source);
    }
}

public sealed class HasUnlockDropsQuantityAction
    : AbilityActionHandler<DropsContext, float, HasUnlockParams>
{
    readonly IAbilityActionRegistry actions;

    public HasUnlockDropsQuantityAction(IAbilityActionRegistry actions) => this.actions = actions;

    public override ActionId Id => ActionIds.HasUnlock;
    public override HookId Hook => HookIds.BlockInteraction;
    public override VerbId Verb => VerbIds.MutateDrops;
    public override PhaseId Phase => HookIds.Quantity;

    protected override bool TryParse(JObject? raw, out HasUnlockParams? parameters, out string error) =>
        HasUnlockGate.TryParseParams(raw, actions, Hook, Verb, Phase, out parameters, out error);

    protected override float Apply(
        DropsContext context,
        float value,
        HasUnlockParams parameters,
        AbilityRuleSource source) =>
        HasUnlockGate.Apply(context, value, parameters, source);
}

public sealed class HasUnlockDropsStacksAction
    : AbilityActionHandler<DropsContext, IReadOnlyList<ItemStack>, HasUnlockParams>
{
    readonly IAbilityActionRegistry actions;

    public HasUnlockDropsStacksAction(IAbilityActionRegistry actions) => this.actions = actions;

    public override ActionId Id => ActionIds.HasUnlock;
    public override HookId Hook => HookIds.BlockInteraction;
    public override VerbId Verb => VerbIds.MutateDrops;
    public override PhaseId Phase => HookIds.Stacks;

    protected override bool TryParse(JObject? raw, out HasUnlockParams? parameters, out string error) =>
        HasUnlockGate.TryParseParams(raw, actions, Hook, Verb, Phase, out parameters, out error);

    protected override IReadOnlyList<ItemStack> Apply(
        DropsContext context,
        IReadOnlyList<ItemStack> value,
        HasUnlockParams parameters,
        AbilityRuleSource source) =>
        HasUnlockGate.Apply(context, value, parameters, source);
}

public sealed class HasUnlockDropsStackAction
    : AbilityActionHandler<DropsContext, ItemStack, HasUnlockParams>
{
    readonly IAbilityActionRegistry actions;

    public HasUnlockDropsStackAction(IAbilityActionRegistry actions) => this.actions = actions;

    public override ActionId Id => ActionIds.HasUnlock;
    public override HookId Hook => HookIds.BlockInteraction;
    public override VerbId Verb => VerbIds.MutateDrops;
    public override PhaseId Phase => HookIds.Stack;

    protected override bool TryParse(JObject? raw, out HasUnlockParams? parameters, out string error) =>
        HasUnlockGate.TryParseParams(raw, actions, Hook, Verb, Phase, out parameters, out error);

    protected override ItemStack Apply(
        DropsContext context,
        ItemStack value,
        HasUnlockParams parameters,
        AbilityRuleSource source) =>
        HasUnlockGate.Apply(context, value, parameters, source);
}
