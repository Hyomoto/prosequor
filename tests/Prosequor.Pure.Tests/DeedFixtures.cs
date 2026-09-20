using Prosequor.Ability;
using Prosequor.Ability.Hooks;
using Prosequor.Data;
using Prosequor.Xp;
using Prosequor.Xp.Activity;
using Prosequor.Xp.Adapters;
using Vintagestory.API.Common;
using Xunit;

namespace Prosequor.Pure.Tests;

/// <summary>Pure fixtures for deed Emit planning, tokens, tables, and tagless warn.</summary>
public static class DeedFixtures
{
    public static void VerifyAll()
    {
        VerifyDeedTokenTags();
        VerifyCallerReservedParseAndMatch();
        VerifyOmitCallerNormalizesToHand();
        VerifyMultiSkillPlanPays();
        VerifyBombCallerPaysMining();
        VerifyAmountTableMetric();
        VerifyLifetimePay();
        VerifyFlatIgnoresQuantity();
        VerifyMissingResistanceUsesTableZero();
        VerifyMissingLifetimeUsesTableZeroOverStages();
        VerifyPayCompileRejects();
        VerifyQuantityExclude();
        VerifyDomesticatedHarvestQuantity();
        VerifyPayeeResolution();
        VerifyFedAnimalDeed();
        VerifyOfflineMailboxFromPlan();
        VerifyTaglessDeedWarns();
        VerifyTailoringClothLeatherAndHidePays();
    }

    static void VerifyDeedTokenTags()
    {
        if (DeedToken.BlockBroken.ToTag() != DeedTokenTags.BlockBroken
            || DeedToken.Crafted.ToTag() != DeedTokenTags.Crafted
            || DeedToken.Crafting.ToTag() != DeedTokenTags.Crafting
            || DeedToken.KilnFired.ToTag() != DeedTokenTags.KilnFired
            || DeedToken.Grown.ToTag() != DeedTokenTags.Grown
            || !DeedTokenTags.TryParse("fishing-catch", out DeedToken t)
            || t != DeedToken.FishingCatch
            || !DeedTokenTags.TryParse("saddle-break", out DeedToken s)
            || s != DeedToken.SaddleBreak
            || !DeedTokenTags.TryParse("grown", out DeedToken g)
            || g != DeedToken.Grown
            || !DeedTokenTags.TryParse("crop-grown", out DeedToken legacy)
            || legacy != DeedToken.Grown
            || DeedToken.TillSoil.ToTag() != DeedTokenTags.TillSoil
            || !DeedTokenTags.TryParse("till-soil", out DeedToken till)
            || till != DeedToken.TillSoil
            || DeedToken.Butchered.ToTag() != DeedTokenTags.Butchered
            || !DeedTokenTags.TryParse("butchered", out DeedToken butcher)
            || butcher != DeedToken.Butchered
            || DeedToken.FertilizerAbsorbed.ToTag() != DeedTokenTags.FertilizerAbsorbed
            || !DeedTokenTags.TryParse("fertilizer-absorbed", out DeedToken fert)
            || fert != DeedToken.FertilizerAbsorbed
            || DeedToken.FedAnimal.ToTag() != DeedTokenTags.FedAnimal
            || !DeedTokenTags.TryParse("fed-animal", out DeedToken fed)
            || fed != DeedToken.FedAnimal
            || !DeedTokenTags.TryParse("trough-eaten", out DeedToken troughAlias)
            || troughAlias != DeedToken.FedAnimal
            || DeedTokenTags.Canonical("trough-eaten") != DeedTokenTags.FedAnimal
            || Deed.Activity != "prosequor:deed")
        {
            Assert.Fail("[prosequor] DeedToken tag map failed.");
        }
    }

    static void VerifyCallerReservedParseAndMatch()
    {
        if (EventFactBuilder.CallerOrHand((IPlayer?)null) != CallerIdentities.Hand
            || EventFactBuilder.CallerOrHand((ItemStack?)null) != CallerIdentities.Hand)
        {
            Assert.Fail("[prosequor] empty harvest caller should be @hand.");
        }

        CollectionIndex collections = new();
        collections.EnsureKey("shovel");
        collections.AddCode("shovel", "game:shovel-copper");
        collections.EnsureKey("knife");
        collections.AddCode("knife", "game:knife-generic-flint");

        if (!TagCriterionParser.TryParse("caller:@hand", collections, out TagCriterion? hand, out _)
            || hand is not RoleIdentityCriterion handId
            || handId.Role != FactRole.Caller
            || handId.Identity != CallerIdentities.Hand)
        {
            Assert.Fail("[prosequor] caller:@hand should parse as reserved identity.");
        }

        if (!TagCriterionParser.TryParse("caller:@grid", collections, out TagCriterion? grid, out _)
            || grid is not RoleIdentityCriterion)
        {
            Assert.Fail("[prosequor] caller:@grid should parse.");
        }

        if (!TagCriterionParser.TryParse("caller:@trough", collections, out TagCriterion? trough, out _)
            || trough is not RoleIdentityCriterion troughId
            || troughId.Identity != CallerIdentities.Trough
            || !TagCriterionParser.TryParse("caller:@crop", collections, out TagCriterion? crop, out _)
            || crop is not RoleIdentityCriterion cropId
            || cropId.Identity != CallerIdentities.Crop
            || !TagCriterionParser.TryParse("caller:@loose", collections, out TagCriterion? loose, out _)
            || loose is not RoleIdentityCriterion looseId
            || looseId.Identity != CallerIdentities.Loose)
        {
            Assert.Fail("[prosequor] feed callers @trough / @crop / @loose should parse.");
        }

        if (!TagCriterionParser.TryParse("held:<shovel>", collections, out TagCriterion? heldAlias, out _)
            || heldAlias is not RoleCollectionCriterion heldCol
            || heldCol.Role != FactRole.Caller)
        {
            Assert.Fail("[prosequor] held:<shovel> alias should compile to caller collection.");
        }

        if (TagCriterionParser.TryParse("caller:@foo", collections, out _, out string bogusError)
            || string.IsNullOrWhiteSpace(bogusError)
            || !bogusError.Contains("@foo", StringComparison.OrdinalIgnoreCase))
        {
            Assert.Fail("[prosequor] caller:@foo should reject unknown reserved identity.");
        }

        if (TagCriterionParser.TryParse("target:@hand", collections, out _, out _))
        {
            Assert.Fail("[prosequor] @hand must not be valid on target.");
        }

        AbilityAction handFact = new()
        {
            Verb = Deed.Activity,
            ActorUid = "p",
            Caller = CallerIdentities.Hand
        };
        AbilityAction shovelFact = new()
        {
            Verb = Deed.Activity,
            ActorUid = "p",
            Caller = "game:shovel-copper"
        };

        if (!hand.Matches(handFact, collections) || hand.Matches(shovelFact, collections))
        {
            Assert.Fail("[prosequor] caller:@hand match failed.");
        }

        if (!TagCriterionParser.TryParse("caller:<knife>", collections, out TagCriterion? knife, out _)
            || knife is not RoleCollectionCriterion knifeCol
            || knifeCol.Role != FactRole.Caller)
        {
            Assert.Fail("[prosequor] caller:<knife> should compile to a caller collection.");
        }

        AbilityAction knifeFact = new()
        {
            Verb = Deed.Activity,
            ActorUid = "p",
            Caller = "game:knife-generic-flint"
        };
        if (!knife.Matches(knifeFact, collections)
            || knife.Matches(handFact, collections)
            || hand.Matches(knifeFact, collections))
        {
            Assert.Fail("[prosequor] caller:<knife> vs caller:@hand must be distinguishable.");
        }

        if (!heldAlias.Matches(shovelFact, collections) || heldAlias.Matches(handFact, collections))
        {
            Assert.Fail("[prosequor] held:<shovel> alias match failed.");
        }
    }

