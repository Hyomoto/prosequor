using Atlas.Api;
using Atlas.XUnit;
using Prosequor;
using Prosequor.Ability;
using Prosequor.Ability.Hooks;
using Prosequor.Player;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.Common;
using Xunit;

namespace Prosequor.Scenarios;

/// <summary>
/// Live <c>crafting-interaction</c> / <c>mutate-output</c> takes.
/// Firewood (axe + log) is synthesis: floor preview in the output slot, fractional
/// StochasticRound remainder merged into the taken sink stack on pull.
/// </summary>
public class CraftMutateOutputScenarios : AtlasScenarioBase
{
    const string Skill = "forestry";
    const int CraftCount = 40;

    [AtlasScenario]
    [Trait("Layer", "Action")]
    [Trait("Kind", "CraftMutateOutput")]
    public async Task FirewoodCraft_Should_YieldExactVanilla_WithoutCleanSplitter()
    {
        ITestPlayer joined = await World.JoinPlayer("CraftQtyBase");
        IPlayer player = joined.Player;
        _ = RequireProgress(player);

        (Item axe, Block log, InventoryCraftingGrid craft, int perCraft) = SetupFirewoodRecipe(player);

        Assert.False(
            CraftMutateOutputStation.TryGetRecipeBase(craft[craft.Count - 1].Itemstack, out _),
            "Expected no craft-base stamp without a quantity bonus.");

        int got = CraftFirewoodSingles(player, craft, axe, log, CraftCount, out int crafted);

        Assert.Equal(CraftCount, crafted);
        Assert.Equal(perCraft * CraftCount, got);
        Assert.False(craft[0].Empty, "Expected the axe to remain in the crafting grid.");
    }

    [AtlasScenario]
    [Trait("Layer", "Pedigree")]
    [Trait("Kind", "CraftMaker")]
    public async Task FirewoodCraft_Should_StampMakerOnPreviewAndTake()
    {
        ITestPlayer joined = await World.JoinPlayer("CraftMakerStamp");
        IPlayer player = joined.Player;
        _ = RequireProgress(player);

        (Item axe, Block log, InventoryCraftingGrid craft, _) = SetupFirewoodRecipe(player);
        ItemStack? preview = craft[craft.Count - 1].Itemstack;
        Assert.Equal(player.PlayerUID, CraftAttribution.TryGetMakerUid(preview));
        Assert.False(
            ItemAffixes.TryGetQuality(preview, out _),
            "Firewood is a block craft; the match preview must not carry a quality grade.");
        Assert.Empty(CraftAttributeMods.GetAll(preview));

        ItemSlot sink = new DummySlot();
        ItemStackMoveOperation op = new(
            World.Api.World,
            EnumMouseButton.Left,
            0,
            EnumMergePriority.AutoMerge,
            requestedQuantity: 999);
        op.ActingPlayer = player;
        int moved = craft[craft.Count - 1].TryPutInto(sink, ref op);
        Assert.True(moved > 0);
        Assert.Equal(player.PlayerUID, CraftAttribution.TryGetMakerUid(sink.Itemstack));
        Assert.False(craft[0].Empty, "Expected the axe to remain in the crafting grid.");
        Assert.NotNull(axe);
        Assert.NotNull(log);
    }

