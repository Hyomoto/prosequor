using Atlas.Api;
using Atlas.XUnit;
using Prosequor;
using Prosequor.Ability;
using Prosequor.Ability.Hooks;
using Prosequor.Player;
using Prosequor.Xp;
using Prosequor.Xp.Activity;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.Common;
using Vintagestory.GameContent;
using Xunit;

namespace Prosequor.Scenarios;

/// <summary>
/// Metalworking XP: grid metal tools, anvil voxel high-water, mold harden settle,
/// quench, and temper.
/// </summary>
public class MetalworkingXpScenarios : AtlasScenarioBase
{
    const string Skill = "metalworking";

    [AtlasScenario]
    [Trait("Layer", "Xp")]
    [Trait("Kind", "Metalworking")]
    public async Task GridCraft_Should_PayMetalworking_When_CopperPickaxe()
    {
        ITestPlayer joined = await World.JoinPlayer("MwCraftCopper");
        IPlayer player = joined.Player;
        IPlayerProgress progress = RequireProgress(player);

        Item? head = World.Api.World.GetItem(new AssetLocation("game:pickaxehead-copper"));
        Item? stick = World.Api.World.GetItem(new AssetLocation("game:stick"));
        Assert.NotNull(head);
        Assert.NotNull(stick);

        InventoryCraftingGrid craft = RequireCraftGrid(player);
        // Pattern "H/S" width 1 height 2 → slots 0 (head) and 3 (stick).
        craft[0].Itemstack = new ItemStack(head, 1);
        craft[3].Itemstack = new ItemStack(stick, 1);
        craft.OnItemSlotModified(craft[0]);
        craft.OnItemSlotModified(craft[3]);

        ItemSlot output = craft[craft.Count - 1];
        Assert.False(output.Empty, "Expected a copper pickaxe recipe match.");
        Assert.Contains("pickaxe", output.Itemstack?.Collectible?.Code?.Path ?? "");

        ItemStack perCraft = output.Itemstack!.Clone();
        float xpBefore = ScenarioXp.TotalSkill(progress, Skill);

        ItemSlot sink = new DummySlot();
        ItemStackMoveOperation op = new(
            World.Api.World,
            EnumMouseButton.Left,
            0,
            EnumMergePriority.AutoMerge,
            requestedQuantity: 1);
        op.ActingPlayer = player;
        Assert.True(output.TryPutInto(sink, ref op) > 0);

        float gained = ScenarioXp.TotalSkill(progress, Skill) - xpBefore;
        float expected = ScenarioXp.PlannedGridTake(
            World.Api.World,
            Skill,
            player,
            perCraft,
            craftCount: 1,
            unitsPerCraft: 2);
        ScenarioXp.AssertPaid(gained, expected, "copper pickaxe grid craft");
    }

    [AtlasScenario]
    [Trait("Layer", "Xp")]
    [Trait("Kind", "Metalworking")]
    public async Task GridCraft_Should_NotPayMetalworking_When_FlintAxe()
    {
        ITestPlayer joined = await World.JoinPlayer("MwCraftFlint");
        IPlayer player = joined.Player;
        IPlayerProgress progress = RequireProgress(player);

        Item? axe = World.Api.World.GetItem(new AssetLocation("game:axe-flint"))
            ?? World.Api.World.GetItem(new AssetLocation("game:axehead-flint"));
        // Craft flint axe from head + stick if needed; otherwise take any flint tool craft.
        Item? head = World.Api.World.GetItem(new AssetLocation("game:axehead-flint"));
        Item? stick = World.Api.World.GetItem(new AssetLocation("game:stick"));
        Assert.NotNull(head);
        Assert.NotNull(stick);

        ProsequorModSystem? mod = ProsequorModSystem.For(World.Api);
        Assert.NotNull(mod);
        string flintAxeCode = "game:axe-flint";
        Assert.False(
            mod!.Collections.Index.Contains("metal-crafts", flintAxeCode),
            "Flint axe must not be in <metal-crafts>.");

        InventoryCraftingGrid craft = RequireCraftGrid(player);
        craft[0].Itemstack = new ItemStack(head, 1);
        craft[3].Itemstack = new ItemStack(stick, 1);
        craft.OnItemSlotModified(craft[0]);
        craft.OnItemSlotModified(craft[3]);

        ItemSlot output = craft[craft.Count - 1];
        Assert.False(output.Empty, "Expected a flint axe recipe match.");

        float xpBefore = ScenarioXp.TotalSkill(progress, Skill);
        ItemSlot sink = new DummySlot();
        ItemStackMoveOperation op = new(
            World.Api.World,
            EnumMouseButton.Left,
            0,
            EnumMergePriority.AutoMerge,
            requestedQuantity: 1);
        op.ActingPlayer = player;
        Assert.True(output.TryPutInto(sink, ref op) > 0);

        float gained = ScenarioXp.TotalSkill(progress, Skill) - xpBefore;
        Assert.True(
            Math.Abs(gained) <= 0.05f,
            $"Flint axe must not pay metalworking XP, gained {gained}.");
        _ = axe;
    }

