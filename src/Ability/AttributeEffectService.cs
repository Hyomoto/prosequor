using Prosequor.Ability.Hooks;
using Prosequor.Data;
using Prosequor.Player;
using Vintagestory.API.Common.Entities;

namespace Prosequor.Ability;

/// <summary>
/// Routes attribute score changes to phase memo invalidation, ability-cache rebuild,
/// and registered phase refresh sinks (e.g. health → MarkDirty).
/// </summary>
public sealed class AttributeEffectService
{
    readonly AttributeEffectIndex index;
    readonly PhaseRefreshRegistry refresh;

    public AttributeEffectService(AttributeEffectIndex index, PhaseRefreshRegistry refresh)
    {
        this.index = index;
        this.refresh = refresh;
    }

    public void OnAttributeScoreChanged(Entity entity, string attributeId, EntityBehaviorProgress progress)
    {
        IReadOnlyList<(HookId Hook, VerbId Verb, PhaseId Phase)> phases = index.ForAttribute(attributeId);
        if (phases.Count == 0)
        {
            return;
        }

        foreach ((HookId hook, VerbId verb, PhaseId phase) in phases)
        {
            progress.ComposeMemo.InvalidatePhase(hook, verb, phase);
        }

        progress.RebuildAbilityCachePublic();

        foreach ((HookId hook, VerbId verb, PhaseId phase) in phases)
        {
            refresh.Apply(hook, verb, phase, entity);
        }
    }

    public void OnAllAttributesApplied(Entity entity, EntityBehaviorProgress progress)
    {
        HashSet<(HookId, VerbId, PhaseId)> seen = new();
        List<(HookId Hook, VerbId Verb, PhaseId Phase)> distinct = new();
        foreach (string id in AttributeIds.All)
        {
            foreach ((HookId hook, VerbId verb, PhaseId phase) in index.ForAttribute(id))
            {
                if (seen.Add((hook, verb, phase)))
                {
                    distinct.Add((hook, verb, phase));
                }
            }
        }

        if (distinct.Count == 0)
        {
            return;
        }

        foreach ((HookId hook, VerbId verb, PhaseId phase) in distinct)
        {
            progress.ComposeMemo.InvalidatePhase(hook, verb, phase);
        }

        progress.RebuildAbilityCachePublic();

        foreach ((HookId hook, VerbId verb, PhaseId phase) in distinct)
        {
            refresh.Apply(hook, verb, phase, entity);
        }
    }
}
