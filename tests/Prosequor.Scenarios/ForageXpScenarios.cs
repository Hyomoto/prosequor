using Atlas.Api;
using Atlas.XUnit;
using Prosequor;
using Prosequor.Ability;
using Prosequor.Ability.Hooks;
using Prosequor.Player;
using Prosequor.Xp.Adapters;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;
using Xunit;

namespace Prosequor.Scenarios;

/// <summary>
/// Live forage XP: wild mushroom / stick break and berry interact harvest
/// (wild-state and unplanted ripe) should pay Forager; planted / placed sources should not.
/// Atlas shares one host per class (16 clients). Overflow tests use <c>FreshWorld</c>.
/// </summary>
public class ForageXpScenarios : AtlasScenarioBase
{
    const string Forager = "forager";
    const string Farming = "farming";

    [AtlasScenario]
    [Trait("Layer", "Xp")]
    [Trait("Kind", "Forage")]
    public async Task LiveForageSources_Should_ClassifyAndJoinCollections()
    {
        ITestPlayer joined = await World.JoinPlayer("ForageClassify");
        _ = joined.Player;

        CollectionIndex collections = RequireCollections();
        Block mushroom = RequireMushroom();
        Block stick = RequireLooseStick();
        Block horsetail = RequireHorsetail();
        Block sap = RequireSap();
        Block wildBush = RequireFruitingBush(preferWildState: true);
        Block fruitingBush = RequireFruitingBush(preferWildState: false);
        Block ripeHarvestable = RequireHarvestableBerryBush();

        Assert.True(ForageBlocks.IsMushroom(mushroom), $"Expected mushroom classify: {Code(mushroom)} ({mushroom.GetType().Name})");
        Assert.True(ForageBlocks.IsLooseStick(stick), $"Expected loose-stick classify: {Code(stick)} ({stick.GetType().Name})");
        Assert.True(ForageBlocks.IsReed(horsetail), $"Expected reed classify for horsetail: {Code(horsetail)} ({horsetail.GetType().Name})");
        Assert.True(ForageBlocks.IsFlatForage(horsetail), $"Expected flat forage for horsetail: {Code(horsetail)}");
        Assert.True(ForageBlocks.IsSap(sap), $"Expected sap classify: {Code(sap)} ({sap.GetType().Name})");
        Assert.True(ForageBlocks.IsFlatForage(sap), $"Expected flat forage for sap: {Code(sap)}");
        Assert.False(ForageBlocks.IsReed(sap), $"Sap must not join the reed collection classify: {Code(sap)}");
        Assert.True(AbilityBootstrap.IsBerryBushBlock(wildBush), $"Expected berry-bush classify: {Code(wildBush)}");
        Assert.True(AbilityBootstrap.IsBerryBushBlock(fruitingBush), $"Expected berry-bush classify: {Code(fruitingBush)}");
        Assert.True(AbilityBootstrap.IsBerryBushBlock(ripeHarvestable), $"Expected berry-bush classify: {Code(ripeHarvestable)}");

        Assert.Equal(BlockBreakClassification.TokenHarvest, BlockBreakClassification.ClassifyToken(mushroom));
        Assert.Equal(BlockBreakClassification.TokenHarvest, BlockBreakClassification.ClassifyToken(stick));
        Assert.Equal(BlockBreakClassification.TokenHarvest, BlockBreakClassification.ClassifyToken(horsetail));
        Assert.Equal(BlockBreakClassification.TokenChop, BlockBreakClassification.ClassifyToken(sap));
        Assert.Equal(BlockBreakClassification.TokenHarvest, BlockBreakClassification.ClassifyToken(wildBush));
        Assert.Equal(BlockBreakClassification.TokenHarvest, BlockBreakClassification.ClassifyToken(ripeHarvestable));

        Assert.True(collections.Contains("mushroom", Code(mushroom)), $"mushroom collection missing {Code(mushroom)}");
        Assert.True(collections.Contains("stick", Code(stick)), $"stick collection missing {Code(stick)}");
        Assert.True(collections.Contains("reed", Code(horsetail)), $"reed collection missing horsetail {Code(horsetail)}");
        Assert.True(collections.Contains("sap", Code(sap)), $"sap collection missing {Code(sap)}");
        Assert.False(collections.Contains("reed", Code(sap)), $"sap must not be in reed: {Code(sap)}");
        Assert.True(collections.Contains("berry-bush", Code(wildBush)), $"berry-bush collection missing {Code(wildBush)}");
        Assert.True(collections.Contains("berry-bush", Code(fruitingBush)), $"berry-bush collection missing {Code(fruitingBush)}");
        Assert.True(
            collections.Contains("berry-bush", Code(ripeHarvestable)),
            $"berry-bush collection missing {Code(ripeHarvestable)}");
    }

