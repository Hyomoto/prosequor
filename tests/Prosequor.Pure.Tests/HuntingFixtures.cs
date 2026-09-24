using Newtonsoft.Json.Linq;
using Prosequor.Ability;
using Prosequor.Ability.Actions;
using Prosequor.Ability.Hooks;
using Prosequor.Data;
using Prosequor.Xp;
using Prosequor.Xp.Activity;
using Xunit;

namespace Prosequor.Pure.Tests;

/// <summary>Pure fixtures for Hunter skill surfaces: hide size, arrow break fold, weight XP.</summary>
public static class HuntingFixtures
{
    public static void VerifyAll()
    {
        VerifyHideSizeUpgrade();
        VerifyArrowBreakClamp();
        VerifyHuntedWeightPlanPays();
        VerifyButcherPaysHuntingNotCooking();
        VerifyArrowAndBowCraftPays();
        VerifyUpgradeHideSizeActionRegistered();
    }

    static void VerifyHideSizeUpgrade()
    {
        if (!HideSizeUpgrade.TryNextSizeCode("game:hide-raw-small", out string next)
            || next != "game:hide-raw-medium"
            || !HideSizeUpgrade.TryNextSizeCode("game:hide-soaked-medium", out next)
            || next != "game:hide-soaked-large"
            || !HideSizeUpgrade.TryNextSizeCode("game:hide-pelt-large", out next)
            || next != "game:hide-pelt-huge"
            || HideSizeUpgrade.TryNextSizeCode("game:hide-raw-huge", out _)
            || HideSizeUpgrade.TryNextSizeCode("game:hide-raw-fox-common", out _)
            || HideSizeUpgrade.TryNextSizeCode("game:hide-raw-bear-brown-complete", out _)
            || HideSizeUpgrade.TryNextSizeCode("game:redmeat-raw", out _))
        {
            Assert.Fail("[prosequor] Hide size upgrade fixture failed.");
        }
    }

    static void VerifyArrowBreakClamp()
    {
        if (!NumberSpec.TryParse(
                JObject.Parse("""{ "op": "add", "value": -0.25 }"""),
                out NumberSpec? add,
                out string error)
            || add == null)
        {
            Assert.Fail("[prosequor] Arrow-break NumberSpec parse failed: " + error);
            return;
        }

        ActionTestProgress progress = new();
        PlayerInteractionContext context = new() { Progress = progress };
        AbilityRuleSource source = new() { SkillId = "hunting", Tier = 1 };
        float folded = add.Apply(0.1f, context, source);
        if (Math.Abs(folded - (-0.15f)) > 0.0001f)
        {
            Assert.Fail($"[prosequor] Arrow-break add fold should subtract break chance; got {folded}.");
        }

        float clamped = Math.Max(0f, folded);
        if (clamped != 0f)
        {
            Assert.Fail("[prosequor] Arrow-break should clamp at 0.");
        }
    }

    static void VerifyHuntedWeightPlanPays()
    {
        CollectionIndex collections = new();
        XpRule weightRule = AmountTableRule(
            "hunted-hunting",
            "hunting",
            [0.1f, 10f],
            order: 1,
            collections,
            tags: ["hunted"]);
        XpRule trapRule = AmountTableRule(
            "trapped-hunting",
            "hunting",
            [0.1f, 10f],
            order: 2,
            collections,
            tags: ["trapped"]);

        IReadOnlyList<Deed.PlannedPay> light = Deed.PlanPays(
            new FixedAmountRules(weightRule),
            collections,
            "p",
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { DeedTokenTags.Hunted },
            caller: "game:arrow-flint",
            target: "game:hare-arctic-adult-male",
            mount: null,
            ground: null,
            lastCraft: null,
            metric: 4f,
            metricMin: 4f,
            metricMax: 450f,
            totalUnits: 0,
            craftCount: 1,
            metricDomain: AnimalWeightCatalog.MetricDomain);
        IReadOnlyList<Deed.PlannedPay> heavy = Deed.PlanPays(
            new FixedAmountRules(weightRule),
            collections,
            "p",
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { DeedTokenTags.Hunted },
            caller: "game:spear-generic-copper",
            target: "game:deer-elk-adult-male",
            mount: null,
            ground: null,
            lastCraft: null,
            metric: 450f,
            metricMin: 4f,
            metricMax: 450f,
            totalUnits: 0,
            craftCount: 1,
            metricDomain: AnimalWeightCatalog.MetricDomain);
        IReadOnlyList<Deed.PlannedPay> trapped = Deed.PlanPays(
            new FixedAmountRules(trapRule),
            collections,
            "p",
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { DeedTokenTags.Trapped },
            caller: "game:baskettrap-papyrus",
            target: "game:hare-arctic-adult-male",
            mount: null,
            ground: null,
            lastCraft: null,
            metric: 4f,
            metricMin: 4f,
            metricMax: 450f,
            totalUnits: 0,
            craftCount: 1,
            metricDomain: AnimalWeightCatalog.MetricDomain);

        if (light.Count != 1
            || Math.Abs(light[0].Amount - 0.1f) > 0.0001f
            || heavy.Count != 1
            || Math.Abs(heavy[0].Amount - 10f) > 0.0001f
            || trapped.Count != 1
            || Math.Abs(trapped[0].Amount - 0.1f) > 0.0001f)
        {
            Assert.Fail(
                $"[prosequor] Hunted/trapped weight plan failed light={AmountOrNone(light)} heavy={AmountOrNone(heavy)} trapped={AmountOrNone(trapped)}.");
        }
    }

