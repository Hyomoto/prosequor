using System.Reflection;
using Atlas.Api;
using Atlas.XUnit;
using Prosequor;
using Prosequor.Ability;
using Prosequor.Commands;
using Prosequor.Player;
using Prosequor.Xp.Activity;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;
using Xunit;

namespace Prosequor.Scenarios;

/// <summary>
/// Barrel fermentation and still distillation pay cooking XP for finished units.
/// Ferment pays once at unseal. Distill pays each new drop, not the bucket total.
/// </summary>
public class FermentDistillXpScenarios : AtlasScenarioBase
{
    const string Skill = "cooking";
    const int JuiceUnits = 10;

    static int nextOffset = 20;

    [AtlasScenario]
    [Trait("Layer", "Xp")]
    [Trait("Kind", "Ferment")]
    public async Task BarrelFerment_Should_PaySealerOnceForOutputQuantity()
    {
        ITestPlayer joined = await World.JoinPlayer("FermentSealer");
        ITestPlayer other = await World.JoinPlayer("FermentBystander");
        IPlayer sealer = joined.Player;
        RequireProgress(sealer);
        RequireProgress(other.Player);
        Assert.Equal(EnumAppSide.Server, World.Api.World.Side);

        BlockEntityBarrel barrel = PlaceBarrel(sealer);
        Item juice = RequireItem("game:juiceportion-apple", "juiceportion-apple");
        LoadJuice(barrel, new ItemStack(juice, JuiceUnits));
        Assert.True(barrel.GetCanSeal(sealer), "Expected a cider barrel recipe for apple juice.");
        Seal(barrel, sealer);
        barrel.SealedSinceTotalHours = World.Api.World.Calendar.TotalHours - 1000;

        float before = TotalCookingXp(sealer);
        float otherBefore = TotalCookingXp(other.Player);
        TickBarrel(barrel);

        ItemStack? cider = FindStack(barrel, "ciderportion");
        Assert.NotNull(cider);
        Assert.False(barrel.Sealed);
        Assert.Equal(sealer.PlayerUID, CraftAttribution.TryGetMakerUid(cider));
        int made = cider!.StackSize;
        Assert.True(made >= 1, "Expected fermented cider in the barrel.");
        float gained = TotalCookingXp(sealer) - before;
        float expected = ScenarioXp.PlannedCrafted(
            World.Api.World,
            Skill,
            sealer.PlayerUID,
            cider,
            made,
            contributors: [new Deed.ContributorShare(sealer.PlayerUID, 1)],
            makerUid: sealer.PlayerUID);
        ScenarioXp.AssertPaid(gained, expected, $"{made} fermented units");
        Assert.Equal(otherBefore, TotalCookingXp(other.Player));

        float afterPay = TotalCookingXp(sealer);
        TickBarrel(barrel);
        Assert.Equal(afterPay, TotalCookingXp(sealer));
    }

