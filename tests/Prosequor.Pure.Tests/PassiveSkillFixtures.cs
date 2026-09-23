using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Prosequor.Ability;
using Prosequor.Ability.Hooks;
using Prosequor.Data;
using Prosequor.Xp;
using Prosequor.Xp.Activity;
using Prosequor.Xp.Adapters;
using Vintagestory.API.MathTools;
using Xunit;

namespace Prosequor.Pure.Tests;

/// <summary>
/// Passive skill contracts: forage XP gates, locomotion exclusivity, and Adaptation curves.
/// </summary>
public class PassiveSkillFixtures
{
    [Fact]
    [Trait("Layer", "Content")]
    [Trait("Kind", "SkillCompile")]
    public void ShippedPassives_Should_ClassifyAsPassiveWithKindCaps()
    {
        foreach ((string id, string file) in new (string, string)[]
        {
            ("forager", "forager.json"),
            ("athletics", "athletics.json"),
            ("swimming", "swimming.json"),
            ("sneaking", "sneaking.json"),
            ("adaptation", "adaptation.json")
        })
        {
            SkillDefJson? row = JsonConvert.DeserializeObject<SkillDefJson>(
                File.ReadAllText(SkillPath(file)));
            if (row == null
                || row.id != id
                || SkillKindPolicy.ClassifyDraft(row) != SkillKind.Passive
                || SkillKindPolicy.MaxLevelFor(SkillKind.Passive) != XpCurves.MinorMaxLevel)
            {
                Assert.Fail($"[prosequor] {file} should be a passive skill capped at {XpCurves.MinorMaxLevel}.");
            }
        }
    }

