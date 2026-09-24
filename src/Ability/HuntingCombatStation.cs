using System.Runtime.CompilerServices;
using HarmonyLib;
using Prosequor.Ability.Hooks;
using Prosequor.Player;
using Prosequor.Xp;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace Prosequor.Ability;

/// <summary>
/// Bow / thrown-spear projectile folds: Nice Shot break chance and Fletcher flight speed.
/// </summary>
public static class HuntingProjectileStation
{
    public static void ApplyLaunchFolds(EntityProjectileBase projectile, EntityAgent? byEntity, ref double speed)
    {
        if (projectile == null || byEntity?.World?.Side != EnumAppSide.Server)
        {
            return;
        }

        if (byEntity is not EntityPlayer ep || ep.Player == null)
        {
            return;
        }

        ItemStack? arrow = projectile.ProjectileStack;
        if (arrow != null)
        {
            float flight = CraftAttributeMods.GetFactor(arrow, FlightAttributeMutator.KeyName);
            if (flight > 1.0001f)
            {
                speed *= flight;
            }

            float breakChance = 1f - Math.Clamp(projectile.DropOnImpactChance, 0f, 1f);
            float nextBreak = PlayerInteractionStation.ResolveArrowBreakChance(ep.Player, breakChance);
            projectile.DropOnImpactChance = 1f - Math.Clamp(nextBreak, 0f, 1f);
        }
    }
}

/// <summary>Harmony adapters for projectile launch folds.</summary>
public static class HuntingProjectilePatches
{
    [HarmonyPatch(typeof(EntityProjectileBase), nameof(EntityProjectileBase.SpawnProjectile))]
    public static class EntityProjectileSpawnProjectilePatch
    {
        [HarmonyPrefix]
        public static void Prefix(
            EntityProjectileBase __instance,
            EntityAgent byEntity,
            ref double speed) =>
            HuntingProjectileStation.ApplyLaunchFolds(__instance, byEntity, ref speed);
    }
}

/// <summary>
/// Heartseeker: extra damage vs animals whose current alert-meter threat is below a threshold.
/// </summary>
public static class HeartseekerStation
{
    public static void TryApply(DamageSource damageSource, Entity victim, ref float damage)
    {
        if (victim?.World == null || victim.World.Side != EnumAppSide.Server || damage <= 0f)
        {
            return;
        }

        if (damageSource == null)
        {
            return;
        }

        EnumDamageType type = damageSource.Type;
        if (type != EnumDamageType.BluntAttack
            && type != EnumDamageType.SlashingAttack
            && type != EnumDamageType.PiercingAttack)
        {
            return;
        }

        IPlayer? player = ResolveAttackingPlayer(damageSource);
        if (player == null)
        {
            return;
        }

        if (!AnimalWeightCatalog.IsAnimal(victim))
        {
            return;
        }

        float threshold = PlayerInteractionStation.ResolveUnawareDamageThreshold(player);
        if (threshold <= 0f)
        {
            return;
        }

        if (!AnimalAlertService.TryReadOverlay(victim, out _, out int threatQ, out _, out _)
            || threatQ >= threshold)
        {
            return;
        }

        float factor = PlayerInteractionStation.ResolveUnawareDamageFactor(player);
        if (factor > 1.0001f)
        {
            damage *= factor;
        }
    }

    static IPlayer? ResolveAttackingPlayer(DamageSource damageSource)
    {
        Entity? source = damageSource.SourceEntity ?? damageSource.CauseEntity;
        if (source is EntityPlayer ep)
        {
            return ep.Player;
        }

        if (source is EntityProjectileBase projectile
            && projectile.FiredBy is EntityPlayer fired)
        {
            return fired.Player;
        }

        return null;
    }
}

/// <summary>Notes the last player who baited / set a trap for trapped XP payee.</summary>
public static class AnimalTrapStarterStation
{
    static readonly ConditionalWeakTable<BlockEntityAnimalTrap, Box> boxes = new();

    sealed class Box
    {
        public string? LastInteractorUid;
    }

    public static void NoteInteractor(BlockEntityAnimalTrap? trap, IPlayer? player)
    {
        if (trap?.Api?.Side != EnumAppSide.Server || string.IsNullOrEmpty(player?.PlayerUID))
        {
            return;
        }

        Box box = boxes.GetOrCreateValue(trap);
        box.LastInteractorUid = player!.PlayerUID;
    }

    public static string? TryResolvePayeeUid(BlockEntityAnimalTrap? trap)
    {
        if (trap == null)
        {
            return null;
        }

        if (ProsequorBlockPedigreeStation.TryGetSoleContributor(trap, out string? uid)
            && !string.IsNullOrWhiteSpace(uid))
        {
            return uid.Trim();
        }

        if (boxes.TryGetValue(trap, out Box? box) && !string.IsNullOrWhiteSpace(box.LastInteractorUid))
        {
            return box.LastInteractorUid!.Trim();
        }

        return null;
    }
}

/// <summary>Harmony adapters for trap interactor tracking.</summary>
public static class AnimalTrapAbilityPatches
{
    [HarmonyPatch(typeof(BlockEntityAnimalTrap), "Interact")]
    public static class AnimalTrapInteractPatch
    {
        [HarmonyPostfix]
        public static void Postfix(BlockEntityAnimalTrap __instance, IPlayer player) =>
            AnimalTrapStarterStation.NoteInteractor(__instance, player);
    }
}
