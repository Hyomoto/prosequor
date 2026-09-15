namespace Prosequor.Data;

/// <summary>
/// Skill contract used for level caps, menu sections, and (for hobbies) local unlock points.
/// </summary>
public enum SkillKind
{
    Specialization,
    Minor,
    Hobby,
    Passive
}

/// <summary>
/// Derives <see cref="SkillKind"/> and level caps from authored JSON (before tree compile)
/// or from a registered <see cref="SkillDef"/>.
/// </summary>
public static class SkillKindPolicy
{
    /// <summary>
    /// Classify from draft JSON after contributions. Hobby wins; otherwise specialization
    /// nodes beat minor; empty/missing trees are passive.
    /// </summary>
    public static SkillKind ClassifyDraft(SkillDefJson row)
    {
        if (row.hobby)
        {
            return SkillKind.Hobby;
        }

        SkillTreeNodeJson[]? nodes = row.tree?.nodes;
        if (nodes == null || nodes.Length == 0)
        {
            return SkillKind.Passive;
        }

        foreach (SkillTreeNodeJson node in nodes)
        {
            if (node != null && node.specialization)
            {
                return SkillKind.Specialization;
            }
        }

        return SkillKind.Minor;
    }

    /// <summary>
    /// Classify a registered skill. Uses <see cref="SkillDef.IsHobby"/> first, then tree shape.
    /// </summary>
    public static SkillKind Classify(SkillDef skill)
    {
        if (skill.IsHobby)
        {
            return SkillKind.Hobby;
        }

        if (skill.Tree == null || skill.Tree.Nodes.Count == 0)
        {
            return SkillKind.Passive;
        }

        foreach (SkillTreeNodeDef node in skill.Tree.Nodes)
        {
            if (node.IsSpecialization)
            {
                return SkillKind.Specialization;
            }
        }

        return SkillKind.Minor;
    }

    public static int MaxLevelFor(SkillKind kind) => kind switch
    {
        SkillKind.Hobby => XpCurves.HobbyMaxLevel,
        SkillKind.Specialization => XpCurves.SkillMaxLevel,
        _ => XpCurves.MinorMaxLevel
    };

    /// <summary>
    /// Copy of <paramref name="tree"/> with every node's <c>specialization</c> cleared.
    /// Returns null when <paramref name="tree"/> is null. Empty trees are returned as-is.
    /// </summary>
    public static SkillTreeJson? StripSpecializationFlags(SkillTreeJson? tree)
    {
        if (tree?.nodes == null || tree.nodes.Length == 0)
        {
            return tree;
        }

        SkillTreeNodeJson[] copy = new SkillTreeNodeJson[tree.nodes.Length];
        for (int i = 0; i < tree.nodes.Length; i++)
        {
            SkillTreeNodeJson? src = tree.nodes[i];
            if (src == null)
            {
                copy[i] = null!;
                continue;
            }

            copy[i] = CloneNodeClearingSpecialization(src);
        }

        return new SkillTreeJson { nodes = copy };
    }

    static SkillTreeNodeJson CloneNodeClearingSpecialization(SkillTreeNodeJson src) =>
        new()
        {
            id = src.id,
            nameLang = src.nameLang,
            descriptionLang = src.descriptionLang,
            descriptionParams = src.descriptionParams,
            totalParams = src.totalParams,
            icon = src.icon,
            cost = src.cost,
            minSkillLevel = src.minSkillLevel,
            specialization = false,
            layout = src.layout,
            tiers = src.tiers,
            requires = src.requires,
            excludes = src.excludes,
            replaces = src.replaces
        };
}
