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
/// Food-animal kills and basket-trap catches, paid by animal weight.
/// Generation at or above <see cref="AnimalWeightCatalog.CleaverCertainGeneration"/> pays husbandry
/// (<c>slaughtered</c>); younger animals pay hunting (<c>hunted</c> / <c>trapped</c>).
/// </summary>
public static class HuntXp
{
    public static void NotifyKilled(Entity victim, DamageSource? damageSource)
    {
        if (victim?.World?.Api is not { Side: EnumAppSide.Server } api
            || damageSource == null
            || ProsequorModSystem.For(api)?.AnimalWeight is not AnimalWeightCatalog weights
            || !weights.IsListed(victim))
        {
            return;
        }

        if (!TryResolvePlayerKill(damageSource, out IPlayer? player, out string? caller)
            || player?.PlayerUID == null)
        {
            return;
        }

        bool husbandry = AnimalWeightCatalog.CanCleaverSlaughter(victim);
        api.Logger.VerboseDebug(
            "[prosequor] deed {0} {1} weight={2:0.###} by {3}",
            husbandry ? DeedTokenTags.Slaughtered : DeedTokenTags.Hunted,
            victim.Code,
            victim.Properties?.Weight ?? 0f,
            player.PlayerName);

        Deed.Emit(
            api,
            player.PlayerUID,
            husbandry ? DeedToken.Slaughtered : DeedToken.Hunted,
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
            || ProsequorModSystem.For(api)?.AnimalWeight is not AnimalWeightCatalog weights
            || !weights.IsListed(animal))
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

        bool husbandry = AnimalWeightCatalog.CanCleaverSlaughter(animal);
        api.Logger.VerboseDebug(
            "[prosequor] deed {0} {1} weight={2:0.###} by {3}",
            husbandry ? DeedTokenTags.Slaughtered : DeedTokenTags.Trapped,
            animal!.Code,
            animal.Properties?.Weight ?? 0f,
            uid);

        Deed.Emit(
            api,
            uid.Trim(),
            husbandry ? DeedToken.Slaughtered : DeedToken.Trapped,
            caller: caller,
            target: EventFactBuilder.CodeOf(animal),
            position: trap.Pos?.Copy());
    }

    static bool TryResolvePlayerKill(
        DamageSource damageSource,
        out IPlayer? player,
        out string? caller)
    {
        player = null;
        caller = null;

        Entity? source = damageSource.SourceEntity ?? damageSource.CauseEntity;
        if (source is EntityProjectileBase projectile)
        {
            if (projectile.FiredBy is not EntityPlayer fired || fired.Player == null)
            {
                return false;
            }

            player = fired.Player;
            caller = EventFactBuilder.CodeOf(projectile.ProjectileStack ?? projectile.WeaponStack)
                ?? CallerIdentities.Hand;
            return true;
        }

        if (source is EntityPlayer ep && ep.Player != null)
        {
            player = ep.Player;
            caller = EventFactBuilder.HeldCode(player) ?? CallerIdentities.Hand;
            return true;
        }

        return false;
    }
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
