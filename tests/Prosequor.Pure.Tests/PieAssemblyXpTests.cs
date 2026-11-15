using Prosequor.Xp.Adapters;
using Xunit;

namespace Prosequor.Pure.Tests;

/// <summary>Pie assembly counts the stack that left the slot, whatever that size was.</summary>
public class PieAssemblyXpTests
{
    [Fact]
    [Trait("Layer", "Xp")]
    public void UnitsRemoved_CountsTheDifferenceWhenTheStackStays()
    {
        Assert.Equal(2, PieAssemblyXp.UnitsRemoved("game:dough-spelt", 8, "game:dough-spelt", 6));
        Assert.Equal(5, PieAssemblyXp.UnitsRemoved("game:fruit-redapple", 5, "game:fruit-redapple", 0));
    }

    [Fact]
    [Trait("Layer", "Xp")]
    public void UnitsRemoved_CountsTheWholeStackWhenTheSlotIsEmptiedOrReplaced()
    {
        Assert.Equal(2, PieAssemblyXp.UnitsRemoved("game:dough-spelt", 2, null, 0));
        Assert.Equal(4, PieAssemblyXp.UnitsRemoved("game:dough-spelt", 4, "game:fruit-redapple", 4));
    }

    [Fact]
    [Trait("Layer", "Xp")]
    public void UnitsRemoved_PaysNothingWhenNothingLeft()
    {
        Assert.Equal(0, PieAssemblyXp.UnitsRemoved("game:dough-spelt", 4, "game:dough-spelt", 4));
        Assert.Equal(0, PieAssemblyXp.UnitsRemoved("game:dough-spelt", 0, null, 0));
        Assert.Equal(0, PieAssemblyXp.UnitsRemoved(null, 3, null, 0));
    }
}