    static void VerifyOmitCallerNormalizesToHand()
    {
        CollectionIndex collections = new();
        XpRule handOnly = AmountRule(
            "hand-only",
            "digging",
            2f,
            order: 1,
            collections,
            tags: ["caller:@hand"]);

        FixedAmountRules rules = new(handOnly);
        IReadOnlyList<Deed.PlannedPay> pays = Deed.PlanPays(
            rules,
            collections,
            "player-1",
            new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            caller: null,
            target: "game:soil-low-none",
            mount: null,
            ground: null,
            lastCraft: null,
            metric: 0f,
            metricMin: 0f,
            metricMax: 0f,
            totalUnits: 0,
            craftCount: 1);

        if (pays.Count != 1
            || pays[0].Fact.Caller != CallerIdentities.Hand)
        {
            Assert.Fail("[prosequor] Deed omit caller should normalize to @hand.");
        }
    }

    static void VerifyMultiSkillPlanPays()
    {
        CollectionIndex collections = new();
        collections.EnsureKey("soil");
        collections.EnsureKey("shovel");
        collections.EnsureKey("wood");
        collections.EnsureKey("axe");
        collections.AddCode("soil", "game:soil-low-none");
        collections.AddCode("shovel", "game:shovel-copper");
        collections.AddCode("wood", "game:log-oak");
        collections.AddCode("axe", "game:axe-copper");

        XpRule dig = AmountRule(
            "dig",
            "digging",
            3f,
            order: 1,
            collections,
            tags: ["block-broken", "target:<soil>", "caller:<shovel>"]);
        XpRule alsoDig = AmountRule(
            "also-dig",
            "mining",
            9f,
            order: 2,
            collections,
            tags: ["block-broken", "target:<soil>", "caller:<shovel>"]);
        XpRule miss = AmountRule(
            "miss",
            "forestry",
            100f,
            order: 3,
            collections,
            tags: ["block-broken", "target:<wood>", "caller:<axe>"]);

        FixedAmountRules rules = new(dig, alsoDig, miss);
        IReadOnlyList<Deed.PlannedPay> pays = Deed.PlanPays(
            rules,
            collections,
            "player-1",
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { DeedTokenTags.BlockBroken },
            caller: "game:shovel-copper",
            target: "game:soil-low-none",
            mount: null,
            ground: null,
            lastCraft: null,
            metric: 0f,
            metricMin: 0f,
            metricMax: 0f,
            totalUnits: 0,
            craftCount: 1);

        if (pays.Count != 2
            || pays.All(p => p.SkillId != "digging")
            || pays.All(p => p.SkillId != "mining")
            || pays.Any(p => p.SkillId == "forestry"))
        {
            Assert.Fail("[prosequor] Deed PlanPays should pay every matching skill.");
        }
    }

    static void VerifyBombCallerPaysMining()
    {
        CollectionIndex collections = new();
        collections.EnsureKey("stone");
        collections.EnsureKey("ore");
        collections.EnsureKey("gemstone");
        collections.EnsureKey("pickaxe");
        collections.EnsureKey("bombs");
        collections.AddCode("ore", "game:ore-bituminouscoal-rich-granite");
        collections.AddCode("pickaxe", "game:pickaxe-copper");
        collections.AddCode("bombs", "game:bomb-ore");

        XpRule mine = AmountRule(
            "mine-mining",
            "mining",
            3f,
            order: 1,
            collections,
            tags: ["block-broken", "target:<stone,ore,gemstone>", "caller:<pickaxe,bombs>"]);

        FixedAmountRules rules = new(mine);
        IReadOnlyList<Deed.PlannedPay> bomb = Deed.PlanPays(
            rules,
            collections,
            "player-1",
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { DeedTokenTags.BlockBroken },
            caller: "game:bomb-ore",
            target: "game:ore-bituminouscoal-rich-granite",
            mount: null,
            ground: null,
            lastCraft: null,
            metric: 0f,
            metricMin: 0f,
            metricMax: 0f,
            totalUnits: 0,
            craftCount: 1);
        IReadOnlyList<Deed.PlannedPay> pickaxe = Deed.PlanPays(
            rules,
            collections,
            "player-1",
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { DeedTokenTags.BlockBroken },
            caller: "game:pickaxe-copper",
            target: "game:ore-bituminouscoal-rich-granite",
            mount: null,
            ground: null,
            lastCraft: null,
            metric: 0f,
            metricMin: 0f,
            metricMax: 0f,
            totalUnits: 0,
            craftCount: 1);
        IReadOnlyList<Deed.PlannedPay> hand = Deed.PlanPays(
            rules,
            collections,
            "player-1",
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { DeedTokenTags.BlockBroken },
            caller: CallerIdentities.Hand,
            target: "game:ore-bituminouscoal-rich-granite",
            mount: null,
            ground: null,
            lastCraft: null,
            metric: 0f,
            metricMin: 0f,
            metricMax: 0f,
            totalUnits: 0,
            craftCount: 1);

        if (bomb.Count != 1
            || bomb[0].SkillId != "mining"
            || pickaxe.Count != 1
            || pickaxe[0].SkillId != "mining"
            || hand.Count != 0)
        {
            Assert.Fail("[prosequor] Mining block-broken should pay pickaxe and bombs, not @hand.");
        }
    }

