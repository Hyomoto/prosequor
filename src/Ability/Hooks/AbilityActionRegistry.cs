namespace Prosequor.Ability.Hooks;

public interface IAbilityActionRegistry
{
    IReadOnlyCollection<IAbilityActionHandler> All { get; }

    bool TryGet(
        ActionId id,
        HookId hook,
        VerbId verb,
        PhaseId phase,
        out IAbilityActionHandler handler);

    void Register(IAbilityActionHandler handler);
}

public sealed class AbilityActionRegistry : IAbilityActionRegistry
{
    readonly Dictionary<string, IAbilityActionHandler> byKey = new(StringComparer.OrdinalIgnoreCase);
    readonly IHookRegistry hooks;

    public AbilityActionRegistry(IHookRegistry hooks)
    {
        this.hooks = hooks;
    }

    public IReadOnlyCollection<IAbilityActionHandler> All => byKey.Values;

    public bool TryGet(
        ActionId id,
        HookId hook,
        VerbId verb,
        PhaseId phase,
        out IAbilityActionHandler handler)
    {
        if (byKey.TryGetValue(Key(id, hook, verb, phase), out IAbilityActionHandler? found)
            && found != null)
        {
            handler = found;
            return true;
        }

        handler = null!;
        return false;
    }

    public void Register(IAbilityActionHandler handler)
    {
        string key = Key(handler.Id, handler.Hook, handler.Verb, handler.Phase);
        if (byKey.ContainsKey(key))
        {
            throw new InvalidOperationException(
                $"Action '{handler.Id}' is already registered for {handler.Hook}/{handler.Verb}/{handler.Phase}.");
        }

        if (!hooks.TryGet(handler.Hook, out HookRegistration hook))
        {
            throw new InvalidOperationException(
                $"Action '{handler.Id}' references unknown hook '{handler.Hook}'.");
        }

        if (!hook.Phases.TryGetValue((handler.Verb, handler.Phase), out HookPhaseRegistration? phase))
        {
            throw new InvalidOperationException(
                $"Action '{handler.Id}' references unknown verb '{handler.Verb}' phase '{handler.Phase}' on hook '{handler.Hook}'.");
        }

        if (handler.ContextType != phase.ContextType)
        {
            throw new InvalidOperationException(
                $"Action '{handler.Id}' context type {handler.ContextType.Name} does not match phase shape {phase.ContextType.Name}.");
        }

        if (handler.ValueType != phase.ValueType)
        {
            throw new InvalidOperationException(
                $"Action '{handler.Id}' value type {handler.ValueType.Name} does not match phase shape {phase.ValueType.Name}.");
        }

        byKey[key] = handler;
    }

    static string Key(ActionId id, HookId hook, VerbId verb, PhaseId phase) =>
        id.Value + "|" + hook.Value + "|" + verb.Value + "|" + phase.Value;
}
