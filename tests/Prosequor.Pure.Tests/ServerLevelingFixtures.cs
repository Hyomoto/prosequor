using Newtonsoft.Json.Linq;
using Prosequor.Data;
using Prosequor.Progress;
using Xunit;

namespace Prosequor.Pure.Tests;

/// <summary>Server leveling file normalize, player cap, milestone interval, rule overlay, and XP scale.</summary>
public static class ServerLevelingFixtures
{
    public static void VerifyAll()
    {
        VerifyStockNormalizeIsClean();
        VerifyGarbageClampsToDefaults();
        VerifyListsDropLevelsAboveTheCap();
        VerifyCustomPlayerCap();
        VerifyMilestoneInterval();
        VerifyOverlay();
        VerifyScale();
    }

    static void VerifyStockNormalizeIsClean()
    {
        JObject stock = Stock();
        if (ServerLevelingConfig.Normalize(stock, out ServerLevelingConfig settings))
        {
            Assert.Fail("[prosequor] Server leveling fixture failed (stock file should not rewrite).");
        }

        if (settings.SkillLevelsPerPoint != ServerLevelingConfig.DefaultSkillLevelsPerPoint
            || settings.SkillPointEvery != ServerLevelingConfig.DefaultSkillPointEvery
            || settings.MaxPlayerLevel != ServerLevelingConfig.DefaultMaxPlayerLevel
            || settings.XpScale != ServerLevelingConfig.DefaultXpScale
            || settings.AttributePointLevels.Length != ServerLevelingConfig.DefaultAttributePointLevels().Length
            || settings.SpecializationPointLevels.Length != 5
            || settings.SpecializationPointLevels[0] != 10
            || settings.SpecializationPointLevels[4] != 50)
        {
            Assert.Fail("[prosequor] Server leveling fixture failed (stock values).");
        }
    }

    static void VerifyGarbageClampsToDefaults()
    {
        JObject root = new()
        {
            [ServerLevelingConfig.SkillLevelsPerPointProperty] = -3,
            [ServerLevelingConfig.SkillPointEveryProperty] = 0,
            [ServerLevelingConfig.MaxPlayerLevelProperty] = 0,
            [ServerLevelingConfig.XpScaleProperty] = 5000,
            [ServerLevelingConfig.AttributePointLevelsProperty] = "nope",
            [ServerLevelingConfig.SpecializationPointLevelsProperty] = JValue.CreateNull()
        };

        if (!ServerLevelingConfig.Normalize(root, out ServerLevelingConfig settings)
            || settings.SkillLevelsPerPoint != 0
            || settings.SkillPointEvery != ServerLevelingConfig.DefaultSkillPointEvery
            || settings.MaxPlayerLevel != ServerLevelingConfig.MinPlayerLevel
            || settings.XpScale != ServerLevelingConfig.MaxXpScale
            || settings.AttributePointLevels.Length != 0
            || settings.SpecializationPointLevels.Length != 0)
        {
            Assert.Fail("[prosequor] Server leveling fixture failed (garbage clamps).");
        }
    }

    static void VerifyListsDropLevelsAboveTheCap()
    {
        JObject root = Stock();
        root[ServerLevelingConfig.MaxPlayerLevelProperty] = 40;
        root[ServerLevelingConfig.AttributePointLevelsProperty] = new JArray(50, 5, 5, 10);
        root[ServerLevelingConfig.SpecializationPointLevelsProperty] = new JArray(20, 60);

        if (!ServerLevelingConfig.Normalize(root, out ServerLevelingConfig settings)
            || settings.MaxPlayerLevel != 40
            || settings.AttributePointLevels.Length != 2
            || settings.AttributePointLevels[0] != 5
            || settings.AttributePointLevels[1] != 10
            || settings.SpecializationPointLevels.Length != 1
            || settings.SpecializationPointLevels[0] != 20)
        {
            Assert.Fail("[prosequor] Server leveling fixture failed (levels above the cap drop).");
        }
    }

    static void VerifyCustomPlayerCap()
    {
        if (XpCurves.XpToNextPlayerLevel(50) != 0
            || XpCurves.XpToNextPlayerLevel(50, 60) <= 0
            || XpCurves.XpToNextPlayerLevel(60, 60) != 0)
        {
            Assert.Fail("[prosequor] Server leveling fixture failed (player cap stops the curve).");
        }

        float at60 = XpCurves.LifetimeXpForPlayerLevel(60, 60);
        if (XpCurves.PlayerLevelFromLifetimeXp(at60, 60) != 60
            || XpCurves.PlayerLevelFromLifetimeXp(at60, 50) != 50)
        {
            Assert.Fail("[prosequor] Server leveling fixture failed (level from XP respects the cap).");
        }
    }

    static void VerifyMilestoneInterval()
    {
        if (UnlockPointPolicy.PointsForSkillLevelGain(0, 19) != 0
            || UnlockPointPolicy.PointsForSkillLevelGain(0, 10, 10) != 1
            || UnlockPointPolicy.PointsForSkillLevelGain(0, 9, 10) != 0
            || UnlockPointPolicy.PointsForSkillLevelGain(0, 40, 0) != 0)
        {
            Assert.Fail("[prosequor] Server leveling fixture failed (milestone interval).");
        }
    }

