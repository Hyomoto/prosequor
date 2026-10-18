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
/// Projectile launch folds: Nice Shot break chance, Fletcher flight speed, and Focus Shot damage.
/// Focus Shot applies when the weapon is in <c>ranged-weapons</c> and the charge was recorded.
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

        if (ItemBowFocusShotPatch.TryRead(out float drawnSeconds)
            && IsRangedWeapon(projectile, byEntity))
        {
            projectile.Damage = FocusShot.ScaleDamage(
                projectile.Damage,
                drawnSeconds,
                PlayerInteractionStation.ResolveFocusShotHoldSeconds(ep.Player),
                PlayerInteractionStation.ResolveFocusShotDamageFactor(ep.Player));
        }
    }

    static bool IsRangedWeapon(EntityProjectileBase projectile, EntityAgent byEntity)
    {
        ItemStack? weapon = projectile.WeaponStack;
        if (weapon == null && byEntity is EntityPlayer holder)
        {
            weapon = holder.RightHandItemSlot?.Itemstack ?? holder.LeftHandItemSlot?.Itemstack;
        }

        return RangedAccAttributeMutator.InRangedWeapons(weapon, projectile.World ?? byEntity.World);
    }
}

/// <summary>
/// Focus Shot: a bow draw that lasts at least the owned hold multiplies that arrow's damage.
/// </summary>
public static class FocusShot
{
    public static float ScaleDamage(float damage, float secondsUsed, float holdSeconds, float factor)
    {
        if (holdSeconds <= 0f || secondsUsed < holdSeconds)
        {
            return damage;
        }

        return damage * factor;
    }
}

/// <summary>
/// Stashes a ranged-weapon charge length for the launch fold on the same call, then clears it.
/// The patch attribute sits on the class so assembly PatchAll picks it up.
/// </summary>
[HarmonyPatch(typeof(ItemBow), nameof(ItemBow.OnHeldInteractStop))]
public static class ItemBowFocusShotPatch
{
    [ThreadStatic]
    static float? drawSeconds;

    internal static bool TryRead(out float secondsUsed)
    {
        if (drawSeconds is float value)
        {
            secondsUsed = value;
            return true;
        }

        secondsUsed = 0f;
        return false;
    }

    internal static void Note(float secondsUsed, EntityAgent? byEntity)
    {
        drawSeconds = null;
        if (byEntity?.World?.Side == EnumAppSide.Server)
        {
            drawSeconds = secondsUsed;
        }
    }

    internal static void Clear() => drawSeconds = null;

    [HarmonyPrefix]
    public static void Prefix(float secondsUsed, EntityAgent byEntity) =>
        Note(secondsUsed, byEntity);

    [HarmonyPostfix]
    public static void Postfix() => Clear();
}

/// <summary>Same charge stash as <see cref="ItemBowFocusShotPatch"/>, for the sling.</summary>
[HarmonyPatch(typeof(ItemSling), nameof(ItemSling.OnHeldInteractStop))]
public static class ItemSlingFocusShotPatch
{
    [HarmonyPrefix]
    public static void Prefix(float secondsUsed, EntityAgent byEntity) =>
        ItemBowFocusShotPatch.Note(secondsUsed, byEntity);

    [HarmonyPostfix]
    public static void Postfix() => ItemBowFocusShotPatch.Clear();
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
