using Newtonsoft.Json.Linq;
using Prosequor.Ability;
using Prosequor.Ability.Hooks;
using Prosequor.Progress;
using Prosequor.Xp;
using Xunit;

namespace Prosequor.Data;

/// <summary>Hobby skills, derived caps, and local unlock-point fixtures.</summary>
public static class HobbySkillFixtures
{
    public static void VerifyAll()
    {
        VerifyKindCaps();
        VerifyHobbyBeatsSpecialization();
        VerifyAuthoredMaxLevelIgnored();
        VerifyHobbyStripsSpecialization();
        VerifyEntitlementFormula();
        VerifyHobbyEligibilityUsesLocalPoints();
        VerifyMenuBucketing();
    }

    static void VerifyKindCaps()
    {
        SkillDefJson hobby = new()
        {
            id = "barber",
            hobby = true,
            tree = new SkillTreeJson
            {
                nodes = [new SkillTreeNodeJson { id = "precise", specialization = true }]
            }
        };
        SkillDefJson spec = new()
        {
            id = "mining",
            tree = new SkillTreeJson
            {
                nodes = [new SkillTreeNodeJson { id = "miner", specialization = true }]
            }
        };
        SkillDefJson minor = new()
        {
            id = "clay",
            tree = new SkillTreeJson
            {
                nodes = [new SkillTreeNodeJson { id = "wheel" }]
            }
        };
        SkillDefJson passive = new() { id = "breathing" };

        if (SkillKindPolicy.ClassifyDraft(hobby) != SkillKind.Hobby
            || SkillKindPolicy.MaxLevelFor(SkillKind.Hobby) != XpCurves.HobbyMaxLevel
            || SkillKindPolicy.ClassifyDraft(spec) != SkillKind.Specialization
            || SkillKindPolicy.MaxLevelFor(SkillKind.Specialization) != XpCurves.SkillMaxLevel
            || SkillKindPolicy.ClassifyDraft(minor) != SkillKind.Minor
            || SkillKindPolicy.MaxLevelFor(SkillKind.Minor) != XpCurves.MinorMaxLevel
            || SkillKindPolicy.ClassifyDraft(passive) != SkillKind.Passive
            || SkillKindPolicy.MaxLevelFor(SkillKind.Passive) != XpCurves.MinorMaxLevel)
        {
            Assert.Fail("[prosequor] Hobby fixture failed (kind → cap mapping).");
        }
    }

    static void VerifyHobbyBeatsSpecialization()
    {
        SkillDefJson row = new()
        {
            id = "hobby-with-spec",
            hobby = true,
            tree = new SkillTreeJson
            {
                nodes =
                [
                    new SkillTreeNodeJson { id = "a", specialization = true },
                    new SkillTreeNodeJson { id = "b" }
                ]
            }
        };

        if (SkillKindPolicy.ClassifyDraft(row) != SkillKind.Hobby)
        {
            Assert.Fail("[prosequor] Hobby fixture failed (hobby should beat specialization).");
        }
    }

    static void VerifyAuthoredMaxLevelIgnored()
    {
        SkillDefJson row = new()
        {
            id = "minor-authored-100",
            maxLevel = 100,
            tree = new SkillTreeJson
            {
                nodes = [new SkillTreeNodeJson { id = "only" }]
            }
        };

        SkillKind kind = SkillKindPolicy.ClassifyDraft(row);
        int cap = SkillKindPolicy.MaxLevelFor(kind);
        if (kind != SkillKind.Minor || cap != XpCurves.MinorMaxLevel || cap == row.maxLevel)
        {
            Assert.Fail("[prosequor] Hobby fixture failed (authored maxLevel must not win).");
        }
    }

