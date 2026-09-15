using Prosequor.Ability;
using Xunit;

namespace Prosequor.Pure.Tests;

/// <summary>Null-guard / API smoke for crop planter pedigree (world scenarios cover stamp+persist).</summary>
public static class CropPlanterPedigreeFixtures
{
    public static void VerifyAll()
    {
        VerifyNullGuards();
    }

    static void VerifyNullGuards()
    {
        ProsequorBlockPedigreeStation.StampPlanter(null, "uid");
        ProsequorBlockPedigreeStation.StampPlanter(null, null);
        ProsequorBlockPedigreeStation.StampPlanter(null, "  ");
        ProsequorBlockPedigreeStation.ClearPlanter(null);
        ProsequorBlockPedigreeStation.ClearCropPlanterAt(null, null);

        if (ProsequorBlockPedigreeStation.TryGetCropPlanter(null, null, out string? planter)
            || planter != null)
        {
            Assert.Fail("[prosequor] TryGetCropPlanter should fail on null world/pos.");
        }
    }
}