    [AtlasScenario]
    [Trait("Layer", "Action")]
    [Trait("Kind", "CraftMutateOutput")]
    public async Task CleanSplitter_Should_MergeExtrasIntoTakenStack_OnCraftTake()
    {
        ITestPlayer joined = await World.JoinPlayer("CraftQtyCS");
        IPlayer player = joined.Player;
        IPlayerProgress progress = RequireProgress(player);
        GrantCleanSplitterMax(progress);

        float qty = CraftMutateOutputStation.ResolveQuantity(
            player,
            new ItemStack(RequireFirewood(player)),
            recipeBase: 4);
        Assert.True(
            qty > 4f,
            $"Expected Clean Splitter to raise firewood quantity above the recipe base, got {qty}.");

        (Item axe, Block log, InventoryCraftingGrid craft, int previewSize) = SetupFirewoodRecipe(player);
        ItemStack? preview = craft[craft.Count - 1].Itemstack;
        Assert.True(
            CraftMutateOutputStation.TryGetRecipeBase(preview, out int recipeBase),
            "Expected recipe base stamped on the preview stack.");
        Assert.Equal(4, recipeBase);
        Assert.Equal(CraftMutateOutputStation.FloorQuantity(qty), previewSize);

        int bagsBefore = CountPlayerFirewood(player);
        int fromTakes = CraftFirewoodSingles(player, craft, axe, log, CraftCount, out int crafted);
        Assert.Equal(CraftCount, crafted);

        int floorTotal = previewSize * CraftCount;
        float remainder = qty - CraftMutateOutputStation.FloorQuantity(qty);
        if (remainder > 0.0001f)
        {
            Assert.True(
                fromTakes > floorTotal,
                $"Expected fractional extras on the taken sink above floor {floorTotal} from {CraftCount} crafts (qty={qty}), got {fromTakes}.");
        }
        else
        {
            Assert.Equal(floorTotal, fromTakes);
        }

        Assert.True(
            CountPlayerFirewood(player) == bagsBefore,
            $"Yield extras must merge into the taken stack, not a separate inventory grant. bagsBefore={bagsBefore}, bagsAfter={CountPlayerFirewood(player)}.");
        Assert.False(craft[0].Empty, "Expected the axe to remain in the crafting grid.");
    }

    [AtlasScenario]
    [Trait("Layer", "Action")]
    [Trait("Kind", "CraftMutateOutput")]
    public async Task UnlockCleanSplitter_Should_RematchOpenFirewoodGrid()
    {
        ITestPlayer joined = await World.JoinPlayer("CraftQtyUnlockCS");
        IPlayer player = joined.Player;
        IPlayerProgress progress = RequireProgress(player);

        (_, _, InventoryCraftingGrid craft, int vanillaSize) = SetupFirewoodRecipe(player);
        ItemStack? before = craft[craft.Count - 1].Itemstack;
        Assert.False(
            CraftMutateOutputStation.TryGetRecipeBase(before, out _),
            "Expected no craft-base stamp before Clean Splitter.");
        Assert.Equal(vanillaSize, before!.StackSize);

        progress.AddUnlockPoints(5);
        progress.SetSkillLevel(Skill, 15);
        Assert.True(progress.GrantUnlock(Skill, "seasoned-logger"));
        Assert.True(progress.GrantUnlock(Skill, "cleansplitter"));

        ItemStack? after = craft[craft.Count - 1].Itemstack;
        Assert.False(after == null || after.Collectible?.Code?.Path != "firewood",
            "Expected firewood to remain matched after unlock rematch.");
        Assert.True(
            CraftMutateOutputStation.TryGetRecipeBase(after, out int recipeBase),
            "Expected unlock rematch to stamp recipe base without another OnItemSlotModified.");
        Assert.Equal(vanillaSize, recipeBase);

        float qty = CraftMutateOutputStation.ResolveQuantity(player, after, recipeBase);
        Assert.True(
            qty > recipeBase,
            $"Expected Clean Splitter rematch to raise quantity above recipe base {recipeBase}, got {qty}.");
        Assert.Equal(CraftMutateOutputStation.FloorQuantity(qty), after!.StackSize);
    }

    [AtlasScenario]
    [Trait("Layer", "Action")]
    [Trait("Kind", "CraftMutateOutput")]
    public async Task UnlockLumberjack_Should_NotRematchOpenFirewoodGrid()
    {
        ITestPlayer joined = await World.JoinPlayer("CraftQtyUnlockLJ");
        IPlayer player = joined.Player;
        IPlayerProgress progress = RequireProgress(player);

        (_, _, InventoryCraftingGrid craft, int vanillaSize) = SetupFirewoodRecipe(player);
        ItemStack? before = craft[craft.Count - 1].Itemstack;
        Assert.NotNull(before);
        Assert.False(CraftMutateOutputStation.TryGetRecipeBase(before, out _));

        progress.AddUnlockPoints(5);
        progress.SetSkillLevel(Skill, 15);
        Assert.True(progress.GrantUnlock(Skill, "seasoned-logger"));

        ItemStack? after = craft[craft.Count - 1].Itemstack;
        Assert.Same(before, after);
        Assert.False(
            CraftMutateOutputStation.TryGetRecipeBase(after, out _),
            "Lumberjack is mutate-drops only; firewood craft output must not rematch.");
        Assert.Equal(vanillaSize, after!.StackSize);
    }

