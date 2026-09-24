using HarmonyLib;
using Prosequor.Ability;
using Prosequor.Ability.Hooks;
using Prosequor.Xp.Activity;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace Prosequor.Xp.Adapters;

/// <summary>
/// Hunting deeds: arrow / thrown-spear kills and basket-trap catches, paid by animal weight.
/// </summary>
public static class HuntXp
{
    public static void NotifyKilled(Entity victim, DamageSource? damageSource)
    {
        if (victim?.World?.Api is not { Side: EnumAppSide.Server } api
            || damageSource == null
            || !AnimalWeightCatalog.IsAnimal(victim))
        {
            return;
        }

        if (!TryResolveProjectileKill(damageSource, out IPlayer? player, out string? caller)
            || player?.PlayerUID == null)
        {
            return;
        }

        api.Logger.VerboseDebug(
            "[prosequor] deed hunted {0} weight={1:0.###} by {2}",
            victim.Code,
            victim.Properties?.Weight ?? 0f,
            player.PlayerName);

        Deed.Emit(
            api,
            player.PlayerUID,
            DeedToken.Hunted,
            caller: caller,
            target: EventFactBuilder.CodeOf(victim),
            position: victim.Pos?.AsBlockPos?.Copy());
    }

    public static void NotifyTrapped(BlockEntityAnimalTrap trap, Entity animal)
    {
        ICoreAPI? api = trap?.Api ?? animal?.World?.Api;
        if (api == null
            || api.Side != EnumAppSide.Server
            || trap == null
            || !AnimalWeightCatalog.IsAnimal(animal))
        {
            return;
        }

        string? uid = AnimalTrapStarterStation.TryResolvePayeeUid(trap);
        if (string.IsNullOrWhiteSpace(uid))
        {
            return;
        }

        Block? block = trap.Block ?? api.World?.BlockAccessor.GetBlock(trap.Pos);
        string? caller = EventFactBuilder.CodeOf(block);
        if (string.IsNullOrWhiteSpace(caller))
        {
            caller = CallerIdentities.Hand;
        }

        CollectionIndex? tags = ProsequorModSystem.For(api)?.Collections?.Index;
        if (tags != null && !tags.Contains("trap", caller))
        {
            return;
        }

        api.Logger.VerboseDebug(
            "[prosequor] deed trapped {0} weight={1:0.###} by {2}",
            animal!.Code,
            animal.Properties?.Weight ?? 0f,
            uid);

        Deed.Emit(
            api,
            uid.Trim(),
            DeedToken.Trapped,
            caller: caller,
            target: EventFactBuilder.CodeOf(animal),
            position: trap.Pos?.Copy());
    }

    static bool TryResolveProjectileKill(
        DamageSource damageSource,
        out IPlayer? player,
        out string? caller)
    {
        player = null;
        caller = null;

        Entity? source = damageSource.SourceEntity;
        if (source is not EntityProjectileBase projectile)
        {
            return false;
        }

        ItemStack? weapon = projectile.WeaponStack;
        ItemStack? ammo = projectile.ProjectileStack;
        bool arrow = IsArrow(ammo) || IsArrow(weapon);
        bool spear = IsThrownSpear(ammo) || IsThrownSpear(weapon);
        if (!arrow && !spear)
        {
            return false;
        }

        if (projectile.FiredBy is not EntityPlayer ep || ep.Player == null)
        {
            return false;
        }

        player = ep.Player;
        caller = EventFactBuilder.CodeOf(arrow ? ammo ?? weapon : weapon ?? ammo)
            ?? CallerIdentities.Hand;
        return true;
    }

    static bool IsArrow(ItemStack? stack) =>
        stack?.Collectible is ItemArrow
        || (stack?.Collectible?.Code?.Path?.StartsWith("arrow-", StringComparison.OrdinalIgnoreCase) ?? false);

    static bool IsThrownSpear(ItemStack? stack) =>
        stack?.Collectible?.Tool == EnumTool.Spear
        || (stack?.Collectible?.Code?.Path?.StartsWith("spear-", StringComparison.OrdinalIgnoreCase) ?? false);
}

/// <summary>Harmony adapters for hunting XP deeds.</summary>
public static class HuntXpPatches
{
    [HarmonyPatch(typeof(Entity), nameof(Entity.Die))]
    public static class EntityDieHuntXpPatch
    {
        [HarmonyPostfix]
        public static void Postfix(Entity __instance, EnumDespawnReason reason, DamageSource damageSourceForDeath)
        {
            _ = reason;
            HuntXp.NotifyKilled(__instance, damageSourceForDeath);
        }
    }

    [HarmonyPatch(typeof(BlockEntityAnimalTrap), "TrapAnimal")]
    public static class AnimalTrapTrapAnimalPatch
    {
        [HarmonyPostfix]
        public static void Postfix(BlockEntityAnimalTrap __instance, Entity entity) =>
            HuntXp.NotifyTrapped(__instance, entity);
    }
}
