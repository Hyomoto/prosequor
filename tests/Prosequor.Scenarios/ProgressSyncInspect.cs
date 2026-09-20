using System.IO;
using Prosequor.Data;
using Prosequor.Player;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.Server;
using Xunit;

namespace Prosequor.Scenarios;

/// <summary>
/// Live / WatchedAttributes mirror / ModData views of one player's progress, plus the
/// byte sizes that stand in for today's full-tree packet and persist blob.
/// Atlas has no client process; <see cref="Mirrored"/> is what
/// <see cref="EntityBehaviorProgress"/> hydrates on a real client.
/// </summary>
sealed class ProgressSyncInspect
{
    public const string Farming = "farming";
    public const string Mining = "mining";

    public required PlayerProgressState Live { get; init; }
    public required PlayerProgressState Mirrored { get; init; }
    public PlayerProgressState? Stored { get; init; }
    public required int MirrorTreeBytes { get; init; }
    public required int ModDataBytes { get; init; }
    public required int SkillKeys { get; init; }
    public required int UnlockKeys { get; init; }
    public required int RegisteredSkillCount { get; init; }
    public required bool TreeHasSkillMeters { get; init; }

    public static ProgressSyncInspect Capture(
        IPlayer player,
        ISkillRegistry registry)
    {
        EntityBehaviorProgress live = RequireLive(player);
        Entity entity = player.Entity;
        Assert.NotNull(entity);

        PlayerProgressState? mirrored = ProgressStore.ReadFromEntity(entity);
        Assert.NotNull(mirrored);

        ITreeAttribute? tree = entity.WatchedAttributes.GetTreeAttribute(ProgressStore.AttrTree);
        Assert.NotNull(tree);

        byte[]? modBytes = ProgressStore.ReadModData(player);
        PlayerProgressState? stored = null;
        if (modBytes is { Length: > 0 })
        {
            Assert.True(ProgressStore.TryHydrateStored(modBytes, registry, out stored));
        }

        return new ProgressSyncInspect
        {
            Live = live.State,
            Mirrored = mirrored!,
            Stored = stored,
            MirrorTreeBytes = TreeBytes(tree!),
            ModDataBytes = modBytes?.Length ?? 0,
            SkillKeys = CountSkillKeys(tree!),
            UnlockKeys = CountUnlockKeys(tree!),
            RegisteredSkillCount = registry.All.Count,
            TreeHasSkillMeters = TreeHasMeterFields(tree!)
        };
    }

    public static EntityBehaviorProgress RequireLive(IPlayer player)
    {
        EntityBehaviorProgress? progress = player.Entity?.GetBehavior<EntityBehaviorProgress>();
        Assert.NotNull(progress);
        return progress!;
    }

    public static ISkillRegistry RequireRegistry(ICoreAPI api)
    {
        ProsequorModSystem? mod = ProsequorModSystem.For(api);
        Assert.NotNull(mod);
        return mod!.Registry;
    }

    public static IServerPlayer RequireServerPlayer(IPlayer player)
    {
        Assert.True(player is IServerPlayer, $"Expected IServerPlayer, got {player.GetType().Name}");
        return (IServerPlayer)player;
    }

    /// <summary>
    /// Fields a real client reads from the entity tree: player track, skill XP/levels/unlocks,
    /// attribute scores and growth buckets. Skill saturation meters are server-only.
    /// </summary>
    public void AssertVisibleParity(string because)
    {
        AssertVisibleTracks(Live, Mirrored, because + " (mirror)");
        if (Stored != null)
        {
            AssertVisibleTracks(Live, Stored, because + " (moddata)");
        }
    }

    public void AssertStoredIncludesSkillMeters(string skillId, string because)
    {
        Assert.NotNull(Stored);
        SkillProgressState liveSkill = Live.GetOrCreateSkill(skillId);
        SkillProgressState storedSkill = Stored!.GetOrCreateSkill(skillId);
        Assert.True(
            NearlyEqual(storedSkill.Fill, liveSkill.Fill)
            && NearlyEqual(storedSkill.Accrued, liveSkill.Accrued),
            $"{because}: stored fill={storedSkill.Fill} accrued={storedSkill.Accrued} live fill={liveSkill.Fill} accrued={liveSkill.Accrued}.");
    }

