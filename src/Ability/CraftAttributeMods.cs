using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;

namespace Prosequor.Ability;

/// <summary>
/// Craft attribute multipliers under <c>prosequorMods</c>. Stack tree is the display
/// cache; pedigree blob is the unit of record. Never write type-level
/// <see cref="ItemStack.ItemAttributes"/>.
/// </summary>
public static class CraftAttributeMods
{
    public const string TreeAttr = "prosequorMods";

    public static IReadOnlyList<ProsequorBlob.ModFactor> GetAll(ItemStack? stack)
    {
        if (stack?.Attributes == null)
        {
            return Array.Empty<ProsequorBlob.ModFactor>();
        }

        ITreeAttribute? tree = stack.Attributes.GetTreeAttribute(TreeAttr);
        if (tree == null)
        {
            return Array.Empty<ProsequorBlob.ModFactor>();
        }

        List<ProsequorBlob.ModFactor> list = new();
        foreach (KeyValuePair<string, IAttribute> kv in tree)
        {
            if (string.IsNullOrWhiteSpace(kv.Key))
            {
                continue;
            }

            float factor = tree.GetFloat(kv.Key, 0f);
            if (!float.IsFinite(factor) || factor <= 0f)
            {
                continue;
            }

            list.Add(new ProsequorBlob.ModFactor(kv.Key.Trim(), factor));
        }

        return list;
    }

    /// <summary>Replace the bag (empty removes the tree). Does not stamp pedigree.</summary>
    public static void WriteAll(ItemStack? stack, IReadOnlyList<ProsequorBlob.ModFactor>? mods)
    {
        if (stack?.Attributes == null)
        {
            return;
        }

        if (mods == null || mods.Count == 0)
        {
            if (stack.Attributes.HasAttribute(TreeAttr))
            {
                stack.Attributes.RemoveAttribute(TreeAttr);
            }

            return;
        }

        ITreeAttribute tree = stack.Attributes.GetOrAddTreeAttribute(TreeAttr);
        List<string> stale = new();
        foreach (KeyValuePair<string, IAttribute> kv in tree)
        {
            stale.Add(kv.Key);
        }

        for (int i = 0; i < stale.Count; i++)
        {
            tree.RemoveAttribute(stale[i]);
        }

        for (int i = 0; i < mods.Count; i++)
        {
            ProsequorBlob.ModFactor mod = mods[i];
            if (string.IsNullOrWhiteSpace(mod.Key) || !float.IsFinite(mod.Factor) || mod.Factor <= 0f)
            {
                continue;
            }

            tree.SetFloat(mod.Key.Trim(), mod.Factor);
        }
    }

    public static float GetFactor(ItemStack? stack, string key)
    {
        if (stack?.Attributes == null || string.IsNullOrWhiteSpace(key))
        {
            return 1f;
        }

        ITreeAttribute? tree = stack.Attributes.GetTreeAttribute(TreeAttr);
        if (tree == null || !tree.HasAttribute(key))
        {
            return 1f;
        }

        float factor = tree.GetFloat(key, 1f);
        return factor > 0f ? factor : 1f;
    }

    public static void MultiplyFactor(ItemStack stack, string key, float factor)
    {
        if (stack?.Attributes == null || string.IsNullOrWhiteSpace(key) || factor <= 0f)
        {
            return;
        }

        if (Math.Abs(factor - 1f) < 0.0001f)
        {
            return;
        }

        ITreeAttribute tree = stack.Attributes.GetOrAddTreeAttribute(TreeAttr);
        float current = tree.HasAttribute(key) ? tree.GetFloat(key, 1f) : 1f;
        if (current <= 0f)
        {
            current = 1f;
        }

        tree.SetFloat(key, current * factor);
        ProsequorStackPedigree.StampSurfaceFromStack(stack);
    }

    /// <summary>Writes an absolute factor under <c>prosequorMods</c> (no-op when unchanged).</summary>
    public static void SetFactor(ItemStack stack, string key, float factor)
    {
        if (stack?.Attributes == null || string.IsNullOrWhiteSpace(key) || factor <= 0f)
        {
            return;
        }

        float current = GetFactor(stack, key);
        if (Math.Abs(factor - current) < 0.0001f)
        {
            return;
        }

        ITreeAttribute tree = stack.Attributes.GetOrAddTreeAttribute(TreeAttr);
        tree.SetFloat(key, factor);
        ProsequorStackPedigree.StampSurfaceFromStack(stack);
    }
}