    [AtlasScenario]
    [Trait("Layer", "Xp")]
    [Trait("Kind", "Forage")]
    public async Task NotifyBlockBroken_Should_PayForager_ForWildMushroom()
    {
        ITestPlayer joined = await World.JoinPlayer("ForageMushEmit");
        IPlayer player = joined.Player;
        EntityBehaviorProgress progress = RequireBehavior(player);

        Block mushroom = RequireMushroom();
        BlockPos pos = PlaceWild(player, mushroom);
        ItemStack[] drops = [new ItemStack(mushroom, 3)];
        HarvestXp.NoteBreakDrops(mushroom, pos, drops);

        float before = TotalSkillXp(progress, Forager);
        HarvestXp.NotifyBlockBroken(RequireServerApi(), player, mushroom, pos);
        float gained = TotalSkillXp(progress, Forager) - before;
        Assert.True(
            gained >= 0.029f,
            $"Expected ~0.03 forager XP from 3 wild mushrooms (emit), got {gained}. block={Code(mushroom)}");
        Assert.Equal(0f, TotalSkillXp(progress, Farming), 3);
    }

    [AtlasScenario]
    [Trait("Layer", "Xp")]
    [Trait("Kind", "Forage")]
    public async Task OnBlockBroken_Should_PayForager_ForWildMushroom()
    {
        ITestPlayer joined = await World.JoinPlayer("ForageMushBreak");
        IPlayer player = joined.Player;
        EntityBehaviorProgress progress = RequireBehavior(player);

        Block mushroom = RequireMushroom();
        BlockPos pos = PlaceWild(player, mushroom);

        float before = TotalSkillXp(progress, Forager);
        mushroom.OnBlockBroken(World.Api.World, pos, player);
        float gained = TotalSkillXp(progress, Forager) - before;
        Assert.True(
            gained > 0f,
            $"Expected forager XP from breaking a world-spawned mushroom, got {gained}. block={Code(mushroom)} type={mushroom.GetType().Name}");
        Assert.Equal(0f, TotalSkillXp(progress, Farming), 3);
    }

    [AtlasScenario]
    [Trait("Layer", "Xp")]
    [Trait("Kind", "Forage")]
    public async Task OnBlockBroken_Should_NotPayForager_ForPlacedMushroom()
    {
        ITestPlayer joined = await World.JoinPlayer("ForageMushPlaced");
        IPlayer player = joined.Player;
        EntityBehaviorProgress progress = RequireBehavior(player);

        Block mushroom = RequireMushroom();
        BlockPos pos = PlaceWild(player, mushroom);
        ForagePlayerPlaced.Mark(World.Api.World, pos);
        Assert.False(ForagePlayerPlaced.IsWild(World.Api.World, mushroom, pos));

        float before = TotalSkillXp(progress, Forager);
        mushroom.OnBlockBroken(World.Api.World, pos, player);
        float gained = TotalSkillXp(progress, Forager) - before;
        Assert.True(
            gained <= 0.0001f,
            $"Player-placed mushrooms must not pay forager, got {gained}.");
    }

    [AtlasScenario]
    [Trait("Layer", "Xp")]
    [Trait("Kind", "Forage")]
    public async Task NotifyBlockBrokenXp_Should_PayForager_ForWildLooseStick()
    {
        ITestPlayer joined = await World.JoinPlayer("ForageStickEmit");
        IPlayer player = joined.Player;
        EntityBehaviorProgress progress = RequireBehavior(player);

        Block stick = RequireLooseStick();
        BlockPos pos = PlaceWild(player, stick);

        float before = TotalSkillXp(progress, Forager);
        ProsequorModSystem.For(World.Api)!.NotifyBlockBrokenXp(player, stick, pos);
        float gained = TotalSkillXp(progress, Forager) - before;
        Assert.True(
            gained >= 0.049f,
            $"Expected 0.05 forager XP from a wild loose stick (emit), got {gained}. block={Code(stick)}");
    }