    static void VerifyAmountTableMetric()
    {
        CollectionIndex collections = new();
        List<TagCriterion> brokenCriteria = [new TokenCriterion { Token = DeedTokenTags.BlockBroken }];
        XpRule tableRule = new()
        {
            Id = "table",
            Activity = Deed.Activity,
            SkillId = "mining",
            Amount = 0f,
            AmountTable = [1f, 5f, 10f],
            Rate = 0f,
            Pay = XpPayChannel.Resistance,
            Criteria = brokenCriteria,
            Priority = 0,
            SourceOrder = 1,
            MatchScore = XpRuleMatcher.Score(brokenCriteria, 0, 1)
        };

        FixedAmountRules rules = new(tableRule);
        IReadOnlyList<Deed.PlannedPay> low = Deed.PlanPays(
            rules,
            collections,
            "p",
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { DeedTokenTags.BlockBroken },
            caller: CallerIdentities.Hand,
            target: null,
            mount: null,
            ground: null,
            lastCraft: null,
            metric: 1f,
            metricMin: 1f,
            metricMax: 10f,
            totalUnits: 0,
            craftCount: 1,
            metricDomain: BlockBreakHardnessCatalog.DomainMine);
        IReadOnlyList<Deed.PlannedPay> high = Deed.PlanPays(
            rules,
            collections,
            "p",
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { DeedTokenTags.BlockBroken },
            caller: CallerIdentities.Hand,
            target: null,
            mount: null,
            ground: null,
            lastCraft: null,
            metric: 10f,
            metricMin: 1f,
            metricMax: 10f,
            totalUnits: 0,
            craftCount: 1,
            metricDomain: BlockBreakHardnessCatalog.DomainMine);

        if (low.Count != 1 || low[0].Amount != 1f || high.Count != 1 || high[0].Amount != 10f)
        {
            Assert.Fail("[prosequor] Deed amount table metric mapping failed.");
        }
    }

    static void VerifyLifetimePay()
    {
        CollectionIndex collections = new();
        List<TagCriterion> grownCriteria =
        [
            new TokenCriterion { Token = DeedTokenTags.Grown },
            new TokenCriterion { Token = HarvestXp.TokenDomesticated }
        ];
        XpRule lifetimeRule = new()
        {
            Id = "crop-life",
            Activity = Deed.Activity,
            SkillId = "farming",
            Amount = 0f,
            AmountTable = [2f, 7f, 20f],
            Rate = 0f,
            Pay = XpPayChannel.Lifetime,
            Criteria = grownCriteria,
            Priority = 0,
            SourceOrder = 1,
            MatchScore = XpRuleMatcher.Score(grownCriteria, 0, 1)
        };

        FixedAmountRules rules = new(lifetimeRule);
        HashSet<string> tokens = new(StringComparer.OrdinalIgnoreCase)
        {
            DeedTokenTags.Grown,
            HarvestXp.TokenDomesticated
        };

        // Min knot 2 / 5 stages = 0.4
        IReadOnlyList<Deed.PlannedPay> low = Deed.PlanPays(
            rules,
            collections,
            "p",
            tokens,
            caller: CallerIdentities.Hand,
            target: "game:crop-turnip-2",
            mount: null,
            ground: null,
            lastCraft: null,
            metric: 9f,
            metricMin: 9f,
            metricMax: 30f,
            totalUnits: 0,
            craftCount: 1,
            metricDomain: Deed.MetricDomainCropLifetime,
            growthStages: 5);
        // Mid of [9,30] → knot 7 / 5 = 1.4
        IReadOnlyList<Deed.PlannedPay> mid = Deed.PlanPays(
            rules,
            collections,
            "p",
            tokens,
            caller: CallerIdentities.Hand,
            target: "game:crop-carrot-2",
            mount: null,
            ground: null,
            lastCraft: null,
            metric: 19.5f,
            metricMin: 9f,
            metricMax: 30f,
            totalUnits: 0,
            craftCount: 1,
            metricDomain: Deed.MetricDomainCropLifetime,
            growthStages: 5);
        // Max knot 20 / 5 = 4
        IReadOnlyList<Deed.PlannedPay> high = Deed.PlanPays(
            rules,
            collections,
            "p",
            tokens,
            caller: CallerIdentities.Hand,
            target: "game:crop-pineapple-2",
            mount: null,
            ground: null,
            lastCraft: null,
            metric: 30f,
            metricMin: 9f,
            metricMax: 30f,
            totalUnits: 0,
            craftCount: 1,
            metricDomain: Deed.MetricDomainCropLifetime,
            growthStages: 5);
        // Scalar lifetime 10 / 5 stages = 2 (table path with constant via ResolveAmount)
        XpRule scalarLife = new()
        {
            Id = "crop-life-scalar",
            Activity = Deed.Activity,
            SkillId = "farming",
            Amount = 10f,
            AmountTable = null,
            Rate = 0f,
            Pay = XpPayChannel.Lifetime,
            Criteria = grownCriteria,
            Priority = 0,
            SourceOrder = 1,
            MatchScore = XpRuleMatcher.Score(grownCriteria, 0, 1)
        };
        IReadOnlyList<Deed.PlannedPay> split = Deed.PlanPays(
            new FixedAmountRules(scalarLife),
            collections,
            "p",
            tokens,
            caller: CallerIdentities.Hand,
            target: "game:crop-carrot-2",
            mount: null,
            ground: null,
            lastCraft: null,
            metric: 15f,
            metricMin: 9f,
            metricMax: 30f,
            totalUnits: 0,
            craftCount: 1,
            metricDomain: Deed.MetricDomainCropLifetime,
            growthStages: 5);

        if (low.Count != 1
            || Math.Abs(low[0].Amount - 0.4f) > 0.0001f
            || mid.Count != 1
            || Math.Abs(mid[0].Amount - 1.4f) > 0.0001f
            || high.Count != 1
            || Math.Abs(high[0].Amount - 4f) > 0.0001f
            || split.Count != 1
            || Math.Abs(split[0].Amount - 2f) > 0.0001f)
        {
            Assert.Fail(
                $"[prosequor] Lifetime pay failed: low={Fmt(low)} mid={Fmt(mid)} high={Fmt(high)} split={Fmt(split)}.");
        }

        static string Fmt(IReadOnlyList<Deed.PlannedPay> pays) =>
            pays.Count == 0 ? "none" : pays[0].Amount.ToString("0.###");
    }

    static void VerifyMissingLifetimeUsesTableZeroOverStages()
    {
        CollectionIndex collections = new();
        List<TagCriterion> grownCriteria = [new TokenCriterion { Token = DeedTokenTags.Grown }];
        XpRule tableRule = new()
        {
            Id = "life-miss",
            Activity = Deed.Activity,
            SkillId = "farming",
            Amount = 0f,
            AmountTable = [2f, 7f, 20f],
            Rate = 0f,
            Pay = XpPayChannel.Lifetime,
            Criteria = grownCriteria,
            Priority = 0,
            SourceOrder = 1,
            MatchScore = XpRuleMatcher.Score(grownCriteria, 0, 1)
        };

        // No lifetime channel — amount[0]=2 / 5 stages = 0.4
        Deed.Channels emptyLife = new(
            Resistance: 0f,
            ResistanceMin: 0f,
            ResistanceMax: 0f,
            HasResistance: false,
            Voxels: 0f,
            VoxelsMin: 0f,
            VoxelsMax: 0f,
            HasVoxels: false,
            Quantity: 1,
            Ingredients: 0,
            GrowthStages: 5);

        IReadOnlyList<Deed.PlannedPay> pays = Deed.PlanPays(
            new FixedAmountRules(tableRule),
            collections,
            "p",
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { DeedTokenTags.Grown },
            caller: CallerIdentities.Hand,
            target: null,
            mount: null,
            ground: null,
            lastCraft: null,
            emptyLife);

        if (pays.Count != 1 || Math.Abs(pays[0].Amount - 0.4f) > 0.0001f)
        {
            Assert.Fail("[prosequor] Missing lifetime should fall back to amount[0] / stages.");
        }
    }

