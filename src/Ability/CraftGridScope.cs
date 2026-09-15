using Vintagestory.API.Common;

namespace Prosequor.Ability;

/// <summary>
/// Thread-scoped craft-grid ingredient snapshot for take-time adapters.
/// Prefix pushes clones while the grid is still full; consumers read via
/// <see cref="TryGet"/>; Finalizer pops.
/// </summary>
public static class CraftGridScope
{
    [ThreadStatic]
    static ItemStack[]? ingredients;

    [ThreadStatic]
    static int depth;

    public static void Push(ItemStack[] snapshot)
    {
        depth++;
        ingredients = snapshot;
    }

    public static void Pop()
    {
        depth--;
        if (depth <= 0)
        {
            depth = 0;
            ingredients = null;
        }
    }

    public static bool TryGet(out IReadOnlyList<ItemStack> snapshot)
    {
        if (ingredients != null)
        {
            snapshot = ingredients;
            return true;
        }

        snapshot = Array.Empty<ItemStack>();
        return false;
    }

    /// <summary>Clones non-empty ingredient stacks from a crafting inventory (excludes output).</summary>
    public static ItemStack[] CaptureIngredients(InventoryBase? inventory)
    {
        if (inventory == null || inventory.Count <= 1)
        {
            return Array.Empty<ItemStack>();
        }

        // Crafting grid: last slot is output; preceding slots are ingredients.
        int ingredientCount = inventory.Count - 1;
        List<ItemStack> clones = new(ingredientCount);
        for (int i = 0; i < ingredientCount; i++)
        {
            ItemStack? stack = inventory[i]?.Itemstack;
            if (stack != null)
            {
                clones.Add(stack.Clone());
            }
        }

        return clones.Count == 0 ? Array.Empty<ItemStack>() : clones.ToArray();
    }
}
