using Atlas.Api;
using Atlas.XUnit;
using Prosequor;
using Prosequor.Ability;
using Prosequor.Data;
using Prosequor.Player;
using Prosequor.Xp.Activity;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Xunit;

namespace Prosequor.Scenarios;

/// <summary>
/// Husbandry friendliness amplifiers: grant live unlocks, resolve animal-behavior
/// multipliers, and assert the same Scale* helpers Harmony uses.
/// Atlas shares one host per class (16 clients). Overflow tests use <c>FreshWorld</c>.
/// </summary>
public class HusbandryFriendlinessScenarios : AtlasScenarioBase
{
    const string Skill = HusbandryFriendliness.SkillId;

    [AtlasScenario]
    [Trait("Layer", "Action")]
    [Trait("Kind", "HusbandryFriendliness")]
    public async Task GentleSpirit_Should_ScaleFearFriendliness_ByRank()
    {
        ITestPlayer joined = await World.JoinPlayer("HSGentle");
        IPlayer player = joined.Player;
        IPlayerProgress progress = RequireProgress(player);

        progress.AddUnlockPoints(10);
        progress.SetSkillLevel(Skill, 40);

        Assert.Equal(
            HusbandryFriendliness.MultSeedPercent,
            AnimalBehaviorStation.ResolveFleeFearMultiplierPercent(player, animal: null));

        Assert.True(progress.GrantUnlock(Skill, "gentle-spirit"));
        Assert.Equal(200, AnimalBehaviorStation.ResolveFleeFearMultiplierPercent(player, animal: null));
        ExpectFearScale(vanilla: 1f, friendliness: 25, multPercent: 200, expected: 0.5f);

        Assert.True(progress.GrantUnlock(Skill, "gentle-spirit"));
        Assert.Equal(300, AnimalBehaviorStation.ResolveFleeFearMultiplierPercent(player, animal: null));
        ExpectFearScale(vanilla: 1f, friendliness: 25, multPercent: 300, expected: 0.25f);
    }

    [AtlasScenario]
    [Trait("Layer", "Action")]
    [Trait("Kind", "HusbandryFriendliness")]
    public async Task CalmingPresence_Should_ScaleAggressionFriendliness_ByRank()
    {
        ITestPlayer joined = await World.JoinPlayer("HSCalm");
        IPlayer player = joined.Player;
        IPlayerProgress progress = RequireProgress(player);
        GrantRancherBranch(progress);

        Assert.Equal(
            HusbandryFriendliness.MultSeedPercent,
            AnimalBehaviorStation.ResolveMeleeFearMultiplierPercent(player, animal: null));

        Assert.True(progress.GrantUnlock(Skill, "calming-presence"));
        Assert.Equal(200, AnimalBehaviorStation.ResolveMeleeFearMultiplierPercent(player, animal: null));
        ExpectFearScale(vanilla: 1f, friendliness: 25, multPercent: 200, expected: 0.5f);

        Assert.True(progress.GrantUnlock(Skill, "calming-presence"));
        Assert.Equal(300, AnimalBehaviorStation.ResolveMeleeFearMultiplierPercent(player, animal: null));
        ExpectFearScale(vanilla: 1f, friendliness: 25, multPercent: 300, expected: 0.25f);
    }

    [AtlasScenario]
    [Trait("Layer", "Action")]
    [Trait("Kind", "HusbandryFriendliness")]
    public async Task HenFriend_Should_ScaleBroodFriendliness_ByRank()
    {
        ITestPlayer joined = await World.JoinPlayer("HSHen");
        IPlayer player = joined.Player;
        IPlayerProgress progress = RequireProgress(player);
        GrantRancherBranch(progress);

        Assert.Equal(
            HusbandryFriendliness.MultSeedPercent,
            AnimalBehaviorStation.ResolveBroodMultiplierPercent(player, animal: null));

        Assert.True(progress.GrantUnlock(Skill, "hen-friend"));
        Assert.Equal(200, AnimalBehaviorStation.ResolveBroodMultiplierPercent(player, animal: null));
        ExpectFearScale(vanilla: 1f, friendliness: 25, multPercent: 200, expected: 0.5f);

        Assert.True(progress.GrantUnlock(Skill, "hen-friend"));
        Assert.Equal(300, AnimalBehaviorStation.ResolveBroodMultiplierPercent(player, animal: null));
        ExpectFearScale(vanilla: 1f, friendliness: 25, multPercent: 300, expected: 0.25f);
    }

