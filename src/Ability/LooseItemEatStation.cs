using System;
using Vintagestory.API.Common.Entities;

namespace Prosequor.Ability;

/// <summary>
/// Friendliness gain when an animal eats a player-dropped ground stack.
/// Blank dropper is a miss — unlike crops, unstamped loot is not anonymous care.
/// </summary>
public static class LooseItemEatStation
{
    /// <summary>
    /// Blank <paramref name="dropperUid"/> returns false without rolling.
    /// Otherwise the same 5% roll, cooldown, and favorite refresh as crop pilfer.
    /// </summary>
    public static bool TryGainFriendliness(Entity? animal, string? dropperUid, Random? rand = null)
    {
        if (string.IsNullOrWhiteSpace(dropperUid))
        {
            return false;
        }

        return CropEatStation.TryGainFriendliness(animal, dropperUid.Trim(), rand);
    }
}
