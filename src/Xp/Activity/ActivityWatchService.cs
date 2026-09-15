using System.Collections.Concurrent;
using Prosequor.Ability;
using Prosequor.Ability.Hooks;
using Prosequor.Player;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace Prosequor.Xp.Activity;

/// <summary>
/// Sole server ticker for admitted players. Effort runs every visit; XP drain and ModData
/// flush ride the same slice clock (see <see cref="PlayerWorkBuckets.ShouldDrain"/>).
/// Membership is the proof the entity finished <c>AfterInitialized</c> — do not enroll earlier.
/// </summary>
public sealed class ActivityWatchService
{
    const double MountTravelSq = 0.04;

    readonly ICoreServerAPI sapi;
    readonly IActivityWrapperRegistry wrappers;
    readonly IXpRuleRegistry rules;
    readonly Dictionary<string, double> lastSampleTotalHours = new(StringComparer.Ordinal);
    readonly ConcurrentDictionary<string, Vec3d> lastMotionPosByPlayer = new(StringComparer.Ordinal);
    readonly PlayerWorkBuckets buckets = new();
    readonly List<IServerPlayer> resolvedSlice = new();
    readonly CollectXpBuffer collectXp = new();

    public ActivityWatchService(
        ICoreServerAPI sapi,
        IActivityWrapperRegistry wrappers,
        IXpRuleRegistry rules)
    {
        this.sapi = sapi;
        this.wrappers = wrappers;
        this.rules = rules;
    }

    /// <summary>Pending collect-XP bag (pickup enqueue → bucket flush).</summary>
    public CollectXpBuffer CollectXp => collectXp;

    public void RememberPlayer(string playerUid) => buckets.Add(playerUid);

    /// <summary>Generation of the slice <see cref="ResolveCurrent"/> just returned. Read before <see cref="Advance"/>.</summary>
    public int CurrentGeneration => buckets.CurrentGeneration;

    /// <summary>
    /// Resolves the current work-bucket UIDs to online players. Stale UIDs are dropped from the roster.
    /// </summary>
    public IReadOnlyList<IServerPlayer> ResolveCurrent()
    {
        resolvedSlice.Clear();
        string[] uids = buckets.Current().ToArray();
        foreach (string uid in uids)
        {
            if (sapi.World.PlayerByUid(uid) is IServerPlayer player)
            {
                resolvedSlice.Add(player);
                continue;
            }

            buckets.Remove(uid);
            ForgetPlayerState(uid);
        }

        return resolvedSlice;
    }

    public void Advance() => buckets.Advance();

    /// <summary>
    /// Drain and staggered ModData flush for the slice just visited.
    /// <paramref name="generation"/> is that visit's counter (before <see cref="Advance"/>).
    /// </summary>
    public void TickMaintenance(IReadOnlyList<IServerPlayer> slice, int generation)
    {
        if (slice.Count == 0)
        {
            return;
        }

        bool drain = PlayerWorkBuckets.ShouldDrain(generation);
        double nowHours = drain ? sapi.World.Calendar?.TotalHours ?? 0.0 : 0.0;
        for (int i = 0; i < slice.Count; i++)
        {
            if (slice[i].Entity is not EntityPlayer entity)
            {
                continue;
            }

            EntityBehaviorProgress? progress = entity.GetBehavior<EntityBehaviorProgress>();
            if (progress == null)
            {
                continue;
            }

            if (drain)
            {
                progress.TickBuckets(nowHours);
            }

            if (PlayerWorkBuckets.ShouldFlush(generation, i))
            {
                progress.FlushSave();
            }
        }
    }

    public void Tick(IReadOnlyList<IServerPlayer> slice)
    {
        if (slice.Count == 0)
        {
            return;
        }

        double nowHours = sapi.World.Calendar?.TotalHours ?? 0.0;
        List<ActivityFact> facts = new();
        CollectionIndex collections =
            ProsequorModSystem.For(sapi)?.Collections?.Index ?? new CollectionIndex();

        foreach (IServerPlayer player in slice)
        {
            if (player.Entity is not EntityPlayer entity || entity.Alive == false)
            {
                continue;
            }

            if (player.WorldData?.CurrentGameMode == EnumGameMode.Spectator)
            {
                continue;
            }

            // Collect-XP flush is independent of effort dt (discrete amount deeds).
            collectXp.Flush(sapi, player.PlayerUID);

            float dt = ComputeDtGameSeconds(player.PlayerUID, nowHours);
            if (dt <= 0f)
            {
                continue;
            }

            ActivityCollectContext context = BuildContext(player, entity, dt, nowHours);

            // Registered polls (fishing/mount/mods) → Effort.Emit; natural Emit sites skip this.
            Effort.RunPolls(player, entity);

            bool moving = UpdateMoving(player.PlayerUID, entity);

            facts.Clear();
            MaterializeEffortFacts(context, moving, facts);

            // Legacy wrappers (deprecated; prefer Effort.Emit / RegisterPoll).
            foreach (IActivityWrapper wrapper in wrappers.All)
            {
                wrapper.Collect(context, facts);
            }

            if (facts.Count == 0)
            {
                continue;
            }

            EntityBehaviorProgress? progress = entity.GetBehavior<EntityBehaviorProgress>();
            if (progress == null)
            {
                continue;
            }

            AwardFacts(progress, player, facts, dt, collections);
        }
    }

