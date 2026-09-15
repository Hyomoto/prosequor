using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;

namespace Prosequor.Xp;

/// <summary>
/// Bool gate for the collect-XP pipeline. Stamped stacks are eligible to enqueue on pickup;
/// clearance happens when enqueued so inventory units never re-grant.
/// </summary>
public static class CollectXpStamp
{
    public const string AttrKey = "prosequorCollectXp";

    public static bool Has(ItemStack? stack) =>
        stack?.Attributes != null && stack.Attributes.GetBool(AttrKey, defaultValue: false);

    public static void Set(ItemStack? stack)
    {
        if (stack?.Attributes == null)
        {
            return;
        }

        stack.Attributes.SetBool(AttrKey, true);
    }

    public static void Clear(ItemStack? stack)
    {
        if (stack?.Attributes == null || !stack.Attributes.HasAttribute(AttrKey))
        {
            return;
        }

        stack.Attributes.RemoveAttribute(AttrKey);
    }

    /// <summary>Copy stamp from <paramref name="source"/> onto <paramref name="dest"/> when present.</summary>
    public static void CopyIfPresent(ItemStack? source, ItemStack? dest)
    {
        if (!Has(source) || dest?.Attributes == null)
        {
            return;
        }

        Set(dest);
    }

    public static bool Has(ITreeAttribute? tree) =>
        tree != null && tree.GetBool(AttrKey, defaultValue: false);

    public static void Set(ITreeAttribute? tree)
    {
        if (tree == null)
        {
            return;
        }

        tree.SetBool(AttrKey, true);
    }

    public static void Clear(ITreeAttribute? tree)
    {
        if (tree == null || !tree.HasAttribute(AttrKey))
        {
            return;
        }

        tree.RemoveAttribute(AttrKey);
    }

    /// <summary>
    /// Apply stamp onto every stack in <paramref name="stacks"/> when the BE/source tree is stamped.
    /// </summary>
    public static void ApplyToStacks(bool stamped, ItemStack[]? stacks)
    {
        if (!stamped || stacks == null || stacks.Length == 0)
        {
            return;
        }

        for (int i = 0; i < stacks.Length; i++)
        {
            Set(stacks[i]);
        }
    }

    public static void ApplyToStack(bool stamped, ItemStack? stack)
    {
        if (stamped)
        {
            Set(stack);
        }
    }
}
