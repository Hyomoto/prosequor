using Vintagestory.API.MathTools;

namespace Prosequor.Ability;

/// <summary>
/// Pure alert-meter math: proximity, threat, integrator, flee chance, and latches.
/// No world types — safe for pure fixtures.
/// </summary>
public static class AnimalAlertMath
{
    public const float AlertMin = 0f;

    /// <summary>Headroom for flee-bolt doubling (and stacked shocks).</summary>
    public const float AlertMax = 400f;

    public const float WakeEnter = 40f;
    public const float WakeExit = 25f;

    /// <summary>Reference full meter for flee-chance curve (p→1). Not an auto-flee gate.</summary>
    public const float CommitEnter = 100f;

    /// <summary>While fleeing, drop out of committed when alert cools below this.</summary>
    public const float CommitExit = 60f;

    /// <summary>Integrator: <c>alert += (threat * RiseGain - Decay) * dt</c>.</summary>
    public const float RiseGain = 25f;

    /// <summary>Threat that holds alert steady (<c>0.20 * 25 = 5</c>).</summary>
    public const float Decay = 5f;

    public const float HoldThreat = Decay / RiseGain;

    public const float MovementStill = 1.0f;
    public const float MovementSneak = 0.75f;
    public const float MovementWalk = 1.5f;
    public const float MovementSprint = 3.0f;

    public const float FavoriteOrdinaryFactor = 0.5f;

    /// <summary>Switch target when new threat exceeds current by this ratio.</summary>
    public const float TargetSwitchRatio = 1.10f;

    /// <summary>Minimum acute share of base threat when ordinary sensitivity is ~0.</summary>
    public const float AcuteFloorFraction = 0.4f;

    public const float HerdAlarmFloor = 70f;

    /// <summary>While committed/fleeing, perceived threat is scaled by this.</summary>
    public const float FleePerceptionMult = 2f;

    /// <summary>While spooked, sense radius is scaled by this (sensitivity).</summary>
    public const float FleeSenseMult = 2f;

    /// <summary>Meter sense radius floor (blocks).</summary>
    public const float MinCharacteristicRange = 30f;

    /// <summary>Incoming below this is treated as zero for decay (noise floor).</summary>
    public const float IncomingEpsilon = 0.01f;

    public const int BandStill = 0;
    public const int BandSneak = 1;
    public const int BandWalk = 2;
    public const int BandSprint = 3;

    public static float SenseRange(float characteristicRange, bool spooked) =>
        Math.Max(1f, characteristicRange) * (spooked ? FleeSenseMult : 1f);

    /// <summary>
    /// Apply spooked perception to raw incoming threat. Spooked ⇒ ×FleePerceptionMult.
    /// </summary>
    public static float EffectiveIncoming(float rawThreat, bool spooked)
    {
        if (rawThreat <= IncomingEpsilon)
        {
            return 0f;
        }

        float threat = spooked ? rawThreat * FleePerceptionMult : rawThreat;
        return Math.Max(0f, threat);
    }

    public static float Proximity(float distance, float characteristicRange)
    {
        if (characteristicRange <= 0f || distance >= characteristicRange)
        {
            return 0f;
        }

        if (distance <= 0f)
        {
            return 1f;
        }

        float t = 1f - distance / characteristicRange;
        return t * t;
    }

    public static float MovementDisturbance(bool moving, bool sneak, bool sprint)
    {
        if (sprint && moving)
        {
            return MovementSprint;
        }

        if (sneak && moving)
        {
            return MovementSneak;
        }

        if (sneak && !moving)
        {
            return MovementStill * (MovementSneak / MovementWalk);
        }

        if (moving)
        {
            return MovementWalk;
        }

        return MovementStill;
    }

    public static int MovementBand(bool moving, bool sneak, bool sprint)
    {
        if (sprint && moving)
        {
            return BandSprint;
        }

        if (sneak)
        {
            return BandSneak;
        }

        if (moving)
        {
            return BandWalk;
        }

        return BandStill;
    }

    /// <summary>
    /// Maps pipeline <c>animal-threat</c> percent (e.g. 180→80) to an emission multiplier.
    /// </summary>
    public static float ThreatEmissionFromPercent(int percent) =>
        Math.Max(0f, percent) / 100f;

    /// <summary>
    /// Generation fear in 0..1. Generation ≥ taming → 0.
    /// </summary>
    public static float GenerationFear(int generation, float tamingGenerations)
    {
        if (tamingGenerations <= 0f)
        {
            return 1f;
        }

        return Math.Max(0f, (tamingGenerations - generation) / tamingGenerations);
    }

    /// <summary>
    /// Ordinary sensitivity after generation, friendliness calms, flee-chance reduction, favorite.
    /// <paramref name="fleeMult"/> / <paramref name="broodMult"/> are Gentle Spirit / Hen Friend percents as multipliers (1 = ×1).
    /// <paramref name="fleeChanceReduction"/> is the husbandry chance-fold fraction (0..1).
    /// </summary>
    public static float OrdinaryScale(
        float generationFear,
        int friendliness,
        float fleeMult,
        float broodMult,
        float fleeChanceReduction,
        bool isFavoriteSeraph)
    {
        float fleeScale = HusbandryFriendliness.ScaleFearReductionFactor(
            1f,
            friendliness,
            fleeMult);
        float broodScale = HusbandryFriendliness.ScaleFearReductionFactor(
            1f,
            friendliness,
            broodMult);
        float chanceScale = HusbandryFriendliness.ScalePassiveFleeChance(
            1f,
            fleeChanceReduction,
            friendliness);
        float favorite = isFavoriteSeraph ? FavoriteOrdinaryFactor : 1f;
        return GameMath.Clamp(
            generationFear * fleeScale * broodScale * chanceScale * favorite,
            0f,
            1f);
    }

