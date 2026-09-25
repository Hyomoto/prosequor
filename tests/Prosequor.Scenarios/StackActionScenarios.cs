using Atlas.XUnit;
using Newtonsoft.Json.Linq;
using Prosequor.Ability;
using Prosequor.Ability.Actions;
using Prosequor.Ability.Hooks;
using Prosequor.Data;
using Prosequor.Player;
using Prosequor.Progress;
using Prosequor.Xp;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Xunit;

namespace Prosequor.Scenarios;

/// <summary>
/// Stack-list mutator actions against a live Atlas world (one class, shared server).
/// Known stacks in → expected list out; no world regen between cases.
/// </summary>
public class StackActionScenarios : AtlasScenarioBase
{
    const string SkillId = "stack-test-skill";
    const string TagKeep = "test-keep";
    const string PoolBonus = "test-bonus";

    IWorldAccessor WorldAccessor => World.Api.World;

    [AtlasScenario]
    [Trait("Layer", "Action")]
    [Trait("Kind", "Stack")]
    public void AppendFromDropTable_Should_AppendRolledStack()
    {
        (Item first, Item second) = RequireTwoItems();
        IReadOnlyList<ItemStack> input = ListOf(new ItemStack(first, 1));
        DropsContext context = MakeDrops(
            tags: new CollectionIndex(),
            dropTable: new FixedDropTable(new ItemStack(second, 1)));

        var action = new AppendFromDropTableItemAction();
        Assert.True(action.TryParseParams(new JObject(), out object parameters, out string error), error);
        var result = ApplyStacks(action, context, input, parameters);

        Assert.Equal(2, result.Count);
        Assert.Equal(first.Code, result[0].Item?.Code);
        Assert.Equal(second.Code, result[1].Item?.Code);
    }

    [AtlasScenario]
    [Trait("Layer", "Action")]
    [Trait("Kind", "Stack")]
    public void ReplaceMatchingStackWithBlock_Should_ReplaceStackWithBlock()
    {
        Item stick = RequireItem("game:stick", "game:drygrass", "game:bone");
        Block dirt = RequireBlock("game:soil-low-none", "game:soil-medium-none", "game:dirt");
        DropsContext context = MakeDrops(tags: new CollectionIndex(), block: dirt);
        ItemStack input = new(stick, 3);

        var action = new ReplaceMatchingStackWithBlockStackAction();
        var result = (ItemStack)action.Apply(context, input, new object(), SkillSource());

        Assert.Equal(dirt.Code, result.Block?.Code);
        Assert.Equal(1, result.StackSize);
    }

    [AtlasScenario]
    [Trait("Layer", "Action")]
    [Trait("Kind", "Stack")]
    public void AppendFromDropTable_NamedTable_Should_AppendPoolItem()
    {
        (Item first, Item second) = RequireTwoItems();
        CollectionIndex tags = new();
        tags.EnsureKey(PoolBonus);
        tags.AddWeighted(PoolBonus, second.Code?.ToString());
        DropsContext context = MakeDrops(tags: tags);
        var action = new AppendFromDropTableBlockAction();
        Assert.True(action.TryParseParams(
            JObject.Parse($"{{\"table\":\"{PoolBonus}\",\"quantity\":2,\"rolls\":2}}"),
            out object parameters,
            out string error),
            error);

        var result = ApplyStacks(action, context, ListOf(new ItemStack(first, 1)), parameters);

        Assert.Equal(3, result.Count);
        Assert.Equal(first.Code, result[0].Item?.Code);
        Assert.Equal(second.Code, result[1].Item?.Code);
        Assert.Equal(2, result[1].StackSize);
        Assert.Equal(second.Code, result[2].Item?.Code);
        Assert.Equal(2, result[2].StackSize);
    }

    [AtlasScenario]
    [Trait("Layer", "Action")]
    [Trait("Kind", "Stack")]
    public void ReplaceFromDropTable_NamedTable_Should_ReplaceFirstByDefault()
    {
        (Item first, Item second) = RequireTwoItems();
        Item keep = RequireThirdItem(first, second);
        CollectionIndex tags = new();
        tags.EnsureKey(PoolBonus);
        tags.AddWeighted(PoolBonus, second.Code?.ToString());
        DropsContext context = MakeDrops(tags: tags);
        var action = new ReplaceFromDropTableBlockAction();
        Assert.True(action.TryParseParams(
            JObject.Parse($"{{\"table\":\"{PoolBonus}\",\"quantity\":3}}"),
            out object parameters,
            out string error),
            error);

        var result = ApplyStacks(
            action,
            context,
            ListOf(new ItemStack(first, 1), new ItemStack(keep, 1)),
            parameters);

        Assert.Equal(2, result.Count);
        Assert.Equal(second.Code, result[0].Item?.Code);
        Assert.Equal(3, result[0].StackSize);
        Assert.Equal(keep.Code, result[1].Item?.Code);
    }