    public static void AssertVisibleTracks(
        PlayerProgressState expected,
        PlayerProgressState actual,
        string because)
    {
        Assert.True(
            expected.PlayerLevel == actual.PlayerLevel
            && NearlyEqual(expected.PlayerXp, actual.PlayerXp)
            && expected.UnlockPoints == actual.UnlockPoints,
            $"{because}: player track live lv={expected.PlayerLevel} xp={expected.PlayerXp} pts={expected.UnlockPoints} vs actual lv={actual.PlayerLevel} xp={actual.PlayerXp} pts={actual.UnlockPoints}.");

        foreach (string id in AttributeIds.All)
        {
            Assert.True(
                expected.GetAttribute(id) == actual.GetAttribute(id)
                && NearlyEqual(expected.GetAttributeBucket(id), actual.GetAttributeBucket(id)),
                $"{because}: attribute {id} live={expected.GetAttribute(id)}/{expected.GetAttributeBucket(id)} actual={actual.GetAttribute(id)}/{actual.GetAttributeBucket(id)}.");
        }

        HashSet<string> ids = new(expected.Skills.Keys, StringComparer.OrdinalIgnoreCase);
        ids.UnionWith(actual.Skills.Keys);
        foreach (string skillId in ids)
        {
            SkillProgressState expectedSkill = expected.GetOrCreateSkill(skillId);
            SkillProgressState actualSkill = actual.GetOrCreateSkill(skillId);
            Assert.True(
                expectedSkill.Level == actualSkill.Level
                && NearlyEqual(expectedSkill.Xp, actualSkill.Xp),
                $"{because}: {skillId} live lv={expectedSkill.Level} xp={expectedSkill.Xp} actual lv={actualSkill.Level} xp={actualSkill.Xp}.");

            HashSet<string> nodes = new(expectedSkill.UnlockTiers.Keys, StringComparer.OrdinalIgnoreCase);
            nodes.UnionWith(actualSkill.UnlockTiers.Keys);
            foreach (string nodeId in nodes)
            {
                Assert.True(
                    expectedSkill.GetTier(nodeId) == actualSkill.GetTier(nodeId),
                    $"{because}: {skillId}/{nodeId} live tier={expectedSkill.GetTier(nodeId)} actual={actualSkill.GetTier(nodeId)}.");
            }
        }
    }

    public static int TreeBytes(ITreeAttribute tree)
    {
        using MemoryStream stream = new();
        using BinaryWriter writer = new(stream);
        tree.ToBytes(writer);
        writer.Flush();
        return (int)stream.Length;
    }

    static int CountSkillKeys(ITreeAttribute root)
    {
        ITreeAttribute? skills = root.GetTreeAttribute("skills");
        return skills == null ? 0 : CountEntries(skills);
    }

    static int CountUnlockKeys(ITreeAttribute root)
    {
        ITreeAttribute? skills = root.GetTreeAttribute("skills");
        if (skills == null)
        {
            return 0;
        }

        int total = 0;
        foreach (KeyValuePair<string, IAttribute> skill in skills)
        {
            if (skill.Value is not ITreeAttribute skillTree)
            {
                continue;
            }

            ITreeAttribute? tiers = skillTree.GetTreeAttribute("unlockTiers");
            if (tiers != null)
            {
                total += CountEntries(tiers);
            }
        }

        return total;
    }

    static bool TreeHasMeterFields(ITreeAttribute root)
    {
        ITreeAttribute? skills = root.GetTreeAttribute("skills");
        if (skills == null)
        {
            return false;
        }

        foreach (KeyValuePair<string, IAttribute> skill in skills)
        {
            if (skill.Value is not ITreeAttribute skillTree)
            {
                continue;
            }

            if (skillTree.HasAttribute("fill")
                || skillTree.HasAttribute("accrued")
                || skillTree.HasAttribute("cachedCap")
                || skillTree.HasAttribute("lastAccrualTotalHours")
                || skillTree.HasAttribute("lastDrainTotalHours"))
            {
                return true;
            }
        }

        return false;
    }

    static int CountEntries(ITreeAttribute tree)
    {
        int count = 0;
        foreach (KeyValuePair<string, IAttribute> _ in tree)
        {
            count++;
        }

        return count;
    }

    static bool NearlyEqual(float a, float b) => Math.Abs(a - b) <= 0.001f;
}