    /// <summary>
    /// 0 = fully ordinary (relationship applies). 1 = fully acute (sprint / body-close).
    /// Proximity acute only rises inside half the characteristic range.
    /// </summary>
    public static float AcuteBlend(float proximity, bool sprint)
    {
        float fromProximity = proximity <= 0.5f
            ? 0f
            : GameMath.Clamp((proximity - 0.5f) * 2f, 0f, 1f);
        float fromSprint = sprint ? 0.55f : 0f;
        return GameMath.Clamp(fromProximity + fromSprint, 0f, 1f);
    }

    public static float Threat(
        float proximity,
        float movementDisturbance,
        float detectability,
        float ordinaryScale,
        bool sprint)
    {
        float emission = proximity * movementDisturbance * detectability;
        if (emission <= 0f)
        {
            return 0f;
        }

        float blend = AcuteBlend(proximity, sprint);
        float ordinary = emission * ordinaryScale;
        float threat = ordinary + (emission - ordinary) * blend;
        if (blend > 0f && ordinaryScale < 0.01f)
        {
            threat = Math.Max(threat, emission * blend * AcuteFloorFraction);
        }

        // Uncapped above 1: skittish animals can rise/commit faster than "full" emission.
        return Math.Max(0f, threat);
    }

    public static float Integrate(float alert, float threat, float dt)
    {
        if (dt <= 0f)
        {
            return GameMath.Clamp(alert, AlertMin, AlertMax);
        }

        float next = alert + (threat * RiseGain - Decay) * dt;
        return GameMath.Clamp(next, AlertMin, AlertMax);
    }

    public static bool UpdateAwake(bool awake, float alert)
    {
        if (awake)
        {
            return alert >= WakeExit;
        }

        return alert >= WakeEnter;
    }

    /// <summary>
    /// Committed (fleeing) only exits on cool-off. Entry is via flee bolt / shocks, not auto at 100.
    /// </summary>
    public static bool UpdateCommitted(bool committed, float alert)
    {
        if (!committed)
        {
            return false;
        }

        return alert >= CommitExit;
    }

    /// <summary>
    /// Flee probability from alert. Control points: alert 0 / 50 / 100 → 0 / 0.1 / 1.
    /// Alert above 100 saturates at 1.
    /// </summary>
    public static float FleeChance(float alert)
    {
        float u = GameMath.Clamp(alert / CommitEnter, 0f, 1f);
        if (u <= 0.5f)
        {
            return GameMath.Lerp(0f, 0.1f, u * 2f);
        }

        return GameMath.Lerp(0.1f, 1f, (u - 0.5f) * 2f);
    }

    /// <summary>
    /// True when instantaneous threat rose enough to warrant another flee roll.
    /// </summary>
    public static bool ThreatRose(float previousThreat, float currentThreat) =>
        currentThreat > previousThreat + 0.0001f;

    /// <summary>
    /// True when the seraph's movement band changed (posture / gait).
    /// </summary>
    public static bool PostureChanged(int previousBand, int currentBand) =>
        previousBand != currentBand;

    /// <summary>
    /// True when a non-committed animal should attempt a flee roll this sample.
    /// </summary>
    public static bool ShouldAttemptFleeRoll(
        bool committed,
        float alert,
        float previousThreat,
        float currentThreat,
        int previousBand,
        int currentBand,
        bool hasTarget) =>
        !committed
        && hasTarget
        && alert > 0f
        && (ThreatRose(previousThreat, currentThreat)
            || PostureChanged(previousBand, currentBand));

    /// <summary>
    /// True when <paramref name="newThreat"/> should replace the current alert target.
    /// Missing current (threat ≤ 0) always accepts a positive new threat.
    /// </summary>
    public static bool ShouldSwitchTarget(float currentTargetThreat, float newThreat)
    {
        if (newThreat <= 0f)
        {
            return false;
        }

        if (currentTargetThreat <= 0f)
        {
            return true;
        }

        return newThreat > currentTargetThreat * TargetSwitchRatio;
    }

    public static float ApplyHerdAlarmFloor(float alert) =>
        Math.Max(alert, HerdAlarmFloor);

    public static float ApplyDamageCommit(float alert) =>
        Math.Max(alert, CommitEnter);

    /// <summary>Double alert when flee behavior triggers (bolt). Floors high enough to stay spooked briefly.</summary>
    public static float ApplyFleeBolt(float alert)
    {
        float bolted = Math.Max(alert, 1f) * 2f;
        // Must clear CommitExit or cool-off would drop spooked on the same tick.
        return GameMath.Clamp(Math.Max(bolted, CommitExit + 15f), AlertMin, AlertMax);
    }

    /// <summary>
    /// Independent commits (own flee bolt / damage) may broadcast once.
    /// Propagated herd alarms do not rebroadcast.
    /// </summary>
    public static bool MayBroadcastHerd(bool committedNow, bool wasCommitted, bool propagatedHerd) =>
        committedNow && !wasCommitted && !propagatedHerd;
}