    [Fact]
    [Trait("Layer", "Xp")]
    [Trait("Kind", "Deed")]
    public void ForagerXp_Should_PayWildProduceAndFlat_AndNotFarming()
    {
        CollectionIndex collections = ForageCollections();
        XpRule produce = CompileAmount(
            collections,
            "forage-produce",
            0.01f,
            pay: "quantity",
            tags: ["harvested", "undomesticated", "target:<berry-bush, crop, mushroom>"]);
        XpRule flat = CompileAmount(
            collections,
            "forage-flat",
            0.05f,
            pay: null,
            tags: ["harvested", "undomesticated", "target:<reed, stick, sap>"]);
        XpRule farming = CompileAmount(
            collections,
            "farming",
            0.01f,
            pay: "quantity",
            tags: ["harvested", "domesticated", "target:<crop, berry-bush, fruit-tree>"]);

        Deed.Channels units = Quantity(
            new Deed.QuantityUnit("game:mushroom-fieldmushroom-normal", 3));

        IReadOnlyList<Deed.PlannedPay> wildMushroom = Deed.PlanPays(
            new FixedRules(produce, farming),
            collections,
            "p",
            Tokens(DeedTokenTags.Harvested, HarvestXp.TokenUndomesticated),
            caller: "prosequor:@hand",
            target: "game:mushroom-fieldmushroom-normal",
            mount: null,
            ground: null,
            lastCraft: null,
            units);
        if (wildMushroom.Count != 1 || Math.Abs(wildMushroom[0].Amount - 0.03f) > 0.0001f)
        {
            Assert.Fail("[prosequor] wild mushroom should pay 0.03 forage XP and not farming.");
        }

        IReadOnlyList<Deed.PlannedPay> wildRipeBerry = Deed.PlanPays(
            new FixedRules(produce, farming),
            collections,
            "p",
            Tokens(DeedTokenTags.Harvested, HarvestXp.TokenUndomesticated),
            caller: "prosequor:@hand",
            target: "game:fruitingbush-blueberry-ripe",
            mount: null,
            ground: null,
            lastCraft: null,
            Quantity(new Deed.QuantityUnit("game:fruit-blueberry", 2)));
        if (wildRipeBerry.Count != 1 || Math.Abs(wildRipeBerry[0].Amount - 0.02f) > 0.0001f)
        {
            Assert.Fail("[prosequor] unplanted ripe berry bush should pay 0.02 forage XP and not farming.");
        }

        IReadOnlyList<Deed.PlannedPay> wildStateBerry = Deed.PlanPays(
            new FixedRules(produce, farming),
            collections,
            "p",
            Tokens(DeedTokenTags.Harvested, HarvestXp.TokenUndomesticated),
            caller: "prosequor:@hand",
            target: "game:fruitingbush-blueberry-wild",
            mount: null,
            ground: null,
            lastCraft: null,
            Quantity(new Deed.QuantityUnit("game:fruit-blueberry", 2)));
        if (wildStateBerry.Count != 1 || Math.Abs(wildStateBerry[0].Amount - 0.02f) > 0.0001f)
        {
            Assert.Fail("[prosequor] wild-state berry bush should pay 0.02 forage XP and not farming.");
        }

        IReadOnlyList<Deed.PlannedPay> planted = Deed.PlanPays(
            new FixedRules(produce, farming),
            collections,
            "p",
            Tokens(DeedTokenTags.Harvested, HarvestXp.TokenDomesticated),
            caller: "prosequor:@hand",
            target: "game:crop-carrot-9",
            mount: null,
            ground: null,
            lastCraft: null,
            Quantity(new Deed.QuantityUnit("game:carrot", 4)));
        if (planted.Count != 1 || planted[0].SkillId != "farming-skill")
        {
            Assert.Fail("[prosequor] domesticated harvest must not pay forager.");
        }

        IReadOnlyList<Deed.PlannedPay> wildStick = Deed.PlanPays(
            new FixedRules(flat, produce),
            collections,
            "p",
            Tokens(DeedTokenTags.Harvested, HarvestXp.TokenUndomesticated),
            caller: "prosequor:@hand",
            target: "game:loosestick-free",
            mount: null,
            ground: null,
            lastCraft: null,
            Quantity(new Deed.QuantityUnit("game:stick", 1)));
        if (wildStick.Count != 1 || Math.Abs(wildStick[0].Amount - 0.05f) > 0.0001f)
        {
            Assert.Fail("[prosequor] wild loose stick should pay 0.05 flat, not quantity.");
        }

        IReadOnlyList<Deed.PlannedPay> wildHorsetail = Deed.PlanPays(
            new FixedRules(flat, produce),
            collections,
            "p",
            Tokens(DeedTokenTags.Harvested, HarvestXp.TokenUndomesticated),
            caller: "prosequor:@hand",
            target: "game:flower-horsetail-free",
            mount: null,
            ground: null,
            lastCraft: null,
            Quantity(new Deed.QuantityUnit("game:flower-horsetail-free", 1)));
        if (wildHorsetail.Count != 1 || Math.Abs(wildHorsetail[0].Amount - 0.05f) > 0.0001f)
        {
            Assert.Fail("[prosequor] wild horsetail should pay 0.05 flat like reeds.");
        }

        IReadOnlyList<Deed.PlannedPay> wildSap = Deed.PlanPays(
            new FixedRules(flat, produce),
            collections,
            "p",
            Tokens(DeedTokenTags.Harvested, HarvestXp.TokenUndomesticated),
            caller: "prosequor:@hand",
            target: "game:log-resin-pine-ud",
            mount: null,
            ground: null,
            lastCraft: null,
            Quantity(new Deed.QuantityUnit("game:resin", 1)));
        if (wildSap.Count != 1 || Math.Abs(wildSap[0].Amount - 0.05f) > 0.0001f)
        {
            Assert.Fail("[prosequor] wild sap scoop should pay 0.05 flat like reeds.");
        }

        IReadOnlyList<Deed.PlannedPay> placedPile = Deed.PlanPays(
            new FixedRules(flat, produce),
            collections,
            "p",
            Tokens(DeedTokenTags.Harvested),
            caller: "prosequor:@hand",
            target: "game:loosestick-free",
            mount: null,
            ground: null,
            lastCraft: null,
            Quantity(new Deed.QuantityUnit("game:stick", 8)));
        if (placedPile.Count != 0)
        {
            Assert.Fail("[prosequor] player-placed stick sources (no undomesticated) must not pay.");
        }
    }

