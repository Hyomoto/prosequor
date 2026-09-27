using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.GameContent;

namespace Prosequor.Ability;

/// <summary>
/// Server alert meter: tracks player-sensing animals, integrates threat, and
/// exposes latches for AI task gates.
/// </summary>
public static class AnimalAlertService
{
    public const int TickMs = 200;

    /// <summary>WatchedAttributes: alert 0–100 (client overlay).</summary>
    public const string AttrAlert = "prosequorAlert";

    /// <summary>WatchedAttributes: current threat 0–100 (client overlay).</summary>
    public const string AttrThreat = "prosequorThreat";

    /// <summary>
    /// WatchedAttributes bit flags: 1 = awake, 2 = committed (client overlay color).
    /// </summary>
    public const string AttrFlags = "prosequorAlertFlags";

    public const int FlagAwake = 1;
    public const int FlagCommitted = 2;

    /// <summary>WatchedAttributes: entity id of the alert target while committed.</summary>
    public const string AttrTarget = "prosequorAlertTarget";

    static readonly ConditionalWeakTable<Entity, AnimalAlertState> States = new();
    static readonly List<Entity> Tracked = new();
    static readonly HashSet<long> TrackedIds = new();

    static readonly FieldInfo FleeSeekingRangeField =
        AccessTools.Field(typeof(AiTaskFleeEntity), "seekingRange");
    static readonly FieldInfo FleeInstaChanceField =
        AccessTools.Field(typeof(AiTaskFleeEntity), "instafleeOnDamageChance");
    static readonly FieldInfo FleeTamingField =
        AccessTools.Field(typeof(AiTaskBaseTargetable), "tamingGenerations");
    static readonly FieldInfo TargetExactField =
        AccessTools.Field(typeof(AiTaskBaseTargetable), "targetEntityCodesExact");
    static readonly FieldInfo TargetBeginsField =
        AccessTools.Field(typeof(AiTaskBaseTargetable), "targetEntityCodesBeginsWith");
    static readonly FieldInfo TargetFirstLettersField =
        AccessTools.Field(typeof(AiTaskBaseTargetable), "targetEntityFirstLetters");

    public static AnimalAlertState GetOrCreate(Entity entity) =>
        States.GetOrCreateValue(entity);

    public static bool TryGet(Entity? entity, out AnimalAlertState state)
    {
        state = null!;
        return entity != null && States.TryGetValue(entity, out state!);
    }

    public static bool IsAwake(Entity? entity) =>
        TryGet(entity, out AnimalAlertState state) && state.Awake;

    public static bool IsCommitted(Entity? entity) =>
        TryGet(entity, out AnimalAlertState state) && state.Committed;

    public static bool TryGetAlertTarget(Entity? animal, out Entity? target)
    {
        target = null;
        if (!TryGet(animal, out AnimalAlertState state)
            || state.AlertTargetEntityId == 0
            || animal?.World == null)
        {
            return false;
        }

        target = animal.World.GetEntityById(state.AlertTargetEntityId);
        return target != null && target.Alive;
    }

    public static void ConsiderTracking(EntityBehaviorTaskAI taskAi)
    {
        if (taskAi?.entity == null
            || taskAi.entity.World?.Side != EnumAppSide.Server
            || taskAi.TaskManager == null)
        {
            return;
        }

        Entity entity = taskAi.entity;
        AnimalAlertState state = GetOrCreate(entity);
        ScanTasks(entity, taskAi.TaskManager, state);
        if (!state.SensesPlayers)
        {
            return;
        }

        RestoreLatch(entity, state);
        EnsureThreatFlee(taskAi);
        EnsureInTrackedList(entity);
    }

    /// <summary>
    /// Chunk reload: committed flag and target id live on the entity, so resume
    /// flees without a new rising-threat roll.
    /// </summary>
    static void RestoreLatch(Entity entity, AnimalAlertState state)
    {
        if (state.Committed || entity.WatchedAttributes == null)
        {
            return;
        }

        int flags = entity.WatchedAttributes.GetInt(AttrFlags, 0);
        if ((flags & FlagCommitted) == 0)
        {
            return;
        }

        int alertQ = entity.WatchedAttributes.GetInt(AttrAlert, 0);
        state.Committed = true;
        state.Awake = true;
        state.WasCommitted = true;
        state.Alert = Math.Max(alertQ, AnimalAlertMath.CommitExit);
        state.AlertTargetEntityId = entity.WatchedAttributes.GetLong(AttrTarget, 0L);
        state.LastSyncedAlert = alertQ;
        state.LastSyncedFlags = flags;
        state.LastSyncedTarget = state.AlertTargetEntityId;
    }

