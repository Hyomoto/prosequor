using Prosequor.Ability.Hooks;

namespace Prosequor.Data;

/// <summary>
/// Compiles a skill tree; on hard validation failure, orphans the LIFO-blamed node and retries
/// so conflicting contributions degrade to a reduced tree instead of no tree.
/// </summary>
public static class SkillTreeCompileRepair
{
    public sealed class RepairResult
    {
        public SkillTreeCompiler.CompileResult Compile { get; set; } = new();
        public List<(string NodeId, string Cause)> Orphans { get; } = new();
    }

    public static RepairResult CompileWithOrphanRepair(
        string skillId,
        int skillMaxLevel,
        SkillTreeJson? json,
        IReadOnlyList<AbilityEffectJson> rootEffects,
        IHookRegistry hooks,
        IAbilityActionRegistry actions,
        CollectionIndex collections,
        ref int sourceOrder)
    {
        RepairResult repair = new()
        {
            Compile = SkillTreeCompiler.Compile(
                skillId,
                skillMaxLevel,
                json,
                rootEffects,
                hooks,
                actions,
                collections,
                ref sourceOrder)
        };

        if (repair.Compile.Success || json?.nodes == null || json.nodes.Length == 0)
        {
            return repair;
        }

        // Work on a mutable copy so callers keep the original asset row intact.
        List<SkillTreeNodeJson> remaining = json.nodes.ToList();
        int maxIterations = remaining.Count;

        for (int i = 0; i < maxIterations && !repair.Compile.Success && remaining.Count > 0; i++)
        {
            SkillTreeCompiler.CompileIssue? issue = repair.Compile.Issues.FirstOrDefault()
                ?? (repair.Compile.Errors.Count > 0
                    ? new SkillTreeCompiler.CompileIssue { Message = repair.Compile.Errors[0] }
                    : null);
            if (issue == null)
            {
                break;
            }

            string? victim = PickVictim(issue, remaining);
            if (victim == null)
            {
                break;
            }

            remaining.RemoveAll(n =>
                string.Equals(n.id?.Trim(), victim, StringComparison.OrdinalIgnoreCase));
            HashSet<string> removed = new(StringComparer.OrdinalIgnoreCase) { victim };
            foreach (SkillTreeNodeJson node in remaining)
            {
                SkillTreeRequiresRewriter.StripIds(node, removed);
            }

            repair.Orphans.Add((victim, issue.Message));

            SkillTreeJson stripped = new() { nodes = remaining.ToArray() };
            repair.Compile = SkillTreeCompiler.Compile(
                skillId,
                skillMaxLevel,
                stripped,
                rootEffects,
                hooks,
                actions,
                collections,
                ref sourceOrder);
        }

        return repair;
    }

    /// <summary>
    /// Among nodes blamed for the error that still remain, pick the last-declared (LIFO).
    /// Falls back to the last authored node only when blame is empty/unmatched.
    /// </summary>
    internal static string? PickVictim(
        SkillTreeCompiler.CompileIssue issue,
        IReadOnlyList<SkillTreeNodeJson> remaining)
    {
        if (remaining.Count == 0)
        {
            return null;
        }

        HashSet<string> blame = new(
            issue.BlamedNodeIds.Where(id => !string.IsNullOrWhiteSpace(id)),
            StringComparer.OrdinalIgnoreCase);

        for (int i = remaining.Count - 1; i >= 0; i--)
        {
            string? id = remaining[i].id?.Trim();
            if (string.IsNullOrEmpty(id))
            {
                continue;
            }

            if (blame.Count == 0 || blame.Contains(id))
            {
                if (blame.Count == 0)
                {
                    // No structured blame: last remaining node.
                    return remaining[^1].id?.Trim();
                }

                // LIFO within blame set — first hit walking from the end.
                return id;
            }
        }

        // Blame ids not present (already stripped): fall back to last remaining.
        return remaining[^1].id?.Trim();
    }
}
