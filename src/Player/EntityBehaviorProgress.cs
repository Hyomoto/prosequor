using Prosequor.Ability;
using Prosequor.Ability.Hooks;
using Prosequor.Data;
using Prosequor.Network;
using Prosequor.Progress;
using Prosequor.Xp;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.Server;

namespace Prosequor.Player;

/// <summary>
/// Player-attached progress. Server owns ModData truth; clients merge sparse public
/// WatchedAttributes plus (for the owning client) owner-channel snapshot/deltas.
/// </summary>
public class EntityBehaviorProgress : EntityBehavior, IPlayerProgress, IAbilityComposeCache
{
    public const string Code = "prosequor:progress";

    PlayerProgressState state = new();
    bool loaded;
    bool dirty;
    bool publicMirrorReady;
    bool ownerChannelSynced;
    int syncSeq;
    int clientLastSeq;
    readonly PendingProgressFlush pending = new();
    ActiveAbilityRuleCache? abilityCache;
    readonly ComposeMemo composeMemo = new();

    public ComposeMemo ComposeMemo => composeMemo;

    public EntityBehaviorProgress(Entity entity) : base(entity)
    {
    }

    public override string PropertyName() => Code;

    public event Action? Changed;

    public PlayerProgressState State => state;

    public int PlayerLevel => state.PlayerLevel;
    public float PlayerXp => state.PlayerXp;
    public int UnlockPoints => state.UnlockPoints;

    /// <summary>
    /// True after server ModData load, or on the client after the owning channel snapshot
    /// (local player) / public WatchedAttributes merge (other players).
    /// HUD observers must wait for this so an empty pre-mirror 0 is not treated as first-seen.
    /// </summary>
    public bool HasSyncedMirror =>
        entity.World.Side == EnumAppSide.Server
            ? loaded
            : IsOwningClient
                ? ownerChannelSynced
                : publicMirrorReady;

    bool IsOwningClient =>
        entity.World.Side == EnumAppSide.Client
        && entity is EntityPlayer eplr
        && eplr.Player?.PlayerUID != null
        && string.Equals(
            eplr.Player.PlayerUID,
            entity.Api is Vintagestory.API.Client.ICoreClientAPI capi
                ? capi.World.Player?.PlayerUID
                : null,
            StringComparison.Ordinal);

    public float PlayerXpUntilNext =>
        XpCurves.XpUntilNextPlayerLevel(state.PlayerXp, state.PlayerLevel);

    public override void Initialize(EntityProperties properties, JsonObject attributes)
    {
        base.Initialize(properties, attributes);

        if (entity.World.Side == EnumAppSide.Client)
        {
            MergePublicMirror();
            entity.WatchedAttributes.RegisterModifiedListener(ProgressStore.AttrUnlocks, OnMirrorChanged);
            entity.WatchedAttributes.RegisterModifiedListener(ProgressStore.AttrScores, OnMirrorChanged);
            // Mid-upgrade hosts may still publish the legacy blob once.
            entity.WatchedAttributes.RegisterModifiedListener(ProgressStore.AttrTree, OnMirrorChanged);
            return;
        }

        TryLoadFromPlayer();
    }

    public override void AfterInitialized(bool onFirstSpawn)
    {
        base.AfterInitialized(onFirstSpawn);
        if (entity.World.Side != EnumAppSide.Server)
        {
            return;
        }

        // Engine just finished filling sided behaviors. Enroll from this, not GetBehavior.
        if (entity is EntityPlayer eplr && eplr.Player is IServerPlayer player)
        {
            ProsequorModSystem.For(entity.Api)?.AdmitInitialized(player, this);
        }
    }

    public override void OnEntityDespawn(EntityDespawnData despawn)
    {
        if (entity.World.Side == EnumAppSide.Client)
        {
            entity.WatchedAttributes.UnregisterListener(OnMirrorChanged);
        }
        else
        {
            FlushPendingWire(forceChannelSnapshot: false);
            FlushSave();
            if (entity is EntityPlayer eplr && !string.IsNullOrEmpty(eplr.PlayerUID))
            {
                ProsequorModSystem.For(entity.Api)?.ActivityWatch?.ForgetPlayer(eplr.PlayerUID);
            }
        }

        base.OnEntityDespawn(despawn);
    }

    void OnMirrorChanged()
    {
        Dictionary<(string SkillId, string NodeId), int>? beforeUnlocks =
            entity.World.Side == EnumAppSide.Client && publicMirrorReady
                ? SnapshotUnlockTiers(state)
                : null;
        Dictionary<string, int>? beforeScores =
            entity.World.Side == EnumAppSide.Client && publicMirrorReady
                ? SnapshotAttributeScores(state)
                : null;

        if (!MergePublicMirror())
        {
            return;
        }

        bool unlocksChanged = false;
        if (beforeUnlocks != null
            && entity is EntityPlayer eplr
            && eplr.Player != null)
        {
            foreach ((string skillId, string nodeId) in DiffUnlockTiers(beforeUnlocks, state))
            {
                unlocksChanged = true;
                CraftMutateOutputStation.TryRefreshOpenGrid(eplr.Player, skillId, nodeId);
            }
        }

        bool scoresChanged = beforeScores != null && DiffAttributeScores(beforeScores, state);
        if (unlocksChanged || scoresChanged || beforeUnlocks == null)
        {
            RebuildAbilityCache();
            BumpProgressRevision();
            if (scoresChanged || beforeScores == null)
            {
                NotifyAttributeEffectsApplied();
            }
        }

        Changed?.Invoke();
    }