    static void VerifyButcherPaysHuntingNotCooking()
    {
        CollectionIndex collections = new();
        collections.EnsureKey("meat");
        collections.EnsureKey("fat");
        collections.EnsureKey("hide");
        collections.AddCode("meat", "game:redmeat-raw");
        collections.AddCode("fat", "game:fat");
        collections.AddCode("hide", "game:hide-raw-small");

        if (!XpRuleCompiler.TryCompile(
                new XpRuleJson
                {
                    id = "butcher-hunting",
                    amount = new JValue(0.082),
                    pay = new JValue("quantity"),
                    include = ["<meat>", "<fat>"],
                    when = new XpRuleWhenJson
                    {
                        activity = Deed.Activity,
                        tags = ["butchered"]
                    }
                },
                "hunting",
                1,
                collections,
                out XpRule huntingButcher,
                out string qtyError)
            || huntingButcher.Include.IsEmpty)
        {
            Assert.Fail("[prosequor] hunting butcher include compile failed: " + qtyError);
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
            Quantity: 4,
            Ingredients: 0,
            QuantityUnits:
            [
                new Deed.QuantityUnit("game:redmeat-raw", 2),
                new Deed.QuantityUnit("game:fat", 1),
                new Deed.QuantityUnit("game:hide-raw-small", 1)
            ]);

        IReadOnlyList<Deed.PlannedPay> pays = Deed.PlanPays(
            new FixedAmountRules(huntingButcher),
            collections,
            "p",
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { DeedTokenTags.Butchered },
            caller: CallerIdentities.Hand,
            target: "game:hare-arctic-adult-male",
            mount: null,
            ground: null,
            lastCraft: null,
            parts);

        if (pays.Count != 1
            || pays[0].SkillId != "hunting"
            || Math.Abs(pays[0].Amount - 0.082f * 3f) > 0.0001f)
        {
            Assert.Fail(
                $"[prosequor] Butcher should pay hunting quantity of meat+fat only; got count={pays.Count} amount={AmountOrNone(pays)}.");
        }
    }

    static void VerifyArrowAndBowCraftPays()
    {
        CollectionIndex collections = new();
        collections.EnsureKey("arrow");
        collections.EnsureKey("bow");
        collections.AddCode("arrow", "game:arrow-flint");
        collections.AddCode("bow", "game:bow-simple");

        XpRule arrowRule = QuantityRule(
            "craft-arrows-hunting",
            "hunting",
            0.02f,
            order: 1,
            collections,
            tags: ["crafted", "caller:@grid", "target:<arrow>"]);
        XpRule bowRule = QuantityRule(
            "craft-bows-hunting",
            "hunting",
            0.75f,
            order: 2,
            collections,
            tags: ["crafted", "caller:@grid", "target:<bow>"]);

        IReadOnlyList<Deed.PlannedPay> arrows = Deed.PlanPays(
            new FixedAmountRules(arrowRule),
            collections,
            "p",
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { DeedTokenTags.Crafted },
            caller: CallerIdentities.Grid,
            target: "game:arrow-flint",
            mount: null,
            ground: null,
            lastCraft: null,
            metric: 0f,
            metricMin: 0f,
            metricMax: 0f,
            totalUnits: 0,
            craftCount: 10);
        IReadOnlyList<Deed.PlannedPay> bows = Deed.PlanPays(
            new FixedAmountRules(bowRule),
            collections,
            "p",
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { DeedTokenTags.Crafted },
            caller: CallerIdentities.Grid,
            target: "game:bow-simple",
            mount: null,
            ground: null,
            lastCraft: null,
            metric: 0f,
            metricMin: 0f,
            metricMax: 0f,
            totalUnits: 0,
            craftCount: 1);

        if (arrows.Count != 1
            || Math.Abs(arrows[0].Amount - 0.2f) > 0.0001f
            || bows.Count != 1
            || Math.Abs(bows[0].Amount - 0.75f) > 0.0001f)
        {
            Assert.Fail(
                $"[prosequor] Arrow/bow craft plan failed arrows={AmountOrNone(arrows)} bows={AmountOrNone(bows)}.");
        }
    }