    /// <summary>
    /// Passive prey with an ungated player fleeentity get a task we own.
    /// </summary>
    public static void EnsureThreatFlee(EntityBehaviorTaskAI taskAi)
    {
        if (taskAi?.entity is not EntityAgent agent || taskAi.TaskManager == null)
        {
            return;
        }

        if (!HasUngatedPlayerFlee(agent))
        {
            return;
        }

        foreach (IAiTask task in taskAi.TaskManager.AllTasks)
        {
            if (task is AiTaskThreatFlee)
            {
                return;
            }
        }

        AiTaskFleeEntity? style = null;
        foreach (IAiTask task in taskAi.TaskManager.AllTasks)
        {
            if (task is AiTaskThreatFlee)
            {
                continue;
            }

            if (task is AiTaskFleeEntity flee && IsUngatedPlayerFlee(flee))
            {
                style = flee;
                break;
            }
        }

        taskAi.TaskManager.AddTask(AiTaskThreatFlee.Create(agent, style));
    }

    public static AiTaskThreatFlee? FindThreatFlee(Entity? animal)
    {
        EntityBehaviorTaskAI? taskAi = animal?.GetBehavior<EntityBehaviorTaskAI>();
        if (taskAi?.TaskManager == null)
        {
            return null;
        }

        foreach (IAiTask task in taskAi.TaskManager.AllTasks)
        {
            if (task is AiTaskThreatFlee flee)
            {
                return flee;
            }
        }

        return null;
    }

    public static void OnEntityDespawn(Entity? entity)
    {
        if (entity == null || !TrackedIds.Remove(entity.EntityId))
        {
            return;
        }

        for (int i = Tracked.Count - 1; i >= 0; i--)
        {
            if (Tracked[i].EntityId == entity.EntityId)
            {
                Tracked.RemoveAt(i);
                break;
            }
        }
    }

    /// <summary>
    /// Atlas / unit isolation: drop static tracking so EntityId reuse across fresh
    /// worlds cannot leave a new animal untracked.
    /// </summary>
    public static void ClearTrackingForTests()
    {
        Tracked.Clear();
        TrackedIds.Clear();
    }

    /// <summary>
    /// Integrate one animal immediately (bypasses the staggered slice).
    /// </summary>
    public static void TickEntity(Entity entity, float dt)
    {
        if (entity?.World == null || !TryGet(entity, out AnimalAlertState state))
        {
            return;
        }

        float stepDt = dt > 0f
            ? dt
            : state.LastIntegrateMs > 0
                ? Math.Clamp(
                    (entity.World.ElapsedMilliseconds - state.LastIntegrateMs) / 1000f,
                    0.05f,
                    1.5f)
                : 1f;
        state.LastIntegrateMs = entity.World.ElapsedMilliseconds;
        IntegrateEntity(entity, state, stepDt);
    }

    public static void Tick(float dt)
    {
        if (Tracked.Count == 0)
        {
            return;
        }

        // Nearby / meter-active animals every fire so the overlay and flee cool-off
        // stay live. Far idle animals are skipped (no work until a player approaches).
        for (int i = Tracked.Count - 1; i >= 0; i--)
        {
            Entity entity = Tracked[i];
            if (entity?.World == null || !entity.Alive)
            {
                OnEntityDespawn(entity);
                continue;
            }

            if (!TryGet(entity, out AnimalAlertState state) || !state.SensesPlayers)
            {
                continue;
            }

            // Do not untrack Inactive — pathing/flee can flap Active and used to
            // permanently freeze meters (last synced threat stuck forever).
            bool meterHot = state.Alert > 0f || state.Awake || state.Committed;
            if (entity.State != EnumEntityState.Active && !meterHot)
            {
                continue;
            }

            if (!meterHot && !IsNearAnySurvivalPlayer(entity, state))
            {
                continue;
            }

            long nowMs = entity.World.ElapsedMilliseconds;
            float stepDt = state.LastIntegrateMs > 0
                ? Math.Clamp((nowMs - state.LastIntegrateMs) / 1000f, 0.05f, 1.5f)
                : (dt > 0f ? dt : AnimalAlertService.TickMs / 1000f);
            state.LastIntegrateMs = nowMs;
            IntegrateEntity(entity, state, stepDt);
        }
    }