    /// <summary>Merge public unlock tiers and attribute scores into the live state (in place).</summary>
    bool MergePublicMirror()
    {
        if (!ProgressStore.MergePublicFromEntity(entity, state))
        {
            return false;
        }

        publicMirrorReady = true;
        if (!IsOwningClient)
        {
            loaded = true;
        }

        return true;
    }

    static Dictionary<(string SkillId, string NodeId), int> SnapshotUnlockTiers(PlayerProgressState progress)
    {
        Dictionary<(string SkillId, string NodeId), int> snapshot = new();
        foreach (KeyValuePair<string, SkillProgressState> skill in progress.Skills)
        {
            foreach (KeyValuePair<string, int> tier in skill.Value.UnlockTiers)
            {
                if (tier.Value > 0)
                {
                    snapshot[(skill.Key, tier.Key)] = tier.Value;
                }
            }
        }

        return snapshot;
    }

    static Dictionary<string, int> SnapshotAttributeScores(PlayerProgressState progress)
    {
        Dictionary<string, int> snapshot = new(StringComparer.OrdinalIgnoreCase);
        foreach (string id in AttributeIds.All)
        {
            snapshot[id] = progress.GetAttribute(id);
        }

        return snapshot;
    }

    static bool DiffAttributeScores(Dictionary<string, int> before, PlayerProgressState after)
    {
        foreach (string id in AttributeIds.All)
        {
            before.TryGetValue(id, out int previous);
            if (previous != after.GetAttribute(id))
            {
                return true;
            }
        }

        return false;
    }

    static IEnumerable<(string SkillId, string NodeId)> DiffUnlockTiers(
        Dictionary<(string SkillId, string NodeId), int> before,
        PlayerProgressState after)
    {
        HashSet<(string SkillId, string NodeId)> seen = new();
        foreach (KeyValuePair<string, SkillProgressState> skill in after.Skills)
        {
            foreach (KeyValuePair<string, int> tier in skill.Value.UnlockTiers)
            {
                (string SkillId, string NodeId) key = (skill.Key, tier.Key);
                seen.Add(key);
                before.TryGetValue(key, out int previous);
                if (previous != tier.Value)
                {
                    yield return key;
                }
            }
        }

        foreach (KeyValuePair<(string SkillId, string NodeId), int> prior in before)
        {
            if (!seen.Contains(prior.Key))
            {
                yield return prior.Key;
            }
        }
    }

    public bool TryGetActiveRules(
        HookId hook,
        VerbId verb,
        PhaseId phase,
        out IReadOnlyList<AbilityRule> rules)
    {
        if (abilityCache == null)
        {
            rules = Array.Empty<AbilityRule>();
            return false;
        }

        if (abilityCache.TryGet(hook, verb, phase, out rules))
        {
            return true;
        }

        rules = Array.Empty<AbilityRule>();
        return true;
    }

    public void RebuildAbilityCachePublic() => RebuildAbilityCache();

    void RebuildAbilityCache()
    {
        AbilityRuleIndex? index = ProsequorModSystem.For(entity.Api)?.Pipeline?.RuleIndex;
        if (index == null)
        {
            abilityCache = null;
            return;
        }

        abilityCache = ActiveAbilityRuleCache.Rebuild(index, this);
    }

    public void BumpProgressRevision() => composeMemo.BumpProgressRevision();

    public void EnsureLoaded(IServerPlayer player, ISkillRegistry registry)
    {
        if (loaded)
        {
            PlayerProgressState.EnsureSkillEntries(state, registry);
            RefreshAllSkillCaps();
            ProgressStore.MirrorToEntity(entity, state);
            RebuildAbilityCache();
            NotifyAttributeEffectsApplied();
            SendOwnerSnapshot();
            return;
        }

        state = ProgressStore.Load(player, registry);
        loaded = true;
        dirty = false;
        RefreshAllSkillCaps();
        ProgressStore.MirrorToEntity(entity, state);
        RebuildAbilityCache();
        BumpProgressRevision();
        NotifyAttributeEffectsApplied();
        SendOwnerSnapshot();
    }

    void NotifyAttributeScoreChanged(string attributeId)
    {
        AttributeEffectService? service = ProsequorModSystem.For(entity.Api)?.AttributeEffects;
        service?.OnAttributeScoreChanged(entity, attributeId, this);
    }

    void NotifyAttributeEffectsApplied()
    {
        AttributeEffectService? service = ProsequorModSystem.For(entity.Api)?.AttributeEffects;
        service?.OnAllAttributesApplied(entity, this);
    }

    /// <summary>Server tick: drain saturation meters using calendar time.</summary>
    public void TickBuckets(double nowTotalHours)
    {
        if (entity.World.Side != EnumAppSide.Server || !loaded)
        {
            return;
        }

        float fillBefore = 0f;
        foreach (SkillProgressState skill in state.Skills.Values)
        {
            fillBefore += skill.Fill;
        }

        XpAwardService.ApplyDrain(state, nowTotalHours);

        float fillAfter = 0f;
        foreach (SkillProgressState skill in state.Skills.Values)
        {
            fillAfter += skill.Fill;
        }

        if (fillAfter < fillBefore - 0.0001f)
        {
            MarkPersistDirty();
        }
    }

    double NowTotalHours() => entity.World.Calendar?.TotalHours ?? 0.0;