    static void VerifyFlatIgnoresQuantity()
    {
        CollectionIndex collections = new();
        XpRule flat = AmountRule(
            "flat-fish",
            "fishing",
            5f,
            order: 1,
            collections,
            tags: ["fishing-catch"]);

        FixedAmountRules rules = new(flat);
        IReadOnlyList<Deed.PlannedPay> pays = Deed.PlanPays(
            rules,
            collections,
            "p",
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { DeedTokenTags.FishingCatch },
            caller: CallerIdentities.Hand,
            target: null,
            mount: null,
            ground: null,
            lastCraft: null,
            metric: 9f,
            metricMin: 1f,
            metricMax: 10f,
            totalUnits: 12,
            craftCount: 7);

        if (pays.Count != 1 || Math.Abs(pays[0].Amount - 5f) > 0.001f)
        {
            Assert.Fail("[prosequor] Omitted pay (flat) must ignore quantity/ingredients/metrics.");
        }

        if (flat.Pay != XpPayChannel.Flat)
        {
            Assert.Fail("[prosequor] AmountRule helper should default Pay to flat.");
        }
    }

    static void VerifyMissingResistanceUsesTableZero()
    {
        CollectionIndex collections = new();
        List<TagCriterion> brokenCriteria = [new TokenCriterion { Token = DeedTokenTags.BlockBroken }];
        XpRule tableRule = new()
        {
            Id = "table-miss",
            Activity = Deed.Activity,
            SkillId = "digging",
            Amount = 0f,
            AmountTable = [1f, 5f, 10f],
            Rate = 0f,
            Pay = XpPayChannel.Resistance,
            Criteria = brokenCriteria,
            Priority = 0,
            SourceOrder = 1,
            MatchScore = XpRuleMatcher.Score(brokenCriteria, 0, 1)
        };

        // Craft emit: no resistance channel published.
        Deed.Channels craftOnly = new(
            Resistance: 0f,
            ResistanceMin: 0f,
            ResistanceMax: 0f,
            HasResistance: false,
            Voxels: 0f,
            VoxelsMin: 0f,
            VoxelsMax: 0f,
            HasVoxels: false,
            Quantity: 3,
            Ingredients: 0);

        IReadOnlyList<Deed.PlannedPay> pays = Deed.PlanPays(
            new FixedAmountRules(tableRule),
            collections,
            "p",
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { DeedTokenTags.BlockBroken },
            caller: CallerIdentities.Hand,
            target: null,
            mount: null,
            ground: null,
            lastCraft: null,
            craftOnly);

        if (pays.Count != 1 || pays[0].Amount != 1f)
        {
            Assert.Fail("[prosequor] Missing resistance should fall back to amount[0].");
        }
    }

    static void VerifyPayCompileRejects()
    {
        CollectionIndex collections = new();

        if (XpRuleCompiler.TryCompile(
                new XpRuleJson
                {
                    id = "pay-array",
                    amount = new Newtonsoft.Json.Linq.JValue(1),
                    pay = new Newtonsoft.Json.Linq.JArray("ingredients", "quantity"),
                    when = new XpRuleWhenJson { activity = Deed.Activity }
                },
                "fixture",
                1,
                collections,
                out _,
                out string payArrayError)
            || !payArrayError.Contains("single", StringComparison.OrdinalIgnoreCase))
        {
            Assert.Fail(string.Format(
                "[prosequor] pay array should reject: {0}",
                payArrayError));
        }

        if (XpRuleCompiler.TryCompile(
                new XpRuleJson
                {
                    id = "unknown-pay",
                    amount = new Newtonsoft.Json.Linq.JValue(1),
                    pay = new Newtonsoft.Json.Linq.JValue("drop"),
                    when = new XpRuleWhenJson { activity = Deed.Activity }
                },
                "fixture",
                2,
                collections,
                out _,
                out string unknownError)
            || !unknownError.Contains("unknown", StringComparison.OrdinalIgnoreCase))
        {
            Assert.Fail(string.Format(
                "[prosequor] unknown pay channel should reject: {0}",
                unknownError));
        }

        if (!XpRuleCompiler.TryCompile(
                new XpRuleJson
                {
                    id = "omit-pay",
                    amount = new Newtonsoft.Json.Linq.JValue(7),
                    when = new XpRuleWhenJson
                    {
                        activity = Deed.Activity,
                        tags = ["fishing-catch"]
                    }
                },
                "fixture",
                3,
                collections,
                out XpRule omitted,
                out string omitError)
            || omitted.Pay != XpPayChannel.Flat)
        {
            Assert.Fail(string.Format(
                "[prosequor] omitted pay should default to flat: {0}",
                omitError));
        }

        if (XpRuleCompiler.TryCompile(
                new XpRuleJson
                {
                    id = "exclude-no-quantity",
                    amount = new Newtonsoft.Json.Linq.JValue(1),
                    exclude = ["game:stick"],
                    when = new XpRuleWhenJson { activity = Deed.Activity }
                },
                "fixture",
                4,
                collections,
                out _,
                out string excludeError)
            || !excludeError.Contains("quantity", StringComparison.OrdinalIgnoreCase))
        {
            Assert.Fail(string.Format(
                "[prosequor] exclude without quantity should reject: {0}",
                excludeError));
        }

        if (!XpRuleCompiler.TryCompile(
                new XpRuleJson
                {
                    id = "lifetime-table",
                    amount = new Newtonsoft.Json.Linq.JArray(2, 7, 20),
                    pay = new Newtonsoft.Json.Linq.JValue("lifetime"),
                    when = new XpRuleWhenJson
                    {
                        activity = Deed.Activity,
                        tags = ["grown", "domesticated"]
                    }
                },
                "farming",
                5,
                collections,
                out XpRule lifetime,
                out string lifeError)
            || lifetime.Pay != XpPayChannel.Lifetime
            || lifetime.AmountTable == null
            || lifetime.AmountTable.Count != 3)
        {
            Assert.Fail(string.Format(
                "[prosequor] pay lifetime with amount table should compile: {0}",
                lifeError));
        }
    }