    [AtlasScenario]
    [Trait("Layer", "Action")]
    [Trait("Kind", "Stack")]
    public void ReplaceFromDropTable_RuleLast_Should_ReplaceLastEntry()
    {
        (Item first, Item second) = RequireTwoItems();
        Item keep = RequireThirdItem(first, second);
        CollectionIndex tags = new();
        tags.EnsureKey(PoolBonus);
        tags.AddWeighted(PoolBonus, second.Code?.ToString());
        DropsContext context = MakeDrops(tags: tags);
        var action = new ReplaceFromDropTableBlockAction();
        Assert.True(action.TryParseParams(
            JObject.Parse($"{{\"table\":\"{PoolBonus}\",\"rule\":\"last\",\"quantity\":1}}"),
            out object parameters,
            out string error),
            error);

        var result = ApplyStacks(
            action,
            context,
            ListOf(new ItemStack(keep, 1), new ItemStack(first, 1)),
            parameters);

        Assert.Equal(2, result.Count);
        Assert.Equal(keep.Code, result[0].Item?.Code);
        Assert.Equal(second.Code, result[1].Item?.Code);
    }

    [AtlasScenario]
    [Trait("Layer", "Action")]
    [Trait("Kind", "Stack")]
    public void IncreaseFreshness_Should_ScalePerishFreshHours()
    {
        Item perishable = RequirePerishableItem();
        ItemStack baseline = new(perishable, 1);
        Assert.True(
            ItemFreshnessApplicator.CanImproveFreshness(WorldAccessor, baseline),
            $"Item '{perishable.Code}' is not perishable.");

        // Tiny apply only to initialize transitionstate for a readable baseline.
        Assert.True(ItemFreshnessApplicator.TryApplyExtraFraction(WorldAccessor, baseline, 0.000001f));
        float before = ReadFreshHours(baseline);
        Assert.True(before > 0f);

        ItemStack stack = new(perishable, 1);
        var progress = new ScenarioProgress();
        progress.SetSkillLevel(SkillId, 0);
        DropsContext context = MakeDrops(tags: new CollectionIndex(), progress: progress);
        var action = new IncreaseFreshnessStackAction();
        Assert.True(action.TryParseParams(
            JObject.Parse("""{"op":"scale","base":0.50,"perSkillLevel":0,"cap":0.50}"""),
            out object parameters,
            out string error),
            error);

        var result = (ItemStack)action.Apply(context, stack, parameters, SkillSource());

        Assert.Same(stack, result);
        float after = ReadFreshHours(stack);
        float vanillaApprox = before / 1.000001f;
        Assert.InRange(after / vanillaApprox, 1.49f, 1.51f);
    }

    [AtlasScenario]
    [Trait("Layer", "Action")]
    [Trait("Kind", "Stack")]
    public void AddCropSeed_Should_AppendResolvedSeed()
    {
        Block crop = RequireCropBlock();
        Item? expectedSeed = AbilityBootstrap.TryResolveCropSeed(WorldAccessor, crop);
        Assert.NotNull(expectedSeed);

        Item stick = RequireItem("game:stick", "game:drygrass", "game:bone");
        DropsContext context = MakeDrops(tags: new CollectionIndex(), block: crop);
        var action = new AddCropSeedAction();
        var result = ApplyStacks(action, context, ListOf(new ItemStack(stick, 1)), new object());

        Assert.Equal(2, result.Count);
        Assert.Equal(stick.Code, result[0].Item?.Code);
        Assert.Equal(expectedSeed!.Code, result[1].Item?.Code);
        Assert.Equal(1, result[1].StackSize);
    }