    static bool IsNearAnySurvivalPlayer(Entity entity, AnimalAlertState state)
    {
        IPlayer[]? players = entity.World?.AllOnlinePlayers;
        if (players == null)
        {
            return false;
        }

        float range = state.CharacteristicRange;
        // Small pad so approaching from just outside still starts ticks promptly.
        float pad = range + 4f;
        for (int i = 0; i < players.Length; i++)
        {
            IPlayer player = players[i];
            if (player?.Entity is not EntityPlayer ep || !ep.Alive)
            {
                continue;
            }

            EnumGameMode mode = player.WorldData.CurrentGameMode;
            if (mode == EnumGameMode.Creative || mode == EnumGameMode.Spectator)
            {
                continue;
            }

            if (ep.Pos.Dimension != entity.Pos.Dimension)
            {
                continue;
            }

            if (entity.Pos.DistanceTo(ep.Pos) <= pad)
            {
                return true;
            }
        }

        return false;
    }

    static void EnsureInTrackedList(Entity entity)
    {
        if (TrackedIds.Add(entity.EntityId))
        {
            Tracked.Add(entity);
            return;
        }

        // EntityId reuse across worlds: replace the stale reference.
        for (int i = 0; i < Tracked.Count; i++)
        {
            if (Tracked[i].EntityId != entity.EntityId)
            {
                continue;
            }

            if (!ReferenceEquals(Tracked[i], entity))
            {
                Tracked[i] = entity;
            }

            return;
        }

        Tracked.Add(entity);
    }

    public static void ApplyDamageSpike(Entity animal, Entity? attacker, Random rand)
    {
        if (animal?.World?.Side != EnumAppSide.Server
            || attacker == null
            || !TryGet(animal, out AnimalAlertState state)
            || !state.SensesPlayers)
        {
            return;
        }

        if (state.InstaFleeOnDamageChance <= 0f
            || rand.NextDouble() >= state.InstaFleeOnDamageChance)
        {
            return;
        }

        state.AlertTargetEntityId = attacker.EntityId;
        state.AlertTargetThreat = 1f;
        state.CurrentThreat = 1f;
        state.Alert = AnimalAlertMath.ApplyDamageCommit(state.Alert);
        BeginFleeBolt(animal, state, propagatedHerd: false);
        SyncWatched(animal, state);
    }

    public static void ApplyHerdAlarm(Entity recipient, Entity threatTarget)
    {
        if (recipient?.World?.Side != EnumAppSide.Server
            || threatTarget == null
            || !TryGet(recipient, out AnimalAlertState state)
            || !state.SensesPlayers)
        {
            return;
        }

        state.Alert = AnimalAlertMath.ApplyHerdAlarmFloor(state.Alert);
        if (AnimalAlertMath.ShouldSwitchTarget(state.AlertTargetThreat, 0.85f)
            || state.AlertTargetEntityId == 0)
        {
            state.AlertTargetEntityId = threatTarget.EntityId;
            state.AlertTargetThreat = 0.85f;
        }

        BeginFleeBolt(recipient, state, propagatedHerd: true);
        SyncWatched(recipient, state);
    }

    /// <summary>
    /// Atlas: force spooked/flee-bolt toward a player without waiting on rolls.
    /// </summary>
    public static void ForcePanicForTests(Entity animal, Entity target)
    {
        if (animal == null || target == null)
        {
            return;
        }

        AnimalAlertState state = GetOrCreate(animal);
        state.SensesPlayers = true;
        state.AlertTargetEntityId = target.EntityId;
        state.AlertTargetThreat = 1f;
        state.CurrentThreat = 1f;
        state.Alert = AnimalAlertMath.ApplyDamageCommit(state.Alert);
        BeginFleeBolt(animal, state, propagatedHerd: false);
        EnsureInTrackedList(animal);
        SyncWatched(animal, state);
    }

