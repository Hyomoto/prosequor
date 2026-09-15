using Prosequor.Client;
using Xunit;

namespace Prosequor.Progress;

/// <summary>Percent-band qualitative scale (no world required).</summary>
public static class QualitativeStatScaleFixtures
{
    public static void VerifyAll()
    {
        VerifyNeutralOmitted();
        VerifyScreenshotPercents();
        VerifyClampStillGreat();
        VerifyHungerPolarity();
        VerifyCatalog();
    }

    static void VerifyNeutralOmitted()
    {
        if (QualitativeStatScale.TryClassify(1f, true, out _))
        {
            Assert.Fail("[prosequor] Qualitative scale must omit 100%.");
        }

        if (QualitativeStatScale.TryClassify(float.NaN, true, out _))
        {
            Assert.Fail("[prosequor] Qualitative scale must omit non-finite values.");
        }
    }

    static void VerifyScreenshotPercents()
    {
        // Walk 95%: 0.05 / 0.60 < 1/3 → slight negative.
        Expect(0.95f, higherIsBetter: true, positive: false, QualitativeStatTier.Slight, "walk 95%");
        // Heal 49%: 0.51 / 0.60 > 2/3 → great negative.
        Expect(0.49f, higherIsBetter: true, positive: false, QualitativeStatTier.Great, "heal 49%");
        // Hunger 181% (higher worse): 0.81 / 1.00 > 2/3 → great negative.
        Expect(1.81f, higherIsBetter: false, positive: false, QualitativeStatTier.Great, "hunger 181%");
        // Charge 58%: 0.42 / 0.60 > 2/3 → great negative.
        Expect(0.58f, higherIsBetter: true, positive: false, QualitativeStatTier.Great, "charge 58%");
    }

    static void VerifyClampStillGreat()
    {
        Expect(0.1f, higherIsBetter: true, positive: false, QualitativeStatTier.Great, "below floor");
        Expect(3f, higherIsBetter: true, positive: true, QualitativeStatTier.Great, "above ceiling");
    }

    static void VerifyHungerPolarity()
    {
        // Lower hunger rate is a benefit.
        Expect(0.5f, higherIsBetter: false, positive: true, QualitativeStatTier.Great, "hunger 50%");
        if (!BlendedStatDescription.TryGetHigherIsBetter(BlendedStatDescription.HungerRate, out bool higherIsBetter)
            || higherIsBetter)
        {
            Assert.Fail("[prosequor] Hunger rate must be catalogued as higher-is-worse.");
        }
    }

    static void VerifyCatalog()
    {
        if (!BlendedStatDescription.CoversEntityStat(BlendedStatDescription.WalkSpeed)
            || !BlendedStatDescription.CoversEntityStat(BlendedStatDescription.HealingEffectiveness)
            || !BlendedStatDescription.CoversEntityStat(BlendedStatDescription.HungerRate)
            || !BlendedStatDescription.CoversEntityStat(BlendedStatDescription.RangedWeaponsSpeed))
        {
            Assert.Fail("[prosequor] Blended catalog is missing a Physical stat.");
        }

        if (BlendedStatDescription.CoversEntityStat("rangedWeaponsAcc"))
        {
            Assert.Fail("[prosequor] Ranged accuracy stays on the perception attribute line.");
        }
    }

    static void Expect(
        float value,
        bool higherIsBetter,
        bool positive,
        QualitativeStatTier tier,
        string label)
    {
        if (!QualitativeStatScale.TryClassify(value, higherIsBetter, out QualitativeStatScale.Band band))
        {
            Assert.Fail(string.Format("[prosequor] Qualitative scale omitted {0}.", label));
        }

        if (band.Positive != positive || band.Tier != tier)
        {
            Assert.Fail(string.Format(
                "[prosequor] Qualitative scale {0}: got {1}/{2}, want {3}/{4}.",
                label,
                band.Positive ? "pos" : "neg",
                band.Tier,
                positive ? "pos" : "neg",
                tier));
        }
    }
}