    static void VerifyQuantityExclude()
    {
        CollectionIndex collections = new();
        collections.EnsureKey("stick");
        collections.AddCode("stick", "game:stick");

        if (!XpRuleCompiler.TryCompile(
                new XpRuleJson
                {
                    id = "harvest-filtered",
                    amount = new Newtonsoft.Json.Linq.JValue(2),
                    pay = new Newtonsoft.Json.Linq.JValue("quantity"),
                    exclude = ["<stick>", "game:drygrass"],
                    when = new XpRuleWhenJson
                    {
                        activity = Deed.Activity,
                        tags = ["harvested"]
                    }
                },
                "farming",
                1,
                collections,
                out XpRule rule,
                out string compileError)
            || rule.Exclude.IsEmpty)
        {
            Assert.Fail(string.Format(
                "[prosequor] quantity exclude compile failed: {0}",
                compileError));
            return;
        }

        Deed.Channels parts = new(
            Resistance: 0f,
            ResistanceMin: 0f,
            ResistanceMax: 0f,
            HasResistance: false,
            Voxels: 0f,
            VoxelsMin: 0f,
            VoxelsMax: 0f,
            HasVoxels: false,
            Quantity: 5,
            Ingredients: 0,
            QuantityUnits:
            [
                new Deed.QuantityUnit("game:grain-spelt", 3),
                new Deed.QuantityUnit("game:stick", 1),
                new Deed.QuantityUnit("game:drygrass", 1)
            ]);

        IReadOnlyList<Deed.PlannedPay> filtered = Deed.PlanPays(
            new FixedAmountRules(rule),
            collections,
            "p",
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { DeedTokenTags.Harvested },
            caller: CallerIdentities.Hand,
            target: "game:crop-spelt",
            mount: null,
            ground: null,
            lastCraft: null,
            parts);

        // 3 grain × 2 XP = 6
        if (filtered.Count != 1 || Math.Abs(filtered[0].Amount - 6f) > 0.001f)
        {
            Assert.Fail(string.Format(
                "[prosequor] quantity exclude units filter failed, amount={0}",
                filtered.Count == 0 ? "none" : filtered[0].Amount.ToString()));
        }

        Deed.Channels targetGate = new(
            Resistance: 0f,
            ResistanceMin: 0f,
            ResistanceMax: 0f,
            HasResistance: false,
            Voxels: 0f,
            VoxelsMin: 0f,
            VoxelsMax: 0f,
            HasVoxels: false,
            Quantity: 4,
            Ingredients: 0);

        IReadOnlyList<Deed.PlannedPay> gated = Deed.PlanPays(
            new FixedAmountRules(rule),
            collections,
            "p",
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { DeedTokenTags.Harvested },
            caller: CallerIdentities.Hand,
            target: "game:stick",
            mount: null,
            ground: null,
            lastCraft: null,
            targetGate);

        if (gated.Count != 0)
        {
            Assert.Fail("[prosequor] exclude should zero quantity when target matches and no units list.");
        }
    }

    static void VerifyDomesticatedHarvestQuantity()
    {
        CollectionIndex collections = new();
        collections.EnsureKey("seed");
        collections.EnsureKey("crop");
        collections.EnsureKey("berry-bush");
        collections.EnsureKey("fruit-tree");
        collections.AddCode("seed", "game:seeds-carrot");
        collections.AddCode("crop", "game:crop-carrot-9");
        collections.AddCode("berry-bush", "game:fruitingbush-blueberry-ripe");

        if (!XpRuleCompiler.TryCompile(
                new XpRuleJson
                {
                    id = "harvest-domesticated",
                    amount = new Newtonsoft.Json.Linq.JValue(0.01),
                    pay = new Newtonsoft.Json.Linq.JValue("quantity"),
                    exclude = ["<seed>"],
                    when = new XpRuleWhenJson
                    {
                        activity = Deed.Activity,
                        tags = ["harvested", "domesticated", "target:<crop, berry-bush, fruit-tree>"]
                    }
                },
                "farming",
                1,
                collections,
                out XpRule rule,
                out string compileError))
        {
            Assert.Fail(string.Format(
                "[prosequor] domesticated harvest compile failed: {0}",
                compileError));
            return;
        }

        Deed.Channels parts = new(
            Resistance: 0f,
            ResistanceMin: 0f,
            ResistanceMax: 0f,
            HasResistance: false,
            Voxels: 0f,
            VoxelsMin: 0f,
            VoxelsMax: 0f,
            HasVoxels: false,
            Quantity: 1,
            Ingredients: 0,
            QuantityUnits:
            [
                new Deed.QuantityUnit("game:carrot", 4),
                new Deed.QuantityUnit("game:seeds-carrot", 2)
            ]);

        IReadOnlyList<Deed.PlannedPay> withDomesticated = Deed.PlanPays(
            new FixedAmountRules(rule),
            collections,
            "p",
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                DeedTokenTags.Harvested,
                HarvestXp.TokenDomesticated
            },
            caller: CallerIdentities.Hand,
            target: "game:crop-carrot-9",
            mount: null,
            ground: null,
            lastCraft: null,
            parts);

        if (withDomesticated.Count != 1 || Math.Abs(withDomesticated[0].Amount - 0.04f) > 0.0001f)
        {
            Assert.Fail(string.Format(
                "[prosequor] domesticated harvest should pay 0.04, got count={0} amount={1}",
                withDomesticated.Count,
                withDomesticated.Count > 0 ? withDomesticated[0].Amount : 0f));
        }

        IReadOnlyList<Deed.PlannedPay> wild = Deed.PlanPays(
            new FixedAmountRules(rule),
            collections,
            "p",
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { DeedTokenTags.Harvested },
            caller: CallerIdentities.Hand,
            target: "game:crop-carrot-9",
            mount: null,
            ground: null,
            lastCraft: null,
            parts);

