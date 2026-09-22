using Prosequor;
using Xunit;

namespace Prosequor.Pure.Tests;

/// <summary>Optional Plus companion: absent is fine; 1.0.2 and older are not.</summary>
public static class ProsequorPlusCompatFixtures
{
    public static void VerifyAll()
    {
        VerifyAbsentPasses();
        VerifyBlankVersionFails();
        VerifyOldFails();
        VerifyMinimumPasses();
        VerifyNewerPasses();
        VerifyPrereleaseBelowStableFails();
        VerifyErrorNamesBothMods();
    }

    static void VerifyAbsentPasses()
    {
        if (!ProsequorPlusCompat.IsCompatible(loaded: false, version: null)
            || !ProsequorPlusCompat.IsCompatible(loaded: false, version: "1.0.0"))
        {
            Assert.Fail("[prosequor] Plus compat fixture failed (absent Plus must pass).");
        }
    }

    static void VerifyBlankVersionFails()
    {
        if (ProsequorPlusCompat.IsCompatible(loaded: true, version: null)
            || ProsequorPlusCompat.IsCompatible(loaded: true, version: "")
            || ProsequorPlusCompat.IsCompatible(loaded: true, version: "  "))
        {
            Assert.Fail("[prosequor] Plus compat fixture failed (blank Plus version must fail).");
        }
    }

    static void VerifyOldFails()
    {
        if (ProsequorPlusCompat.IsCompatible(true, "1.0.2")
            || ProsequorPlusCompat.IsCompatible(true, "1.0.1")
            || ProsequorPlusCompat.IsCompatible(true, "1.0.0")
            || ProsequorPlusCompat.IsCompatible(true, "0.9.0"))
        {
            Assert.Fail("[prosequor] Plus compat fixture failed (1.0.2 and older must fail).");
        }
    }

    static void VerifyMinimumPasses()
    {
        if (!ProsequorPlusCompat.IsCompatible(true, ProsequorPlusCompat.MinimumVersion))
        {
            Assert.Fail("[prosequor] Plus compat fixture failed (minimum version must pass).");
        }
    }

    static void VerifyNewerPasses()
    {
        if (!ProsequorPlusCompat.IsCompatible(true, "1.0.4")
            || !ProsequorPlusCompat.IsCompatible(true, "1.1.0")
            || !ProsequorPlusCompat.IsCompatible(true, "2.0.0"))
        {
            Assert.Fail("[prosequor] Plus compat fixture failed (newer Plus must pass).");
        }
    }

    static void VerifyPrereleaseBelowStableFails()
    {
        if (ProsequorPlusCompat.IsCompatible(true, "1.0.3-rc.1")
            || ProsequorPlusCompat.IsCompatible(true, "1.0.3-pre.1"))
        {
            Assert.Fail("[prosequor] Plus compat fixture failed (1.0.3 prerelease is below 1.0.3).");
        }
    }

    static void VerifyErrorNamesBothMods()
    {
        string message = ProsequorPlusCompat.FormatError("1.0.2");
        if (!message.Contains("Prosequor Plus 1.0.2", StringComparison.Ordinal)
            || !message.Contains("Update Prosequor Plus", StringComparison.Ordinal)
            || !message.Contains(ProsequorPlusCompat.MinimumVersion, StringComparison.Ordinal))
        {
            Assert.Fail($"[prosequor] Plus compat fixture failed (error text got '{message}').");
        }
    }
}
