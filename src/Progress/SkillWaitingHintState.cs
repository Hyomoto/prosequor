namespace Prosequor.Progress;

/// <summary>
/// Session-only arm/disarm for the unspent-points HUD reminder.
/// Arms on first observe with points or a later point gain; disarms when the C menu opens.
/// Optional <see cref="SkillHint"/> is set only by skill level-ups and cleared with the arm flag.
/// </summary>
public sealed class SkillWaitingHintState
{
    bool seen;
    bool armed;
    int lastPoints;
    string? skillHint;

    public bool Armed => armed;

    public bool Seen => seen;

    public int LastPoints => lastPoints;

    /// <summary>Skill tree to open on C while armed; null for login / non-skill arms.</summary>
    public string? SkillHint => skillHint;

    /// <summary>
    /// Feed the latest mirrored unlock-point count. First positive reading or a later increase
    /// arms the reminder. Does not set or clear <see cref="SkillHint"/> (login passes through here).
    /// </summary>
    public void Observe(int unlockPoints)
    {
        unlockPoints = Math.Max(0, unlockPoints);
        if (!seen)
        {
            if (unlockPoints <= 0)
            {
                return;
            }

            seen = true;
            lastPoints = unlockPoints;
            armed = true;
            return;
        }

        if (unlockPoints > lastPoints)
        {
            armed = true;
        }

        lastPoints = unlockPoints;
    }

    /// <summary>
    /// Skill leveled up: arm the reminder and remember which tree to open (overrides prior hint).
    /// </summary>
    public void NoteSkillLevelUp(string skillId)
    {
        if (string.IsNullOrWhiteSpace(skillId))
        {
            return;
        }

        seen = true;
        armed = true;
        skillHint = skillId.Trim();
    }

    /// <summary>
    /// Call when the character dialog opens. Clears the arm flag and skill hint; returns whether
    /// it was armed (so the Skills tab should be selected) and the hint to open if any.
    /// </summary>
    public bool ConsumeOpen(out string? openSkillId)
    {
        openSkillId = skillHint;
        skillHint = null;
        bool wasArmed = armed;
        armed = false;
        return wasArmed;
    }

    public bool IsVisible(int unlockPoints, bool characterMenuOpen) =>
        armed && unlockPoints > 0 && !characterMenuOpen;
}