    [AtlasScenario]
    [Trait("Layer", "Action")]
    [Trait("Kind", "HusbandryFriendliness")]
    public async Task WarmHands_Should_ScaleMilkingAggro_ByRank()
    {
        ITestPlayer joined = await World.JoinPlayer("HSWarm");
        IPlayer player = joined.Player;
        IPlayerProgress progress = RequireProgress(player);
        GrantRancherBranch(progress);

        Assert.Equal(
            HusbandryFriendliness.MultSeedPercent,
            AnimalBehaviorStation.ResolveMilkMultiplierPercent(player, animal: null));

        Assert.True(progress.GrantUnlock(Skill, "warm-hands"));
        Assert.Equal(150, AnimalBehaviorStation.ResolveMilkMultiplierPercent(player, animal: null));
        ExpectMilkScale(aggro: 0.95f, friendliness: 50, multPercent: 150, expected: 0.2375f);

        Assert.True(progress.GrantUnlock(Skill, "warm-hands"));
        Assert.Equal(200, AnimalBehaviorStation.ResolveMilkMultiplierPercent(player, animal: null));
        ExpectMilkScale(aggro: 0.95f, friendliness: 50, multPercent: 200, expected: 0f);
    }

    [AtlasScenario]
    [Trait("Layer", "Action")]
    [Trait("Kind", "HusbandryFriendliness")]
    public async Task HusbandryPassive_Should_ResolveFleeFriendliness_FromRootEffect()
    {
        ITestPlayer joined = await World.JoinPlayer("HSPassive");
        IPlayer player = joined.Player;
        IPlayerProgress progress = RequireProgress(player);

        Assert.Equal(
            0f,
            AnimalBehaviorStation.ResolveFleeChanceReduction(player, animal: null),
            precision: 4);

        progress.SetSkillLevel(Skill, 40);
        Assert.Equal(
            0.2f,
            AnimalBehaviorStation.ResolveFleeChanceReduction(player, animal: null),
            precision: 4);

        // Cap by friendliness: reduction 0.2 vs f=0.10 → chance * 0.9
        float scaled = HusbandryFriendliness.ScalePassiveFleeChance(1f, 0.2f, friendliness: 10);
        Assert.Equal(0.9f, scaled, precision: 4);
    }

    [AtlasScenario]
    [Trait("Layer", "Action")]
    [Trait("Kind", "HusbandryFriendliness")]
    public async Task Feedhand_Should_RaiseFillQuantity_And_EatChance()
    {
        ITestPlayer joined = await World.JoinPlayer("HSFeedhand");
        IPlayer player = joined.Player;
        IPlayerProgress progress = RequireProgress(player);
        Entity animal = player.Entity;

        Assert.Equal(1, TroughFillStation.ResolveFillQuantity(player));
        Assert.Equal(
            TroughEatStation.BaseFriendlinessChance,
            TroughEatStation.ResolveFriendlinessChance(player.Entity.Api, player.PlayerUID, animal),
            precision: 4);

        progress.AddUnlockPoints(10);
        progress.SetSkillLevel(Skill, 40);
        Assert.True(progress.GrantUnlock(Skill, "feedhand"));
        Assert.Equal(2, TroughFillStation.ResolveFillQuantity(player));
        Assert.Equal(
            0.10f,
            TroughEatStation.ResolveFriendlinessChance(player.Entity.Api, player.PlayerUID, animal),
            precision: 4);

        Assert.True(progress.GrantUnlock(Skill, "feedhand"));
        Assert.Equal(3, TroughFillStation.ResolveFillQuantity(player));
        Assert.Equal(
            0.10f,
            TroughEatStation.ResolveFriendlinessChance(player.Entity.Api, player.PlayerUID, animal),
            precision: 4);
    }

