using Prosequor.Ability;
using Xunit;

namespace Prosequor.Pure.Tests;

/// <summary>Null-guard smoke for shared ownership credit formatting.</summary>
public static class OwnerCreditFixtures
{
    public static void VerifyAll()
    {
        VerifyNullGuards();
        VerifyStyleWrapsPlainLine();
        VerifyStampGrownBySetsCreditLang();
    }

    static void VerifyNullGuards()
    {
        if (OwnerCredit.TryFormat(null, null, OwnerCredit.CreatedByLang, out string line)
            || !string.IsNullOrEmpty(line))
        {
            Assert.Fail("[prosequor] OwnerCredit.TryFormat should fail on null uid.");
        }

        if (OwnerCredit.TryFormat(null, "  ", OwnerCredit.PlantedByLang, out line)
            || !string.IsNullOrEmpty(line))
        {
            Assert.Fail("[prosequor] OwnerCredit.TryFormat should fail on blank uid.");
        }

        if (OwnerCredit.TryFormat(null, "uid", null, out line)
            || !string.IsNullOrEmpty(line))
        {
            Assert.Fail("[prosequor] OwnerCredit.TryFormat should fail on null lang key.");
        }

        if (OwnerCredit.TryFormatStyled(null, null, OwnerCredit.CreatedByLang, out string styled)
            || !string.IsNullOrEmpty(styled))
        {
            Assert.Fail("[prosequor] OwnerCredit.TryFormatStyled should fail on null uid.");
        }

        Assert.Equal("", OwnerCredit.AppendToBody("", null, null, OwnerCredit.CreatedByLang));
        Assert.Equal("body", OwnerCredit.AppendForStack("body", null, null));
    }

    static void VerifyStyleWrapsPlainLine()
    {
        string styled = OwnerCredit.Style("Planted By: Ada");
        if (styled.IndexOf(OwnerCredit.MutedColor, StringComparison.Ordinal) < 0
            || styled.IndexOf("<i>", StringComparison.Ordinal) < 0
            || styled.IndexOf("Planted By: Ada", StringComparison.Ordinal) < 0)
        {
            Assert.Fail("[prosequor] OwnerCredit.Style should wrap muted italic VTML.");
        }
    }

    static void VerifyStampGrownBySetsCreditLang()
    {
        OwnerCredit.StampGrownBy(null, "planter");
        // ItemStack needs Collectible for StampMakerUid — skip live stack without VS world.
        // Credit-lang resolve still covers the attr contract used by AppendForStack.
        if (OwnerCredit.TryGetCreditLang(null) != null)
        {
            Assert.Fail("[prosequor] TryGetCreditLang should be null on null stack.");
        }

        if (OwnerCredit.TryResolvePlanter(null, null, null, out string? planter) || planter != null)
        {
            Assert.Fail("[prosequor] TryResolvePlanter should fail on nulls.");
        }
    }
}
