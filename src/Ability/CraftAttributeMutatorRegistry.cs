using Vintagestory.API.Common;
using Vintagestory.GameContent;

namespace Prosequor.Ability;

/// <summary>
/// One legal craft-time attribute key. Accepts decides whether a stack may receive it;
/// <see cref="OnStamped"/> runs after the factor is written (e.g. sync remaining durability).
/// </summary>
public interface ICraftAttributeMutator
{
    string Key { get; }

    bool Accepts(ItemStack stack);

    /// <param name="previousFactor">Factor before this write (1 when unset).</param>
    /// <param name="world">Optional; required for mutators that touch perish state.</param>
    void OnStamped(ItemStack stack, float factor, float previousFactor, IWorldAccessor? world);
}

/// <summary>Lookup table of craft attribute mutators by key.</summary>
public sealed class CraftAttributeMutatorRegistry
{
    readonly Dictionary<string, ICraftAttributeMutator> byKey =
        new(StringComparer.OrdinalIgnoreCase);

    public void Register(ICraftAttributeMutator mutator)
    {
        if (mutator == null || string.IsNullOrWhiteSpace(mutator.Key))
        {
            throw new ArgumentException("Mutator and key are required.", nameof(mutator));
        }

        byKey[mutator.Key] = mutator;
    }

    public bool TryGet(string key, out ICraftAttributeMutator? mutator) =>
        byKey.TryGetValue(key, out mutator);

    public bool Accepts(ItemStack? stack, string key)
    {
        if (stack?.Collectible == null
            || !TryGet(key, out ICraftAttributeMutator? mutator)
            || mutator == null)
        {
            return false;
        }

        return mutator.Accepts(stack);
    }

    /// <summary>
    /// Multiplies the stamped factor when the key is registered and the stack accepts it.
    /// Unknown or illegal keys are ignored.
    /// </summary>
    public bool Multiply(ItemStack? stack, string key, float factor, IWorldAccessor? world = null)
    {
        if (stack?.Collectible == null
            || string.IsNullOrWhiteSpace(key)
            || factor <= 0f
            || Math.Abs(factor - 1f) < 0.0001f)
        {
            return false;
        }

        if (!TryGet(key, out ICraftAttributeMutator? mutator) || mutator == null)
        {
            return false;
        }

        if (!mutator.Accepts(stack))
        {
            return false;
        }

        float previous = CraftAttributeMods.GetFactor(stack, key);
        CraftAttributeMods.MultiplyFactor(stack, key, factor);
        float next = CraftAttributeMods.GetFactor(stack, key);
        mutator.OnStamped(stack, next, previous, world);
        return true;
    }

    /// <summary>
    /// Sets the stamped factor absolutely when the key is registered and the stack accepts it.
    /// Unknown or illegal keys are ignored.
    /// </summary>
    public bool Set(ItemStack? stack, string key, float factor, IWorldAccessor? world = null)
    {
        if (stack?.Collectible == null
            || string.IsNullOrWhiteSpace(key)
            || factor <= 0f)
        {
            return false;
        }

        if (!TryGet(key, out ICraftAttributeMutator? mutator) || mutator == null)
        {
            return false;
        }

        if (!mutator.Accepts(stack))
        {
            return false;
        }

        float previous = CraftAttributeMods.GetFactor(stack, key);
        if (Math.Abs(factor - previous) < 0.0001f)
        {
            return false;
        }

        CraftAttributeMods.SetFactor(stack, key, factor);
        mutator.OnStamped(stack, CraftAttributeMods.GetFactor(stack, key), previous, world);
        return true;
    }

    /// <summary>
    /// Re-runs <see cref="ICraftAttributeMutator.OnStamped"/> for each stamped mod
    /// (e.g. after copying pedigree onto a freshly minted meal bowl).
    /// </summary>
    public void Rematerialize(ItemStack? stack, IWorldAccessor? world)
    {
        if (stack?.Collectible == null || world == null)
        {
            return;
        }

        IReadOnlyList<ProsequorBlob.ModFactor> mods = CraftAttributeMods.GetAll(stack);
        for (int i = 0; i < mods.Count; i++)
        {
            ProsequorBlob.ModFactor mod = mods[i];
            if (!TryGet(mod.Key, out ICraftAttributeMutator? mutator) || mutator == null)
            {
                continue;
            }

            if (!mutator.Accepts(stack))
            {
                continue;
            }

            // Treat as first materialization onto this host (bowl hours are post-SetContents).
            mutator.OnStamped(stack, mod.Factor, previousFactor: 1f, world);
        }
    }
}

/// <summary>Durability mutator: max durability &gt; 1; syncs remaining to new max on stamp.</summary>
public sealed class DurabilityAttributeMutator : ICraftAttributeMutator
{
    public const string KeyName = "durability";

    public string Key => KeyName;

    public bool Accepts(ItemStack stack)
    {
        if (stack?.Collectible == null)
        {
            return false;
        }

        // Use type Durability to avoid Harmony recursion during accept checks.
        return stack.Collectible.Durability > 1;
    }

    public void OnStamped(ItemStack stack, float factor, float previousFactor, IWorldAccessor? world)
    {
        _ = factor;
        _ = previousFactor;
        _ = world;
        if (stack?.Collectible == null)
        {
            return;
        }

        int max = stack.Collectible.GetMaxDurability(stack);
        if (max > 0)
        {
            stack.Collectible.SetDurability(stack, max);
        }
    }
}
