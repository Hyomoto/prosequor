using System.Reflection;
using Atlas.Api;
using Atlas.XUnit;
using Prosequor.Ability;
using Prosequor.Inventory;
using Prosequor.Player;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;
using Xunit;

namespace Prosequor.Scenarios;

/// <summary>
/// Bloom Brigand sneak-extract. Finished goods always come out. The break
/// roll decides whether the bloomery breaks with them.
/// </summary>
public class BloomeryHarvestScenarios : AtlasScenarioBase
{
    const string Skill = "metalworking";
    const int GlassBatch = 12;

    [AtlasScenario]
    [Trait("Layer", "Action")]
    [Trait("Kind", "Metalworking")]
    public async Task BloomBrigand_Should_KeepProduct_And_MaybeBreakBloomery()
    {
        ITestPlayer joined = await World.JoinPlayer("MwBrigandGlass");
        IPlayer player = joined.Player;
        IPlayerProgress progress = RequireProgress(player);
        GrantBloomBrigand(progress);
        PrepareHands(player);

        IWorldAccessor world = World.Api.World;
        Block bloomery = RequireBloomery(world);
        Block glassBlock = RequireBlock(world, "game:glass-plain");
        Item bloomItem = RequireItem(world, "game:ironbloom");
        Item charcoal = RequireItem(world, "game:charcoal");
        Assert.True(BloomeryHarvestStation.ResolveAllowRightClickHarvest(player, (BlockBloomery)bloomery, player.Entity!.Pos.AsBlockPos));
        Assert.Equal(
            0.25f,
            BloomeryHarvestStation.ResolveRightClickBreakChance(player, (BlockBloomery)bloomery, player.Entity.Pos.AsBlockPos),
            precision: 3);

        SetBreakRoll(1d);
        try
        {
            BlockPos keptPos = PlaceBloomery(player, bloomery, offset: 2);
            BlockEntityBloomery keptBe = RequireBloomeryEntity(keptPos);
            ItemStack glass = HotStack(world, new ItemStack(glassBlock, GlassBatch));
            PutOutput(keptBe, glass);
            PutFuel(keptBe, new ItemStack(charcoal, 3));

            float before = ScenarioXp.TotalSkill(progress, Skill);
            float expected = ScenarioXp.PlannedBloomeryHarvest(
                world,
                Skill,
                player.PlayerUID,
                EventFactBuilder.CodeOf(bloomery),
                EventFactBuilder.CodeOf(glass),
                GlassBatch);
            int glassBefore = CountProduct(player, "glass");
            int fuelBefore = CountProduct(player, "charcoal");

            Assert.True(Click(player, bloomery, keptPos), "Bloom Brigand should handle the sneak click.");
            Assert.Equal(bloomery.Id, world.BlockAccessor.GetBlock(keptPos).Id);
            AssertOutputEmpty(keptBe);
            Assert.Equal(3, FuelStack(keptBe)?.StackSize ?? 0);
            Assert.Equal(glassBefore + GlassBatch, CountProduct(player, "glass"));
            Assert.Equal(fuelBefore, CountProduct(player, "charcoal"));
            ScenarioXp.AssertPaid(
                ScenarioXp.TotalSkill(progress, Skill) - before,
                expected,
                "bloomery extract");

            SetBreakRoll(0d);
            BlockPos brokenPos = PlaceBloomery(player, bloomery, offset: 4);
            BlockEntityBloomery brokenBe = RequireBloomeryEntity(brokenPos);
            ItemStack brokenGlass = HotStack(world, new ItemStack(glassBlock, GlassBatch));
            PutOutput(brokenBe, brokenGlass);
            PutFuel(brokenBe, new ItemStack(charcoal, 2));

            float beforeBreak = ScenarioXp.TotalSkill(progress, Skill);
            float expectedBreak = ScenarioXp.PlannedBloomeryHarvest(
                world,
                Skill,
                player.PlayerUID,
                EventFactBuilder.CodeOf(bloomery),
                EventFactBuilder.CodeOf(brokenGlass),
                GlassBatch);
            int glassBeforeBreak = CountProduct(player, "glass");
            int fuelBeforeBreak = CountProduct(player, "charcoal");

            Assert.True(Click(player, bloomery, brokenPos));
            Assert.NotEqual(bloomery.Id, world.BlockAccessor.GetBlock(brokenPos).Id);
            Assert.Equal(glassBeforeBreak + GlassBatch, CountProduct(player, "glass"));
            Assert.Equal(fuelBeforeBreak + 2, CountProduct(player, "charcoal"));
            ScenarioXp.AssertPaid(
                ScenarioXp.TotalSkill(progress, Skill) - beforeBreak,
                expectedBreak,
                "bloomery break on failed extract");

            BlockPos bloomPos = PlaceBloomery(player, bloomery, offset: 6);
            BlockEntityBloomery bloomBe = RequireBloomeryEntity(bloomPos);
            ItemStack bloom = HotStack(world, new ItemStack(bloomItem, 1));
            PutOutput(bloomBe, bloom);
            int ironBefore = CountProduct(player, "ironbloom");
            Assert.True(Click(player, bloomery, bloomPos));
            Assert.NotEqual(bloomery.Id, world.BlockAccessor.GetBlock(bloomPos).Id);
            Assert.Equal(ironBefore + 1, CountProduct(player, "ironbloom"));
        }
        finally
        {
            SetBreakRoll(null);
        }
    }