    [Fact]
    [Trait("Layer", "Xp")]
    [Trait("Kind", "Deed")]
    public void ForagerXp_ShippedRules_Should_PayWildProduceAndFlat()
    {
        CollectionIndex collections = ForageCollections();
        SkillDefJson? row = JsonConvert.DeserializeObject<SkillDefJson>(
            File.ReadAllText(SkillPath("forager.json")));
        if (row?.xpRules == null || row.xpRules.Length == 0)
        {
            Assert.Fail("[prosequor] forager.json should ship xpRules.");
        }

        int sourceOrder = 0;
        List<string> warnings = new();
        List<XpRule> rules = XpRuleCompiler.CompileAll(
            row!.xpRules,
            "forager",
            collections,
            ref sourceOrder,
            warnings.Add);
        if (warnings.Count > 0 || rules.Count != 2)
        {
            Assert.Fail(
                "[prosequor] forager.json xpRules should compile to two amount rules: "
                + string.Join("; ", warnings));
        }

        FixedRules registry = new([.. rules]);
        IReadOnlyList<Deed.PlannedPay> berry = Deed.PlanPays(
            registry,
            collections,
            "p",
            Tokens(DeedTokenTags.Harvested, HarvestXp.TokenUndomesticated),
            caller: "prosequor:@hand",
            target: "game:fruitingbush-wild-blueberry-free",
            mount: null,
            ground: null,
            lastCraft: null,
            Quantity(new Deed.QuantityUnit("game:fruit-blueberry", 2)));
        if (berry.Count != 1 || berry[0].SkillId != "forager" || Math.Abs(berry[0].Amount - 0.02f) > 0.0001f)
        {
            Assert.Fail("[prosequor] shipped forage-wild-produce should pay 0.02 on fruitingbush-wild-blueberry-free.");
        }

        IReadOnlyList<Deed.PlannedPay> mushroom = Deed.PlanPays(
            registry,
            collections,
            "p",
            Tokens(DeedTokenTags.Harvested, HarvestXp.TokenUndomesticated),
            caller: "prosequor:@hand",
            target: "game:mushroom-fieldmushroom-normal",
            mount: null,
            ground: null,
            lastCraft: null,
            Quantity(new Deed.QuantityUnit("game:mushroom-fieldmushroom-normal", 3)));
        if (mushroom.Count != 1 || Math.Abs(mushroom[0].Amount - 0.03f) > 0.0001f)
        {
            Assert.Fail("[prosequor] shipped forage-wild-produce should pay 0.03 on a wild mushroom.");
        }

        IReadOnlyList<Deed.PlannedPay> stick = Deed.PlanPays(
            registry,
            collections,
            "p",
            Tokens(DeedTokenTags.Harvested, HarvestXp.TokenUndomesticated),
            caller: "prosequor:@hand",
            target: "game:loosestick-free",
            mount: null,
            ground: null,
            lastCraft: null,
            Quantity(new Deed.QuantityUnit("game:stick", 1)));
        if (stick.Count != 1 || Math.Abs(stick[0].Amount - 0.05f) > 0.0001f)
        {
            Assert.Fail("[prosequor] shipped forage-wild-flat should pay 0.05 on a wild loose stick.");
        }

        IReadOnlyList<Deed.PlannedPay> horsetail = Deed.PlanPays(
            registry,
            collections,
            "p",
            Tokens(DeedTokenTags.Harvested, HarvestXp.TokenUndomesticated),
            caller: "prosequor:@hand",
            target: "game:flower-horsetail-free",
            mount: null,
            ground: null,
            lastCraft: null,
            Quantity(new Deed.QuantityUnit("game:flower-horsetail-free", 1)));
        if (horsetail.Count != 1 || Math.Abs(horsetail[0].Amount - 0.05f) > 0.0001f)
        {
            Assert.Fail("[prosequor] shipped forage-wild-flat should pay 0.05 on wild horsetail.");
        }

        IReadOnlyList<Deed.PlannedPay> sap = Deed.PlanPays(
            registry,
            collections,
            "p",
            Tokens(DeedTokenTags.Harvested, HarvestXp.TokenUndomesticated),
            caller: "prosequor:@hand",
            target: "game:log-resin-pine-ud",
            mount: null,
            ground: null,
            lastCraft: null,
            Quantity(new Deed.QuantityUnit("game:resin", 1)));
        if (sap.Count != 1 || Math.Abs(sap[0].Amount - 0.05f) > 0.0001f)
        {
            Assert.Fail("[prosequor] shipped forage-wild-flat should pay 0.05 on wild sap.");
        }
    }

