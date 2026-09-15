using Prosequor.Ability;
using Prosequor.Data;
using Vintagestory.API.Common;
using Vintagestory.GameContent;
using Xunit;

namespace Prosequor.Progress;

/// <summary>xUnit checks for trait→attribute inference and class strip.</summary>
public static class TraitAttributeFixtures
{
    public static void VerifyAll()
    {
        VerifyResolveScoresMath();
        VerifyVanillaClassTotals();
        VerifyClassTraitStrip();
    }

    static void VerifyResolveScoresMath()
    {
        TraitAttributeRegistry registry = BuildShippedRegistry();
        Dictionary<string, int> empty = TraitAttributeConverter.ResolveScores(registry, Array.Empty<string>());
        foreach (string id in AttributeIds.All)
        {
            if (empty[id] != AttributeGrowth.DefaultScore)
            {
                Assert.Fail(string.Format("[prosequor] Trait-attribute empty resolve expected {0}={1}, got {2}.",
                    id,
                    AttributeGrowth.DefaultScore,
                    empty[id]));
            }
        }

        Dictionary<string, int> soldier = TraitAttributeConverter.ResolveScores(registry, ["soldier"]);
        if (soldier[AttributeIds.Strength] != 12)
        {
            Assert.Fail(string.Format("[prosequor] Trait-attribute soldier resolve expected strength 12, got {0}.",
                soldier[AttributeIds.Strength]));
        }
    }

    static void VerifyVanillaClassTotals()
    {
        TraitAttributeRegistry registry = BuildShippedRegistry();
        // Totals match shipped trait-attributes.json (claustrophobic → resilience −1).
        (string Class, string[] Traits, int Str, int Per, int Con, int Inc, int Res)[] expected =
        [
            ("commoner", [], 10, 10, 10, 10, 10),
            ("hunter", ["focused", "resourceful", "fleetfooted", "bowyer", "farsighted", "claustrophobic"], 9, 13, 11, 10, 9),
            ("malefactor", ["forager", "pilferer", "furtive", "improviser", "frail", "nervous"], 9, 11, 8, 14, 10),
            ("clockmaker", ["precise", "technical", "fleetfooted", "frail", "nervous", "tinkerer"], 9, 11, 9, 10, 11),
            ("blackguard", ["soldier", "hardy", "merciless", "ravenous", "nearsighted", "heavyhanded"], 12, 7, 10, 10, 10),
            ("tailor", ["clothier", "mender", "civil", "weak", "kind"], 8, 8, 10, 10, 11)
        ];

        foreach ((string cls, string[] traits, int str, int per, int con, int inc, int res) in expected)
        {
            Dictionary<string, int> scores = TraitAttributeConverter.ResolveScores(registry, traits);
            if (scores[AttributeIds.Strength] != str
                || scores[AttributeIds.Perception] != per
                || scores[AttributeIds.Constitution] != con
                || scores[AttributeIds.Inconspicuity] != inc
                || scores[AttributeIds.Resilience] != res)
            {
                Assert.Fail(string.Format("[prosequor] Trait-attribute class '{0}' expected STR/PER/CON/INC/RES {1}/{2}/{3}/{4}/{5}, got {6}/{7}/{8}/{9}/{10}.",
                    cls,
                    str,
                    per,
                    con,
                    inc,
                    res,
                    scores[AttributeIds.Strength],
                    scores[AttributeIds.Perception],
                    scores[AttributeIds.Constitution],
                    scores[AttributeIds.Inconspicuity],
                    scores[AttributeIds.Resilience]));
            }
        }
    }