    [AtlasScenario(FreshWorld = true)]
    [Trait("Layer", "Xp")]
    [Trait("Kind", "Forage")]
    public async Task OnBlockBroken_Should_PayForager_ForWildLooseStick()
    {
        ITestPlayer joined = await World.JoinPlayer("ForageStickBreak");
        IPlayer player = joined.Player;
        EntityBehaviorProgress progress = RequireBehavior(player);

        Block stick = RequireLooseStick();
        BlockPos pos = PlaceWild(player, stick);

        float before = TotalSkillXp(progress, Forager);
        stick.OnBlockBroken(World.Api.World, pos, player);
        float gained = TotalSkillXp(progress, Forager) - before;
        Assert.True(
            gained >= 0.049f,
            $"Expected 0.05 forager XP from breaking a world-spawned stick, got {gained}. block={Code(stick)} type={stick.GetType().Name}");
    }

    [AtlasScenario]
    [Trait("Layer", "Xp")]
    [Trait("Kind", "Forage")]
    public async Task NotifyBlockBrokenXp_Should_PayForager_ForWildHorsetail()
    {
        ITestPlayer joined = await World.JoinPlayer("ForageHorseEmit");
        IPlayer player = joined.Player;
        EntityBehaviorProgress progress = RequireBehavior(player);

        Block horsetail = RequireHorsetail();
        BlockPos pos = PlaceWild(player, horsetail);

        float before = TotalSkillXp(progress, Forager);
        ProsequorModSystem.For(World.Api)!.NotifyBlockBrokenXp(player, horsetail, pos);
        float gained = TotalSkillXp(progress, Forager) - before;
        Assert.True(
            gained >= 0.049f,
            $"Expected 0.05 forager XP from wild horsetail (emit), got {gained}. block={Code(horsetail)} type={horsetail.GetType().Name}");
    }

    [AtlasScenario(FreshWorld = true)]
    [Trait("Layer", "Xp")]
    [Trait("Kind", "Forage")]
    public async Task OnBlockBroken_Should_PayForager_ForWildHorsetail()
    {
        ITestPlayer joined = await World.JoinPlayer("ForageHorseBrk");
        IPlayer player = joined.Player;
        EntityBehaviorProgress progress = RequireBehavior(player);

        Block horsetail = RequireHorsetail();
        BlockPos pos = PlaceWild(player, horsetail);

        float before = TotalSkillXp(progress, Forager);
        horsetail.OnBlockBroken(World.Api.World, pos, player);
        float gained = TotalSkillXp(progress, Forager) - before;
        Assert.True(
            gained >= 0.049f,
            $"Expected 0.05 forager XP from breaking wild horsetail, got {gained}. block={Code(horsetail)} type={horsetail.GetType().Name}");
    }

    [AtlasScenario]
    [Trait("Layer", "Xp")]
    [Trait("Kind", "Forage")]
    public async Task InteractHarvest_Should_PayForager_ForWildSap()
    {
        ITestPlayer joined = await World.JoinPlayer("ForageSapPick");
        IPlayer player = joined.Player;
        EntityBehaviorProgress progress = RequireBehavior(player);

        Block sap = RequireSap();
        BlockBehaviorHarvestable? harvestable = sap.GetBehavior<BlockBehaviorHarvestable>();
        Assert.NotNull(harvestable);
        BlockPos pos = PlaceWild(player, sap);
        BlockSelection sel = new()
        {
            Position = pos,
            Face = BlockFacing.UP,
            HitPosition = new Vec3d(0.5, 0.5, 0.5),
            Block = sap
        };

        float before = TotalSkillXp(progress, Forager);
        sap.OnBlockInteractStop(2f, World.Api.World, player, sel);
        float gained = TotalSkillXp(progress, Forager) - before;
        Assert.True(
            gained >= 0.049f,
            $"Expected 0.05 forager XP from scooping wild sap, got {gained}. block={Code(sap)} type={sap.GetType().Name}");
    }

