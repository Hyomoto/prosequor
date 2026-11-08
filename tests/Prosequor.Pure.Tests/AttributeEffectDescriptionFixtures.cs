using Prosequor.Client;
using Xunit;

namespace Prosequor.Pure.Tests;

/// <summary>Amount formatting for numeric attribute plate tooltips.</summary>
public class AttributeEffectDescriptionFixtures
{
    [Fact]
    public void FormatSignedPercent_Should_IncludeSign()
    {
        Assert.Equal("+9%", AttributeEffectDescription.FormatSignedPercent(9f));
        Assert.Equal("-5%", AttributeEffectDescription.FormatSignedPercent(-5f));
        Assert.Equal("0%", AttributeEffectDescription.FormatSignedPercent(0f));
        Assert.Equal("+15%", AttributeEffectDescription.FormatSignedPercent(14.6f));
    }

    [Fact]
    public void FormatAbsolutePercent_Should_RoundWithoutSign()
    {
        Assert.Equal("125%", AttributeEffectDescription.FormatAbsolutePercent(125f));
        Assert.Equal("33%", AttributeEffectDescription.FormatAbsolutePercent(33.4f));
        Assert.Equal("100%", AttributeEffectDescription.FormatAbsolutePercent(100f));
    }

    [Fact]
    public void FormatFlatSigned_Should_IncludeSign()
    {
        Assert.Equal("+5", AttributeEffectDescription.FormatFlatSigned(5f));
        Assert.Equal("-3", AttributeEffectDescription.FormatFlatSigned(-3f));
        Assert.Equal("0", AttributeEffectDescription.FormatFlatSigned(0f));
    }

    [Fact]
    public void FormatFlatCount_Should_BeUnsigned()
    {
        Assert.Equal("10", AttributeEffectDescription.FormatFlatCount(10f));
        Assert.Equal("2", AttributeEffectDescription.FormatFlatCount(2.4f));
    }

    [Fact]
    public void FormatMultiplierFactor_Should_UseTimesSign()
    {
        Assert.Equal("×2.5", AttributeEffectDescription.FormatMultiplierFactor(2.5f));
        Assert.Equal("×1", AttributeEffectDescription.FormatMultiplierFactor(1f));
        Assert.Equal("×0.5", AttributeEffectDescription.FormatMultiplierFactor(0.5f));
    }

    [Fact]
    public void FormatAmount_Should_RouteByUnit()
    {
        Assert.Equal(
            "+10%",
            AttributeEffectDescription.FormatAmount(AttributeEffectUnit.MultiplierPercent, 1.1f));
        Assert.Equal(
            "-10%",
            AttributeEffectDescription.FormatAmount(AttributeEffectUnit.MultiplierPercent, 0.9f));
        Assert.Equal(
            "×2.5",
            AttributeEffectDescription.FormatAmount(AttributeEffectUnit.MultiplierFactor, 2.5f));
        Assert.Equal(
            "+15%",
            AttributeEffectDescription.FormatAmount(AttributeEffectUnit.SignedPercent, 15f));
        Assert.Equal(
            "150%",
            AttributeEffectDescription.FormatAmount(AttributeEffectUnit.AbsolutePercent, 150f));
        Assert.Equal(
            "",
            AttributeEffectDescription.FormatAmount(AttributeEffectUnit.Binary, 1f));
    }

    [Fact]
    public void IsDisplayNeutral_Should_UseRoundedResult()
    {
        // Flat health can evaluate off the score-10 neutral but still round to 0.
        Assert.True(
            AttributeEffectDescription.IsDisplayNeutral(
                AttributeEffectUnit.FlatSigned, current: 0f, neutral: 1f));
        Assert.True(
            AttributeEffectDescription.IsDisplayNeutral(
                AttributeEffectUnit.FlatSigned, current: 0.4f, neutral: 1f));
        Assert.False(
            AttributeEffectDescription.IsDisplayNeutral(
                AttributeEffectUnit.FlatSigned, current: 1f, neutral: 0f));

        Assert.True(
            AttributeEffectDescription.IsDisplayNeutral(
                AttributeEffectUnit.AbsolutePercent, current: 100.4f, neutral: 100f));
        Assert.False(
            AttributeEffectDescription.IsDisplayNeutral(
                AttributeEffectUnit.AbsolutePercent, current: 125f, neutral: 100f));

        Assert.True(
            AttributeEffectDescription.IsDisplayNeutral(
                AttributeEffectUnit.SignedPercent, current: 0.4f, neutral: 0f));
        Assert.True(
            AttributeEffectDescription.IsDisplayNeutral(
                AttributeEffectUnit.MultiplierPercent, current: 1.004f, neutral: 1f));
    }
}
