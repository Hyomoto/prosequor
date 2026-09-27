namespace Prosequor.Client.Tracker;

/// <summary>
/// One candidate and one marked animal. The caller supplies aim and the marked
/// body's presence, distance, and angle; this type does not touch the world.
/// </summary>
public sealed class TrackMark
{
    public long? MarkedId { get; private set; }

    public long? CandidateId { get; private set; }

    public float CandidateSeconds { get; private set; }

    /// <summary>Lost aim shorter than this leaves candidate progress untouched.</summary>
    public const float AimGraceSeconds = 0.4f;

    /// <summary>After the grace, candidate seconds fall this many per second of lost aim.</summary>
    public const float AimDecayPerSecond = 2f;

    float offAimSeconds;

    /// <summary>
    /// <paramref name="aimedLivingAnimalId"/> is set only while a living animal inside
    /// <paramref name="range"/> sits near the cursor. A missing marked body, a distance
    /// past <paramref name="range"/>, or an angle past <paramref name="loseAngleDegrees"/>
    /// clears the mark. Death is not a signal here: a body that is still present stays.
    /// A short gap in aim holds candidate progress, then drains it instead of clearing it.
    /// </summary>
    public void Tick(
        float deltaTime,
        bool hasUnlock,
        long? aimedLivingAnimalId,
        bool markedPresent,
        float markedDistance,
        float markedAngleDegrees,
        float range,
        float focusSeconds,
        float loseAngleDegrees)
    {
        if (!hasUnlock || range <= 0f)
        {
            Clear();
            return;
        }

        if (MarkedId != null
            && (!markedPresent
                || markedDistance > range
                || markedAngleDegrees > loseAngleDegrees))
        {
            MarkedId = null;
        }

        if (aimedLivingAnimalId is not long aimed)
        {
            DecayCandidate(deltaTime);
            return;
        }

        offAimSeconds = 0f;
        if (CandidateId != aimed)
        {
            CandidateId = aimed;
            CandidateSeconds = 0f;
        }

        CandidateSeconds += Math.Max(0f, deltaTime);
        if (CandidateSeconds >= focusSeconds)
        {
            MarkedId = aimed;
            CandidateId = null;
            CandidateSeconds = 0f;
            offAimSeconds = 0f;
        }
    }

    void DecayCandidate(float deltaTime)
    {
        if (CandidateId == null)
        {
            offAimSeconds = 0f;
            return;
        }

        float before = offAimSeconds;
        offAimSeconds += Math.Max(0f, deltaTime);
        float decayStart = Math.Max(before, AimGraceSeconds);
        float decayEnd = offAimSeconds;
        if (decayEnd > decayStart)
        {
            CandidateSeconds -= (decayEnd - decayStart) * AimDecayPerSecond;
        }

        if (CandidateSeconds <= 0f)
        {
            CandidateId = null;
            CandidateSeconds = 0f;
            offAimSeconds = 0f;
        }
    }

    public void Clear()
    {
        MarkedId = null;
        CandidateId = null;
        CandidateSeconds = 0f;
        offAimSeconds = 0f;
    }
}
