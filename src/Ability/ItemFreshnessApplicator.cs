using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;

namespace Prosequor.Ability;

/// <summary>
/// Reads and writes perish <c>transitionstate.freshHours</c> for stack projectors.
/// </summary>
public static class ItemFreshnessApplicator
{
    /// <summary>
    /// True when the stack has a perish transition and is not time-frozen
    /// (vanilla skips transition updates when <c>timeFrozen</c> is set).
    /// </summary>
    public static bool CanImproveFreshness(IWorldAccessor world, ItemStack? stack)
    {
        if (world == null || stack?.Collectible == null)
        {
            return false;
        }

        if (stack.Attributes?.GetBool("timeFrozen") == true)
        {
            return false;
        }

        return TryGetPerishIndex(world, stack, out _);
    }

    public static bool TryGetFreshHours(IWorldAccessor world, ItemStack stack, out float hours)
    {
        hours = 0f;
        if (!EnsurePerishState(world, stack, out int perishIndex, out float[] freshHours))
        {
            return false;
        }

        hours = freshHours[perishIndex];
        return true;
    }

    public static bool TrySetFreshHours(IWorldAccessor world, ItemStack stack, float hours)
    {
        if (!EnsurePerishState(world, stack, out int perishIndex, out float[] freshHours))
        {
            return false;
        }

        if (stack.Attributes?["transitionstate"] is not ITreeAttribute tree)
        {
            return false;
        }

        freshHours[perishIndex] = Math.Max(0f, hours);
        tree["freshHours"] = new FloatArrayAttribute(freshHours);
        return true;
    }

    /// <summary>
    /// Initializes perish transition state if needed, then scales the perish fresh-hours entry.
    /// Prefer <see cref="TryGetFreshHours"/> / <see cref="TrySetFreshHours"/> for number projectors.
    /// </summary>
    public static bool TryApplyExtraFraction(IWorldAccessor world, ItemStack stack, float extraFraction)
    {
        if (world == null || stack == null || extraFraction <= 0f || !CanImproveFreshness(world, stack))
        {
            return false;
        }

        if (!TryGetFreshHours(world, stack, out float hours))
        {
            return false;
        }

        return TrySetFreshHours(world, stack, hours * (1f + extraFraction));
    }

    static bool EnsurePerishState(
        IWorldAccessor world,
        ItemStack stack,
        out int perishIndex,
        out float[] freshHours)
    {
        perishIndex = -1;
        freshHours = Array.Empty<float>();
        if (world == null || stack == null || !CanImproveFreshness(world, stack))
        {
            return false;
        }

        CollectibleObject collectible = stack.Collectible;
        if (!TryGetPerishIndex(world, stack, out perishIndex))
        {
            return false;
        }

        collectible.UpdateAndGetTransitionState(world, new DummySlot(stack), EnumTransitionType.Perish);
        if (stack.Attributes?["transitionstate"] is not ITreeAttribute tree)
        {
            return false;
        }

        if (tree["freshHours"] is not FloatArrayAttribute freshHoursAttr)
        {
            return false;
        }

        freshHours = freshHoursAttr.value;
        return perishIndex < freshHours.Length;
    }

    static bool TryGetPerishIndex(IWorldAccessor world, ItemStack stack, out int perishIndex)
    {
        perishIndex = -1;
        TransitionableProperties[]? props = stack.Collectible?.GetTransitionableProperties(world, stack, null);
        if (props == null || props.Length == 0)
        {
            return false;
        }

        for (int i = 0; i < props.Length; i++)
        {
            if (props[i]?.Type == EnumTransitionType.Perish)
            {
                perishIndex = i;
                return true;
            }
        }

        return false;
    }
}
