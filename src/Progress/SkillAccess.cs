namespace Prosequor.Progress;

/// <summary>
/// One contributor to a player's skill-access union. The set reference is frozen after store.
/// </summary>
public readonly record struct SkillAccessSlot(string Key, IReadOnlySet<string> Skills);

/// <summary>
/// Per-player skill membership. Slots are replaced by key; the union is the interrogation set.
/// </summary>
public sealed class SkillAccess
{
    public const string ClassKey = "class";

    readonly Dictionary<string, SkillAccessSlot> slots = new(StringComparer.OrdinalIgnoreCase);
    IReadOnlySet<string> union = EmptySet;

    public static IReadOnlySet<string> EmptySet { get; } =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    public IReadOnlySet<string> Union => union;

    /// <summary>True when no contributor slots have been written yet.</summary>
    public bool IsUnbound => slots.Count == 0;

    public bool Contains(string skillId) =>
        !string.IsNullOrWhiteSpace(skillId) && union.Contains(skillId.Trim());

    /// <summary>
    /// Replace the slot for <paramref name="key"/>. Same set reference is a no-op.
    /// A different reference replaces the slot and rebuilds the union.
    /// Pass <paramref name="skills"/> null to drop the key.
    /// </summary>
    /// <returns>True when the union reference changed.</returns>
    public bool Update(string key, IReadOnlySet<string>? skills)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return false;
        }

        string slotKey = key.Trim();
        if (skills == null)
        {
            if (!slots.Remove(slotKey))
            {
                return false;
            }

            RebuildUnion();
            return true;
        }

        if (slots.TryGetValue(slotKey, out SkillAccessSlot existing)
            && ReferenceEquals(existing.Skills, skills))
        {
            return false;
        }

        slots[slotKey] = new SkillAccessSlot(slotKey, skills);
        RebuildUnion();
        return true;
    }

    void RebuildUnion()
    {
        if (slots.Count == 0)
        {
            union = EmptySet;
            return;
        }

        if (slots.Count == 1)
        {
            foreach (SkillAccessSlot slot in slots.Values)
            {
                union = slot.Skills;
                return;
            }
        }

        HashSet<string> merged = new(StringComparer.OrdinalIgnoreCase);
        foreach (SkillAccessSlot slot in slots.Values)
        {
            foreach (string id in slot.Skills)
            {
                if (!string.IsNullOrWhiteSpace(id))
                {
                    merged.Add(id);
                }
            }
        }

        union = merged;
    }
}
