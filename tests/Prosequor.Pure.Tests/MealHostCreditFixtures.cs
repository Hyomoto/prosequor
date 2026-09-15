using System.Text;
using Prosequor.Ability;
using Xunit;

namespace Prosequor.Pure.Tests;

/// <summary>Vessel maker stays. Cook is a contributor and leaves with the meal.</summary>
public static class MealHostCreditFixtures
{
    public static void VerifyAll()
    {
        VerifyAttachKeepsVesselMakerAndDoesNotDoubleStamp();
        VerifyAnonymousAttachDoesNotPromoteCookToMaker();
        VerifyDetachKeepsMakerAndDropsCookAndQuality();
        VerifyPreparedByKeepsCookWhoIsAlsoMaker();
        VerifyPreparedBySitsAboveNutritionFacts();
    }

    static void VerifyAttachKeepsVesselMakerAndDoesNotDoubleStamp()
    {
        ProsequorBlob source = ProsequorBlob.Empty
            .WithMaker("potter-a")
            .WithContributor("cook-b", 1)
            .WithQualityRank(3)
            .WithMods([new ProsequorBlob.ModFactor("satiety", 1.25f)]);
        ProsequorBlob dest = ProsequorBlob.Empty.WithMaker("potter-c");

        ProsequorBlob merged = MealHostCredit.AttachMeal(dest, source, "potter-c");
        Assert.Equal("potter-c", merged.MakerUid);
        Assert.Equal(3, merged.QualityRank);
        Assert.Equal(1.25f, Factor(merged, "satiety"));
        Assert.Equal(1, Weight(merged, "cook-b"));
        Assert.False(MealHostCredit.HasContributor(merged, "potter-a"));

        ProsequorBlob again = MealHostCredit.AttachMeal(merged, source, "potter-c");
        Assert.Equal(1, Weight(again, "cook-b"));
        Assert.Equal("potter-c", again.MakerUid);
    }

    static void VerifyAnonymousAttachDoesNotPromoteCookToMaker()
    {
        ProsequorBlob source = ProsequorBlob.Empty
            .WithContributor("cook-b", 1)
            .WithQualityRank(2);
        ProsequorBlob merged = MealHostCredit.AttachMeal(ProsequorBlob.Empty, source, vesselMaker: null);
        Assert.True(string.IsNullOrEmpty(merged.MakerUid));
        Assert.Equal(1, Weight(merged, "cook-b"));
        Assert.Equal(2, merged.QualityRank);
    }

    static void VerifyDetachKeepsMakerAndDropsCookAndQuality()
    {
        ProsequorBlob filled = ProsequorBlob.Empty
            .WithMaker("potter-a")
            .WithContributor("cook-b", 1)
            .WithQualityRank(3)
            .WithMods([new ProsequorBlob.ModFactor("satiety", 1.25f)]);
        ProsequorBlob emptied = MealHostCredit.DetachMeal(filled);
        Assert.Equal("potter-a", emptied.MakerUid);
        Assert.False(MealHostCredit.HasContributor(emptied, "cook-b"));
        Assert.Equal(0, emptied.QualityRank);
        Assert.Empty(emptied.Mods);
        Assert.Empty(emptied.Affixes);

        ProsequorBlob anonymous = MealHostCredit.DetachMeal(
            ProsequorBlob.Empty.WithContributor("cook-b", 1).WithQualityRank(2));
        Assert.True(string.IsNullOrEmpty(anonymous.MakerUid));
        Assert.False(anonymous.HasPersistable);
    }

    static void VerifyPreparedByKeepsCookWhoIsAlsoMaker()
    {
        ProsequorBlob blob = ProsequorBlob.Empty
            .WithMaker("potter-c")
            .WithContributor("potter-c", 1)
            .WithContributor("cook-b", 1)
            .WithContributor("@grid", 1);

        string line = OwnerCredit.AppendPreparedBy("body", world: null, blob);
        Assert.Contains("potter-c", line);
        Assert.Contains("cook-b", line);
        Assert.DoesNotContain("@grid", line);
        Assert.True(
            line.Contains("Prepared By", StringComparison.Ordinal)
                || line.Contains("prosequor:prepared-by", StringComparison.Ordinal),
            line);
    }

    static void VerifyPreparedBySitsAboveNutritionFacts()
    {
        var dsc = new StringBuilder();
        dsc.AppendLine("Material: Ceramic");
        dsc.AppendLine("1 serving of Hearty Carrot stew");
        dsc.AppendLine("Nutrition Facts");
        dsc.AppendLine("- Vegetable: 450 sat.");
        dsc.AppendLine("Perishable.");

        ProsequorBlob blob = ProsequorBlob.Empty.WithContributor("cook-b", 1);
        MealTooltipCredit.InsertBeforeNutritionFacts(dsc, world: null, blob);
        string text = dsc.ToString();
        int serving = text.IndexOf("1 serving", StringComparison.Ordinal);
        int prepared = text.IndexOf("Prepared By", StringComparison.Ordinal);
        if (prepared < 0)
        {
            prepared = text.IndexOf("prosequor:prepared-by", StringComparison.Ordinal);
        }

        int nutrition = MealTooltipCredit.IndexOfNutritionFactsLine(text);
        int perishable = text.IndexOf("Perishable", StringComparison.Ordinal);
        Assert.True(serving >= 0 && prepared > serving && nutrition > prepared && perishable > nutrition, text);
    }

    static int Weight(ProsequorBlob blob, string uid)
    {
        foreach (ProsequorBlob.Share share in blob.Contributors)
        {
            if (string.Equals(share.PlayerUid, uid, StringComparison.Ordinal))
            {
                return share.Weight;
            }
        }

        return 0;
    }

    static float Factor(ProsequorBlob blob, string key)
    {
        foreach (ProsequorBlob.ModFactor mod in blob.Mods)
        {
            if (string.Equals(mod.Key, key, StringComparison.Ordinal))
            {
                return mod.Factor;
            }
        }

        return 0f;
    }
}