        if (wild.Count != 0)
        {
            Assert.Fail("[prosequor] harvest without domesticated should not match.");
        }
    }

    static void VerifyPayeeResolution()
    {
        CollectionIndex collections = new();

        if (!XpRuleCompiler.TryCompile(
                new XpRuleJson
                {
                    id = "omit-payee",
                    amount = new Newtonsoft.Json.Linq.JValue(5),
                    when = new XpRuleWhenJson
                    {
                        activity = Deed.Activity,
                        tags = ["fishing-catch"]
                    }
                },
                "fishing",
                1,
                collections,
                out XpRule userRule,
                out string omitError)
            || userRule.Payee != XpPayee.User)
        {
            Assert.Fail(string.Format(
                "[prosequor] omitted payee should default to user: {0}",
                omitError));
            return;
        }

        if (XpRuleCompiler.TryCompile(
                new XpRuleJson
                {
                    id = "bad-payee-rate",
                    rate = 0.01f,
                    payee = "contributor",
                    when = new XpRuleWhenJson { activity = "prosequor:effort" }
                },
                "fishing",
                2,
                collections,
                out _,
                out string ratePayeeError)
            || !ratePayeeError.Contains("payee", StringComparison.OrdinalIgnoreCase))
        {
            Assert.Fail(string.Format(
                "[prosequor] payee on rate rule should reject: {0}",
                ratePayeeError));
            return;
        }

        if (!XpRuleCompiler.TryCompile(
                new XpRuleJson
                {
                    id = "selected-contributor",
                    amount = new Newtonsoft.Json.Linq.JValue(10),
                    payee = "contributor",
                    when = new XpRuleWhenJson
                    {
                        activity = Deed.Activity,
                        tags = ["trough-eaten"]
                    }
                },
                "husbandry",
                3,
                collections,
                out XpRule contributorRule,
                out string contribError)
            || contributorRule.Payee != XpPayee.Contributor)
        {
            Assert.Fail(string.Format(
                "[prosequor] contributor payee compile failed: {0}",
                contribError));
            return;
        }

        if (!XpRuleCompiler.TryCompile(
                new XpRuleJson
                {
                    id = "maker-payee",
                    amount = new Newtonsoft.Json.Linq.JValue(7),
                    payee = "maker",
                    when = new XpRuleWhenJson
                    {
                        activity = Deed.Activity,
                        tags = ["grown", "domesticated"]
                    }
                },
                "farming",
                5,
                collections,
                out XpRule makerRule,
                out string makerError)
            || makerRule.Payee != XpPayee.Maker)
        {
            Assert.Fail(string.Format(
                "[prosequor] maker payee compile failed: {0}",
                makerError));
            return;
        }

        if (!XpRuleCompiler.TryCompile(
                new XpRuleJson
                {
                    id = "all-contributors",
                    amount = new Newtonsoft.Json.Linq.JValue(10),
                    payee = "contributors",
                    when = new XpRuleWhenJson
                    {
                        activity = Deed.Activity,
                        tags = ["trough-eaten"]
                    }
                },
                "husbandry",
                4,
                collections,
                out XpRule contributorsRule,
                out string allError)
            || contributorsRule.Payee != XpPayee.Contributors)
        {
            Assert.Fail(string.Format(
                "[prosequor] contributors payee compile failed: {0}",
                allError));
            return;
        }

        Deed.Channels emptyShares = new(
            Resistance: 0f,
            ResistanceMin: 0f,
            ResistanceMax: 0f,
            HasResistance: false,
            Voxels: 0f,
            VoxelsMin: 0f,
            VoxelsMax: 0f,
            HasVoxels: false,
            Quantity: 1,
            Ingredients: 0);

        Deed.Channels withShares = emptyShares with
        {
            Contributors =
            [
                new Deed.ContributorShare("a", 3f),
                new Deed.ContributorShare("b", 1f),
                new Deed.ContributorShare("c", 1f)
            ],
            SelectedContributorUid = "b",
            MakerUid = "planter"
        };

        HashSet<string> tokens = new(StringComparer.OrdinalIgnoreCase) { "trough-eaten" };

        IReadOnlyList<Deed.PlannedPay> selectedOnly = Deed.PlanPays(
            new FixedAmountRules(contributorRule),
            collections,
            playerUid: "",
            tokens,
            caller: null,
            target: null,
            mount: null,
            ground: null,
            lastCraft: null,
            withShares);

        if (selectedOnly.Count != 1
            || selectedOnly[0].Fact.ActorUid != "b"
            || Math.Abs(selectedOnly[0].Amount - 10f) > 0.001f)
        {
            Assert.Fail(string.Format(
                "[prosequor] payee contributor should pay selected uid full grant, got count={0} uid={1} amt={2}",
                selectedOnly.Count,
                selectedOnly.Count > 0 ? selectedOnly[0].Fact.ActorUid : "-",
                selectedOnly.Count > 0 ? selectedOnly[0].Amount : 0f));
        }

        IReadOnlyList<Deed.PlannedPay> missingSelected = Deed.PlanPays(
            new FixedAmountRules(contributorRule),
            collections,
            playerUid: "actor",
            tokens,
            caller: null,
            target: null,
            mount: null,
            ground: null,
            lastCraft: null,
            emptyShares with { Contributors = withShares.Contributors });

        if (missingSelected.Count != 0)
        {
            Assert.Fail("[prosequor] payee contributor without selectedContributorUid should pay nobody.");
        }

        HashSet<string> grownTokens = new(StringComparer.OrdinalIgnoreCase)
        {
            "grown",
            "domesticated"
        };

        IReadOnlyList<Deed.PlannedPay> makerPays = Deed.PlanPays(
            new FixedAmountRules(makerRule),
            collections,
            playerUid: "",
            grownTokens,
            caller: null,
            target: null,
            mount: null,
            ground: null,
            lastCraft: null,
            withShares);

        if (makerPays.Count != 1
            || makerPays[0].Fact.ActorUid != "planter"
            || Math.Abs(makerPays[0].Amount - 7f) > 0.001f)
        {
            Assert.Fail("[prosequor] payee maker should pay emit makerUid.");
        }

        IReadOnlyList<Deed.PlannedPay> missingMaker = Deed.PlanPays(
            new FixedAmountRules(makerRule),
            collections,
            playerUid: "actor",
            grownTokens,
            caller: null,
            target: null,
            mount: null,
            ground: null,
            lastCraft: null,
            emptyShares);

        if (missingMaker.Count != 0)
        {
            Assert.Fail("[prosequor] payee maker without makerUid should pay nobody.");
        }

        IReadOnlyList<Deed.PlannedPay> weighted = Deed.PlanPays(
            new FixedAmountRules(contributorsRule),
            collections,
            playerUid: "",
            tokens,
            caller: null,
            target: null,
            mount: null,
            ground: null,
            lastCraft: null,
            withShares);

        if (weighted.Count != 3
            || Math.Abs(weighted.First(p => p.Fact.ActorUid == "a").Amount - 6f) > 0.001f
            || Math.Abs(weighted.First(p => p.Fact.ActorUid == "b").Amount - 2f) > 0.001f
            || Math.Abs(weighted.First(p => p.Fact.ActorUid == "c").Amount - 2f) > 0.001f)
        {
            Assert.Fail("[prosequor] payee contributors should split 3/1/1 → 6/2/2.");
        }

        IReadOnlyList<Deed.PlannedPay> emptyContrib = Deed.PlanPays(
            new FixedAmountRules(contributorsRule),
            collections,
            playerUid: "actor",
            tokens,
            caller: null,
            target: null,
            mount: null,
            ground: null,
            lastCraft: null,
            emptyShares);

        if (emptyContrib.Count != 0)
        {
            Assert.Fail("[prosequor] payee contributors with empty shares should pay nobody.");
        }

        IReadOnlyList<Deed.PlannedPay> blankUser = Deed.PlanPays(
            new FixedAmountRules(userRule),
            collections,
            playerUid: "",
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { DeedTokenTags.FishingCatch },
            caller: null,
            target: null,
            mount: null,
            ground: null,
            lastCraft: null,
            withShares);

        if (blankUser.Count != 0)
        {
            Assert.Fail("[prosequor] blank actor + payee user should skip.");
        }

        IReadOnlyList<(string PlayerUid, float Amount)> slices = Deed.ResolvePayees(
            XpPayee.Contributors,
            actorUid: null,
            makerUid: null,
            selectedContributorUid: null,
            contributors:
            [
                new Deed.ContributorShare("a", 3f),
                new Deed.ContributorShare("b", 1f),
                new Deed.ContributorShare("c", 1f)
            ],
            grant: 10f);

        if (slices.Count != 3
            || Math.Abs(slices[0].Amount - 6f) > 0.001f
            || Math.Abs(slices[1].Amount - 2f) > 0.001f
            || Math.Abs(slices[2].Amount - 2f) > 0.001f)
        {
            Assert.Fail("[prosequor] ResolvePayees weighted split failed.");
        }
    }

    static void VerifyFedAnimalDeed()
    {
        CollectionIndex collections = new();
        if (!TagCriterionParser.TryParse("trough-eaten", collections, out TagCriterion? alias, out string aliasError)
            || alias is not TokenCriterion aliasToken
            || aliasToken.Token != DeedTokenTags.FedAnimal)
        {
            Assert.Fail($"[prosequor] trough-eaten should compile as fed-animal: {aliasError}");
        }

        if (!TagCriterionParser.TryParse("caller:@loose", collections, out TagCriterion? loose, out _)
            || loose is not RoleIdentityCriterion looseId
            || looseId.Identity != CallerIdentities.Loose)
        {
            Assert.Fail("[prosequor] caller:@loose should parse.");
        }

        if (!AnimalFeedXp.IsRealPlayer("dropper")
            || AnimalFeedXp.IsRealPlayer(null)
            || AnimalFeedXp.IsRealPlayer("  ")
            || AnimalFeedXp.IsRealPlayer(HusbandryFriendliness.AnonContributorUid))
        {
            Assert.Fail("[prosequor] feed payer should reject blank and sentinel uids.");
        }

        if (!XpRuleCompiler.TryCompile(
                new XpRuleJson
                {
                    id = "feed",
                    amount = new Newtonsoft.Json.Linq.JValue(0.1),
                    payee = "contributor",
                    when = new XpRuleWhenJson
                    {
                        activity = Deed.Activity,
                        tags = ["fed-animal", "friendly", "caller:@loose", "input:game:grain"]
                    }
                },
                "husbandry",
                1,
                collections,
                out XpRule rule,
                out string compileError)
            || rule.Payee != XpPayee.Contributor)
        {
            Assert.Fail($"[prosequor] fed-animal rule should compile: {compileError}");
            return;
        }

        Deed.Channels channels = new(
            Resistance: 0f,
            ResistanceMin: 0f,
            ResistanceMax: 0f,
            HasResistance: false,
            Voxels: 0f,
            VoxelsMin: 0f,
            VoxelsMax: 0f,
            HasVoxels: false,
            Quantity: 1,
            Ingredients: 0,
            SelectedContributorUid: "dropper");

        HashSet<string> tokens = new(StringComparer.OrdinalIgnoreCase)
        {
            DeedTokenTags.FedAnimal,
            DeedTokenTags.Friendly
        };

        IReadOnlyList<Deed.PlannedPay> pays = Deed.PlanPays(
            new FixedAmountRules(rule),
            collections,
            playerUid: "",
            tokens,
            caller: CallerIdentities.Loose,
            target: "game:hare-female",
            mount: null,
            ground: null,
            lastCraft: null,
            channels,
            inputs: ["game:grain"]);

        if (pays.Count != 1
            || pays[0].Fact.ActorUid != "dropper"
            || Math.Abs(pays[0].Amount - 0.1f) > 0.001f
            || pays[0].Fact.Caller != CallerIdentities.Loose
            || pays[0].Fact.Inputs.Count != 1)
        {
            Assert.Fail("[prosequor] fed-animal should pay the dropper when caller and input match.");
        }

        IReadOnlyList<Deed.PlannedPay> wrongCaller = Deed.PlanPays(
            new FixedAmountRules(rule),
            collections,
            playerUid: "",
            tokens,
            caller: CallerIdentities.Trough,
            target: "game:hare-female",
            mount: null,
            ground: null,
            lastCraft: null,
            channels,
            inputs: ["game:grain"]);
        if (wrongCaller.Count != 0)
        {
            Assert.Fail("[prosequor] fed-animal caller filter should reject a trough meal.");
        }

        IReadOnlyList<Deed.PlannedPay> wrongFood = Deed.PlanPays(
            new FixedAmountRules(rule),
            collections,
            playerUid: "",
            tokens,
            caller: CallerIdentities.Loose,
            target: "game:hare-female",
            mount: null,
            ground: null,
            lastCraft: null,
            channels,
            inputs: ["game:cabbage"]);
        if (wrongFood.Count != 0)
        {
            Assert.Fail("[prosequor] fed-animal input filter should reject a different food.");
        }
    }

    static void VerifyOfflineMailboxFromPlan()
    {
        CollectionIndex collections = new();
        XpRule fired = AmountRule(
            "fired",
            "clayforming",
            4f,
            order: 1,
            collections,
            tags: ["kiln-fired"]);

        FixedAmountRules rules = new(fired);
        IReadOnlyList<Deed.PlannedPay> pays = Deed.PlanPays(
            rules,
            collections,
            "offline-maker",
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { DeedTokenTags.KilnFired },
            caller: DeedTokenTags.PitKiln,
            target: "game:bowl-blue",
            mount: null,
            ground: null,
            lastCraft: null,
            metric: 0f,
            metricMin: 0f,
            metricMax: 0f,
            totalUnits: 0,
            craftCount: 1);

        FatherXpMailbox mailbox = new();
        foreach (Deed.PlannedPay pay in pays)
        {
            mailbox.Enqueue(pay.Fact.ActorUid, pay.SkillId, pay.Amount);
        }

        if (!mailbox.TryTake("offline-maker", out IReadOnlyList<FatherXpGrant> grants)
            || grants.Count != 1
            || grants[0].SkillId != "clayforming"
            || Math.Abs(grants[0].Amount - 4f) > 0.001f)
        {
            Assert.Fail("[prosequor] Deed plan → mailbox offline path failed.");
        }
    }

    static void VerifyTaglessDeedWarns()
    {
        CollectionIndex collections = new();
        int order = 0;
        List<string> warnings = new();
        _ = XpRuleCompiler.CompileAll(
            [
                new XpRuleJson
                {
                    id = "warn-deed",
                    amount = new Newtonsoft.Json.Linq.JValue(1),
                    when = new XpRuleWhenJson { activity = Deed.Activity }
                }
            ],
            "fixture",
            collections,
            ref order,
            warnings.Add);

        if (warnings.Count == 0
            || warnings.TrueForAll(w => !w.Contains("no when.tags", StringComparison.Ordinal)))
        {
            Assert.Fail("[prosequor] Tagless deed amount rule should warn.");
        }
    }

    static void VerifyTailoringClothLeatherAndHidePays()
    {
        CollectionIndex collections = new();
        collections.EnsureKey("cloth");
        collections.EnsureKey("leather");
        collections.EnsureKey("hide");
        collections.AddCode("cloth", "game:linen-normal-down");
        collections.AddCode("cloth", "game:cloth-plain");
        collections.AddCode("leather", "game:leather-normal-plain");
        collections.AddCode("leather", "game:leather-sturdy-plain");
        collections.AddCode("hide", "game:hide-soaked-large");
        collections.AddCode("hide", "game:hide-scraped-large");
        collections.AddCode("hide", "game:hide-prepared-large");

        if (!TryCompileTailoringCraft(
                "prosequor:craft-cloth-tailoring",
                0.3f,
                "cloth",
                1,
                collections,
                out XpRule cloth,
                out string clothError))
        {
            Assert.Fail($"[prosequor] cloth tailoring compile failed: {clothError}");
            return;
        }

        if (!TryCompileTailoringCraft(
                "prosequor:craft-leather-tailoring",
                0.5f,
                "leather",
                2,
                collections,
                out XpRule leather,
                out string leatherError))
        {
            Assert.Fail($"[prosequor] leather tailoring compile failed: {leatherError}");
            return;
        }

        if (!TryCompileTailoringCraft(
                "prosequor:craft-hide-tailoring",
                0.2f,
                "hide",
                3,
                collections,
                out XpRule hide,
                out string hideError))
        {
            Assert.Fail($"[prosequor] hide tailoring compile failed: {hideError}");
            return;
        }

        FixedAmountRules rules = new(cloth, leather, hide);
        IReadOnlyList<Deed.PlannedPay> linen = PayGridCraft(
            rules, collections, "game:linen-normal-down", 1);
        IReadOnlyList<Deed.PlannedPay> clothPlain = PayGridCraft(
            rules, collections, "game:cloth-plain", 2);
        IReadOnlyList<Deed.PlannedPay> tanned = PayGridCraft(
            rules, collections, "game:leather-normal-plain", 3);
        IReadOnlyList<Deed.PlannedPay> soaked = PayGridCraft(
            rules, collections, "game:hide-soaked-large", 1);
        IReadOnlyList<Deed.PlannedPay> scraped = PayGridCraft(
            rules, collections, "game:hide-scraped-large", 1);
        IReadOnlyList<Deed.PlannedPay> prepared = PayGridCraft(
            rules, collections, "game:hide-prepared-large", 1);
        IReadOnlyList<Deed.PlannedPay> raw = PayGridCraft(
            rules, collections, "game:hide-raw-large", 1);

        if (linen.Count != 1 || Math.Abs(linen[0].Amount - 0.3f) > 0.001f
            || clothPlain.Count != 1 || Math.Abs(clothPlain[0].Amount - 0.6f) > 0.001f
            || tanned.Count != 1 || Math.Abs(tanned[0].Amount - 1.5f) > 0.001f
            || soaked.Count != 1 || Math.Abs(soaked[0].Amount - 0.2f) > 0.001f
            || scraped.Count != 1 || Math.Abs(scraped[0].Amount - 0.2f) > 0.001f
            || prepared.Count != 1 || Math.Abs(prepared[0].Amount - 0.2f) > 0.001f
            || raw.Count != 0)
        {
            Assert.Fail("[prosequor] tailoring cloth/leather/hide craft XP match failed.");
        }
    }

    static bool TryCompileTailoringCraft(
        string id,
        float amount,
        string collection,
        int order,
        CollectionIndex collections,
        out XpRule rule,
        out string error) =>
        XpRuleCompiler.TryCompile(
            new XpRuleJson
            {
                id = id,
                amount = new Newtonsoft.Json.Linq.JValue(amount),
                pay = new Newtonsoft.Json.Linq.JValue("quantity"),
                when = new XpRuleWhenJson
                {
                    activity = "prosequor:deed",
                    tags = ["crafted", "caller:@grid", $"target:<{collection}>"]
                }
            },
            "tailoring",
            order,
            collections,
            out rule,
            out error);

    static IReadOnlyList<Deed.PlannedPay> PayGridCraft(
        FixedAmountRules rules,
        CollectionIndex collections,
        string target,
        int quantity) =>
        Deed.PlanPays(
            rules,
            collections,
            "p",
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { DeedTokenTags.Crafted },
            caller: CallerIdentities.Grid,
            target: target,
            mount: null,
            ground: null,
            lastCraft: null,
            new Deed.Channels(
                0, 0, 0, false, 0, 0, 0, false,
                Quantity: quantity,
                Ingredients: 0,
                QuantityUnits: [new Deed.QuantityUnit(target, quantity)]));

    static XpRule AmountRule(
        string id,
        string skill,
        float amount,
        int order,
        CollectionIndex collections,
        string[] tags)
    {
        List<TagCriterion> criteria = new();
        foreach (string raw in tags)
        {
            if (!TagCriterionParser.TryParse(raw, collections, out TagCriterion? criterion, out string error)
                || criterion == null)
            {
                Assert.Fail($"[prosequor] Deed fixture tag parse failed: {raw} ({error})");
            }

            criteria.Add(criterion);
        }

        return new XpRule
        {
            Id = id,
            Activity = Deed.Activity,
            SkillId = skill,
            Amount = amount,
            Rate = 0f,
            Pay = XpPayChannel.Flat,
            Payee = XpPayee.User,
            Criteria = criteria,
            Priority = 0,
            SourceOrder = order,
            MatchScore = XpRuleMatcher.Score(criteria, 0, order)
        };
    }

    sealed class FixedAmountRules : IXpRuleRegistry
    {
        readonly List<XpRule> rules;

        public FixedAmountRules(params XpRule[] rules) => this.rules = [.. rules];

        public IReadOnlyList<XpRule> All => rules;

        public IReadOnlyList<XpRule> ByActivity(string activity) =>
            string.Equals(activity, Deed.Activity, StringComparison.OrdinalIgnoreCase)
                ? rules
                : Array.Empty<XpRule>();

        public IReadOnlyList<XpRule> ByActivityRate(string activity) => Array.Empty<XpRule>();

        public IReadOnlyList<XpRule> ByActivityAmount(string activity) =>
            string.Equals(activity, Deed.Activity, StringComparison.OrdinalIgnoreCase)
                ? rules
                : Array.Empty<XpRule>();
    }
}