    [AtlasScenario]
    [Trait("Layer", "Action")]
    [Trait("Kind", "HusbandryFriendliness")]
    public async Task AnimalWhisperer_Should_AllowPet_WhenFriendlinessAboveThreshold()
    {
        ITestPlayer joined = await World.JoinPlayer("HSWhisper");
        IPlayer player = joined.Player;
        IPlayerProgress progress = RequireProgress(player);
        Entity animal = player.Entity;

        progress.AddUnlockPoints(10);
        progress.SetSkillLevel(Skill, 40);
        Assert.True(progress.GrantUnlock(Skill, "feedhand"));

        HusbandryFriendliness.Set(animal, 8);
        Assert.False(AnimalBehaviorStation.ResolvePetInteractionAllowed(player, animal));

        // Tier 1: minFriendliness 7 → need score > 7.
        Assert.True(progress.GrantUnlock(Skill, "animal-whisperer"));
        HusbandryFriendliness.Set(animal, 7);
        Assert.False(AnimalBehaviorStation.ResolvePetInteractionAllowed(player, animal));
        HusbandryFriendliness.Set(animal, 8);
        Assert.True(AnimalBehaviorStation.ResolvePetInteractionAllowed(player, animal));

        // Tier 2 (skill 40 ≥ 30): minFriendliness 6 → need score > 6.
        Assert.True(progress.GrantUnlock(Skill, "animal-whisperer"));
        HusbandryFriendliness.Set(animal, 6);
        Assert.False(AnimalBehaviorStation.ResolvePetInteractionAllowed(player, animal));
        HusbandryFriendliness.Set(animal, 7);
        Assert.True(AnimalBehaviorStation.ResolvePetInteractionAllowed(player, animal));

        // Friendliness gain is chance-gated (5% / 10% / 25%); result is 0 or 1.
        int gain = AnimalBehaviorStation.ResolvePetFriendlinessGain(player, animal);
        Assert.True(gain is 0 or 1, $"Expected pet friendliness gain 0 or 1, got {gain}.");
    }

    [AtlasScenario]
    [Trait("Layer", "Action")]
    [Trait("Kind", "HusbandryFriendliness")]
    public async Task Friendliness_Should_LiveOnPedigreeContributors()
    {
        ITestPlayer joined = await World.JoinPlayer("HSPedCred");
        IPlayer player = joined.Player;
        Entity animal = player.Entity;

        HusbandryFriendliness.Set(animal, 0);
        HusbandryFriendliness.Add(animal, player.PlayerUID, 3);
        Assert.True(HusbandryFriendliness.TryGetFriendlinessReadyAt(animal, out double readyAt));
        Assert.True(readyAt > (animal.World.Calendar?.TotalHours ?? 0));
        HusbandryFriendliness.StampFriendlinessReadyAt(animal, 0);
        HusbandryFriendliness.Add(animal, "other", 2);

        Assert.Equal(5, HusbandryFriendliness.Get(animal));
        Assert.True(ProsequorEntityPedigreeStation.TryGetBlob(animal, out ProsequorBlob blob));
        Assert.True(blob.TryGetContributorWeight(player.PlayerUID, out int self));
        Assert.Equal(3, self);
        Assert.True(blob.TryGetContributorWeight("other", out int other));
        Assert.Equal(2, other);

        HusbandryFriendliness.Set(animal, 6);
        Assert.Equal(6, HusbandryFriendliness.Get(animal));
        Assert.True(ProsequorEntityPedigreeStation.TryGetBlob(animal, out ProsequorBlob afterSet));
        Assert.True(afterSet.TryGetContributorWeight(HusbandryFriendliness.AnonContributorUid, out int anon));
        Assert.Equal(6, anon);
    }

