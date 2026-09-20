using Prosequor.Data;
using Prosequor.Network;
using Prosequor.Player;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Server;
using Xunit;

namespace Prosequor.Scenarios;

/// <summary>
/// Live / public WatchedAttributes / ModData views of one player's progress, plus the
/// byte sizes that stand in for public wire cost. Private XP rides the owner channel.
/// </summary>
sealed class ProgressSyncInspect
{
    public const string Farming = "farming";
    public const string Mining = "mining";

    public required PlayerProgressState Live { get; init; }
    public required PlayerProgressState PublicMirror { get; init; }
    public PlayerProgressState? Stored { get; init; }
    public required int PublicTreeBytes { get; init; }
    public required int ModDataBytes { get; init; }
    public required int UnlockSkillKeys { get; init; }
    public required int UnlockKeys { get; init; }
    public required int RegisteredSkillCount { get; init; }
    public required bool HasLegacyTree { get; init; }
    public required bool HasScoreTree { get; init; }

    public static ProgressSyncInspect Capture(IPlayer player, ISkillRegistry registry)
    {
        EntityBehaviorProgress live = RequireLive(player);
        Entity entity = player.Entity;
        Assert.NotNull(entity);

        PlayerProgressState publicMirror = new() { Schema = PlayerProgressState.CurrentSchema };
        ProgressStore.MergePublicFromEntity(entity, publicMirror);

        byte[]? modBytes = ProgressStore.ReadModData(player);
        PlayerProgressState? stored = null;
        if (modBytes is { Length: > 0 })
        {
            Assert.True(ProgressStore.TryHydrateStored(modBytes, registry, out stored));
        }

        return new ProgressSyncInspect
        {
            Live = live.State,
            PublicMirror = publicMirror,
            Stored = stored,
            PublicTreeBytes = ProgressStore.PublicTreeBytes(entity),
            ModDataBytes = modBytes?.Length ?? 0,
            UnlockSkillKeys = ProgressStore.CountUnlockSkillKeys(entity),
            UnlockKeys = ProgressStore.CountUnlockNodeKeys(entity),
            RegisteredSkillCount = registry.All.Count,
            HasLegacyTree = ProgressStore.HasLegacyTree(entity),
            HasScoreTree = entity.WatchedAttributes.HasAttribute(ProgressStore.AttrScores)
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

    /// <summary>Public WA carries unlocks + attribute scores only.</summary>
    public void AssertPublicParity(string because)
    {
        AssertPublicFields(Live, PublicMirror, because + " (public WA)");
    }

    public void AssertStoredParity(string because)
    {
        Assert.NotNull(Stored);
        AssertFullParity(Live, Stored!, because + " (moddata)");
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

    public static void AssertPublicFields(
        PlayerProgressState expected,
        PlayerProgressState actual,
        string because)
    {
        foreach (string id in AttributeIds.All)
        {
            Assert.True(
                expected.GetAttribute(id) == actual.GetAttribute(id),
                $"{because}: attribute score {id} live={expected.GetAttribute(id)} actual={actual.GetAttribute(id)}.");
        }

        HashSet<string> ids = new(expected.Skills.Keys, StringComparer.OrdinalIgnoreCase);
        ids.UnionWith(actual.Skills.Keys);
        foreach (string skillId in ids)
        {
            SkillProgressState expectedSkill = expected.GetOrCreateSkill(skillId);
            SkillProgressState actualSkill = actual.GetOrCreateSkill(skillId);
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

    public static void AssertFullParity(
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

    static bool NearlyEqual(float a, float b) => Math.Abs(a - b) <= 0.001f;
}