    static void VerifyClassTraitStrip()
    {
        TraitAttributeRegistry registry = BuildShippedRegistry();
        CharacterSystem fake = new();
        fake.TraitsByCode["technical"] = new Trait
        {
            Code = "technical",
            Attributes = new Dictionary<string, double> { ["temporalGearTLRepairCost"] = -1 }
        };
        fake.characterClasses.Add(new CharacterClass
        {
            Code = "hunter",
            Traits = ["focused", "resourceful", "fleetfooted", "bowyer", "farsighted", "claustrophobic"]
        });
        fake.characterClasses.Add(new CharacterClass
        {
            Code = "clockmaker",
            Traits = ["precise", "technical", "fleetfooted", "frail", "nervous", "tinkerer"]
        });

        TraitAttributeConverter.MutateLoadedClasses(fake, registry);

        CharacterClass hunter = fake.characterClasses.First(c => c.Code == "hunter");
        if (hunter.Traits is not ["bowyer"])
        {
            Assert.Fail(string.Format("[prosequor] Hunter leftover traits expected [bowyer], got [{0}].",
                string.Join(", ", hunter.Traits ?? Array.Empty<string>())));
        }

        CharacterClass clockmaker = fake.characterClasses.First(c => c.Code == "clockmaker");
        string[] clockLeftover = clockmaker.Traits ?? Array.Empty<string>();
        if (clockLeftover.Length != 2
            || !clockLeftover.Contains("technical", StringComparer.OrdinalIgnoreCase)
            || !clockLeftover.Contains("tinkerer", StringComparer.OrdinalIgnoreCase))
        {
            Assert.Fail(string.Format("[prosequor] Clockmaker leftover traits expected [technical, tinkerer], got [{0}].",
                string.Join(", ", clockLeftover)));
        }

        if (!registry.ClassStartingScores.TryGetValue("hunter", out Dictionary<string, int>? hunterScores)
            || hunterScores[AttributeIds.Strength] != 9
            || hunterScores[AttributeIds.Perception] != 13)
        {
            Assert.Fail("[prosequor] Hunter ClassStartingScores cache missing or wrong after strip.");
        }

        if (fake.TraitsByCode["technical"].Attributes is not { Count: 0 })
        {
            Assert.Fail("[prosequor] retainTrait technical should have cleared Entity.Stats attributes.");
        }

        Dictionary<string, int> leftover = TraitAttributeConverter.ResolveScores(registry, hunter.Traits);
        foreach (string id in AttributeIds.All)
        {
            if (leftover[id] != AttributeGrowth.DefaultScore)
            {
                Assert.Fail(string.Format(
                    "[prosequor] Stripped hunter leftovers must resolve as default {0} (mid-save must use ClassStartingScores), {1}={2}.",
                    AttributeGrowth.DefaultScore,
                    id,
                    leftover[id]));
            }
        }

        if (hunterScores[AttributeIds.Perception] != 13)
        {
            Assert.Fail("[prosequor] Mid-save class profile must read hunter perception 13 from cache, not leftover traits.");
        }
    }

    static TraitAttributeRegistry BuildShippedRegistry()
    {
        TraitAttributeRegistry registry = new();
        void Add(string code, bool retainTrait = false, params (string Attr, int Delta)[] deltas)
        {
            Dictionary<string, int> map = new(StringComparer.OrdinalIgnoreCase);
            foreach ((string attr, int delta) in deltas)
            {
                map[attr] = delta;
            }

            registry.Register(new TraitAttributeMapping
            {
                Code = code,
                Attributes = map,
                RetainTrait = retainTrait
            });
        }

        Add("focused", false, ("perception", 2));
        Add("resourceful", false, ("perception", 1));
        Add("fleetfooted", false, ("constitution", 1));
        Add("bowyer");
        Add("forager", false, ("perception", 1));
        Add("pilferer", false, ("inconspicuity", 2));
        Add("furtive", false, ("inconspicuity", 2));
        Add("precise", false, ("perception", 1));
        Add("technical", true, ("resilience", 1));
        Add("soldier", false, ("strength", 2));
        Add("hardy", false, ("constitution", 2));
        Add("clothier");
        Add("mender", false, ("resilience", 1));
        Add("merciless");
        Add("farsighted", false, ("strength", -1));
        Add("claustrophobic", false, ("resilience", -1));
        Add("frail", false, ("constitution", -2));
        Add("nervous", false, ("strength", -1));
        Add("ravenous", false, ("constitution", -2));
        Add("nearsighted", false, ("perception", -2));
        Add("heavyhanded", false, ("perception", -1));
        Add("kind", false, ("perception", -1));
        Add("weak", false, ("strength", -2));
        Add("civil", false, ("perception", -1));
        Add("improviser");
        Add("tinkerer");
        return registry;
    }
}