    [AtlasScenario]
    [Trait("Layer", "Xp")]
    [Trait("Kind", "Metalworking")]
    public async Task AnvilGoodVoxels_Should_AwardMetalworkingXp()
    {
        ITestPlayer joined = await World.JoinPlayer("MwAnvilGood");
        IPlayer player = joined.Player;
        EntityBehaviorProgress progress = RequireBehavior(player);

        BlockEntityAnvil anvil = PrepareAnvil(player, out SmithingRecipe recipe, out List<Vec3i> goods);
        Assert.True(goods.Count >= 10, $"Expected ≥10 good voxels, got {goods.Count}.");

        string? target = recipe.Output?.Code?.ToString()
            ?? recipe.Output?.ResolvedItemstack?.Collectible?.Code?.ToString();
        float xpBefore = ScenarioXp.TotalSkill(progress, Skill);

        AnvilHitScope.Begin(player);
        try
        {
            for (int i = 0; i < 10; i++)
            {
                Vec3i cell = goods[i];
                anvil.Voxels[cell.X, cell.Y, cell.Z] = AnvilVoxelGrid.Metal;
                AnvilXpStation.TryAwardProgress(anvil, player);
            }
        }
        finally
        {
            AnvilHitScope.End();
        }

        float gained = ScenarioXp.TotalSkill(progress, Skill) - xpBefore;
        float expected = ScenarioXp.PlannedCraftingVoxels(
            World.Api.World,
            Skill,
            player.PlayerUID,
            target,
            voxelCount: 10,
            caller: EventFactBuilder.CodeOf(anvil.Block)!);
        ScenarioXp.AssertPaid(gained, expected, "10 anvil good voxels");
    }

    [AtlasScenario]
    [Trait("Layer", "Xp")]
    [Trait("Kind", "Metalworking")]
    public async Task AnvilUndoReplace_Should_NotFarmXp()
    {
        ITestPlayer joined = await World.JoinPlayer("MwAnvilUndo");
        IPlayer player = joined.Player;
        EntityBehaviorProgress progress = RequireBehavior(player);

        BlockEntityAnvil anvil = PrepareAnvil(player, out SmithingRecipe recipe, out List<Vec3i> goods);
        Vec3i cell = goods[0];
        string? target = recipe.Output?.Code?.ToString()
            ?? recipe.Output?.ResolvedItemstack?.Collectible?.Code?.ToString();
        float one = ScenarioXp.PlannedCraftingVoxels(
            World.Api.World,
            Skill,
            player.PlayerUID,
            target,
            voxelCount: 1,
            caller: EventFactBuilder.CodeOf(anvil.Block)!);
        Assert.True(one > 0f, "Expected smith-work rule to plan a positive grant.");

        float before = ScenarioXp.TotalSkill(progress, Skill);
        AnvilHitScope.Begin(player);
        try
        {
            anvil.Voxels[cell.X, cell.Y, cell.Z] = AnvilVoxelGrid.Metal;
            AnvilXpStation.TryAwardProgress(anvil, player);
            float afterPlace = ScenarioXp.TotalSkill(progress, Skill);
            ScenarioXp.AssertPaid(afterPlace - before, one, "one anvil good voxel");

            anvil.Voxels[cell.X, cell.Y, cell.Z] = AnvilVoxelGrid.Empty;
            AnvilXpStation.TryAwardProgress(anvil, player);
            Assert.Equal(afterPlace, ScenarioXp.TotalSkill(progress, Skill), precision: 4);

            anvil.Voxels[cell.X, cell.Y, cell.Z] = AnvilVoxelGrid.Metal;
            AnvilXpStation.TryAwardProgress(anvil, player);
            Assert.Equal(afterPlace, ScenarioXp.TotalSkill(progress, Skill), precision: 4);
        }
        finally
        {
            AnvilHitScope.End();
        }
    }

