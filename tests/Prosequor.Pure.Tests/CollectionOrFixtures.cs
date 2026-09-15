using Prosequor.Ability.Hooks;
using Xunit;

namespace Prosequor.Ability;

/// <summary>Inline collection OR (`&lt;a, b&gt;`) parse/match and named union materialization.</summary>
public static class CollectionOrFixtures
{
    public static void VerifyAll()
    {
        VerifyParseMultiCollection();
        VerifyMatchEitherCollection();
        VerifyBoundMembersMatchWithoutIndex();
        VerifyRejectUnknownAndEmpty();
        VerifyUnionMaterialization();
        VerifyExcludeSubtractsSnapshot();
        VerifySpecificityUnchanged();
        VerifyHeldNone();
    }

    static void VerifyParseMultiCollection()
    {
        CollectionIndex collections = NewKeys("clothing", "armor");
        if (!TagCriterionParser.TryParse(
                "last-craft:<clothing, armor>",
                collections,
                out TagCriterion? criterion,
                out string error)
            || criterion is not RoleCollectionCriterion role
            || role.Role != FactRole.LastCraft
            || role.CollectionIds.Count != 2
            || !string.Equals(role.CollectionIds[0], "clothing", StringComparison.OrdinalIgnoreCase)
            || !string.Equals(role.CollectionIds[1], "armor", StringComparison.OrdinalIgnoreCase))
        {
            Assert.Fail(string.Format(
                "[prosequor] Collection OR fixture failed (parse multi). error={0}",
                error));
        }

        if (!TagCriterionParser.TryParse(
                "<clothing, armor>",
                collections,
                out TagCriterion? bare,
                out _)
            || bare is not RoleCollectionCriterion bareRole
            || bareRole.Role != FactRole.Target
            || bareRole.CollectionIds.Count != 2)
        {
            Assert.Fail("[prosequor] Collection OR fixture failed (bare multi defaults to target).");
        }
    }

    static void VerifyBoundMembersMatchWithoutIndex()
    {
        CollectionIndex collections = NewKeys("clothing", "armor");
        collections.AddCode("clothing", "game:clothes-plain-shirt");
        collections.AddCode("armor", "game:armor-body-leather");

        if (!TagCriterionParser.TryParse(
                "last-craft:<clothing, armor>",
                collections,
                out TagCriterion? criterion,
                out _)
            || criterion is not RoleCollectionCriterion role)
        {
            Assert.Fail("[prosequor] Collection OR fixture failed (bind parse).");
            return;
        }

        role.Bind(collections);

        AbilityAction clothingFact = new()
        {
            Verb = "prosequor:craft",
            ActorUid = "fixture",
            LastCraft = "game:clothes-plain-shirt"
        };
        AbilityAction armorFact = new()
        {
            Verb = "prosequor:craft",
            ActorUid = "fixture",
            LastCraft = "game:armor-body-leather"
        };
        AbilityAction linenFact = new()
        {
            Verb = "prosequor:craft",
            ActorUid = "fixture",
            LastCraft = "game:linen-normal-down"
        };

        CollectionIndex empty = new();
        if (!criterion.Matches(clothingFact, empty)
            || !criterion.Matches(armorFact, empty)
            || criterion.Matches(linenFact, empty)
            || !criterion.Matches(clothingFact, null))
        {
            Assert.Fail("[prosequor] Collection OR fixture failed (bound members without index).");
        }
    }

    static void VerifyMatchEitherCollection()
    {
        CollectionIndex collections = NewKeys("clothing", "armor");
        collections.AddCode("clothing", "game:clothes-plain-shirt");
        collections.AddCode("armor", "game:armor-body-leather");

        if (!TagCriterionParser.TryParse(
                "last-craft:<clothing, armor>",
                collections,
                out TagCriterion? criterion,
                out _)
            || criterion == null)
        {
            Assert.Fail("[prosequor] Collection OR fixture failed (match parse).");
            return;
        }

        AbilityAction clothingFact = new()
        {
            Verb = "prosequor:craft",
            ActorUid = "fixture",
            LastCraft = "game:clothes-plain-shirt"
        };
        AbilityAction armorFact = new()
        {
            Verb = "prosequor:craft",
            ActorUid = "fixture",
            LastCraft = "game:armor-body-leather"
        };
        AbilityAction linenFact = new()
        {
            Verb = "prosequor:craft",
            ActorUid = "fixture",
            LastCraft = "game:linen-normal-down"
        };

        if (!criterion.Matches(clothingFact, collections)
            || !criterion.Matches(armorFact, collections)
            || criterion.Matches(linenFact, collections))
        {
            Assert.Fail("[prosequor] Collection OR fixture failed (either-collection match).");
        }
    }

