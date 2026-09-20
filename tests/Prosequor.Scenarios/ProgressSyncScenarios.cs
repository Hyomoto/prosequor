using Atlas.Api;
using Atlas.XUnit;
using Prosequor.Data;
using Prosequor.Network;
using Prosequor.Player;
using Prosequor.Xp;
using Vintagestory.API.Common;
using Xunit;

namespace Prosequor.Scenarios;

/// <summary>
/// Regression for sparse progress sync: live state, public WatchedAttributes (unlocks +
/// scores), owner-channel private numbers, and debounced ModData.
/// </summary>
public class ProgressSyncScenarios : AtlasScenarioBase
{
    const string Farming = ProgressSyncInspect.Farming;
    const string Mining = ProgressSyncInspect.Mining;
    const string Repotting = "repotting";

    [AtlasScenario]
    [Trait("Layer", "Server")]
    [Trait("Kind", "ProgressSync")]
    public async Task Join_Should_PublishPublicMirrorAndOwnerSnapshot()
    {
        ITestPlayer joined = await World.JoinPlayer("SyncJoin");
        ISkillRegistry registry = ProgressSyncInspect.RequireRegistry(World.Api);
        EntityBehaviorProgress live = ProgressSyncInspect.RequireLive(joined.Player);

        Assert.True(live.HasSyncedMirror);
        ProgressSyncInspect inspect = ProgressSyncInspect.Capture(joined.Player, registry);
        inspect.AssertPublicParity("join");
        Assert.False(inspect.HasLegacyTree);
        Assert.True(inspect.HasScoreTree);
        Assert.Equal(0, inspect.UnlockSkillKeys);
        Assert.Equal(0, inspect.UnlockKeys);
        Assert.Equal(0, inspect.ModDataBytes);

        IReadOnlyList<ProgressSnapshotPacket> snapshots =
            joined.Client.Packets<ProgressSnapshotPacket>(ProgressNetwork.ChannelName);
        Assert.NotEmpty(snapshots);
        ProgressSnapshotPacket snap = snapshots[^1];
        Assert.Equal(live.PlayerLevel, snap.PlayerLevel);
        Assert.Equal(live.UnlockPoints, snap.UnlockPoints);
    }

    [AtlasScenario]
    [Trait("Layer", "Server")]
    [Trait("Kind", "ProgressSync")]
    public async Task VisibleMutations_Should_ReachPublicMirrorChannelAndModData()
    {
        ITestPlayer joined = await World.JoinPlayer("SyncVisible");
        IPlayer player = joined.Player;
        ISkillRegistry registry = ProgressSyncInspect.RequireRegistry(World.Api);
        EntityBehaviorProgress live = ProgressSyncInspect.RequireLive(player);
        joined.Client.Clear();

        live.AddSkillXp(Farming, 5f, mode: XpAwardMode.Grant);
        Assert.True(live.GetSkillXp(Farming) >= 5f - 0.001f);
        Assert.Equal(0f, live.GetSkillXp(Mining));

        // Grant XP coalesces; force the pending flush.
        live.FlushPendingCoalesced();

        ProgressSyncInspect afterXp = ProgressSyncInspect.Capture(player, registry);
        afterXp.AssertPublicParity("grant farming xp");
        Assert.Equal(0, afterXp.ModDataBytes);
        Assert.Equal(0, afterXp.UnlockSkillKeys);
        // Public mirror must not carry skill XP.
        Assert.Equal(0f, afterXp.PublicMirror.GetOrCreateSkill(Farming).Xp, 3);

        IReadOnlyList<ProgressDeltaPacket> deltas =
            joined.Client.Packets<ProgressDeltaPacket>(ProgressNetwork.ChannelName);
        Assert.Contains(
            deltas,
            d => d.Skills.Exists(s =>
                string.Equals(s.SkillId, Farming, StringComparison.OrdinalIgnoreCase)
                && s.Xp >= 5f - 0.001f));

        live.SetSkillLevel(Farming, Math.Max(1, live.GetSkillLevel(Farming)));
        live.AddUnlockPoints(5);
        Assert.True(live.GrantUnlock(Farming, Repotting));
        Assert.Equal(1, live.GetUnlockTier(Farming, Repotting));

        ProgressSyncInspect afterUnlock = ProgressSyncInspect.Capture(player, registry);
        afterUnlock.AssertPublicParity("grant farming unlock");
        Assert.Equal(1, afterUnlock.PublicMirror.GetOrCreateSkill(Farming).GetTier(Repotting));
        Assert.Equal(1, afterUnlock.UnlockKeys);
        Assert.Equal(0, afterUnlock.ModDataBytes);

        live.FlushSave();
        ProgressSyncInspect afterFlush = ProgressSyncInspect.Capture(player, registry);
        Assert.NotNull(afterFlush.Stored);
        Assert.Equal(1, afterFlush.Stored!.GetOrCreateSkill(Farming).GetTier(Repotting));
        Assert.True(afterFlush.Stored.GetOrCreateSkill(Farming).Xp > 0f);

        live.SetAttribute(AttributeIds.Strength, 14);
        live.AddAttributeBucket(AttributeIds.Perception, 2.5f);
        live.FlushPendingCoalesced();

        ProgressSyncInspect afterAttrs = ProgressSyncInspect.Capture(player, registry);
        afterAttrs.AssertPublicParity("set attributes");
        Assert.Equal(14, afterAttrs.PublicMirror.GetAttribute(AttributeIds.Strength));
        Assert.Equal(1, afterAttrs.PublicMirror.GetOrCreateSkill(Farming).GetTier(Repotting));
        // Buckets stay private until ModData flush; live has them.
        Assert.Equal(2.5f, live.GetAttributeBucket(AttributeIds.Perception), 3);
    }

    [AtlasScenario]
    [Trait("Layer", "Server")]
    [Trait("Kind", "ProgressSync")]
    public async Task BucketEarn_And_GrantXp_Should_DebounceModData()
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
        beforeFlush.AssertPublicParity("earn-below-min before flush");
        Assert.Equal(0, beforeFlush.ModDataBytes);

        live.AddSkillXp(Farming, 1f, mode: XpAwardMode.Grant);
        live.FlushPendingCoalesced();
        ProgressSyncInspect afterGrant = ProgressSyncInspect.Capture(player, registry);
        Assert.True(
            afterGrant.ModDataBytes == 0,
            "Grant XP should leave ModData untouched until FlushSave.");
        Assert.True(live.GetSkillXp(Farming) >= xpBefore + 1f - 0.001f);

        live.FlushSave();
        ProgressSyncInspect afterFlush = ProgressSyncInspect.Capture(player, registry);
        afterFlush.AssertPublicParity("after FlushSave");
        afterFlush.AssertStoredIncludesSkillMeters(Farming, "after FlushSave");
        Assert.True(afterFlush.Stored!.GetOrCreateSkill(Farming).Xp >= xpBefore + 1f - 0.001f);
    }
}
