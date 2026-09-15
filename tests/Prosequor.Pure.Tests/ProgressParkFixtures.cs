using Prosequor.Data;
using Prosequor.Player;
using Prosequor.Xp;
using Xunit;

namespace Prosequor.Ability;

/// <summary>UID park + ModData hydrate fixtures (no world required).</summary>
public static class ProgressParkFixtures
{
    public static void VerifyAll()
    {
        VerifyParkAndRead();
        VerifyEvict();
        VerifyCaseInsensitiveUid();
        VerifyMutationsThrow();
        VerifyHydrateRoundTrip();
        VerifyUnlockIdRemapDoesNotChain();
        VerifyHydrateRejectsEmpty();
        VerifyMissingSkillBarIsZero();
        VerifySkillBarCompleteAtKindCap();
    }

    static void VerifyParkAndRead()
    {
        ProgressPark park = new();
        PlayerProgressState state = SeedState();
        park.ParkStored("abc", state, ruleIndex: null);
        if (park.Count != 1
            || park.TryGet("abc") is not ParkedPlayerProgress parked
            || parked.GetSkillLevel("husbandry") != 12
            || !parked.HasUnlock("husbandry", "gentle-spirit")
            || parked.GetUnlockTier("husbandry", "gentle-spirit") != 2
            || parked.PlayerLevel != 4)
        {
            Assert.Fail("[prosequor] ProgressPark fixture failed (park/read).");
        }
    }

    static void VerifyEvict()
    {
        ProgressPark park = new();
        park.ParkStored("abc", SeedState(), ruleIndex: null);
        park.Evict("abc");
        if (park.Count != 0 || park.TryGet("abc") != null)
        {
            Assert.Fail("[prosequor] ProgressPark fixture failed (evict).");
        }
    }

    static void VerifyCaseInsensitiveUid()
    {
        ProgressPark park = new();
        park.ParkStored("AbC", SeedState(), ruleIndex: null);
        if (park.TryGet("abc") == null || park.TryGet("ABC") == null)
        {
            Assert.Fail("[prosequor] ProgressPark fixture failed (uid case).");
        }

        park.Evict("aBc");
        if (park.Count != 0)
        {
            Assert.Fail("[prosequor] ProgressPark fixture failed (evict case).");
        }
    }

    static void VerifyMutationsThrow()
    {
        ParkedPlayerProgress parked = new(SeedState());
        try
        {
            parked.AddSkillXp("husbandry", 1f);
            Assert.Fail("[prosequor] ProgressPark fixture failed (mutate should throw).");
        }
        catch (InvalidOperationException)
        {
        }
    }

    static void VerifyHydrateRoundTrip()
    {
        SkillRegistry registry = new();
        PlayerProgressState original = SeedState();
        if (!ProgressStore.TryHydrateStored(ProgressStore.Serialize(original), registry, out PlayerProgressState restored)
            || restored.PlayerLevel != original.PlayerLevel
            || restored.Skills["husbandry"].Level != 12
            || restored.Skills["husbandry"].GetTier("gentle-spirit") != 2)
        {
            Assert.Fail("[prosequor] ProgressPark fixture failed (hydrate round-trip).");
        }
    }

    static void VerifyUnlockIdRemapDoesNotChain()
    {
        SkillRegistry registry = new();
        PlayerProgressState stored = new() { Schema = 4 };
        SkillProgressState forestry = stored.GetOrCreateSkill("forestry");
        forestry.SetTier("lumberjack", 3);
        forestry.SetTier("forester", 1);
        forestry.SetTier("clean-splitter", 2);
        forestry.Unlocks = new List<string> { "axeexpert" };
        SkillProgressState mining = stored.GetOrCreateSkill("mining");
        mining.SetTier("miner", 1);

        if (!ProgressStore.TryHydrateStored(ProgressStore.Serialize(stored), registry, out PlayerProgressState once))
        {
            Assert.Fail("[prosequor] ProgressPark fixture failed (unlock remap hydrate).");
            return;
        }

        SkillProgressState mapped = once.Skills["forestry"];
        if (once.Schema != PlayerProgressState.CurrentSchema
            || mapped.GetTier("seasoned-logger") != 3
            || mapped.GetTier("lumberjack") != 1
            || mapped.GetTier("forester") != 0
            || mapped.GetTier("clean-splitter") != 2
            || mapped.GetTier("tree-felling") != 1
            || once.Skills["mining"].GetTier("miner") != 1)
        {
            Assert.Fail("[prosequor] ProgressPark fixture failed (unlock remap forestry pair).");
        }

        if (!ProgressStore.TryHydrateStored(ProgressStore.Serialize(once), registry, out PlayerProgressState twice)
            || twice.Skills["forestry"].GetTier("seasoned-logger") != 3
            || twice.Skills["forestry"].GetTier("lumberjack") != 1
            || twice.Skills["forestry"].GetTier("forester") != 0)
        {
            Assert.Fail("[prosequor] ProgressPark fixture failed (unlock remap chained).");
        }
    }

