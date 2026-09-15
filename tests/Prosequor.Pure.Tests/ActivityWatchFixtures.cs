using Prosequor.Ability.Hooks;
using Prosequor.Xp;
using Prosequor.Xp.Activity;
using Xunit;

namespace Prosequor.Ability;

/// <summary>Unified skill-embedded XP rule activity match fixtures (amount + rate paths).</summary>
public static class ActivityWatchFixtures
{
    public static void VerifyAll()
    {
        VerifyActivityNormalization();
        VerifyMatchAndNarrowing();
        VerifySameSkillWinner();
        VerifyFishSizeAmountWinner();
        VerifyRateTimesDt();
        VerifyAmountVsRateFlags();
        VerifyMatchScoreParity();
    }

    static void VerifyActivityNormalization()
    {
        if (XpRuleRegistry.NormalizeActivity("running") != "game:running"
            || XpRuleRegistry.NormalizeActivity("game:digging") != "game:digging"
            || XpRuleRegistry.NormalizeActivity("prosequor:dig") != "prosequor:dig"
            || XpRuleRegistry.NormalizeActivity("mymod:threadmaking") != "mymod:threadmaking")
        {
            Assert.Fail("[prosequor] XP rule fixture failed (activity normalization).");
        }
    }

    static void VerifyMatchAndNarrowing()
    {
        CollectionIndex collections = DiggingCollections();
        XpMatchFact digging = Fact("game:digging", target: "game:soil-low-none");
        XpRule activityOnly = RateRule("a", "game:digging", skill: "digging", rate: 0.01f, order: 1);
        XpRule wrongActivity = RateRule("b", "game:running", skill: "digging", rate: 0.01f, order: 2);
        XpRule needsSoil = RateRule(
            "c",
            "game:digging",
            skill: "digging",
            rate: 0.02f,
            order: 3,
            collections,
            tags: ["target:<soil>"]);
        XpRule needsStone = RateRule(
            "d",
            "game:digging",
            skill: "digging",
            rate: 0.03f,
            order: 4,
            collections,
            tags: ["target:<stone>"]);
        XpRule needsShovel = RateRule(
            "e",
            "game:digging",
            skill: "digging",
            rate: 0.01f,
            order: 5,
            collections,
            tags: ["caller:<shovel>"]);

        if (!XpRuleMatcher.Matches(activityOnly, digging, collections)
            || XpRuleMatcher.Matches(wrongActivity, digging, collections)
            || !XpRuleMatcher.Matches(needsSoil, digging, collections)
            || XpRuleMatcher.Matches(needsStone, digging, collections)
            || XpRuleMatcher.Matches(needsShovel, digging, collections))
        {
            Assert.Fail("[prosequor] XP rule fixture failed (match / narrowing).");
        }

        XpMatchFact withShovel = Fact(
            "game:digging",
            caller: "game:shovel-copper",
            target: "game:soil-low-none");
        if (!XpRuleMatcher.Matches(needsShovel, withShovel, collections))
        {
            Assert.Fail("[prosequor] XP rule fixture failed (caller tag match).");
        }
    }

    static void VerifySameSkillWinner()
    {
        CollectionIndex collections = DiggingCollections();
        XpMatchFact fact = Fact("game:digging", target: "game:soil-low-none");
        XpRule broad = RateRule("broad", "game:digging", "digging", 0.01f, order: 1);
        XpRule narrow = RateRule(
            "narrow",
            "game:digging",
            "digging",
            0.02f,
            order: 2,
            collections,
            tags: ["target:<soil>"]);

        XpRule? winner = XpRuleMatcher.PickWinner(new[] { broad, narrow }, fact, collections);
        if (winner?.Id != "narrow")
        {
            Assert.Fail(string.Format("[prosequor] XP rule fixture failed (same-skill specificity), winner={0}.",
                winner?.Id ?? "null"));
        }
    }

    static void VerifyFishSizeAmountWinner()
    {
        CollectionIndex collections = new();
        collections.EnsureKey("small-fish");
        collections.EnsureKey("medium-fish");
        collections.EnsureKey("large-fish");
        collections.EnsureKey("freshwater");
        collections.EnsureKey("fishingpole");
        collections.AddCode("medium-fish", "game:fish-perch");
        collections.AddCode("freshwater", "game:fish-perch");
        collections.AddCode("fishingpole", "game:fishingrod-crude");

        XpMatchFact medium = Fact(
            Deed.Activity,
            caller: "game:fishingrod-crude",
            target: "game:fish-perch",
            tokens: [DeedTokenTags.FishingCatch]);
        XpRule small = AmountRule(
            "fish-small",
            Deed.Activity,
            "fishing",
            1f,
            order: 1,
            collections,
            tags: ["fishing-catch", "caller:<fishingpole>", "target:<small-fish>"]);
        XpRule med = AmountRule(
            "fish-medium",
            Deed.Activity,
            "fishing",
            2f,
            order: 2,
            collections,
            tags: ["fishing-catch", "caller:<fishingpole>", "target:<medium-fish>"]);
        XpRule large = AmountRule(
            "fish-large",
            Deed.Activity,
            "fishing",
            4f,
            order: 3,
            collections,
            tags: ["fishing-catch", "caller:<fishingpole>", "target:<large-fish>"]);

        XpRule? winner = XpRuleMatcher.PickWinner(new[] { small, med, large }, medium, collections);
        if (winner?.Id != "fish-medium")
        {
            Assert.Fail(string.Format("[prosequor] XP rule fixture failed (fish size amount winner), winner={0}.",
                winner?.Id ?? "null"));
        }
    }