    [AtlasScenario]
    [Trait("Layer", "Xp")]
    [Trait("Kind", "Forage")]
    public async Task OnBlockBroken_Should_NotPayForager_ForSapLog()
    {
        ITestPlayer joined = await World.JoinPlayer("ForageSapChop");
        IPlayer player = joined.Player;
        EntityBehaviorProgress progress = RequireBehavior(player);

        Block sap = RequireSap();
        BlockPos pos = PlaceWild(player, sap);

        float before = TotalSkillXp(progress, Forager);
        sap.OnBlockBroken(World.Api.World, pos, player);
        float gained = TotalSkillXp(progress, Forager) - before;
        Assert.True(
            gained <= 0.0001f,
            $"Axe-felling a dripping sap log must not pay forager, got {gained}. block={Code(sap)}");
    }

    [AtlasScenario]
    [Trait("Layer", "Xp")]
    [Trait("Kind", "Forage")]
    public async Task NotifyInteractHarvest_Should_PayForager_ForUnplantedRipeBush()
    {
        ITestPlayer joined = await World.JoinPlayer("ForageBerryEmit");
        IPlayer player = joined.Player;
        EntityBehaviorProgress progress = RequireBehavior(player);

        Block bush = RequireFruitingBush(preferWildState: false);
        BlockPos pos = PlaceWild(player, bush);
        Assert.False(OwnerCredit.TryResolvePlanter(World.Api.World, bush, pos, out _));
        Assert.True(ForagePlayerPlaced.IsWild(World.Api.World, bush, pos));

        Item? fruit = World.Api.World.GetItem(new AssetLocation("game:fruit-blueberry"))
            ?? World.Api.World.GetItem(new AssetLocation("game:fruit-redcurrant"));
        Assert.NotNull(fruit);

        float before = TotalSkillXp(progress, Forager);
        HarvestXp.NotifyInteractHarvest(
            World.Api,
            player,
            bush,
            pos,
            [new ItemStack(fruit, 2)]);
        float gained = TotalSkillXp(progress, Forager) - before;
        Assert.True(
            gained >= 0.019f,
            $"Expected ~0.02 forager XP from unplanted berry harvest (emit), got {gained}. block={Code(bush)}");
        Assert.Equal(0f, TotalSkillXp(progress, Farming), 3);
    }

    [AtlasScenario]
    [Trait("Layer", "Xp")]
    [Trait("Kind", "Forage")]
    public async Task InteractHarvest_Should_PayForager_ForWildStateBush()
    {
        ITestPlayer joined = await World.JoinPlayer("ForageWild");
        IPlayer player = joined.Player;
        EntityBehaviorProgress progress = RequireBehavior(player);

        Block bush = RequireFruitingBush(preferWildState: true);
        float gained = InteractHarvestForagerGain(player, progress, bush, stampPlanter: false);
        Assert.True(
            gained > 0f,
            $"Expected forager XP from picking a wild-state berry bush, got {gained}. block={Code(bush)}");
        Assert.Equal(0f, TotalSkillXp(progress, Farming), 3);
    }

    [AtlasScenario]
    [Trait("Layer", "Xp")]
    [Trait("Kind", "Forage")]
    public async Task InteractHarvest_Should_PayForager_ForUnplantedRipeBush()
    {
        ITestPlayer joined = await World.JoinPlayer("ForageRipe");
        IPlayer player = joined.Player;
        EntityBehaviorProgress progress = RequireBehavior(player);

        Block bush = RequireFruitingBush(preferWildState: false);
        float gained = InteractHarvestForagerGain(player, progress, bush, stampPlanter: false);
        Assert.True(
            gained > 0f,
            $"Expected forager XP from picking an unplanted ripe bush, got {gained}. block={Code(bush)}");
        Assert.Equal(0f, TotalSkillXp(progress, Farming), 3);
    }

    [AtlasScenario]
    [Trait("Layer", "Xp")]
    [Trait("Kind", "Forage")]
    public async Task InteractHarvest_Should_PayFarming_NotForager_ForPlantedBush()
    {
        ITestPlayer joined = await World.JoinPlayer("ForagePlanted");
        IPlayer player = joined.Player;
        EntityBehaviorProgress progress = RequireBehavior(player);

        Block bush = RequireFruitingBush(preferWildState: false);
        float forageBefore = TotalSkillXp(progress, Forager);
        float farmBefore = TotalSkillXp(progress, Farming);
        _ = InteractHarvestForagerGain(player, progress, bush, stampPlanter: true);
        float forageGained = TotalSkillXp(progress, Forager) - forageBefore;
        float farmGained = TotalSkillXp(progress, Farming) - farmBefore;
        Assert.True(
            forageGained <= 0.0001f,
            $"Planted berry harvest must not pay forager, got {forageGained}.");
        Assert.True(
            farmGained > 0f,
            $"Expected farming XP from a planted berry harvest, got {farmGained}. block={Code(bush)}");
    }