    [AtlasScenario]
    [Trait("Layer", "Action")]
    [Trait("Kind", "Metalworking")]
    public async Task BloomBrigand_Should_LeaveFinishedGoods_WithoutUnlock()
    {
        ITestPlayer joined = await World.JoinPlayer("MwBrigandLocked");
        IPlayer player = joined.Player;
        PrepareHands(player);

        IWorldAccessor world = World.Api.World;
        Block bloomery = RequireBloomery(world);
        Block glassBlock = RequireBlock(world, "game:glass-plain");
        BlockPos pos = PlaceBloomery(player, bloomery, offset: 2);
        BlockEntityBloomery be = RequireBloomeryEntity(pos);
        PutOutput(be, HotStack(world, new ItemStack(glassBlock, GlassBatch)));
        Assert.False(BloomeryHarvestStation.ResolveAllowRightClickHarvest(player, (BlockBloomery)bloomery, pos));

        SetBreakRoll(0d);
        try
        {
            Click(player, bloomery, pos);
            ItemStack? left = BloomeryHarvestXp.TryGetOutStack(be);
            Assert.Equal(GlassBatch, left?.StackSize ?? 0);
            Assert.Contains("glass", left?.Collectible?.Code?.Path ?? "", StringComparison.Ordinal);
            Assert.Equal(0, CountProduct(player, "glass"));
        }
        finally
        {
            SetBreakRoll(null);
        }
    }

    [AtlasScenario]
    [Trait("Layer", "Action")]
    [Trait("Kind", "Metalworking")]
    public async Task BreakingBloomery_Should_DropFinishedGlass()
    {
        ITestPlayer joined = await World.JoinPlayer("MwBrigandBreak");
        IPlayer player = joined.Player;
        Assert.NotNull(player.WorldData);
        player.WorldData.CurrentGameMode = EnumGameMode.Survival;

        IWorldAccessor world = World.Api.World;
        Block bloomery = RequireBloomery(world);
        Block glassBlock = RequireBlock(world, "game:glass-plain");
        BlockPos pos = PlaceBloomery(player, bloomery, offset: 3);
        BlockEntityBloomery be = RequireBloomeryEntity(pos);
        PutOutput(be, HotStack(world, new ItemStack(glassBlock, GlassBatch)));

        bloomery.OnBlockBroken(world, pos, player, 1f);

        Assert.Equal(GlassBatch, CountProduct(player, "glass"));
    }

    static void GrantBloomBrigand(IPlayerProgress progress)
    {
        progress.AddUnlockPoints(5);
        progress.SetPlayerLevel(20);
        progress.SetSkillLevel(Skill, 10);
        Assert.True(progress.GrantUnlock(Skill, "bloom-brigand"), "Expected bloom-brigand to grant at skill level 10.");
    }

    static void PrepareHands(IPlayer player)
    {
        Assert.NotNull(player.WorldData);
        player.WorldData.CurrentGameMode = EnumGameMode.Survival;
        SetSneak(player, sneak: true);
        ItemSlot? hotbar = player.InventoryManager.ActiveHotbarSlot;
        Assert.NotNull(hotbar);
        hotbar!.Itemstack = null;
        hotbar.MarkDirty();
    }

    BlockPos PlaceBloomery(IPlayer player, Block bloomery, int offset)
    {
        Assert.NotNull(player.Entity);
        BlockPos pos = player.Entity.Pos.AsBlockPos.AddCopy(offset, 0, 0);
        IWorldAccessor world = World.Api.World;
        world.BlockAccessor.SetBlock(0, pos);
        world.BlockAccessor.SetBlock(bloomery.BlockId, pos);
        Assert.Equal(bloomery.Id, world.BlockAccessor.GetBlock(pos).Id);
        return pos;
    }

    BlockEntityBloomery RequireBloomeryEntity(BlockPos pos)
    {
        BlockEntity? raw = World.Api.World.BlockAccessor.GetBlockEntity(pos);
        Assert.NotNull(raw);
        Assert.IsType<BlockEntityBloomery>(raw);
        return (BlockEntityBloomery)raw!;
    }

    static void PutOutput(BlockEntityBloomery be, ItemStack stack)
    {
        ItemSlot? slot = BloomeryHarvestXp.TryGetOutSlot(be);
        Assert.NotNull(slot);
        slot!.Itemstack = stack;
        Assert.True(BloomeryHarvestXp.HasFinishedBloom(be));
    }

