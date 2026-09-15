using Prosequor.Ability;
using Xunit;

namespace Prosequor.Pure.Tests;

/// <summary>Null-guard smoke for still / boiler process-starter helpers.</summary>
public static class BoilerProcessStarterFixtures
{
    public static void VerifyAll()
    {
        VerifyResolveBoilerNullGuards();
        VerifyDistillQualityUidFallsBackToMashMaker();
    }

    static void VerifyResolveBoilerNullGuards()
    {
        if (BoilerProcessStarterPatches.TryResolveBoilerForSourceSlot(null, null, out var boiler)
            || boiler != null)
        {
            Assert.Fail("[prosequor] TryResolveBoilerForSourceSlot should fail on nulls.");
        }
    }

    static void VerifyDistillQualityUidFallsBackToMashMaker()
    {
        string? uid = BoilerProcessStarterPatches.TryGetDistillQualityUid(null, null, "alice");
        if (uid != "alice")
        {
            Assert.Fail("[prosequor] Distill quality uid should fall back to mash MakerUid.");
        }

        if (BoilerProcessStarterPatches.TryGetDistillQualityUid(null, null, "  ") != null
            || BoilerProcessStarterPatches.TryGetDistillQualityUid(null, null, null) != null)
        {
            Assert.Fail("[prosequor] Distill quality uid should be null when mash maker is blank.");
        }
    }
}