    /// <summary>
    /// Flee behavior triggered: double alert on first commit and optional herd broadcast.
    /// The owned threat-flee task notices the latch on the next AI tick.
    /// </summary>
    static void BeginFleeBolt(Entity entity, AnimalAlertState state, bool propagatedHerd)
    {
        bool wasCommitted = state.Committed;
        if (!wasCommitted)
        {
            state.Alert = AnimalAlertMath.ApplyFleeBolt(state.Alert);
        }

        state.Awake = true;
        state.Committed = true;
        state.PropagatedHerd = propagatedHerd;

        if (state.AlertTargetEntityId != 0)
        {
            Entity? target = entity.World.GetEntityById(state.AlertTargetEntityId);
            if (target != null
                && AnimalAlertMath.MayBroadcastHerd(
                    state.Committed,
                    wasCommitted,
                    state.PropagatedHerd))
            {
                BroadcastHerdAlarm(entity, state, target);
            }
        }

        state.WasCommitted = state.Committed;
    }

    static void IntegrateEntity(Entity entity, AnimalAlertState state, float dt)
    {
        if (!state.SensesPlayers)
        {
            return;
        }

        // 1. Calculate raw incoming threat from nearby survival players.
        float bestRaw = 0f;
        long bestId = 0;
        int bestBand = AnimalAlertMath.BandStill;
        float currentTargetRaw = 0f;
        bool haveCurrent = state.AlertTargetEntityId != 0;
        Entity? currentTarget = haveCurrent
            ? entity.World.GetEntityById(state.AlertTargetEntityId)
            : null;
        if (currentTarget is not { Alive: true })
        {
            haveCurrent = false;
            state.AlertTargetEntityId = 0;
            state.AlertTargetThreat = 0f;
        }

        // Base characteristic range only. Spooked perception (×2) applies inside
        // that range via EffectiveIncoming; widening the sample radius kept cool-off unreachable.
        float range = state.CharacteristicRange;
        IPlayer[]? players = entity.World.AllOnlinePlayers;
        if (players != null)
        {
            for (int i = 0; i < players.Length; i++)
            {
                IPlayer player = players[i];
                if (player?.Entity is not EntityPlayer ep || !ep.Alive)
                {
                    continue;
                }

                if (ep.Pos.Dimension != entity.Pos.Dimension)
                {
                    continue;
                }

                EnumGameMode mode = player.WorldData.CurrentGameMode;
                if (mode == EnumGameMode.Creative || mode == EnumGameMode.Spectator)
                {
                    continue;
                }

                EntityControls controls = ep.ServerControls;
                bool sneak = controls.Sneak;
                float playerRange = range;
                if (sneak)
                {
                    float senseFactor = PlayerInteractionStation.ResolveAnimalSenseRangeFactor(player);
                    if (senseFactor > 0f && Math.Abs(senseFactor - 1f) > 0.0001f)
                    {
                        playerRange = Math.Max(1f, range * senseFactor);
                    }
                }

                float dist = (float)entity.Pos.DistanceTo(ep.Pos);
                if (dist > playerRange)
                {
                    continue;
                }

                bool sprint = controls.Sprint;
                bool moving = controls.TriesToMove
                    || Math.Abs(controls.WalkVector.X) + Math.Abs(controls.WalkVector.Z) > 0.001
                    || ep.Pos.Motion.LengthSq() > 0.0001;
                int band = AnimalAlertMath.MovementBand(moving, sneak, sprint);

                // Proximity uses the base range (narrowed while sneaking).
                float raw = EvaluateRawThreat(
                    entity,
                    state,
                    player,
                    ep,
                    dist,
                    playerRange,
                    moving,
                    sneak,
                    sprint);
                if (haveCurrent && ep.EntityId == state.AlertTargetEntityId)
                {
                    currentTargetRaw = raw;
                }

                if (raw > bestRaw)
                {
                    bestRaw = raw;
                    bestId = ep.EntityId;
                    bestBand = band;
                }
            }
        }

        float chosenRaw = bestRaw;
        if (bestId != 0
            && (!haveCurrent
                || bestId == state.AlertTargetEntityId
                || AnimalAlertMath.ShouldSwitchTarget(currentTargetRaw, bestRaw)))
        {
            state.AlertTargetEntityId = bestId;
            state.AlertTargetThreat = bestRaw;
        }
        else if (haveCurrent)
        {
            state.AlertTargetThreat = currentTargetRaw;
            chosenRaw = Math.Max(chosenRaw, currentTargetRaw);
        }

        // 2. If spooked and the source is still inside base range, multiply threat.
        float incoming = AnimalAlertMath.EffectiveIncoming(chosenRaw, state.Committed);
        state.CurrentThreat = incoming;

        if (incoming <= 0f && state.Alert <= 0f && !state.Awake && !state.Committed)
        {
            state.LastThreatSample = incoming;
            SyncWatched(entity, state);
            return;
        }

        float previousThreat = state.LastThreatSample;
        int previousBand = state.LastMovementBand;
        bool wasCommitted = state.Committed;

        // 3–4. Incoming raises/maintains accumulated alert; ~0 incoming decays it.
        state.Alert = AnimalAlertMath.Integrate(state.Alert, incoming, dt);
        state.Awake = AnimalAlertMath.UpdateAwake(state.Awake, state.Alert);

        // 5–6. Reaction chance from accumulated alert; roll only on rise/posture.
        bool boltedNow = false;
        if (!state.Committed
            && AnimalAlertMath.ShouldAttemptFleeRoll(
                committed: false,
                state.Alert,
                previousThreat,
                incoming,
                previousBand,
                bestBand,
                hasTarget: bestId != 0 || haveCurrent))
        {
            float p = AnimalAlertMath.FleeChance(state.Alert);
            Random rand = entity.World.Rand ?? Random.Shared;
            if (rand.NextDouble() < p)
            {
                BeginFleeBolt(entity, state, propagatedHerd: false);
                boltedNow = true;
            }
        }

        // 7. Leave spooked once accumulated alert cools below CommitExit.
        if (state.Committed && !boltedNow)
        {
            state.Committed = AnimalAlertMath.UpdateCommitted(committed: true, state.Alert);
            if (!state.Committed && wasCommitted)
            {
                state.PropagatedHerd = false;
            }
        }

        state.LastThreatSample = incoming;
        if (bestId != 0)
        {
            state.LastMovementBand = bestBand;
        }

        state.WasCommitted = state.Committed;
        SyncWatched(entity, state);
    }

