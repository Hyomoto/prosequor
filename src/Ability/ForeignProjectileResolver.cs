using System.Collections.Concurrent;
using System.Reflection;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.GameContent;

namespace Prosequor.Ability;

/// <summary>
/// Resolves a player kill when the damage source is a foreign projectile entity
/// (Combat Overhaul <c>ProjectileEntity</c>): <c>SourceEntity</c> is not a vanilla
/// projectile or player, <c>CauseEntity</c> is the shooter, and the source exposes
/// <c>ProjectileStack</c> / <c>WeaponStack</c>.
/// </summary>
public static class ForeignProjectileResolver
{
    static readonly ConcurrentDictionary<Type, PropertyInfo?> projectileStackProps = new();
    static readonly ConcurrentDictionary<Type, PropertyInfo?> weaponStackProps = new();

    /// <summary>
    /// When <paramref name="damageSource"/> is a foreign projectile hit, sets the shooter
    /// and a caller code from projectile then weapon stack.
    /// </summary>
    public static bool TryResolve(
        DamageSource? damageSource,
        out IPlayer? player,
        out string? caller)
    {
        player = null;
        caller = null;
        if (damageSource == null)
        {
            return false;
        }

        Entity? source = damageSource.SourceEntity;
        if (source == null
            || source is EntityProjectileBase
            || source is EntityPlayer)
        {
            return false;
        }

        if (damageSource.CauseEntity is not EntityPlayer cause
            || cause.Player == null)
        {
            return false;
        }

        PropertyInfo? projectileProp = projectileStackProps.GetOrAdd(
            source.GetType(),
            static type => type.GetProperty(
                "ProjectileStack",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic));
        if (projectileProp == null)
        {
            return false;
        }

        player = cause.Player;
        ItemStack? projectile = projectileProp.GetValue(source) as ItemStack;
        ItemStack? weapon = null;
        PropertyInfo? weaponProp = weaponStackProps.GetOrAdd(
            source.GetType(),
            static type => type.GetProperty(
                "WeaponStack",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic));
        if (weaponProp != null)
        {
            weapon = weaponProp.GetValue(source) as ItemStack;
        }

        caller = EventFactBuilder.CodeOf(projectile ?? weapon) ?? CallerIdentities.Hand;
        return true;
    }

    /// <summary>Shooter only; used by Heartseeker.</summary>
    public static IPlayer? TryResolvePlayer(DamageSource? damageSource) =>
        TryResolve(damageSource, out IPlayer? player, out _) ? player : null;
}
