using Prosequor.Xp;
using Xunit;

namespace Prosequor.Ability;

/// <summary>Pure FatherXp mailbox fixtures (no world required).</summary>
public static class FatherXpFixtures
{
    public static void VerifyAll()
    {
        VerifyRejectsBadEnqueue();
        VerifyCoalescesSameSkill();
        VerifySeparatesPlayersAndSkills();
        VerifyTakeRemovesTab();
        VerifyRoundTrip();
        VerifyCaseInsensitiveKeys();
    }

    static void VerifyRejectsBadEnqueue()
    {
        FatherXpMailbox mailbox = new();
        mailbox.Enqueue("", "clayforming", 1f);
        mailbox.Enqueue("p1", "", 1f);
        mailbox.Enqueue("p1", "clayforming", 0f);
        mailbox.Enqueue("p1", "clayforming", -3f);
        if (mailbox.PlayerCount != 0)
        {
            Assert.Fail("[prosequor] FatherXp fixture failed (bad enqueue should be ignored).");
        }
    }

    static void VerifyCoalescesSameSkill()
    {
        FatherXpMailbox mailbox = new();
        mailbox.Enqueue("p1", "husbandry", 2f);
        mailbox.Enqueue("p1", "husbandry", 0.5f);
        if (!mailbox.TryPeek("p1", "husbandry", out float amount) || !Near(amount, 2.5f))
        {
            Assert.Fail(string.Format("[prosequor] FatherXp fixture failed (coalesce). amount={0}.", amount));
        }
    }

    static void VerifySeparatesPlayersAndSkills()
    {
        FatherXpMailbox mailbox = new();
        mailbox.Enqueue("p1", "husbandry", 1f);
        mailbox.Enqueue("p1", "clayforming", 4f);
        mailbox.Enqueue("p2", "husbandry", 9f);
        if (mailbox.PlayerCount != 2
            || !mailbox.TryPeek("p1", "husbandry", out float p1Husbandry)
            || !Near(p1Husbandry, 1f)
            || !mailbox.TryPeek("p1", "clayforming", out float p1Clay)
            || !Near(p1Clay, 4f)
            || !mailbox.TryPeek("p2", "husbandry", out float p2Husbandry)
            || !Near(p2Husbandry, 9f)
            || mailbox.TryPeek("p2", "clayforming", out _))
        {
            Assert.Fail("[prosequor] FatherXp fixture failed (player/skill isolation).");
        }
    }

    static void VerifyTakeRemovesTab()
    {
        FatherXpMailbox mailbox = new();
        mailbox.Enqueue("p1", "clayforming", 3f);
        mailbox.Enqueue("p1", "husbandry", 1f);
        mailbox.Enqueue("p2", "husbandry", 8f);
        if (!mailbox.TryTake("p1", out IReadOnlyList<FatherXpGrant> grants)
            || grants.Count != 2
            || grants[0].SkillId != "clayforming"
            || !Near(grants[0].Amount, 3f)
            || grants[1].SkillId != "husbandry"
            || !Near(grants[1].Amount, 1f)
            || mailbox.TryPeek("p1", "husbandry", out _)
            || mailbox.PlayerCount != 1
            || mailbox.TryTake("p1", out _))
        {
            Assert.Fail("[prosequor] FatherXp fixture failed (take).");
        }
    }

    static void VerifyRoundTrip()
    {
        FatherXpMailbox mailbox = new();
        mailbox.Enqueue("p1", "clayforming", 3f);
        mailbox.Enqueue("p2", "husbandry", 1.25f);
        FatherXpMailbox restored = FatherXpMailbox.Deserialize(mailbox.Serialize());
        if (restored.PlayerCount != 2
            || !restored.TryPeek("p1", "clayforming", out float clay)
            || !Near(clay, 3f)
            || !restored.TryPeek("p2", "husbandry", out float husbandry)
            || !Near(husbandry, 1.25f))
        {
            Assert.Fail("[prosequor] FatherXp fixture failed (round trip).");
        }

        FatherXpMailbox empty = FatherXpMailbox.Deserialize(null);
        if (empty.PlayerCount != 0)
        {
            Assert.Fail("[prosequor] FatherXp fixture failed (empty deserialize).");
        }
    }

    static void VerifyCaseInsensitiveKeys()
    {
        FatherXpMailbox mailbox = new();
        mailbox.Enqueue("AbC", "ClayForming", 1f);
        mailbox.Enqueue("abc", "clayforming", 2f);
        if (!mailbox.TryPeek("ABC", "CLAYFORMING", out float amount) || !Near(amount, 3f))
        {
            Assert.Fail(string.Format("[prosequor] FatherXp fixture failed (case). amount={0}.", amount));
        }
    }

    static bool Near(float a, float b) => Math.Abs(a - b) < 0.0001f;
}