    [Fact]
    [Trait("Layer", "Ability")]
    [Trait("Kind", "Locomotion")]
    public void FootLocomotion_Should_BeExclusive_AndEffortOnlyWhileMoving()
    {
        if (PlayerInteractionStation.ClassifyFootLocomotion(true, false, false, true, false)
            != FootLocomotion.None)
        {
            Assert.Fail("[prosequor] mounted sprint is not a foot mode.");
        }

        if (PlayerInteractionStation.ClassifyFootLocomotion(false, true, false, true, true)
            != FootLocomotion.Swim
            || PlayerInteractionStation.ClassifyFootLocomotion(false, false, true, false, true)
            != FootLocomotion.Swim)
        {
            Assert.Fail("[prosequor] liquid should win over sprint and sneak.");
        }

        if (PlayerInteractionStation.ClassifyFootLocomotion(false, false, false, true, true)
            != FootLocomotion.None)
        {
            Assert.Fail("[prosequor] sprint and sneak together should apply neither.");
        }

        if (EffortFootEmitter.TokenFor(FootLocomotion.Sprint, swimming: false) != EffortTokenTags.Sprinting
            || EffortFootEmitter.TokenFor(FootLocomotion.Swim, swimming: false) != null
            || EffortFootEmitter.TokenFor(FootLocomotion.Swim, swimming: true) != EffortTokenTags.Swimming
            || EffortFootEmitter.TokenFor(FootLocomotion.Sneak, swimming: false) != EffortTokenTags.Sneaking
            || EffortFootEmitter.TokenFor(FootLocomotion.None, swimming: false) != null)
        {
            Assert.Fail("[prosequor] foot effort tokens should match the active mode.");
        }

        Vec3d origin = new(0, 0, 0);
        if (EffortFootEmitter.HasDisplaced(origin, new Vec3d(0.1, 0, 0))
            || !EffortFootEmitter.HasDisplaced(origin, new Vec3d(0.3, 0, 0)))
        {
            Assert.Fail("[prosequor] foot effort requires displacement at the mount travel threshold.");
        }
    }

    [Fact]
    [Trait("Layer", "Ability")]
    [Trait("Kind", "Number")]
    public void PassiveFolds_Should_MatchCapsAtLevelZeroAndFifty()
    {
        HookRegistry hooks = new();
        AbilityActionRegistry actions = new(hooks);
        AbilityBootstrap.RegisterBuiltIns(hooks, actions);
        CollectionIndex collections = new();

        float sprint0 = FoldFloat(hooks, actions, collections, "athletics", VerbIds.SprintSpeed, 0);
        float sprint50 = FoldFloat(hooks, actions, collections, "athletics", VerbIds.SprintSpeed, 50);
        if (Math.Abs(sprint0) > 0.0001f || Math.Abs(sprint50 - 0.25f) > 0.0001f)
        {
            Assert.Fail($"[prosequor] athletics sprint bonus should be 0 / 0.25, got {sprint0} / {sprint50}.");
        }

        float swim50 = FoldFloat(hooks, actions, collections, "swimming", VerbIds.SwimSpeed, 50);
        float sneak50 = FoldFloat(hooks, actions, collections, "sneaking", VerbIds.SneakSpeed, 50);
        if (Math.Abs(swim50 - 0.2f) > 0.0001f || Math.Abs(sneak50 - 0.2f) > 0.0001f)
        {
            Assert.Fail($"[prosequor] swim/sneak bonus at 50 should be 0.2, got {swim50} / {sneak50}.");
        }

        int recover0 = FoldInt(hooks, actions, collections, VerbIds.TemporalRecoverRate, 0);
        int recover50 = FoldInt(hooks, actions, collections, VerbIds.TemporalRecoverRate, 50);
        int drain0 = FoldInt(hooks, actions, collections, VerbIds.TemporalDrainRate, 0);
        int drain50 = FoldInt(hooks, actions, collections, VerbIds.TemporalDrainRate, 50);
        if (recover0 != 100 || recover50 != 150 || drain0 != 100 || drain50 != 70)
        {
            Assert.Fail(
                $"[prosequor] adaptation curve should be 100/150 recover and 100/70 drain, got {recover0}/{recover50} and {drain0}/{drain50}.");
        }
    }

    static float FoldFloat(
        IHookRegistry hooks,
        IAbilityActionRegistry actions,
        CollectionIndex collections,
        string skillId,
        VerbId verb,
        int level)
    {
        AbilityPipeline pipeline = PipelineFor(hooks, actions, collections, skillId, level);
        return pipeline.Run(
            HookIds.PlayerInteraction,
            verb,
            HookIds.Default,
            Context(skillId, level, verb),
            0f);
    }

    static int FoldInt(
        IHookRegistry hooks,
        IAbilityActionRegistry actions,
        CollectionIndex collections,
        VerbId verb,
        int level)
    {
        AbilityPipeline pipeline = PipelineFor(hooks, actions, collections, "adaptation", level);
        return pipeline.Run(
            HookIds.PlayerInteraction,
            verb,
            HookIds.Default,
            Context("adaptation", level, verb),
            0);
    }