    static void VerifyHobbyStripsSpecialization()
    {
        HookRegistry hooks = new();
        AbilityActionRegistry actions = new(hooks);
        AbilityBootstrap.RegisterBuiltIns(hooks, actions);
        CollectionIndex collections = new();
        int order = 0;

        SkillTreeJson? stripped = SkillKindPolicy.StripSpecializationFlags(
            new SkillTreeJson
            {
                nodes =
                [
                    new SkillTreeNodeJson
                    {
                        id = "precise",
                        cost = 1,
                        minSkillLevel = 1,
                        specialization = true
                    },
                    new SkillTreeNodeJson
                    {
                        id = "braid",
                        cost = 1,
                        minSkillLevel = 5,
                        specialization = true,
                        requires = ["precise"]
                    }
                ]
            });

        if (stripped?.nodes == null
            || stripped.nodes.Any(n => n.specialization))
        {
            Assert.Fail("[prosequor] Hobby fixture failed (strip should clear specialization flags).");
        }

        SkillTreeCompiler.CompileResult compiled = SkillTreeCompiler.Compile(
            "hobby-barber",
            XpCurves.HobbyMaxLevel,
            stripped,
            hooks,
            actions,
            collections,
            ref order);

        if (!compiled.Success
            || compiled.Tree == null
            || compiled.Tree.Nodes.Any(n => n.IsSpecialization))
        {
            Assert.Fail("[prosequor] Hobby fixture failed (compiled hobby tree must have no specializations).");
        }
    }

    static void VerifyEntitlementFormula()
    {
        SkillDef eight = HobbyDef(
            "hobby-8",
            [
                Node("a", 1),
                Node("b", 1),
                Node("c", 1),
                Node("d", 1),
                Node("e", 1),
                Node("f", 1),
                Node("g", 1),
                Node("h", 1)
            ]);

        // ceil(8 * 1 / 20) = 1
        if (HobbyPointPolicy.TotalTierCost(eight) != 8
            || HobbyPointPolicy.Entitled(eight, 0) != 0
            || HobbyPointPolicy.Entitled(eight, 1) != 1
            || HobbyPointPolicy.Entitled(eight, 10) != 4
            || HobbyPointPolicy.Entitled(eight, 20) != 8)
        {
            Assert.Fail("[prosequor] Hobby fixture failed (8-cost entitlement).");
        }

        SkillDef hundred = HobbyDef(
            "hobby-100",
            Enumerable.Range(0, 100).Select(i => Node("n" + i, 1)).ToArray());

        // ceil(100 * L / 20) = 5 * L
        if (HobbyPointPolicy.TotalTierCost(hundred) != 100
            || HobbyPointPolicy.Entitled(hundred, 1) != 5
            || HobbyPointPolicy.Entitled(hundred, 4) != 20
            || HobbyPointPolicy.Entitled(hundred, 20) != 100)
        {
            Assert.Fail("[prosequor] Hobby fixture failed (100-cost entitlement / 5 per level).");
        }

        SkillDef zeroCost = HobbyDef(
            "hobby-free",
            [new SkillTreeNodeDef
            {
                Id = "free",
                Tiers = [new SkillTreeTierDef { Cost = 0, MinSkillLevel = 1 }]
            }]);
        if (HobbyPointPolicy.TotalTierCost(zeroCost) != 0
            || HobbyPointPolicy.Entitled(zeroCost, 20) != 0)
        {
            Assert.Fail("[prosequor] Hobby fixture failed (cost-0 tiers must not entitle points).");
        }
    }

