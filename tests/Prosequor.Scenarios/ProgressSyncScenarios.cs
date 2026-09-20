using Atlas.Api;
using Atlas.XUnit;
using Prosequor.Data;
using Prosequor.Player;
using Prosequor.Xp;
using Vintagestory.API.Common;
using Xunit;

namespace Prosequor.Scenarios;

/// <summary>
/// Regression for the current progress bus: server live state, entity WatchedAttributes
/// mirror (what a client hydrates), and ModData persist. Transport may get cheaper later;
/// these three views must still agree on visible fields.
/// </summary>
public class ProgressSyncScenarios : AtlasScenarioBase
{
    const string Farming = ProgressSyncInspect.Farming;
    const string Mining = ProgressSyncInspect.Mining;
    const string Repotting = "repotting";

    [AtlasScenario]
    [Trait("Layer", "Server")]
    [Trait("Kind", "ProgressSync")]
    public async Task Join_Should_MirrorProgressOntoEntity()
    {
        ITestPlayer joined = await World.JoinPlayer("SyncJoin");
        ISkillRegistry registry = ProgressSyncInspect.RequireRegistry(World.Api);
        EntityBehaviorProgress live = ProgressSyncInspect.RequireLive(joined.Player);

        Assert.True(live.HasSyncedMirror);
        ProgressSyncInspect inspect = ProgressSyncInspect.Capture(joined.Player, registry);
        inspect.AssertVisibleParity("join");
        Assert.False(inspect.TreeHasSkillMeters);
        Assert.Equal(inspect.RegisteredSkillCount, inspect.SkillKeys);
        Assert.Equal(0, inspect.UnlockKeys);
        Assert.True(inspect.MirrorTreeBytes > 0);
    }

    [AtlasScenario]
    [Trait("Layer", "Server")]
    [Trait("Kind", "ProgressSync")]
    public async Task VisibleMutations_Should_ReachMirrorAndModData()
    {
        ITestPlayer joined = await World.JoinPlayer("SyncVisible");
        IPlayer player = joined.Player;
        ISkillRegistry registry = ProgressSyncInspect.RequireRegistry(World.Api);
        EntityBehaviorProgress live = ProgressSyncInspect.RequireLive(player);

        live.AddSkillXp(Farming, 5f, mode: XpAwardMode.Grant);
        Assert.True(live.GetSkillXp(Farming) >= 5f - 0.001f);
        Assert.Equal(0f, live.GetSkillXp(Mining));

        ProgressSyncInspect afterXp = ProgressSyncInspect.Capture(player, registry);
        afterXp.AssertVisibleParity("grant farming xp");
        Assert.NotNull(afterXp.Stored);
        Assert.False(afterXp.TreeHasSkillMeters);
        Assert.Equal(inspectSkillXp(afterXp.Mirrored, Farming), live.GetSkillXp(Farming), 3);
        Assert.Equal(0f, inspectSkillXp(afterXp.Mirrored, Mining), 3);

        live.SetSkillLevel(Farming, Math.Max(1, live.GetSkillLevel(Farming)));
        live.AddUnlockPoints(5);
        Assert.True(live.GrantUnlock(Farming, Repotting));
        Assert.Equal(1, live.GetUnlockTier(Farming, Repotting));

        ProgressSyncInspect afterUnlock = ProgressSyncInspect.Capture(player, registry);
        afterUnlock.AssertVisibleParity("grant farming unlock");
        Assert.Equal(1, afterUnlock.Mirrored.GetOrCreateSkill(Farming).GetTier(Repotting));
        Assert.Equal(1, afterUnlock.Stored!.GetOrCreateSkill(Farming).GetTier(Repotting));
        Assert.True(afterUnlock.Mirrored.GetOrCreateSkill(Farming).Xp > 0f);

        live.SetAttribute(AttributeIds.Strength, 14);
        live.AddAttributeBucket(AttributeIds.Perception, 2.5f);

        ProgressSyncInspect afterAttrs = ProgressSyncInspect.Capture(player, registry);
        afterAttrs.AssertVisibleParity("set attributes");
        Assert.Equal(14, afterAttrs.Mirrored.GetAttribute(AttributeIds.Strength));
        Assert.Equal(14, afterAttrs.Stored!.GetAttribute(AttributeIds.Strength));
        Assert.Equal(2.5f, afterAttrs.Mirrored.GetAttributeBucket(AttributeIds.Perception), 3);
        Assert.Equal(1, afterAttrs.Mirrored.GetOrCreateSkill(Farming).GetTier(Repotting));
    }

    [AtlasScenario]
    [Trait("Layer", "Server")]
    [Trait("Kind", "ProgressSync")]
    public async Task BucketEarn_Should_PersistWithoutMirroringMeters()
    {
        ITestPlayer joined = await World.JoinPlayer("SyncBucket");
        IPlayer player = joined.Player;
        ISkillRegistry registry = ProgressSyncInspect.RequireRegistry(World.Api);
        EntityBehaviorProgress live = ProgressSyncInspect.RequireLive(player);

        float xpBefore = live.GetSkillXp(Farming);
        live.AddSkillXp(Farming, 0.01f, mode: XpAwardMode.Earn);

        SkillProgressState liveSkill = live.State.GetOrCreateSkill(Farming);
        Assert.True(
            liveSkill.Fill > 0f || liveSkill.Accrued > 0f,
            "Earn below MinAward should land in skill meters, not lifetime XP.");
        Assert.Equal(xpBefore, live.GetSkillXp(Farming), 3);

        ProgressSyncInspect beforeFlush = ProgressSyncInspect.Capture(player, registry);
        beforeFlush.AssertVisibleParity("earn-below-min before flush");
        Assert.False(beforeFlush.TreeHasSkillMeters);
        Assert.Equal(xpBefore, inspectSkillXp(beforeFlush.Mirrored, Farming), 3);
        Assert.True(
            beforeFlush.ModDataBytes == 0,
            "Bucket-only Earn should leave ModData untouched until FlushSave.");

        live.FlushSave();
        ProgressSyncInspect afterFlush = ProgressSyncInspect.Capture(player, registry);
        afterFlush.AssertVisibleParity("earn-below-min after flush");
        afterFlush.AssertStoredIncludesSkillMeters(Farming, "earn-below-min after flush");
        Assert.False(afterFlush.TreeHasSkillMeters);
        Assert.Equal(xpBefore, inspectSkillXp(afterFlush.Mirrored, Farming), 3);
    }

    static float inspectSkillXp(PlayerProgressState state, string skillId) =>
        state.GetOrCreateSkill(skillId).Xp;
}
