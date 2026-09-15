using Prosequor.Ability.Hooks;

namespace Prosequor.Data;

/// <summary>
/// Reverse index: attribute id → distinct (hook, verb, phase) addresses touched by its rules.
/// Built once after attribute stats compile.
/// </summary>
public sealed class AttributeEffectIndex
{
    readonly Dictionary<string, List<(HookId Hook, VerbId Verb, PhaseId Phase)>> byAttribute =
        new(StringComparer.OrdinalIgnoreCase);

    public static AttributeEffectIndex Empty { get; } = new();

    public static AttributeEffectIndex Build(IAttributeStatRegistry stats)
    {
        AttributeEffectIndex index = new();
        foreach (AttributeStatDef attr in stats.All)
        {
            HashSet<(HookId, VerbId, PhaseId)> seen = new();
            List<(HookId Hook, VerbId Verb, PhaseId Phase)> list = new();
            foreach (AbilityRule rule in attr.Rules)
            {
                if (seen.Add((rule.Hook, rule.Verb, rule.Phase)))
                {
                    list.Add((rule.Hook, rule.Verb, rule.Phase));
                }
            }

            if (list.Count > 0)
            {
                index.byAttribute[attr.Id] = list;
            }
        }

        return index;
    }

    public IReadOnlyList<(HookId Hook, VerbId Verb, PhaseId Phase)> ForAttribute(string attributeId)
    {
        if (byAttribute.TryGetValue(attributeId, out List<(HookId, VerbId, PhaseId)>? list))
        {
            return list;
        }

        return Array.Empty<(HookId, VerbId, PhaseId)>();
    }
}
