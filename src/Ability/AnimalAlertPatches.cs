using System.Reflection;
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.GameContent;

namespace Prosequor.Ability;

/// <summary>
/// Wires the alert meter into TaskAI init, player sensing, flee/seek/melee/idle, and damage.
/// Meter owns player-flee eligibility via CanSensePlayer; TaskAI runs flee naturally.
/// </summary>
[HarmonyPatch]
public static class AnimalAlertTaskAiPatch
{
    [HarmonyPostfix]
    [HarmonyPatch(typeof(EntityBehaviorTaskAI), nameof(EntityBehaviorTaskAI.Initialize))]
    public static void InitializePostfix(EntityBehaviorTaskAI __instance) =>
        AnimalAlertService.ConsiderTracking(__instance);

    [HarmonyPostfix]
    [HarmonyPatch(typeof(EntityBehaviorTaskAI), nameof(EntityBehaviorTaskAI.OnEntityDespawn))]
    public static void OnEntityDespawnPostfix(EntityBehaviorTaskAI __instance) =>
        AnimalAlertService.OnEntityDespawn(__instance?.entity);
}

/// <summary>
/// Fused eligibility: meter animals do not sense players until flee-committed.
/// When committed, the alert target is sensed regardless of vanilla seekingRange/sneak.
/// </summary>
[HarmonyPatch(typeof(AiTaskBaseTargetable), nameof(AiTaskBaseTargetable.CanSensePlayer))]
public static class AnimalAlertCanSensePlayerPatch
{
    [HarmonyPrefix]
    public static bool Prefix(
        AiTaskBaseTargetable __instance,
        EntityPlayer eplr,
        double range,
        ref bool __result)
    {
        if (__instance?.entity == null
            || !AnimalAlertService.TryGet(__instance.entity, out AnimalAlertState state)
            || !state.SensesPlayers)
        {
            return true;
        }

        if (state.Committed
            && state.AlertTargetEntityId != 0
            && eplr != null
            && eplr.EntityId == state.AlertTargetEntityId)
        {
            __result = true;
            return false;
        }

        __result = false;
        return false;
    }
}

[HarmonyPatch(typeof(AiTaskFleeEntity), nameof(AiTaskFleeEntity.OnEntityHurt))]
public static class AnimalAlertFleeHurtPatch
{
    static readonly FieldInfo InstaFleeNowField =
        AccessTools.Field(typeof(AiTaskFleeEntity), "instafleenow");

    [HarmonyPostfix]
    public static void Postfix(AiTaskFleeEntity __instance, DamageSource source)
    {
        // Player damage commit is owned by the meter. Non-player hits keep vanilla insta-flee.
        if (source?.GetCauseEntity() is not EntityPlayer)
        {
            return;
        }

        InstaFleeNowField?.SetValue(__instance, false);
    }
}

[HarmonyPatch(typeof(AiTaskSeekEntity), nameof(AiTaskSeekEntity.ShouldExecute))]
public static class AnimalAlertSeekShouldExecutePatch
{
    static readonly FieldInfo WhenInEmotionField =
        AccessTools.Field(typeof(AiTaskBase), "WhenInEmotionStates");
    static readonly FieldInfo TargetPosField =
        AccessTools.Field(typeof(AiTaskSeekEntity), "targetPos");
    static readonly MethodInfo IsInEmotionStatesMethod =
        AccessTools.Method(typeof(AiTaskBase), "IsInEmotionState", [typeof(string[])]);

    [HarmonyPrefix]
    [HarmonyPriority(Priority.High)]
    public static bool Prefix(AiTaskSeekEntity __instance, ref bool __result)
    {
        if (__instance?.entity == null
            || !AnimalAlertService.IsCommitted(__instance.entity)
            || !AnimalAlertService.TryGetAlertTarget(__instance.entity, out Entity? target)
            || target is not EntityPlayer
            || !AnimalAlertService.TaskCanTargetPlayer(__instance))
        {
            return true;
        }

        // Emotion-gated seek (e.g. rooster aggressiveondamage) only when in that emotion.
        if (WhenInEmotionField.GetValue(__instance) is string[] emotions
            && emotions.Length > 0
            && IsInEmotionStatesMethod?.Invoke(__instance, [emotions]) is not true)
        {
            return true;
        }

        __instance.targetEntity = target;
        if (TargetPosField != null)
        {
            TargetPosField.SetValue(__instance, target.Pos.XYZ);
        }

        __result = true;
        return false;
    }
}

