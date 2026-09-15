using Prosequor.Ability;

namespace Prosequor.Ability.Hooks;

/// <summary>
/// Stable hash of the match bag <see cref="AbilityWhenFilter"/> uses
/// (verb, roles, tokens, damage, op, inputs). Position is excluded.
/// </summary>
public readonly struct FactFingerprint : IEquatable<FactFingerprint>
{
    public static readonly FactFingerprint Empty = new(
        "",
        null,
        null,
        null,
        null,
        null,
        null,
        null,
        Array.Empty<string>(),
        Array.Empty<string>(),
        Array.Empty<string>());

    readonly string verb;
    readonly string? held;
    readonly string? target;
    readonly string? drop;
    readonly string? lastCraft;
    readonly string? ground;
    readonly string? mount;
    readonly string? op;
    readonly string[] tokens;
    readonly string[] damage;
    readonly string[] inputs;
    readonly int hashCode;

    FactFingerprint(
        string verb,
        string? held,
        string? target,
        string? drop,
        string? lastCraft,
        string? ground,
        string? mount,
        string? op,
        string[] tokens,
        string[] damage,
        string[] inputs)
    {
        this.verb = verb;
        this.held = held;
        this.target = target;
        this.drop = drop;
        this.lastCraft = lastCraft;
        this.ground = ground;
        this.mount = mount;
        this.op = op;
        this.tokens = tokens;
        this.damage = damage;
        this.inputs = inputs;
        hashCode = ComputeHash(
            verb, held, target, drop, lastCraft, ground, mount, op, tokens, damage, inputs);
    }

    public static FactFingerprint From(AbilityAction? fact)
    {
        if (fact == null)
        {
            return Empty;
        }

        string[] sortedTokens = Sort(fact.Tokens);
        string[] sortedDamage = Sort(fact.Damage);
        string[] sortedInputs = SortList(fact.Inputs);
        return new FactFingerprint(
            fact.Verb ?? "",
            fact.Held,
            fact.Target,
            fact.Drop,
            fact.LastCraft,
            fact.Ground,
            fact.Mount,
            fact.Op,
            sortedTokens,
            sortedDamage,
            sortedInputs);
    }

    public bool Equals(FactFingerprint other) =>
        hashCode == other.hashCode
        && string.Equals(verb, other.verb, StringComparison.OrdinalIgnoreCase)
        && string.Equals(held, other.held, StringComparison.OrdinalIgnoreCase)
        && string.Equals(target, other.target, StringComparison.OrdinalIgnoreCase)
        && string.Equals(drop, other.drop, StringComparison.OrdinalIgnoreCase)
        && string.Equals(lastCraft, other.lastCraft, StringComparison.OrdinalIgnoreCase)
        && string.Equals(ground, other.ground, StringComparison.OrdinalIgnoreCase)
        && string.Equals(mount, other.mount, StringComparison.OrdinalIgnoreCase)
        && string.Equals(op, other.op, StringComparison.OrdinalIgnoreCase)
        && StringsEqual(tokens, other.tokens)
        && StringsEqual(damage, other.damage)
        && StringsEqual(inputs, other.inputs);

    public override bool Equals(object? obj) => obj is FactFingerprint other && Equals(other);

    public override int GetHashCode() => hashCode;

    static string[] Sort(IReadOnlySet<string> set)
    {
        if (set.Count == 0)
        {
            return Array.Empty<string>();
        }

        string[] arr = set.ToArray();
        Array.Sort(arr, StringComparer.OrdinalIgnoreCase);
        return arr;
    }

    static string[] SortList(IReadOnlyList<string> list)
    {
        if (list.Count == 0)
        {
            return Array.Empty<string>();
        }

        string[] arr = new string[list.Count];
        for (int i = 0; i < list.Count; i++)
        {
            arr[i] = list[i];
        }

        Array.Sort(arr, StringComparer.OrdinalIgnoreCase);
        return arr;
    }

    static bool StringsEqual(string[] a, string[] b)
    {
        if (a.Length != b.Length)
        {
            return false;
        }

        for (int i = 0; i < a.Length; i++)
        {
            if (!string.Equals(a[i], b[i], StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }

    static int ComputeHash(
        string verb,
        string? held,
        string? target,
        string? drop,
        string? lastCraft,
        string? ground,
        string? mount,
        string? op,
        string[] tokens,
        string[] damage,
        string[] inputs)
    {
        HashCode hash = new();
        hash.Add(verb, StringComparer.OrdinalIgnoreCase);
        hash.Add(held, StringComparer.OrdinalIgnoreCase);
        hash.Add(target, StringComparer.OrdinalIgnoreCase);
        hash.Add(drop, StringComparer.OrdinalIgnoreCase);
        hash.Add(lastCraft, StringComparer.OrdinalIgnoreCase);
        hash.Add(ground, StringComparer.OrdinalIgnoreCase);
        hash.Add(mount, StringComparer.OrdinalIgnoreCase);
        hash.Add(op, StringComparer.OrdinalIgnoreCase);
        foreach (string t in tokens)
        {
            hash.Add(t, StringComparer.OrdinalIgnoreCase);
        }

        foreach (string d in damage)
        {
            hash.Add(d, StringComparer.OrdinalIgnoreCase);
        }

        foreach (string input in inputs)
        {
            hash.Add(input, StringComparer.OrdinalIgnoreCase);
        }

        return hash.ToHashCode();
    }
}