    void TryLoadFromPlayer()
    {
        if (entity is not EntityPlayer eplr || eplr.Player is not IServerPlayer splr)
        {
            return;
        }

        ISkillRegistry? registry = ProsequorModSystem.For(entity.Api)?.Registry;
        if (registry == null)
        {
            return;
        }

        EnsureLoaded(splr, registry);
    }

    public void FlushSave()
    {
        if (entity.World.Side != EnumAppSide.Server || !dirty)
        {
            return;
        }

        if (entity is not EntityPlayer eplr || eplr.Player is not IServerPlayer splr)
        {
            return;
        }

        ProgressStore.WriteModData(splr, state);
        dirty = false;
    }

    /// <summary>Server-only: debounced ModData write.</summary>
    void MarkPersistDirty()
    {
        dirty = true;
    }

    void NotifyVisibleProgress(VisibleProgressChange change)
    {
        EnsureServer();
        pending.Accumulate(change);
        MarkPersistDirty();
        if (change.Immediate)
        {
            FlushPendingWire(forceChannelSnapshot: false);
        }

        Changed?.Invoke();
    }

    /// <summary>Coalesced flush of non-immediate XP/bucket dirty bits (~150ms tick).</summary>
    public void FlushPendingCoalesced()
    {
        if (entity.World.Side != EnumAppSide.Server || !pending.Any || pending.Immediate)
        {
            // Immediate was already flushed; if only Immediate leftover flags somehow remain, clear.
            if (pending.Immediate && pending.Any)
            {
                FlushPendingWire(forceChannelSnapshot: false);
            }

            return;
        }

        FlushPendingWire(forceChannelSnapshot: false);
    }

    void FlushPendingWire(bool forceChannelSnapshot)
    {
        if (entity.World.Side != EnumAppSide.Server || !pending.Any && !forceChannelSnapshot)
        {
            return;
        }

        if (entity is not EntityPlayer eplr || eplr.Player is not IServerPlayer splr)
        {
            pending.Clear();
            return;
        }

        if (ProgressChannelCodec.NeedsPublic(pending))
        {
            if (pending.Unlocks.Count > 1 || (pending.AttributeScores && pending.Unlocks.Count > 0))
            {
                ProgressStore.MirrorToEntity(entity, state);
            }
            else
            {
                ProgressStore.ApplyVisibleMirror(entity, state, pending.ToPublicChange());
            }
        }

        ProgressNetwork? network = ProsequorModSystem.For(entity.Api)?.Network;
        if (network != null)
        {
            if (forceChannelSnapshot)
            {
                syncSeq++;
                network.SendProgressSnapshot(splr, ProgressChannelCodec.BuildSnapshot(state, syncSeq));
            }
            else if (ProgressChannelCodec.NeedsChannel(pending))
            {
                syncSeq++;
                network.SendProgressDelta(
                    splr,
                    ProgressChannelCodec.BuildDelta(state, pending, syncSeq));
            }
        }

        pending.Clear();
    }

    /// <summary>Owner-channel full private snapshot (join / resync / clear).</summary>
    public void SendOwnerSnapshot()
    {
        if (entity.World.Side != EnumAppSide.Server)
        {
            return;
        }

        if (entity is not EntityPlayer eplr || eplr.Player is not IServerPlayer splr)
        {
            return;
        }

        ProgressNetwork? network = ProsequorModSystem.For(entity.Api)?.Network;
        if (network == null)
        {
            return;
        }

        syncSeq++;
        network.SendProgressSnapshot(splr, ProgressChannelCodec.BuildSnapshot(state, syncSeq));
    }

    /// <summary>Client: apply a private snapshot in place.</summary>
    public void ApplyOwnerSnapshot(ProgressSnapshotPacket packet)
    {
        if (entity.World.Side != EnumAppSide.Server)
        {
            // Accept any snapshot; reset private tracks then fill.
            state.PlayerLevel = packet.PlayerLevel;
            state.PlayerXp = packet.PlayerXp;
            state.UnlockPoints = packet.UnlockPoints;
            foreach (SkillProgressState skill in state.Skills.Values)
            {
                skill.Level = 0;
                skill.Xp = 0f;
            }

            foreach (ProgressSkillTrackDto skill in packet.Skills)
            {
                if (string.IsNullOrWhiteSpace(skill.SkillId))
                {
                    continue;
                }

                SkillProgressState row = state.GetOrCreateSkill(skill.SkillId);
                row.Level = skill.Level;
                row.Xp = skill.Xp;
            }

            PlayerProgressState.EnsureAttributeEntries(state);
            foreach (ProgressAttributeBucketDto bucket in packet.AttributeBuckets)
            {
                string? canonical = AttributeIds.Canonicalize(bucket.Id);
                if (canonical != null)
                {
                    state.AttributeBuckets[canonical] = bucket.Fill;
                }
            }

            clientLastSeq = packet.Seq;
            ownerChannelSynced = true;
            loaded = true;
            Changed?.Invoke();
        }
    }