    [AtlasScenario]
    [Trait("Layer", "Action")]
    [Trait("Kind", "HusbandryFriendliness")]
    public async Task Friendliness_Should_GateGainUntilReadyAt()
    {
        ITestPlayer joined = await World.JoinPlayer("HSGainGate");
        Entity animal = joined.Player.Entity;

        // Deterministic cooldown: Next(1,7) → 1 → +4h.
        HusbandryFriendliness.Add(animal, "alice", 1, new FixedRandom(0.0, 0));
        Assert.Equal(1, HusbandryFriendliness.Get(animal));
        Assert.True(HusbandryFriendliness.TryGetFriendlinessReadyAt(animal, out double readyAt));
        double now = animal.World.Calendar?.TotalHours ?? 0;
        Assert.Equal(now + HusbandryFriendliness.GainCooldownStepHours, readyAt, precision: 5);
        Assert.False(HusbandryFriendliness.CanGain(animal, now));

        HusbandryFriendliness.Add(animal, "bob", 1, new FixedRandom(0.0, 0));
        Assert.Equal(1, HusbandryFriendliness.Get(animal));
        Assert.False(
            ProsequorEntityPedigreeStation.TryGetBlob(animal, out ProsequorBlob blob)
            && blob.TryGetContributorWeight("bob", out _));

        HusbandryFriendliness.StampFriendlinessReadyAt(animal, now);
        Assert.True(HusbandryFriendliness.CanGain(animal, now));
        HusbandryFriendliness.Add(animal, "bob", 1, new FixedRandom(0.0, 5));
        Assert.Equal(2, HusbandryFriendliness.Get(animal));
        Assert.True(HusbandryFriendliness.TryGetFriendlinessReadyAt(animal, out double nextReady));
        Assert.Equal(
            now + HusbandryFriendliness.GainCooldownStepHours * 6,
            nextReady,
            precision: 5);
    }

    [AtlasScenario]
    [Trait("Layer", "Action")]
    [Trait("Kind", "HusbandryFriendliness")]
    public async Task FavoriteSeraph_Should_PickHighestWeightContributor()
    {
        ITestPlayer joined = await World.JoinPlayer("HSFavSeraph");
        Entity animal = joined.Player.Entity;

        // Direct pedigree weights — avoid Add()'s 5% roll while building the bag.
        ProsequorEntityPedigreeStation.AddContributor(animal, "alice", 2);
        ProsequorEntityPedigreeStation.AddContributor(animal, "bob", 5);
        ProsequorEntityPedigreeStation.AddContributor(animal, "carol", 5);
        ProsequorEntityPedigreeStation.AddContributor(
            animal, HusbandryFriendliness.AnonContributorUid, 99);
        Assert.True(
            HusbandryFriendliness.TryPickTopContributor(animal, new Random(1), out string? top));
        Assert.Contains(top, new[] { "bob", "carol" });

        HusbandryFriendliness.MaybeRefreshFavoriteSeraph(animal, new FixedRandom(0.0, 0));
        Assert.True(HusbandryFriendliness.TryGetFavoriteSeraph(animal, out string? favorite));
        Assert.Equal("bob", favorite);
    }

    [AtlasScenario]
    [Trait("Layer", "Action")]
    [Trait("Kind", "HusbandryFriendliness")]
    public async Task FavoriteSeraph_Should_KeepCurrent_WhenNoPickableContributors()
    {
        ITestPlayer joined = await World.JoinPlayer("HSFavEmpty");
        Entity animal = joined.Player.Entity;

        ProsequorEntityPedigreeStation.ApplyBlob(
            animal,
            new ProsequorBlob("old-fav", Array.Empty<ProsequorBlob.Share>()));
        Assert.True(HusbandryFriendliness.TryGetFavoriteSeraph(animal, out string? before));
        Assert.Equal("old-fav", before);

        // Roll always passes; empty contributors → no change.
        HusbandryFriendliness.MaybeRefreshFavoriteSeraph(animal, new FixedRandom(0.0, 0));
        Assert.True(HusbandryFriendliness.TryGetFavoriteSeraph(animal, out string? afterEmpty));
        Assert.Equal("old-fav", afterEmpty);

        // Sentinel-only is also not pickable.
        ProsequorEntityPedigreeStation.AddContributor(
            animal, HusbandryFriendliness.AnonContributorUid, 10);
        HusbandryFriendliness.MaybeRefreshFavoriteSeraph(animal, new FixedRandom(0.0, 0));
        Assert.True(HusbandryFriendliness.TryGetFavoriteSeraph(animal, out string? afterAnon));
        Assert.Equal("old-fav", afterAnon);
    }

