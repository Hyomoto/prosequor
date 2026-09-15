using System.Globalization;

namespace Prosequor.Data;

public enum SkillMenuSectionKind
{
    Specializations,
    MinorSkills,
    HobbySkills,
    Passives
}

/// <summary>One non-empty skills-tab section (header + ordered skills).</summary>
public sealed class SkillMenuSection
{
    public required SkillMenuSectionKind Kind { get; init; }
    public required string TitleLang { get; init; }
    public required IReadOnlyList<SkillDef> Skills { get; init; }
}

/// <summary>
/// Cached Skills-tab ordering: Specializations, Minor Skills, Hobby Skills, Passives.
/// Built once after skill registration; empty buckets are omitted. Hobby wins.
/// </summary>
public sealed class SkillMenuIndex
{
    public static SkillMenuIndex Empty { get; } = new(Array.Empty<SkillMenuSection>());

    public IReadOnlyList<SkillMenuSection> Sections { get; }

    SkillMenuIndex(IReadOnlyList<SkillMenuSection> sections)
    {
        Sections = sections;
    }

    public static SkillMenuIndex Build(IEnumerable<SkillDef> skills, string? languageCode = null)
    {
        List<SkillDef> specializations = new();
        List<SkillDef> minors = new();
        List<SkillDef> hobbies = new();
        List<SkillDef> passives = new();

        foreach (SkillDef skill in skills)
        {
            // Prefer stored Kind from compile; fall back to tree shape for hand-built defs.
            SkillKind kind = skill.IsHobby
                ? SkillKind.Hobby
                : skill.Kind switch
                {
                    SkillKind.Hobby => SkillKind.Hobby,
                    SkillKind.Passive => SkillKind.Passive,
                    SkillKind.Specialization => SkillKind.Specialization,
                    SkillKind.Minor when HasSpecialization(skill) => SkillKind.Specialization,
                    SkillKind.Minor when IsPassiveShape(skill) => SkillKind.Passive,
                    _ => skill.Kind
                };

            switch (kind)
            {
                case SkillKind.Hobby:
                    hobbies.Add(skill);
                    break;
                case SkillKind.Passive:
                    passives.Add(skill);
                    break;
                case SkillKind.Specialization:
                    specializations.Add(skill);
                    break;
                default:
                    minors.Add(skill);
                    break;
            }
        }

        SortByDisplayName(specializations, languageCode);
        SortByDisplayName(minors, languageCode);
        SortByDisplayName(hobbies, languageCode);
        SortByDisplayName(passives, languageCode);

        List<SkillMenuSection> sections = new();
        AddIfAny(sections, SkillMenuSectionKind.Specializations, "prosequor:skill-section-specializations", specializations);
        AddIfAny(sections, SkillMenuSectionKind.MinorSkills, "prosequor:skill-section-minor", minors);
        AddIfAny(sections, SkillMenuSectionKind.HobbySkills, "prosequor:skill-section-hobby", hobbies);
        AddIfAny(sections, SkillMenuSectionKind.Passives, "prosequor:skill-section-passives", passives);
        return new SkillMenuIndex(sections);
    }

    public static bool IsPassive(SkillDef skill) =>
        skill.IsHobby
            ? false
            : skill.Kind == SkillKind.Passive || IsPassiveShape(skill);

    public static bool HasSpecialization(SkillDef skill)
    {
        if (skill.IsHobby)
        {
            return false;
        }

        if (skill.Tree == null)
        {
            return false;
        }

        foreach (SkillTreeNodeDef node in skill.Tree.Nodes)
        {
            if (node.IsSpecialization)
            {
                return true;
            }
        }

        return false;
    }

    static bool IsPassiveShape(SkillDef skill) =>
        skill.Tree == null || skill.Tree.Nodes.Count == 0;

    static void AddIfAny(
        List<SkillMenuSection> sections,
        SkillMenuSectionKind kind,
        string titleLang,
        List<SkillDef> skills)
    {
        if (skills.Count == 0)
        {
            return;
        }

        sections.Add(new SkillMenuSection
        {
            Kind = kind,
            TitleLang = titleLang,
            Skills = skills
        });
    }

    static void SortByDisplayName(List<SkillDef> skills, string? languageCode)
    {
        skills.Sort((a, b) => string.Compare(
            SkillRegistry.DisplayName(a, languageCode),
            SkillRegistry.DisplayName(b, languageCode),
            CultureInfo.CurrentCulture,
            CompareOptions.IgnoreCase));
    }
}
