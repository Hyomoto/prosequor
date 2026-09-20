using System.Diagnostics;
using Atlas.Api;
using Atlas.XUnit;
using Prosequor.Data;
using Prosequor.Network;
using Prosequor.Player;
using Prosequor.Xp;
using Vintagestory.API.Common;
using Xunit;
using Xunit.Abstractions;

namespace Prosequor.Scenarios;

/// <summary>
/// Point-of-comparison for sparse progress transport. Shape asserts lock the cheap public
/// WA + owner-channel design; numbers print for local scale feel.
/// </summary>
public class ProgressSyncCostScenarios : AtlasScenarioBase
{
    const string Farming = ProgressSyncInspect.Farming;
    const int TimedSerializes = 40;

    readonly ITestOutputHelper output;

    public ProgressSyncCostScenarios(ITestOutputHelper output) => this.output = output;

    [AtlasScenario]
    [Trait("Layer", "Profile")]
    [Trait("Kind", "ProgressSync")]
    public async Task PublicMirror_Should_StaySparse_AndXpShouldRideOwnerChannel()
    {
        ITestPlayer joined = await World.JoinPlayer("SyncCost");
        IPlayer player = joined.Player;
        ISkillRegistry registry = ProgressSyncInspect.RequireRegistry(World.Api);
        EntityBehaviorProgress live = ProgressSyncInspect.RequireLive(player);

        ProgressSyncInspect empty = ProgressSyncInspect.Capture(player, registry);
        Report(empty, "empty-join");

        Assert.True(empty.RegisteredSkillCount >= 10);
        Assert.False(empty.HasLegacyTree);
        Assert.Equal(0, empty.UnlockSkillKeys);
        Assert.Equal(0, empty.UnlockKeys);
        Assert.Equal(0, empty.ModDataBytes);
        Assert.True(empty.HasScoreTree);
        // Attribute scores only — far below the old ~1065B full skill blob.
        Assert.True(
            empty.PublicTreeBytes < 400,
            $"Empty public WA should be scores-only; got {empty.PublicTreeBytes}B.");

        joined.Client.Clear();
        live.AddSkillXp(Farming, 1f, mode: XpAwardMode.Grant);
        live.FlushPendingCoalesced();
        ProgressSyncInspect oneTick = ProgressSyncInspect.Capture(player, registry);
        Report(oneTick, "one-farming-xp");

        Assert.Equal(0, oneTick.UnlockSkillKeys);
        Assert.Equal(0, oneTick.ModDataBytes);
        Assert.True(
            Math.Abs(oneTick.PublicTreeBytes - empty.PublicTreeBytes) < 32,
            $"XP-only ticks must not grow public WA; empty={empty.PublicTreeBytes}B after={oneTick.PublicTreeBytes}B.");
        Assert.Contains(
            joined.Client.Packets<ProgressDeltaPacket>(ProgressNetwork.ChannelName),
            d => d.Skills.Exists(s =>
                string.Equals(s.SkillId, Farming, StringComparison.OrdinalIgnoreCase)));

        GrantFarmingTree(live, registry);
        live.AddSkillXp(Farming, 25f, mode: XpAwardMode.Grant);
        live.FlushPendingCoalesced();
        ProgressSyncInspect grown = ProgressSyncInspect.Capture(player, registry);
        Report(grown, "grown-farming");

        Assert.True(grown.UnlockKeys > 0);
        Assert.True(grown.UnlockSkillKeys > 0);
        Assert.True(grown.UnlockSkillKeys < grown.RegisteredSkillCount);
        Assert.False(grown.HasLegacyTree);
        Assert.True(
            grown.PublicTreeBytes > empty.PublicTreeBytes,
            $"Owned unlocks should enlarge public WA; empty={empty.PublicTreeBytes}B grown={grown.PublicTreeBytes}B.");
        Assert.Equal(0, grown.ModDataBytes);
        grown.AssertPublicParity("grown farming");

        live.FlushSave();
        ProgressSyncInspect persisted = ProgressSyncInspect.Capture(player, registry);
        Assert.True(persisted.ModDataBytes > 0);
        Report(persisted, "grown-flushed");

        TimeSerialize("serialize/grown", player, registry);
    }

    void GrantFarmingTree(IPlayerProgress progress, ISkillRegistry registry)
    {
        Assert.True(registry.TryGet(Farming, out SkillDef skill), "farming skill missing");
        Assert.NotNull(skill.Tree);

        progress.SetSkillLevel(Farming, skill.MaxLevel);
        progress.AddUnlockPoints(200);

        bool progressed;
        do
        {
            progressed = false;
            foreach (SkillTreeNodeDef node in skill.Tree!.Nodes)
            {
                while (progress.GetUnlockTier(Farming, node.Id) < node.MaxTier)
                {
                    if (!progress.GrantUnlock(Farming, node.Id))
                    {
                        break;
                    }

                    progressed = true;
                }
            }
        }
        while (progressed);

        int owned = 0;
        foreach (SkillTreeNodeDef node in skill.Tree.Nodes)
        {
            if (progress.GetUnlockTier(Farming, node.Id) > 0)
            {
                owned++;
            }
        }

        Assert.True(owned > 0, "Expected at least one farming unlock for the grown blob.");
        ReportLine($"grant/farming-tree ownedNodes={owned} level={progress.GetSkillLevel(Farming)}");
    }

    void TimeSerialize(string label, IPlayer player, ISkillRegistry registry)
    {
        for (int i = 0; i < 5; i++)
        {
            _ = ProgressSyncInspect.Capture(player, registry);
        }

        int firstBytes = 0;
        int lastBytes = 0;
        var sw = Stopwatch.StartNew();
        long firstTicks = 0;
        for (int i = 0; i < TimedSerializes; i++)
        {
            long before = Stopwatch.GetTimestamp();
            ProgressSyncInspect snap = ProgressSyncInspect.Capture(player, registry);
            long elapsed = Stopwatch.GetTimestamp() - before;
            lastBytes = snap.PublicTreeBytes;
            if (i == 0)
            {
                firstTicks = elapsed;
                firstBytes = snap.PublicTreeBytes;
            }
        }

        sw.Stop();
        double totalMs = sw.Elapsed.TotalMilliseconds;
        double meanUs = totalMs * 1000.0 / TimedSerializes;
        double firstUs = firstTicks * 1_000_000.0 / Stopwatch.Frequency;
        ReportLine(
            $"{label} n={TimedSerializes} first={firstUs:F1}µs mean={meanUs:F1}µs total={totalMs:F2}ms tree={lastBytes}B firstTree={firstBytes}B");
        Assert.Equal(firstBytes, lastBytes);
    }

    void Report(ProgressSyncInspect snap, string label)
    {
        ReportLine(
            $"{label} public={snap.PublicTreeBytes}B moddata={snap.ModDataBytes}B unlockSkills={snap.UnlockSkillKeys}/{snap.RegisteredSkillCount} unlocks={snap.UnlockKeys} legacy={snap.HasLegacyTree} scores={snap.HasScoreTree} stored={(snap.Stored != null)}");
    }

    void ReportLine(string line)
    {
        string text = "[prosequor-sync-cost] " + line;
        output.WriteLine(text);
        Console.WriteLine(text);
    }
}
