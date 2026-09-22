using Prosequor;
using Xunit;

namespace Prosequor.Pure.Tests;

/// <summary>Companion <see cref="CompanionSupport.QuerySupport"/> expectations.</summary>
public static class CompanionSupportFixtures
{
    public static void VerifyAll()
    {
        VerifyUnknownAllowed();
        VerifyPlusBlankDenied();
        VerifyPlusOldDenied();
        VerifyPlusMinimumAllowed();
        VerifyPlusNewerAllowed();
        VerifyPlusPrereleaseBelowStableDenied();
        VerifyPlusDenialNamesBothMods();
    }

    static void VerifyUnknownAllowed()
    {
        SupportAnswer missing = CompanionSupport.QuerySupport(null, "1.0.0");
        SupportAnswer blank = CompanionSupport.QuerySupport("", "1.0.0");
        SupportAnswer other = CompanionSupport.QuerySupport("wildcraft", "0.1.0");
        if (!missing.Allowed || missing.Reason.Length != 0
            || !blank.Allowed || blank.Reason.Length != 0
            || !other.Allowed || other.Reason.Length != 0)
        {
            Assert.Fail("[prosequor] Companion support fixture failed (unknown id must be allowed).");
        }
    }

    static void VerifyPlusBlankDenied()
    {
        SupportAnswer nullVersion = CompanionSupport.QuerySupport(CompanionSupport.PlusModId, null);
        SupportAnswer empty = CompanionSupport.QuerySupport(CompanionSupport.PlusModId, "");
        SupportAnswer whitespace = CompanionSupport.QuerySupport(CompanionSupport.PlusModId, "  ");
        if (nullVersion.Allowed || empty.Allowed || whitespace.Allowed)
        {
            Assert.Fail("[prosequor] Companion support fixture failed (blank Plus version must be denied).");
        }
    }

    static void VerifyPlusOldDenied()
    {
        foreach (string version in new[] { "1.0.2", "1.0.1", "1.0.0", "0.9.0" })
        {
            SupportAnswer answer = CompanionSupport.QuerySupport(CompanionSupport.PlusModId, version);
            if (answer.Allowed)
            {
                Assert.Fail(
                    $"[prosequor] Companion support fixture failed ({version} must be denied).");
            }
        }
    }

    static void VerifyPlusMinimumAllowed()
    {
        SupportAnswer answer = CompanionSupport.QuerySupport(
            CompanionSupport.PlusModId,
            CompanionSupport.PlusMinimumVersion);
        if (!answer.Allowed || answer.Reason.Length != 0)
        {
            Assert.Fail("[prosequor] Companion support fixture failed (minimum Plus must be allowed).");
        }
    }

    static void VerifyPlusNewerAllowed()
    {
        foreach (string version in new[] { "1.0.4", "1.1.0", "2.0.0" })
        {
            SupportAnswer answer = CompanionSupport.QuerySupport(CompanionSupport.PlusModId, version);
            if (!answer.Allowed || answer.Reason.Length != 0)
            {
                Assert.Fail(
                    $"[prosequor] Companion support fixture failed ({version} must be allowed).");
            }
        }
    }

    static void VerifyPlusPrereleaseBelowStableDenied()
    {
        SupportAnswer rc = CompanionSupport.QuerySupport(CompanionSupport.PlusModId, "1.0.3-rc.1");
        SupportAnswer pre = CompanionSupport.QuerySupport(CompanionSupport.PlusModId, "1.0.3-pre.1");
        if (rc.Allowed || pre.Allowed)
        {
            Assert.Fail(
                "[prosequor] Companion support fixture failed (1.0.3 prerelease is below 1.0.3).");
        }
    }

    static void VerifyPlusDenialNamesBothMods()
    {
        SupportAnswer answer = CompanionSupport.QuerySupport(CompanionSupport.PlusModId, "1.0.2");
        if (answer.Allowed
            || !answer.Reason.Contains("Prosequor Plus 1.0.2", StringComparison.Ordinal)
            || !answer.Reason.Contains("Update Prosequor Plus", StringComparison.Ordinal)
            || !answer.Reason.Contains(CompanionSupport.PlusMinimumVersion, StringComparison.Ordinal))
        {
            Assert.Fail(
                $"[prosequor] Companion support fixture failed (denial text got '{answer.Reason}').");
        }
    }
}
