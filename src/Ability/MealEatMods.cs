using Vintagestory.API.Common;

namespace Prosequor.Ability;

/// <summary>
/// Applies meal pedigree satiety / hungerDelay factors onto a ReceiveSaturation call.
/// Satiety scales saturation and divides nutritionGainMultiplier so nutrition stays fixed.
/// </summary>
public static class MealEatMods
{
    public static void Apply(
        ItemStack? meal,
        ref float saturation,
        ref float saturationLossDelay,
        ref float nutritionGainMultiplier)
    {
        if (meal == null)
        {
            return;
        }

        float satietyF = CraftAttributeMods.GetFactor(meal, SatietyAttributeMutator.KeyName);
        if (satietyF > 1.0001f)
        {
            saturation *= satietyF;
            nutritionGainMultiplier /= satietyF;
            if (nutritionGainMultiplier < 0.0001f)
            {
                nutritionGainMultiplier = 0.0001f;
            }
        }

        float delayF = CraftAttributeMods.GetFactor(meal, HungerDelayAttributeMutator.KeyName);
        if (delayF > 1.0001f)
        {
            saturationLossDelay *= delayF;
        }
    }
}
