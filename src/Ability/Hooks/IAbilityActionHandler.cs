using Newtonsoft.Json.Linq;

namespace Prosequor.Ability.Hooks;

/// <summary>
/// Registered action for exactly one hook/verb/phase shape.
/// Runtime Apply is type-checked against the phase's context and value types.
/// </summary>
public interface IAbilityActionHandler
{
    ActionId Id { get; }
    HookId Hook { get; }
    VerbId Verb { get; }
    PhaseId Phase { get; }
    Type ContextType { get; }
    Type ValueType { get; }
    Type ParamsType { get; }

    bool TryParseParams(JObject? raw, out object parameters, out string error);

    object Apply(IHookContext context, object value, object parameters, AbilityRuleSource source);
}

/// <summary>Helper base for strongly typed action handlers.</summary>
public abstract class AbilityActionHandler<TContext, TValue, TParams> : IAbilityActionHandler
    where TContext : class, IHookContext
{
    public abstract ActionId Id { get; }
    public abstract HookId Hook { get; }
    public abstract VerbId Verb { get; }
    public abstract PhaseId Phase { get; }

    public Type ContextType => typeof(TContext);
    public Type ValueType => typeof(TValue);
    public Type ParamsType => typeof(TParams);

    public bool TryParseParams(JObject? raw, out object parameters, out string error)
    {
        if (!TryParse(raw, out TParams? typed, out error) || typed == null)
        {
            parameters = null!;
            return false;
        }

        parameters = typed;
        return true;
    }

    public object Apply(IHookContext context, object value, object parameters, AbilityRuleSource source)
    {
        if (context is not TContext typedContext)
        {
            throw new InvalidOperationException(
                $"Action '{Id}' expected context {typeof(TContext).Name}, got {context.GetType().Name}.");
        }

        if (value is not TValue typedValue)
        {
            throw new InvalidOperationException(
                $"Action '{Id}' expected value {typeof(TValue).Name}, got {value.GetType().Name}.");
        }

        if (parameters is not TParams typedParams)
        {
            throw new InvalidOperationException(
                $"Action '{Id}' expected params {typeof(TParams).Name}, got {parameters.GetType().Name}.");
        }

        return Apply(typedContext, typedValue, typedParams, source)!;
    }

    protected abstract bool TryParse(JObject? raw, out TParams? parameters, out string error);

    protected abstract TValue Apply(
        TContext context,
        TValue value,
        TParams parameters,
        AbilityRuleSource source);
}
