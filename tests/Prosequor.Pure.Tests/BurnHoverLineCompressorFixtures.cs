using Prosequor.Client;
using Xunit;

namespace Prosequor.Pure.Tests;

/// <summary>
/// In-place collapse of vanilla burn temp + duration into one hover line.
/// </summary>
public static class BurnHoverLineCompressorFixtures
{
    public static void VerifyAll()
    {
        VerifyCompressesTempAndDuration();
        VerifyLeavesSingleLineAlone();
        VerifyPreservesNeighborsAndOrder();
        VerifyExtractCaptures();
        VerifyNullSafe();
    }

    static void VerifyCompressesTempAndDuration()
    {
        string desc =
            "Burn temperature: 600°C\n" +
            "Burn duration: 10s";

        string got = BurnHoverLineCompressor.Compress(desc);
        if (got != "Burns for 10s at 600°C")
        {
            Assert.Fail("[prosequor] Burn lines should compress in-place. got=" + got);
        }
    }

    static void VerifyLeavesSingleLineAlone()
    {
        string onlyTemp = "Burn temperature: 600°C\nSome flavor";
        string gotTemp = BurnHoverLineCompressor.Compress(onlyTemp);
        if (gotTemp != onlyTemp)
        {
            Assert.Fail("[prosequor] Temp-only must stay unchanged. got=" + gotTemp);
        }

        string onlyDur = "Burn duration: 10s\nSome flavor";
        string gotDur = BurnHoverLineCompressor.Compress(onlyDur);
        if (gotDur != onlyDur)
        {
            Assert.Fail("[prosequor] Duration-only must stay unchanged. got=" + gotDur);
        }
    }

    static void VerifyPreservesNeighborsAndOrder()
    {
        string desc =
            "Mining Speed: Soil 5x\n" +
            "Burn temperature: 800°C\n" +
            "Burn duration: 48s\n" +
            "\n" +
            "A good firewood.";

        string got = BurnHoverLineCompressor.Compress(desc);
        string expected =
            "Mining Speed: Soil 5x\n" +
            "Burns for 48s at 800°C\n" +
            "\n" +
            "A good firewood.";
        if (got != expected)
        {
            Assert.Fail("[prosequor] Neighbors should stay; burn collapses once. got=" + got);
        }
    }

    static void VerifyExtractCaptures()
    {
        if (!HoverStatLineStripper.TryExtract(
                "Burn temperature: 600°C",
                "Burn temperature: {0}°C",
                out string[] temp)
            || temp.Length != 1
            || temp[0] != "600")
        {
            Assert.Fail("[prosequor] TryExtract should capture burn temperature.");
        }

        if (!HoverStatLineStripper.TryExtract(
                "Burn duration: 10s",
                "Burn duration: {0}s",
                out string[] dur)
            || dur.Length != 1
            || dur[0] != "10")
        {
            Assert.Fail("[prosequor] TryExtract should capture burn duration.");
        }
    }

    static void VerifyNullSafe()
    {
        string empty = BurnHoverLineCompressor.Compress(null);
        if (empty != "")
        {
            Assert.Fail("[prosequor] Compress(null) should return empty string.");
        }
    }
}
