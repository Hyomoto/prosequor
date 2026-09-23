using Prosequor.Ability;
using Xunit;

namespace Prosequor.Pure.Tests;

/// <summary>Pure fixtures for the animal alert meter math.</summary>
public static class AnimalAlertMathFixtures
{
    public static void VerifyAll()
    {
        VerifyProximityCurve();
        VerifyIntegratorHoldAndRise();
        VerifyWakeAndCommitLatches();
        VerifyFleeChanceCurve();
        VerifyFleeRollTriggers();
        VerifyFleeBoltAndPerception();
        VerifyTargetSwitchHysteresis();
        VerifyOrdinaryVersusAcute();
        VerifyHerdAlarmFloor();
    }

    static void VerifyProximityCurve()
    {
        const float range = 20f;
        if (Math.Abs(AnimalAlertMath.Proximity(20f, range)) > 0.0001f
            || Math.Abs(AnimalAlertMath.Proximity(25f, range)) > 0.0001f
            || Math.Abs(AnimalAlertMath.Proximity(0f, range) - 1f) > 0.0001f
            || Math.Abs(AnimalAlertMath.Proximity(10f, range) - 0.25f) > 0.0001f
            || Math.Abs(AnimalAlertMath.Proximity(15f, range) - 0.0625f) > 0.0001f
            || Math.Abs(AnimalAlertMath.Proximity(5f, range) - 0.5625f) > 0.0001f)
        {
            Assert.Fail("[prosequor] Alert proximity curve failed.");
        }
    }

    static void VerifyIntegratorHoldAndRise()
    {
        float held = AnimalAlertMath.Integrate(50f, AnimalAlertMath.HoldThreat, 1f);
        if (Math.Abs(held - 50f) > 0.0001f)
        {
            Assert.Fail(string.Format(
                "[prosequor] Alert hold failed. got={0} want=50.",
                held));
        }

        float decayed = AnimalAlertMath.Integrate(50f, 0f, 1f);
        if (Math.Abs(decayed - 45f) > 0.0001f)
        {
            Assert.Fail(string.Format(
                "[prosequor] Alert decay failed. got={0} want=45.",
                decayed));
        }

        float risen = AnimalAlertMath.Integrate(50f, 0.4f, 1f);
        if (Math.Abs(risen - 55f) > 0.0001f)
        {
            Assert.Fail(string.Format(
                "[prosequor] Alert rise failed. got={0} want=55.",
                risen));
        }
    }

    static void VerifyWakeAndCommitLatches()
    {
        if (AnimalAlertMath.UpdateAwake(awake: false, 39.9f)
            || !AnimalAlertMath.UpdateAwake(awake: false, 40f)
            || !AnimalAlertMath.UpdateAwake(awake: true, 25f)
            || AnimalAlertMath.UpdateAwake(awake: true, 24.9f))
        {
            Assert.Fail("[prosequor] Alert wake latch failed.");
        }

        // Committed does not auto-enter at 100; only cools off below CommitExit.
        if (AnimalAlertMath.UpdateCommitted(committed: false, 100f)
            || !AnimalAlertMath.UpdateCommitted(committed: true, 60f)
            || AnimalAlertMath.UpdateCommitted(committed: true, 59.9f))
        {
            Assert.Fail("[prosequor] Alert commit cool-off latch failed.");
        }
    }

    static void VerifyFleeChanceCurve()
    {
        if (Math.Abs(AnimalAlertMath.FleeChance(0f)) > 0.0001f
            || Math.Abs(AnimalAlertMath.FleeChance(50f) - 0.1f) > 0.0001f
            || Math.Abs(AnimalAlertMath.FleeChance(100f) - 1f) > 0.0001f
            || Math.Abs(AnimalAlertMath.FleeChance(200f) - 1f) > 0.0001f
            || AnimalAlertMath.FleeChance(25f) is < 0.04f or > 0.06f)
        {
            Assert.Fail("[prosequor] Flee chance curve [0, 0.1, 1] failed.");
        }
    }

    static void VerifyFleeRollTriggers()
    {
        if (!AnimalAlertMath.ShouldAttemptFleeRoll(
                committed: false,
                alert: 20f,
                previousThreat: 0.1f,
                currentThreat: 0.2f,
                previousBand: AnimalAlertMath.BandStill,
                currentBand: AnimalAlertMath.BandStill,
                hasTarget: true)
            || AnimalAlertMath.ShouldAttemptFleeRoll(
                committed: false,
                alert: 20f,
                previousThreat: 0.2f,
                currentThreat: 0.2f,
                previousBand: AnimalAlertMath.BandStill,
                currentBand: AnimalAlertMath.BandStill,
                hasTarget: true)
            || !AnimalAlertMath.ShouldAttemptFleeRoll(
                committed: false,
                alert: 20f,
                previousThreat: 0.2f,
                currentThreat: 0.2f,
                previousBand: AnimalAlertMath.BandStill,
                currentBand: AnimalAlertMath.BandWalk,
                hasTarget: true)
            || AnimalAlertMath.ShouldAttemptFleeRoll(
                committed: true,
                alert: 80f,
                previousThreat: 0.1f,
                currentThreat: 0.9f,
                previousBand: AnimalAlertMath.BandStill,
                currentBand: AnimalAlertMath.BandSprint,
                hasTarget: true)
            || AnimalAlertMath.ShouldAttemptFleeRoll(
                committed: false,
                alert: 0f,
                previousThreat: 0f,
                currentThreat: 0.5f,
                previousBand: AnimalAlertMath.BandStill,
                currentBand: AnimalAlertMath.BandWalk,
                hasTarget: true))
        {
            Assert.Fail("[prosequor] Flee roll rising-threat / posture gates failed.");
        }
    }

