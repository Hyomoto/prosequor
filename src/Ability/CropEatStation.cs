using System;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;

namespace Prosequor.Ability;

/// <summary>
/// Fixed-chance friendliness gain when an animal eats a farmland crop.
/// Credited to the crop planter; no Feedhand / trough pipeline and no XP deed.
/// </summary>
public static class CropEatStation
{
    /// <summary>
    /// Rolls <see cref="TroughEatStation.BaseFriendlinessChance"/>, then
    /// <see cref="HusbandryFriendliness.Add"/> (cooldown + favorite refresh).
    /// Blank planter becomes the anonymous sentinel inside <c>Add</c>.
    /// </summary>
    public static bool TryGainFriendliness(Entity? animal, string? planterUid, Random? rand = null)
    {
        if (animal?.WatchedAttributes == null || animal.World?.Side != EnumAppSide.Server)
        {
            return false;
        }

        Random rng = rand ?? animal.World.Rand ?? Random.Shared;
        if (rng.NextDouble() >= TroughEatStation.BaseFriendlinessChance)
        {
            return false;
        }

        int before = HusbandryFriendliness.Get(animal);
        HusbandryFriendliness.Add(animal, planterUid, 1, rng);
        return HusbandryFriendliness.Get(animal) > before;
    }
}