    static void VerifyHobbyEligibilityUsesLocalPoints()
    {
        SkillDef hobby = HobbyDef("hobby-buy", [Node("precise", 1), Node("braid", 1)]);
        FixtureProgress progress = new();
        progress.SetSkillLevel("hobby-buy", 1);
        progress.SetUnlockPoints(0);

        // Level 1 entitles 1 point on a 2-cost tree (ceil(2/20)=1). Global points are 0.
        if (HobbyPointPolicy.Spendable(hobby, progress) != 1)
        {
            Assert.Fail("[prosequor] Hobby fixture failed (expected 1 spendable at level 1).");
        }

        if (SkillTreeEligibility.Evaluate(hobby, progress, "precise", out _)
            != UnlockPurchaseStatus.Ok)
        {
            Assert.Fail("[prosequor] Hobby fixture failed (hobby purchase should use local points).");
        }

        progress.SetUnlockTier("hobby-buy", "precise", 1);
        if (HobbyPointPolicy.Spendable(hobby, progress) != 0
            || SkillTreeEligibility.Evaluate(hobby, progress, "braid", out _)
            != UnlockPurchaseStatus.NotEnoughPoints)
        {
            Assert.Fail("[prosequor] Hobby fixture failed (spent hobby points should block next buy).");
        }

        // Global points still 0 — revoke simulation just clears tier; spendable returns.
        progress.SetUnlockTier("hobby-buy", "precise", 0);
        if (HobbyPointPolicy.Spendable(hobby, progress) != 1
            || progress.UnlockPoints != 0)
        {
            Assert.Fail("[prosequor] Hobby fixture failed (revoke must not credit global UnlockPoints).");
        }

        SkillDef minor = new()
        {
            Id = "minor-buy",
            Kind = SkillKind.Minor,
            MaxLevel = XpCurves.MinorMaxLevel,
            Tree = new SkillTreeDef
            {
                Nodes = [Node("only", 1)]
            }
        };
        minor.Tree = BuildTree([Node("only", 1)]);
        progress.SetSkillLevel("minor-buy", 1);
        progress.SetUnlockPoints(0);
        if (SkillTreeEligibility.Evaluate(minor, progress, "only", out _)
            != UnlockPurchaseStatus.NotEnoughPoints)
        {
            Assert.Fail("[prosequor] Hobby fixture failed (minor should still require global points).");
        }
    }

    static void VerifyMenuBucketing()
    {
        SkillDef hobby = new()
        {
            Id = "hobby-menu",
            Kind = SkillKind.Hobby,
            MaxLevel = XpCurves.HobbyMaxLevel,
            Tree = BuildTree([Node("a", 1)])
        };
        SkillDef spec = new()
        {
            Id = "spec-menu",
            Kind = SkillKind.Specialization,
            MaxLevel = XpCurves.SkillMaxLevel,
            Tree = BuildTree(
            [
                new SkillTreeNodeDef
                {
                    Id = "prof",
                    IsSpecialization = true,
                    Tiers = [new SkillTreeTierDef { Cost = 1, MinSkillLevel = 1 }]
                }
            ])
        };
        SkillDef minor = new()
        {
            Id = "minor-menu",
            Kind = SkillKind.Minor,
            MaxLevel = XpCurves.MinorMaxLevel,
            Tree = BuildTree([Node("m", 1)])
        };
        SkillDef passive = new()
        {
            Id = "passive-menu",
            Kind = SkillKind.Passive,
            MaxLevel = XpCurves.MinorMaxLevel
        };
        // Hobby with empty tree still buckets as hobby.
        SkillDef hobbyPassiveShape = new()
        {
            Id = "hobby-empty",
            Kind = SkillKind.Hobby,
            MaxLevel = XpCurves.HobbyMaxLevel
        };

        SkillMenuIndex menu = SkillMenuIndex.Build(
            [hobby, spec, minor, passive, hobbyPassiveShape]);

        if (menu.Sections.Count != 4
            || menu.Sections[0].Kind != SkillMenuSectionKind.Specializations
            || menu.Sections[1].Kind != SkillMenuSectionKind.MinorSkills
            || menu.Sections[2].Kind != SkillMenuSectionKind.HobbySkills
            || menu.Sections[3].Kind != SkillMenuSectionKind.Passives
            || menu.Sections[2].Skills.Count != 2
            || !menu.Sections[2].Skills.Any(s => s.Id == "hobby-empty"))
        {
            Assert.Fail("[prosequor] Hobby fixture failed (menu section bucketing).");
        }
    }

