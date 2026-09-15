using Newtonsoft.Json.Linq;
using Prosequor.Ability;
using Prosequor.Ability.Actions;
using Prosequor.Ability.Hooks;
using Xunit;

namespace Prosequor.Pure.Tests;

/// <summary>
/// Chance gate branch selection: prove onSuccess / onFailure wiring, not RNG quality.
/// </summary>
public class ChanceActionTests
{
    const string SkillId = "chance-test-skill";

    static AbilityRuleSource SkillSource() => new() { SkillId = SkillId };

    static (ChanceDropsQuantityAction Action, AbilityActionRegistry Registry) CreateQuantityChance()
    {
        HookRegistry hooks = new();
        AbilityActionRegistry registry = new(hooks);
        AbilityBootstrap.RegisterBuiltIns(hooks, registry);
        return (new ChanceDropsQuantityAction(registry), registry);
    }

    static DropsContext EmptyDrops() => new()
    {
        World = null!,
        Tags = new CollectionIndex()
    };

    [Fact]
    [Trait("Layer", "Action")]
    public void Chance_100Percent_RunsOnSuccess()
    {
        (ChanceDropsQuantityAction action, _) = CreateQuantityChance();
        Assert.True(action.TryParseParams(
            JObject.Parse(
                """
                {
                  "chance": { "percent": 100 },
                  "onSuccess": {
                    "action": "prosequor:number",
                    "params": { "op": "scale", "value": 1 }
                  }
                }
                """),
            out object parameters,
            out string error),
            error);

        object result = action.Apply(EmptyDrops(), 2f, parameters, SkillSource());
        Assert.Equal(4f, (float)result, precision: 4);
    }

    [Fact]
    [Trait("Layer", "Action")]
    public void Chance_0Percent_RunsOnFailure()
    {
        (ChanceDropsQuantityAction action, _) = CreateQuantityChance();
        Assert.True(action.TryParseParams(
            JObject.Parse(
                """
                {
                  "chance": { "percent": 0 },
                  "onSuccess": {
                    "action": "prosequor:number",
                    "params": { "op": "scale", "value": 1 }
                  },
                  "onFailure": {
                    "action": "prosequor:number",
                    "params": { "op": "scale", "value": 0.5 }
                  }
                }
                """),
            out object parameters,
            out string error),
            error);

        // Failure path: 2 * 1.5 = 3 (not the success double).
        object result = action.Apply(EmptyDrops(), 2f, parameters, SkillSource());
        Assert.Equal(3f, (float)result, precision: 4);
    }

    [Fact]
    [Trait("Layer", "Action")]
    public void Chance_0Percent_NoOnFailure_LeavesValue()
    {
        (ChanceDropsQuantityAction action, _) = CreateQuantityChance();
        Assert.True(action.TryParseParams(
            JObject.Parse(
                """
                {
                  "chance": { "percent": 0 },
                  "onSuccess": {
                    "action": "prosequor:number",
                    "params": { "op": "scale", "value": 1 }
                  }
                }
                """),
            out object parameters,
            out string error),
            error);

        object result = action.Apply(EmptyDrops(), 2f, parameters, SkillSource());
        Assert.Equal(2f, (float)result, precision: 4);
    }

    [Fact]
    [Trait("Layer", "Action")]
    public void Chance_100Percent_NoOnSuccess_LeavesValue()
    {
        (ChanceDropsQuantityAction action, _) = CreateQuantityChance();
        Assert.True(action.TryParseParams(
            JObject.Parse("""{"chance": { "percent": 100 }}"""),
            out object parameters,
            out string error),
            error);

        object result = action.Apply(EmptyDrops(), 2.5f, parameters, SkillSource());
        Assert.Equal(2.5f, (float)result, precision: 4);
    }
}