    static void VerifyRejectUnknownAndEmpty()
    {
        CollectionIndex collections = NewKeys("clothing", "armor");

        if (TagCriterionParser.TryParse("last-craft:<clothing, nope>", collections, out _, out string unknownError)
            || !unknownError.Contains("unknown collection", StringComparison.OrdinalIgnoreCase))
        {
            Assert.Fail(string.Format(
                "[prosequor] Collection OR fixture failed (reject unknown). error={0}",
                unknownError));
        }

        if (TagCriterionParser.TryParse("last-craft:<clothing,>", collections, out _, out string emptyError)
            || !emptyError.Contains("empty collection", StringComparison.OrdinalIgnoreCase))
        {
            Assert.Fail(string.Format(
                "[prosequor] Collection OR fixture failed (reject empty slot). error={0}",
                emptyError));
        }

        if (TagCriterionParser.TryParse("op:<clothing, armor>", collections, out _, out string opError)
            || !opError.Contains("op role", StringComparison.OrdinalIgnoreCase))
        {
            Assert.Fail(string.Format(
                "[prosequor] Collection OR fixture failed (reject op collections). error={0}",
                opError));
        }
    }

    static void VerifyUnionMaterialization()
    {
        CollectionIndex collections = NewKeys("clothing", "armor", "wearable", "raft", "sailboat", "watercraft");
        collections.AddCode("clothing", "game:clothes-a");
        collections.AddCode("armor", "game:armor-b");
        collections.AddCode("raft", "game:raft-oak");
        collections.AddCode("sailboat", "game:sailboat-oak");
        collections.AddUnion("wearable", ["clothing", "armor"]);
        collections.AddUnion("watercraft", ["raft", "sailboat"]);

        List<string> warnings = new();
        collections.MaterializeUnions(warnings.Add);

        if (!collections.Contains("wearable", "game:clothes-a")
            || !collections.Contains("wearable", "game:armor-b")
            || !collections.Contains("watercraft", "game:raft-oak")
            || !collections.Contains("watercraft", "game:sailboat-oak")
            || warnings.Count != 0)
        {
            Assert.Fail(string.Format(
                "[prosequor] Collection OR fixture failed (union materialize). warnings={0}",
                warnings.Count));
        }

        // Union-of-unions: one more pass copies nested membership.
        collections.EnsureKey("gear");
        collections.AddUnion("gear", ["wearable"]);
        collections.MaterializeUnions();
        if (!collections.Contains("gear", "game:clothes-a")
            || !collections.Contains("gear", "game:armor-b"))
        {
            Assert.Fail("[prosequor] Collection OR fixture failed (union-of-unions).");
        }
    }

    static void VerifyExcludeSubtractsSnapshot()
    {
        CollectionIndex forward = NewKeys("raw", "named");
        forward.AddCode("raw", "game:meat-cooked");
        forward.AddCode("raw", "game:claypot-blue-cooked");
        forward.AddCode("named", "game:claypot-blue-cooked");
        forward.AddExclude("raw", ["named"]);
        forward.ApplyExcludes();
        if (forward.Contains("raw", "game:claypot-blue-cooked")
            || !forward.Contains("raw", "game:meat-cooked")
            || !forward.Contains("named", "game:claypot-blue-cooked"))
        {
            Assert.Fail("[prosequor] Collection exclude fixture failed (snapshot subtract).");
        }

        CollectionIndex reversed = NewKeys("left", "right");
        reversed.AddCode("left", "game:shared");
        reversed.AddCode("left", "game:left-only");
        reversed.AddCode("right", "game:shared");
        reversed.AddCode("right", "game:right-only");
        reversed.AddExclude("left", ["right"]);
        reversed.AddExclude("right", ["left"]);
        reversed.ApplyExcludes();
        if (reversed.Contains("left", "game:shared")
            || reversed.Contains("right", "game:shared")
            || !reversed.Contains("left", "game:left-only")
            || !reversed.Contains("right", "game:right-only"))
        {
            Assert.Fail("[prosequor] Collection exclude fixture failed (mutual excludes).");
        }

        CollectionIndex unioned = NewKeys("meal", "claypot", "cooked-food");
        unioned.AddCode("meal", "game:redmeat-cooked");
        unioned.AddCode("meal", "game:claypot-blue-cooked");
        unioned.AddCode("claypot", "game:claypot-blue-cooked");
        unioned.AddExclude("meal", ["claypot"]);
        unioned.AddUnion("cooked-food", ["meal"]);
        unioned.ApplyExcludes();
        unioned.MaterializeUnions();
        if (unioned.Contains("cooked-food", "game:claypot-blue-cooked")
            || !unioned.Contains("cooked-food", "game:redmeat-cooked")
            || unioned.Contains("meal", "game:claypot-blue-cooked"))
        {
            Assert.Fail("[prosequor] Collection exclude fixture failed (union after exclude).");
        }

        CollectionIndex unknown = NewKeys("raw");
        unknown.AddCode("raw", "game:meat-cooked");
        unknown.AddExclude("raw", ["missing"]);
        List<string> unknownWarnings = new();
        unknown.ApplyExcludes(unknownWarnings.Add);
        if (unknownWarnings.Count != 1
            || !unknownWarnings[0].Contains("unknown exclude", StringComparison.OrdinalIgnoreCase)
            || !unknown.Contains("raw", "game:meat-cooked"))
        {
            Assert.Fail("[prosequor] Collection exclude fixture failed (unknown exclude).");
        }

        CollectionIndex unionOnly = NewKeys("raw", "gear", "clothing");
        unionOnly.AddCode("raw", "game:meat-cooked");
        unionOnly.AddCode("clothing", "game:clothes-a");
        unionOnly.AddUnion("gear", ["clothing"]);
        unionOnly.AddExclude("raw", ["gear"]);
        List<string> unionOnlyWarnings = new();
        unionOnly.ApplyExcludes(unionOnlyWarnings.Add);
        if (unionOnlyWarnings.Count != 1
            || !unionOnlyWarnings[0].Contains("no positive membership", StringComparison.OrdinalIgnoreCase)
            || !unionOnly.Contains("raw", "game:meat-cooked"))
        {
            Assert.Fail("[prosequor] Collection exclude fixture failed (union-only exclude).");
        }
    }

