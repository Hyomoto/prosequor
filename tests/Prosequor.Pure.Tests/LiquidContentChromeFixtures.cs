using System.Text;
using Prosequor.Ability;
using Vintagestory.API.Common;
using Xunit;

namespace Prosequor.Pure.Tests;

/// <summary>Vessel-contents tooltip chrome: affixes, quality, Created By, insert point.</summary>
public static class LiquidContentChromeFixtures
{
    public static void VerifyAll()
    {
        VerifyEmptyPortionNoChrome();
        VerifyFormatsAffixesQualityAndCreator();
        VerifyInsertAfterFirstLine();
        VerifyInsertAfterSecondLine();
        VerifyInsertDoesNotDuplicate();
    }

    static void VerifyEmptyPortionNoChrome()
    {
        if (LiquidContentChrome.TryFormat(null, null, out string chrome) || chrome.Length > 0)
        {
            Assert.Fail("[prosequor] LiquidContentChrome should be empty on null portion.");
        }

        if (LiquidContentChrome.TryFormat(null, new ItemStack(), out chrome) || chrome.Length > 0)
        {
            Assert.Fail("[prosequor] LiquidContentChrome should be empty on anonymous portion.");
        }
    }

    static void VerifyFormatsAffixesQualityAndCreator()
    {
        ItemStack portion = new();
        if (!ItemAffixes.Add(portion, "intoxication-5", "prosequor:affix-intoxication-5", "#2A4849")
            || !ItemAffixes.Add(portion, "liquor-stock-5", "prosequor:affix-liquor-stock-5", "#B08A4E")
            || !ItemAffixes.SetFront(
                portion,
                ItemAffixes.QualityCode,
                "prosequor:affix-quality-2"))
        {
            Assert.Fail("[prosequor] LiquidContentChrome fixture failed (stamp affixes).");
            return;
        }

        CraftAttribution.StampMakerUid(portion, "alice");
        if (CraftAttribution.TryGetMakerUid(portion) != "alice")
        {
            Assert.Fail("[prosequor] LiquidContentChrome fixture failed (maker stamp).");
            return;
        }

        if (!LiquidContentChrome.TryFormat(null, portion, out string chrome)
            || chrome.IndexOf("affix-intoxication-5", StringComparison.OrdinalIgnoreCase) < 0
            || chrome.IndexOf("affix-liquor-stock-5", StringComparison.OrdinalIgnoreCase) < 0
            || chrome.IndexOf("#2A4849", StringComparison.Ordinal) < 0
            || chrome.IndexOf("affix-quality-2", StringComparison.OrdinalIgnoreCase) < 0)
        {
            Assert.Fail("[prosequor] LiquidContentChrome should include affixes and quality.");
            return;
        }

        bool hasCredit = chrome.IndexOf("alice", StringComparison.Ordinal) >= 0
            || chrome.IndexOf("created-by", StringComparison.OrdinalIgnoreCase) >= 0
            || chrome.IndexOf("Created By", StringComparison.OrdinalIgnoreCase) >= 0;
        if (!hasCredit)
        {
            Assert.Fail("[prosequor] LiquidContentChrome should include Created By for the portion maker.");
        }
    }

    static void VerifyInsertAfterFirstLine()
    {
        StringBuilder sb = new("1 litres of cider\nperish\n");
        LiquidContentChrome.InsertAfterNthLine(sb, 0, 1, "Mild");
        string got = sb.ToString();
        if (got != "1 litres of cider\nMild\nperish\n")
        {
            Assert.Fail("[prosequor] InsertAfterNthLine(1) should sit under the litres line. got=" + got);
        }
    }

    static void VerifyInsertAfterSecondLine()
    {
        string info = "Contents:\n 1 litres of cider\nperish\n";
        LiquidContentChrome.InsertAfterNthLine(ref info, 0, 2, "Mild");
        if (info != "Contents:\n 1 litres of cider\nMild\nperish\n")
        {
            Assert.Fail("[prosequor] InsertAfterNthLine(2) should sit under placed litres. got=" + info);
        }
    }

    static void VerifyInsertDoesNotDuplicate()
    {
        StringBuilder sb = new("1 litres of cider\nMild\n");
        LiquidContentChrome.InsertAfterNthLine(sb, 0, 1, "Mild");
        if (sb.ToString() != "1 litres of cider\nMild\n")
        {
            Assert.Fail("[prosequor] InsertAfterNthLine should no-op when chrome is already present.");
        }
    }
}