    [AtlasScenario]
    [Trait("Layer", "Action")]
    [Trait("Kind", "Stack")]
    public void ReplaceWithVariant_Should_SwapSeededStacksOnly()
    {
        (Item keep, Item match) = RequireTwoItems();
        Item replacement = RequireThirdItem(keep, match);

        CollectibleVariantTable variants = new();
        string key = CollectibleVariantTable.Key(match.Code.FirstCodePart(), "decorative");
        variants.Add(key, replacement);

        MutateProcessContext context = MakeOnProcessed(variants);
        var action = new ReplaceWithVariantAction();
        Assert.True(action.TryParseParams(
            JObject.Parse("""{"variant":"decorative"}"""),
            out object parameters,
            out string error),
            error);

        IReadOnlyList<ItemStack> input = ListOf(new ItemStack(keep, 2), new ItemStack(match, 3));
        var result = (IReadOnlyList<ItemStack>)action.Apply(context, input, parameters, SkillSource());

        Assert.Equal(2, result.Count);
        Assert.Equal(keep.Code, result[0].Collectible?.Code);
        Assert.Equal(2, result[0].StackSize);
        Assert.Equal(replacement.Code, result[1].Collectible?.Code);
        Assert.Equal(3, result[1].StackSize);
    }

    [AtlasScenario]
    [Trait("Layer", "Action")]
    [Trait("Kind", "Stack")]
    public void DecorativePotteryVariants_Should_PopulateDecorativeKeys()
    {
        CollectibleVariantTable table = new();
        DecorativePotteryVariants.Populate(World.Api, table);

        foreach (string bas in DecorativePotteryVariants.Bases)
        {
            string key = CollectibleVariantTable.Key(bas, DecorativePotteryVariants.Family);
            IReadOnlyList<CollectibleObject> list = table.Get(key);
            Assert.True(list.Count > 0, $"Expected non-empty variants for {key}");
            foreach (CollectibleObject collectible in list)
            {
                string path = collectible.Code.Path;
                Assert.False(
                    path.EndsWith("raw", StringComparison.OrdinalIgnoreCase),
                    $"Unexpected raw pottery in {key}: {collectible.Code}");
                Assert.False(
                    path.EndsWith("fired", StringComparison.OrdinalIgnoreCase),
                    $"Unexpected fired pottery in {key}: {collectible.Code}");
            }
        }
    }

    [AtlasScenario]
    [Trait("Layer", "Action")]
    [Trait("Kind", "Stack")]
    public void EnrichSoil_Should_ReplaceSoilOnly_WhenSnapshotPresent()
    {
        Block lowSoil = RequireBlock("game:soil-verylow-none", "game:soil-low-none");
        Item stick = RequireItem("game:stick", "game:drygrass", "game:bone");

        float[] nutrients = [65f, 65f, 65f];
        int[] original = [5, 5, 5];
        string? expectedKey = EnrichSoilAction.TryResolveFertilityKey(nutrients, original, 65f);
        Assert.NotNull(expectedKey);
        Block? expectedSoil = WorldAccessor.GetBlock(new AssetLocation("game", "soil-" + expectedKey + "-none"));
        Assert.NotNull(expectedSoil);

        DropsContext context = MakeDrops(
            tags: new CollectionIndex(),
            farmlandNutrients: nutrients,
            farmlandOriginal: original);
        var action = new EnrichSoilAction();
        Assert.True(action.TryParseParams(
            JObject.Parse("""{"maxFertility":65}"""),
            out object parameters,
            out string error),
            error);

        IReadOnlyList<ItemStack> input = ListOf(new ItemStack(lowSoil, 1), new ItemStack(stick, 2));
        var result = ApplyStacks(action, context, input, parameters);

        Assert.Equal(2, result.Count);
        Assert.Equal(expectedSoil!.Code, result[0].Block?.Code);
        Assert.Equal(1, result[0].StackSize);
        Assert.Equal(stick.Code, result[1].Item?.Code);
        Assert.Equal(2, result[1].StackSize);
    }

    [AtlasScenario]
    [Trait("Layer", "Action")]
    [Trait("Kind", "Stack")]
    public void EnrichSoil_Should_NoOp_WithoutSnapshot()
    {
        Block lowSoil = RequireBlock("game:soil-verylow-none", "game:soil-low-none");
        Item stick = RequireItem("game:stick", "game:drygrass", "game:bone");

        DropsContext context = MakeDrops(tags: new CollectionIndex());
        var action = new EnrichSoilAction();
        Assert.True(action.TryParseParams(
            JObject.Parse("""{"maxFertility":65}"""),
            out object parameters,
            out string error),
            error);

        IReadOnlyList<ItemStack> input = ListOf(new ItemStack(lowSoil, 1), new ItemStack(stick, 2));
        var result = ApplyStacks(action, context, input, parameters);

        Assert.Same(input, result);
    }

