using System.Reflection;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.GameContent;

namespace Prosequor.Ability;

/// <summary>
/// Passive-prey flee whose lifetime is the alert meter's committed latch.
/// Pathing stays with <see cref="AiTaskFleeEntity"/>. Vanilla player-flee does not run.
/// </summary>
public sealed class AiTaskThreatFlee : AiTaskFleeEntity
{
    public const string TaskCode = "prosequorthreatflee";

    long lastRestartMs;

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

            object? anim = AccessToolsAnimation(styleFrom);
            if (anim is string named && named.Length > 0)
            {
                animation = named;
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
        if (!TryReadyTarget(out Entity target))
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

        lastRestartMs = entity.World.ElapsedMilliseconds;
        base.StartExecute();
    }

    public override bool ContinueExecute(float dt)
    {
        if (!TryReadyTarget(out Entity target))
        {
            return false;
        }

        targetEntity = target;
        if (!base.ContinueExecute(dt))
        {
            long now = entity.World.ElapsedMilliseconds;
            if (now - lastRestartMs > 250)
            {
                lastRestartMs = now;
                base.StartExecute();
            }
        }

        return true;
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

    static object? AccessToolsAnimation(AiTaskFleeEntity flee) =>
        HarmonyLib.AccessTools.Field(typeof(AiTaskBase), "animation")?.GetValue(flee)
        ?? HarmonyLib.AccessTools.Field(typeof(AiTaskBase), "animCode")?.GetValue(flee);
}