    [AtlasScenario]
    [Trait("Layer", "Xp")]
    [Trait("Kind", "Forage")]
    public async Task InteractHarvest_Should_PayForager_ForRipeBigOrSmallBush()
    {
        ITestPlayer joined = await World.JoinPlayer("ForageBigRipe");
        IPlayer player = joined.Player;
        EntityBehaviorProgress progress = RequireBehavior(player);

        Block bush = RequireHarvestableBerryBush();
        float gained = InteractHarvestForagerGain(player, progress, bush, stampPlanter: false);
        Assert.True(
            gained > 0f,
            $"Expected forager XP from picking a ripe big/small berry bush, got {gained}. block={Code(bush)} type={bush.GetType().Name}");
        Assert.Equal(0f, TotalSkillXp(progress, Farming), 3);
    }

    [AtlasScenario]
    [Trait("Layer", "Xp")]
    [Trait("Kind", "Forage")]
    public async Task RightClickPickup_Should_PayForager_ForWildLooseStick()
    {
        ITestPlayer joined = await World.JoinPlayer("ForageStickRC");
        IPlayer player = joined.Player;
        EntityBehaviorProgress progress = RequireBehavior(player);

        Block stick = RequireLooseStick();
        BlockBehaviorRightClickPickup? pickup = stick.GetBehavior<BlockBehaviorRightClickPickup>();
        Assert.NotNull(pickup);
        BlockPos pos = PlaceWild(player, stick);
        BlockSelection sel = new()
        {
            Position = pos,
            Face = BlockFacing.UP,
            HitPosition = new Vec3d(0.5, 0.05, 0.5)
        };

        float before = TotalSkillXp(progress, Forager);
        EnumHandling handling = EnumHandling.PassThrough;
        bool handled = pickup!.OnBlockInteractStart(World.Api.World, player, sel, ref handling);
        Assert.True(handled, $"Expected RightClickPickup to take {Code(stick)}.");
        float gained = TotalSkillXp(progress, Forager) - before;
        Assert.True(
            gained >= 0.049f,
            $"Expected 0.05 forager XP from right-clicking a loose stick, got {gained}. block={Code(stick)}");
    }

    [AtlasScenario]
    [Trait("Layer", "Xp")]
    [Trait("Kind", "Forage")]
    public async Task HarvestCaller_Should_BeHandOrHeldKnife()
    {
        ITestPlayer joined = await World.JoinPlayer("ForageCaller");
        IPlayer player = joined.Player;

        Block mushroom = RequireMushroom();
        Block reed = RequireReed();
        Item knife = RequireKnife();
        BlockPos pos = PlaceWild(player, mushroom);
        CollectionIndex collections = RequireCollections();

        Assert.Equal(CallerIdentities.Hand, EventFactBuilder.CallerOrHand(player));
        Assert.Equal(
            CallerIdentities.Hand,
            DropsFactBuilder.ForHarvest(player, mushroom, pos).Caller);
        Assert.Equal(
            CallerIdentities.Hand,
            DropsFactBuilder.ForHarvest(player, reed, pos).Caller);

        player.InventoryManager.ActiveHotbarSlot.Itemstack = new ItemStack(mushroom, 1);
        player.InventoryManager.ActiveHotbarSlot.MarkDirty();
        Assert.Equal(
            CallerIdentities.Hand,
            EventFactBuilder.CallerOrHand(player));

        player.InventoryManager.ActiveHotbarSlot.Itemstack = new ItemStack(knife, 1);
        player.InventoryManager.ActiveHotbarSlot.MarkDirty();
        string knifeCode = EventFactBuilder.CodeOf(knife) ?? knife.Code.ToString();
        Assert.Equal(knifeCode, EventFactBuilder.CallerOrHand(player));
        Assert.Equal(knifeCode, DropsFactBuilder.ForHarvest(player, mushroom, pos).Caller);
        Assert.Equal(knifeCode, DropsFactBuilder.ForHarvest(player, reed, pos).Caller);
        Assert.True(
            collections.Contains("knife", knifeCode),
            $"knife collection missing {knifeCode}");
    }

