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
        VerifyBurstCompact();
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

    static void VerifyBurstCompact()
    {
        LevelUpHudPacket single = new()
        {
            SkillId = "fishing",
            SkillLevelBefore = 1,
            SkillLevelAfter = 2,
            PlayerLevelBefore = 4,
            PlayerLevelAfter = 4,
            PlayerBarFillBefore = 0.2f,
            PlayerBarFillAfter = 0.5f
        };
        List<LevelUpHudPacket> one = LevelUpHudBurst.Compact(new[] { single });
        if (one.Count != 1 || !ReferenceEquals(one[0], single))
        {
            Assert.Fail("[prosequor] Level-up HUD fixture failed (single packet is not folded).");
            return;
        }

        List<LevelUpHudPacket> sameSkill = LevelUpHudBurst.Compact(new[]
        {
            Skill("fishing", 2, 3, 4, 4, 0.2f, 0.5f),
            Skill("fishing", 3, 5, 4, 4, 0.5f, 0.1f)
        });
        if (sameSkill.Count != 1
            || sameSkill[0].SkillLevelBefore != 2
            || sameSkill[0].SkillLevelAfter != 5
            || sameSkill[0].PlayerLeveledUp
            || !Near(sameSkill[0].PlayerBarFillBefore, 0.2f)
            || !Near(sameSkill[0].PlayerBarFillAfter, 0.1f))
        {
            Assert.Fail("[prosequor] Level-up HUD fixture failed (same-skill span).");
            return;
        }

        List<LevelUpHudPacket> skillsOnly = LevelUpHudBurst.Compact(new[]
        {
            Skill("fishing", 1, 2, 4, 4, 0.1f, 0.4f),
            Skill("mining", 2, 3, 4, 4, 0.4f, 0.7f)
        });
        if (skillsOnly.Count != 2
            || skillsOnly[0].SkillId != "fishing"
            || skillsOnly[0].PlayerLeveledUp
            || !Near(skillsOnly[0].PlayerBarFillAfter, 0.1f)
            || skillsOnly[1].SkillId != "mining"
            || skillsOnly[1].SkillLevelAfter != 3
            || !Near(skillsOnly[1].PlayerBarFillBefore, 0.1f)
            || !Near(skillsOnly[1].PlayerBarFillAfter, 0.7f))
        {
            Assert.Fail("[prosequor] Level-up HUD fixture failed (per-skill fold, bar on the last).");
            return;
        }

        LevelUpHudPacket withAttrs = Skill("fishing", 2, 4, 5, 7, 0.4f, 0.1f);
        withAttrs.AttributeGains = new List<string> { AttributeIds.Strength, AttributeIds.Perception };
        LevelUpHudPacket mining = Skill("mining", 3, 4, 7, 7, 0.1f, 0.3f);
        mining.AttributeGains = new List<string> { AttributeIds.Strength, AttributeIds.Constitution };
        List<LevelUpHudPacket> burst = LevelUpHudBurst.Compact(new[]
        {
            Skill("fishing", 1, 2, 5, 5, 0.2f, 0.4f),
            withAttrs,
            mining
        });
        if (burst.Count != 3
            || burst[0].SkillId != "fishing"
            || burst[0].SkillLevelBefore != 1
            || burst[0].SkillLevelAfter != 4
            || burst[0].PlayerLevelAfter != 5
            || burst[0].PlayerLeveledUp
            || burst[0].AttributeGains.Count != 0
            || burst[1].SkillId != "mining"
            || burst[1].PlayerLevelAfter != 5
            || burst[2].SkillLeveledUp
            || !burst[2].PlayerLeveledUp
            || burst[2].PlayerLevelBefore != 5
            || burst[2].PlayerLevelAfter != 7
            || !Near(burst[2].PlayerBarFillBefore, 0.2f)
            || !Near(burst[2].PlayerBarFillAfter, 0.3f)
            || burst[2].AttributeGains.Count != 3
            || burst[2].AttributeGains[0] != AttributeIds.Strength
            || burst[2].AttributeGains[1] != AttributeIds.Perception
            || burst[2].AttributeGains[2] != AttributeIds.Constitution)
        {
            Assert.Fail("[prosequor] Level-up HUD fixture failed (skill spans then one player step).");
            return;
        }

        List<LevelUpHudPacket> playerOnly = LevelUpHudBurst.Compact(new[]
        {
            Player(3, 4, 0.9f, 0.1f, AttributeIds.Strength),
            Player(4, 6, 0.2f, 0.4f, AttributeIds.Strength, AttributeIds.Constitution)
        });
        if (playerOnly.Count != 1
            || playerOnly[0].SkillLeveledUp
            || !playerOnly[0].PlayerLeveledUp
            || playerOnly[0].PlayerLevelBefore != 3
            || playerOnly[0].PlayerLevelAfter != 6
            || !Near(playerOnly[0].PlayerBarFillBefore, 0.9f)
            || !Near(playerOnly[0].PlayerBarFillAfter, 0.4f)
            || playerOnly[0].AttributeGains.Count != 2
            || playerOnly[0].AttributeGains[1] != AttributeIds.Constitution)
        {
            Assert.Fail("[prosequor] Level-up HUD fixture failed (player-only span).");
        }
    }

    static LevelUpHudPacket Skill(
        string skillId,
        int skillBefore,
        int skillAfter,
        int playerBefore,
        int playerAfter,
        float barBefore,
        float barAfter) =>
        new()
        {
            SkillId = skillId,
            SkillLevelBefore = skillBefore,
            SkillLevelAfter = skillAfter,
            PlayerLevelBefore = playerBefore,
            PlayerLevelAfter = playerAfter,
            PlayerBarFillBefore = barBefore,
            PlayerBarFillAfter = barAfter,
            PlayerLeveledUp = playerAfter > playerBefore,
            AttributeGains = new List<string>()
        };

    static LevelUpHudPacket Player(
        int playerBefore,
        int playerAfter,
        float barBefore,
        float barAfter,
        params string[] attributes) =>
        new()
        {
            PlayerLevelBefore = playerBefore,
            PlayerLevelAfter = playerAfter,
            PlayerBarFillBefore = barBefore,
            PlayerBarFillAfter = barAfter,
            PlayerLeveledUp = playerAfter > playerBefore,
            AttributeGains = new List<string>(attributes)
        };

    static bool Near(float a, float b, float eps = 0.0001f) =>
        Math.Abs(a - b) <= eps;
}