    /// <summary>
    /// Publishes quantized alert/threat for the client overlay. Clears attrs when idle.
    /// </summary>
    public static void SyncWatched(Entity entity, AnimalAlertState state)
    {
        if (entity?.WatchedAttributes == null || entity.World?.Side != EnumAppSide.Server)
        {
            return;
        }

        int alertQ = (int)Math.Clamp(Math.Round(state.Alert), 0, 100);
        int threatQ = (int)Math.Clamp(Math.Round(state.CurrentThreat * 100f), 0, 100);
        int flags = (state.Awake ? FlagAwake : 0) | (state.Committed ? FlagCommitted : 0);
        long targetId = state.Committed ? state.AlertTargetEntityId : 0L;

        if (alertQ == 0 && threatQ == 0 && flags == 0)
        {
            if (state.LastSyncedAlert == 0
                && state.LastSyncedThreat == 0
                && state.LastSyncedFlags == 0
                && state.LastSyncedTarget == 0)
            {
                return;
            }

            if (entity.WatchedAttributes.HasAttribute(AttrAlert))
            {
                entity.WatchedAttributes.RemoveAttribute(AttrAlert);
                entity.WatchedAttributes.MarkPathDirty(AttrAlert);
            }

            if (entity.WatchedAttributes.HasAttribute(AttrThreat))
            {
                entity.WatchedAttributes.RemoveAttribute(AttrThreat);
                entity.WatchedAttributes.MarkPathDirty(AttrThreat);
            }

            if (entity.WatchedAttributes.HasAttribute(AttrFlags))
            {
                entity.WatchedAttributes.RemoveAttribute(AttrFlags);
                entity.WatchedAttributes.MarkPathDirty(AttrFlags);
            }

            if (entity.WatchedAttributes.HasAttribute(AttrTarget))
            {
                entity.WatchedAttributes.RemoveAttribute(AttrTarget);
                entity.WatchedAttributes.MarkPathDirty(AttrTarget);
            }

            state.LastSyncedAlert = 0;
            state.LastSyncedThreat = 0;
            state.LastSyncedFlags = 0;
            state.LastSyncedTarget = 0;
            return;
        }

        if (alertQ == state.LastSyncedAlert
            && threatQ == state.LastSyncedThreat
            && flags == state.LastSyncedFlags
            && targetId == state.LastSyncedTarget)
        {
            return;
        }

        entity.WatchedAttributes.SetInt(AttrAlert, alertQ);
        entity.WatchedAttributes.SetInt(AttrThreat, threatQ);
        entity.WatchedAttributes.SetInt(AttrFlags, flags);
        if (targetId != 0)
        {
            entity.WatchedAttributes.SetLong(AttrTarget, targetId);
        }
        else if (entity.WatchedAttributes.HasAttribute(AttrTarget))
        {
            entity.WatchedAttributes.RemoveAttribute(AttrTarget);
        }

        entity.WatchedAttributes.MarkPathDirty(AttrAlert);
        entity.WatchedAttributes.MarkPathDirty(AttrThreat);
        entity.WatchedAttributes.MarkPathDirty(AttrFlags);
        entity.WatchedAttributes.MarkPathDirty(AttrTarget);
        state.LastSyncedAlert = alertQ;
        state.LastSyncedThreat = threatQ;
        state.LastSyncedFlags = flags;
        state.LastSyncedTarget = targetId;
    }