    /// <summary>
    /// No sealer means the fresh cider is anonymous. A pourer and a sealer share
    /// the grant, and the cider maker is the closer — not the juice pedigree.
    /// </summary>
    [AtlasScenario]
    [Trait("Layer", "Ability")]
    [Trait("Kind", "Ferment")]
    public async Task BarrelFerment_Should_StampSealer_And_SplitXp()
    {
        ITestPlayer joined = await World.JoinPlayer("JuicePresser");
        ITestPlayer other = await World.JoinPlayer("BarrelSealer");
        IPlayer pourer = joined.Player;
        IPlayer sealer = other.Player;
        RequireProgress(pourer);
        RequireProgress(sealer);

        BlockEntityBarrel anonymous = PlaceBarrel(pourer);
        LoadJuice(anonymous, StampedJuice(pourer.PlayerUID));
        anonymous.SealBarrel();
        anonymous.SealedSinceTotalHours = World.Api.World.Calendar.TotalHours - 1000;
        TickBarrel(anonymous);
        ItemStack? dropped = FindStack(anonymous, "ciderportion");
        Assert.NotNull(dropped);
        Assert.True(string.IsNullOrEmpty(CraftAttribution.TryGetMakerUid(dropped)));

        BlockEntityBarrel barrel = PlaceBarrel(pourer);
        ItemStack juice = StampedJuice(pourer.PlayerUID);
        ItemAffixes.SetFront(juice, "juice-only", "prosequor:test-juice-only");
        PourIntoBarrel(pourer, barrel, juice);
        PourIntoBarrel(pourer, barrel, StampedJuice(pourer.PlayerUID));
        Assert.Equal(1, ContributorWeight(barrel, pourer.PlayerUID));

        Seal(barrel, sealer);
        Assert.Equal(sealer.PlayerUID, CraftAttribution.TryGetMakerUid(barrel.Inventory[1].Itemstack));
        Assert.Equal(1, ContributorWeight(barrel, pourer.PlayerUID));
        Assert.Equal(1, ContributorWeight(barrel, sealer.PlayerUID));

        barrel.SealedSinceTotalHours = World.Api.World.Calendar.TotalHours - 1000;
        float pourerBefore = TotalCookingXp(pourer);
        float sealerBefore = TotalCookingXp(sealer);
        TickBarrel(barrel);

        ItemStack? cider = FindStack(barrel, "ciderportion");
        Assert.NotNull(cider);
        Assert.Equal(sealer.PlayerUID, CraftAttribution.TryGetMakerUid(cider));
        Assert.DoesNotContain(ItemAffixes.GetAll(cider), a => a.Code == "juice-only");
        int made = cider!.StackSize;
        Deed.ContributorShare[] shares =
        [
            new(pourer.PlayerUID, 1),
            new(sealer.PlayerUID, 1)
        ];
        float pourerExpected = ScenarioXp.PlannedCrafted(
            World.Api.World,
            Skill,
            pourer.PlayerUID,
            cider,
            made,
            contributors: shares,
            makerUid: sealer.PlayerUID);
        float sealerExpected = ScenarioXp.PlannedCrafted(
            World.Api.World,
            Skill,
            sealer.PlayerUID,
            cider,
            made,
            contributors: shares,
            makerUid: sealer.PlayerUID);
        ScenarioXp.AssertPaid(
            TotalCookingXp(pourer) - pourerBefore,
            pourerExpected,
            "pourer share of fermented cider");
        ScenarioXp.AssertPaid(
            TotalCookingXp(sealer) - sealerBefore,
            sealerExpected,
            "sealer share of fermented cider");
    }

    /// <summary>
    /// Fermented quality is a roll from the sealer. Cooking's window can miss, so
    /// several barrels are sealed until one write lands.
    /// </summary>
    [AtlasScenario]
    [Trait("Layer", "Ability")]
    [Trait("Kind", "Ferment")]
    public async Task BarrelFerment_Should_RollQualityFromSealer()
    {
        ITestPlayer joined = await World.JoinPlayer("QualitySealer");
        IPlayer sealer = joined.Player;
        IPlayerProgress progress = RequireProgress(sealer);
        progress.SetSkillLevel(Skill, 50);

        bool stamped = false;
        for (int i = 0; i < 8 && !stamped; i++)
        {
            BlockEntityBarrel barrel = PlaceBarrel(sealer);
            LoadJuice(barrel, new ItemStack(RequireItem("game:juiceportion-apple", "juiceportion-apple"), JuiceUnits));
            Seal(barrel, sealer);
            barrel.SealedSinceTotalHours = World.Api.World.Calendar.TotalHours - 1000;
            TickBarrel(barrel);
            ItemStack? cider = FindStack(barrel, "ciderportion");
            Assert.NotNull(cider);
            Assert.Equal(sealer.PlayerUID, CraftAttribution.TryGetMakerUid(cider));
            stamped = HasQualityStamp(cider!);
        }

        Assert.True(stamped, "Expected fermented quality to stamp on at least one of 8 barrels.");
    }

    ItemStack StampedJuice(string makerUid)
    {
        ItemStack juice = new(RequireItem("game:juiceportion-apple", "juiceportion-apple"), JuiceUnits);
        CraftAttribution.StampMakerUid(juice, makerUid);
        Assert.Equal(makerUid, CraftAttribution.TryGetMakerUid(juice));
        return juice;
    }

    static void Seal(BlockEntityBarrel barrel, IPlayer player) =>
        barrel.OnReceivedClientPacket(player, 1337, null);

    static int ContributorWeight(BlockEntityBarrel barrel, string uid)
    {
        if (!ProsequorBlockPedigreeStation.TryGetBlob(barrel, out ProsequorBlob blob))
        {
            return 0;
        }

        foreach (ProsequorBlob.Share share in blob.Contributors)
        {
            if (string.Equals(share.PlayerUid, uid, StringComparison.Ordinal))
            {
                return share.Weight;
            }
        }

        return 0;
    }

