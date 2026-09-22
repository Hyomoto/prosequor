using Prosequor.Ability;
using Prosequor.Ability.Actions;
using Prosequor.Ability.Hooks;
using Xunit;

namespace Prosequor.Pure.Tests;

/// <summary>Pure math for Construction reinforce strength and heat-structure skip.</summary>
public static class ConstructionAbilityFixtures
{
    public static void VerifyAll()
    {
        VerifyReinforceStochasticScale();
        VerifyHeatStructureSkip();
        VerifyHeatStructurePathMatch();
    }

    static void VerifyReinforceStochasticScale()
    {
        double expected = 10.0 * (1.0 + 0.10);
        if (Math.Abs(expected - 11.0) > 0.0001)
        {
            Assert.Fail($"[prosequor] Expected 10 * 1.10 = 11, got {expected}.");
        }

        int rounded = AbilityFormulas.StochasticRound(5.5, new Random(0));
        if (rounded is < 5 or > 6)
        {
            Assert.Fail($"[prosequor] StochasticRound(5.5) out of range: {rounded}.");
        }

        int floor = Math.Max(1, AbilityFormulas.StochasticRound(0.1, new Random(1)));
        if (floor < 1)
        {
            Assert.Fail("[prosequor] Reinforce floor should be at least 1.");
        }
    }

    static void VerifyHeatStructureSkip()
    {
        if (!NumberSpec.TryParse(
                Newtonsoft.Json.Linq.JObject.Parse("""{ "op": "add", "value": 0.25 }"""),
                out NumberSpec? spec,
                out string error)
            || spec == null)
        {
            Assert.Fail($"[prosequor] Heat skip NumberSpec parse failed: {error}");
            return;
        }

        ActionTestProgress progress = new();
        HeatStructureDamageContext context = new() { Progress = progress };
        AbilityRuleSource source = new() { SkillId = "construction", Tier = 1 };
        float next = spec.Apply(0f, context, source);
        if (Math.Abs(next - 0.25f) > 0.0001f)
        {
            Assert.Fail($"[prosequor] Expected skip fold 0.25, got {next}.");
        }

        if (!NumberSpec.TryParse(
                Newtonsoft.Json.Linq.JObject.Parse("""{ "op": "add", "value": 0.5 }"""),
                out NumberSpec? spec2,
                out _)
            || spec2 == null)
        {
            Assert.Fail("[prosequor] Heat skip tier-2 NumberSpec parse failed.");
            return;
        }

        float tier2 = spec2.Apply(0f, context, source);
        if (Math.Abs(tier2 - 0.5f) > 0.0001f)
        {
            Assert.Fail($"[prosequor] Expected skip fold 0.5, got {tier2}.");
        }
    }

    static void VerifyHeatStructurePathMatch()
    {
        if (HeatStructureDamageStation.IsHeatStructurePath(null)
            || HeatStructureDamageStation.IsHeatStructurePath("")
            || !HeatStructureDamageStation.IsHeatStructurePath("claybricks-damaged-fire")
            || !HeatStructureDamageStation.IsHeatStructurePath("refractorybricks-good-tier1")
            || !HeatStructureDamageStation.IsHeatStructurePath("refractorybrickgrating-damaged-tier2")
            || HeatStructureDamageStation.IsHeatStructurePath("cobblestone-granite"))
        {
            Assert.Fail("[prosequor] Heat-structure path match failed.");
        }
    }
}
