using Newtonsoft.Json.Linq;
using Prosequor.Data;
using Xunit;

namespace Prosequor.Progress;

/// <summary>Level-set parsing, baseline grants, preset overlay, and sparse server config.</summary>
public static class ProgressionProfileFixtures
{
    public static void VerifyAll()
    {
        VerifyLevelSetParse();
        VerifyBaselineGrants();
        VerifyMmoSlots();
        VerifyEarnScale();
        VerifyServerConfigNormalize();
    }

    static void VerifyLevelSetParse()
    {
        LevelSet range = MustParse("1..50");
        ExpectCount(range, 50, "1..50");
        ExpectCrossed(range, 1, 4, 3, "1..50");
        ExpectCrossed(range, 0, 1, 1, "1..50");
        ExpectCrossed(range, 5, 1, 0, "1..50");
        ExpectAtMost(range, 0, 0, "1..50");
        ExpectAtMost(range, 50, 50, "1..50");
        ExpectAtMost(range, 100, 50, "1..50");

        ExpectSame("20, 40, 60, 80, 100", "20,40,60,80,100");
        ExpectSame("20, 40, 60, 80, 100", "20..100^20");
        LevelSet list = MustParse("20,40,60,80,100");
        ExpectCrossed(list, 0, 40, 2, "20,40,60,80,100");
        ExpectCrossed(list, 19, 20, 1, "20,40,60,80,100");
        ExpectCrossed(list, 20, 21, 0, "20,40,60,80,100");
        ExpectSame("10,20,30,40,50", "10, 20, 30, 40, 50");
        if (MustParse("10,20,30,40,50") != ProgressionProfile.Baseline.SpecializationLevels)
        {
            Assert.Fail("[prosequor] Level set '10,20,30,40,50' does not match the baseline specialization list.");
        }

        ExpectCount(MustParse("1..11^3"), 4, "1..11^3");
        ExpectAtMost(MustParse("1..11^3"), 9, 3, "1..11^3");
        ExpectAtMost(MustParse("1..11^3"), 10, 4, "1..11^3");
        ExpectSame("..^3", "1..50^3");
        ExpectCount(MustParse("..^3"), 17, "..^3");
        ExpectSame("1..,20..50^2", "1..19,20..50^2");
        ExpectSame("1..20,..50^2", "1..20,21..50^2");
        ExpectAtMost(MustParse("1..20,..50^2"), 50, 35, "1..20,..50^2");
        ExpectSame("10..20,..,40..50", "10..50");
        ExpectSame("1..,20..50", "1..50");
        ExpectSame("1..,10..,20..50", "1..50");
        ExpectSame("1..20^2,2..20^2", "1..20");
        ExpectSame("1..9^2,11..19^2", "1..19^2");
        ExpectSame("1..30,20..40", "1..40");
        ExpectSame("1 .. 50 ^ 2", "1..50^2");
        ExpectSame(" 20 , 40 ", "20,40");
        ExpectSame("1..10^1", "1..10");
        ExpectSame("5..5^4", "5");
        ExpectSame("20..", "20..100", 100);

        LevelSet pointInGap = MustParse("1..20^2,6");
        ExpectCount(pointInGap, 11, "1..20^2,6");
        ExpectAtMost(pointInGap, 5, 3, "1..20^2,6");
        ExpectAtMost(pointInGap, 6, 4, "1..20^2,6");
        ExpectAtMost(pointInGap, 7, 5, "1..20^2,6");
        ExpectSame("1..20^2,5", "1..20^2");

        LevelSet clipped = MustParse("1..30,20..40^2");
        ExpectAtMost(clipped, 30, 30, "1..30,20..40^2");
        ExpectAtMost(clipped, 31, 30, "1..30,20..40^2");
        ExpectAtMost(clipped, 32, 31, "1..30,20..40^2");
        ExpectAtMost(clipped, 40, 35, "1..30,20..40^2");

        LevelSet ownedSpan = MustParse("1..20^3,..50");
        ExpectAtMost(ownedSpan, 19, 7, "1..20^3,..50");
        ExpectAtMost(ownedSpan, 20, 7, "1..20^3,..50");
        ExpectAtMost(ownedSpan, 21, 8, "1..20^3,..50");

        LevelSet hole = MustParse("1..10,20");
        ExpectCount(hole, 11, "1..10,20");
        ExpectAtMost(hole, 15, 10, "1..10,20");
        ExpectAtMost(hole, 19, 10, "1..10,20");
        ExpectCrossed(hole, 10, 20, 1, "1..10,20");
        ExpectCrossed(hole, 10, 19, 0, "1..10,20");

        LevelSet steppedGap = MustParse("10..20,..^2,40..50");
        ExpectSame("10..20,..^2,40..50", "10..20,21..39^2,40..50");
        ExpectAtMost(steppedGap, 21, 12, "10..20,..^2,40..50");
        ExpectAtMost(steppedGap, 22, 12, "10..20,..^2,40..50");
        ExpectAtMost(steppedGap, 40, 22, "10..20,..^2,40..50");

        ExpectCount(MustParse("1..10000"), 10000, "1..10000");
        ExpectCount(MustParse("1..10000,1..10000"), 10000, "1..10000,1..10000");
        ExpectCount(MustParse("..", 10000), 10000, ".. cap 10000");
        ExpectSame("0", "0..0");
        ExpectAtMost(MustParse("0"), 0, 1, "0");
        ExpectAtMost(MustParse("..5"), 0, 0, "..5");
        ExpectSame("..5", "1..5");

        LevelSet pastCap = MustParse("60..70", 50);
        ExpectCount(pastCap, 11, "60..70");
        ExpectAtMost(pastCap, 59, 0, "60..70");
        ExpectAtMost(pastCap, 70, 11, "60..70");
        if (pastCap != MustParse("60..70", 1))
        {
            Assert.Fail("[prosequor] Level set '60..70' changed when the unused cap changed.");
        }

        LevelSet huge = MustParse("2000000000");
        ExpectCount(huge, 1, "2000000000");
        ExpectAtMost(huge, 1999999999, 0, "2000000000");
        ExpectAtMost(huge, 2000000000, 1, "2000000000");
        ExpectCount(MustParse("2147483647"), 1, "2147483647");

        if (LevelSet.FromRuns(pointInGap.ToRuns()) != pointInGap
            || LevelSet.FromRuns(MustParse("1..20^2,2..20^2").ToRuns()) != MustParse("1..20"))
        {
            Assert.Fail("[prosequor] Level set FromRuns did not round-trip.");
        }

        if (LevelSet.FromRuns(null) != LevelSet.Empty
            || LevelSet.FromRuns(
            [
                new LevelRun(1, 5, 0),
                new LevelRun(-3, 4, 1),
                new LevelRun(9, 4, 2),
                new LevelRun(8, 8, 1)
            ]) != MustParse("8"))
        {
            Assert.Fail("[prosequor] Level set FromRuns kept an invalid run.");
        }

        if (LevelSet.FromRuns([new LevelRun(1, 29, 2), new LevelRun(1, 28, 3)]) != LevelSet.Empty)
        {
            Assert.Fail("[prosequor] Level set FromRuns kept overlapping stepped runs.");
        }

        if (LevelSet.FromRuns([new LevelRun(21, 50, 2)]) != MustParse("21..50^2"))
        {
            Assert.Fail("[prosequor] Level set FromRuns did not snap 21..50^2 to its last grant.");
        }

        ExpectEmpty(null);
        ExpectEmpty("");
        ExpectEmpty("   ");

        ExpectReject("5..1", "runs backwards");
        ExpectReject("nope", "not a whole number");
        ExpectReject("1,,2", "empty entry");
        ExpectReject("1,", "empty entry");
        ExpectReject("1..,..50", "no bound to meet");
        ExpectReject("..,..", "no bound to meet");
        ExpectReject("1..^2,..50", "no bound to meet");
        ExpectReject("1..30^2,1..30^3", "overlap");
        ExpectReject("30..,20..40", "runs backwards");
        ExpectReject("1..50,..", "runs backwards");
        ExpectReject("10^2", "not a whole number");
        ExpectReject("^3", "not a whole number");
        ExpectReject("1..10001", "wider than");
        ExpectReject("1..5000,5001..10001", "wider than");
        ExpectReject("..", "wider than", 10001);
        ExpectReject("1..", "wider than", int.MaxValue);
        ExpectReject("10...20", "not a valid inclusive range");
        ExpectReject("1..2..3", "not a valid inclusive range");
        ExpectReject("1..10^", "not a valid inclusive range");
        ExpectReject("1..10^2^3", "not a valid inclusive range");
        ExpectReject("1..10^0", "not a valid inclusive range");
        ExpectReject("1..10^-1", "not a valid inclusive range");
        ExpectReject("-1", "not a whole number");
        ExpectReject("-1..5", "not a valid inclusive range");
        ExpectReject("1..-1", "not a valid inclusive range");
        ExpectReject("10..", "runs backwards", 5);
        ExpectReject("..", "runs backwards", 0);
        ExpectReject("1..", "runs backwards", 0);
        ExpectReject("..", "runs backwards", -1);
        ExpectReject("2147483647..", "runs backwards", 50);
        ExpectReject("2147483647,..", "not a valid inclusive range");
    }

