using Prosequor.Ability;
using Prosequor.Xp;
using Xunit;

namespace Prosequor.Pure.Tests;

/// <summary>Pure math for Medicine bleed-out, heal, application, triage, and recipe gate.</summary>
public static class MedicineAbilityFixtures
{
    public static void VerifyAll()
    {
        VerifyBleedOutWindow();
        VerifyHealTotalOrder();
        VerifyApplicationDivisor();
        VerifyTriageHoldback();
        VerifyRecipeGate();
        VerifyUsableHealFraction();
        VerifyEffortPayAlias();
    }

    static void VerifyBleedOutWindow()
    {
        // Level 0: rate 1 → unchanged window.
        double left0 = MedicineStation.ScaleBleedOutHoursLeft(0.5, 0.1, 1f);
        if (Math.Abs(left0 - 0.4) > 0.0001)
        {
            Assert.Fail($"[prosequor] Expected bleed-out hours 0.4 at rate 1, got {left0}.");
        }

        // Cap medicine: 50 * 0.005 = 0.25 → rate 1.25 → 0.5 * 1.25 - 0.1 = 0.525.
        double leftCap = MedicineStation.ScaleBleedOutHoursLeft(0.5, 0.1, 1.25f);
        if (Math.Abs(leftCap - 0.525) > 0.0001)
        {
            Assert.Fail($"[prosequor] Expected bleed-out hours 0.525 at rate 1.25, got {leftCap}.");
        }
    }

    static void VerifyHealTotalOrder()
    {
        // type 7 * regen 1.1 * tend 1.25 = 9.625.
        float total = MedicineStation.FoldHealTotal(7f, 1.1f, 1.25f);
        if (Math.Abs(total - 9.625f) > 0.0001f)
        {
            Assert.Fail($"[prosequor] Expected heal total 9.625, got {total}.");
        }

        // Self-use path: tend rate stays 1.
        float self = MedicineStation.FoldHealTotal(7f, 1.1f, 1f);
        if (Math.Abs(self - 7.7f) > 0.0001f)
        {
            Assert.Fail($"[prosequor] Expected self heal 7.7, got {self}.");
        }
    }

    static void VerifyApplicationDivisor()
    {
        float seconds = MedicineStation.FoldApplicationSeconds(3f, 1.1f);
        if (Math.Abs(seconds - (3f / 1.1f)) > 0.0001f)
        {
            Assert.Fail($"[prosequor] Expected application seconds {3f / 1.1f}, got {seconds}.");
        }

        float unchanged = MedicineStation.FoldApplicationSeconds(3f, 1f);
        if (Math.Abs(unchanged - 3f) > 0.0001f)
        {
            Assert.Fail($"[prosequor] Expected unchanged application 3, got {unchanged}.");
        }
    }

    static void VerifyTriageHoldback()
    {
        MedicineStation.ResolveTriageHoldback(
            20f,
            0.10f,
            10f,
            out float start,
            out float regen,
            out float duration);

        if (Math.Abs(start - 18f) > 0.0001f
            || Math.Abs(regen - 2f) > 0.0001f
            || Math.Abs(duration - 10f) > 0.0001f)
        {
            Assert.Fail(
                $"[prosequor] Expected triage start=18 regen=2 duration=10, got {start}/{regen}/{duration}.");
        }

        MedicineStation.ResolveTriageHoldback(
            20f,
            0f,
            10f,
            out float fullStart,
            out float fullRegen,
            out _);
        if (Math.Abs(fullStart - 20f) > 0.0001f || Math.Abs(fullRegen) > 0.0001f)
        {
            Assert.Fail(
                $"[prosequor] Expected triage no-holdback start=20 regen=0, got {fullStart}/{fullRegen}.");
        }
    }

    static void VerifyRecipeGate()
    {
        // No unlock attribute → always available (vanilla recipes).
        if (!MedicineStation.IsRecipeAvailable(null, null))
        {
            Assert.Fail("[prosequor] Null recipe should pass the unlock gate.");
        }
    }

    static void VerifyUsableHealFraction()
    {
        // 8 HP bandage at 9.9/10 → missing 0.1 → 0.1/8 = 0.0125.
        float nearFull = MedicineStation.UsableHealFraction(0.1f, 8f);
        if (Math.Abs(nearFull - 0.0125f) > 0.0001f)
        {
            Assert.Fail($"[prosequor] Expected usable fraction 0.0125, got {nearFull}.");
        }

        float fullNeed = MedicineStation.UsableHealFraction(10f, 8f);
        if (Math.Abs(fullNeed - 1f) > 0.0001f)
        {
            Assert.Fail($"[prosequor] Expected usable fraction 1 when missing >= heal, got {fullNeed}.");
        }

        if (MedicineStation.UsableHealFraction(0f, 8f) != 0f
            || MedicineStation.UsableHealFraction(5f, 0f) != 0f)
        {
            Assert.Fail("[prosequor] Usable fraction should be 0 when missing or capacity is 0.");
        }
    }

    static void VerifyEffortPayAlias()
    {
        if (!XpPayChannels.TryParseName("effort", out XpPayChannel effort, out _)
            || effort != XpPayChannel.Resistance)
        {
            Assert.Fail("[prosequor] pay 'effort' should alias to Resistance.");
        }

        if (!XpPayChannels.TryParseName("resistance", out XpPayChannel resistance, out _)
            || resistance != XpPayChannel.Resistance)
        {
            Assert.Fail("[prosequor] pay 'resistance' should still parse to Resistance.");
        }

        // [0, 1] table at metric 0.01 → 0.01 XP.
        float grant = AmountTableMath.LerpAmount([0f, 1f], 0.01f, 0f, 1f);
        if (Math.Abs(grant - 0.01f) > 0.0001f)
        {
            Assert.Fail($"[prosequor] Expected effort lerp 0.01, got {grant}.");
        }
    }
}
