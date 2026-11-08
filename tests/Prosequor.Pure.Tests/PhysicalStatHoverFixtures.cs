using Prosequor.Client;
using Xunit;

namespace Prosequor.Pure.Tests;

/// <summary>
/// Rounding and slot placement for Stats panel Physical percents.
/// </summary>
public class PhysicalStatHoverFixtures
{
    [Fact]
    public void FormatPercent_Should_MatchVanillaRounding()
    {
        Assert.Equal("100%", PhysicalStatHover.FormatPercent(1f));
        Assert.Equal("86%", PhysicalStatHover.FormatPercent(0.864f));
        Assert.Equal("115%", PhysicalStatHover.FormatPercent(1.15f));
        Assert.Equal("0%", PhysicalStatHover.FormatPercent(0f));
    }

    [Fact]
    public void FormatLine_Should_JoinLabelAndPercent()
    {
        Assert.Equal("Walk speed: 86%", PhysicalStatHover.FormatLine("Walk speed", 0.864f));
    }

    [Fact]
    public void StatIdsFor_Should_PlaceHealthSatietyAndRing()
    {
        Assert.Equal(
            [PhysicalStatHover.StatHealingEffectiveness],
            PhysicalStatHover.StatIdsFor(PhysicalStatHover.Health));
        Assert.Equal(
            [PhysicalStatHover.StatHungerRate],
            PhysicalStatHover.StatIdsFor(PhysicalStatHover.Satiety));
        Assert.Equal(
            [
                PhysicalStatHover.StatWalkSpeed,
                PhysicalStatHover.StatRangedAccuracy,
                PhysicalStatHover.StatRangedChargeSpeed
            ],
            PhysicalStatHover.StatIdsFor(PhysicalStatHover.Ring));
        Assert.Empty(PhysicalStatHover.StatIdsFor("temperature"));
        Assert.Empty(PhysicalStatHover.StatIdsFor(""));
    }

    [Fact]
    public void FormatLines_Should_DropNullAndWhitespace()
    {
        Assert.Null(GuiElementVitalTooltip.FormatLines(null));
        Assert.Null(GuiElementVitalTooltip.FormatLines([]));
        Assert.Null(GuiElementVitalTooltip.FormatLines(["", "  ", null!]));
        Assert.Equal(
            "a\nb",
            GuiElementVitalTooltip.FormatLines(["  a  ", "", "b"]));
    }
}
