using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.GameContent;

namespace Prosequor.Ability;

/// <summary>Harmony adapters for <c>prosequor:player-interaction</c> (stat verbs and take-damage).</summary>
[HarmonyPatch]
public static class PlayerInteractionAbilityPatches
{
    [ThreadStatic]
    static bool healthSnapshotValid;

    [ThreadStatic]
    static float healthSnapshotHealth;

    [ThreadStatic]
    static float healthSnapshotMax;

    /// <summary>
    /// Snapshot current/max before vanilla recomputes max without Constitution.
    /// Fill must use the player's actual pool, not the stripped vanilla max.
    /// </summary>
    [HarmonyPrefix]
    [HarmonyPatch(typeof(EntityBehaviorHealth), nameof(EntityBehaviorHealth.UpdateMaxHealth))]
    public static void UpdateMaxHealthPrefix(EntityBehaviorHealth __instance)
    {
        healthSnapshotValid = false;
        Entity entity = __instance.entity;
        if (entity == null || entity.World.Side != EnumAppSide.Server)
        {
            return;
        }

        if (entity is not EntityPlayer entityPlayer || entityPlayer.Player == null)
        {
            return;
        }

        healthSnapshotHealth = __instance.Health;
        healthSnapshotMax = __instance.MaxHealth;
        healthSnapshotValid = true;
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(EntityBehaviorHealth), nameof(EntityBehaviorHealth.UpdateMaxHealth))]
    public static void UpdateMaxHealthPostfix(EntityBehaviorHealth __instance)
    {
        try
        {
            Entity entity = __instance.entity;
            if (entity == null || entity.World.Side != EnumAppSide.Server)
            {
                return;
            }

            if (entity is not EntityPlayer entityPlayer || entityPlayer.Player == null)
            {
                return;
            }

            int delta = PlayerInteractionStation.ResolveHealthDelta(entityPlayer.Player);
            if (delta == 0)
            {
                return;
            }

            // Vanilla just wrote the un-boosted max; restore fill against the pre-vanilla pool.
            float snapshotHealth = healthSnapshotValid ? healthSnapshotHealth : __instance.Health;
            float snapshotMax = healthSnapshotValid ? healthSnapshotMax : __instance.MaxHealth;
            float newMax = __instance.MaxHealth + delta;
            float ratio = snapshotMax > 0.001f
                ? Math.Clamp(snapshotHealth / snapshotMax, 0f, 1f)
                : 1f;

            __instance.MaxHealth = newMax;
            __instance.Health = newMax * ratio;
        }
        finally
        {
            healthSnapshotValid = false;
        }
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(EntityBehaviorHunger), nameof(EntityBehaviorHunger.OnEntityReceiveSaturation))]
    public static void OnEntityReceiveSaturationPrefix(
        EntityBehaviorHunger __instance,
        ref float saturation,
        ref float saturationLossDelay,
        ref float nutritionGainMultiplier)
    {
        Entity entity = __instance.entity;
        if (entity == null || entity.World.Side != EnumAppSide.Server)
        {
            return;
        }

        MealEatMods.Apply(
            MealEatScope.Current,
            ref saturation,
            ref saturationLossDelay,
            ref nutritionGainMultiplier);

        if (entity is not EntityPlayer entityPlayer || entityPlayer.Player == null)
        {
            return;
        }

        int pct = PlayerInteractionStation.ResolveHungerDelayPercent(entityPlayer.Player);
        if (pct > 0)
        {
            saturationLossDelay *= 1f + pct / 100f;
        }
    }

    /// <summary>
    /// After vanilla <c>onDamaged</c> delegates, fold on-damage amount / last-stand (frost, Last Stand, …).
    /// </summary>
    [HarmonyPostfix]
    [HarmonyPatch(typeof(EntityBehaviorHealth), "ApplyOnDamageDelegates")]
    public static void TakeDamagePostfix(
        EntityBehaviorHealth __instance,
        DamageSource damageSource,
        ref float damage) =>
        TakeDamageStation.Run(__instance, damageSource, ref damage);

    /// <summary>
    /// Before damage behaviors run: Inconspicuity crit ×2 on player weapon hits
    /// (after melee/ranged Entity.Stats multipliers already applied at the attack site),
    /// then Heartseeker unaware damage.
    /// </summary>
    [HarmonyPrefix]
    [HarmonyPatch(typeof(Entity), nameof(Entity.ReceiveDamage))]
    public static void ReceiveDamageCritPrefix(
        Entity __instance,
        DamageSource damageSource,
        ref float damage)
    {
        PlayerInteractionStation.TryApplyCrit(damageSource, __instance, ref damage);
        HeartseekerStation.TryApply(damageSource, __instance, ref damage);
    }
}
