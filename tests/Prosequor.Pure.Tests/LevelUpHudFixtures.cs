using Prosequor.Data;
using Prosequor.Network;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Xunit;

namespace Prosequor.Progress;

/// <summary>Level-up HUD math and packet shape checks (no world required).</summary>
public static class LevelUpHudFixtures
{
    public static void VerifyAll()
    {
        VerifyPlayerBarFill();
        VerifySkillBarAtKindCaps();
        VerifyPacketFlags();
        VerifyAttributeGains();
    }

    static void VerifyPlayerBarFill()
    {
        if (!Near(LevelUpHudMath.PlayerBarFill(0f, 1), 0f))
        {
            Assert.Fail("[prosequor] Level-up HUD fixture failed (empty bar at level start).");
            return;
        }

        const float lifetimeXp = 20f;
        const int level = 1;
        int need = XpCurves.XpToNextPlayerLevel(level);
        float expected = GameMath.Clamp(lifetimeXp / need, 0f, 1f);
        if (!Near(LevelUpHudMath.PlayerBarFill(lifetimeXp, level), expected))
        {
            Assert.Fail("[prosequor] Level-up HUD fixture failed (mid-level fill).");
            return;
        }

        if (!Near(LevelUpHudMath.PlayerBarFill(999999f, XpCurves.PlayerMaxLevel), 1f))
        {
            Assert.Fail("[prosequor] Level-up HUD fixture failed (cap fill).");
        }
    }

    static void VerifySkillBarAtKindCaps()
    {
        if (XpCurves.XpToNextSkillLevel(XpCurves.SkillMaxLevel) != 0
            || XpCurves.XpToNextSkillLevel(XpCurves.MinorMaxLevel) <= 0
            || XpCurves.XpToNextSkillLevel(XpCurves.MinorMaxLevel, XpCurves.MinorMaxLevel) != 0
            || XpCurves.XpToNextSkillLevel(XpCurves.HobbyMaxLevel, XpCurves.HobbyMaxLevel) != 0)
        {
            Assert.Fail("[prosequor] Level-up HUD fixture failed (skill XP-to-next at kind caps).");
            return;
        }

        XpCurves.SkillBar(
            XpCurves.LifetimeXpForSkillLevel(XpCurves.SkillMaxLevel),
            XpCurves.SkillMaxLevel,
            XpCurves.SkillMaxLevel,
            out float specInto,
            out int specNeed);
        XpCurves.SkillBar(
            XpCurves.LifetimeXpForSkillLevel(XpCurves.MinorMaxLevel),
            XpCurves.MinorMaxLevel,
            XpCurves.SkillMaxLevel,
            out _,
            out int specMidNeed);
        XpCurves.SkillBar(
            XpCurves.LifetimeXpForSkillLevel(XpCurves.MinorMaxLevel),
            XpCurves.MinorMaxLevel,
            XpCurves.MinorMaxLevel,
            out float minorInto,
            out int minorNeed);
        XpCurves.SkillBar(
            XpCurves.LifetimeXpForSkillLevel(XpCurves.HobbyMaxLevel),
            XpCurves.HobbyMaxLevel,
            XpCurves.HobbyMaxLevel,
            out float hobbyInto,
            out int hobbyNeed);
        XpCurves.SkillBar(
            XpCurves.LifetimeXpForSkillLevel(XpCurves.HobbyMaxLevel - 1),
            XpCurves.HobbyMaxLevel - 1,
            XpCurves.HobbyMaxLevel,
            out _,
            out int hobbyBelowNeed);

        if (specInto != 1f || specNeed != 1)
        {
            Assert.Fail("[prosequor] Level-up HUD fixture failed (specialization bar at 100).");
            return;
        }

        if (specMidNeed <= 1)
        {
            Assert.Fail("[prosequor] Level-up HUD fixture failed (specialization bar still open at 50).");
            return;
        }

        if (minorInto != 1f || minorNeed != 1)
        {
            Assert.Fail("[prosequor] Level-up HUD fixture failed (minor/passive bar at 50).");
            return;
        }

        if (hobbyInto != 1f || hobbyNeed != 1 || hobbyBelowNeed <= 1)
        {
            Assert.Fail("[prosequor] Level-up HUD fixture failed (hobby bar at 20).");
        }
    }

    static void VerifyPacketFlags()
    {
        LevelUpHudPacket skillOnly = new()
        {
            SkillId = "fishing",
            SkillLevelBefore = 9,
            SkillLevelAfter = 10,
            PlayerLevelBefore = 5,
            PlayerLevelAfter = 5,
            PlayerBarFillBefore = 0.2f,
            PlayerBarFillAfter = 0.35f,
            PlayerLeveledUp = false
        };
        if (!skillOnly.SkillLeveledUp || skillOnly.PlayerLeveledUp || skillOnly.AttributeGains.Count != 0)
        {
            Assert.Fail("[prosequor] Level-up HUD fixture failed (skill-only packet flags).");
            return;
        }

        LevelUpHudPacket playerOnly = new()
        {
            SkillId = "",
            SkillLevelBefore = 0,
            SkillLevelAfter = 0,
            PlayerLevelBefore = 5,
            PlayerLevelAfter = 6,
            PlayerBarFillBefore = 0.9f,
            PlayerBarFillAfter = 0.1f,
            PlayerLeveledUp = true
        };
        if (playerOnly.SkillLeveledUp || !playerOnly.PlayerLeveledUp)
        {
            Assert.Fail("[prosequor] Level-up HUD fixture failed (player-only packet flags).");
        }
    }

    static void VerifyAttributeGains()
    {
        LevelUpHudPacket noGain = new()
        {
            SkillId = "fishing",
            SkillLevelBefore = 1,
            SkillLevelAfter = 2,
            PlayerLevelBefore = 8,
            PlayerLevelAfter = 9,
            PlayerLeveledUp = true,
            AttributeGains = new List<string>()
        };
        if (noGain.AttributeGains.Count != 0)
        {
            Assert.Fail("[prosequor] Level-up HUD fixture failed (empty attribute gains).");
            return;
        }

        LevelUpHudPacket single = new()
        {
            PlayerLevelBefore = 9,
            PlayerLevelAfter = 10,
            PlayerLeveledUp = true,
            AttributeGains = new List<string> { AttributeIds.Strength }
        };
        if (single.AttributeGains.Count != 1
            || !string.Equals(single.AttributeGains[0], AttributeIds.Strength, StringComparison.Ordinal))
        {
            Assert.Fail("[prosequor] Level-up HUD fixture failed (single attribute gain).");
            return;
        }

        LevelUpHudPacket multi = new()
        {
            PlayerLevelBefore = 1,
            PlayerLevelAfter = 50,
            PlayerLeveledUp = true,
            AttributeGains = new List<string>
            {
                AttributeIds.Strength,
                AttributeIds.Perception,
                AttributeIds.Constitution
            }
        };
        if (multi.AttributeGains.Count != 3)
        {
            Assert.Fail("[prosequor] Level-up HUD fixture failed (multi attribute gains).");
        }
    }

    static bool Near(float a, float b, float eps = 0.0001f) =>
        Math.Abs(a - b) <= eps;
}