    [AtlasScenario]
    [Trait("Layer", "Action")]
    [Trait("Kind", "HusbandryFriendliness")]
    public async Task FavoriteSeraph_Should_NotRoll_WhenBelowOrAtThreshold()
    {
        ITestPlayer joined = await World.JoinPlayer("HSFavGate");
        Entity animal = joined.Player.Entity;

        HusbandryFriendliness.Add(animal, "alice", 5); // score == 5, not > 5
        Assert.Equal(5, HusbandryFriendliness.Get(animal));
        HusbandryFriendliness.StampFriendlinessReadyAt(animal, 0);

        HusbandryFriendliness.Add(animal, "alice", 1); // before was 5 → no roll
        Assert.Equal(6, HusbandryFriendliness.Get(animal));
        Assert.False(HusbandryFriendliness.TryGetFavoriteSeraph(animal, out _));
    }

    [AtlasScenario]
    [Trait("Layer", "Action")]
    [Trait("Kind", "HusbandryFriendliness")]
    public async Task MilestoneXp_Should_PayContributors_ThenWipeShares()
    {
        ITestPlayer joined = await World.JoinPlayer("HSMileWipe");
        Entity animal = joined.Player.Entity;

        ProsequorEntityPedigreeStation.StampMaker(animal, "fav-seraph");
        ProsequorEntityPedigreeStation.AddContributor(animal, "alice", 3);
        ProsequorEntityPedigreeStation.AddContributor(animal, "bob", 1);
        ProsequorEntityPedigreeStation.AddContributor(
            animal, HusbandryFriendliness.AnonContributorUid, 2);
        Assert.True(HusbandryFriendliness.Get(animal) > HusbandryFriendliness.FriendlyThreshold);

        IReadOnlyList<Deed.ContributorShare> shares =
            HusbandryContributorXp.RealContributorShares(
                ProsequorEntityPedigreeStation.TryGetBlob(animal, out ProsequorBlob before)
                    ? before
                    : ProsequorBlob.Empty);
        Assert.Equal(2, shares.Count);
        Assert.DoesNotContain(shares, s => s.PlayerUid.StartsWith('@'));

        Assert.True(HusbandryContributorXp.TrySettleAndWipe(animal, DeedTokenTags.AgedUp));
        Assert.Equal(0, HusbandryFriendliness.Get(animal));
        Assert.True(HusbandryFriendliness.TryGetFavoriteSeraph(animal, out string? fav));
        Assert.Equal("fav-seraph", fav);
        Assert.True(ProsequorEntityPedigreeStation.TryGetBlob(animal, out ProsequorBlob after));
        Assert.Empty(after.Contributors);

        // Not friendly → no wipe of a fresh bag.
        ProsequorEntityPedigreeStation.AddContributor(animal, "carol", 2);
        Assert.False(HusbandryContributorXp.TrySettleAndWipe(animal, DeedTokenTags.GaveBirth));
        Assert.Equal(2, HusbandryFriendliness.Get(animal));
    }

