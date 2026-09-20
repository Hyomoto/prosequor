using Xunit;

namespace Prosequor.Ability;

/// <summary>Pure cementation XP helpers (no world required).</summary>
public static class CementationXpFixtures
{
    public static void VerifyAll()
    {
        VerifyRisingEdgePaysOnce();
        VerifyEmptyContributorsPaysNothing();
        VerifyIncompleteClearsPaid();
    }

    static void VerifyRisingEdgePaysOnce()
    {
        bool was = false;
        bool paid = false;
        if (!CementationXpStation.TrySettleRisingEdge(
                processComplete: true,
                hasContributors: true,
                ref was,
                ref paid)
            || !paid
            || !was)
        {
            Assert.Fail("[prosequor] Cementation XP fixture failed (first rising edge).");
        }

        if (CementationXpStation.TrySettleRisingEdge(
                processComplete: true,
                hasContributors: true,
                ref was,
                ref paid))
        {
            Assert.Fail("[prosequor] Cementation XP fixture failed (second tick must not repay).");
        }
    }

    static void VerifyEmptyContributorsPaysNothing()
    {
        bool was = false;
        bool paid = false;
        if (CementationXpStation.TrySettleRisingEdge(
                processComplete: true,
                hasContributors: false,
                ref was,
                ref paid)
            || paid)
        {
            Assert.Fail("[prosequor] Cementation XP fixture failed (anonymous coffin must not pay).");
        }

        if (!was)
        {
            Assert.Fail("[prosequor] Cementation XP fixture failed (anonymous still advances wasComplete).");
        }
    }

    static void VerifyIncompleteClearsPaid()
    {
        bool was = true;
        bool paid = true;
        if (CementationXpStation.TrySettleRisingEdge(
                processComplete: false,
                hasContributors: true,
                ref was,
                ref paid)
            || paid
            || was)
        {
            Assert.Fail("[prosequor] Cementation XP fixture failed (incomplete must clear paid/was).");
        }
    }
}