    static void VerifyOverlay()
    {
        LevelUpRuleDef extra = new()
        {
            Id = "prosequor:extra",
            Every = 3,
            Action = LevelUpActionKind.EarnSkillPoint,
            Value = 2,
            SourceOrder = 3
        };
        List<LevelUpRuleDef> rules =
        [
            new()
            {
                Id = ServerLevelingConfig.SkillPointRuleId,
                Every = 1,
                Action = LevelUpActionKind.EarnSkillPoint,
                Value = 1,
                SourceOrder = 0
            },
            new()
            {
                Id = ServerLevelingConfig.SpecializationRuleId,
                Every = 10,
                Action = LevelUpActionKind.EarnSpecializationPoint,
                Value = 1,
                SourceOrder = 1
            },
            new()
            {
                Id = ServerLevelingConfig.AttributeRuleId,
                Levels = new HashSet<int> { 5, 10 },
                Action = LevelUpActionKind.EarnAttribute,
                Value = 1,
                AttributeKey = LevelUpRuleDef.BucketsKey,
                SourceOrder = 2
            },
            extra
        ];

        ServerLevelingConfig settings = ServerLevelingConfig.CreateDefault();
        settings.SkillPointEvery = 4;
        settings.AttributePointLevels = [7];
        settings.SpecializationPointLevels = [20];

        List<LevelUpRuleDef> overlaid = ServerLevelingConfig.Overlay(rules, settings, out List<string> missing);
        if (missing.Count != 0)
        {
            Assert.Fail("[prosequor] Server leveling fixture failed (overlay reported a missing rule).");
        }

        LevelUpRuleDef skill = overlaid[0];
        LevelUpRuleDef spec = overlaid[1];
        LevelUpRuleDef attribute = overlaid[2];
        if (skill.Every != 4
            || skill.Levels != null
            || skill.Value != 1
            || attribute.Every != null
            || attribute.Levels == null
            || !attribute.Levels.Contains(7)
            || attribute.Levels.Count != 1
            || attribute.AttributeKey != LevelUpRuleDef.BucketsKey
            || spec.Every != null
            || spec.Levels == null
            || spec.Levels.Contains(10)
            || !spec.Levels.Contains(20)
            || !ReferenceEquals(extra, overlaid[3])
            || extra.Every != 3)
        {
            Assert.Fail("[prosequor] Server leveling fixture failed (overlay touched the wrong rules).");
        }

        if (SpecializationPolicy.AllowedSlots(19, overlaid) != 0
            || SpecializationPolicy.AllowedSlots(20, overlaid) != 1
            || SpecializationPolicy.AllowedSlots(50, overlaid) != 1)
        {
            Assert.Fail("[prosequor] Server leveling fixture failed (specialization list [20]).");
        }

        LevelUpRegistry registry = new();
        registry.Install(rules);
        settings.ApplyTo(registry, _ => Assert.Fail("[prosequor] Server leveling fixture failed (unexpected missing rule)."));
        if (registry.Rules.Count != 4 || registry.Rules[1].Every != null)
        {
            Assert.Fail("[prosequor] Server leveling fixture failed (registry install).");
        }

        List<LevelUpRuleDef> absent = ServerLevelingConfig.Overlay(
            [extra],
            ServerLevelingConfig.CreateDefault(),
            out List<string> missingIds);
        if (absent.Count != 1
            || missingIds.Count != 3
            || !missingIds.Contains(ServerLevelingConfig.SkillPointRuleId)
            || !missingIds.Contains(ServerLevelingConfig.AttributeRuleId)
            || !missingIds.Contains(ServerLevelingConfig.SpecializationRuleId))
        {
            Assert.Fail("[prosequor] Server leveling fixture failed (missing rule ids).");
        }
    }

    static void VerifyScale()
    {
        if (ServerLevelingConfig.ScaleEarned(10f, 1f) != 10f
            || ServerLevelingConfig.ScaleEarned(10f, 2f) != 20f
            || ServerLevelingConfig.ScaleEarned(10f, 0f) != 0f
            || ServerLevelingConfig.ScaleEarned(0f, 4f) != 0f)
        {
            Assert.Fail("[prosequor] Server leveling fixture failed (xp scale).");
        }
    }

    static JObject Stock() => new()
    {
        [ServerLevelingConfig.SkillLevelsPerPointProperty] = ServerLevelingConfig.DefaultSkillLevelsPerPoint,
        [ServerLevelingConfig.SkillPointEveryProperty] = ServerLevelingConfig.DefaultSkillPointEvery,
        [ServerLevelingConfig.MaxPlayerLevelProperty] = ServerLevelingConfig.DefaultMaxPlayerLevel,
        [ServerLevelingConfig.XpScaleProperty] = 1,
        [ServerLevelingConfig.AttributePointLevelsProperty] = new JArray(ServerLevelingConfig.DefaultAttributePointLevels()),
        [ServerLevelingConfig.SpecializationPointLevelsProperty] = new JArray(ServerLevelingConfig.DefaultSpecializationPointLevels())
    };
}
