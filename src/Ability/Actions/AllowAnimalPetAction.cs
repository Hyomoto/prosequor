using Newtonsoft.Json.Linq;
using Prosequor.Ability.Hooks;

namespace Prosequor.Ability.Actions;

public sealed class AllowAnimalPetParams
{
    public int MinFriendliness { get; init; } = 5;
}

/// <summary>
/// Sets animal-pet allow fold to 1 when the animal's friendliness
/// exceeds <c>params.minFriendliness</c> (default 5). Compose memo is skipped
/// (result depends on a live entity attribute). Shares <c>default</c> with
/// <see cref="AddFriendlinessAction"/> (side channel for gain). Bool contract.
/// </summary>
public sealed class AllowAnimalPetAction
    : AbilityActionHandler<AnimalBehaviorContext, int, AllowAnimalPetParams>
{
    public override ActionId Id => ActionIds.AllowAnimalPet;
    public override HookId Hook => HookIds.EntityInteraction;
    public override VerbId Verb => VerbIds.AnimalPet;
    public override PhaseId Phase => HookIds.Default;

    protected override bool TryParse(
        JObject? raw,
        out AllowAnimalPetParams? parameters,
        out string error)
    {
        parameters = null;
        int min = raw?.Value<int?>("minFriendliness") ?? 5;
        if (min < 0)
        {
            error = "minFriendliness must be >= 0.";
            return false;
        }

        parameters = new AllowAnimalPetParams { MinFriendliness = min };
        error = "";
        return true;
    }

    protected override int Apply(
        AnimalBehaviorContext context,
        int value,
        AllowAnimalPetParams parameters,
        AbilityRuleSource source)
    {
        if (context.Animal == null)
        {
            return value;
        }

        return HusbandryFriendliness.Get(context.Animal) > parameters.MinFriendliness ? 1 : value;
    }
}