    [AtlasScenario]
    [Trait("Layer", "Action")]
    [Trait("Kind", "CraftMutateOutput")]
    public async Task TwineCraft_Should_KeepRecipeBase_AcrossRematch()
    {
        ITestPlayer joined = await World.JoinPlayer("CraftQtyTwine");
        IPlayer player = joined.Player;
        IPlayerProgress progress = RequireProgress(player);
        progress.AddUnlockPoints(5);
        progress.SetSkillLevel("tailoring", 1);
        Assert.True(progress.GrantUnlock("tailoring", "threadmaker"));

        InventoryCraftingGrid craft = SetupTwineRecipe(player);
        ItemStack? preview = craft[craft.Count - 1].Itemstack;
        Assert.NotNull(preview);
        Assert.True(
            CraftMutateOutputStation.TryGetRecipeBase(preview, out int recipeBase),
            "Expected threadmaker to stamp the vanilla output count.");
        Assert.Equal(1, recipeBase);
        float qty = CraftMutateOutputStation.ResolveQuantity(player, preview, recipeBase);
        Assert.Equal(CraftMutateOutputStation.FloorQuantity(qty), preview!.StackSize);
        Assert.Equal(player.PlayerUID, CraftAttribution.TryGetMakerUid(preview));
        Assert.False(ItemAffixes.TryGetQuality(preview, out _));
        Assert.Empty(CraftAttributeMods.GetAll(preview));

        craft.OnItemSlotModified(craft[0]);
        ItemStack? rematch = craft[craft.Count - 1].Itemstack;
        Assert.NotNull(rematch);
        Assert.True(CraftMutateOutputStation.TryGetRecipeBase(rematch, out int rematchBase));
        Assert.Equal(recipeBase, rematchBase);
        Assert.Equal(preview.StackSize, rematch!.StackSize);
        Assert.Equal(player.PlayerUID, CraftAttribution.TryGetMakerUid(rematch));
        Assert.False(ItemAffixes.TryGetQuality(rematch, out _));
        Assert.Empty(CraftAttributeMods.GetAll(rematch));
    }

    [AtlasScenario]
    [Trait("Layer", "Action")]
    [Trait("Kind", "CraftMutateOutput")]
    public async Task GenerateOutputStack_Should_NotStamp_When_OutputIsDummySlot()
    {
        ITestPlayer joined = await World.JoinPlayer("CraftQtyDummy");
        IPlayer player = joined.Player;
        _ = RequireProgress(player);

        GridRecipe recipe = RequireRecipe(player, "flaxtwine");
        ItemStack resolved = recipe.Output!.ResolvedItemStack!;
        DummySlot output = new();
        ItemSlot[] inputs = new ItemSlot[9];
        for (int i = 0; i < inputs.Length; i++)
        {
            inputs[i] = new DummySlot();
        }

        recipe.GenerateOutputStack(inputs, output);

        Assert.Equal("flaxtwine", output.Itemstack?.Collectible?.Code?.Path);
        Assert.Equal(resolved.StackSize, output.Itemstack!.StackSize);
        Assert.False(CraftMutateOutputStation.TryGetRecipeBase(output.Itemstack, out _));
        Assert.Null(CraftAttribution.TryGetMakerUid(output.Itemstack));
        Assert.False(ItemAffixes.TryGetQuality(output.Itemstack, out _));
        Assert.Empty(CraftAttributeMods.GetAll(output.Itemstack));
    }

    [AtlasScenario]
    [Trait("Layer", "Action")]
    [Trait("Kind", "CraftMutateOutput")]
    public async Task LeatherHelmet_Should_StampQualityOnce()
    {
        ITestPlayer joined = await World.JoinPlayer("CraftQtyHelmet");
        IPlayer player = joined.Player;
        IPlayerProgress progress = RequireProgress(player);
        GrantTailoringQuality(progress);

        InventoryCraftingGrid craft = SetupLeatherHelmetRecipe(player);
        AssertPreviewQuality(player, craft[craft.Count - 1].Itemstack);

        craft.OnItemSlotModified(craft[0]);
        AssertPreviewQuality(player, craft[craft.Count - 1].Itemstack);
    }

