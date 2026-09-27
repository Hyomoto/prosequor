namespace Prosequor.Client.Tracker;

/// <summary>
/// Chooses which in-cone animal the cursor is focusing. A neighbor stays put until
/// it is clearly more centered; a separated animal replaces the current one as soon
/// as it is closer to the cursor.
/// </summary>
public static class TrackAim
{
    public const float ConeDegrees = 4f;

    public const float StickySeparationDegrees = 10f;

    public const float StickyAdvantageDegrees = 3f;

    /// <param name="AngleDegrees">How near the look passes the body. Above <see cref="ConeDegrees"/> is ignored.</param>
    /// <param name="SeparationDegrees">Angle at the eye between this animal and the current candidate.</param>
    /// <param name="CenterDegrees">Angle from the look to the body center. Smaller means the cursor is more on that animal.</param>
    public readonly record struct AimSample(
        long Id,
        float AngleDegrees,
        float SeparationDegrees,
        float CenterDegrees);

    public static long? Select(long? heldId, ReadOnlySpan<AimSample> samples)
    {
        int best = -1;
        int held = -1;
        for (int i = 0; i < samples.Length; i++)
        {
            if (samples[i].AngleDegrees > ConeDegrees)
            {
                continue;
            }

            bool closer = best < 0 || samples[i].CenterDegrees < samples[best].CenterDegrees;
            bool tiedWithHeld = heldId is long id
                && samples[i].Id == id
                && (best < 0 || samples[i].CenterDegrees <= samples[best].CenterDegrees);
            if (closer || tiedWithHeld)
            {
                best = i;
            }

            if (heldId is long heldEntity && samples[i].Id == heldEntity)
            {
                held = i;
            }
        }

        if (best < 0)
        {
            return null;
        }

        if (held < 0 || samples[best].Id == samples[held].Id)
        {
            return samples[best].Id;
        }

        float separation = Math.Max(0f, samples[best].SeparationDegrees);
        float advantage = separation >= StickySeparationDegrees
            ? 0f
            : StickyAdvantageDegrees * (1f - (separation / StickySeparationDegrees));
        if (samples[best].CenterDegrees + advantage <= samples[held].CenterDegrees)
        {
            return samples[best].Id;
        }

        return samples[held].Id;
    }
}