    static SkillDef HobbyDef(string id, SkillTreeNodeDef[] nodes) =>
        new()
        {
            Id = id,
            Kind = SkillKind.Hobby,
            MaxLevel = XpCurves.HobbyMaxLevel,
            Tree = BuildTree(nodes)
        };

    static SkillTreeNodeDef Node(string id, int cost) =>
        new()
        {
            Id = id,
            Tiers = [new SkillTreeTierDef { Cost = cost, MinSkillLevel = 1 }]
        };

    static SkillTreeDef BuildTree(SkillTreeNodeDef[] nodes)
    {
        Dictionary<string, SkillTreeNodeDef> byId = new(StringComparer.OrdinalIgnoreCase);
        foreach (SkillTreeNodeDef node in nodes)
        {
            byId[node.Id] = node;
        }

        return new SkillTreeDef
        {
            Nodes = nodes,
            ById = byId
        };
    }

    /// <summary>Minimal progress stub for hobby point / eligibility checks.</summary>
    sealed class FixtureProgress : IPlayerProgress
    {
        readonly Dictionary<string, int> skillLevels = new(StringComparer.OrdinalIgnoreCase);
        readonly Dictionary<string, int> unlockTiers = new(StringComparer.OrdinalIgnoreCase);
        int unlockPoints;

        public event Action? Changed { add { } remove { } }

        public int PlayerLevel => 1;
        public float PlayerXp => 0;
        public int UnlockPoints => unlockPoints;
        public float PlayerXpUntilNext => 0;

        public void SetSkillLevel(string skillId, int level) => skillLevels[skillId] = level;

        public void SetUnlockTier(string skillId, string nodeId, int tier) =>
            unlockTiers[$"{skillId}:{nodeId}"] = tier;

        public void SetUnlockPoints(int points) => unlockPoints = points;

        public int GetSkillLevel(string skillId) =>
            skillLevels.TryGetValue(skillId, out int level) ? level : 0;

        public float GetSkillXp(string skillId) => 0;
        public IReadOnlyList<string> GetUnlocks(string skillId) => Array.Empty<string>();
        public bool HasUnlock(string skillId, string code) => GetUnlockTier(skillId, code) > 0;

        public int GetUnlockTier(string skillId, string nodeId) =>
            unlockTiers.TryGetValue($"{skillId}:{nodeId}", out int tier) ? tier : 0;

        public int GetAttribute(string id) => AttributeGrowth.DefaultScore;
        public float GetAttributeBucket(string id) => 0f;
        public bool HasSkillAccess(string skillId) => true;

        public void GetPlayerBar(out float intoLevel, out int needForNext, out int level)
        {
            intoLevel = 0;
            needForNext = 1;
            level = PlayerLevel;
        }

        public void GetSkillBar(string skillId, out float intoLevel, out int needForNext, out int level)
        {
            intoLevel = 0;
            needForNext = 1;
            level = GetSkillLevel(skillId);
        }

        public void AddPlayerXp(float amount, XpAwardMode mode = XpAwardMode.Earn) { }
        public void AddSkillXp(
            string skillId,
            float amount,
            AbilityAction? fact = null,
            XpAwardMode mode = XpAwardMode.Earn)
        { }
        public void AddUnlockPoints(int amount) => unlockPoints += amount;
        public void SetPlayerLevel(int level) { }
        public void AddAttributeBucket(string id, float amount) { }
        public void SetAttribute(string id, int score) { }
        public bool GrantUnlock(string skillId, string code, int cost = 1) => false;
        public bool RevokeUnlock(string skillId, string code, int refund = 1) => false;
        public UnlockPurchaseStatus TryPurchaseNode(string skillId, string nodeId) =>
            UnlockPurchaseStatus.UnknownSkill;
    }
}
