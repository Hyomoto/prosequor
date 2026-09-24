using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.GameContent;

namespace Prosequor.Ability;

/// <summary>Shared Accept helpers for craft attribute mutators.</summary>
public static class CraftAttributeMutatorAccept
{
    public static bool HasNonZeroItemAttr(ItemStack stack, string key)
    {
        JsonObject? attrs = stack.ItemAttributes;
        if (attrs == null)
        {
            return false;
        }

        JsonObject token = attrs[key];
        return token != null && token.Exists && Math.Abs(token.AsFloat(0f)) > 0.0001f;
    }

    public static bool HasProtection(ItemStack stack)
    {
        CollectibleBehaviorWearable? wearable =
            stack.Collectible?.GetCollectibleBehavior<CollectibleBehaviorWearable>(false);
        if (wearable == null)
        {
            return false;
        }

        return wearable.GetProtectionModifiers(new DummySlot(stack)) != null;
    }

    /// <summary>
    /// Cooked pot / meal hosts may store a freshness factor before serve rematerializes hours.
    /// </summary>
    public static bool IsCookedMealHost(ItemStack stack)
    {
        if (stack?.Collectible == null)
        {
            return false;
        }

        if (stack.Collectible is BlockCookedContainer or BlockMeal)
        {
            return true;
        }

        string? path = stack.Collectible.Code?.Path;
        return path != null
            && (path.Contains("-cooked", StringComparison.OrdinalIgnoreCase)
                || path.Contains("-meal", StringComparison.OrdinalIgnoreCase));
    }
}

/// <summary>Warmth mutator: type <c>warmth</c> &gt; 0.</summary>
public sealed class WarmthAttributeMutator : ICraftAttributeMutator
{
    public const string KeyName = "warmth";

    public string Key => KeyName;

    public bool Accepts(ItemStack stack) =>
        CraftAttributeMutatorAccept.HasNonZeroItemAttr(stack, KeyName);

    public void OnStamped(ItemStack stack, float factor, float previousFactor, IWorldAccessor? world)
    {
        _ = stack;
        _ = factor;
        _ = previousFactor;
        _ = world;
    }
}

/// <summary>Cooling mutator: type <c>cooling</c> present (HoD). Reader is optional.</summary>
public sealed class CoolingAttributeMutator : ICraftAttributeMutator
{
    public const string KeyName = "cooling";

    public string Key => KeyName;

    public bool Accepts(ItemStack stack) =>
        CraftAttributeMutatorAccept.HasNonZeroItemAttr(stack, KeyName);

    public void OnStamped(ItemStack stack, float factor, float previousFactor, IWorldAccessor? world)
    {
        _ = stack;
        _ = factor;
        _ = previousFactor;
        _ = world;
    }
}

/// <summary>Protection mutator: wearable with protection modifiers.</summary>
public sealed class ProtectionAttributeMutator : ICraftAttributeMutator
{
    public const string KeyName = "protection";

    public string Key => KeyName;

    public bool Accepts(ItemStack stack) =>
        CraftAttributeMutatorAccept.HasProtection(stack);

    public void OnStamped(ItemStack stack, float factor, float previousFactor, IWorldAccessor? world)
    {
        _ = stack;
        _ = factor;
        _ = previousFactor;
        _ = world;
    }
}

/// <summary>
/// Freshness mutator: scales perish <c>freshHours</c> by the stamped factor ratio.
/// Accepts perishable stacks and cooked meal hosts (factor may live on the pot until serve).
/// </summary>
public sealed class FreshnessAttributeMutator : ICraftAttributeMutator
{
    public const string KeyName = "freshness";

    public string Key => KeyName;

    public bool Accepts(ItemStack stack)
    {
        if (stack?.Collectible == null)
        {
            return false;
        }

        if (CraftAttributeMutatorAccept.IsCookedMealHost(stack))
        {
            return true;
        }

        TransitionableProperties[]? props = stack.Collectible.TransitionableProps;
        if (props == null || props.Length == 0)
        {
            return false;
        }

        for (int i = 0; i < props.Length; i++)
        {
            if (props[i]?.Type == EnumTransitionType.Perish)
            {
                return true;
            }
        }

        return false;
    }

    public void OnStamped(ItemStack stack, float factor, float previousFactor, IWorldAccessor? world)
    {
        if (world == null || stack == null || factor <= 0f)
        {
            return;
        }

        float prev = previousFactor > 0f ? previousFactor : 1f;
        float ratio = factor / prev;
        if (Math.Abs(ratio - 1f) < 0.0001f)
        {
            return;
        }

        if (!ItemFreshnessApplicator.CanImproveFreshness(world, stack))
        {
            return;
        }

        if (!ItemFreshnessApplicator.TryGetFreshHours(world, stack, out float hours))
        {
            return;
        }

        ItemFreshnessApplicator.TrySetFreshHours(world, stack, hours * ratio);
    }
}