    static void VerifyFleeBoltAndPerception()
    {
        if (Math.Abs(AnimalAlertMath.ApplyFleeBolt(100f) - 200f) > 0.0001f
            || Math.Abs(AnimalAlertMath.ApplyFleeBolt(70f) - 140f) > 0.0001f
            || AnimalAlertMath.ApplyFleeBolt(10f) < AnimalAlertMath.CommitExit
            || AnimalAlertMath.FleePerceptionMult < 1.99f
            || Math.Abs(AnimalAlertMath.EffectiveIncoming(0.5f, spooked: true) - 1f) > 0.0001f
            || AnimalAlertMath.EffectiveIncoming(0.005f, spooked: false) > 0f)
        {
            Assert.Fail("[prosequor] Flee bolt / perception / effective incoming failed.");
        }

        // Threat may exceed 1 (uncapped).
        float hot = AnimalAlertMath.Threat(
            proximity: 1f,
            movementDisturbance: AnimalAlertMath.MovementSprint,
            detectability: 1.8f,
            ordinaryScale: 1f,
            sprint: true);
        if (hot <= 1f)
        {
            Assert.Fail(string.Format(
                "[prosequor] Threat should exceed 1 when emission is hot. got={0}.",
                hot));
        }
    }

    static void VerifyTargetSwitchHysteresis()
    {
        if (!AnimalAlertMath.ShouldSwitchTarget(0f, 0.2f)
            || AnimalAlertMath.ShouldSwitchTarget(0.5f, 0.54f)
            || !AnimalAlertMath.ShouldSwitchTarget(0.5f, 0.56f)
            || AnimalAlertMath.ShouldSwitchTarget(0.5f, 0f))
        {
            Assert.Fail("[prosequor] Alert target switch hysteresis failed.");
        }
    }

    static void VerifyOrdinaryVersusAcute()
    {
        // Wild (scale 1): walk at mid range is moderated by proximity².
        float walk = AnimalAlertMath.Threat(
            proximity: 0.25f,
            movementDisturbance: AnimalAlertMath.MovementWalk,
            detectability: 1f,
            ordinaryScale: 1f,
            sprint: false);
        float walkExpect = 0.25f * AnimalAlertMath.MovementWalk;
        if (Math.Abs(walk - walkExpect) > 0.0001f)
        {
            Assert.Fail(string.Format(
                "[prosequor] Ordinary walk threat out of range. got={0} want={1}.",
                walk,
                walkExpect));
        }

        // Gen-10 (scale 0): walk at mid range stays near zero (no acute until prox>0.5).
        float tameWalk = AnimalAlertMath.Threat(
            proximity: 0.25f,
            movementDisturbance: AnimalAlertMath.MovementWalk,
            detectability: 1f,
            ordinaryScale: 0f,
            sprint: false);
        if (tameWalk > 0.01f)
        {
            Assert.Fail(string.Format(
                "[prosequor] Tame walk should be ~0. got={0}.",
                tameWalk));
        }

        // Gen-10 sprint still produces acute floor pressure.
        float tameSprint = AnimalAlertMath.Threat(
            proximity: 0.25f,
            movementDisturbance: AnimalAlertMath.MovementSprint,
            detectability: 1f,
            ordinaryScale: 0f,
            sprint: true);
        if (tameSprint < 0.05f)
        {
            Assert.Fail(string.Format(
                "[prosequor] Tame sprint should retain acute floor. got={0}.",
                tameSprint));
        }

        float favoriteScale = AnimalAlertMath.OrdinaryScale(
            generationFear: 1f,
            friendliness: 0,
            fleeMult: 1f,
            broodMult: 1f,
            fleeChanceReduction: 0f,
            isFavoriteSeraph: true);
        if (Math.Abs(favoriteScale - 0.5f) > 0.0001f)
        {
            Assert.Fail(string.Format(
                "[prosequor] Favorite ordinary scale failed. got={0}.",
                favoriteScale));
        }
    }

    static void VerifyHerdAlarmFloor()
    {
        if (Math.Abs(AnimalAlertMath.ApplyHerdAlarmFloor(10f) - 70f) > 0.0001f
            || Math.Abs(AnimalAlertMath.ApplyHerdAlarmFloor(80f) - 80f) > 0.0001f
            || Math.Abs(AnimalAlertMath.ApplyDamageCommit(40f) - 100f) > 0.0001f
            || !AnimalAlertMath.MayBroadcastHerd(committedNow: true, wasCommitted: false, propagatedHerd: false)
            || AnimalAlertMath.MayBroadcastHerd(committedNow: true, wasCommitted: false, propagatedHerd: true)
            || AnimalAlertMath.MayBroadcastHerd(committedNow: true, wasCommitted: true, propagatedHerd: false)
            || AnimalAlertMath.MayBroadcastHerd(committedNow: false, wasCommitted: false, propagatedHerd: false))
        {
            Assert.Fail("[prosequor] Alert herd/damage floor / no-rebroadcast failed.");
        }
    }
}
