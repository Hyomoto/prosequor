using Vintagestory.API.Common;

namespace Prosequor.Ability;

/// <summary>
/// Thread-local meal stack during <see cref="Vintagestory.GameContent.BlockMeal.Consume"/>
/// so hunger receive can apply pedigree satiety / hungerDelay factors.
/// </summary>
public static class MealEatScope
{
    [ThreadStatic]
    static ItemStack? stack;

    [ThreadStatic]
    static int depth;

    public static ItemStack? Current => depth > 0 ? stack : null;

    public static void Begin(ItemStack? mealStack)
    {
        if (depth == 0)
        {
            stack = mealStack;
        }

        depth++;
    }

    public static void End()
    {
        if (depth <= 0)
        {
            depth = 0;
            stack = null;
            return;
        }

        depth--;
        if (depth == 0)
        {
            stack = null;
        }
    }
}