    static void VerifySpecificityUnchanged()
    {
        CollectionIndex collections = NewKeys("clothing", "armor");
        if (!TagCriterionParser.TryParse("last-craft:<clothing, armor>", collections, out TagCriterion? multi, out _)
            || !TagCriterionParser.TryParse("last-craft:<clothing>", collections, out TagCriterion? single, out _)
            || multi == null
            || single == null
            || multi.SpecificityScore != 1_000
            || single.SpecificityScore != 1_000)
        {
            Assert.Fail("[prosequor] Collection OR fixture failed (specificity stays 1000).");
        }
    }

    static void VerifyHeldNone()
    {
        CollectionIndex collections = NewKeys();
        if (!TagCriterionParser.TryParse("held:none", collections, out TagCriterion? emptyHands, out string parseError)
            || emptyHands is not RoleIdentityCriterion identity
            || !RoleIdentityCriterion.IsNoneIdentity(identity.Identity)
            || identity.SpecificityScore != 1_000_000)
        {
            Assert.Fail(string.Format(
                "[prosequor] held:none fixture failed (parse). error={0}",
                parseError));
        }

        AbilityAction bare = new() { Verb = "prosequor:block-break", ActorUid = "fixture", Held = null };
        AbilityAction blank = new() { Verb = "prosequor:block-break", ActorUid = "fixture", Held = "  " };
        AbilityAction shovel = new()
        {
            Verb = "prosequor:block-break",
            ActorUid = "fixture",
            Held = "game:shovel-copper"
        };
        if (!emptyHands!.Matches(bare, collections)
            || !emptyHands.Matches(blank, collections)
            || emptyHands.Matches(shovel, collections))
        {
            Assert.Fail("[prosequor] held:none fixture failed (match empty vs tool).");
        }

        if (TagCriterionParser.TryParse("op:none", collections, out _, out string opError)
            || string.IsNullOrWhiteSpace(opError)
            || TagCriterionParser.TryParse("damage:none", collections, out _, out string damageError)
            || string.IsNullOrWhiteSpace(damageError))
        {
            Assert.Fail("[prosequor] held:none fixture failed (op/damage must reject none).");
        }

        if (!TagCriterionParser.TryParse("input:none", collections, out TagCriterion? noInputs, out _)
            || noInputs == null)
        {
            Assert.Fail("[prosequor] held:none fixture failed (input:none parse).");
        }

        AbilityAction emptyInputs = new()
        {
            Verb = "prosequor:craft",
            ActorUid = "fixture",
            Inputs = Array.Empty<string>()
        };
        AbilityAction withInput = new()
        {
            Verb = "prosequor:craft",
            ActorUid = "fixture",
            Inputs = ["game:flaxtwine"]
        };
        if (!noInputs.Matches(emptyInputs, collections) || noInputs.Matches(withInput, collections))
        {
            Assert.Fail("[prosequor] held:none fixture failed (input:none match).");
        }
    }

    static CollectionIndex NewKeys(params string[] keys)
    {
        CollectionIndex collections = new();
        foreach (string key in keys)
        {
            collections.EnsureKey(key);
        }

        return collections;
    }
}