    static void VerifyRateTimesDt()
    {
        float rate = 0.01f;
        float dt = 10f;
        float raw = rate * dt;
        if (Math.Abs(raw - 0.1f) > 0.0001f)
        {
            Assert.Fail("[prosequor] XP rule fixture failed (rate * dt).");
        }
    }

    static void VerifyAmountVsRateFlags()
    {
        XpRule amount = new()
        {
            Id = "amt",
            Activity = "prosequor:dig",
            SkillId = "digging",
            Amount = 1f,
            Rate = 0f,
            SourceOrder = 1
        };
        XpRule rate = new()
        {
            Id = "rate",
            Activity = "game:digging",
            SkillId = "digging",
            Amount = 0f,
            Rate = 0.01f,
            SourceOrder = 2
        };
        if (!amount.IsAmountRule || amount.IsRateRule || !rate.IsRateRule || rate.IsAmountRule)
        {
            Assert.Fail("[prosequor] XP rule fixture failed (amount/rate flags).");
        }
    }

    static void VerifyMatchScoreParity()
    {
        CollectionIndex collections = DiggingCollections();
        XpRule tagged = RateRule(
            "score-tag",
            "game:digging",
            "digging",
            0.01f,
            order: 3,
            collections,
            tags: ["target:<soil>"]);
        long expected = XpRuleMatcher.Score(tagged.Criteria, tagged.Priority, tagged.SourceOrder);
        if (tagged.MatchScore != expected)
        {
            Assert.Fail(string.Format("[prosequor] XP rule fixture failed (MatchScore parity), got {0}, expected {1}.",
                tagged.MatchScore,
                expected));
        }
    }

    static CollectionIndex DiggingCollections()
    {
        CollectionIndex collections = new();
        collections.EnsureKey("soil");
        collections.EnsureKey("stone");
        collections.EnsureKey("shovel");
        collections.AddCode("soil", "game:soil-low-none");
        collections.AddCode("shovel", "game:shovel-copper");
        return collections;
    }

    static XpMatchFact Fact(
        string activity,
        string? caller = null,
        string? target = null,
        IEnumerable<string>? tokens = null) =>
        new()
        {
            Activity = activity,
            Caller = caller,
            Target = target,
            Tokens = tokens == null
                ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                : new HashSet<string>(tokens, StringComparer.OrdinalIgnoreCase)
        };

    static XpRule RateRule(
        string id,
        string activity,
        string skill,
        float rate,
        int order,
        CollectionIndex? collections = null,
        string[]? tags = null)
    {
        List<TagCriterion> criteria = ParseCriteria(collections ?? new CollectionIndex(), tags);
        return new()
        {
            Id = id,
            Activity = activity,
            SkillId = skill,
            Amount = 0f,
            Rate = rate,
            Criteria = criteria,
            Priority = 0,
            SourceOrder = order,
            MatchScore = XpRuleMatcher.Score(criteria, 0, order)
        };
    }

    static XpRule AmountRule(
        string id,
        string activity,
        string skill,
        float amount,
        int order,
        CollectionIndex collections,
        string[]? tags = null)
    {
        List<TagCriterion> criteria = ParseCriteria(collections, tags);
        return new()
        {
            Id = id,
            Activity = activity,
            SkillId = skill,
            Amount = amount,
            Rate = 0f,
            Pay = XpPayChannel.Flat,
            Criteria = criteria,
            Priority = 0,
            SourceOrder = order,
            MatchScore = XpRuleMatcher.Score(criteria, 0, order)
        };
    }

    static List<TagCriterion> ParseCriteria(CollectionIndex collections, string[]? tags)
    {
        List<TagCriterion> criteria = new();
        foreach (string? raw in tags ?? Array.Empty<string>())
        {
            if (!TagCriterionParser.TryParse(raw, collections, out TagCriterion? criterion, out _)
                || criterion == null)
            {
                continue;
            }

            criteria.Add(criterion);
        }

        return criteria;
    }
}