/// <summary>
/// Meal satiety factor (Very Filling). Storage-only; applied at eat with nutrition compensation.
/// </summary>
public sealed class SatietyAttributeMutator : ICraftAttributeMutator
{
    public const string KeyName = "satiety";

    public string Key => KeyName;

    public bool Accepts(ItemStack stack) =>
        CraftAttributeMutatorAccept.IsCookedMealHost(stack);

    public void OnStamped(ItemStack stack, float factor, float previousFactor, IWorldAccessor? world)
    {
        _ = stack;
        _ = factor;
        _ = previousFactor;
        _ = world;
    }
}

/// <summary>
/// Meal hunger-delay factor (Nothing Wasted). Storage-only; applied at eat on saturationLossDelay.
/// </summary>
public sealed class HungerDelayAttributeMutator : ICraftAttributeMutator
{
    public const string KeyName = "hungerDelay";

    public string Key => KeyName;

    public bool Accepts(ItemStack stack) =>
        CraftAttributeMutatorAccept.IsCookedMealHost(stack);

    public void OnStamped(ItemStack stack, float factor, float previousFactor, IWorldAccessor? world)
    {
        _ = stack;
        _ = factor;
        _ = previousFactor;
        _ = world;
    }
}

/// <summary>
/// Arrow flight-speed factor (Fletcher). Storage-only; applied when the projectile launches.
/// </summary>
public sealed class FlightAttributeMutator : ICraftAttributeMutator
{
    public const string KeyName = "flight";

    public string Key => KeyName;

    public bool Accepts(ItemStack stack) =>
        stack?.Collectible is ItemArrow
        || (stack?.Collectible?.Code?.Path?.StartsWith("arrow-", StringComparison.OrdinalIgnoreCase) ?? false);

    public void OnStamped(ItemStack stack, float factor, float previousFactor, IWorldAccessor? world)
    {
        _ = stack;
        _ = factor;
        _ = previousFactor;
        _ = world;
    }
}

/// <summary>
/// Bow accuracy factor (Bowyer). Storage-only; applied onto the holder&apos;s rangedWeaponsAcc.
/// </summary>
public sealed class RangedAccAttributeMutator : ICraftAttributeMutator
{
    public const string KeyName = "rangedAcc";

    public string Key => KeyName;

    public bool Accepts(ItemStack stack) =>
        stack?.Collectible?.Tool == EnumTool.Bow
        || (stack?.Collectible?.Attributes?["statModifier"]?["rangedWeaponsAcc"] != null
            && stack.Collectible.Attributes["statModifier"]["rangedWeaponsAcc"].Exists);

    public void OnStamped(ItemStack stack, float factor, float previousFactor, IWorldAccessor? world)
    {
        _ = stack;
        _ = factor;
        _ = previousFactor;
        _ = world;
    }
}

/// <summary>
/// Distilled intoxication factor (Strong Spirits). Storage-only; drink reader is a follow-up.
/// </summary>
public sealed class IntoxicationAttributeMutator : ICraftAttributeMutator
{
    public const string KeyName = "intoxication";

    public string Key => KeyName;

    public bool Accepts(ItemStack stack) => ProsequorLiquidPedigree.IsPortion(stack);

    public void OnStamped(ItemStack stack, float factor, float previousFactor, IWorldAccessor? world)
    {
        _ = stack;
        _ = factor;
        _ = previousFactor;
        _ = world;
    }
}

/// <summary>
/// Healing-item regen factor (Herbal Remedies / Chirurgeon). Storage-only; read at apply.
/// </summary>
public sealed class RegenAttributeMutator : ICraftAttributeMutator
{
    public const string KeyName = "regen";

    public string Key => KeyName;

    public bool Accepts(ItemStack stack) =>
        stack?.Collectible?.GetCollectibleBehavior<CollectibleBehaviorHealingItem>(false) != null;

    public void OnStamped(ItemStack stack, float factor, float previousFactor, IWorldAccessor? world)
    {
        _ = stack;
        _ = factor;
        _ = previousFactor;
        _ = world;
    }
}

/// <summary>
/// Distilled merchant-price factor (Prized Liquors). Storage-only; sale reader is a follow-up.
/// </summary>
public sealed class PriceAttributeMutator : ICraftAttributeMutator
{
    public const string KeyName = "price";

    public string Key => KeyName;

    public bool Accepts(ItemStack stack) => ProsequorLiquidPedigree.IsPortion(stack);

    public void OnStamped(ItemStack stack, float factor, float previousFactor, IWorldAccessor? world)
    {
        _ = stack;
        _ = factor;
        _ = previousFactor;
        _ = world;
    }
}
