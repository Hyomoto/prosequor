using Newtonsoft.Json.Linq;
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
        VerifyExtraTraitFoldMath();
        VerifyEmptyStatCatalogFallsBackForMutate();
        VerifyClassProfileScores();
        VerifyClassProfileSkills();
        VerifyClassProfileTraits();
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

    static void VerifyExtraTraitFoldMath()
    {
        TraitAttributeRegistry registry = BuildShippedRegistry();
        HashSet<string> fresh = TraitAttributeConverter.NewExtraTraitCodes(
            ["soldier", "focused"],
            ["focused"]);
        if (fresh.Count != 1 || !fresh.Contains("soldier"))
        {
            Assert.Fail(string.Format(
                "[prosequor] New extra-trait codes expected [soldier], got [{0}].",
                string.Join(", ", fresh)));
        }

        Dictionary<string, int> hunter = TraitAttributeConverter.ResolveScores(
            registry,
            ["focused", "resourceful", "fleetfooted", "bowyer", "farsighted", "claustrophobic"]);
        Dictionary<string, int> withSoldier = TraitAttributeConverter.WithExtraTraits(
            hunter,
            registry,
            ["soldier"]);
        if (withSoldier[AttributeIds.Strength] != 11 || withSoldier[AttributeIds.Perception] != 13)
        {
            Assert.Fail(string.Format(
                "[prosequor] Hunter + soldier extras expected STR/PER 11/13, got {0}/{1}.",
                withSoldier[AttributeIds.Strength],
                withSoldier[AttributeIds.Perception]));
        }
    }

    static void VerifyEmptyStatCatalogFallsBackForMutate()
    {
        if (!ReferenceEquals(AttributeIds.CatalogIds(new AttributeStatRegistry()), AttributeIds.All))
        {
            Assert.Fail("[prosequor] Empty attribute-stat registry must fall back to AttributeIds.All.");
        }

        TraitAttributeRegistry registry = BuildShippedRegistry();
        CharacterSystem fake = new();
        fake.characterClasses.Add(new CharacterClass
        {
            Code = "hunter",
            Traits = ["focused", "resourceful", "fleetfooted", "bowyer", "farsighted", "claustrophobic"]
        });

        TraitAttributeConverter.MutateLoadedClasses(fake, registry, new AttributeStatRegistry());
        if (!registry.ClassStartingScores.TryGetValue("hunter", out Dictionary<string, int>? scores)
            || scores[AttributeIds.Perception] != 13
            || scores[AttributeIds.Strength] != 9)
        {
            Assert.Fail("[prosequor] Mutate with unloaded attribute stats must still cache hunter trait scores.");
        }
    }


    static void VerifyClassProfileScores()
    {
        TraitAttributeRegistry registry = BuildShippedRegistry();
        List<string> warnings = new();
        ClassProfile? profile = ClassProfile.Compile(
            "scout",
            new ClassProfileJson
            {
                attributes = new Dictionary<string, int>
                {
                    ["strength"] = 12,
                    ["nope"] = 3
                }
            },
            AttributeIds.All,
            skills: null,
            warnings.Add);
        if (profile == null
            || profile.Attributes[AttributeIds.Strength] != 12
            || profile.Attributes[AttributeIds.Perception] != AttributeGrowth.DefaultScore
            || profile.Attributes.ContainsKey("nope")
            || warnings.Count != 1)
        {
            Assert.Fail(string.Format(
                "[prosequor] Class profile expected strength 12, other attributes 10, and one unknown-attribute warning. Warnings: {0}.",
                string.Join("; ", warnings)));
        }

        string[] hunterTraits =
        [
            "focused", "resourceful", "fleetfooted", "bowyer", "farsighted", "claustrophobic"
        ];
        Dictionary<string, int> plain = TraitAttributeConverter.ResolveClassScores(registry, "hunter", hunterTraits);
        Dictionary<string, int> classic = TraitAttributeConverter.ResolveScores(registry, hunterTraits);
        if (plain[AttributeIds.Strength] != classic[AttributeIds.Strength]
            || plain[AttributeIds.Perception] != classic[AttributeIds.Perception]
            || plain[AttributeIds.Strength] != 9
            || plain[AttributeIds.Perception] != 13)
        {
            Assert.Fail("[prosequor] A class with no profile must match trait-only starting scores.");
        }

        registry.RegisterClassProfile(profile);
        CharacterSystem fake = new();
        fake.characterClasses.Add(new CharacterClass
        {
            Code = "scout",
            Traits = ["soldier"]
        });
        fake.characterClasses.Add(new CharacterClass
        {
            Code = "hunter",
            Traits = hunterTraits
        });
        TraitAttributeConverter.MutateLoadedClasses(fake, registry);

        if (!registry.ClassStartingScores.TryGetValue("scout", out Dictionary<string, int>? scout)
            || scout[AttributeIds.Strength] != 14
            || scout[AttributeIds.Perception] != AttributeGrowth.DefaultScore)
        {
            Assert.Fail(string.Format(
                "[prosequor] Scout strength 12 plus soldier +2 expected STR/PER 14/10, got {0}/{1}.",
                scout?[AttributeIds.Strength],
                scout?[AttributeIds.Perception]));
        }

        if (!registry.ClassStartingScores.TryGetValue("hunter", out Dictionary<string, int>? hunter)
            || hunter[AttributeIds.Strength] != 9
            || hunter[AttributeIds.Perception] != 13)
        {
            Assert.Fail("[prosequor] Hunter without a class profile must keep trait-only scores after mutate.");
        }
    }

    static void VerifyClassProfileSkills()
    {
        SkillRegistry skills = new();
        skills.Register(new SkillDef { Id = "always" });
        skills.Register(new SkillDef { Id = "gated", IsOptional = true });

        JObject raw = JObject.Parse("""
            {
              "attributes": {},
              "skills": ["gated", "always", "no-such-skill", " "],
              "unlocks": [
                { "skill": "prosequor:hunting", "nodes": ["tracker", ""], "level": 4 }
              ]
            }
            """);
        ClassProfileJson? row = ClassProfile.ReadJson(raw, out string? error);
        List<string> warnings = new();
        ClassProfile? profile = ClassProfile.Compile("scout", row, AttributeIds.All, skills, warnings.Add);
        if (error != null
            || profile == null
            || profile.Skills.Count != 2
            || !profile.Skills.Contains("gated")
            || !profile.Skills.Contains("always")
            || profile.Unlocks.Count != 1
            || profile.Unlocks[0].Skill != "prosequor:hunting"
            || profile.Unlocks[0].Nodes.Count != 1
            || profile.Unlocks[0].Nodes[0] != "tracker")
        {
            Assert.Fail(string.Format(
                "[prosequor] Class profile JSON expected gated+always and unlock tracker. Error={0}. Skills=[{1}].",
                error,
                profile == null ? "" : string.Join(", ", profile.Skills)));
        }

        foreach (string id in AttributeIds.All)
        {
            if (profile.Attributes[id] != AttributeGrowth.DefaultScore)
            {
                Assert.Fail(string.Format(
                    "[prosequor] Empty class attributes must stay {0} for {1}, got {2}.",
                    AttributeGrowth.DefaultScore,
                    id,
                    profile.Attributes[id]));
            }
        }

        TraitAttributeRegistry traits = new();
        traits.RegisterClassProfile(profile);
        traits.SetClassOriginalTraits("scout", Array.Empty<string>());
        traits.RebuildClassSkillSets(skills);
        IReadOnlySet<string> set = traits.SkillSetForClass("scout");
        if (!set.Contains("always")
            || !set.Contains("gated")
            || set.Contains("no-such-skill")
            || ReferenceEquals(set, traits.BaseSkillSet))
        {
            Assert.Fail("[prosequor] Class skills must add known optional ids and skip unknown ones.");
        }

        ClassProfile? baseOnly = ClassProfile.Compile(
            "commoner",
            new ClassProfileJson { skills = ["always"] },
            AttributeIds.All,
            skills,
            warn: null);
        traits.RegisterClassProfile(baseOnly!);
        traits.SetClassOriginalTraits("commoner", Array.Empty<string>());
        traits.RebuildClassSkillSets(skills);
        if (!ReferenceEquals(traits.SkillSetForClass("commoner"), traits.BaseSkillSet))
        {
            Assert.Fail("[prosequor] Class skills already in the base must keep the shared base reference.");
        }
    }

    static void VerifyClassProfileTraits()
    {
        List<string> warnings = new();
        ClassProfile? profile = ClassProfile.Compile(
            "hunter",
            new ClassProfileJson
            {
                traits = ["Soldier", "-Claustrophobic", "-bowyer", "tinkerer", "focused", "-", " ", "ghost"]
            },
            AttributeIds.All,
            skills: null,
            warnings.Add);
        if (profile == null
            || profile.TraitEdits.Count != 6
            || profile.TraitEdits[0].Remove
            || profile.TraitEdits[0].Code != "Soldier"
            || !profile.TraitEdits[1].Remove
            || profile.TraitEdits[1].Code != "Claustrophobic"
            || warnings.Count != 2)
        {
            Assert.Fail(string.Format(
                "[prosequor] Trait edits expected 6 ops and 2 blank warnings, got {0} ops and [{1}].",
                profile?.TraitEdits.Count,
                string.Join("; ", warnings)));
        }

        string[] hunterTraits =
        [
            "focused", "resourceful", "fleetfooted", "bowyer", "farsighted", "claustrophobic"
        ];
        string[] withoutThenWith = profile.ApplyTraitEdits(
            hunterTraits,
            code => code == "ghost" ? null : code,
            warnings.Add);
        if (withoutThenWith.Count(code => string.Equals(code, "soldier", StringComparison.OrdinalIgnoreCase)) != 1
            || withoutThenWith.Contains("claustrophobic", StringComparer.OrdinalIgnoreCase)
            || withoutThenWith.Contains("bowyer", StringComparer.OrdinalIgnoreCase)
            || withoutThenWith.Contains("ghost", StringComparer.OrdinalIgnoreCase)
            || !withoutThenWith.Contains("tinkerer"))
        {
            Assert.Fail(string.Format(
                "[prosequor] Trait edits expected soldier and tinkerer, without claustrophobic, bowyer, or ghost. Got [{0}].",
                string.Join(", ", withoutThenWith)));
        }

        ClassProfile? removeAfterAdd = ClassProfile.Compile(
            "hunter",
            new ClassProfileJson { traits = ["soldier", "-soldier"] },
            AttributeIds.All,
            skills: null,
            warn: null);
        string[] removed = removeAfterAdd!.ApplyTraitEdits(hunterTraits, resolveAdd: null, warn: null);
        if (removed.Contains("soldier", StringComparer.OrdinalIgnoreCase))
        {
            Assert.Fail("[prosequor] A later -soldier must remove an added soldier.");
        }

        TraitAttributeRegistry registry = BuildShippedRegistry();
        registry.RegisterClassProfile(profile);
        CharacterSystem fake = new();
        fake.TraitsByCode["soldier"] = new Trait { Code = "soldier" };
        fake.TraitsByCode["tinkerer"] = new Trait { Code = "tinkerer" };
        fake.characterClasses.Add(new CharacterClass
        {
            Code = "hunter",
            Traits = hunterTraits
        });
        TraitAttributeConverter.MutateLoadedClasses(fake, registry);

        if (!registry.ClassStartingScores.TryGetValue("hunter", out Dictionary<string, int>? scores)
            || scores[AttributeIds.Strength] != 11
            || scores[AttributeIds.Perception] != 13
            || scores[AttributeIds.Resilience] != 10)
        {
            Assert.Fail(string.Format(
                "[prosequor] Hunter trait edits expected STR/PER/RES 11/13/10, got {0}/{1}/{2}.",
                scores?[AttributeIds.Strength],
                scores?[AttributeIds.Perception],
                scores?[AttributeIds.Resilience]));
        }

        string[] leftover = fake.characterClasses[0].Traits ?? Array.Empty<string>();
        if (leftover.Length != 1 || leftover[0] != "tinkerer")
        {
            Assert.Fail(string.Format(
                "[prosequor] Hunter leftover traits expected [tinkerer], got [{0}].",
                string.Join(", ", leftover)));
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
