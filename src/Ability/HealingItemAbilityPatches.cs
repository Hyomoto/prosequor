using System.Reflection;
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.GameContent;

namespace Prosequor.Ability;

/// <summary>
/// Healing-item apply: quality regen + First Aid tend folds, Triage after revive,
/// and tooltip heal text that includes the stack regen factor.
/// </summary>
[HarmonyPatch]
public static class HealingItemAbilityPatches
{
    static readonly MethodInfo GetApplicationTimeMethod = AccessTools.Method(
        typeof(CollectibleBehaviorHealingItem),
        "GetApplicationTime",
        [typeof(EntityAgent)]);

    static readonly MethodInfo GetTargetEntityMethod = AccessTools.Method(
        typeof(CollectibleBehaviorHealingItem),
        "GetTargetEntity",
        [typeof(ItemSlot), typeof(EntityAgent), typeof(EntitySelection)]);

    [ThreadStatic]
    static float healthRestore;

    [ThreadStatic]
    static bool healthStashed;

    [ThreadStatic]
    static Entity? revivePatient;

    [ThreadStatic]
    static IPlayer? reviveCaregiver;

    [HarmonyPrefix]
    [HarmonyPatch(typeof(CollectibleBehaviorHealingItem), nameof(CollectibleBehaviorHealingItem.OnHeldInteractStop))]
    public static void OnHeldInteractStopPrefix(
        CollectibleBehaviorHealingItem __instance,
        float secondsUsed,
        ItemSlot slot,
        EntityAgent byEntity,
        EntitySelection? entitySel)
    {
        healthStashed = false;
        revivePatient = null;
        reviveCaregiver = null;

        if (byEntity?.World?.Side != EnumAppSide.Server
            || byEntity is not EntityPlayer entityPlayer
            || entityPlayer.Player == null
            || slot?.Itemstack == null)
        {
            return;
        }

        float applicationTime = InvokeGetApplicationTime(__instance, byEntity);
        if (secondsUsed < applicationTime)
        {
            return;
        }

        IPlayer caregiver = entityPlayer.Player;
        Entity? target = InvokeGetTargetEntity(__instance, slot, byEntity, entitySel);
        if (target == null)
        {
            return;
        }

        if (!target.Alive
            && target.GetBehavior<EntityBehaviorPlayerRevivable>() != null
            && __instance.CanRevive)
        {
            revivePatient = target;
            reviveCaregiver = caregiver;
        }

        float typeHealth = __instance.Health;
        float folded = MedicineStation.ResolveHealTotal(
            caregiver,
            slot.Itemstack,
            typeHealth,
            target);
        if (Math.Abs(folded - typeHealth) < 0.0001f)
        {
            return;
        }

        healthRestore = typeHealth;
        healthStashed = true;
        __instance.Health = folded;
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(CollectibleBehaviorHealingItem), nameof(CollectibleBehaviorHealingItem.OnHeldInteractStop))]
    public static void OnHeldInteractStopPostfix(CollectibleBehaviorHealingItem __instance)
    {
        try
        {
            if (reviveCaregiver != null
                && revivePatient != null
                && revivePatient.Alive)
            {
                MedicineStation.ApplyTriageAfterRevive(reviveCaregiver, revivePatient);
            }
        }
        finally
        {
            if (healthStashed)
            {
                __instance.Health = healthRestore;
                healthStashed = false;
            }

            revivePatient = null;
            reviveCaregiver = null;
        }
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(CollectibleBehaviorHealingItem), "GetApplicationTime")]
    public static void GetApplicationTimePostfix(
        EntityAgent byEntity,
        ref float __result)
    {
        if (byEntity is not EntityPlayer entityPlayer || entityPlayer.Player == null)
        {
            return;
        }

        Entity? patient = entityPlayer.Player.CurrentEntitySelection?.Entity;
        float next = MedicineStation.ResolveApplicationSeconds(
            entityPlayer.Player,
            __result,
            patient);
        if (Math.Abs(next - __result) > 0.0001f)
        {
            __result = next;
        }
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(CollectibleBehaviorHealingItem), nameof(CollectibleBehaviorHealingItem.GetHeldItemInfo))]
    public static void GetHeldItemInfoPrefix(
        CollectibleBehaviorHealingItem __instance,
        ItemSlot inSlot,
        ref float __state)
    {
        __state = float.NaN;
        ItemStack? stack = inSlot?.Itemstack;
        if (stack == null)
        {
            return;
        }

        float regen = CraftAttributeMods.GetFactor(stack, RegenAttributeMutator.KeyName);
        if (Math.Abs(regen - 1f) < 0.0001f)
        {
            return;
        }

        __state = __instance.Health;
        __instance.Health = __state * regen;
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(CollectibleBehaviorHealingItem), nameof(CollectibleBehaviorHealingItem.GetHeldItemInfo))]
    public static void GetHeldItemInfoPostfix(
        CollectibleBehaviorHealingItem __instance,
        float __state)
    {
        if (!float.IsNaN(__state))
        {
            __instance.Health = __state;
        }
    }

    static float InvokeGetApplicationTime(CollectibleBehaviorHealingItem self, EntityAgent byEntity)
    {
        if (GetApplicationTimeMethod == null)
        {
            return self.ApplicationTimeSec;
        }

        object? result = GetApplicationTimeMethod.Invoke(self, [byEntity]);
        return result is float f ? f : self.ApplicationTimeSec;
    }

    static Entity? InvokeGetTargetEntity(
        CollectibleBehaviorHealingItem self,
        ItemSlot slot,
        EntityAgent byEntity,
        EntitySelection? entitySel)
    {
        if (GetTargetEntityMethod == null)
        {
            return byEntity;
        }

        return GetTargetEntityMethod.Invoke(self, [slot, byEntity, entitySel]) as Entity;
    }
}