    [AtlasScenario]
    [Trait("Layer", "Action")]
    [Trait("Kind", "Stack")]
    public void EnrichSoil_Should_NoOp_WhenNoSoilStacks()
    {
        Item stick = RequireItem("game:stick", "game:drygrass", "game:bone");

        DropsContext context = MakeDrops(
            tags: new CollectionIndex(),
            farmlandNutrients: [65f, 65f, 65f],
            farmlandOriginal: [5, 5, 5]);
        var action = new EnrichSoilAction();
        Assert.True(action.TryParseParams(
            JObject.Parse("""{"maxFertility":65}"""),
            out object parameters,
            out string error),
            error);

        IReadOnlyList<ItemStack> input = ListOf(new ItemStack(stick, 2));
        var result = ApplyStacks(action, context, input, parameters);

        Assert.Same(input, result);
    }

    // --- helpers ---

    static AbilityRuleSource SkillSource() => new() { SkillId = SkillId };

    static IReadOnlyList<ItemStack> ListOf(params ItemStack[] stacks) => stacks;

    static IReadOnlyList<ItemStack> ApplyStacks(
        IAbilityActionHandler action,
        DropsContext context,
        IReadOnlyList<ItemStack> input,
        object parameters) =>
        (IReadOnlyList<ItemStack>)action.Apply(context, input, parameters, SkillSource());

    MutateProcessContext MakeOnProcessed(CollectibleVariantTable variants) =>
        new()
        {
            World = WorldAccessor,
            OutputSlot = new DummySlot(),
            Tags = new CollectionIndex(),
            Variants = variants
        };

    DropsContext MakeDrops(
        CollectionIndex tags,
        Block? block = null,
        IPlayerProgress? progress = null,
        IDropTable? dropTable = null,
        float[]? farmlandNutrients = null,
        int[]? farmlandOriginal = null,
        IReadOnlyList<ItemStack>? originalDrops = null) =>
        new()
        {
            World = WorldAccessor,
            Block = block,
            Progress = progress,
            Tags = tags,
            DropTable = dropTable,
            FarmlandNutrients = farmlandNutrients,
            FarmlandOriginalFertility = farmlandOriginal,
            OriginalDrops = originalDrops ?? Array.Empty<ItemStack>()
        };

    Item RequireItem(params string[] codes)
    {
        foreach (string code in codes)
        {
            Item? item = WorldAccessor.GetItem(new AssetLocation(code));
            if (item != null && item.Id != 0)
            {
                return item;
            }
        }

        Assert.Fail($"None of these items resolved: {string.Join(", ", codes)}");
        return null!;
    }

    Block RequireBlock(params string[] codes)
    {
        foreach (string code in codes)
        {
            Block? block = WorldAccessor.GetBlock(new AssetLocation(code));
            if (block != null && block.Id != 0)
            {
                return block;
            }
        }

        Assert.Fail($"None of these blocks resolved: {string.Join(", ", codes)}");
        return null!;
    }

    (Item First, Item Second) RequireTwoItems()
    {
        Item first = RequireOrdinaryItem();
        Item second = RequireThirdItem(first);
        return (first, second);
    }

    Item RequireOrdinaryItem(params Item[] exclude)
    {
        string[] candidates =
        [
            "game:stick",
            "game:drygrass",
            "game:bone",
            "game:fat",
            "game:honeycomb",
            "game:cattailtops",
            "game:flaxfibers",
            "game:seeds-flax",
            "game:clay",
            "game:fireclay",
            "game:stone-granite",
            "game:gear-temporal"
        ];

        HashSet<int> skip = new(exclude.Where(i => i != null).Select(i => i.Id));
        foreach (string code in candidates)
        {
            Item? item = WorldAccessor.GetItem(new AssetLocation(code));
            if (item != null && item.Id != 0 && !skip.Contains(item.Id))
            {
                return item;
            }
        }

        foreach (Item? item in WorldAccessor.Items)
        {
            if (item == null || item.Id == 0 || item.Code == null || skip.Contains(item.Id))
            {
                continue;
            }

            return item;
        }

        Assert.Fail("Could not resolve an ordinary item.");
        return null!;
    }

    Item RequireThirdItem(params Item[] exclude) => RequireOrdinaryItem(exclude);

