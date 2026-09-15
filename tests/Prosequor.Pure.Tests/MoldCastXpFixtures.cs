using Xunit;

namespace Prosequor.Ability;

/// <summary>Pure mold-cast XP helpers (no world required).</summary>
public static class MoldCastXpFixtures
{
    public static void VerifyAll()
    {
        VerifyIngredientsFromFill();
        VerifyRisingEdgePaysOnce();
        VerifyEmptyPourerPaysNothing();
        VerifyEmptiedClearsPaid();
    }

    static void VerifyIngredientsFromFill()
    {
        if (MoldCastXpStation.IngredientsFromFill(0) != 1
            || MoldCastXpStation.IngredientsFromFill(9) != 1
            || MoldCastXpStation.IngredientsFromFill(100) != 10
            || MoldCastXpStation.IngredientsFromFill(900) != 90)
        {
            Assert.Fail("[prosequor] MoldCast XP fixture failed (ingredients from fill).");
        }
    }

    static void VerifyRisingEdgePaysOnce()
    {
        bool was = false;
        bool paid = false;
        if (!MoldCastXpStation.TrySettleRisingEdge(
                full: true,
                hardened: true,
                hasPourer: true,
                ref was,
                ref paid)
            || !paid
            || !was)
        {
            Assert.Fail("[prosequor] MoldCast XP fixture failed (first rising edge).");
        }

        if (MoldCastXpStation.TrySettleRisingEdge(
                full: true,
                hardened: true,
                hasPourer: true,
                ref was,
                ref paid))
        {
            Assert.Fail("[prosequor] MoldCast XP fixture failed (second tick must not repay).");
        }
    }

    static void VerifyEmptyPourerPaysNothing()
    {
        bool was = false;
        bool paid = false;
        if (MoldCastXpStation.TrySettleRisingEdge(
                full: true,
                hardened: true,
                hasPourer: false,
                ref was,
                ref paid)
            || paid)
        {
            Assert.Fail("[prosequor] MoldCast XP fixture failed (anonymous mold must not pay).");
        }

        if (!was)
        {
            Assert.Fail("[prosequor] MoldCast XP fixture failed (anonymous still advances wasHardened).");
        }
    }

    static void VerifyEmptiedClearsPaid()
    {
        bool was = true;
        bool paid = true;
        if (MoldCastXpStation.TrySettleRisingEdge(
                full: false,
                hardened: false,
                hasPourer: true,
                ref was,
                ref paid)
            || paid
            || was)
        {
            Assert.Fail("[prosequor] MoldCast XP fixture failed (empty cavity must clear paid).");
        }

        // Next full harden can pay again.
        if (!MoldCastXpStation.TrySettleRisingEdge(
                full: true,
                hardened: true,
                hasPourer: true,
                ref was,
                ref paid)
            || !paid)
        {
            Assert.Fail("[prosequor] MoldCast XP fixture failed (re-pour after empty must pay).");
        }
    }
}
