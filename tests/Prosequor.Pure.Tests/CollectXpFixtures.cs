using Prosequor.Xp.Activity;
using Vintagestory.API.Common;
using Xunit;

namespace Prosequor.Xp;

/// <summary>Pure collect-XP stamp + buffer fixtures (no world).</summary>
public static class CollectXpFixtures
{
    public static void VerifyAll()
    {
        VerifyStampSetClear();
        VerifyBufferMergeAndPeek();
        VerifyBufferDiscard();
        VerifyPartialStackKeepsStamp();
        VerifyCollectXpItemActivity();
    }

    static void VerifyStampSetClear()
    {
        ItemStack stack = new() { StackSize = 2 };
        if (CollectXpStamp.Has(stack))
        {
            Assert.Fail("[prosequor] fresh stack should not be collect-XP stamped.");
        }

        CollectXpStamp.Set(stack);
        if (!CollectXpStamp.Has(stack))
        {
            Assert.Fail("[prosequor] Set should stamp collect-XP.");
        }

        CollectXpStamp.Clear(stack);
        if (CollectXpStamp.Has(stack))
        {
            Assert.Fail("[prosequor] Clear should remove collect-XP stamp.");
        }
    }

    static void VerifyBufferMergeAndPeek()
    {
        CollectXpBuffer buffer = new();
        buffer.Enqueue("p1", "game:egg-chicken-raw", 2);
        buffer.Enqueue("p1", "game:egg-chicken-raw", 3);
        buffer.Enqueue("p1", "game:egg-duck-raw", 1);
        if (buffer.Peek("p1", "game:egg-chicken-raw") != 5
            || buffer.Peek("p1", "game:egg-duck-raw") != 1
            || !buffer.HasPending("p1"))
        {
            Assert.Fail("[prosequor] CollectXpBuffer should merge same codes.");
        }
    }

    static void VerifyBufferDiscard()
    {
        CollectXpBuffer buffer = new();
        buffer.Enqueue("p1", "game:egg-chicken-raw", 4);
        buffer.Discard("p1");
        if (buffer.HasPending("p1") || buffer.Peek("p1", "game:egg-chicken-raw") != 0)
        {
            Assert.Fail("[prosequor] Discard should drop the player's collect bag.");
        }
    }

    static void VerifyPartialStackKeepsStamp()
    {
        ItemStack stack = new() { StackSize = 3 };
        CollectXpStamp.Set(stack);
        // Simulate partial give: 1 moved, 2 left — leftover must stay stamped.
        stack.StackSize = 2;
        if (!CollectXpStamp.Has(stack))
        {
            Assert.Fail("[prosequor] partial give leftover should keep collect-XP stamp.");
        }
    }

    static void VerifyCollectXpItemActivity()
    {
        if (CollectXpItem.Activity != "prosequor:collect-xp-item")
        {
            Assert.Fail("[prosequor] collect-xp-item activity id mismatch.");
        }
    }
}
