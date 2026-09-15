using Prosequor.Ability;
using Xunit;

namespace Prosequor.Pure.Tests;

/// <summary>Null-guard / API smoke for bush planter pedigree (world scenarios cover stamp+persist).</summary>
public static class BushPlanterPedigreeFixtures
{
    public static void VerifyAll()
    {
        VerifyNullGuards();
    }

    static void VerifyNullGuards()
    {
        ProsequorBlockPedigreeStation.StampPlanter(null, "uid");
        ProsequorBlockPedigreeStation.ClearPlanter(null);

        if (ProsequorBlockPedigreeStation.TryGetBushPlanter(null, null, out string? planter)
            || planter != null)
        {
            Assert.Fail("[prosequor] TryGetBushPlanter should fail on null world/pos.");
        }

        if (ProsequorBlockPedigreeStation.TryGetPlanter(null, out planter) || planter != null)
        {
            Assert.Fail("[prosequor] TryGetPlanter should fail on null BE.");
        }
    }
}
