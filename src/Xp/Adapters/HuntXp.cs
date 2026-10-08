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
/// A kill emits <c>hunted</c>. A trap emits <c>trapped</c>.
/// Generation at or above <see cref="AnimalWeightCatalog.CleaverCertainGeneration"/> also emits <c>cleaver-certain</c>.
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

        bool cleaverCertain = AnimalWeightCatalog.CanCleaverSlaughter(victim);
        api.Logger.VerboseDebug(
            "[prosequor] deed hunted{0} {1} weight={2:0.###} by {3}",
            cleaverCertain ? "+cleaver-certain" : "",
            victim.Code,
            victim.Properties?.Weight ?? 0f,
            player.PlayerName);

        Deed.Emit(
            api,
            player.PlayerUID,
            CleaverTokens(DeedTokenTags.Hunted, cleaverCertain),
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

        bool cleaverCertain = AnimalWeightCatalog.CanCleaverSlaughter(animal);
        api.Logger.VerboseDebug(
            "[prosequor] deed trapped{0} {1} weight={2:0.###} by {3}",
            cleaverCertain ? "+cleaver-certain" : "",
            animal!.Code,
            animal.Properties?.Weight ?? 0f,
            uid);

        Deed.Emit(
            api,
            uid.Trim(),
            CleaverTokens(DeedTokenTags.Trapped, cleaverCertain),
            caller: caller,
            target: EventFactBuilder.CodeOf(animal),
            position: trap.Pos?.Copy());
    }

    static List<string> CleaverTokens(string moment, bool cleaverCertain)
    {
        List<string> tokens = [moment];
        if (cleaverCertain)
        {
            tokens.Add(DeedTokenTags.CleaverCertain);
        }

        return tokens;
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
