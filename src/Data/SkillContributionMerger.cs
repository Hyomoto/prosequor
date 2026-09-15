using Newtonsoft.Json.Linq;
using Prosequor.Xp;
using Vintagestory.API.Common;

namespace Prosequor.Data;

/// <summary>
/// Merges contribution disable/replace/add operations into draft skill trees before compile.
/// Soft-deps: unknown skills / bad namespaces / duplicate ids / unmet requires are skipped with warnings.
/// </summary>
public static class SkillContributionMerger
{
    public sealed class PendingEntry
    {
        public required string SourceDomain { get; init; }
        public required string SourcePath { get; init; }
        public required string SkillId { get; init; }
        public bool DisableAll { get; init; }
        public IReadOnlyList<string> Disable { get; init; } = Array.Empty<string>();
        public IReadOnlyList<SkillTreeNodeJson> Nodes { get; init; } = Array.Empty<SkillTreeNodeJson>();
        public IReadOnlyList<XpRuleJson> XpRules { get; init; } = Array.Empty<XpRuleJson>();
    }

    public sealed class PendingNode
    {
        public required string SourceDomain { get; init; }
        public required string SourcePath { get; init; }
        public required string SkillId { get; init; }
        public required SkillTreeNodeJson Node { get; init; }
    }

    public static void Apply(
        IDictionary<string, SkillDefJson> drafts,
        IReadOnlyList<PendingEntry> entries,
        Action<string> warn)
    {
        List<PendingNode> pending = new();
        foreach (PendingEntry entry in entries)
        {
            if (entry.DisableAll)
            {
                if (!drafts.Remove(entry.SkillId))
                {
                    warn(
                        $"[prosequor] Contribution disable all for unknown skill '{entry.SkillId}' "
                        + $"from {entry.SourceDomain}:{entry.SourcePath}.");
                }

                continue;
            }

            ApplyDisable(drafts, entry, warn);
            MergeXpRules(drafts, entry, warn);
            foreach (SkillTreeNodeJson node in entry.Nodes)
            {
                if (node == null)
                {
                    continue;
                }

                string? replaces = node.replaces?.Trim();
                if (string.IsNullOrWhiteSpace(replaces))
                {
                    pending.Add(new PendingNode
                    {
                        SourceDomain = entry.SourceDomain,
                        SourcePath = entry.SourcePath,
                        SkillId = entry.SkillId,
                        Node = node
                    });
                    continue;
                }

                if (!TryApplyReplace(drafts, entry, node, replaces, warn))
                {
                    continue;
                }

                node.replaces = null;
                pending.Add(new PendingNode
                {
                    SourceDomain = entry.SourceDomain,
                    SourcePath = entry.SourcePath,
                    SkillId = entry.SkillId,
                    Node = node
                });
            }
        }

        List<PendingNode> remaining = pending.ToList();
        bool progress = true;
        while (progress && remaining.Count > 0)
        {
            progress = false;
            List<PendingNode> next = new();
            foreach (PendingNode item in remaining)
            {
                if (!TryAccept(drafts, item, warn, out bool defer))
                {
                    if (defer)
                    {
                        next.Add(item);
                    }

                    continue;
                }

                progress = true;
            }

            remaining = next;
        }

        foreach (PendingNode leftover in remaining)
        {
            string nodeId = leftover.Node.id?.Trim() ?? "";
            warn(
                $"[prosequor] Skipping contribution node '{nodeId}' for skill '{leftover.SkillId}' "
                + $"from {leftover.SourceDomain}:{leftover.SourcePath}: unmet requires (soft-dep).");
        }
    }