    public static bool TryReadOverlay(
        Entity entity,
        out int alert,
        out int threat,
        out bool awake,
        out bool committed)
    {
        alert = 0;
        threat = 0;
        awake = false;
        committed = false;
        if (entity?.WatchedAttributes == null)
        {
            return false;
        }

        alert = entity.WatchedAttributes.GetInt(AttrAlert, 0);
        threat = entity.WatchedAttributes.GetInt(AttrThreat, 0);
        int flags = entity.WatchedAttributes.GetInt(AttrFlags, 0);
        awake = (flags & FlagAwake) != 0;
        committed = (flags & FlagCommitted) != 0;
        return alert > 0 || threat > 0 || awake || committed;
    }

    static float EvaluateRawThreat(
        Entity animal,
        AnimalAlertState state,
        IPlayer player,
        EntityPlayer ep,
        float distance,
        float senseRange,
        bool moving,
        bool sneak,
        bool sprint)
    {
        float proximity = AnimalAlertMath.Proximity(distance, senseRange);
        if (proximity <= 0f)
        {
            return 0f;
        }

        float movement = AnimalAlertMath.MovementDisturbance(moving, sneak, sprint);

        int threatPct = PlayerInteractionStation.ResolveAnimalThreatPercent(player);
        if (threatPct < 1)
        {
            threatPct = 100;
        }

        float detectability = AnimalAlertMath.ThreatEmissionFromPercent(threatPct);
        if (sneak)
        {
            float sneakFactor = PlayerInteractionStation.ResolveAnimalThreatSneakFactor(player);
            if (sneakFactor > 0f && Math.Abs(sneakFactor - 1f) > 0.0001f)
            {
                detectability *= sneakFactor;
            }
        }

        int generation = animal.WatchedAttributes.GetInt("generation", 0);
        JsonObject? attrs = animal.Properties?.Attributes;
        if (attrs != null && attrs.IsTrue("tamed"))
        {
            generation += 10;
        }

        float genFear = AnimalAlertMath.GenerationFear(generation, state.TamingGenerations);
        int friendliness = HusbandryFriendliness.Get(animal);
        float fleeMult = HusbandryFriendliness.MultFromPercent(
            AnimalBehaviorStation.ResolveFleeFearMultiplierPercent(player, animal));
        float broodMult = HusbandryFriendliness.MultFromPercent(
            AnimalBehaviorStation.ResolveBroodMultiplierPercent(player, animal));
        float fleeChanceReduction = AnimalBehaviorStation.ResolveFleeChanceReduction(player, animal);
        bool favorite = HusbandryFriendliness.TryGetFavoriteSeraph(animal, out string? fav)
            && string.Equals(fav, player.PlayerUID, StringComparison.Ordinal);
        float ordinary = AnimalAlertMath.OrdinaryScale(
            genFear,
            friendliness,
            fleeMult,
            broodMult,
            fleeChanceReduction,
            favorite);

        return AnimalAlertMath.Threat(
            proximity,
            movement,
            detectability,
            ordinary,
            sprint && moving);
    }

