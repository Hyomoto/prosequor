using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace Prosequor.Ability;

/// <summary>
/// Applies Restful Rider / Jousting / Break Fall player stats while mounted (or recently dismounted).
/// </summary>
public static class RidingPlayerStats
{
    static readonly HashSet<string> AppliedModifierPlayers = new(StringComparer.Ordinal);

    public static void Tick(IReadOnlyList<IServerPlayer> slice)
    {
        for (int i = 0; i < slice.Count; i++)
        {
            Apply(slice[i]);
        }
    }

    public static void Apply(IServerPlayer player)
    {
        EntityPlayer? entity = player.Entity as EntityPlayer;
        if (entity?.Stats == null)
        {
            return;
        }

        bool mounted = entity.MountedOn != null;
        bool fallWindow = mounted || MountedStation.RecentlyDismounted(entity);
        if (!mounted && !fallWindow)
        {
            if (AppliedModifierPlayers.Remove(player.PlayerUID))
            {
                ClearRidingStats(entity.Stats);
            }

            return;
        }

        EntityStats stats = entity.Stats;
        Entity? mount = ResolveMountEntity(entity);

        float hungerReduction = 0f;
        float meleeBonus = 0f;
        float fallReduction = 0f;
        if (mounted && mount != null)
        {
            hungerReduction = MountedStation.ResolveHungerReduction(player, mount);
            meleeBonus = MountedStation.ResolveMeleeBonus(player, mount);
        }

        if (fallWindow)
        {
            fallReduction = MountedStation.ResolveFallDamageReduction(player, mount);
        }

        if (hungerReduction > 0f)
        {
            stats.Set("hungerrate", MountedStation.StatHungerKey, -hungerReduction, false);
        }
        else
        {
            stats.Remove("hungerrate", MountedStation.StatHungerKey);
        }

        if (meleeBonus > 0f)
        {
            stats.Set("meleeWeaponsDamage", MountedStation.StatMeleeKey, meleeBonus, false);
        }
        else
        {
            stats.Remove("meleeWeaponsDamage", MountedStation.StatMeleeKey);
        }

        if (fallReduction > 0f)
        {
            stats.Set("fallDamageFactor", MountedStation.StatFallKey, -fallReduction, false);
        }
        else
        {
            stats.Remove("fallDamageFactor", MountedStation.StatFallKey);
        }

        bool anyApplied = hungerReduction > 0f || meleeBonus > 0f || fallReduction > 0f;
        if (anyApplied)
        {
            AppliedModifierPlayers.Add(player.PlayerUID);
        }
        else if (AppliedModifierPlayers.Remove(player.PlayerUID))
        {
            ClearRidingStats(stats);
        }
    }

    static void ClearRidingStats(EntityStats stats)
    {
        stats.Remove("hungerrate", MountedStation.StatHungerKey);
        stats.Remove("meleeWeaponsDamage", MountedStation.StatMeleeKey);
        stats.Remove("fallDamageFactor", MountedStation.StatFallKey);
    }

    static Entity? ResolveMountEntity(EntityAgent entity)
    {
        IMountableSeat? seat = entity.MountedOn;
        if (seat == null)
        {
            return null;
        }

        if (seat.MountSupplier is EntityBehavior behavior)
        {
            return behavior.entity;
        }

        if (seat.MountSupplier is Entity supplier)
        {
            return supplier;
        }

        return seat.Entity;
    }
}