    [AtlasScenario]
    [Trait("Layer", "Action")]
    [Trait("Kind", "CraftMutateOutput")]
    public async Task LeatherHelmetRepair_Should_StampMakerWithoutQuality()
    {
        ITestPlayer joined = await World.JoinPlayer("CraftQtyRepair");
        IPlayer player = joined.Player;
        IPlayerProgress progress = RequireProgress(player);
        GrantTailoringQuality(progress);

        Item? helmet = player.Entity.World.GetItem(new AssetLocation("game:armor-head-sewn-leather"));
        Item? leather = player.Entity.World.GetItem(new AssetLocation("game:leather-normal-plain"));
        Assert.NotNull(helmet);
        Assert.NotNull(leather);

        InventoryCraftingGrid craft = RequireCraftGrid(player);
        craft[0].Itemstack = new ItemStack(helmet, 1);
        craft[1].Itemstack = new ItemStack(leather, 1);
        craft.OnItemSlotModified(craft[0]);
        craft.OnItemSlotModified(craft[1]);

        ItemStack? preview = craft[craft.Count - 1].Itemstack;
        Assert.Equal("armor-head-sewn-leather", preview?.Collectible?.Code?.Path);
        string? recipePath = craft.MatchingRecipe?.Name?.Path;
        Assert.NotNull(recipePath);
        Assert.Contains("repair", recipePath, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(player.PlayerUID, CraftAttribution.TryGetMakerUid(preview));
        Assert.False(
            CraftMutateOutputStation.TryGetRecipeBase(preview, out _),
            "Repair output quantity is unchanged, so the preview must not stamp a craft base.");
        Assert.False(
            ItemAffixes.TryGetQuality(preview, out _),
            "A repair recipe must skip the attribute pass.");
        Assert.Empty(CraftAttributeMods.GetAll(preview));
        Assert.Equal(1f, CraftAttributeMods.GetFactor(preview, "durability"));
    }

    static void AssertPreviewQuality(IPlayer player, ItemStack? preview)
    {
        Assert.NotNull(preview);
        Assert.Equal("armor-head-sewn-leather", preview!.Collectible?.Code?.Path);
        Assert.Equal(player.PlayerUID, CraftAttribution.TryGetMakerUid(preview));
        Assert.False(CraftMutateOutputStation.TryGetRecipeBase(preview, out _));
        Assert.True(
            ItemAffixes.TryGetQuality(preview, out _),
            "Expected one quality grade on the armor preview.");
        Assert.Equal(1, CountAffix(preview, ItemAffixes.QualityCode));
        Assert.True(
            CraftAttributeMods.GetFactor(preview, "durability") > 1f,
            "Expected a single durability mod from clothing proficiency.");
        Assert.Single(CraftAttributeMods.GetAll(preview));
    }

    static int CountAffix(ItemStack stack, string code)
    {
        int count = 0;
        foreach (ItemAffixEntry affix in ItemAffixes.GetAll(stack))
        {
            if (string.Equals(affix.Code, code, StringComparison.OrdinalIgnoreCase))
            {
                count++;
            }
        }

        return count;
    }

    static void GrantTailoringQuality(IPlayerProgress progress)
    {
        progress.AddUnlockPoints(30);
        progress.SetPlayerLevel(50);
        progress.SetSkillLevel("tailoring", 100);
        Assert.True(progress.GrantUnlock("tailoring", "threadmaker"));
        Assert.True(progress.GrantUnlock("tailoring", "textile-laborer"));
        Assert.True(progress.GrantUnlock("tailoring", "clothesmending"));
        Assert.True(progress.GrantUnlock("tailoring", "tailor"));
        Assert.True(progress.GrantUnlock("tailoring", "clothingproficiency"));
        Assert.True(progress.GrantUnlock("tailoring", "clothingproficiency"));
        Assert.True(progress.GrantUnlock("tailoring", "clothingproficiency"));
        Assert.Equal(3, progress.GetUnlockTier("tailoring", "clothingproficiency"));
    }

    static InventoryCraftingGrid SetupTwineRecipe(IPlayer player)
    {
        Item? fibers = player.Entity.World.GetItem(new AssetLocation("game:flaxfibers"));
        Assert.NotNull(fibers);
        InventoryCraftingGrid craft = RequireCraftGrid(player);
        craft[0].Itemstack = new ItemStack(fibers, 1);
        craft[1].Itemstack = new ItemStack(fibers, 1);
        craft[3].Itemstack = new ItemStack(fibers, 1);
        craft[4].Itemstack = new ItemStack(fibers, 1);
        craft.OnItemSlotModified(craft[0]);
        ItemSlot output = craft[craft.Count - 1];
        Assert.Equal("flaxtwine", output.Itemstack?.Collectible?.Code?.Path);
        return craft;
    }

    static InventoryCraftingGrid SetupLeatherHelmetRecipe(IPlayer player)
    {
        Item? leather = player.Entity.World.GetItem(new AssetLocation("game:leather-normal-plain"));
        Assert.NotNull(leather);
        InventoryCraftingGrid craft = RequireCraftGrid(player);
        // L K L / K _ K — shaped leather helmet, not the repair recipe.
        craft[0].Itemstack = new ItemStack(leather, 2);
        craft[1].Itemstack = new ItemStack(leather, 1);
        craft[2].Itemstack = new ItemStack(leather, 2);
        craft[3].Itemstack = new ItemStack(leather, 1);
        craft[5].Itemstack = new ItemStack(leather, 1);
        craft.OnItemSlotModified(craft[0]);
        ItemSlot output = craft[craft.Count - 1];
        Assert.Equal("armor-head-sewn-leather", output.Itemstack?.Collectible?.Code?.Path);
        string? recipePath = craft.MatchingRecipe?.Name?.Path;
        Assert.NotNull(recipePath);
        Assert.DoesNotContain("repair", recipePath, StringComparison.OrdinalIgnoreCase);
        return craft;
    }

    static InventoryCraftingGrid RequireCraftGrid(IPlayer player)
    {
        IInventory? raw = player.InventoryManager.GetOwnInventory(GlobalConstants.craftingInvClassName);
        Assert.NotNull(raw);
        Assert.True(raw is InventoryCraftingGrid, $"Expected InventoryCraftingGrid, got {raw.GetType().Name}");
        return (InventoryCraftingGrid)raw;
    }

    static GridRecipe RequireRecipe(IPlayer player, string outputPath)
    {
        foreach (GridRecipe recipe in player.Entity.World.GridRecipes)
        {
            string? path = recipe.Output?.ResolvedItemStack?.Collectible?.Code?.Path;
            if (string.Equals(path, outputPath, StringComparison.OrdinalIgnoreCase))
            {
                return recipe;
            }
        }

        Assert.Fail($"Expected a resolved grid recipe for {outputPath}.");
        return null!;
    }

    static void GrantCleanSplitterMax(IPlayerProgress progress)
    {
        progress.AddUnlockPoints(20);
        progress.SetSkillLevel(Skill, 50);
        Assert.True(progress.GrantUnlock(Skill, "seasoned-logger"));
        Assert.True(progress.GrantUnlock(Skill, "cleansplitter"));
        Assert.True(progress.GrantUnlock(Skill, "cleansplitter"));
        Assert.True(progress.GrantUnlock(Skill, "cleansplitter"));
        Assert.Equal(3, progress.GetUnlockTier(Skill, "cleansplitter"));
    }

    static Item RequireFirewood(IPlayer player)
    {
        Item? firewood = player.Entity.World.GetItem(new AssetLocation("game:firewood"));
        Assert.NotNull(firewood);
        return firewood!;
    }

    static (Item axe, Block log, InventoryCraftingGrid craft, int perCraft) SetupFirewoodRecipe(
        IPlayer player)
    {
        Item? axe = WorldItem(player, "game:axe-copper", "game:axe-flint");
        Block? log = WorldBlock(player, "game:log-placed-oak-ud", "game:log-placed-pine-ud");
        Assert.NotNull(axe);
        Assert.NotNull(log);

        IInventory? raw = player.InventoryManager.GetOwnInventory(GlobalConstants.craftingInvClassName);
        Assert.NotNull(raw);
        Assert.True(raw is InventoryCraftingGrid, $"Expected InventoryCraftingGrid, got {raw.GetType().Name}");
        InventoryCraftingGrid craft = (InventoryCraftingGrid)raw;

        // Pattern "A/L" width 1 height 2 → slots 0 (axe) and 3 (logs).
        craft[0].Itemstack = new ItemStack(axe, 1);
        craft[3].Itemstack = new ItemStack(log, 1);
        craft.OnItemSlotModified(craft[0]);
        craft.OnItemSlotModified(craft[3]);

        ItemSlot output = craft[craft.Count - 1];
        Assert.False(output.Empty, "Expected a matching firewood recipe in the output slot.");
        Assert.Equal("firewood", output.Itemstack?.Collectible?.Code?.Path);

        int perCraft = output.Itemstack!.StackSize;
        Assert.True(perCraft > 0);
        return (axe!, log!, craft, perCraft);
    }

    /// <summary>
    /// Single left-click takes (no shift). Returns firewood counted from the sink after each take
    /// (includes pull-time stochastic extras merged onto the taken stack).
    /// </summary>
    static int CraftFirewoodSingles(
        IPlayer player,
        InventoryCraftingGrid craft,
        Item axe,
        Block log,
        int count,
        out int completed)
    {
        ItemSlot sink = new DummySlot();
        int taken = 0;
        completed = 0;
        for (int i = 0; i < count; i++)
        {
            if (craft[0].Empty)
            {
                craft[0].Itemstack = new ItemStack(axe, 1);
                craft.OnItemSlotModified(craft[0]);
            }

            craft[3].Itemstack = new ItemStack(log, 1);
            craft.OnItemSlotModified(craft[3]);

            ItemSlot output = craft[craft.Count - 1];
            if (output.Empty || output.Itemstack?.Collectible?.Code?.Path != "firewood")
            {
                break;
            }

            // Keep the sink from filling to MaxStackSize and blocking further puts.
            if (!sink.Empty && sink.Itemstack != null)
            {
                taken += sink.Itemstack.StackSize;
                sink.Itemstack = null;
            }

            ItemStackMoveOperation op = new(
                player.Entity.World,
                EnumMouseButton.Left,
                (EnumModifierKey)0,
                EnumMergePriority.AutoMerge,
                requestedQuantity: output.StackSize);
            op.ActingPlayer = player;

            int moved = output.TryPutInto(sink, ref op);
            if (moved <= 0)
            {
                break;
            }

            completed++;
        }

        if (!sink.Empty && sink.Itemstack != null)
        {
            taken += sink.Itemstack.StackSize;
            sink.Itemstack = null;
        }

        return taken;
    }

    static int CountPlayerFirewood(IPlayer player)
    {
        int total = 0;
        total += CountFirewoodIn(player.InventoryManager?.GetHotbarInventory());
        total += CountFirewoodIn(player.InventoryManager?.GetOwnInventory(GlobalConstants.backpackInvClassName));
        total += CountFirewoodIn(player.InventoryManager?.GetOwnInventory(GlobalConstants.mousecursorInvClassName));
        total += CountFirewoodIn(player.InventoryManager?.GetOwnInventory("character"));
        return total;
    }

    static int CountFirewoodIn(IInventory? inv)
    {
        if (inv == null)
        {
            return 0;
        }

        int total = 0;
        for (int i = 0; i < inv.Count; i++)
        {
            ItemStack? stack = inv[i]?.Itemstack;
            if (IsFirewood(stack))
            {
                total += stack!.StackSize;
            }
        }

        return total;
    }

    static bool IsFirewood(ItemStack? stack) =>
        stack?.Collectible?.Code?.Path.Equals("firewood", StringComparison.OrdinalIgnoreCase) == true;

    static Item? WorldItem(IPlayer player, params string[] codes)
    {
        foreach (string code in codes)
        {
            Item? item = player.Entity.World.GetItem(new AssetLocation(code));
            if (item != null)
            {
                return item;
            }
        }

        return null;
    }

    static Block? WorldBlock(IPlayer player, params string[] codes)
    {
        foreach (string code in codes)
        {
            Block? block = player.Entity.World.GetBlock(new AssetLocation(code));
            if (block != null)
            {
                return block;
            }
        }

        return null;
    }

    static IPlayerProgress RequireProgress(IPlayer player)
    {
        IPlayerProgress? progress = ProsequorModSystem.GetProgress(player);
        Assert.NotNull(progress);
        return progress!;
    }
}
