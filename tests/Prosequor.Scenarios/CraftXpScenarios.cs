using Atlas.Api;
using Atlas.XUnit;
using Prosequor;
using Prosequor.Ability;
using Prosequor.Player;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.Common;
using Xunit;

namespace Prosequor.Scenarios;

/// <summary>
/// Live craft-grid takes. Documents vanilla <c>CraftMany</c> MovedQuantity quirks and
/// asserts Prosequor craft XP scales with output quantity (stack × completions), not return value.
/// </summary>
public class CraftXpScenarios : AtlasScenarioBase
{
    [AtlasScenario]
    [Trait("Layer", "Xp")]
    [Trait("Kind", "Craft")]
    public async Task CraftXp_Should_ScaleWithMultiCraft_When_ShiftTakingFirewood()
    {
        ITestPlayer joined = await World.JoinPlayer("CraftMulti");
        IPlayer player = joined.Player;
        IPlayerProgress progress = RequireProgress(player);

        Item? axe = World.Api.World.GetItem(new AssetLocation("game:axe-copper"))
            ?? World.Api.World.GetItem(new AssetLocation("game:axe-flint"));
        Block? log = World.Api.World.GetBlock(new AssetLocation("game:log-placed-oak-ud"))
            ?? World.Api.World.GetBlock(new AssetLocation("game:log-placed-pine-ud"));
        Assert.NotNull(axe);
        Assert.NotNull(log);

        IInventory? raw = player.InventoryManager.GetOwnInventory(GlobalConstants.craftingInvClassName);
        Assert.NotNull(raw);
        Assert.True(raw is InventoryCraftingGrid, $"Expected InventoryCraftingGrid, got {raw.GetType().Name}");
        InventoryCraftingGrid craft = (InventoryCraftingGrid)raw;

        // Pattern "A/L" width 1 height 2 → slots 0 (axe) and 3 (logs).
        craft[0].Itemstack = new ItemStack(axe, 1);
        craft[3].Itemstack = new ItemStack(log, 5);
        craft.OnItemSlotModified(craft[0]);
        craft.OnItemSlotModified(craft[3]);

        ItemSlot output = craft[craft.Count - 1];
        Assert.False(output.Empty, "Expected a matching firewood recipe in the output slot.");
        Assert.Equal("firewood", output.Itemstack?.Collectible?.Code?.Path);

        int outputPerCraft = output.Itemstack!.StackSize;
        Assert.True(outputPerCraft > 0);

        ItemStack perCraft = output.Itemstack!.Clone();
        float xpBefore = ScenarioXp.TotalSkill(progress, "forestry");

        ItemSlot sink = new DummySlot();
        ItemStackMoveOperation op = new(
            World.Api.World,
            EnumMouseButton.Left,
            EnumModifierKey.SHIFT,
            EnumMergePriority.AutoMerge,
            requestedQuantity: 999);
        op.ActingPlayer = player;

        int moved = output.TryPutInto(sink, ref op);

        // Vanilla CraftMany returns only the last craft's MovedQuantity.
        Assert.True(
            moved <= outputPerCraft,
            $"Expected vanilla MovedQuantity ≤ one craft ({outputPerCraft}), got {moved}.");

        int firewoodGot = sink.Itemstack?.StackSize ?? 0;
        Assert.True(
            firewoodGot >= outputPerCraft * 5,
            $"Expected ≥ {outputPerCraft * 5} firewood from 5 logs, got {firewoodGot}.");

        int crafts = firewoodGot / outputPerCraft;
        float gained = ScenarioXp.TotalSkill(progress, "forestry") - xpBefore;
        float expected = ScenarioXp.PlannedGridTake(
            World.Api.World,
            "forestry",
            player,
            perCraft,
            crafts,
            unitsPerCraft: 1);
        ScenarioXp.AssertPaid(
            gained,
            expected,
            $"{outputPerCraft}×{crafts} firewood (moved={moved})");
    }

