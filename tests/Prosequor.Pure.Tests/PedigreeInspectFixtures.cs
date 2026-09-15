using Prosequor.Ability;
using Prosequor.Commands;
using Xunit;

namespace Prosequor.Pure.Tests;

/// <summary>Name mapping and empty-dump contract for <c>/prosequor pedigree</c>.</summary>
public static class PedigreeInspectFixtures
{
    public static void VerifyAll()
    {
        VerifyNameMapConvertsPlayerUids();
        VerifySentinelUidsStayRaw();
        VerifyMissingMakerAndContributorsSayNone();
    }

    static void VerifyNameMapConvertsPlayerUids()
    {
        ProsequorBlob blob = new(
            "uid-ada",
            new[] { new ProsequorBlob.Share("uid-ada", 2), new ProsequorBlob.Share("uid-bob", 1) },
            recipe: "cider");
        string dump = PedigreeInspect.FormatBlob(blob, NameOf);
        if (dump.IndexOf("maker Ada", StringComparison.Ordinal) < 0
            || dump.IndexOf("contributors Ada 2, Bob 1", StringComparison.Ordinal) < 0
            || dump.IndexOf("recipe cider", StringComparison.Ordinal) < 0
            || dump.IndexOf("uid-ada", StringComparison.Ordinal) >= 0)
        {
            Assert.Fail($"[prosequor] Pedigree inspect should map player UIDs to names. dump={dump}");
        }
    }

    static void VerifySentinelUidsStayRaw()
    {
        ProsequorBlob blob = new(
            "uid-ada",
            new[] { new ProsequorBlob.Share(HusbandryFriendliness.AnonContributorUid, 4) },
            recipe: null);
        string dump = PedigreeInspect.FormatBlob(blob, NameOf);
        if (dump.IndexOf("maker Ada", StringComparison.Ordinal) < 0
            || dump.IndexOf(HusbandryFriendliness.AnonContributorUid + " 4", StringComparison.Ordinal) < 0)
        {
            Assert.Fail($"[prosequor] Pedigree inspect should leave sentinel UIDs raw. dump={dump}");
        }
    }

    static void VerifyMissingMakerAndContributorsSayNone()
    {
        ProsequorBlob blob = new ProsequorBlob(null, Array.Empty<ProsequorBlob.Share>(), recipe: null)
            .WithQualityRank(4);
        string dump = PedigreeInspect.FormatBlob(blob, NameOf);
        if (dump.IndexOf("maker none", StringComparison.Ordinal) < 0
            || dump.IndexOf("contributors none", StringComparison.Ordinal) < 0
            || dump.IndexOf("quality 4", StringComparison.Ordinal) < 0)
        {
            Assert.Fail($"[prosequor] Pedigree inspect should print none for missing attribution. dump={dump}");
        }
    }

    static string NameOf(string uid) => uid switch
    {
        "uid-ada" => "Ada",
        "uid-bob" => "Bob",
        _ => uid
    };
}