    static void BroadcastHerdAlarm(Entity source, AnimalAlertState sourceState, Entity threatTarget)
    {
        if (source is not EntityAgent agent || agent.HerdId <= 0)
        {
            return;
        }

        long herdId = agent.HerdId;
        float range = Math.Max(1f, sourceState.HerdNotifyRange);
        source.World.GetNearestEntity(
            source.Pos.XYZ,
            range,
            range,
            e =>
            {
                if (e.EntityId == source.EntityId
                    || e is not EntityAgent other
                    || !other.Alive
                    || other.HerdId != herdId)
                {
                    return true;
                }

                ApplyHerdAlarm(other, threatTarget);
                // false = keep walking (vanilla alarmherd pattern).
                return false;
            });
    }

    static void ScanTasks(Entity entity, AiTaskManager manager, AnimalAlertState state)
    {
        float maxSeek = 0f;
        float taming = 10f;
        float insta = 0f;
        bool ungatedFlee = false;
        bool ungatedHunt = false;

        foreach (IAiTask task in manager.AllTasks)
        {
            switch (task)
            {
                case AiTaskFleeEntity flee:
                    if (!TaskCanTargetPlayer(flee))
                    {
                        break;
                    }

                    if (FleeTamingField.GetValue(flee) is float tg)
                    {
                        taming = tg;
                    }

                    // Daylight or damage flees that are emotion-gated are not prey panic.
                    if (EmotionGates(flee))
                    {
                        break;
                    }

                    ungatedFlee = true;
                    if (FleeSeekingRangeField.GetValue(flee) is float fr)
                    {
                        maxSeek = Math.Max(maxSeek, fr);
                    }

                    if (FleeInstaChanceField.GetValue(flee) is float ic)
                    {
                        insta = Math.Max(insta, ic);
                    }

                    break;
                case AiTaskSeekEntity seek:
                    if (TaskCanTargetPlayer(seek) && !EmotionGates(seek))
                    {
                        ungatedHunt = true;
                    }

                    break;
                case AiTaskMeleeAttack melee:
                    if (TaskCanTargetPlayer(melee) && !EmotionGates(melee))
                    {
                        ungatedHunt = true;
                    }

                    break;
            }
        }

        EmotionState? alarm = FindAlarmHerdState(entity);
        state.HerdNotifyRange = alarm?.NotifyRange > 0f ? alarm.NotifyRange : 12f;
        float resolvedRange = maxSeek > 0f ? maxSeek : AnimalAlertMath.MinCharacteristicRange;
        state.CharacteristicRange = Math.Max(AnimalAlertMath.MinCharacteristicRange, resolvedRange);
        state.TamingGenerations = taming;
        state.InstaFleeOnDamageChance = insta;
        // Passive prey only. An ungated player seek or melee means this creature
        // hunts the seraph (drifters, wolves) and must keep vanilla AI.
        state.SensesPlayers = ungatedFlee && !ungatedHunt;
    }

    static bool EmotionGates(IAiTask task)
    {
        FieldInfo whenField = AccessTools.Field(typeof(AiTaskBase), "WhenInEmotionStates");
        return whenField?.GetValue(task) is string[] emotions && emotions.Length > 0;
    }

    static EmotionState? FindAlarmHerdState(Entity entity)
    {
        EntityBehaviorEmotionStates? emo = entity.GetBehavior<EntityBehaviorEmotionStates>();
        if (emo == null)
        {
            return null;
        }

        FieldInfo? field = AccessTools.Field(typeof(EntityBehaviorEmotionStates), "availableStates");
        if (field?.GetValue(emo) is not EmotionState[] states)
        {
            return null;
        }

        for (int i = 0; i < states.Length; i++)
        {
            if (states[i].Code == "alarmherdondamage")
            {
                return states[i];
            }
        }

        return null;
    }