    static void MergeXpRules(
        IDictionary<string, SkillDefJson> drafts,
        PendingEntry entry,
        Action<string> warn)
    {
        if (entry.XpRules.Count == 0)
        {
            return;
        }

        if (!drafts.TryGetValue(entry.SkillId, out SkillDefJson? draft) || draft == null)
        {
            warn(
                $"[prosequor] Skipping contribution xpRules for unknown skill '{entry.SkillId}' "
                + $"from {entry.SourceDomain}:{entry.SourcePath}.");
            return;
        }

        Dictionary<string, XpRuleJson> byId = new(StringComparer.OrdinalIgnoreCase);
        if (draft.xpRules != null)
        {
            foreach (XpRuleJson existing in draft.xpRules)
            {
                if (existing == null || string.IsNullOrWhiteSpace(existing.id))
                {
                    continue;
                }

                byId[existing.id.Trim()] = existing;
            }
        }

        foreach (XpRuleJson rule in entry.XpRules)
        {
            if (rule == null || string.IsNullOrWhiteSpace(rule.id))
            {
                warn(
                    $"[prosequor] Skipping contribution xpRule with blank id for skill '{entry.SkillId}' "
                    + $"from {entry.SourceDomain}:{entry.SourcePath}.");
                continue;
            }

            string id = rule.id.Trim();
            rule.id = id;
            byId[id] = rule;
        }

        draft.xpRules = byId.Values.ToArray();
    }

    static void ApplyDisable(
        IDictionary<string, SkillDefJson> drafts,
        PendingEntry entry,
        Action<string> warn)
    {
        if (entry.Disable.Count == 0)
        {
            return;
        }

        if (!drafts.TryGetValue(entry.SkillId, out SkillDefJson? draft) || draft?.tree?.nodes == null)
        {
            foreach (string id in entry.Disable)
            {
                if (string.IsNullOrWhiteSpace(id))
                {
                    continue;
                }

                warn(
                    $"[prosequor] Skipping disable of '{id.Trim()}' for unknown skill '{entry.SkillId}' "
                    + $"from {entry.SourceDomain}:{entry.SourcePath}.");
            }

            return;
        }

        List<SkillTreeNodeJson> nodes = draft.tree.nodes.ToList();
        foreach (string rawId in entry.Disable)
        {
            string disabledId = rawId?.Trim() ?? "";
            if (disabledId.Length == 0)
            {
                continue;
            }

            int removed = nodes.RemoveAll(n =>
                string.Equals(n.id?.Trim(), disabledId, StringComparison.OrdinalIgnoreCase));
            if (removed == 0)
            {
                warn(
                    $"[prosequor] Contribution disable '{disabledId}' for skill '{entry.SkillId}' "
                    + $"from {entry.SourceDomain}:{entry.SourcePath}: node not found.");
                continue;
            }

            HashSet<string> stripped = new(StringComparer.OrdinalIgnoreCase) { disabledId };
            foreach (SkillTreeNodeJson node in nodes)
            {
                bool hadRequires = node.requires is { Length: > 0 };
                SkillTreeRequiresRewriter.StripIds(node, stripped);
                if (hadRequires && (node.requires == null || node.requires.Length == 0))
                {
                    warn(
                        $"[prosequor] Contribution disable '{disabledId}' for skill '{entry.SkillId}' "
                        + $"left node '{node.id}' without prerequisites (promoted to root).");
                }
            }
        }

        draft.tree.nodes = nodes.ToArray();
    }

    static bool TryApplyReplace(
        IDictionary<string, SkillDefJson> drafts,
        PendingEntry entry,
        SkillTreeNodeJson node,
        string oldId,
        Action<string> warn)
    {
        string newId = node.id?.Trim() ?? "";
        if (newId.Length == 0)
        {
            warn(
                $"[prosequor] Skipping contribution replace for '{oldId}' with blank id "
                + $"on skill '{entry.SkillId}' from {entry.SourceDomain}:{entry.SourcePath}.");
            return false;
        }

        if (!drafts.TryGetValue(entry.SkillId, out SkillDefJson? draft) || draft == null)
        {
            warn(
                $"[prosequor] Skipping contribution replace '{oldId}' -> '{newId}' for unknown skill "
                + $"'{entry.SkillId}' from {entry.SourceDomain}:{entry.SourcePath}.");
            return false;
        }

        draft.tree ??= new SkillTreeJson();
        List<SkillTreeNodeJson> nodes = draft.tree.nodes?.ToList() ?? new List<SkillTreeNodeJson>();
        bool found = nodes.RemoveAll(n =>
            string.Equals(n.id?.Trim(), oldId, StringComparison.OrdinalIgnoreCase)) > 0;
        if (!found)
        {
            warn(
                $"[prosequor] Skipping contribution replace '{oldId}' -> '{newId}' for skill "
                + $"'{entry.SkillId}' from {entry.SourceDomain}:{entry.SourcePath}: replaced node not found.");
            return false;
        }

        Dictionary<string, string> replacements = new(StringComparer.OrdinalIgnoreCase)
        {
            [oldId] = newId
        };
        foreach (SkillTreeNodeJson existing in nodes)
        {
            SkillTreeRequiresRewriter.ApplyReplacementMap(existing, replacements);
        }

        draft.tree.nodes = nodes.ToArray();
        return true;
    }

