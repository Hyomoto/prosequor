using Newtonsoft.Json.Linq;
using Prosequor.Ability.Hooks;

namespace Prosequor.Ability.Actions;

public sealed class AddFriendlinessParams
{
    public int Base { get; init; }
}

/// <summary>
/// Adds <c>params.base</c> to <see cref="AnimalBehaviorContext.FriendlinessGain"/>
/// on animal-pet / default. Leaves the allow int fold unchanged.
/// </summary>
public sealed class AddFriendlinessAction
    : AbilityActionHandler<AnimalBehaviorContext, int, AddFriendlinessParams>
{
    public override ActionId Id => ActionIds.AddFriendliness;
    public override HookId Hook => HookIds.EntityInteraction;
    public override VerbId Verb => VerbIds.AnimalPet;
    public override PhaseId Phase => HookIds.Default;

    protected override bool TryParse(JObject? raw, out AddFriendlinessParams? parameters, out string error)
    {
        parameters = null;
        int value = raw?.Value<int?>("base") ?? int.MinValue;
        if (value < 0)
        {
            error = "base must be >= 0.";
            return false;
        }

        parameters = new AddFriendlinessParams { Base = value };
        error = "";
        return true;
    }

    protected override int Apply(
        AnimalBehaviorContext context,
        int value,
        AddFriendlinessParams parameters,
        AbilityRuleSource source)
    {
        context.FriendlinessGain += parameters.Base;
        return value;
    }
}
