using Xunit;

namespace Prosequor.Progress;

/// <summary>Skill-waiting HUD arm/disarm/visibility checks (no world required).</summary>
public static class SkillWaitingHintFixtures
{
    public static void VerifyAll()
    {
        VerifyFirstSeenWithPoints();
        VerifyLeadingZerosDoNotLockFirstSeen();
        VerifyFirstSeenZeroThenIncrease();
        VerifyIncreaseAfterDismiss();
        VerifyDecreaseDoesNotArm();
        VerifyConsumeOpen();
        VerifyVisibleGates();
        VerifyLoginObserveLeavesHintNull();
        VerifySkillLevelUpSetsAndOverridesHint();
        VerifyConsumeOpenClearsHint();
    }

    static void VerifyFirstSeenWithPoints()
    {
        SkillWaitingHintState state = new();
        state.Observe(3);
        if (!state.Armed || !state.IsVisible(3, characterMenuOpen: false))
        {
            Assert.Fail("[prosequor] Skill-waiting fixture failed (first-seen with points should arm).");
        }
    }

    static void VerifyLeadingZerosDoNotLockFirstSeen()
    {
        SkillWaitingHintState state = new();
        state.Observe(0);
        state.Observe(0);
        if (state.Seen || state.Armed)
        {
            Assert.Fail("[prosequor] Skill-waiting fixture failed (leading zeros must not lock first-seen).");
            return;
        }

        state.Observe(2);
        if (!state.Seen || !state.Armed || state.LastPoints != 2)
        {
            Assert.Fail("[prosequor] Skill-waiting fixture failed (first positive after zeros should arm).");
        }
    }

    static void VerifyFirstSeenZeroThenIncrease()
    {
        // Legacy path name: zeros are ignored until the first positive reading.
        SkillWaitingHintState state = new();
        state.Observe(0);
        if (state.Armed || state.IsVisible(0, characterMenuOpen: false))
        {
            Assert.Fail("[prosequor] Skill-waiting fixture failed (first-seen with 0 should stay disarmed).");
            return;
        }

        state.Observe(1);
        if (!state.Armed || !state.IsVisible(1, characterMenuOpen: false))
        {
            Assert.Fail("[prosequor] Skill-waiting fixture failed (0→1 should arm).");
        }
    }

    static void VerifyIncreaseAfterDismiss()
    {
        SkillWaitingHintState state = new();
        state.Observe(2);
        state.ConsumeOpen(out _);
        if (state.Armed)
        {
            Assert.Fail("[prosequor] Skill-waiting fixture failed (ConsumeOpen should disarm).");
            return;
        }

        state.Observe(3);
        if (!state.Armed)
        {
            Assert.Fail("[prosequor] Skill-waiting fixture failed (increase after dismiss should re-arm).");
        }
    }

    static void VerifyDecreaseDoesNotArm()
    {
        SkillWaitingHintState state = new();
        state.Observe(5);
        state.ConsumeOpen(out _);
        state.Observe(2);
        if (state.Armed)
        {
            Assert.Fail("[prosequor] Skill-waiting fixture failed (decrease must not arm).");
        }
    }

    static void VerifyConsumeOpen()
    {
        SkillWaitingHintState state = new();
        if (state.ConsumeOpen(out _))
        {
            Assert.Fail("[prosequor] Skill-waiting fixture failed (ConsumeOpen while disarmed should be false).");
            return;
        }

        state.Observe(1);
        if (!state.ConsumeOpen(out _) || state.Armed)
        {
            Assert.Fail("[prosequor] Skill-waiting fixture failed (ConsumeOpen while armed should return true and clear).");
            return;
        }

        if (state.ConsumeOpen(out _))
        {
            Assert.Fail("[prosequor] Skill-waiting fixture failed (second ConsumeOpen should be false).");
        }
    }

    static void VerifyVisibleGates()
    {
        SkillWaitingHintState state = new();
        state.Observe(4);
        if (!state.IsVisible(4, characterMenuOpen: false))
        {
            Assert.Fail("[prosequor] Skill-waiting fixture failed (armed + points + menu closed should show).");
            return;
        }

        if (state.IsVisible(4, characterMenuOpen: true))
        {
            Assert.Fail("[prosequor] Skill-waiting fixture failed (menu open should hide).");
            return;
        }

        if (state.IsVisible(0, characterMenuOpen: false))
        {
            Assert.Fail("[prosequor] Skill-waiting fixture failed (zero points should hide even when armed).");
        }
    }

    static void VerifyLoginObserveLeavesHintNull()
    {
        SkillWaitingHintState state = new();
        state.Observe(2);
        if (state.SkillHint != null)
        {
            Assert.Fail("[prosequor] Skill-waiting fixture failed (login Observe must leave SkillHint null).");
        }
    }

    static void VerifySkillLevelUpSetsAndOverridesHint()
    {
        SkillWaitingHintState state = new();
        state.Observe(1);
        state.NoteSkillLevelUp("logging");
        if (!state.Armed || state.SkillHint != "logging")
        {
            Assert.Fail("[prosequor] Skill-waiting fixture failed (skill level-up should arm and set hint).");
            return;
        }

        state.NoteSkillLevelUp("digging");
        if (state.SkillHint != "digging")
        {
            Assert.Fail("[prosequor] Skill-waiting fixture failed (later skill level-up should override hint).");
        }
    }

    static void VerifyConsumeOpenClearsHint()
    {
        SkillWaitingHintState state = new();
        state.NoteSkillLevelUp("forestry");
        if (!state.ConsumeOpen(out string? open) || open != "forestry" || state.SkillHint != null || state.Armed)
        {
            Assert.Fail("[prosequor] Skill-waiting fixture failed (ConsumeOpen should return and clear skill hint).");
        }
    }
}
