using Vintagestory.API.Common;
using Xunit;

namespace Prosequor.Client;

/// <summary>xUnit checks for create-character description split.</summary>
public static class CreateCharacterClassFixtures
{
    public static void VerifyAll()
    {
        VerifyVanillaTwoPart();
        VerifyVanillaInnerBr();
        VerifyAllclassesTrailingSpacer();
        VerifyNoBreak();
        VerifyTrailingWhitespaceAndCase();
    }

    static void VerifyVanillaTwoPart()
    {
        const string full =
            "<font color=\"#99c9f9\"><i>Leave behind your old life. Crawl into the new world.</i></font>"
            + "<br><br>"
            + "Commoners are generalists from a multitude of backgrounds.";

        AssertSplit(
            "vanilla-two-part",
            full,
            "<font color=\"#99c9f9\"><i>Leave behind your old life. Crawl into the new world.</i></font>",
            "Commoners are generalists from a multitude of backgrounds.");
    }

    static void VerifyVanillaInnerBr()
    {
        // Blackguard-style: single <br> inside the quote, then <br><br> before body.
        const string full =
            "<font color=\"#99c9f9\"><i>Fight if you must. Kill if you must. Don’t look away."
            + "<br>A hard gaze on life will see you through.</i></font>"
            + "<br><br>"
            + "Blackguards are fierce soldiers that thrive in a close-ranged skirmish.";

        AssertSplit(
            "vanilla-inner-br",
            full,
            "<font color=\"#99c9f9\"><i>Fight if you must. Kill if you must. Don’t look away."
            + "<br>A hard gaze on life will see you through.</i></font>",
            "Blackguards are fierce soldiers that thrive in a close-ranged skirmish.");
    }

    static void VerifyAllclassesTrailingSpacer()
    {
        // Allclasses: story + <br> tagline + trailing <br><br> (vanilla trait-list padding).
        const string full =
            "<font color=\"#99c9f9\"><i>Several chemical compounds adorn your shelves.</i></font>"
            + "<font color=\"#c69c29\"><br>You are an Alchemyst - and limitations are yours to redefine.</i></font>"
            + "<br><br>";

        AssertSplit(
            "allclasses-trailing-spacer",
            full,
            "<font color=\"#99c9f9\"><i>Several chemical compounds adorn your shelves.</i></font>"
            + "<font color=\"#c69c29\">",
            "You are an Alchemyst - and limitations are yours to redefine.</i></font>");
    }

    static void VerifyNoBreak()
    {
        const string full = "<font color=\"#99c9f9\"><i>One unbroken paragraph.</i></font>";
        AssertSplit("no-break", full, full, "");
    }

    static void VerifyTrailingWhitespaceAndCase()
    {
        const string full =
            "<font color=\"#99c9f9\"><i>Quote.</i></font>"
            + "<BR><BR>"
            + "Body text."
            + "<BR><BR>  \n\r";

        AssertSplit(
            "trailing-whitespace-case",
            full,
            "<font color=\"#99c9f9\"><i>Quote.</i></font>",
            "Body text.");
    }

    static void AssertSplit(
        string label,
        string full,
        string expectedFlavor,
        string expectedBody)
    {
        CreateCharacterClassTab.SplitCharacterDescText(full, out string flavor, out string body);
        if (!string.Equals(flavor, expectedFlavor, StringComparison.Ordinal)
            || !string.Equals(body, expectedBody, StringComparison.Ordinal))
        {
            Assert.Fail(string.Format("[prosequor] Class-desc split '{0}' failed.\n  flavor got: {1}\n  flavor want: {2}\n  body got: {3}\n  body want: {4}",
                label,
                flavor,
                expectedFlavor,
                body,
                expectedBody));
        }
    }
}