    [AtlasScenario]
    [Trait("Layer", "Action")]
    [Trait("Kind", "HusbandryFriendliness")]
    public async Task CropEat_Should_CreditPlanter_OrMiss_AndHonorCooldown()
    {
        ITestPlayer joined = await World.JoinPlayer("HSCropEat");
        Entity animal = joined.Player.Entity;

        Assert.False(CropEatStation.TryGainFriendliness(animal, "planter", new FixedRandom(1.0, 0)));
        Assert.Equal(0, HusbandryFriendliness.Get(animal));
        Assert.False(ProsequorEntityPedigreeStation.TryGetBlob(animal, out _));

        Assert.True(CropEatStation.TryGainFriendliness(animal, "planter", new FixedRandom(0.0, 0)));
        Assert.Equal(1, HusbandryFriendliness.Get(animal));
        Assert.True(ProsequorEntityPedigreeStation.TryGetBlob(animal, out ProsequorBlob blob));
        Assert.True(blob.TryGetContributorWeight("planter", out int weight));
        Assert.Equal(1, weight);

        Assert.False(CropEatStation.TryGainFriendliness(animal, "other", new FixedRandom(0.0, 0)));
        Assert.Equal(1, HusbandryFriendliness.Get(animal));
        Assert.False(
            ProsequorEntityPedigreeStation.TryGetBlob(animal, out ProsequorBlob gated)
            && gated.TryGetContributorWeight("other", out _));
    }

    [AtlasScenario(FreshWorld = true)]
    [Trait("Layer", "Action")]
    [Trait("Kind", "HusbandryFriendliness")]
    public async Task LooseItemEat_Should_CreditDropper_AndIgnoreBlankUid()
    {
        ITestPlayer joined = await World.JoinPlayer("HSLooseEat");
        Entity animal = joined.Player.Entity;

        Assert.False(LooseItemEatStation.TryGainFriendliness(animal, null, new FixedRandom(0.0, 0)));
        Assert.False(LooseItemEatStation.TryGainFriendliness(animal, "  ", new FixedRandom(0.0, 0)));
        Assert.Equal(0, HusbandryFriendliness.Get(animal));
        Assert.False(ProsequorEntityPedigreeStation.TryGetBlob(animal, out _));

        Assert.False(LooseItemEatStation.TryGainFriendliness(animal, "dropper", new FixedRandom(1.0, 0)));
        Assert.Equal(0, HusbandryFriendliness.Get(animal));

        Assert.True(LooseItemEatStation.TryGainFriendliness(animal, "dropper", new FixedRandom(0.0, 0)));
        Assert.Equal(1, HusbandryFriendliness.Get(animal));
        Assert.True(ProsequorEntityPedigreeStation.TryGetBlob(animal, out ProsequorBlob blob));
        Assert.True(blob.TryGetContributorWeight("dropper", out int weight));
        Assert.Equal(1, weight);
        Assert.False(blob.TryGetContributorWeight(HusbandryFriendliness.AnonContributorUid, out _));

        Assert.False(LooseItemEatStation.TryGainFriendliness(animal, "other", new FixedRandom(0.0, 0)));
        Assert.Equal(1, HusbandryFriendliness.Get(animal));
        Assert.False(
            ProsequorEntityPedigreeStation.TryGetBlob(animal, out ProsequorBlob gated)
            && gated.TryGetContributorWeight("other", out _));
    }

    [AtlasScenario]
    [Trait("Layer", "Action")]
    [Trait("Kind", "HusbandryFriendliness")]
    public async Task Attack_Should_StepDown_AndClearFavorite_OnlyWhenAttackerMatches()
    {
        ITestPlayer joined = await World.JoinPlayer("HSAttackPen");
        Entity animal = joined.Player.Entity;

        HusbandryFriendliness.Set(animal, 12);
        ProsequorEntityPedigreeStation.StampMaker(animal, "fav-seraph");

        HusbandryFriendliness.ApplyAttackPenalty(animal, "fav-seraph");
        Assert.Equal(5, HusbandryFriendliness.Get(animal));
        Assert.False(HusbandryFriendliness.IsFriendly(animal));
        Assert.True(HusbandryFriendliness.TryGetFavoriteSeraph(animal, out string? kept));
        Assert.Equal("fav-seraph", kept);

        HusbandryFriendliness.ApplyAttackPenalty(animal, "someone-else");
        Assert.Equal(0, HusbandryFriendliness.Get(animal));
        Assert.True(HusbandryFriendliness.TryGetFavoriteSeraph(animal, out string? still));
        Assert.Equal("fav-seraph", still);

        HusbandryFriendliness.Set(animal, 5);
        ProsequorEntityPedigreeStation.StampMaker(animal, "fav-seraph");
        HusbandryFriendliness.ApplyAttackPenalty(animal, "fav-seraph");
        Assert.Equal(0, HusbandryFriendliness.Get(animal));
        Assert.False(HusbandryFriendliness.TryGetFavoriteSeraph(animal, out _));
    }