    static bool TryAccept(
        IDictionary<string, SkillDefJson> drafts,
        PendingNode item,
        Action<string> warn,
        out bool defer)
    {
        defer = false;
        string nodeId = item.Node.id?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(nodeId))
        {
            warn(
                $"[prosequor] Skipping contribution node with blank id for skill '{item.SkillId}' "
                + $"from {item.SourceDomain}:{item.SourcePath}.");
            return false;
        }

        item.Node.id = nodeId;
        int colon = nodeId.IndexOf(':');
        if (colon <= 0 || colon >= nodeId.Length - 1)
        {
            warn(
                $"[prosequor] Skipping contribution node '{nodeId}' for skill '{item.SkillId}' "
                + $"from {item.SourceDomain}:{item.SourcePath}: id must be '{item.SourceDomain}:localId'.");
            return false;
        }

        string domain = nodeId[..colon];
        if (!string.Equals(domain, item.SourceDomain, StringComparison.OrdinalIgnoreCase))
        {
            warn(
                $"[prosequor] Skipping contribution node '{nodeId}' for skill '{item.SkillId}' "
                + $"from {item.SourceDomain}:{item.SourcePath}: domain must match asset domain "
                + $"'{item.SourceDomain}'.");
            return false;
        }

        if (!drafts.TryGetValue(item.SkillId, out SkillDefJson? draft) || draft == null)
        {
            warn(
                $"[prosequor] Skipping contribution for unknown skill '{item.SkillId}' "
                + $"(node '{nodeId}' from {item.SourceDomain}:{item.SourcePath}).");
            return false;
        }

        draft.tree ??= new SkillTreeJson();
        List<SkillTreeNodeJson> nodes = draft.tree.nodes?.ToList() ?? new List<SkillTreeNodeJson>();
        HashSet<string> known = new(
            nodes
                .Select(n => n.id?.Trim() ?? "")
                .Where(id => id.Length > 0),
            StringComparer.OrdinalIgnoreCase);

        if (known.Contains(nodeId))
        {
            warn(
                $"[prosequor] Skipping contribution node '{nodeId}' for skill '{item.SkillId}' "
                + $"from {item.SourceDomain}:{item.SourcePath}: duplicate node id.");
            return false;
        }

        foreach (RequireGroup group in ParseContributionRequires(item.Node.requires))
        {
            bool anyKnown = false;
            foreach (string req in group.Alternatives)
            {
                if (known.Contains(req))
                {
                    anyKnown = true;
                    break;
                }
            }

            if (!anyKnown)
            {
                defer = true;
                return false;
            }
        }

        nodes.Add(item.Node);
        draft.tree.nodes = nodes.ToArray();
        return true;
    }

    static IEnumerable<RequireGroup> ParseContributionRequires(JToken[]? rawRequires)
    {
        if (rawRequires == null || rawRequires.Length == 0)
        {
            yield break;
        }

        List<string> errors = new();
        if (!SkillTreeCompiler.TryParseRequireGroups(
                "contribution",
                "pending",
                rawRequires,
                out List<RequireGroup> groups,
                out _,
                errors))
        {
            // Soft-dep: treat unparsable requires as unmet so the node is skipped with a warning.
            yield return new RequireGroup { Alternatives = ["__unparsable__"] };
            yield break;
        }

        foreach (RequireGroup group in groups)
        {
            yield return group;
        }
    }
}