    static LevelSet MustParse(string text, int openEnd = 50)
    {
        if (LevelSet.TryParse(text, openEnd, out LevelSet set, out string? error))
        {
            return set;
        }

        Assert.Fail($"[prosequor] Level set '{text}' (cap {openEnd}) rejected: {error}");
        return LevelSet.Empty;
    }

    static void ExpectSame(string text, string expected, int openEnd = 50)
    {
        LevelSet set = MustParse(text, openEnd);
        LevelSet other = MustParse(expected, openEnd);
        if (set != other)
        {
            Assert.Fail($"[prosequor] Level set '{text}' count {set.Count} != '{expected}' count {other.Count} (cap {openEnd}).");
        }
    }

    static void ExpectCount(LevelSet set, int count, string label)
    {
        if (set.Count != count)
        {
            Assert.Fail($"[prosequor] Level set '{label}' count {set.Count} != {count}.");
        }
    }

    static void ExpectAtMost(LevelSet set, int level, int count, string label)
    {
        int actual = set.CountAtMost(level);
        if (actual != count)
        {
            Assert.Fail($"[prosequor] Level set '{label}' CountAtMost({level}) is {actual}, expected {count}.");
        }
    }

    static void ExpectCrossed(LevelSet set, int before, int after, int count, string label)
    {
        int actual = set.CountCrossed(before, after);
        if (actual != count)
        {
            Assert.Fail($"[prosequor] Level set '{label}' CountCrossed({before}, {after}) is {actual}, expected {count}.");
        }
    }