    bool UpdateMoving(string playerUid, EntityAgent entity)
    {
        Vec3d now;
        Entity? mount = entity.MountedOn != null
            ? EffortMountEmitter.ResolveMountEntity(entity)
            : null;
        if (mount != null)
        {
            now = mount.Pos.XYZ;
        }
        else
        {
            now = entity.Pos.XYZ;
            lastMotionPosByPlayer.TryRemove(playerUid, out _);
            // Foot locomotion is not an effort stamp; moving only attaches to mount stamps.
            return false;
        }

        bool moved = false;
        if (lastMotionPosByPlayer.TryGetValue(playerUid, out Vec3d? previous))
        {
            double dx = now.X - previous.X;
            double dy = now.Y - previous.Y;
            double dz = now.Z - previous.Z;
            moved = dx * dx + dy * dy + dz * dz >= MountTravelSq;
        }

        lastMotionPosByPlayer[playerUid] = now.Clone();
        return moved;
    }

    static void MaterializeEffortFacts(
        ActivityCollectContext context,
        bool moving,
        List<ActivityFact> into)
    {
        IReadOnlyList<EffortStamp> stamps = Effort.Store.GetFresh(
            context.Player.PlayerUID,
            context.NowTotalHours);
        foreach (EffortStamp stamp in stamps)
        {
            HashSet<string> tokens = new(stamp.Tokens, StringComparer.OrdinalIgnoreCase);
            bool mountRelated = tokens.Contains(EffortTokenTags.Mounted)
                || tokens.Contains(EffortTokenTags.Riding)
                || tokens.Contains(EffortTokenTags.Boating);
            if (moving && mountRelated)
            {
                tokens.Add(EffortTokenTags.Moving);
            }

            into.Add(new ActivityFact
            {
                Activity = Effort.Activity,
                Caller = context.Held,
                LastCraft = context.LastCraft,
                Target = stamp.Target,
                Mount = stamp.Mount,
                Ground = stamp.Ground,
                Tokens = tokens
            });
        }
    }

    float ComputeDtGameSeconds(string playerUid, double nowHours)
    {
        if (!lastSampleTotalHours.TryGetValue(playerUid, out double last))
        {
            lastSampleTotalHours[playerUid] = nowHours;
            return 0f;
        }

        lastSampleTotalHours[playerUid] = nowHours;
        if (nowHours <= last)
        {
            return 0f;
        }

        double seconds = (nowHours - last) * XpBucketFormulas.GameSecondsPerHour;
        return (float)Math.Min(seconds, 30.0);
    }

    public static ActivityCollectContext BuildContext(
        IServerPlayer player,
        EntityAgent entity,
        float dtGameSeconds,
        double nowTotalHours)
    {
        return new ActivityCollectContext
        {
            Player = player,
            Entity = entity,
            Held = EventFactBuilder.HeldCode(player),
            LastCraft = EventFactBuilder.LastCraftCode(player),
            DtGameSeconds = dtGameSeconds,
            NowTotalHours = nowTotalHours
        };
    }

    void AwardFacts(
        EntityBehaviorProgress progress,
        IServerPlayer player,
        List<ActivityFact> facts,
        float dt,
        CollectionIndex collections)
    {
        Dictionary<string, (XpRule Rule, long Score, float Raw, ActivityFact Fact)> bestBySkill =
            new(StringComparer.OrdinalIgnoreCase);

        foreach (ActivityFact fact in facts)
        {
            XpMatchFact match = XpMatchFact.FromActivity(fact);
            IReadOnlyList<XpRule> candidates = rules.ByActivityRate(fact.Activity);
            if (candidates.Count == 0)
            {
                continue;
            }

            foreach (IGrouping<string, XpRule> skillGroup in candidates.GroupBy(
                         r => r.SkillId,
                         StringComparer.OrdinalIgnoreCase))
            {
                XpRule? winner = XpRuleMatcher.PickWinner(skillGroup, match, collections);
                if (winner == null)
                {
                    continue;
                }

                float raw = winner.Rate * dt;
                if (raw <= 0f)
                {
                    continue;
                }

                long winnerScore = winner.MatchScore;
                if (!bestBySkill.TryGetValue(
                        winner.SkillId,
                        out (XpRule Rule, long Score, float Raw, ActivityFact Fact) existing)
                    || winnerScore > existing.Score
                    || (winnerScore == existing.Score && raw > existing.Raw))
                {
                    bestBySkill[winner.SkillId] = (winner, winnerScore, raw, fact);
                }
            }
        }

        foreach (KeyValuePair<string, (XpRule Rule, long Score, float Raw, ActivityFact Fact)> kv in bestBySkill)
        {
            ActivityFact fact = kv.Value.Fact;
            AbilityAction action = new()
            {
                Verb = fact.Activity,
                ActorUid = player.PlayerUID,
                Held = fact.Held,
                Target = fact.Target,
                LastCraft = fact.LastCraft,
                Ground = fact.Ground,
                Mount = fact.Mount,
                Tokens = fact.Tokens
            };
            progress.AddSkillXp(kv.Key, kv.Value.Raw, action);
        }
    }

    public void ForgetPlayer(string playerUid)
    {
        buckets.Remove(playerUid);
        ForgetPlayerState(playerUid);
    }

    void ForgetPlayerState(string playerUid)
    {
        lastSampleTotalHours.Remove(playerUid);
        lastMotionPosByPlayer.TryRemove(playerUid, out _);
        Effort.Forget(playerUid);
        collectXp.Discard(playerUid);
    }
}
