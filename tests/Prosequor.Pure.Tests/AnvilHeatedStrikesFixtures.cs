using Prosequor.Ability;
using Vintagestory.API.Datastructures;
using Xunit;

namespace Prosequor.Pure.Tests;

/// <summary>Pure Heated Strikes cooling-debt shrink math.</summary>
public static class AnvilHeatedStrikesFixtures
{
    public static void VerifyAll()
    {
        Shrink_Should_AdvanceLastUpdateByPctOfDebt();
        Shrink_Should_ClampAtNow();
        Shrink_Should_NoOpWhenPctOff();
        Shrink_Should_NoOpWhenMissingLastUpdate();
        Shrink_Should_NoOpWhenNoDebt();
    }

    static TreeAttribute TempWithLastUpdate(double lastUpdate)
    {
        TreeAttribute temp = new();
        temp.SetDouble(AnvilHeatedStrikesOps.LastUpdateKey, lastUpdate);
        temp.SetFloat("temperature", 800f);
        return temp;
    }

    static void Shrink_Should_AdvanceLastUpdateByPctOfDebt()
    {
        TreeAttribute temp = TempWithLastUpdate(10.0);
        bool changed = AnvilHeatedStrikesOps.TryShrinkDebt(temp, nowHours: 20.0, pct: 0.02f);
        double next = temp.GetDouble(AnvilHeatedStrikesOps.LastUpdateKey);
        // debt=10 → +0.2 → ~10.2 (float pct may be slightly under)
        if (!changed || Math.Abs(next - 10.2) > 1e-5)
        {
            Assert.Fail($"[prosequor] Expected 2% debt shrink to 10.2, got changed={changed} next={next}.");
        }
    }

    static void Shrink_Should_ClampAtNow()
    {
        TreeAttribute temp = TempWithLastUpdate(0.0);
        bool changed = AnvilHeatedStrikesOps.TryShrinkDebt(temp, nowHours: 1.0, pct: 2f);
        double next = temp.GetDouble(AnvilHeatedStrikesOps.LastUpdateKey);
        if (!changed || Math.Abs(next - 1.0) > 1e-9)
        {
            Assert.Fail($"[prosequor] Expected clamp at now=1, got changed={changed} next={next}.");
        }
    }

    static void Shrink_Should_NoOpWhenPctOff()
    {
        TreeAttribute temp = TempWithLastUpdate(5.0);
        bool changed = AnvilHeatedStrikesOps.TryShrinkDebt(temp, nowHours: 10.0, pct: 0f);
        if (changed || Math.Abs(temp.GetDouble(AnvilHeatedStrikesOps.LastUpdateKey) - 5.0) > 1e-9)
        {
            Assert.Fail("[prosequor] pct≤0 must leave lastUpdate unchanged.");
        }
    }

    static void Shrink_Should_NoOpWhenMissingLastUpdate()
    {
        TreeAttribute temp = new();
        temp.SetFloat("temperature", 500f);
        bool changed = AnvilHeatedStrikesOps.TryShrinkDebt(temp, nowHours: 10.0, pct: 0.05f);
        if (changed)
        {
            Assert.Fail("[prosequor] Missing temperatureLastUpdate must no-op.");
        }
    }

    static void Shrink_Should_NoOpWhenNoDebt()
    {
        TreeAttribute temp = TempWithLastUpdate(10.0);
        bool changed = AnvilHeatedStrikesOps.TryShrinkDebt(temp, nowHours: 10.0, pct: 0.05f);
        if (changed)
        {
            Assert.Fail("[prosequor] Zero debt must no-op.");
        }
    }
}
