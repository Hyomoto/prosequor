using System.Reflection;
using HarmonyLib;
using Prosequor;
using Prosequor.Client;
using Xunit;

namespace Prosequor.Pure.Tests;

/// <summary>
/// <see cref="StatsVitalHover.Lines"/> is empty by default and Harmony postfixes append.
/// </summary>
public class StatsVitalHoverFixtures
{
    [Fact]
    [Trait("Layer", "Harmony")]
    [Trait("Kind", "VitalHover")]
    public void Lines_Unpatched_Should_ReturnEmpty()
    {
        List<string> lines = StatsVitalHover.Lines(StatsVitalHover.Health);
        Assert.Empty(lines);

        lines = StatsVitalHover.Lines(StatsVitalHover.Satiety);
        Assert.Empty(lines);

        lines = StatsVitalHover.Lines(StatsVitalHover.Temperature);
        Assert.Empty(lines);
    }

    [Fact]
    [Trait("Layer", "Harmony")]
    [Trait("Kind", "VitalHover")]
    public void Lines_Postfixes_Should_AppendWithoutReplacing()
    {
        string harmonyId = $"{ProsequorModSystem.ModId}.test.vitalhover.{Guid.NewGuid():N}";
        Harmony harmony = new(harmonyId);
        MethodInfo? target = AccessTools.Method(typeof(StatsVitalHover), nameof(StatsVitalHover.Lines));
        Assert.NotNull(target);

        MethodInfo first = AccessTools.Method(typeof(StatsVitalHoverFixtures), nameof(AppendFirst));
        MethodInfo second = AccessTools.Method(typeof(StatsVitalHoverFixtures), nameof(AppendSecond));
        Assert.NotNull(first);
        Assert.NotNull(second);

        try
        {
            harmony.Patch(target, postfix: new HarmonyMethod(first));
            harmony.Patch(target, postfix: new HarmonyMethod(second));

            List<string> health = StatsVitalHover.Lines(StatsVitalHover.Health);
            Assert.Empty(health);

            List<string> temp = StatsVitalHover.Lines(StatsVitalHover.Temperature);
            Assert.Equal(2, temp.Count);
            Assert.Equal("first-temp", temp[0]);
            Assert.Equal("second-temp", temp[1]);
        }
        finally
        {
            harmony.UnpatchAll(harmonyId);
        }

        Assert.Empty(StatsVitalHover.Lines(StatsVitalHover.Temperature));
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

    static void AppendFirst(string slot, List<string> __result)
    {
        if (slot == StatsVitalHover.Temperature)
        {
            __result.Add("first-temp");
        }
    }

    static void AppendSecond(string slot, List<string> __result)
    {
        if (slot == StatsVitalHover.Temperature)
        {
            __result.Add("second-temp");
        }
    }
}