    [AtlasScenario(FreshWorld = true)]
    [Trait("Layer", "Xp")]
    [Trait("Kind", "Forage")]
    public async Task KnifeBrokenWith_Should_PayForager_ForWildMushroom()
    {
        ITestPlayer joined = await World.JoinPlayer("ForageMushKnife");
        IPlayer player = joined.Player;
        EntityBehaviorProgress progress = RequireBehavior(player);

        Block mushroom = RequireMushroom();
        BlockPos pos = PlaceWild(player, mushroom);
        Item knife = RequireKnife();
        player.InventoryManager.ActiveHotbarSlot.Itemstack = new ItemStack(knife, 1);
        player.InventoryManager.ActiveHotbarSlot.MarkDirty();

        BlockSelection sel = new()
        {
            Position = pos,
            Face = BlockFacing.UP,
            HitPosition = new Vec3d(0.5, 0.1, 0.5),
            Block = mushroom
        };

        Assert.Equal(
            EventFactBuilder.CodeOf(knife) ?? knife.Code.ToString(),
            EventFactBuilder.CallerOrHand(player));

        float before = TotalSkillXp(progress, Forager);
        bool broken = knife.OnBlockBrokenWith(World.Api.World, player.Entity, player.InventoryManager.ActiveHotbarSlot, sel);
        Assert.True(broken, "Expected knife OnBlockBrokenWith to break the mushroom.");
        float gained = TotalSkillXp(progress, Forager) - before;
        Assert.True(
            gained > 0f,
            $"Expected forager XP from knife-harvesting a mushroom, got {gained}. block={Code(mushroom)} knife={knife.Code}");
    }

    [AtlasScenario(FreshWorld = true)]
    [Trait("Layer", "Xp")]
    [Trait("Kind", "Forage")]
    public async Task OnBlockBroken_Should_PayForager_ForRipeBigOrSmallBush()
    {
        ITestPlayer joined = await World.JoinPlayer("ForageBigBreak");
        IPlayer player = joined.Player;
        EntityBehaviorProgress progress = RequireBehavior(player);

        Block bush = RequireHarvestableBerryBush();
        BlockPos pos = PlaceWild(player, bush);

        float before = TotalSkillXp(progress, Forager);
        bush.OnBlockBroken(World.Api.World, pos, player);
        float gained = TotalSkillXp(progress, Forager) - before;
        Assert.True(
            gained > 0f,
            $"Expected forager XP from breaking a ripe big/small berry bush, got {gained}. block={Code(bush)} type={bush.GetType().Name}");
        Assert.Equal(0f, TotalSkillXp(progress, Farming), 3);
    }

    float InteractHarvestForagerGain(
        IPlayer player,
        EntityBehaviorProgress progress,
        Block bush,
        bool stampPlanter)
    {
        IWorldAccessor world = World.Api.World;
        BlockPos pos = PlaceWild(player, bush);
        BlockEntity? be = world.BlockAccessor.GetBlockEntity(pos);
        if (be == null)
        {
            world.BlockAccessor.SetBlock(bush.BlockId, pos);
            be = world.BlockAccessor.GetBlockEntity(pos);
        }

        Assert.NotNull(be);
        if (stampPlanter)
        {
            ProsequorBlockPedigreeStation.StampPlanter(be, player.PlayerUID);
        }

        BEBehaviorFruitingBush? fruiting = be!.GetBehavior<BEBehaviorFruitingBush>();
        if (fruiting != null)
        {
            fruiting.BState.Growthstate = EnumFruitingBushGrowthState.Ripe;
        }

        BlockSelection sel = new()
        {
            Position = pos,
            Face = BlockFacing.UP,
            HitPosition = new Vec3d(0.5, 0.5, 0.5)
        };
        float before = TotalSkillXp(progress, Forager);
        // Player path: Block.OnBlockInteractStop → BlockEntityInteract → BE harvest.
        bush.OnBlockInteractStop(2f, world, player, sel);
        return TotalSkillXp(progress, Forager) - before;
    }