    [AtlasScenario]
    [Trait("Layer", "Xp")]
    [Trait("Kind", "Craft")]
    public async Task CraftXp_Should_PayTailoring_When_CraftingLinen()
    {
        ITestPlayer joined = await World.JoinPlayer("CraftLinen");
        IPlayer player = joined.Player;
        IPlayerProgress progress = RequireProgress(player);

        Item? twine = World.Api.World.GetItem(new AssetLocation("game:flaxtwine"));
        Assert.NotNull(twine);

        IInventory? raw = player.InventoryManager.GetOwnInventory(GlobalConstants.craftingInvClassName);
        Assert.NotNull(raw);
        Assert.True(raw is InventoryCraftingGrid, $"Expected InventoryCraftingGrid, got {raw.GetType().Name}");
        InventoryCraftingGrid craft = (InventoryCraftingGrid)raw;

        // Pattern "FF/FF" width 2 height 2 → slots 0,1,3,4.
        craft[0].Itemstack = new ItemStack(twine, 1);
        craft[1].Itemstack = new ItemStack(twine, 1);
        craft[3].Itemstack = new ItemStack(twine, 1);
        craft[4].Itemstack = new ItemStack(twine, 1);
        craft.OnItemSlotModified(craft[0]);
        craft.OnItemSlotModified(craft[1]);
        craft.OnItemSlotModified(craft[3]);
        craft.OnItemSlotModified(craft[4]);

        ItemSlot output = craft[craft.Count - 1];
        Assert.False(output.Empty, "Expected a matching linen recipe in the output slot.");
        Assert.Equal("linen-normal-down", output.Itemstack?.Collectible?.Code?.Path);

        ItemStack perCraft = output.Itemstack!.Clone();
        float xpBefore = ScenarioXp.TotalSkill(progress, "tailoring");

        ItemSlot sink = new DummySlot();
        ItemStackMoveOperation op = new(
            World.Api.World,
            EnumMouseButton.Left,
            0,
            EnumMergePriority.AutoMerge,
            requestedQuantity: 1);
        op.ActingPlayer = player;

        int moved = output.TryPutInto(sink, ref op);
        Assert.True(moved > 0, "Expected to take the linen output.");

        float gained = ScenarioXp.TotalSkill(progress, "tailoring") - xpBefore;
        float expected = ScenarioXp.PlannedGridTake(
            World.Api.World,
            "tailoring",
            player,
            perCraft,
            craftCount: 1,
            unitsPerCraft: 4);
        ScenarioXp.AssertPaid(gained, expected, "one linen");
    }

    [AtlasScenario]
    [Trait("Layer", "Xp")]
    [Trait("Kind", "Craft")]
    public async Task CraftXp_Should_PayTailoring_When_BarrelEmitsLeather()
    {
        ITestPlayer joined = await World.JoinPlayer("TanLeather");
        IPlayer player = joined.Player;
        IPlayerProgress progress = RequireProgress(player);

        Item? leather = World.Api.World.GetItem(new AssetLocation("game:leather-normal-plain"));
        Assert.NotNull(leather);

        ItemStack stack = new(leather, 3);
        float xpBefore = ScenarioXp.TotalSkill(progress, "tailoring");
        CraftedProductXp.Emit(World.Api.World, player.PlayerUID, stack, 3);
        float gained = ScenarioXp.TotalSkill(progress, "tailoring") - xpBefore;
        float expected = ScenarioXp.PlannedCrafted(
            World.Api.World,
            "tailoring",
            player.PlayerUID,
            stack,
            quantity: 3);
        ScenarioXp.AssertPaid(gained, expected, "3 leather");
    }

    [AtlasScenario]
    [Trait("Layer", "Xp")]
    [Trait("Kind", "Craft")]
    public async Task CraftXp_Should_PayTailoring_When_BarrelEmitsPreparedHide()
    {
        ITestPlayer joined = await World.JoinPlayer("ShowHide");
        IPlayer player = joined.Player;
        IPlayerProgress progress = RequireProgress(player);

        Item? hide = World.Api.World.GetItem(new AssetLocation("game:hide-prepared-large"))
            ?? World.Api.World.GetItem(new AssetLocation("game:hide-soaked-large"));
        Assert.NotNull(hide);

        ItemStack stack = new(hide, 1);
        float xpBefore = ScenarioXp.TotalSkill(progress, "tailoring");
        CraftedProductXp.Emit(World.Api.World, player.PlayerUID, stack, 1);
        float gained = ScenarioXp.TotalSkill(progress, "tailoring") - xpBefore;
        float expected = ScenarioXp.PlannedCrafted(
            World.Api.World,
            "tailoring",
            player.PlayerUID,
            stack,
            quantity: 1);
        ScenarioXp.AssertPaid(gained, expected, "one processed hide");
    }

    static IPlayerProgress RequireProgress(IPlayer player)
    {
        IPlayerProgress? progress = ProsequorModSystem.GetProgress(player);
        Assert.NotNull(progress);
        return progress;
    }
}