    [AtlasScenario]
    [Trait("Layer", "Xp")]
    [Trait("Kind", "Metalworking")]
    public async Task SmithingFormedCollection_Should_ContainRecipeOutputs()
    {
        ITestPlayer joined = await World.JoinPlayer("MwCatalog");
        _ = RequireBehavior(joined.Player);

        ProsequorModSystem? mod = ProsequorModSystem.For(World.Api);
        Assert.NotNull(mod);
        SmithingRecipeCatalog? catalog = mod!.SmithingRecipes;
        Assert.NotNull(catalog);
        CollectionIndex collections = mod.Collections.Index;

        List<string> missing = new();
        foreach (SmithingRecipe recipe in World.Api.GetSmithingRecipes())
        {
            foreach (string? code in RecipeOutputCodes(recipe))
            {
                if (!collections.Contains("smithing-formed", code))
                {
                    missing.Add(code);
                }
            }
        }

        Assert.True(
            missing.Count == 0,
            "Expected every smithing recipe output in <smithing-formed>. Missing: "
            + string.Join(", ", missing.Distinct(StringComparer.OrdinalIgnoreCase)));
    }

    [AtlasScenario]
    [Trait("Layer", "Xp")]
    [Trait("Kind", "Metalworking")]
    public async Task MoldHarden_Should_PayPourer_Once()
    {
        ITestPlayer joined = await World.JoinPlayer("MwMold");
        IPlayer player = joined.Player;
        IPlayerProgress progress = RequireProgress(player);

        Item? copper = World.Api.World.GetItem(new AssetLocation("game:ingot-copper"));
        Assert.NotNull(copper);
        ItemStack targetStack = new(copper, 1);
        const int fill = 100;
        string mold = RequireCollectionCode(World.Api, "mold");

        float expected = ScenarioXp.PlannedMoldCast(
            World.Api.World,
            Skill,
            player.PlayerUID,
            mold,
            EventFactBuilder.CodeOf(targetStack),
            fill);
        Assert.True(expected > 0f, "Expected mold-cast rule to plan a positive grant.");

        float xpBefore = ScenarioXp.TotalSkill(progress, Skill);
        Deed.Emit(
            World.Api,
            playerUid: "",
            DeedToken.Crafted,
            caller: mold,
            target: EventFactBuilder.CodeOf(targetStack),
            inputs: [new Deed.QuantityUnit(
                EventFactBuilder.CodeOf(targetStack) ?? "",
                MoldCastXpStation.IngredientsFromFill(fill))],
            contributors: [new Deed.ContributorShare(player.PlayerUID, 1f)]);

        float gained = ScenarioXp.TotalSkill(progress, Skill) - xpBefore;
        ScenarioXp.AssertPaid(gained, expected, "mold-cast harden");

        bool was = true;
        bool paid = true;
        Assert.False(
            MoldCastXpStation.TrySettleRisingEdge(
                full: true,
                hardened: true,
                hasPourer: true,
                ref was,
                ref paid),
            "Second harden tick must not repay.");
    }