    BlockPos PlaceWild(IPlayer nearPlayer, Block block)
    {
        IWorldAccessor world = World.Api.World;
        BlockPos pos = nearPlayer.Entity.Pos.AsBlockPos.AddCopy(2, 0, 0);
        EnsureFloor(pos);
        world.BlockAccessor.SetBlock(0, pos);
        world.BlockAccessor.SetBlock(block.BlockId, pos);
        Assert.Equal(block.Id, world.BlockAccessor.GetBlock(pos).Id);
        Assert.True(
            ForagePlayerPlaced.IsWild(world, block, pos),
            $"SetBlock should leave the source wild. block={Code(block)}");
        return pos;
    }

    Block RequireMushroom()
    {
        IWorldAccessor world = World.Api.World;
        foreach (string path in new[]
                 {
                     "game:mushroom-fieldmushroom-normal",
                     "game:mushroom-flyagaric-normal",
                     "game:mushroom-bolete-normal"
                 })
        {
            Block? block = world.GetBlock(new AssetLocation(path));
            if (block != null && ForageBlocks.IsMushroom(block))
            {
                return block;
            }
        }

        foreach (Block block in world.Blocks)
        {
            if (block != null && block.Id != 0 && ForageBlocks.IsMushroom(block))
            {
                return block;
            }
        }

        Assert.Fail("[prosequor] No mushroom block found.");
        throw new InvalidOperationException();
    }

    Block RequireLooseStick()
    {
        IWorldAccessor world = World.Api.World;
        foreach (string path in new[]
                 {
                     "game:loosestick-free",
                     "game:loosestick-ground",
                     "game:loosestick"
                 })
        {
            Block? block = world.GetBlock(new AssetLocation(path));
            if (block != null && ForageBlocks.IsLooseStick(block))
            {
                return block;
            }
        }

        foreach (Block block in world.Blocks)
        {
            if (block != null && block.Id != 0 && ForageBlocks.IsLooseStick(block))
            {
                return block;
            }
        }

        Assert.Fail("[prosequor] No loosestick block found.");
        throw new InvalidOperationException();
    }

    Block RequireReed()
    {
        IWorldAccessor world = World.Api.World;
        foreach (string path in new[]
                 {
                     "game:tallplant-coopersreed-land-normal-free",
                     "game:tallplant-tule-land-normal-free",
                     "game:tallplant-papyrus-land-normal-free"
                 })
        {
            Block? block = world.GetBlock(new AssetLocation(path));
            if (block != null && ForageBlocks.IsReed(block))
            {
                return block;
            }
        }

        foreach (Block block in world.Blocks)
        {
            if (block != null && block.Id != 0 && ForageBlocks.IsReed(block)
                && !(block.Code?.Path?.StartsWith("flower-horsetail-", StringComparison.OrdinalIgnoreCase) ?? false))
            {
                return block;
            }
        }

        Assert.Fail("[prosequor] No reed block found.");
        throw new InvalidOperationException();
    }

    Block RequireSap()
    {
        IWorldAccessor world = World.Api.World;
        foreach (string path in new[]
                 {
                     "game:log-resin-pine-ud",
                     "game:log-resin-acacia-ud"
                 })
        {
            Block? block = world.GetBlock(new AssetLocation(path));
            if (block != null && ForageBlocks.IsSap(block))
            {
                return block;
            }
        }

        foreach (Block block in world.Blocks)
        {
            if (block != null && block.Id != 0 && ForageBlocks.IsSap(block))
            {
                return block;
            }
        }

        Assert.Fail("[prosequor] No dripping sap log found.");
        throw new InvalidOperationException();
    }

    Block RequireHorsetail()
    {
        IWorldAccessor world = World.Api.World;
        foreach (string path in new[]
                 {
                     "game:flower-horsetail-free",
                     "game:flower-horsetail-snow"
                 })
        {
            Block? block = world.GetBlock(new AssetLocation(path));
            if (block != null && ForageBlocks.IsReed(block))
            {
                return block;
            }
        }

        foreach (Block block in world.Blocks)
        {
            if (block != null && block.Id != 0 && ForageBlocks.IsReed(block)
                && (block.Code?.Path?.StartsWith("flower-horsetail-", StringComparison.OrdinalIgnoreCase) ?? false))
            {
                return block;
            }
        }

        Assert.Fail("[prosequor] No horsetail block found.");
        throw new InvalidOperationException();
    }