    static AbilityPipeline PipelineFor(
        IHookRegistry hooks,
        IAbilityActionRegistry actions,
        CollectionIndex collections,
        string skillId,
        int level)
    {
        SkillDefJson row = JsonConvert.DeserializeObject<SkillDefJson>(
            File.ReadAllText(SkillPath(skillId + ".json")))!;
        int sourceOrder = 0;
        List<string> errors = new();
        List<AbilityRule>? rules = AbilityRuleCompiler.CompileEffects(
            skillId,
            nodeId: null,
            tier: null,
            row.effects,
            hooks,
            actions,
            collections,
            errors,
            ref sourceOrder);
        if (rules == null || errors.Count > 0)
        {
            throw new InvalidOperationException(string.Join("; ", errors));
        }

        PipelineSkillRegistry skills = new();
        skills.Register(new SkillDef
        {
            Id = skillId,
            Kind = SkillKind.Passive,
            MaxLevel = XpCurves.MinorMaxLevel,
            Rules = rules
        });
        _ = level;
        return new AbilityPipeline(actions, skills);
    }

    static PlayerInteractionContext Context(string skillId, int level, VerbId verb)
    {
        ActionTestProgress progress = new();
        progress.SetSkillLevel(skillId, level);
        return new PlayerInteractionContext
        {
            Progress = progress,
            Fact = new AbilityAction
            {
                Verb = verb.Value,
                ActorUid = "p"
            }
        };
    }

    static CollectionIndex ForageCollections()
    {
        CollectionIndex collections = new();
        collections.EnsureKey("berry-bush");
        collections.EnsureKey("crop");
        collections.EnsureKey("fruit-tree");
        collections.EnsureKey("mushroom");
        collections.EnsureKey("reed");
        collections.EnsureKey("sap");
        collections.EnsureKey("stick");
        collections.AddCode("berry-bush", "game:fruitingbush-blueberry-ripe");
        collections.AddCode("berry-bush", "game:fruitingbush-blueberry-wild");
        collections.AddCode("berry-bush", "game:fruitingbush-wild-blueberry-free");
        collections.AddCode("crop", "game:crop-carrot-9");
        collections.AddCode("fruit-tree", "game:fruittree-apple");
        collections.AddCode("mushroom", "game:mushroom-fieldmushroom-normal");
        collections.AddCode("reed", "game:tallplant-tule-land-normal-free");
        collections.AddCode("reed", "game:tallplant-coopersreed-land-normal-free");
        collections.AddCode("reed", "game:flower-horsetail-free");
        collections.AddCode("sap", "game:log-resin-pine-ud");
        collections.AddCode("stick", "game:loosestick-free");
        collections.AddCode("stick", "game:stick");
        return collections;
    }

    static XpRule CompileAmount(
        CollectionIndex collections,
        string skillId,
        float amount,
        string? pay,
        string[] tags)
    {
        if (!XpRuleCompiler.TryCompile(
                new XpRuleJson
                {
                    id = skillId,
                    amount = new JValue(amount),
                    pay = pay == null ? null : new JValue(pay),
                    when = new XpRuleWhenJson
                    {
                        activity = Deed.Activity,
                        tags = tags
                    }
                },
                skillId + "-skill",
                sourceOrder: 1,
                collections,
                out XpRule rule,
                out string error))
        {
            throw new InvalidOperationException(error);
        }

        return rule;
    }

    static Deed.Channels Quantity(Deed.QuantityUnit unit) => new(
        Resistance: 0f,
        ResistanceMin: 0f,
        ResistanceMax: 0f,
        HasResistance: false,
        Voxels: 0f,
        VoxelsMin: 0f,
        VoxelsMax: 0f,
        HasVoxels: false,
        Quantity: unit.Count,
        Ingredients: 0,
        QuantityUnits: [unit]);

    static HashSet<string> Tokens(params string[] tags) =>
        new(tags, StringComparer.OrdinalIgnoreCase);

    static string SkillPath(string file)
    {
        string? dir = AppContext.BaseDirectory;
        for (int i = 0; i < 8 && dir != null; i++)
        {
            string candidate = Path.Combine(
                dir,
                "assets",
                "prosequor",
                "config",
                "prosequor",
                "skills",
                file);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            dir = Directory.GetParent(dir)?.FullName;
        }

        throw new InvalidOperationException("Could not find " + file);
    }

    sealed class FixedRules : IXpRuleRegistry
    {
        readonly List<XpRule> rules;

        public FixedRules(params XpRule[] rules) => this.rules = [.. rules];

        public IReadOnlyList<XpRule> All => rules;

        public IReadOnlyList<XpRule> ByActivity(string activity) =>
            string.Equals(activity, Deed.Activity, StringComparison.OrdinalIgnoreCase)
                ? rules
                : Array.Empty<XpRule>();

        public IReadOnlyList<XpRule> ByActivityRate(string activity) => Array.Empty<XpRule>();

        public IReadOnlyList<XpRule> ByActivityAmount(string activity) => ByActivity(activity);
    }
}