    static void ExpectEmpty(string? text)
    {
        if (!LevelSet.TryParse(text, 50, out LevelSet set, out string? error) || set.Count != 0)
        {
            Assert.Fail($"[prosequor] Level set '{text}' should be empty, count {set.Count}, error '{error}'.");
        }
    }

    static void ExpectReject(string text, string fragment, int openEnd = 50)
    {
        bool ok = LevelSet.TryParse(text, openEnd, out _, out string? error);
        if (!ok && error != null && error.Contains(fragment, StringComparison.Ordinal))
        {
            return;
        }

        Assert.Fail($"[prosequor] Level set '{text}' (cap {openEnd}) expected '{fragment}', success={ok}, error='{error}'.");
    }

    static void VerifyBaselineGrants()
    {
        ProgressionProfile profile = ProgressionProfile.Baseline;
        if (profile.SkillPointsForPlayerLevel(1, 4) != 3
            || profile.SkillPointsForPlayerLevel(1, 50) != 49
            || profile.SkillPointsForPlayerLevel(1, 1) != 0
            || profile.SkillPointsForSkillLevel(0, 19) != 0
            || profile.SkillPointsForSkillLevel(0, 100) != 5
            || profile.SpecializationSlotCount(9) != 0
            || profile.SpecializationSlotCount(10) != 1
            || profile.SpecializationSlotCount(50) != 5
            || profile.MaxPlayerLevel != 50
            || profile.XpGain != 1f)
        {
            Assert.Fail("[prosequor] Progression fixture failed (baseline grants).");
        }
    }

    static void VerifyMmoSlots()
    {
        ProgressionProfile mmo = ProgressionProfile.Baseline.With(
            presetId: ProgressionProfile.MmoPresetId,
            specializationLevels: LevelSet.Parse("20,40", 50));
        if (mmo.SpecializationSlotCount(19) != 0
            || mmo.SpecializationSlotCount(20) != 1
            || mmo.SpecializationSlotCount(40) != 2
            || mmo.SpecializationSlotCount(50) != 2
            || mmo.SkillPointsForPlayerLevel(1, 50) != 49)
        {
            Assert.Fail("[prosequor] Progression fixture failed (MMO slots).");
        }
    }

    static void VerifyEarnScale()
    {
        ProgressionProfile single = ProgressionProfile.Baseline.With(xpGain: 2f);
        if (Math.Abs(single.ScaleEarn(4f) - 8f) > 0.0001f
            || Math.Abs(ProgressionProfile.Baseline.ScaleEarn(4f) - 4f) > 0.0001f
            || single.ScaleEarn(0f) != 0f
            || single.ScaleEarn(-1f) != -1f)
        {
            Assert.Fail("[prosequor] Progression fixture failed (xp gain).");
        }
    }