    static void PutFuel(BlockEntityBloomery be, ItemStack stack)
    {
        ItemSlot? output = BloomeryHarvestXp.TryGetOutSlot(be);
        Assert.NotNull(output?.Inventory);
        Assert.True(output!.Inventory.Count > 0);
        output.Inventory[0].Itemstack = stack;
    }

    static ItemStack? FuelStack(BlockEntityBloomery be)
    {
        ItemSlot? output = BloomeryHarvestXp.TryGetOutSlot(be);
        return output?.Inventory[0].Itemstack;
    }

    static bool Click(IPlayer player, Block bloomery, BlockPos pos) =>
        bloomery.OnBlockInteractStart(
            player.Entity!.World,
            player,
            new BlockSelection
            {
                Position = pos,
                Face = BlockFacing.UP,
                HitPosition = new Vec3d(0.5, 0.5, 0.5),
                Block = bloomery
            });

    static ItemStack HotStack(IWorldAccessor world, ItemStack stack)
    {
        stack.Collectible!.SetTemperature(world, stack, 900f, true);
        return stack;
    }

    static void AssertOutputEmpty(BlockEntityBloomery be)
    {
        ItemSlot? slot = BloomeryHarvestXp.TryGetOutSlot(be);
        Assert.NotNull(slot);
        Assert.True(slot!.Empty, "Finished goods should leave the bloomery output slot.");
    }

    /// <summary>
    /// Atlas loads the mod dll in its own context, so the scenario's static and the
    /// patched static are not the same object. Write every loaded copy.
    /// </summary>
    static void SetBreakRoll(double? roll)
    {
        BloomeryHarvestXp.BreakRollForTests = roll;
        foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (!string.Equals(asm.GetName().Name, "prosequor", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            Type? type = asm.GetType("Prosequor.Ability.BloomeryHarvestXp");
            PropertyInfo? prop = type?.GetProperty(
                "BreakRollForTests",
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            prop?.SetValue(null, roll);
        }
    }

    int CountProduct(IPlayer player, string pathPart)
    {
        int total = 0;
        total += CountIn(player.InventoryManager?.GetHotbarInventory(), pathPart);
        total += CountIn(player.InventoryManager?.GetOwnInventory(GlobalConstants.backpackInvClassName), pathPart);
        total += CountIn(player.InventoryManager?.GetOwnInventory(GlobalConstants.mousecursorInvClassName), pathPart);
        total += CountIn(player.InventoryManager?.GetOwnInventory("character"), pathPart);
        total += CountIn(player.InventoryManager?.GetOwnInventory(ProsequorCarryInventory.InventoryClassName), pathPart);

        Assert.NotNull(player.Entity);
        Vec3d origin = player.Entity.Pos.XYZ;
        foreach (Entity entity in World.Api.World.GetEntitiesAround(origin, 16f, 16f, e => e is EntityItem))
        {
            if (entity is EntityItem item && Matches(item.Itemstack, pathPart))
            {
                total += item.Itemstack!.StackSize;
            }
        }

        return total;
    }

    static int CountIn(IInventory? inv, string pathPart)
    {
        if (inv == null)
        {
            return 0;
        }

        int total = 0;
        for (int i = 0; i < inv.Count; i++)
        {
            ItemSlot? slot = inv[i];
            ItemStack? stack = slot?.Itemstack;
            if (Matches(stack, pathPart))
            {
                total += stack!.StackSize;
            }
        }

        return total;
    }

    static bool Matches(ItemStack? stack, string pathPart) =>
        stack?.Collectible?.Code?.Path?.Contains(pathPart, StringComparison.Ordinal) == true;

    Block RequireBloomery(IWorldAccessor world)
    {
        Block? bloomery = world.GetBlock(new AssetLocation("game:bloomerybase-north"));
        Assert.NotNull(bloomery);
        Assert.IsType<BlockBloomery>(bloomery);
        return bloomery!;
    }

    static Block RequireBlock(IWorldAccessor world, string code)
    {
        Block? block = world.GetBlock(new AssetLocation(code));
        Assert.NotNull(block);
        Assert.NotEqual(0, block!.Id);
        return block;
    }

    static Item RequireItem(IWorldAccessor world, string code)
    {
        Item? item = world.GetItem(new AssetLocation(code));
        Assert.NotNull(item);
        return item!;
    }

    static void SetSneak(IPlayer player, bool sneak)
    {
        Assert.NotNull(player.Entity?.Controls);
        player.Entity.Controls.Sneak = sneak;
        if (player.WorldData?.EntityControls != null)
        {
            player.WorldData.EntityControls.Sneak = sneak;
        }
    }

    static IPlayerProgress RequireProgress(IPlayer player)
    {
        EntityBehaviorProgress? progress = player.Entity?.GetBehavior<EntityBehaviorProgress>();
        Assert.NotNull(progress);
        return progress!;
    }
}