    internal static bool TaskCanTargetPlayer(AiTaskBaseTargetable task)
    {
        string firstLetters = TargetFirstLettersField.GetValue(task) as string ?? "";
        if (firstLetters.Length == 0)
        {
            return true;
        }

        if (TargetExactField.GetValue(task) is string[] exact)
        {
            for (int i = 0; i < exact.Length; i++)
            {
                if (exact[i] == "player")
                {
                    return true;
                }
            }
        }

        if (TargetBeginsField.GetValue(task) is string[] begins)
        {
            for (int i = 0; i < begins.Length; i++)
            {
                string prefix = begins[i];
                if (prefix.Length == 0 || "player".StartsWith(prefix, StringComparison.Ordinal))
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Atlas: after <see cref="ForcePanicForTests"/>, the owned threat-flee task must
    /// be ready and vanilla player-flee (including emotion-gated) must not start.
    /// </summary>
    public static bool TryProbePanicFlee(
        Entity animal,
        out int ungatedReady,
        out int gatedForcedWhileCalm)
    {
        ungatedReady = 0;
        gatedForcedWhileCalm = 0;
        EntityBehaviorTaskAI? taskAi = animal?.GetBehavior<EntityBehaviorTaskAI>();
        if (taskAi?.TaskManager == null || !IsCommitted(animal))
        {
            return false;
        }

        AiTaskThreatFlee? threat = FindThreatFlee(animal);
        if (threat?.ShouldExecute() == true)
        {
            ungatedReady = 1;
        }

        FieldInfo whenField = AccessTools.Field(typeof(AiTaskBase), "WhenInEmotionStates");
        foreach (IAiTask task in taskAi.TaskManager.AllTasks)
        {
            if (task is not AiTaskFleeEntity flee
                || task is AiTaskThreatFlee
                || !TaskCanTargetPlayer(flee))
            {
                continue;
            }

            if (!flee.ShouldExecute())
            {
                continue;
            }

            string[]? emotions = whenField?.GetValue(flee) as string[];
            if (emotions is { Length: > 0 })
            {
                gatedForcedWhileCalm++;
            }
            else
            {
                // Vanilla ungated player flee started; the owned task should own that path.
                gatedForcedWhileCalm++;
            }
        }

        return ungatedReady > 0 && gatedForcedWhileCalm == 0;
    }

    /// <summary>
    /// Atlas: whether an ungated player fleeentity's CanSensePlayer accepts this player
    /// (fused eligibility).
    /// </summary>
    public static bool TryCanSensePlayer(Entity animal, EntityPlayer player, out bool sensed)
    {
        sensed = false;
        EntityBehaviorTaskAI? taskAi = animal?.GetBehavior<EntityBehaviorTaskAI>();
        if (taskAi?.TaskManager == null || player == null)
        {
            return false;
        }

        FieldInfo whenField = AccessTools.Field(typeof(AiTaskBase), "WhenInEmotionStates");
        foreach (IAiTask task in taskAi.TaskManager.AllTasks)
        {
            if (task is not AiTaskFleeEntity flee || !TaskCanTargetPlayer(flee))
            {
                continue;
            }

            if (whenField?.GetValue(flee) is string[] emotions && emotions.Length > 0)
            {
                continue;
            }

            sensed = flee.CanSensePlayer(player, range: 64);
            return true;
        }

        return false;
    }

    /// <summary>
    /// True when the animal has a player fleeentity with no whenInEmotionState gate
    /// (the path meter-driven panic should use — chickens also have a gated flee).
    /// </summary>
    public static bool HasUngatedPlayerFlee(Entity animal)
    {
        EntityBehaviorTaskAI? taskAi = animal?.GetBehavior<EntityBehaviorTaskAI>();
        if (taskAi?.TaskManager == null)
        {
            return false;
        }

        foreach (IAiTask task in taskAi.TaskManager.AllTasks)
        {
            if (task is AiTaskFleeEntity flee && IsUngatedPlayerFlee(flee))
            {
                return true;
            }
        }

        return false;
    }

    static bool IsUngatedPlayerFlee(AiTaskFleeEntity flee)
    {
        if (!TaskCanTargetPlayer(flee))
        {
            return false;
        }

        return !EmotionGates(flee);
    }
}
