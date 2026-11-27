using System.Reflection;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace Prosequor.Ability;

/// <summary>
/// Passive-prey flee whose lifetime is the alert meter's committed latch.
/// Pathing stays with <see cref="AiTaskFleeEntity"/>. Vanilla player-flee does not run.
/// </summary>
public sealed class AiTaskThreatFlee : AiTaskFleeEntity
{
    public const string TaskCode = "prosequorthreatflee";

    static readonly FieldInfo? TargetPosField =
        HarmonyLib.AccessTools.Field(typeof(AiTaskFleeEntity), "targetPos");
    static readonly FieldInfo? FleeingDistanceField =
        HarmonyLib.AccessTools.Field(typeof(AiTaskFleeEntity), "fleeingDistance");
    static readonly FieldInfo? NowFleeingDistanceField =
        HarmonyLib.AccessTools.Field(typeof(AiTaskFleeEntity), "nowFleeingDistance");

    long lastRestartMs;

    /// <summary>
    /// Fish keep a land flop task. Their flee is a water behavior, so this
    /// replacement must not take the slot while they are out of the water.
    /// </summary>
    bool onlyWhileSwimming;

    public AiTaskThreatFlee(EntityAgent entity, JsonObject taskConfig, JsonObject aiConfig)
        : base(entity, taskConfig, aiConfig)
    {
    }

    public static AiTaskThreatFlee Create(EntityAgent agent, AiTaskFleeEntity? styleFrom)
    {
        float moveSpeed = 0.06f;
        string animation = "Run";
        if (styleFrom != null)
        {
            object? speed = AccessToolsMoveSpeed(styleFrom);
            if (speed is float s && s > 0f)
            {
                moveSpeed = s;
            }
        }

        JObject config = new()
        {
            ["code"] = TaskCode,
            ["priority"] = 3f,
            ["priorityForCancel"] = 3f,
            ["slot"] = 0,
            ["movespeed"] = moveSpeed,
            ["seekingRange"] = 30f,
            ["executionChance"] = 1f,
            ["animation"] = animation,
            ["animationSpeed"] = 2f,
            ["entityCodes"] = new JArray("player")
        };

        AiTaskThreatFlee task = new(agent, new JsonObject(config), new JsonObject(new JObject()));
        task.onlyWhileSwimming = HostFlopsOnLand(agent);
        if (!string.IsNullOrEmpty(styleFrom?.animMeta?.Code))
        {
            // The source flee already resolved its clip. Mods rename that clip
            // (swimfast, gallop, and so on); the replacement has to play that one.
            task.animMeta = styleFrom!.animMeta.Clone();
        }
        HarmonyLib.AccessTools.Field(typeof(AiTaskBase), "priority")?.SetValue(task, 3f);
        PropertyInfo? slot = HarmonyLib.AccessTools.Property(typeof(AiTaskBase), "Slot");
        if (slot?.CanWrite == true)
        {
            slot.SetValue(task, 0);
        }

        PropertyInfo? priority = HarmonyLib.AccessTools.Property(typeof(AiTaskBase), "Priority");
        if (priority?.CanWrite == true)
        {
            priority.SetValue(task, 3f);
        }

        return task;
    }

    public override bool ShouldExecute()
    {
        if (!WaterGateAllows() || !TryReadyTarget(out Entity target))
        {
            return false;
        }

        targetEntity = target;
        return true;
    }

    public override void StartExecute()
    {
        if (TryReadyTarget(out Entity target))
        {
            targetEntity = target;
        }

        // Vanilla writes the away-point and the leg length in ShouldExecute.
        // The walk reads both, and this override is the method that used to write them.
        AimFleeLeg();
        lastRestartMs = entity.World.ElapsedMilliseconds;
        base.StartExecute();
    }

    public override bool ContinueExecute(float dt)
    {
        if (!WaterGateAllows() || !TryReadyTarget(out Entity target))
        {
            return false;
        }

        targetEntity = target;
        if (!base.ContinueExecute(dt))
        {
            long now = entity.World.ElapsedMilliseconds;
            if (now - lastRestartMs > 250)
            {
                StartExecute();
            }
        }

        return true;
    }

    void AimFleeLeg()
    {
        if (targetEntity == null || TargetPosField?.GetValue(this) is not Vec3d point)
        {
            return;
        }

        float yaw = (float)Math.Atan2(
            targetEntity.Pos.X - entity.Pos.X,
            targetEntity.Pos.Z - entity.Pos.Z);
        updateTargetPosFleeMode(point, yaw);

        float leg = FleeingDistanceField?.GetValue(this) is float fleeing && fleeing > 0f ? fleeing : 45f;
        NowFleeingDistanceField?.SetValue(this, leg);
    }

    bool TryReadyTarget(out Entity target)
    {
        target = null!;
        if (!AnimalAlertService.IsCommitted(entity)
            || !AnimalAlertService.TryGetAlertTarget(entity, out Entity? alertTarget)
            || alertTarget is not EntityPlayer
            || !alertTarget.Alive)
        {
            return false;
        }

        target = alertTarget;
        return true;
    }

    static object? AccessToolsMoveSpeed(AiTaskFleeEntity flee) =>
        HarmonyLib.AccessTools.Field(typeof(AiTaskFleeEntity), "moveSpeed")?.GetValue(flee);

    bool WaterGateAllows() => !onlyWhileSwimming || entity.Swimming;

    static bool HostFlopsOnLand(EntityAgent agent)
    {
        EntityBehaviorTaskAI? taskAi = agent.GetBehavior<EntityBehaviorTaskAI>();
        if (taskAi?.TaskManager == null)
        {
            return false;
        }

        foreach (IAiTask task in taskAi.TaskManager.AllTasks)
        {
            if (task is AiTaskFishOutOfWater)
            {
                return true;
            }
        }

        return false;
    }
}
