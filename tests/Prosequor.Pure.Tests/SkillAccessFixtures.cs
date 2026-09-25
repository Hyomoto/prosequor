using Prosequor.Ability.Hooks;
using Prosequor.Progress;
using Prosequor.Xp;
using Xunit;

namespace Prosequor.Data;

/// <summary>Skill-access union, class sets, and lost-access refund fixtures.</summary>
public static class SkillAccessFixtures
{
    public static void VerifyAll()
    {
        VerifyUpdateSharesSingleSlot();
        VerifyUpdateUnionsSecondKey();
        VerifyClassBaseExcludesOptional();
        VerifyTraitSkillsAddKnownOnly();
        VerifyRefundNonHobby();
        VerifyRefundHobby();
    }

    static void VerifyUpdateSharesSingleSlot()
    {
        HashSet<string> catalog = new(StringComparer.OrdinalIgnoreCase) { "digging", "farming" };
        SkillAccess access = new();
        if (access.Update(SkillAccess.ClassKey, catalog) != true
            || !ReferenceEquals(access.Union, catalog)
            || access.Update(SkillAccess.ClassKey, catalog)
            || !access.Contains("digging")
            || access.Contains("missing"))
        {
            Assert.Fail("[prosequor] SkillAccess single-slot should share the catalog reference.");
        }
    }

    static void VerifyUpdateUnionsSecondKey()
    {
        HashSet<string> classSet = new(StringComparer.OrdinalIgnoreCase) { "digging" };
        HashSet<string> playerSet = new(StringComparer.OrdinalIgnoreCase) { "farming" };
        SkillAccess access = new();
        access.Update(SkillAccess.ClassKey, classSet);
        if (!access.Update("player", playerSet)
            || !access.Contains("digging")
            || !access.Contains("farming")
            || ReferenceEquals(access.Union, classSet))
        {
            Assert.Fail("[prosequor] SkillAccess second key should union into a new set.");
        }

        if (!access.Update("player", null)
            || !ReferenceEquals(access.Union, classSet)
            || access.Contains("farming"))
        {
            Assert.Fail("[prosequor] Dropping the second key should share the class set again.");
        }
    }

    static void VerifyClassBaseExcludesOptional()
    {
        SkillRegistry skills = new();
        skills.Register(new SkillDef { Id = "always", IsOptional = false });
        skills.Register(new SkillDef { Id = "gated", IsOptional = true });

        TraitAttributeRegistry traits = new();
        traits.SetClassOriginalTraits("hunter", Array.Empty<string>());
        traits.RebuildClassSkillSets(skills);

        if (!traits.BaseSkillSet.Contains("always")
            || traits.BaseSkillSet.Contains("gated")
            || !ReferenceEquals(traits.SkillSetForClass("hunter"), traits.BaseSkillSet))
        {
            Assert.Fail("[prosequor] Class base must include non-optional skills and share for empty traits.");
        }
    }

    static void VerifyTraitSkillsAddKnownOnly()
    {
        SkillRegistry skills = new();
        skills.Register(new SkillDef { Id = "always" });
        skills.Register(new SkillDef { Id = "gated", IsOptional = true });

        TraitAttributeRegistry traits = new();
        traits.Register(new TraitAttributeMapping
        {
            Code = "soldier",
            Skills = ["gated", "always", "no-such-skill"]
        });
        traits.SetClassOriginalTraits("blackguard", ["soldier"]);
        traits.RebuildClassSkillSets(skills);

        IReadOnlySet<string> set = traits.SkillSetForClass("blackguard");
        if (!set.Contains("always")
            || !set.Contains("gated")
            || set.Contains("no-such-skill")
            || ReferenceEquals(set, traits.BaseSkillSet))
        {
            Assert.Fail("[prosequor] Trait skills must add known optional ids and skip unknown ones.");
        }

        traits.Register(new TraitAttributeMapping
        {
            Code = "noop",
            Skills = ["always"]
        });
        traits.SetClassOriginalTraits("commoner", ["noop"]);
        traits.RebuildClassSkillSets(skills);
        if (!ReferenceEquals(traits.SkillSetForClass("commoner"), traits.BaseSkillSet))
        {
            Assert.Fail("[prosequor] Trait skills already in the base must keep the shared base reference.");
        }
    }

    static void VerifyRefundNonHobby()
    {
        SkillDef skill = BuildMinorSkill("mining", "pick", cost: 2);
        SkillRegistry skills = new();
        skills.Register(skill);

        PlayerProgressState state = new();
        state.UnlockPoints = 1;
        SkillProgressState progress = state.GetOrCreateSkill(skill.Id);
        progress.Level = 12;
        progress.Xp = 40f;
        progress.SetTier("pick", 1);

        HashSet<string> union = new(StringComparer.OrdinalIgnoreCase);
        int cleared = SkillAccessReconcile.RefundLostAccess(state, skills, union);
        if (cleared != 1
            || state.UnlockPoints != 3
            || progress.GetTier("pick") != 0
            || progress.Level != 12
            || progress.Xp != 40f)
        {
            Assert.Fail("[prosequor] Non-hobby lost access must refund tier costs and keep XP/level.");
        }

        if (SkillAccessReconcile.RefundLostAccess(state, skills, union) != 0)
        {
            Assert.Fail("[prosequor] Lost-access refund must be idempotent.");
        }
    }

    static void VerifyRefundHobby()
    {
        SkillDef skill = BuildHobbySkill("barber", "cut", cost: 3);
        SkillRegistry skills = new();
        skills.Register(skill);

        PlayerProgressState state = new();
        state.UnlockPoints = 5;
        SkillProgressState progress = state.GetOrCreateSkill(skill.Id);
        progress.Level = 4;
        progress.Xp = 10f;
        progress.SetTier("cut", 1);

        HashSet<string> union = new(StringComparer.OrdinalIgnoreCase);
        int cleared = SkillAccessReconcile.RefundLostAccess(state, skills, union);
        if (cleared != 1
            || state.UnlockPoints != 5
            || progress.GetTier("cut") != 0
            || progress.Level != 4
            || progress.Xp != 10f)
        {
            Assert.Fail("[prosequor] Hobby lost access must clear tiers without minting global points.");
        }
    }

    static SkillDef BuildMinorSkill(string id, string nodeId, int cost)
    {
        SkillTreeNodeDef node = new()
        {
            Id = nodeId,
            Tiers =
            [
                new SkillTreeTierDef
                {
                    Cost = cost,
                    MinSkillLevel = 0,
                    Rules = Array.Empty<AbilityRule>()
                }
            ]
        };
        return new SkillDef
        {
            Id = id,
            Kind = SkillKind.Minor,
            MaxLevel = XpCurves.MinorMaxLevel,
            Tree = new SkillTreeDef { Nodes = [node] }
        };
    }

    static SkillDef BuildHobbySkill(string id, string nodeId, int cost)
    {
        SkillTreeNodeDef node = new()
        {
            Id = nodeId,
            Tiers =
            [
                new SkillTreeTierDef
                {
                    Cost = cost,
                    MinSkillLevel = 0,
                    Rules = Array.Empty<AbilityRule>()
                }
            ]
        };
        return new SkillDef
        {
            Id = id,
            Kind = SkillKind.Hobby,
            MaxLevel = XpCurves.HobbyMaxLevel,
            Tree = new SkillTreeDef { Nodes = [node] }
        };
    }
}