    [AtlasScenario]
    [Trait("Layer", "Xp")]
    [Trait("Kind", "Metalworking")]
    public async Task BloomeryHarvest_Should_PayFlatThree()
    {
        ITestPlayer joined = await World.JoinPlayer("MwBloomery");
        IPlayer player = joined.Player;
        IPlayerProgress progress = RequireProgress(player);

        CollectionIndex? collections = ProsequorModSystem.For(World.Api)?.Collections?.Index;
        Assert.NotNull(collections);
        Block? bloomery = null;
        foreach (string code in collections!.Codes("bloomery"))
        {
            Block? block = World.Api.World.GetBlock(new AssetLocation(code));
            if (block != null && block.Id != 0)
            {
                bloomery = block;
                break;
            }
        }

        Item? bloom = World.Api.World.GetItem(new AssetLocation("game:ironbloom"));
        Assert.True(
            bloomery != null,
            "bloomery collection has no resolvable block. count=" + collections.CodeCount("bloomery"));
        Assert.NotNull(bloom);

        float expected = ScenarioXp.PlannedBloomeryHarvest(
            World.Api.World,
            Skill,
            player.PlayerUID,
            EventFactBuilder.CodeOf(bloomery),
            EventFactBuilder.CodeOf(bloom),
            bloomCount: 1);
        Assert.True(expected > 0f, "Expected harvested bloomery rule to plan a positive grant.");

        float xpBefore = ScenarioXp.TotalSkill(progress, Skill);
        BloomeryHarvestXp.Settle(player, new ItemStack(bloom), bloomery);
        float gained = ScenarioXp.TotalSkill(progress, Skill) - xpBefore;
        ScenarioXp.AssertPaid(gained, expected, "bloomery harvest");
    }

    [AtlasScenario]
    [Trait("Layer", "Xp")]
    [Trait("Kind", "Metalworking")]
    public async Task CementationFired_Should_PayQuantityPerBlister()
    {
        ITestPlayer joined = await World.JoinPlayer("MwCementation");
        IPlayer player = joined.Player;
        IPlayerProgress progress = RequireProgress(player);

        Item? blister = World.Api.World.GetItem(new AssetLocation("game:ingot-blistersteel"));
        Assert.NotNull(blister);
        ItemStack targetStack = new(blister, 16);
        const int quantity = 16;
        string furnace = RequireCollectionCode(World.Api, "cementation");

        float expected = ScenarioXp.PlannedCementationFired(
            World.Api.World,
            Skill,
            player.PlayerUID,
            furnace,
            EventFactBuilder.CodeOf(targetStack),
            quantity);
        Assert.True(expected > 0f, "Expected cementation-fired rule to plan a positive grant.");

        float xpBefore = ScenarioXp.TotalSkill(progress, Skill);
        Deed.Emit(
            World.Api,
            playerUid: "",
            DeedToken.Crafted,
            caller: furnace,
            target: EventFactBuilder.CodeOf(targetStack),
            outputs: [new Deed.QuantityUnit(EventFactBuilder.CodeOf(targetStack) ?? "", quantity)],
            contributors: [new Deed.ContributorShare(player.PlayerUID, 1f)]);

        float gained = ScenarioXp.TotalSkill(progress, Skill) - xpBefore;
        ScenarioXp.AssertPaid(gained, expected, "cementation fired");

        bool was = true;
        bool paid = true;
        Assert.False(
            CementationXpStation.TrySettleRisingEdge(
                processComplete: true,
                hasContributors: true,
                ref was,
                ref paid),
            "Second complete tick must not repay.");
    }