    Item RequirePerishableItem()
    {
        string[] candidates =
        [
            "game:fruit-redapple",
            "game:fruit-pinkapple",
            "game:vegetable-carrot",
            "game:vegetable-turnip",
            "game:grain-flax",
            "game:meat-raw"
        ];

        foreach (string code in candidates)
        {
            Item? item = WorldAccessor.GetItem(new AssetLocation(code));
            if (item == null || item.Id == 0)
            {
                continue;
            }

            ItemStack probe = new(item, 1);
            if (ItemFreshnessApplicator.CanImproveFreshness(WorldAccessor, probe))
            {
                return item;
            }
        }

        foreach (Item? item in WorldAccessor.Items)
        {
            if (item == null || item.Id == 0 || item.Code == null)
            {
                continue;
            }

            ItemStack probe = new(item, 1);
            if (ItemFreshnessApplicator.CanImproveFreshness(WorldAccessor, probe))
            {
                return item;
            }
        }

        Assert.Fail("No perishable item found in the Atlas world.");
        return null!;
    }

    Block RequireCropBlock()
    {
        string[] candidates =
        [
            "game:crop-flax-8",
            "game:crop-flax-9",
            "game:crop-carrot-7",
            "game:crop-spelt-8",
            "game:crop-turnip-6"
        ];

        foreach (string code in candidates)
        {
            Block? block = WorldAccessor.GetBlock(new AssetLocation(code));
            if (block == null || block.Id == 0)
            {
                continue;
            }

            if (AbilityBootstrap.TryResolveCropSeed(WorldAccessor, block) != null)
            {
                return block;
            }
        }

        foreach (Block? block in WorldAccessor.Blocks)
        {
            if (block?.Code == null || block.Id == 0)
            {
                continue;
            }

            if (!block.Code.Path.StartsWith("crop-", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (AbilityBootstrap.TryResolveCropSeed(WorldAccessor, block) != null)
            {
                return block;
            }
        }

        Assert.Fail("No crop block with resolvable seeds found.");
        return null!;
    }

    static float ReadFreshHours(ItemStack stack)
    {
        if (stack.Attributes?["transitionstate"] is not ITreeAttribute tree)
        {
            Assert.Fail("Missing transitionstate after freshness apply.");
            return 0f;
        }

        if (tree["freshHours"] is not FloatArrayAttribute freshHoursAttr
            || freshHoursAttr.value.Length == 0)
        {
            Assert.Fail("Missing freshHours after freshness apply.");
            return 0f;
        }

        return freshHoursAttr.value[0];
    }

    sealed class FixedDropTable(ItemStack stack) : IDropTable
    {
        public ItemStack? TryRollOne() => stack.Clone();
    }

    sealed class ScenarioProgress : IPlayerProgress
    {
        readonly Dictionary<string, int> skillLevels = new(StringComparer.OrdinalIgnoreCase);

        public event Action? Changed;

        public int PlayerLevel => 1;
        public float PlayerXp => 0;
        public int UnlockPoints => 0;
        public float PlayerXpUntilNext => 0;

        public void SetSkillLevel(string skillId, int level)
        {
            skillLevels[skillId] = level;
            Changed?.Invoke();
        }

        public int GetSkillLevel(string skillId) =>
            skillLevels.TryGetValue(skillId, out int level) ? level : 0;

        public int GetAttribute(string id) => AttributeGrowth.DefaultScore;
        public float GetSkillXp(string skillId) => 0;
        public IReadOnlyList<string> GetUnlocks(string skillId) => Array.Empty<string>();
        public bool HasUnlock(string skillId, string code) => false;
        public int GetUnlockTier(string skillId, string nodeId) => 0;
        public float GetAttributeBucket(string id) => 0f;
        public bool HasSkillAccess(string skillId) => true;

        public void GetPlayerBar(out float intoLevel, out int needForNext, out int level)
        {
            intoLevel = 0;
            needForNext = 1;
            level = PlayerLevel;
        }

        public void GetSkillBar(string skillId, out float intoLevel, out int needForNext, out int level)
        {
            intoLevel = 0;
            needForNext = 1;
            level = GetSkillLevel(skillId);
        }

        public void AddPlayerXp(float amount, XpAwardMode mode = XpAwardMode.Earn) { }
        public void AddSkillXp(
            string skillId,
            float amount,
            AbilityAction? fact = null,
            XpAwardMode mode = XpAwardMode.Earn) { }
        public void AddUnlockPoints(int amount) { }
        public void SetPlayerLevel(int level) { }
        public void SetAttribute(string id, int score) { }
        public void AddAttributeBucket(string id, float amount) { }
        public bool GrantUnlock(string skillId, string code, int cost = 1) => false;
        public bool RevokeUnlock(string skillId, string code, int refund = 1) => false;
        public UnlockPurchaseStatus TryPurchaseNode(string skillId, string nodeId) =>
            UnlockPurchaseStatus.UnknownNode;
    }
}