[HarmonyPatch(typeof(AiTaskMeleeAttack), nameof(AiTaskMeleeAttack.ShouldExecute))]
public static class AnimalAlertMeleeShouldExecutePatch
{
    static readonly FieldInfo WhenInEmotionField =
        AccessTools.Field(typeof(AiTaskBase), "WhenInEmotionStates");
    static readonly FieldInfo MinDistField =
        AccessTools.Field(typeof(AiTaskMeleeAttack), "minDist");
    static readonly FieldInfo MinVerDistField =
        AccessTools.Field(typeof(AiTaskMeleeAttack), "minVerDist");
    static readonly FieldInfo AttackedByField =
        AccessTools.Field(typeof(AiTaskBaseTargetable), "attackedByEntity");
    static readonly FieldInfo TamingField =
        AccessTools.Field(typeof(AiTaskBaseTargetable), "tamingGenerations");
    static readonly MethodInfo HasDirectContactMethod =
        AccessTools.Method(typeof(AiTaskBaseTargetable), "hasDirectContact");
    static readonly MethodInfo IsInEmotionStatesMethod =
        AccessTools.Method(typeof(AiTaskBase), "IsInEmotionState", [typeof(string[])]);

    [HarmonyPrefix]
    [HarmonyPriority(Priority.High)]
    public static bool Prefix(AiTaskMeleeAttack __instance, ref bool __result)
    {
        if (__instance?.entity == null
            || !AnimalAlertService.IsCommitted(__instance.entity)
            || !AnimalAlertService.TryGetAlertTarget(__instance.entity, out Entity? target)
            || target is not EntityPlayer
            || !AnimalAlertService.TaskCanTargetPlayer(__instance))
        {
            return true;
        }

        if (WhenInEmotionField.GetValue(__instance) is string[] emotions
            && emotions.Length > 0
            && IsInEmotionStatesMethod?.Invoke(__instance, [emotions]) is not true)
        {
            return true;
        }

        int generation = __instance.GetOwnGeneration();
        float taming = TamingField?.GetValue(__instance) is float t ? t : 10f;
        bool fullyTamed = generation >= taming;
        Entity? attackedBy = AttackedByField?.GetValue(__instance) as Entity;
        if (fullyTamed && (attackedBy == null || attackedBy.EntityId != target.EntityId))
        {
            return true;
        }

        float minDist = MinDistField?.GetValue(__instance) is float md ? md : 2f;
        float minVer = MinVerDistField?.GetValue(__instance) is float mv ? mv : 1f;
        bool contact = HasDirectContactMethod != null
            && HasDirectContactMethod.Invoke(__instance, [target, minDist, minVer]) is true;
        if (!contact)
        {
            __result = false;
            return false;
        }

        __instance.targetEntity = target;
        __result = true;
        return false;
    }
}

[HarmonyPatch(typeof(AiTaskIdle), "entityInRange")]
public static class AnimalAlertIdleInRangePatch
{
    [HarmonyPostfix]
    public static void Postfix(AiTaskIdle __instance, ref bool __result)
    {
        if (__result || __instance?.entity == null || !AnimalAlertService.IsAwake(__instance.entity))
        {
            return;
        }

        __result = true;
    }
}

[HarmonyPatch(typeof(Entity), nameof(Entity.ReceiveDamage))]
public static class AnimalAlertDamagePatch
{
    [HarmonyPostfix]
    public static void Postfix(Entity __instance, DamageSource damageSource, bool __result)
    {
        if (!__result
            || __instance?.World?.Side != EnumAppSide.Server
            || damageSource == null
            || damageSource.Type == EnumDamageType.Heal)
        {
            return;
        }

        Entity? attacker = damageSource.GetCauseEntity();
        if (attacker is not EntityPlayer)
        {
            return;
        }

        AnimalAlertService.ApplyDamageSpike(
            __instance,
            attacker,
            __instance.World.Rand ?? Random.Shared);
    }
}
