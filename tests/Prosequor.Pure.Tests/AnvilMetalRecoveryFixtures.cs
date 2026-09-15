using Prosequor.Ability;
using Xunit;

namespace Prosequor.Pure.Tests;

/// <summary>Pure Metal Recovery counter math (no world / anvil).</summary>
public static class AnvilMetalRecoveryFixtures
{
    public static void VerifyAll()
    {
        Advance_Should_NotRefundBelowThreshold();
        Advance_Should_RefundAndResetAtThreshold();
        Advance_Should_NoOpWhenThresholdOff();
    }

    static void Advance_Should_NotRefundBelowThreshold()
    {
        bool refund = AnvilMetalRecoveryOps.TryAdvanceSplit(4, threshold: 6, out int next);
        if (refund || next != 5)
        {
            Assert.Fail($"[prosequor] Expected no refund at 4→5/6. refund={refund} next={next}.");
        }
    }

    static void Advance_Should_RefundAndResetAtThreshold()
    {
        bool refund = AnvilMetalRecoveryOps.TryAdvanceSplit(5, threshold: 6, out int next);
        if (!refund || next != 0)
        {
            Assert.Fail($"[prosequor] Expected refund+reset at 5→6/6. refund={refund} next={next}.");
        }
    }

    static void Advance_Should_NoOpWhenThresholdOff()
    {
        bool refund = AnvilMetalRecoveryOps.TryAdvanceSplit(9, threshold: 0, out int next);
        if (refund || next != 9)
        {
            Assert.Fail($"[prosequor] Threshold 0 must leave count unchanged. refund={refund} next={next}.");
        }
    }
}
