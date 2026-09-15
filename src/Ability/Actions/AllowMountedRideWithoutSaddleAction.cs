using Newtonsoft.Json.Linq;
using Prosequor.Ability.Hooks;

namespace Prosequor.Ability.Actions;

/// <summary>
/// Sets mounted/<c>can-ride</c> to true (1): allow riding when the mount has a bridle but no saddle.
/// </summary>
public sealed class AllowMountedRideWithoutSaddleAction
    : AbilityActionHandler<MountedContext, float, object>
{
    public override ActionId Id => ActionIds.AllowMountedRideWithoutSaddle;
    public override HookId Hook => HookIds.EntityInteraction;
    public override VerbId Verb => VerbIds.Mounted;
    public override PhaseId Phase => HookIds.CanRide;

    protected override bool TryParse(JObject? raw, out object? parameters, out string error)
    {
        parameters = new object();
        error = "";
        return true;
    }

    protected override float Apply(
        MountedContext context,
        float value,
        object parameters,
        AbilityRuleSource source) =>
        1f;
}
