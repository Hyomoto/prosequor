using System.Diagnostics;
using Atlas.Api;
using Atlas.XUnit;
using Prosequor.Data;
using Prosequor.Player;
using Prosequor.Xp;
using Vintagestory.API.Common;
using Xunit;
using Xunit.Abstractions;

namespace Prosequor.Scenarios;

/// <summary>
/// Point-of-comparison for progress transport cost. Numbers print; a few shape asserts
/// lock today's full-tree WatchedAttributes blob so a cheaper follow-up has to beat them
/// on purpose rather than by accident.
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
    public async Task MirrorBlob_Should_ReportEmptyVsGrown_AndStayFullTreeOnXpTick()
    {
        ITestPlayer joined = await World.JoinPlayer("SyncCost");
        IPlayer player = joined.Player;
        ISkillRegistry registry = ProgressSyncInspect.RequireRegistry(World.Api);
        EntityBehaviorProgress live = ProgressSyncInspect.RequireLive(player);

        ProgressSyncInspect empty = ProgressSyncInspect.Capture(player, registry);
        Report(empty, "empty-join");

        // Today's mirror writes a row for every registered skill, even at 0/0.
        Assert.Equal(empty.RegisteredSkillCount, empty.SkillKeys);
        Assert.True(empty.RegisteredSkillCount >= 10, "Expected a multi-skill catalog for this cost baseline.");
        Assert.True(empty.MirrorTreeBytes > 0);
        Assert.False(empty.TreeHasSkillMeters);

        live.AddSkillXp(Farming, 1f, mode: XpAwardMode.Grant);
        ProgressSyncInspect oneTick = ProgressSyncInspect.Capture(player, registry);
        Report(oneTick, "one-farming-xp");

        Assert.Equal(empty.SkillKeys, oneTick.SkillKeys);
        Assert.Equal(0, inspectTier(oneTick.Mirrored, ProgressSyncInspect.Mining, "miner"));
        Assert.True(
            oneTick.MirrorTreeBytes >= empty.MirrorTreeBytes * 9 / 10,
            $"A one-skill XP tick still dirties the whole prosequor tree; expected payload near the empty blob ({empty.MirrorTreeBytes}B), got {oneTick.MirrorTreeBytes}B.");
        Assert.True(
            Math.Abs(oneTick.MirrorTreeBytes - empty.MirrorTreeBytes) < 64,
            $"XP-only ticks should not grow the tree structure; empty={empty.MirrorTreeBytes}B after={oneTick.MirrorTreeBytes}B.");

        GrantFarmingTree(live, registry);
        live.AddSkillXp(Farming, 25f, mode: XpAwardMode.Grant);
        ProgressSyncInspect grown = ProgressSyncInspect.Capture(player, registry);
        Report(grown, "grown-farming");

        Assert.Equal(empty.SkillKeys, grown.SkillKeys);
        Assert.True(grown.UnlockKeys > 0);
        Assert.True(
            grown.MirrorTreeBytes > empty.MirrorTreeBytes,
            $"Owned unlocks should enlarge the blob; empty={empty.MirrorTreeBytes}B grown={grown.MirrorTreeBytes}B.");
        Assert.True(
            grown.ModDataBytes > empty.ModDataBytes,
            $"Owned unlocks should enlarge ModData; empty={empty.ModDataBytes}B grown={grown.ModDataBytes}B.");
        grown.AssertVisibleParity("grown farming");
        Assert.False(grown.TreeHasSkillMeters);

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
            lastBytes = snap.MirrorTreeBytes;
            if (i == 0)
            {
                firstTicks = elapsed;
                firstBytes = snap.MirrorTreeBytes;
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
            $"{label} tree={snap.MirrorTreeBytes}B moddata={snap.ModDataBytes}B skills={snap.SkillKeys}/{snap.RegisteredSkillCount} unlocks={snap.UnlockKeys} metersOnTree={snap.TreeHasSkillMeters} stored={(snap.Stored != null)}");
    }

    void ReportLine(string line)
    {
        string text = "[prosequor-sync-cost] " + line;
        output.WriteLine(text);
        Console.WriteLine(text);
    }

    static int inspectTier(PlayerProgressState state, string skillId, string nodeId) =>
        state.GetOrCreateSkill(skillId).GetTier(nodeId);
}
