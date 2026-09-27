using Prosequor.Ability;
using Xunit;

namespace Prosequor.Pure.Tests;

/// <summary>Pure Focus Shot threshold: a short draw stays vanilla, a full hold scales, an unowned node does not.</summary>
public static class FocusShotFixtures
{
    public static void VerifyAll()
    {
        VerifyShortDrawUnchanged();
        VerifyFullHoldScales();
        VerifyMissingUnlockUnchanged();
    }

    static void VerifyShortDrawUnchanged()
    {
        float damage = FocusShot.ScaleDamage(10f, secondsUsed: 4.9f, holdSeconds: 5f, factor: 1.40f);
        if (damage != 10f)
        {
            Assert.Fail("[prosequor] A 4.9s draw of a 5s hold should leave damage at 10.");
        }
    }

    static void VerifyFullHoldScales()
    {
        float damage = FocusShot.ScaleDamage(10f, secondsUsed: 5f, holdSeconds: 5f, factor: 1f + 0.40f);
        if (MathF.Abs(damage - 14f) > 0.001f)
        {
            Assert.Fail("[prosequor] A 5s draw at 0.40 scale should turn 10 damage into 14.");
        }
    }

    static void VerifyMissingUnlockUnchanged()
    {
        float damage = FocusShot.ScaleDamage(10f, secondsUsed: 5f, holdSeconds: 0f, factor: 1f);
        if (damage != 10f)
        {
            Assert.Fail("[prosequor] A missing Focus Shot unlock should leave damage at 10.");
        }
    }
}