    static void VerifyServerConfigNormalize()
    {
        Dictionary<string, ProgressionProfile> presets = StockPresets();
        ProgressionProfile baseline = ProgressionProfile.Baseline;

        JObject defaults = new()
        {
            ["maxPlayerLevel"] = 50,
            ["skillPointsPerPlayerLevel"] = "1..50",
            ["skillPointsPerSkillLevel"] = "20,40,60,80,100",
            ["specializationLevels"] = "10, 20, 30, 40, 50",
            ["xpGain"] = 3
        };
        List<string> warnings = new();
        ServerProgressionConfig.Normalize(defaults, presets, baseline, out ProgressionProfile resolved, warnings);
        if (defaults.Count != 2
            || defaults["preset"]?.Value<string>() != ProgressionProfile.MultiplayerPresetId
            || defaults["xpGain"]?.Value<int>() != 3
            || Math.Abs(resolved.XpGain - 3f) > 0.0001f
            || resolved.SpecializationSlotCount(50) != 5
            || warnings.Count != 0)
        {
            Assert.Fail("[prosequor] Progression fixture failed (default table migrates, custom xp stays).");
        }

        JObject single = new()
        {
            ["preset"] = ProgressionProfile.SinglePlayerPresetId,
            ["xpGain"] = 2,
            ["specializationLevels"] = "20,40"
        };
        warnings.Clear();
        ServerProgressionConfig.Normalize(single, presets, baseline, out ProgressionProfile singleResolved, warnings);
        if (single["xpGain"] != null
            || single["specializationLevels"]?.Value<string>() != "20,40"
            || Math.Abs(singleResolved.XpGain - 2f) > 0.0001f
            || singleResolved.SpecializationSlotCount(50) != 2)
        {
            Assert.Fail("[prosequor] Progression fixture failed (single-player drops its own xp gain).");
        }

        single["preset"] = ProgressionProfile.MmoPresetId;
        warnings.Clear();
        ServerProgressionConfig.Normalize(single, presets, baseline, out ProgressionProfile mmoResolved, warnings);
        if (mmoResolved.PresetId != ProgressionProfile.MmoPresetId
            || single["specializationLevels"] != null
            || Math.Abs(mmoResolved.XpGain - 1f) > 0.0001f
            || mmoResolved.SpecializationSlotCount(50) != 2)
        {
            Assert.Fail("[prosequor] Progression fixture failed (switching to MMO drops matching slots).");
        }

        JObject customGain = new()
        {
            ["preset"] = ProgressionProfile.MmoPresetId,
            ["xpGain"] = 3
        };
        warnings.Clear();
        ServerProgressionConfig.Normalize(customGain, presets, baseline, out ProgressionProfile kept, warnings);
        if (customGain["xpGain"]?.Value<int>() != 3 || Math.Abs(kept.XpGain - 3f) > 0.0001f)
        {
            Assert.Fail("[prosequor] Progression fixture failed (MMO keeps a custom xp gain).");
        }

        JObject unknown = new()
        {
            ["preset"] = "prosequor:missing",
            ["xpGain"] = 4
        };
        warnings.Clear();
        ServerProgressionConfig.Normalize(unknown, presets, baseline, out ProgressionProfile missing, warnings);
        if (warnings.Count != 1
            || unknown["preset"]?.Value<string>() != "prosequor:missing"
            || unknown["xpGain"]?.Value<int>() != 4
            || Math.Abs(missing.XpGain - 4f) > 0.0001f
            || missing.SpecializationSlotCount(50) != 5)
        {
            Assert.Fail("[prosequor] Progression fixture failed (unknown preset keeps overrides).");
        }

        JObject openPlayer = new()
        {
            ["skillPointsPerPlayerLevel"] = ".."
        };
        warnings.Clear();
        ServerProgressionConfig.Normalize(openPlayer, presets, baseline, out _, warnings);
        if (openPlayer.Count != 1
            || openPlayer["preset"]?.Value<string>() != ProgressionProfile.MultiplayerPresetId
            || warnings.Count != 0)
        {
            Assert.Fail("[prosequor] Progression fixture failed (open player list matches 1..50).");
        }

        JObject raised = new()
        {
            ["maxPlayerLevel"] = 60,
            ["skillPointsPerPlayerLevel"] = "..",
            ["skillPointsPerSkillLevel"] = ".."
        };
        warnings.Clear();
        ServerProgressionConfig.Normalize(raised, presets, baseline, out ProgressionProfile raisedProfile, warnings);
        if (raisedProfile.MaxPlayerLevel != 60
            || raisedProfile.SkillPointsForPlayerLevel(50, 60) != 10
            || raisedProfile.SkillPointsForSkillLevel(0, 100) != 100
            || warnings.Count != 0)
        {
            Assert.Fail("[prosequor] Progression fixture failed (open ends use the list cap).");
        }
    }

    static Dictionary<string, ProgressionProfile> StockPresets()
    {
        ProgressionProfile baseline = ProgressionProfile.Baseline;
        return new Dictionary<string, ProgressionProfile>(StringComparer.OrdinalIgnoreCase)
        {
            [ProgressionProfile.MultiplayerPresetId] = baseline,
            [ProgressionProfile.SinglePlayerPresetId] = baseline.With(
                presetId: ProgressionProfile.SinglePlayerPresetId,
                xpGain: 2f),
            [ProgressionProfile.MmoPresetId] = baseline.With(
                presetId: ProgressionProfile.MmoPresetId,
                specializationLevels: LevelSet.Parse("20,40", 50))
        };
    }
}
