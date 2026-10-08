using Prosequor.Ability;
using Vintagestory.API.MathTools;
using Xunit;

namespace Prosequor.Pure.Tests;

/// <summary>Tree-fell position scope used to tag mutate-drops facts. No world required.</summary>
public class TreeFellScopeTests
{
    [Fact]
    [Trait("Layer", "Ability")]
    public void Note_OutsideBegin_DoesNotMark()
    {
        BlockPos pos = new(1, 2, 3, 0);
        try
        {
            TreeFellScope.Note([pos]);
            Assert.False(TreeFellScope.Contains(pos));
        }
        finally
        {
            TreeFellScope.End();
        }
    }

    [Fact]
    [Trait("Layer", "Ability")]
    public void Contains_MatchesNotedPositionByValue()
    {
        BlockPos noted = new(4, 64, 8, 0);
        BlockPos same = new(4, 64, 8, 0);
        BlockPos other = new(4, 65, 8, 0);
        TreeFellScope.Begin();
        try
        {
            TreeFellScope.Note([noted]);
            Assert.True(TreeFellScope.Contains(same));
            Assert.False(TreeFellScope.Contains(other));
            Assert.False(TreeFellScope.Contains(null));
        }
        finally
        {
            TreeFellScope.End();
        }

        Assert.False(TreeFellScope.Contains(same));
    }

    [Fact]
    [Trait("Layer", "Ability")]
    public void NestedScope_HidesOuterPositionsUntilInnerEnds()
    {
        BlockPos outer = new(1, 1, 1, 0);
        BlockPos inner = new(2, 2, 2, 0);
        TreeFellScope.Begin();
        try
        {
            TreeFellScope.Note([outer]);
            TreeFellScope.Begin();
            try
            {
                TreeFellScope.Note([inner]);
                Assert.True(TreeFellScope.Contains(inner));
                Assert.False(TreeFellScope.Contains(outer));
            }
            finally
            {
                TreeFellScope.End();
            }

            Assert.True(TreeFellScope.Contains(outer));
            Assert.False(TreeFellScope.Contains(inner));
        }
        finally
        {
            TreeFellScope.End();
        }
    }
}