    static void VerifyUpgradeHideSizeActionRegistered()
    {
        HookRegistry hooks = new();
        AbilityActionRegistry actions = new(hooks);
        AbilityBootstrap.RegisterBuiltIns(hooks, actions);
        if (!actions.TryGet(
                ActionIds.UpgradeHideSize,
                HookIds.EntityInteraction,
                VerbIds.MutateDrops,
                HookIds.Stack,
                out _)
            || !actions.TryGet(
                ActionIds.Chance,
                HookIds.EntityInteraction,
                VerbIds.MutateDrops,
                HookIds.Stack,
                out _)
            || !actions.TryGet(
                ActionIds.Number,
                HookIds.PlayerInteraction,
                VerbIds.AnimalSenseRange,
                HookIds.Default,
                out _)
            || !actions.TryGet(
                ActionIds.Number,
                HookIds.PlayerInteraction,
                VerbIds.AnimalThreatSneak,
                HookIds.Default,
                out _)
            || !actions.TryGet(
                ActionIds.Number,
                HookIds.PlayerInteraction,
                VerbIds.ArrowBreak,
                HookIds.Default,
                out _)
            || !actions.TryGet(
                ActionIds.Number,
                HookIds.PlayerInteraction,
                VerbIds.UnawareDamage,
                HookIds.Amount,
                out _)
            || !actions.TryGet(
                ActionIds.Number,
                HookIds.PlayerInteraction,
                VerbIds.UnawareDamage,
                HookIds.Threshold,
                out _))
        {
            Assert.Fail("[prosequor] Hunting action registration fixture failed.");
        }
    }

    static XpRule AmountTableRule(
        string id,
        string skill,
        float[] table,
        int order,
        CollectionIndex collections,
        string[] tags)
    {
        List<TagCriterion> criteria = ParseTags(collections, tags);
        return new XpRule
        {
            Id = id,
            Activity = Deed.Activity,
            SkillId = skill,
            Amount = 0f,
            AmountTable = table,
            Rate = 0f,
            Pay = XpPayChannel.Resistance,
            Payee = XpPayee.User,
            Criteria = criteria,
            Priority = 0,
            SourceOrder = order,
            MatchScore = XpRuleMatcher.Score(criteria, 0, order)
        };
    }

    static XpRule QuantityRule(
        string id,
        string skill,
        float amount,
        int order,
        CollectionIndex collections,
        string[] tags)
    {
        List<TagCriterion> criteria = ParseTags(collections, tags);
        return new XpRule
        {
            Id = id,
            Activity = Deed.Activity,
            SkillId = skill,
            Amount = amount,
            Rate = 0f,
            Pay = XpPayChannel.Quantity,
            Payee = XpPayee.User,
            Criteria = criteria,
            Priority = 0,
            SourceOrder = order,
            MatchScore = XpRuleMatcher.Score(criteria, 0, order)
        };
    }

    static List<TagCriterion> ParseTags(CollectionIndex collections, string[] tags)
    {
        List<TagCriterion> criteria = new();
        foreach (string raw in tags)
        {
            if (!TagCriterionParser.TryParse(raw, collections, out TagCriterion? criterion, out string error)
                || criterion == null)
            {
                Assert.Fail($"[prosequor] Hunting fixture tag parse failed: {raw} ({error})");
            }

            criteria.Add(criterion);
        }

        return criteria;
    }

    static string AmountOrNone(IReadOnlyList<Deed.PlannedPay> pays) =>
        pays.Count == 0 ? "none" : pays[0].Amount.ToString();

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
