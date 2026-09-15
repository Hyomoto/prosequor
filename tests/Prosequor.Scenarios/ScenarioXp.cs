using Prosequor.Ability;
using Prosequor.Player;
using Prosequor.Xp;
using Prosequor.Xp.Activity;
using Vintagestory.API.Common;
using Xunit;

namespace Prosequor.Scenarios;

/// <summary>
/// Scenario expected-XP from the loaded rule table via <see cref="Deed.PlanPays"/>.
/// Asserts the live emit matches compiled data, not a hardcoded balance number.
/// </summary>
static class ScenarioXp
{
    const float Tolerance = 0.05f;

    public static float TotalSkill(IPlayer player, string skillId)
    {
        EntityBehaviorProgress? progress = player.Entity?.GetBehavior<EntityBehaviorProgress>();
        Assert.NotNull(progress);
        var skill = progress!.State.GetOrCreateSkill(skillId);
        return progress.GetSkillXp(skillId) + skill.Accrued;
    }

    public static float TotalSkill(IPlayerProgress progress, string skillId)
    {
        if (progress is EntityBehaviorProgress live)
        {
            var skill = live.State.GetOrCreateSkill(skillId);
            return live.GetSkillXp(skillId) + skill.Accrued;
        }

        return progress.GetSkillXp(skillId);
    }

    /// <summary>Same fact <see cref="CraftedProductXp.Emit"/> publishes.</summary>
    public static float PlannedCrafted(
        IWorldAccessor world,
        string skillId,
        string playerUid,
        ItemStack stack,
        int quantity,
        int ingredients = 0,
        IReadOnlyList<Deed.ContributorShare>? contributors = null,
        string? makerUid = null,
        IReadOnlyList<string>? extraTokens = null)
    {
        string? target = EventFactBuilder.CodeOf(stack);
        Assert.False(string.IsNullOrWhiteSpace(target), "Expected a crafted stack code.");
        HashSet<string> tokens = new(StringComparer.OrdinalIgnoreCase) { DeedTokenTags.Crafted };
        if (extraTokens != null)
        {
            foreach (string extra in extraTokens)
            {
                if (!string.IsNullOrWhiteSpace(extra))
                {
                    tokens.Add(extra.Trim());
                }
            }
        }

        List<Deed.QuantityUnit>? units = quantity > 0
            ? [new Deed.QuantityUnit(target!, quantity)]
            : null;
        return Sum(
            Plan(
                world,
                playerUid,
                tokens,
                CallerIdentities.Grid,
                target,
                ingredients,
                quantity,
                units,
                contributors,
                makerUid),
            skillId,
            playerUid);
    }

    /// <summary>Same fact <see cref="Prosequor.Xp.Adapters.CraftXpAdapter.NotifyTake"/> publishes.</summary>
    public static float PlannedGridTake(
        IWorldAccessor world,
        string skillId,
        IPlayer player,
        ItemStack perCraftOutput,
        int craftCount,
        int unitsPerCraft)
    {
        string? target = EventFactBuilder.CodeOf(perCraftOutput);
        Assert.False(string.IsNullOrWhiteSpace(target), "Expected a craft output code.");
        int reps = Math.Max(1, craftCount);
        int outputCount = Math.Max(0, perCraftOutput.StackSize) * reps;
        List<Deed.QuantityUnit>? units = outputCount > 0
            ? [new Deed.QuantityUnit(target!, outputCount)]
            : null;
        return Sum(
            Plan(
                world,
                player.PlayerUID,
                new HashSet<string>(StringComparer.OrdinalIgnoreCase) { DeedTokenTags.Crafted },
                CallerIdentities.Grid,
                target,
                unitsPerCraft,
                reps,
                units,
                lastCraft: EventFactBuilder.LastCraftCode(player)),
            skillId,
            player.PlayerUID);
    }

    /// <summary>Same fact clay/anvil voxel progress publishes (<c>crafting</c> + <c>@hand</c>).</summary>
    public static float PlannedCraftingVoxels(
        IWorldAccessor world,
        string skillId,
        string playerUid,
        string? target,
        int voxelCount)
    {
        Assert.True(voxelCount > 0);
        List<Deed.QuantityUnit>? units = !string.IsNullOrWhiteSpace(target)
            ? [new Deed.QuantityUnit(target!, voxelCount)]
            : null;
        return Sum(
            Plan(
                world,
                playerUid,
                new HashSet<string>(StringComparer.OrdinalIgnoreCase) { DeedTokenTags.Crafting },
                CallerIdentities.Hand,
                target,
                totalUnits: 0,
                craftCount: voxelCount,
                units),
            skillId,
            playerUid);
    }