    /// <summary>Client: apply a private delta in place; request resync on sequence gap.</summary>
    public void ApplyOwnerDelta(ProgressDeltaPacket packet)
    {
        if (entity.World.Side == EnumAppSide.Server)
        {
            return;
        }

        if (!ownerChannelSynced)
        {
            ProsequorModSystem.For(entity.Api)?.Network.RequestProgressResync(clientLastSeq);
            return;
        }

        if (packet.Seq != clientLastSeq + 1)
        {
            ProsequorModSystem.For(entity.Api)?.Network.RequestProgressResync(clientLastSeq);
            return;
        }

        if (packet.HasPlayerTrack)
        {
            state.PlayerLevel = packet.PlayerLevel;
            state.PlayerXp = packet.PlayerXp;
            state.UnlockPoints = packet.UnlockPoints;
        }

        foreach (ProgressSkillTrackDto skill in packet.Skills)
        {
            if (string.IsNullOrWhiteSpace(skill.SkillId))
            {
                continue;
            }

            SkillProgressState row = state.GetOrCreateSkill(skill.SkillId);
            row.Level = skill.Level;
            row.Xp = skill.Xp;
        }

        if (packet.HasAttributeBuckets)
        {
            PlayerProgressState.EnsureAttributeEntries(state);
            foreach (ProgressAttributeBucketDto bucket in packet.AttributeBuckets)
            {
                string? canonical = AttributeIds.Canonicalize(bucket.Id);
                if (canonical != null)
                {
                    state.AttributeBuckets[canonical] = bucket.Fill;
                }
            }
        }

        clientLastSeq = packet.Seq;
        Changed?.Invoke();
    }

    void SyncFullProgress()
    {
        if (entity is EntityPlayer eplr && eplr.Player is IServerPlayer splr)
        {
            pending.Clear();
            ProgressStore.MirrorToEntity(entity, state);
            ProgressStore.WriteModData(splr, state);
            dirty = false;
            SendOwnerSnapshot();
        }
        else
        {
            ProgressStore.MirrorToEntity(entity, state);
        }

        Changed?.Invoke();
    }

    public int GetSkillLevel(string skillId) =>
        state.Skills.TryGetValue(skillId, out SkillProgressState? s) ? s.Level : 0;

    public float GetSkillXp(string skillId) =>
        state.Skills.TryGetValue(skillId, out SkillProgressState? s) ? s.Xp : 0f;