    [AtlasScenario]
    [Trait("Layer", "Xp")]
    [Trait("Kind", "Metalworking")]
    public async Task HeatTreat_Should_PayOneEach_ForQuenchAndTemper()
    {
        ITestPlayer joined = await World.JoinPlayer("MwHeatTreat");
        IPlayer player = joined.Player;
        IPlayerProgress progress = RequireProgress(player);
        IWorldAccessor world = World.Api.World;

        Item? pick = world.GetItem(new AssetLocation("game:pickaxehead-iron"));
        Assert.NotNull(pick);
        Assert.NotNull(pick!.GetBehavior<CollectibleBehaviorQuenchable>());

        ItemStack loose = new(pick, 1);
        float beforeLoose = ScenarioXp.TotalSkill(progress, Skill);
        SettleHeatTreat(world, loose, "quench", "lastinquenchrangetotalhours", world.Calendar.ElapsedHours);
        Assert.Equal(1, loose.Attributes.GetInt("quenchIteration", 0));
        Assert.Equal(beforeLoose, ScenarioXp.TotalSkill(progress, Skill));

        ItemSlot hand = player.InventoryManager.ActiveHotbarSlot;
        Assert.NotNull(hand);
        hand.Itemstack = new ItemStack(pick, 1);
        hand.MarkDirty();
        ItemStack stack = hand.Itemstack!;
        Assert.NotNull(stack.Collectible.GetBehavior<CollectibleBehaviorQuenchable>());

        string? target = EventFactBuilder.CodeOf(stack);
        string caller = EventFactBuilder.CallerOrHand(player);
        float expectedQuench = ScenarioXp.PlannedDeed(
            world,
            Skill,
            player.PlayerUID,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { DeedTokenTags.Quenched },
            caller,
            target);
        float expectedTemper = ScenarioXp.PlannedDeed(
            world,
            Skill,
            player.PlayerUID,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { DeedTokenTags.Tempered },
            caller,
            target);
        Assert.True(expectedQuench > 0f && expectedTemper > 0f);

        float beforeQuench = ScenarioXp.TotalSkill(progress, Skill);
        SettleHeatTreat(world, stack, "quench", "lastinquenchrangetotalhours", world.Calendar.ElapsedHours - 1.0);
        Assert.Equal(0, stack.Attributes.GetInt("quenchIteration", 0));
        Assert.Equal(beforeQuench, ScenarioXp.TotalSkill(progress, Skill));

        SettleHeatTreat(world, stack, "quench", "lastinquenchrangetotalhours", world.Calendar.ElapsedHours);
        Assert.Equal(1, stack.Attributes.GetInt("quenchIteration", 0));
        ScenarioXp.AssertPaid(
            ScenarioXp.TotalSkill(progress, Skill) - beforeQuench,
            expectedQuench,
            "quench");

        float beforeEarlyTemper = ScenarioXp.TotalSkill(progress, Skill);
        SettleHeatTreat(world, stack, "temper", "lastintemperrangetotalhours", world.Calendar.ElapsedHours);
        Assert.Equal(0, stack.Attributes.GetInt("temperIteration", 0));
        Assert.Equal(beforeEarlyTemper, ScenarioXp.TotalSkill(progress, Skill));

        float beforeTemper = ScenarioXp.TotalSkill(progress, Skill);
        SettleHeatTreat(world, stack, "temper", "lastintemperrangetotalhours", world.Calendar.ElapsedHours - 2.0);
        Assert.Equal(1, stack.Attributes.GetInt("temperIteration", 0));
        ScenarioXp.AssertPaid(
            ScenarioXp.TotalSkill(progress, Skill) - beforeTemper,
            expectedTemper,
            "temper");

        float beforeRepeat = ScenarioXp.TotalSkill(progress, Skill);
        SettleHeatTreat(world, stack, "temper", "lastintemperrangetotalhours", world.Calendar.ElapsedHours - 2.0);
        Assert.Equal(1, stack.Attributes.GetInt("temperIteration", 0));
        Assert.Equal(beforeRepeat, ScenarioXp.TotalSkill(progress, Skill));
    }

    static void SettleHeatTreat(
        IWorldAccessor world,
        ItemStack stack,
        string state,
        string hoursAttr,
        double hours)
    {
        stack.Attributes.SetString("metalworkingstate", state);
        stack.Attributes.SetDouble(hoursAttr, hours);
        stack.Collectible.SetTemperature(world, stack, 20f, false);
    }