    Block RequireFruitingBush(bool preferWildState)
    {
        IWorldAccessor world = World.Api.World;
        foreach (string path in new[]
                 {
                     "game:fruitingbush-wild-blueberry-free",
                     "game:fruitingbush-wild-cranberry-free",
                     "game:fruitingbush-grown-blueberry-free",
                     "game:fruitingbush-grown-cranberry-free"
                 })
        {
            Block? preferred = world.GetBlock(new AssetLocation(path));
            if (preferred != null
                && preferred.GetBehavior<BlockBehaviorFruitingBush>() != null
                && IsWildState(preferred) == preferWildState)
            {
                return preferred;
            }
        }

        Block? fallback = null;
        foreach (Block block in world.Blocks)
        {
            if (block == null
                || block.Id == 0
                || block.GetBehavior<BlockBehaviorFruitingBush>() == null)
            {
                continue;
            }

            fallback ??= block;
            if (IsWildState(block) == preferWildState)
            {
                return block;
            }
        }

        Assert.True(fallback != null, "[prosequor] No fruiting-bush block found.");
        return fallback!;
    }

    Item RequireKnife()
    {
        IWorldAccessor world = World.Api.World;
            foreach (string path in new[]
                     {
                         "game:knife-generic-flint",
                         "game:knife-generic-copper",
                         "game:knife-generic-iron"
                     })
        {
            Item? item = world.GetItem(new AssetLocation(path));
            if (item != null)
            {
                return item;
            }
        }

        Assert.Fail("[prosequor] No knife item found.");
        throw new InvalidOperationException();
    }

    Block RequireHarvestableBerryBush()
    {
        IWorldAccessor world = World.Api.World;
        foreach (string path in new[]
                 {
                     "game:bigberrybush-redcurrant-ripe",
                     "game:bigberrybush-blueberry-ripe",
                     "game:smallberrybush-redcurrant-ripe",
                     "game:smallberrybush-blueberry-ripe",
                     "game:berrybush-blueberry-ripe"
                 })
        {
            Block? block = world.GetBlock(new AssetLocation(path));
            if (block != null && block.GetBehavior<BlockBehaviorHarvestable>() != null)
            {
                return block;
            }
        }

        foreach (Block block in world.Blocks)
        {
            if (block != null
                && block.Id != 0
                && block.GetBehavior<BlockBehaviorHarvestable>() != null
                && AbilityBootstrap.IsBerryBushBlock(block))
            {
                return block;
            }
        }

        Assert.Fail("[prosequor] No harvestable ripe berry bush found.");
        throw new InvalidOperationException();
    }

    static bool IsWildState(Block block)
    {
        if (block.Variant != null
            && block.Variant.TryGetValue("state", out string? state)
            && string.Equals(state, "wild", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return block.Code?.Path?.Contains("-wild", StringComparison.OrdinalIgnoreCase) ?? false;
    }

    CollectionIndex RequireCollections()
    {
        CollectionIndex? index = ProsequorModSystem.For(World.Api)?.Collections?.Index;
        Assert.NotNull(index);
        return index!;
    }

    ICoreServerAPI RequireServerApi()
    {
        Assert.True(World.Api is ICoreServerAPI, "Forage XP emit requires a server API.");
        return (ICoreServerAPI)World.Api;
    }

    static float TotalSkillXp(EntityBehaviorProgress progress, string skill)
    {
        var state = progress.State.GetOrCreateSkill(skill);
        return progress.GetSkillXp(skill) + state.Accrued;
    }

    static EntityBehaviorProgress RequireBehavior(IPlayer player)
    {
        EntityBehaviorProgress? progress = player.Entity?.GetBehavior<EntityBehaviorProgress>();
        Assert.NotNull(progress);
        return progress!;
    }

    static string Code(Block block) => EventFactBuilder.CodeOf(block) ?? block.Code?.ToString() ?? "?";

    void EnsureFloor(BlockPos pos)
    {
        IWorldAccessor world = World.Api.World;
        Block? dirt = world.GetBlock(new AssetLocation("game:soil-low-none"))
            ?? world.GetBlock(new AssetLocation("game:dirt"));
        if (dirt != null)
        {
            world.BlockAccessor.SetBlock(dirt.BlockId, pos.DownCopy());
        }
    }
}