    public IReadOnlyList<string> GetUnlocks(string skillId)
    {
        if (!state.Skills.TryGetValue(skillId, out SkillProgressState? s))
        {
            return Array.Empty<string>();
        }

        return s.UnlockTiers
            .Where(kv => kv.Value > 0)
            .Select(kv => kv.Key)
            .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public bool HasUnlock(string skillId, string code) => GetUnlockTier(skillId, code) > 0;

    public int GetUnlockTier(string skillId, string nodeId)
    {
        if (!state.Skills.TryGetValue(skillId, out SkillProgressState? s))
        {
            return 0;
        }

        return s.GetTier(nodeId);
    }

    public int GetAttribute(string id) => state.GetAttribute(id);

    public float GetAttributeBucket(string id) => state.GetAttributeBucket(id);

    public void GetPlayerBar(out float intoLevel, out int needForNext, out int level)
    {
        level = state.PlayerLevel;
        intoLevel = XpCurves.InLevelPlayerXp(state.PlayerXp, level);
        needForNext = XpCurves.XpToNextPlayerLevel(level);
        if (needForNext <= 0)
        {
            intoLevel = 1f;
            needForNext = 1;
        }
        else if (intoLevel > needForNext)
        {
            intoLevel = needForNext;
        }
    }

    public void GetSkillBar(string skillId, out float intoLevel, out int needForNext, out int level)
    {
        SkillProgressState skill = state.GetOrCreateSkill(skillId);
        level = skill.Level;
        XpCurves.SkillBar(skill.Xp, level, ResolveSkillMaxLevel(skillId), out intoLevel, out needForNext);
    }

    public void AddPlayerXp(float amount, XpAwardMode mode = XpAwardMode.Earn)
    {
        EnsureServer();
        if (amount <= 0f)
        {
            return;
        }

        // Player XP is unmetered; nb/fb are no-ops on this track.
        _ = mode;
        CommitPlayerXp(amount);
    }

    public void AddSkillXp(
        string skillId,
        float amount,
        AbilityAction? fact = null,
        XpAwardMode mode = XpAwardMode.Earn)
    {
        EnsureServer();
        if (amount <= 0f || string.IsNullOrWhiteSpace(skillId))
        {
            return;
        }

        SkillDef registered = RequireRegisteredSkill(skillId);
        skillId = registered.Id;
        SkillProgressState skill = state.GetOrCreateSkill(skillId);
        int max = Math.Min(XpCurves.SkillMaxLevel, registered.MaxLevel);

        if (skill.Level >= max)
        {
            return;
        }

        amount = ApplySkillXpModifiers(skillId, amount, fact);
        if (amount <= 0f)
        {
            return;
        }

        if (skill.CachedCap <= 0f)
        {
            RefreshSkillCap(skillId);
        }

        double nowHours = NowTotalHours();
        float toCommit = amount;
        if (mode == XpAwardMode.Earn)
        {
            toCommit = XpAwardService.AwardSkill(state, skillId, amount, nowHours);
            if (toCommit <= 0f)
            {
                // Accrued / fill / lastAccrual may have changed (server-only; debounced persist).
                MarkPersistDirty();
                return;
            }
        }
        else if (mode == XpAwardMode.GrantAndFill)
        {
            XpAwardService.FillSkillMeters(state, skillId, amount, nowHours);
        }

        CommitSkillXp(skillId, registered, toCommit);
    }

    /// <summary>Commits lifetime player XP (events / HUD). Used by Earn flush and Grant modes.</summary>
    void CommitPlayerXp(float amount)
    {
        float playerXpBefore = state.PlayerXp;
        int playerLevelBefore = state.PlayerLevel;
        float barFillBefore = LevelUpHudMath.PlayerBarFill(playerXpBefore, playerLevelBefore);
        if (!TryApplyPlayerXp(amount, out int beforeLevel, out float applied, out IReadOnlyList<string> attributeGains))
        {
            MarkPersistDirty();
            return;
        }

        bool leveled = state.PlayerLevel > beforeLevel;
        NotifyVisibleProgress(
            leveled || attributeGains.Count > 0
                ? new VisibleProgressChange
                {
                    MirrorPlayerTrack = true,
                    MirrorAttributeScores = attributeGains.Count > 0,
                    Immediate = true
                }
                : VisibleProgressChange.PlayerTrack(immediate: false));
        PublishExperienceGained(
            ProgressTrack.Player,
            null,
            applied,
            state.PlayerXp,
            state.PlayerLevel);
        PublishLevelUp(ProgressTrack.Player, null, beforeLevel, state.PlayerLevel);
        TrySendLevelUpHud(
            skillId: null,
            skillBefore: 0,
            skillAfter: 0,
            playerLevelBefore: beforeLevel,
            playerLevelAfter: state.PlayerLevel,
            playerBarFillBefore: barFillBefore,
            playerBarFillAfter: LevelUpHudMath.PlayerBarFill(state.PlayerXp, state.PlayerLevel),
            attributeGains: attributeGains);
    }

    float ApplySkillXpModifiers(string skillId, float amount, AbilityAction? fact)
    {
        ProsequorModSystem? mod = ProsequorModSystem.For(entity.Api);
        if (mod?.Pipeline == null)
        {
            return amount;
        }

        IServerPlayer? player = entity.World.PlayerByUid(
            (entity as EntityPlayer)?.PlayerUID ?? string.Empty) as IServerPlayer;

        SkillXpContext context = new()
        {
            Player = player,
            Progress = this,
            Fact = fact,
            SkillId = skillId,
            BaseAmount = amount
        };

        return mod.Pipeline.Run(HookIds.Progress, VerbIds.SkillXp, HookIds.Amount, context, amount);
    }

    float ResolveSkillCapMultiplier(string skillId)
    {
        ProsequorModSystem? mod = ProsequorModSystem.For(entity.Api);
        if (mod?.Pipeline == null)
        {
            return 1f;
        }

        IServerPlayer? player = entity.World.PlayerByUid(
            (entity as EntityPlayer)?.PlayerUID ?? string.Empty) as IServerPlayer;

        SkillBucketCapContext context = new()
        {
            Player = player,
            Progress = this,
            SkillId = skillId
        };

        return mod.Pipeline.Run(HookIds.Progress, VerbIds.SkillBucket, HookIds.Cap, context, 1f);
    }

    void RefreshSkillCap(string skillId)
    {
        SkillProgressState skill = state.GetOrCreateSkill(skillId);
        XpBucketFormulas.RefreshSkillCap(skill, ResolveSkillCapMultiplier(skillId));
    }

    void RefreshAllSkillCaps()
    {
        foreach (KeyValuePair<string, SkillProgressState> kv in state.Skills)
        {
            XpBucketFormulas.RefreshSkillCap(kv.Value, ResolveSkillCapMultiplier(kv.Key));
        }
    }

    /// <summary>Commits flushed skill XP; player XP is awarded only on skill level-up (sum of tier costs).</summary>
    void CommitSkillXp(string skillId, SkillDef registered, float amount)
    {
        SkillProgressState skill = state.GetOrCreateSkill(skillId);
        int max = Math.Min(XpCurves.SkillMaxLevel, registered.MaxLevel);

        if (skill.Level >= max || amount <= 0f)
        {
            return;
        }

        int skillBefore = skill.Level;
        float skillBeforeXp = skill.Xp;
        float playerXpBefore = state.PlayerXp;
        int playerLevelBeforeCommit = state.PlayerLevel;
        float playerBarFillBefore = LevelUpHudMath.PlayerBarFill(playerXpBefore, playerLevelBeforeCommit);
        skill.Xp += amount;
        skill.Level = Math.Min(max, XpCurves.SkillLevelFromLifetimeXp(skill.Xp));
        float skillCapFloor = XpCurves.LifetimeXpForSkillLevel(max);
        if (skill.Level >= max && skill.Xp > skillCapFloor)
        {
            skill.Xp = skillCapFloor;
        }

        float skillApplied = skill.Xp - skillBeforeXp;
        if (skillApplied <= 0f)
        {
            return;
        }

        bool skillLeveled = skill.Level > skillBefore;
        if (skillLeveled)
        {
            RefreshSkillCap(skillId);
            BumpProgressRevision();
            // Bucket fill must land before player XP so a concurrent stat-gain level sees it.
            AttributeGrowth.AddScores(
                state,
                registered.AttributeScores,
                skill.Level - skillBefore);
        }

        int playerBeforeLevel = state.PlayerLevel;
        float playerApplied = 0f;
        int milestonePoints = 0;
        IReadOnlyList<string> attributeGains = Array.Empty<string>();
        if (skillLeveled)
        {
            milestonePoints = UnlockPointPolicy.PointsForSkillLevelGain(skillBefore, skill.Level);
            if (milestonePoints > 0)
            {
                state.UnlockPoints += milestonePoints;
            }

            int playerXpAward = XpCurves.PlayerXpForSkillLevelGain(skillBefore, skill.Level);
            if (playerXpAward > 0)
            {
                TryApplyPlayerXp(playerXpAward, out _, out playerApplied, out attributeGains);
            }
        }

        NotifyVisibleProgress(
            playerApplied > 0f || milestonePoints > 0 || skillLeveled
                ? VisibleProgressChange.SkillAndPlayer(skillId)
                : VisibleProgressChange.SkillTrack(skillId));
        PublishExperienceGained(
            ProgressTrack.Skill,
            skillId,
            skillApplied,
            skill.Xp,
            skill.Level);
        PublishLevelUp(ProgressTrack.Skill, skillId, skillBefore, skill.Level);

        if (playerApplied > 0f)
        {
            PublishExperienceGained(
                ProgressTrack.Player,
                null,
                playerApplied,
                state.PlayerXp,
                state.PlayerLevel);
            PublishLevelUp(ProgressTrack.Player, null, playerBeforeLevel, state.PlayerLevel);
        }

        if (skillLeveled)
        {
            TrySendLevelUpHud(
                skillId,
                skillBefore,
                skill.Level,
                playerLevelBeforeCommit,
                state.PlayerLevel,
                playerBarFillBefore,
                LevelUpHudMath.PlayerBarFill(state.PlayerXp, state.PlayerLevel),
                attributeGains);
        }
    }

    /// <summary>Applies player XP without dirty/events. Returns false when nothing was added.</summary>
    bool TryApplyPlayerXp(
        float amount,
        out int beforeLevel,
        out float applied,
        out IReadOnlyList<string> attributeGains)
    {
        beforeLevel = state.PlayerLevel;
        applied = 0f;
        attributeGains = Array.Empty<string>();
        if (amount <= 0f || state.PlayerLevel >= XpCurves.PlayerMaxLevel)
        {
            return false;
        }

        float beforeXp = state.PlayerXp;
        float capFloor = XpCurves.LifetimeXpForPlayerLevel(XpCurves.PlayerMaxLevel);
        state.PlayerXp = Math.Min(capFloor, state.PlayerXp + amount);
        state.PlayerLevel = XpCurves.PlayerLevelFromLifetimeXp(state.PlayerXp);
        int gained = state.PlayerLevel - beforeLevel;
        if (gained > 0)
        {
            IReadOnlyList<LevelUpRuleDef> levelUpRules =
                ProsequorModSystem.For(entity.Api)?.LevelUps.Rules
                ?? Array.Empty<LevelUpRuleDef>();
            IReadOnlyList<string> winners = LevelUpRules.Apply(
                state,
                levelUpRules,
                beforeLevel,
                state.PlayerLevel,
                entity.World.Rand);
            attributeGains = winners;
            foreach (string winner in winners)
            {
                NotifyAttributeScoreChanged(winner);
            }

            if (winners.Count == 0)
            {
                BumpProgressRevision();
            }
        }

        applied = state.PlayerXp - beforeXp;
        return applied > 0f;
    }

    public void AddUnlockPoints(int amount)
    {
        EnsureServer();
        state.UnlockPoints = Math.Max(0, state.UnlockPoints + amount);
        NotifyVisibleProgress(VisibleProgressChange.PlayerTrack());
    }

    public void SetPlayerLevel(int level)
    {
        EnsureServer();
        level = Math.Clamp(level, XpCurves.PlayerMinLevel, XpCurves.PlayerMaxLevel);
        int before = state.PlayerLevel;
        float barFillBefore = LevelUpHudMath.PlayerBarFill(state.PlayerXp, before);
        state.PlayerLevel = level;
        state.PlayerXp = XpCurves.LifetimeXpForPlayerLevel(level);
        int gained = level - before;
        IReadOnlyList<string> attributeGains = Array.Empty<string>();
        if (gained > 0)
        {
            IReadOnlyList<LevelUpRuleDef> levelUpRules =
                ProsequorModSystem.For(entity.Api)?.LevelUps.Rules
                ?? Array.Empty<LevelUpRuleDef>();
            attributeGains = LevelUpRules.Apply(
                state,
                levelUpRules,
                before,
                state.PlayerLevel,
                entity.World.Rand);
            foreach (string winner in attributeGains)
            {
                NotifyAttributeScoreChanged(winner);
            }

            if (attributeGains.Count == 0)
            {
                BumpProgressRevision();
            }
        }

        NotifyVisibleProgress(
            gained > 0 || attributeGains.Count > 0
                ? new VisibleProgressChange
                {
                    MirrorPlayerTrack = true,
                    MirrorAttributeScores = attributeGains.Count > 0,
                    Immediate = true
                }
                : VisibleProgressChange.PlayerTrack());
        PublishLevelUp(ProgressTrack.Player, null, before, state.PlayerLevel);
        TrySendLevelUpHud(
            skillId: null,
            skillBefore: 0,
            skillAfter: 0,
            playerLevelBefore: before,
            playerLevelAfter: state.PlayerLevel,
            playerBarFillBefore: barFillBefore,
            playerBarFillAfter: LevelUpHudMath.PlayerBarFill(state.PlayerXp, state.PlayerLevel),
            attributeGains: attributeGains);
    }

    public void AddAttributeBucket(string id, float amount)
    {
        EnsureServer();
        string? canonical = AttributeIds.Canonicalize(id);
        if (canonical == null || amount == 0f)
        {
            return;
        }

        PlayerProgressState.EnsureAttributeEntries(state);
        state.AttributeBuckets[canonical] = Math.Max(0f, state.AttributeBuckets[canonical] + amount);
        NotifyVisibleProgress(VisibleProgressChange.AttributeBuckets());
    }

    public void SetAttribute(string id, int score)
    {
        EnsureServer();
        string? canonical = AttributeIds.Canonicalize(id);
        if (canonical == null)
        {
            return;
        }

        PlayerProgressState.EnsureAttributeEntries(state);
        score = Math.Clamp(score, 0, AttributeGrowth.MaxScore);
        if (state.Attributes[canonical] == score)
        {
            return;
        }

        state.Attributes[canonical] = score;
        NotifyVisibleProgress(VisibleProgressChange.AttributeScores());
        NotifyAttributeScoreChanged(canonical);
    }

    /// <summary>Zero saturation fill, pending accrued XP, and bucket drain clocks.</summary>
    public void EmptyBuckets()
    {
        EnsureServer();
        double now = NowTotalHours();
        foreach (SkillProgressState skill in state.Skills.Values)
        {
            skill.Fill = 0f;
            skill.Accrued = 0f;
            skill.LastAccrualTotalHours = now;
            skill.LastDrainTotalHours = now;
        }

        MarkPersistDirty();
    }

    /// <summary>Reset all progress to a fresh character (includes buckets).</summary>
    public void ClearProgress(ISkillRegistry registry)
    {
        EnsureServer();
        state = PlayerProgressState.CreateNew(registry);
        XpBucketFormulas.RefreshAllCaps(state);
        loaded = true;
        SyncFullProgress();
        RebuildAbilityCache();
        BumpProgressRevision();
        NotifyAttributeEffectsApplied();
    }

    public void SetSkillLevel(string skillId, int level)
    {
        EnsureServer();
        SkillDef registered = RequireRegisteredSkill(skillId);
        skillId = registered.Id;
        int max = Math.Min(XpCurves.SkillMaxLevel, registered.MaxLevel);

        level = Math.Clamp(level, XpCurves.SkillMinLevel, max);
        SkillProgressState skill = state.GetOrCreateSkill(skillId);
        int before = skill.Level;
        float playerBarFill = LevelUpHudMath.PlayerBarFill(state.PlayerXp, state.PlayerLevel);
        skill.Level = level;
        skill.Xp = XpCurves.LifetimeXpForSkillLevel(level);
        RefreshSkillCap(skillId);
        int milestonePoints = 0;
        if (level > before)
        {
            milestonePoints = UnlockPointPolicy.PointsForSkillLevelGain(before, level);
            if (milestonePoints > 0)
            {
                state.UnlockPoints += milestonePoints;
            }
        }

        if (before != level)
        {
            BumpProgressRevision();
        }

        NotifyVisibleProgress(
            before != level || milestonePoints > 0
                ? new VisibleProgressChange
                {
                    SkillId = skillId,
                    MirrorSkillTrack = true,
                    MirrorPlayerTrack = milestonePoints > 0,
                    Immediate = true
                }
                : VisibleProgressChange.SkillTrack(skillId));
        PublishLevelUp(ProgressTrack.Skill, skillId, before, skill.Level);
        TrySendLevelUpHud(
            skillId,
            before,
            skill.Level,
            state.PlayerLevel,
            state.PlayerLevel,
            playerBarFill,
            playerBarFill);
    }

    public bool GrantUnlock(string skillId, string code, int cost = 1)
    {
        EnsureServer();
        if (string.IsNullOrWhiteSpace(code))
        {
            return false;
        }

        SkillDef registered = RequireRegisteredSkill(skillId);
        skillId = registered.Id;
        int tier = GetUnlockTier(skillId, code) + 1;

        // When a tree is declared, only known nodes may be granted and eligibility applies.
        if (registered.Tree != null)
        {
            UnlockPurchaseStatus status = SkillTreeEligibility.Evaluate(
                registered,
                this,
                ProsequorModSystem.For(entity.Api)?.Registry,
                ProsequorModSystem.For(entity.Api)?.LevelUps.Rules,
                code,
                out SkillTreeNodeDef? node,
                out tier,
                requirePoints: false);
            if (status != UnlockPurchaseStatus.Ok || node == null)
            {
                return false;
            }

            cost = node.TierAt(tier).Cost;
        }
        else if (tier > 1)
        {
            // Codes outside a declared tree stay single-tier.
            return false;
        }

        if (!registered.IsHobby && cost > 0 && state.UnlockPoints < cost)
        {
            return false;
        }

        SkillProgressState skill = state.GetOrCreateSkill(skillId);
        skill.SetTier(code, tier);
        if (!registered.IsHobby && cost > 0)
        {
            state.UnlockPoints -= cost;
        }

        RefreshSkillCap(skillId);
        NotifyVisibleProgress(VisibleProgressChange.Unlock(skillId, code));
        RebuildAbilityCache();
        BumpProgressRevision();
        RefreshOpenCraftGrid(skillId, code);
        return true;
    }

    public UnlockPurchaseStatus TryPurchaseNode(string skillId, string nodeId)
    {
        EnsureServer();
        if (string.IsNullOrWhiteSpace(nodeId))
        {
            return UnlockPurchaseStatus.UnknownNode;
        }

        SkillDef registered;
        try
        {
            registered = RequireRegisteredSkill(skillId);
        }
        catch (ArgumentException)
        {
            return UnlockPurchaseStatus.UnknownSkill;
        }

        UnlockPurchaseStatus status = SkillTreeEligibility.Evaluate(
            registered,
            this,
            ProsequorModSystem.For(entity.Api)?.Registry,
            ProsequorModSystem.For(entity.Api)?.LevelUps.Rules,
            nodeId,
            out SkillTreeNodeDef? node,
            out int tier);
        if (status != UnlockPurchaseStatus.Ok || node == null)
        {
            return status;
        }

        SkillProgressState skill = state.GetOrCreateSkill(registered.Id);
        skill.SetTier(node.Id, tier);
        int cost = node.TierAt(tier).Cost;
        if (!registered.IsHobby && cost > 0)
        {
            state.UnlockPoints -= cost;
        }

        RefreshSkillCap(registered.Id);
        NotifyVisibleProgress(VisibleProgressChange.Unlock(registered.Id, node.Id));
        RebuildAbilityCache();
        BumpProgressRevision();
        RefreshOpenCraftGrid(registered.Id, node.Id);
        return UnlockPurchaseStatus.Ok;
    }

    public bool RevokeUnlock(string skillId, string code, int refund = 1)
    {
        EnsureServer();
        SkillDef registered = RequireRegisteredSkill(skillId);
        skillId = registered.Id;
        if (!state.Skills.TryGetValue(skillId, out SkillProgressState? skill))
        {
            return false;
        }

        int tier = skill.GetTier(code);
        if (tier <= 0)
        {
            return false;
        }

        skill.SetTier(code, tier - 1);
        if (!registered.IsHobby && refund > 0)
        {
            state.UnlockPoints += refund;
        }

        RefreshSkillCap(skillId);
        NotifyVisibleProgress(VisibleProgressChange.Unlock(skillId, code));
        RebuildAbilityCache();
        BumpProgressRevision();
        RefreshOpenCraftGrid(skillId, code);
        return true;
    }

    void RefreshOpenCraftGrid(string skillId, string nodeId)
    {
        if (entity is EntityPlayer eplr && eplr.Player != null)
        {
            CraftMutateOutputStation.TryRefreshOpenGrid(eplr.Player, skillId, nodeId);
        }
    }

    SkillDef RequireRegisteredSkill(string skillId)
    {
        if (ProsequorModSystem.For(entity.Api)?.Registry.TryGet(skillId, out SkillDef? def) == true && def != null)
        {
            return def;
        }

        throw new ArgumentException($"Unknown Prosequor skill '{skillId}'.", nameof(skillId));
    }

    int ResolveSkillMaxLevel(string skillId)
    {
        if (ProsequorModSystem.For(entity.Api)?.Registry.TryGet(skillId, out SkillDef? def) == true
            && def != null
            && def.MaxLevel > 0)
        {
            return Math.Min(XpCurves.SkillMaxLevel, def.MaxLevel);
        }

        return XpCurves.SkillMaxLevel;
    }

    void PublishExperienceGained(
        ProgressTrack track,
        string? skillId,
        float amount,
        float totalXp,
        int level)
    {
        if (amount <= 0f
            || entity is not EntityPlayer eplr
            || eplr.Player is not IServerPlayer player
            || ProsequorModSystem.For(entity.Api) is not ProsequorModSystem mod)
        {
            return;
        }

        mod.ProgressEvents.Publish(new ExperienceGainedEvent(
            player,
            track,
            skillId,
            amount,
            totalXp,
            level));
    }

    void PublishLevelUp(
        ProgressTrack track,
        string? skillId,
        int previousLevel,
        int newLevel)
    {
        if (newLevel <= previousLevel
            || entity is not EntityPlayer eplr
            || eplr.Player is not IServerPlayer player
            || ProsequorModSystem.For(entity.Api) is not ProsequorModSystem mod)
        {
            return;
        }

        mod.PublishLevelUp(new LevelUpEvent
        {
            Player = player,
            Track = track,
            SkillId = skillId,
            PreviousLevel = previousLevel,
            NewLevel = newLevel
        });
    }

    void TrySendLevelUpHud(
        string? skillId,
        int skillBefore,
        int skillAfter,
        int playerLevelBefore,
        int playerLevelAfter,
        float playerBarFillBefore,
        float playerBarFillAfter,
        IReadOnlyList<string>? attributeGains = null)
    {
        bool skillLeveled = !string.IsNullOrWhiteSpace(skillId) && skillAfter > skillBefore;
        bool playerLeveled = playerLevelAfter > playerLevelBefore;
        if (!skillLeveled && !playerLeveled)
        {
            return;
        }

        if (entity is not EntityPlayer eplr
            || eplr.Player is not IServerPlayer player
            || ProsequorModSystem.For(entity.Api) is not ProsequorModSystem mod)
        {
            return;
        }

        List<string> gains = new();
        if (attributeGains != null)
        {
            foreach (string id in attributeGains)
            {
                if (!string.IsNullOrWhiteSpace(id))
                {
                    gains.Add(id);
                }
            }
        }

        mod.Network.SendLevelUpHud(player, new LevelUpHudPacket
        {
            SkillId = skillLeveled ? skillId!.Trim() : "",
            SkillLevelBefore = skillBefore,
            SkillLevelAfter = skillAfter,
            PlayerLevelBefore = playerLevelBefore,
            PlayerLevelAfter = playerLevelAfter,
            PlayerBarFillBefore = playerBarFillBefore,
            PlayerBarFillAfter = playerBarFillAfter,
            PlayerLeveledUp = playerLeveled,
            AttributeGains = gains
        });
    }

    void EnsureServer()
    {
        if (entity.World.Side != EnumAppSide.Server)
        {
            throw new InvalidOperationException("Progress mutations are server-only.");
        }

        if (!loaded)
        {
            TryLoadFromPlayer();
        }
    }
}
