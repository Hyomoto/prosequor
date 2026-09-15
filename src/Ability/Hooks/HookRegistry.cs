using System;
using System.Collections.Generic;

namespace Prosequor.Ability.Hooks;

/// <summary>Declares a hook and the verb/phase shapes it exposes.</summary>
public sealed class HookRegistration
{
    public required HookId Id { get; init; }

    public Dictionary<(VerbId Verb, PhaseId Phase), HookPhaseRegistration> Phases { get; } = new();
}

public sealed class HookPhaseRegistration
{
    public required VerbId Verb { get; init; }
    public required PhaseId Phase { get; init; }
    public required Type ContextType { get; init; }
    public required Type ValueType { get; init; }
}

public interface IHookRegistry
{
    bool TryGet(HookId id, out HookRegistration hook);
    IReadOnlyCollection<HookRegistration> All { get; }

    void RegisterHook(HookId id);

    void RegisterPhase(HookId hook, VerbId verb, PhaseId phase, Type contextType, Type valueType);
}

public sealed class HookRegistry : IHookRegistry
{
    readonly Dictionary<string, HookRegistration> byId = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyCollection<HookRegistration> All => byId.Values;

    public bool TryGet(HookId id, out HookRegistration hook)
    {
        if (byId.TryGetValue(id.Value, out HookRegistration? found) && found != null)
        {
            hook = found;
            return true;
        }

        hook = null!;
        return false;
    }

    public void RegisterHook(HookId id)
    {
        if (byId.ContainsKey(id.Value))
        {
            throw new InvalidOperationException($"Hook '{id}' is already registered.");
        }

        byId[id.Value] = new HookRegistration
        {
            Id = id
        };
    }

    public void RegisterPhase(
        HookId hookId,
        VerbId verb,
        PhaseId phase,
        Type contextType,
        Type valueType)
    {
        if (!TryGet(hookId, out HookRegistration hook))
        {
            throw new InvalidOperationException($"Unknown hook '{hookId}'.");
        }

        (VerbId, PhaseId) key = (verb, phase);
        if (hook.Phases.ContainsKey(key))
        {
            throw new InvalidOperationException(
                $"Hook '{hookId}' already has verb '{verb}' phase '{phase}'.");
        }

        hook.Phases[key] = new HookPhaseRegistration
        {
            Verb = verb,
            Phase = phase,
            ContextType = contextType,
            ValueType = valueType
        };
    }
}