    static void VerifyHydrateRejectsEmpty()
    {
        SkillRegistry registry = new();
        if (ProgressStore.TryHydrateStored(null, registry, out _)
            || ProgressStore.TryHydrateStored(Array.Empty<byte>(), registry, out _)
            || ProgressStore.TryHydrateStored(
                ProgressStore.Serialize(new PlayerProgressState { Schema = 0 }),
                registry,
                out _))
        {
            Assert.Fail("[prosequor] ProgressPark fixture failed (hydrate should reject empty).");
        }
    }

    static void VerifyMissingSkillBarIsZero()
    {
        ParkedPlayerProgress parked = new(new PlayerProgressState());
        parked.GetSkillBar("missing", out float into, out int need, out int level);
        if (level != 0 || into != 0f || need <= 0)
        {
            Assert.Fail("[prosequor] ProgressPark fixture failed (missing skill bar).");
        }
    }

    static void VerifySkillBarCompleteAtKindCap()
    {
        SkillRegistry registry = new();
        registry.Register(new SkillDef
        {
            Id = "athletics",
            Kind = SkillKind.Passive,
            MaxLevel = XpCurves.MinorMaxLevel
        });
        registry.Register(new SkillDef
        {
            Id = "cooking",
            Kind = SkillKind.Hobby,
            MaxLevel = XpCurves.HobbyMaxLevel
        });

        PlayerProgressState state = new() { Schema = PlayerProgressState.CurrentSchema };
        SkillProgressState athletics = state.GetOrCreateSkill("athletics");
        athletics.Level = XpCurves.MinorMaxLevel;
        athletics.Xp = XpCurves.LifetimeXpForSkillLevel(XpCurves.MinorMaxLevel);
        SkillProgressState cooking = state.GetOrCreateSkill("cooking");
        cooking.Level = XpCurves.HobbyMaxLevel;
        cooking.Xp = XpCurves.LifetimeXpForSkillLevel(XpCurves.HobbyMaxLevel);

        ParkedPlayerProgress parked = new(state, ruleIndex: null, registry);
        parked.GetSkillBar("athletics", out float athleticsInto, out int athleticsNeed, out int athleticsLevel);
        parked.GetSkillBar("cooking", out float cookingInto, out int cookingNeed, out int cookingLevel);
        if (athleticsLevel != XpCurves.MinorMaxLevel
            || athleticsInto != 1f
            || athleticsNeed != 1
            || cookingLevel != XpCurves.HobbyMaxLevel
            || cookingInto != 1f
            || cookingNeed != 1)
        {
            Assert.Fail("[prosequor] ProgressPark fixture failed (skill bar complete at kind cap).");
        }
    }

    static PlayerProgressState SeedState()
    {
        PlayerProgressState state = new()
        {
            Schema = PlayerProgressState.CurrentSchema,
            PlayerLevel = 4,
            PlayerXp = XpCurves.LifetimeXpForPlayerLevel(4)
        };
        SkillProgressState skill = state.GetOrCreateSkill("husbandry");
        skill.Level = 12;
        skill.Xp = XpCurves.LifetimeXpForSkillLevel(12);
        skill.SetTier("gentle-spirit", 2);
        return state;
    }
}