    [AtlasScenario(FreshWorld = true)]
    [Trait("Layer", "Action")]
    [Trait("Kind", "HusbandryFriendliness")]
    public async Task FeedAnimal_Should_PayOnlyWhenAlreadyFriendly_AndSkipBlankPayer()
    {
        ITestPlayer joined = await World.JoinPlayer("HSFeedXp");
        IPlayer player = joined.Player;
        Entity animal = player.Entity;
        EntityBehaviorProgress? progress = player.Entity.GetBehavior<EntityBehaviorProgress>();
        Assert.NotNull(progress);

        HusbandryFriendliness.Set(animal, 6);
        float before = HusbandryTotal(progress);
        AnimalFeedXp.Emit(
            animal.Api,
            animal,
            player.PlayerUID,
            CallerIdentities.Loose,
            "game:grain",
            position: null);
        float afterFriendly = HusbandryTotal(progress);
        Assert.True(
            afterFriendly - before >= 0.099f,
            $"Expected ~0.1 husbandry XP from a friendly feed, got {afterFriendly - before}.");

        AnimalFeedXp.Emit(
            animal.Api,
            animal,
            payerUid: null,
            CallerIdentities.Loose,
            "game:grain",
            position: null);
        Assert.Equal(afterFriendly, HusbandryTotal(progress));

        HusbandryFriendliness.Set(animal, 5);
        AnimalFeedXp.Emit(
            animal.Api,
            animal,
            player.PlayerUID,
            CallerIdentities.Loose,
            "game:grain",
            position: null);
        Assert.Equal(afterFriendly, HusbandryTotal(progress));
    }

    /// <summary>
    /// Deterministic Random: <see cref="Next(int)"/> and range offset both use <c>nextInt</c>.
    /// </summary>
    sealed class FixedRandom(double nextDouble, int nextInt) : Random
    {
        public override double NextDouble() => nextDouble;

        public override int Next(int maxValue) => nextInt;

        public override int Next(int minValue, int maxValue) => minValue + nextInt;
    }

    static float HusbandryTotal(EntityBehaviorProgress progress)
    {
        SkillProgressState skill = progress.State.GetOrCreateSkill(Skill);
        return progress.GetSkillXp(Skill) + skill.Accrued;
    }

    static IPlayerProgress RequireProgress(IPlayer player)
    {
        IPlayerProgress? progress = ProsequorModSystem.GetProgress(player);
        Assert.NotNull(progress);
        return progress!;
    }

    static void GrantRancherBranch(IPlayerProgress progress)
    {
        progress.AddUnlockPoints(20);
        progress.SetPlayerLevel(30);
        progress.SetSkillLevel(Skill, 40);
        Assert.True(progress.GrantUnlock(Skill, "calm-hives"));
        Assert.True(progress.GrantUnlock(Skill, "gentle-spirit"));
        Assert.True(progress.GrantUnlock(Skill, "feedhand"));
        Assert.True(progress.GrantUnlock(Skill, "rancher"));
    }

    static void ExpectFearScale(float vanilla, int friendliness, int multPercent, float expected)
    {
        float actual = HusbandryFriendliness.ScaleFearReductionFactor(
            vanilla,
            friendliness,
            HusbandryFriendliness.MultFromPercent(multPercent));
        Assert.Equal(expected, actual, precision: 4);
    }

    static void ExpectMilkScale(float aggro, int friendliness, int multPercent, float expected)
    {
        float actual = HusbandryFriendliness.ScaleMilkingAggroChance(
            aggro,
            friendliness,
            HusbandryFriendliness.MultFromPercent(multPercent));
        Assert.Equal(expected, actual, precision: 4);
    }
}
