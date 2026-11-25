using Prosequor.Ability.Hooks;
using Prosequor.Progress;
using Prosequor.Xp;
using Xunit;

namespace Prosequor.Data;

/// <summary>Whole-skill tree clear / refund fixtures for <see cref="SkillTreeReset"/>.</summary>
public static class SkillTreeResetFixtures
{
    public static void VerifyAll()
    {
        VerifyNonHobbyMultiNodeAndMultiTier();
        VerifyHobbyClearsWithoutGlobalRefund();
        VerifyStartingNodeRestoredWithoutRefundingTier1();
        VerifyUnknownOwnedIdClearedWithoutRefund();
    }

    static void VerifyNonHobbyMultiNodeAndMultiTier()
    {
        SkillDef skill = BuildMinorSkill(
            "mining",
            ("pick", [2, 3]),
            ("lamp", [1]));

        PlayerProgressState state = new();
        state.UnlockPoints = 1;
        SkillProgressState progress = state.GetOrCreateSkill(skill.Id);
        progress.Level = 12;
        progress.Xp = 40f;
        progress.SetTier("pick", 2);
        progress.SetTier("lamp", 1);

        SkillTreeReset.Result first = SkillTreeReset.ClearTree(state, skill);
        if (first.PointsRefunded != 6
            || first.NodesCleared != 2
            || state.UnlockPoints != 7
            || progress.GetTier("pick") != 0
            || progress.GetTier("lamp") != 0
            || progress.Level != 12
            || progress.Xp != 40f)
        {
            Assert.Fail(
                $"[prosequor] Non-hobby tree reset must refund sum of tier costs and keep XP/level. " +
                $"refund={first.PointsRefunded} nodes={first.NodesCleared} points={state.UnlockPoints} " +
                $"pick={progress.GetTier("pick")} lamp={progress.GetTier("lamp")}.");
        }

        SkillTreeReset.Result second = SkillTreeReset.ClearTree(state, skill);
        if (second.PointsRefunded != 0 || second.NodesCleared != 0 || state.UnlockPoints != 7)
        {
            Assert.Fail("[prosequor] Empty tree reset must be idempotent.");
        }
    }

    static void VerifyHobbyClearsWithoutGlobalRefund()
    {
        SkillDef skill = BuildHobbySkill("barber", ("cut", [3]));
        PlayerProgressState state = new();
        state.UnlockPoints = 5;
        SkillProgressState progress = state.GetOrCreateSkill(skill.Id);
        progress.Level = 4;
        progress.Xp = 10f;
        progress.SetTier("cut", 1);

        SkillTreeReset.Result result = SkillTreeReset.ClearTree(state, skill);
        if (result.PointsRefunded != 0
            || state.UnlockPoints != 5
            || progress.GetTier("cut") != 0
            || progress.Level != 4
            || progress.Xp != 10f)
        {
            Assert.Fail("[prosequor] Hobby tree reset must clear tiers without minting global points.");
        }
    }

    static void VerifyStartingNodeRestoredWithoutRefundingTier1()
    {
        SkillDef skill = BuildMinorSkill(
            "farming",
            ("hoe", [2, 4]),
            ("seed", [1]));

        PlayerProgressState state = new();
        state.UnlockPoints = 0;
        SkillProgressState progress = state.GetOrCreateSkill(skill.Id);
        progress.SetTier("hoe", 2);
        progress.SetTier("seed", 1);

        SkillTreeReset.Result result = SkillTreeReset.ClearTree(state, skill, ["hoe"]);
        if (result.PointsRefunded != 5
            || state.UnlockPoints != 5
            || progress.GetTier("hoe") != 1
            || progress.GetTier("seed") != 0)
        {
            Assert.Fail(
                $"[prosequor] Starting node must stay at tier 1 without refunding that rank; " +
                $"bought ranks refund. refund={result.PointsRefunded} points={state.UnlockPoints} " +
                $"hoe={progress.GetTier("hoe")} seed={progress.GetTier("seed")}.");
        }

        // seed cost 1 + hoe tier2 cost 4 = 5; hoe tier1 cost 2 kept.
        SkillTreeReset.Result again = SkillTreeReset.ClearTree(state, skill, ["hoe"]);
        if (again.PointsRefunded != 0 || progress.GetTier("hoe") != 1 || state.UnlockPoints != 5)
        {
            Assert.Fail("[prosequor] Second reset with only the starting grant must refund 0.");
        }
    }

    static void VerifyUnknownOwnedIdClearedWithoutRefund()
    {
        SkillDef skill = BuildMinorSkill("digging", ("shovel", [2]));
        PlayerProgressState state = new();
        state.UnlockPoints = 3;
        SkillProgressState progress = state.GetOrCreateSkill(skill.Id);
        progress.SetTier("shovel", 1);
        progress.SetTier("ghost-node", 2);

        SkillTreeReset.Result result = SkillTreeReset.ClearTree(state, skill);
        if (result.PointsRefunded != 2
            || state.UnlockPoints != 5
            || progress.GetTier("shovel") != 0
            || progress.GetTier("ghost-node") != 0
            || !result.ClearedNodeIds.Contains("ghost-node", StringComparer.OrdinalIgnoreCase))
        {
            Assert.Fail(
                $"[prosequor] Unknown owned ids must clear with no refund. " +
                $"refund={result.PointsRefunded} points={state.UnlockPoints}.");
        }
    }

    static SkillDef BuildMinorSkill(string id, params (string NodeId, int[] Costs)[] nodes) =>
        BuildSkill(id, SkillKind.Minor, XpCurves.MinorMaxLevel, nodes);

    static SkillDef BuildHobbySkill(string id, params (string NodeId, int[] Costs)[] nodes) =>
        BuildSkill(id, SkillKind.Hobby, XpCurves.HobbyMaxLevel, nodes);

    static SkillDef BuildSkill(
        string id,
        SkillKind kind,
        int maxLevel,
        params (string NodeId, int[] Costs)[] nodes)
    {
        List<SkillTreeNodeDef> compiled = new();
        Dictionary<string, SkillTreeNodeDef> byId = new(StringComparer.OrdinalIgnoreCase);
        foreach ((string nodeId, int[] costs) in nodes)
        {
            SkillTreeTierDef[] tiers = costs
                .Select(cost => new SkillTreeTierDef
                {
                    Cost = cost,
                    MinSkillLevel = 0,
                    Rules = Array.Empty<AbilityRule>()
                })
                .ToArray();
            SkillTreeNodeDef node = new() { Id = nodeId, Tiers = tiers };
            compiled.Add(node);
            byId[nodeId] = node;
        }

        return new SkillDef
        {
            Id = id,
            Kind = kind,
            MaxLevel = maxLevel,
            Tree = new SkillTreeDef { Nodes = compiled, ById = byId }
        };
    }
}