    static bool HasQualityStamp(ItemStack stack)
    {
        if (ProsequorStackPedigree.TryGetQualityRank(stack, out _))
        {
            return true;
        }

        foreach (ItemAffixEntry affix in ItemAffixes.GetAll(stack))
        {
            if (string.Equals(affix.Code, ItemAffixes.QualityCode, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    [AtlasScenario]
    [Trait("Layer", "Xp")]
    [Trait("Kind", "Ferment")]
    public async Task FinishBarrelCommand_Should_CompleteSealedRecipe()
    {
        ITestPlayer joined = await World.JoinPlayer("FinishBarrelCmd");
        IPlayer sealer = joined.Player;
        RequireProgress(sealer);

        BlockEntityBarrel barrel = PlaceBarrel(sealer);
        Item juice = RequireItem("game:juiceportion-apple", "juiceportion-apple");
        LoadJuice(barrel, new ItemStack(juice, JuiceUnits));
        Assert.True(barrel.GetCanSeal(sealer), "Expected a cider barrel recipe for apple juice.");
        Seal(barrel, sealer);

        float before = TotalCookingXp(sealer);
        Assert.True(ProgressCommands.TryFinishBarrel(barrel, out string error), error);
        Assert.False(barrel.Sealed);

        ItemStack? cider = FindStack(barrel, "ciderportion");
        Assert.NotNull(cider);
        Assert.Equal(sealer.PlayerUID, CraftAttribution.TryGetMakerUid(cider));
        float gained = TotalCookingXp(sealer) - before;
        float expected = ScenarioXp.PlannedCrafted(
            World.Api.World,
            Skill,
            sealer.PlayerUID,
            cider!,
            cider.StackSize,
            contributors: [new Deed.ContributorShare(sealer.PlayerUID, 1)],
            makerUid: sealer.PlayerUID);
        ScenarioXp.AssertPaid(gained, expected, "finishbarrel fermented output");
    }

    [AtlasScenario]
    [Trait("Layer", "Xp")]
    [Trait("Kind", "Distill")]
    public async Task Distill_Should_PayLoaderPerNewDrop_NotBucketTotal()
    {
        ITestPlayer joined = await World.JoinPlayer("DistillLoader");
        ITestPlayer other = await World.JoinPlayer("DistillBystander");
        IPlayer loader = joined.Player;
        RequireProgress(loader);
        RequireProgress(other.Player);
        Assert.Equal(EnumAppSide.Server, World.Api.World.Side);

        (BlockEntityBoiler boiler, BlockEntityCondenser condenser, Block boilerBlock) = PlaceStill(loader);
        Item cider = RequireItem("game:ciderportion-apple", "ciderportion-apple");
        Pour(loader, boilerBlock, boiler.Pos, new ItemStack(cider, 100));
        Assert.True(ProsequorBlockPedigreeStation.TryGetSoleContributor(boiler, out string? uid));
        Assert.Equal(loader.PlayerUID, uid);

        DistillationProps props = RequireDistillProps(RequireMash(boiler));
        ItemSlot source = boiler.Inventory[0];
        float before = TotalCookingXp(loader);
        float otherBefore = TotalCookingXp(other.Player);

        Assert.True(condenser.ReceiveDistillate(source, props));
        ItemStack firstDrop = RequireSpirit(condenser).Clone();
        firstDrop.StackSize = 1;
        float perDrop = ScenarioXp.PlannedCrafted(
            World.Api.World,
            Skill,
            loader.PlayerUID,
            firstDrop,
            quantity: 1);
        float afterFirst = TotalCookingXp(loader) - before;
        ScenarioXp.AssertPaid(afterFirst, perDrop, "first spirit drop");

        Assert.True(condenser.ReceiveDistillate(source, props));
        float afterSecond = TotalCookingXp(loader) - before;
        ScenarioXp.AssertPaid(afterSecond, perDrop * 2f, "two spirit drops (not the bucket total)");
        Assert.Equal(otherBefore, TotalCookingXp(other.Player));
        Assert.Equal(2, RequireSpirit(condenser).StackSize);
    }

    static void LoadJuice(BlockEntityBarrel barrel, ItemStack juice)
    {
        barrel.Inventory[1].Itemstack = juice;
        barrel.Inventory[1].MarkDirty();
    }

    static void TickBarrel(BlockEntityBarrel barrel)
    {
        MethodInfo? tick = typeof(BlockEntityBarrel).GetMethod(
            "OnEvery3Second",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(tick);
        tick!.Invoke(barrel, [3f]);
    }

    void PourIntoBarrel(IPlayer player, BlockEntityBarrel barrel, ItemStack liquid)
    {
        IWorldAccessor world = World.Api.World;
        Block bucketBlock = RequireBlock("game:woodbucket", "woodbucket");
        ItemStack bucket = new(bucketBlock, 1);
        var held = (BlockLiquidContainerBase)bucketBlock;
        Assert.True(held.TryPutLiquid(bucket, liquid, desiredLitres: 10f) > 0);

        ItemSlot hotbar = player.InventoryManager.ActiveHotbarSlot;
        Assert.NotNull(hotbar);
        hotbar.Itemstack = bucket;
        hotbar.MarkDirty();
        Assert.NotNull(player.WorldData?.EntityControls);
        player.WorldData.EntityControls.ShiftKey = false;
        player.WorldData.EntityControls.CtrlKey = false;

        int before = barrel.Inventory[1]?.Itemstack?.StackSize ?? 0;
        bool handled = barrel.Block.OnBlockInteractStart(
            world,
            player,
            new BlockSelection
            {
                Position = barrel.Pos,
                Face = BlockFacing.UP,
                HitPosition = new Vec3d(0.5, 0.5, 0.5)
            });
        int after = barrel.Inventory[1]?.Itemstack?.StackSize ?? 0;
        Assert.True(handled && after > before, $"Expected a pour into the barrel. handled={handled} before={before} after={after}.");
    }

    void Pour(IPlayer player, Block boilerBlock, BlockPos pos, ItemStack liquid)
    {
        IWorldAccessor world = World.Api.World;
        Block bucketBlock = RequireBlock("game:woodbucket", "woodbucket");
        ItemStack bucket = new(bucketBlock, 1);
        var held = (BlockLiquidContainerBase)bucketBlock;
        Assert.True(held.TryPutLiquid(bucket, liquid, desiredLitres: 10f) > 0);

        ItemSlot hotbar = player.InventoryManager.ActiveHotbarSlot;
        Assert.NotNull(hotbar);
        hotbar.Itemstack = bucket;
        hotbar.MarkDirty();
        Assert.NotNull(player.WorldData?.EntityControls);
        player.WorldData.EntityControls.ShiftKey = false;
        player.WorldData.EntityControls.CtrlKey = false;

        var container = (BlockLiquidContainerBase)boilerBlock;
        int before = container.GetContent(pos)?.StackSize ?? 0;
        bool handled = boilerBlock.OnBlockInteractStart(
            world,
            player,
            new BlockSelection
            {
                Position = pos,
                Face = BlockFacing.UP,
                HitPosition = new Vec3d(0.5, 0.5, 0.5)
            });
        int after = container.GetContent(pos)?.StackSize ?? 0;
        Assert.True(handled && after > before, $"Expected a pour into the still. handled={handled} before={before} after={after}.");
    }

    (BlockEntityBoiler boiler, BlockEntityCondenser condenser, Block boilerBlock) PlaceStill(IPlayer player)
    {
        IWorldAccessor world = World.Api.World;
        int offset = nextOffset;
        nextOffset += 6;
        BlockPos boilerPos = player.Entity.Pos.AsBlockPos.AddCopy(offset, 1, 0);
        BlockPos condenserPos = boilerPos.EastCopy();
        EnsureFloor(boilerPos);
        EnsureFloor(condenserPos);

        Block boilerBlock = RequireBlock("game:verticalboiler-west", "verticalboiler-west");
        Block condenserBlock = RequireBlock("game:condenser-west", "condenser-west");
        Place(boilerBlock, boilerPos);
        Place(condenserBlock, condenserPos);

        if (world.BlockAccessor.GetBlockEntity(boilerPos) is not BlockEntityBoiler boiler
            || world.BlockAccessor.GetBlockEntity(condenserPos) is not BlockEntityCondenser condenser)
        {
            Assert.Fail("Expected a boiler and an adjacent condenser.");
            throw new InvalidOperationException();
        }

        Item water = RequireItem("game:waterportion", "waterportion");
        condenser.Inventory[0].Itemstack = new ItemStack(water, 20);
        condenser.Inventory[0].MarkDirty();
        Block bucket = RequireBlock("game:woodbucket", "woodbucket");
        condenser.Inventory[1].Itemstack = new ItemStack(bucket, 1);
        condenser.Inventory[1].MarkDirty();
        return (boiler, condenser, boilerBlock);
    }

    DistillationProps RequireDistillProps(ItemStack mash)
    {
        IWorldAccessor world = World.Api.World;
        DistillationProps? fromItem = mash.ItemAttributes?["distillationProps"].AsObject<DistillationProps>();
        if (fromItem?.DistilledStack != null)
        {
            fromItem.DistilledStack.Resolve(world, "ferment-distill-xp");
            if (fromItem.DistilledStack.ResolvedItemstack != null)
            {
                if (fromItem.Ratio <= 0f)
                {
                    fromItem.Ratio = 0.1f;
                }

                return fromItem;
            }
        }

        Item spirit = RequireItem("game:spiritportion-apple", "spiritportion-apple");
        JsonItemStack distilled = new()
        {
            Type = EnumItemClass.Item,
            Code = spirit.Code.Clone()
        };
        distilled.Resolve(world, "ferment-distill-xp");
        Assert.NotNull(distilled.ResolvedItemstack);
        return new DistillationProps { Ratio = 0.1f, DistilledStack = distilled };
    }

    BlockEntityBarrel PlaceBarrel(IPlayer player)
    {
        IWorldAccessor world = World.Api.World;
        int offset = nextOffset;
        nextOffset += 4;
        BlockPos pos = player.Entity.Pos.AsBlockPos.AddCopy(offset, 1, 0);
        EnsureFloor(pos);
        Block barrel = RequireBlock("game:barrel-burned", "barrel");
        Place(barrel, pos);
        if (world.BlockAccessor.GetBlockEntity(pos) is not BlockEntityBarrel be)
        {
            Assert.Fail($"Expected BlockEntityBarrel at {pos}.");
            throw new InvalidOperationException();
        }

        return be;
    }

    static ItemStack RequireMash(BlockEntityBoiler boiler)
    {
        ItemStack? mash = boiler.GetContent();
        Assert.NotNull(mash);
        return mash!;
    }

    static ItemStack RequireSpirit(BlockEntityCondenser condenser)
    {
        ItemStack? bucket = condenser.Inventory[1].Itemstack;
        Assert.NotNull(bucket);
        ItemStack? content = ((BlockLiquidContainerTopOpened)bucket.Collectible).GetContent(bucket);
        Assert.NotNull(content);
        return content!;
    }

    static ItemStack? FindStack(BlockEntityBarrel barrel, string pathContains)
    {
        for (int i = 0; i < barrel.Inventory.Count; i++)
        {
            ItemStack? stack = barrel.Inventory[i].Itemstack;
            if (stack?.Collectible?.Code?.Path?.Contains(pathContains, StringComparison.OrdinalIgnoreCase) == true)
            {
                return stack;
            }
        }

        return null;
    }

    static float TotalCookingXp(IPlayer player)
    {
        EntityBehaviorProgress? progress = player.Entity?.GetBehavior<EntityBehaviorProgress>();
        Assert.NotNull(progress);
        var skill = progress!.State.GetOrCreateSkill(Skill);
        return progress.GetSkillXp(Skill) + skill.Accrued;
    }

    static IPlayerProgress RequireProgress(IPlayer player)
    {
        IPlayerProgress? progress = ProsequorModSystem.GetProgress(player);
        Assert.NotNull(progress);
        progress!.SetSkillLevel(Skill, 1);
        return progress;
    }

    Item RequireItem(string code, string fallback)
    {
        IWorldAccessor world = World.Api.World;
        Item? item = world.GetItem(new AssetLocation(code)) ?? world.GetItem(new AssetLocation(fallback));
        Assert.NotNull(item);
        return item!;
    }

    Block RequireBlock(string code, string fallback)
    {
        IWorldAccessor world = World.Api.World;
        Block? block = world.GetBlock(new AssetLocation(code)) ?? world.GetBlock(new AssetLocation(fallback));
        Assert.NotNull(block);
        return block!;
    }

    void Place(Block block, BlockPos pos)
    {
        IWorldAccessor world = World.Api.World;
        world.BlockAccessor.SetBlock(0, pos);
        world.BlockAccessor.SetBlock(block.BlockId, pos);
    }

    void EnsureFloor(BlockPos pos)
    {
        IWorldAccessor world = World.Api.World;
        BlockPos below = pos.DownCopy();
        Block? dirt = world.GetBlock(new AssetLocation("game:soil-low-none"))
            ?? world.GetBlock(new AssetLocation("game:dirt"));
        if (dirt != null && world.BlockAccessor.GetBlock(below).Id == 0)
        {
            world.BlockAccessor.SetBlock(dirt.BlockId, below);
        }
    }
}