    /// <summary>Same fact mold harden settle publishes (<c>mold-cast</c>, payee contributors).</summary>
    public static float PlannedMoldCast(
        IWorldAccessor world,
        string skillId,
        string pourerUid,
        string? target,
        int fillLevel)
    {
        int ingredients = MoldCastXpStation.IngredientsFromFill(fillLevel);
        return Sum(
            Plan(
                world,
                playerUid: "",
                new HashSet<string>(StringComparer.OrdinalIgnoreCase) { DeedTokenTags.MoldCast },
                CallerIdentities.Mold,
                target,
                totalUnits: ingredients,
                craftCount: 1,
                quantityUnits: null,
                contributors: [new Deed.ContributorShare(pourerUid, 1f)]),
            skillId,
            pourerUid);
    }

    /// <summary>Same fact <see cref="BloomeryHarvestXp.Settle"/> publishes.</summary>
    public static float PlannedBloomeryHarvest(
        IWorldAccessor world,
        string skillId,
        string playerUid,
        string? target)
    {
        return Sum(
            Plan(
                world,
                playerUid,
                new HashSet<string>(StringComparer.OrdinalIgnoreCase) { DeedTokenTags.BloomeryHarvest },
                CallerIdentities.Hand,
                target,
                totalUnits: 0,
                craftCount: 1,
                quantityUnits: null),
            skillId,
            playerUid);
    }

    public static void AssertPaid(float gained, float expected, string detail)
    {
        Assert.True(
            Math.Abs(gained - expected) <= Tolerance,
            $"{detail} expected {expected} (from loaded xpRules), got {gained}.");
    }

    /// <summary>Same fact <see cref="Prosequor.Xp.Adapters.BlockBreakXpAdapter"/> publishes for mine/dig/chop.</summary>
    public static float PlannedBlockBroken(
        IWorldAccessor world,
        string skillId,
        string playerUid,
        string caller,
        string? target,
        float resistance,
        string metricDomain)
    {
        ProsequorModSystem? mod = ProsequorModSystem.For(world.Api);
        Assert.NotNull(mod);
        float min = 0f;
        float max = 0f;
        _ = mod!.BlockBreakHardness?.TryGetRange(metricDomain, out min, out max);
        return Sum(
            Deed.PlanPays(
                mod.XpRules,
                mod.Collections.Index,
                playerUid,
                new HashSet<string>(StringComparer.OrdinalIgnoreCase) { DeedTokenTags.BlockBroken },
                caller,
                target,
                mount: null,
                ground: null,
                lastCraft: null,
                resistance,
                min,
                max,
                totalUnits: 0,
                craftCount: 1,
                metricDomain: metricDomain),
            skillId,
            playerUid);
    }

    static IReadOnlyList<Deed.PlannedPay> Plan(
        IWorldAccessor world,
        string playerUid,
        IReadOnlySet<string> tokens,
        string? caller,
        string? target,
        int totalUnits,
        int craftCount,
        IReadOnlyList<Deed.QuantityUnit>? quantityUnits,
        IReadOnlyList<Deed.ContributorShare>? contributors = null,
        string? makerUid = null,
        string? lastCraft = null)
    {
        ProsequorModSystem? mod = ProsequorModSystem.For(world.Api);
        Assert.NotNull(mod);
        return Deed.PlanPays(
            mod!.XpRules,
            mod.Collections.Index,
            playerUid,
            tokens,
            caller,
            target,
            mount: null,
            ground: null,
            lastCraft,
            Deed.BuildChannels(
                metric: 0f,
                metricMin: 0f,
                metricMax: 0f,
                metricDomain: null,
                totalUnits,
                craftCount,
                quantityUnits,
                contributors,
                makerUid));
    }

    static float Sum(IReadOnlyList<Deed.PlannedPay> pays, string skillId, string playerUid)
    {
        float total = 0f;
        foreach (Deed.PlannedPay pay in pays)
        {
            if (!string.Equals(pay.SkillId, skillId, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!string.Equals(pay.Fact.ActorUid, playerUid, StringComparison.Ordinal))
            {
                continue;
            }

            total += pay.Amount;
        }

        return total;
    }
}