    BlockEntityAnvil PrepareAnvil(IPlayer player, out SmithingRecipe recipe, out List<Vec3i> goods)
    {
        ICoreAPI api = World.Api;
        IWorldAccessor world = api.World;

        Block? anvilBlock = world.GetBlock(new AssetLocation("game:anvil-copper"))
            ?? world.GetBlock(new AssetLocation("game:anvil-bronze"))
            ?? world.GetBlock(new AssetLocation("game:anvil-iron"));
        Assert.NotNull(anvilBlock);

        BlockPos pos = player.Entity.Pos.AsBlockPos.AddCopy(2, 0, 0);
        world.BlockAccessor.SetBlock(0, pos);
        world.BlockAccessor.SetBlock(anvilBlock.BlockId, pos);

        BlockEntityAnvil? anvil = world.BlockAccessor.GetBlockEntity(pos) as BlockEntityAnvil;
        Assert.NotNull(anvil);

        List<SmithingRecipe> recipes = api.GetSmithingRecipes();
        Assert.NotEmpty(recipes);
        recipe = recipes
            .OrderByDescending(CountWanted)
            .First(r => CountWanted(r) >= 10 && r.Voxels != null);

        ItemStack? work = recipe.Output?.ResolvedItemstack?.Clone();
        if (work == null)
        {
            Item? copperIngot = world.GetItem(new AssetLocation("game:ingot-copper"));
            Assert.NotNull(copperIngot);
            work = new ItemStack(copperIngot, 1);
        }

        anvil.SelectedRecipeId = recipe.RecipeId;
        typeof(BlockEntityAnvil)
            .GetField("workItemStack", System.Reflection.BindingFlags.Instance
                | System.Reflection.BindingFlags.NonPublic)
            !.SetValue(anvil, work);

        Assert.NotNull(anvil.SelectedRecipe);
        Assert.NotNull(anvil.recipeVoxels);

        anvil.Voxels = new byte[16, 6, 16];
        AnvilXpStation.NoteBaseline(anvil);
        goods = CollectGoodVoxels(anvil.recipeVoxels!, Math.Min(6, recipe.QuantityLayers), max: 12);
        Assert.NotEmpty(goods);
        return anvil;
    }

    static List<Vec3i> CollectGoodVoxels(bool[,,] want, int layers, int max)
    {
        List<Vec3i> list = new();
        int ySize = Math.Min(want.GetLength(1), layers);
        for (int y = 0; y < ySize && list.Count < max; y++)
        {
            for (int x = 0; x < want.GetLength(0) && list.Count < max; x++)
            {
                for (int z = 0; z < want.GetLength(2) && list.Count < max; z++)
                {
                    if (want[x, y, z])
                    {
                        list.Add(new Vec3i(x, y, z));
                    }
                }
            }
        }

        return list;
    }

    static int CountWanted(SmithingRecipe recipe)
    {
        if (recipe.Voxels == null)
        {
            return 0;
        }

        return ClayFormingRecipeCatalog.CountWantedVoxels(
            recipe.Voxels,
            Math.Min(6, recipe.QuantityLayers));
    }

    static string RequireCollectionCode(ICoreAPI api, string collectionId)
    {
        CollectionIndex? collections = ProsequorModSystem.For(api)?.Collections?.Index;
        Assert.NotNull(collections);
        foreach (string code in collections!.Codes(collectionId))
        {
            if (!string.IsNullOrWhiteSpace(code))
            {
                return code;
            }
        }

        Assert.Fail($"Collection '{collectionId}' has no codes.");
        return "";
    }

    static IEnumerable<string> RecipeOutputCodes(SmithingRecipe recipe)
    {
        string? outputCode = recipe.Output?.Code?.ToString();
        if (!string.IsNullOrWhiteSpace(outputCode))
        {
            yield return outputCode.Trim();
        }

        string? resolved = recipe.Output?.ResolvedItemstack?.Collectible?.Code?.ToString();
        if (!string.IsNullOrWhiteSpace(resolved))
        {
            yield return resolved.Trim();
        }
    }

    static InventoryCraftingGrid RequireCraftGrid(IPlayer player)
    {
        IInventory? raw = player.InventoryManager.GetOwnInventory(GlobalConstants.craftingInvClassName);
        Assert.NotNull(raw);
        Assert.True(raw is InventoryCraftingGrid, $"Expected InventoryCraftingGrid, got {raw.GetType().Name}");
        return (InventoryCraftingGrid)raw;
    }

    static IPlayerProgress RequireProgress(IPlayer player)
    {
        EntityBehaviorProgress? progress = player.Entity?.GetBehavior<EntityBehaviorProgress>();
        Assert.NotNull(progress);
        return progress!;
    }

    static EntityBehaviorProgress RequireBehavior(IPlayer player)
    {
        EntityBehaviorProgress? progress = player.Entity?.GetBehavior<EntityBehaviorProgress>();
        Assert.NotNull(progress);
        return progress!;
    }
}
