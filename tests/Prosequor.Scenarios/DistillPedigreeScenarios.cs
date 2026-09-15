using Atlas.Api;
using Atlas.XUnit;
using Prosequor;
using Prosequor.Ability;
using Prosequor.Player;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;
using Xunit;

namespace Prosequor.Scenarios;

/// <summary>
/// Actor pours into a still, mixes dilute the mash, and the condenser stamps the
/// loader onto the spirit as one batch (second tick does not reroll).
/// </summary>
public class DistillPedigreeScenarios : AtlasScenarioBase
{
    const string Skill = "cooking";
    const int PourUnits = 100;

    static int nextOffset = 12;

    [AtlasScenario]
    [Trait("Layer", "Pedigree")]
    [Trait("Kind", "Distill")]
    public async Task StillPour_Should_DiluteSameMaker_And_StampPourerOnSpirit()
    {
        ITestPlayer joined = await World.JoinPlayer("StillAlice");
        IPlayer alice = joined.Player;
        GrantDistillQuality(RequireProgress(alice));
        AssertServer();

        (BlockEntityBoiler boiler, BlockEntityCondenser condenser, Block boilerBlock) = PlaceStill(alice);
        Pour(alice, boilerBlock, boiler.Pos, Mash("alice", qualityRank: 0, mod: 1.0f));
        Assert.True(ProsequorBlockPedigreeStation.TryGetSoleContributor(boiler, out string? loader));
        Assert.Equal(alice.PlayerUID, loader);

        ItemStack first = RequireMash(boiler);
        Assert.Equal("alice", MakerOf(first));
        Assert.Equal(PourUnits, first.StackSize);

        Pour(alice, boilerBlock, boiler.Pos, Mash("alice", qualityRank: 100, mod: 2.0f));
        ItemStack mixed = RequireMash(boiler);
        Assert.True(ProsequorStackPedigree.TryGetPrimaryBlob(mixed, out ProsequorBlob mash));
        Assert.Equal("alice", mash.MakerUid);
        Assert.Equal(50, mash.QualityRank);
        Assert.Equal(1.5f, FindMod(mash.Mods, "intoxication"), 3);
        Assert.Equal(PourUnits * 2, mixed.StackSize);
        Assert.False(
            BoilerDistillBatch.TryGet(boiler, out _),
            "A pour must clear any locked distill batch.");

        ItemStack spirit = DistillTwice(boiler, condenser, mixed);
        Assert.Equal(alice.PlayerUID, MakerOf(spirit));
        Assert.True(ProsequorStackPedigree.TryGetPrimaryBlob(spirit, out ProsequorBlob outBlob));
        Assert.Equal(0, outBlob.QualityRank);
        Assert.True(BoilerDistillBatch.TryGet(boiler, out ProsequorBlob locked));
        Assert.Equal(outBlob.ContentHash, locked.ContentHash);
        Assert.Equal(alice.PlayerUID, locked.MakerUid);
    }

    [AtlasScenario]
    [Trait("Layer", "Pedigree")]
    [Trait("Kind", "Distill")]
    public async Task StillPour_Should_StripMixedMakers_And_StampLastPourerOnSpirit()
    {
        ITestPlayer aJoin = await World.JoinPlayer("StillMixA");
        ITestPlayer bJoin = await World.JoinPlayer("StillMixB");
        IPlayer alice = aJoin.Player;
        IPlayer bob = bJoin.Player;
        GrantDistillQuality(RequireProgress(bob));
        AssertServer();

        (BlockEntityBoiler boiler, BlockEntityCondenser condenser, Block boilerBlock) = PlaceStill(alice);
        Pour(alice, boilerBlock, boiler.Pos, Mash(alice.PlayerUID, qualityRank: 80, mod: 1.0f));
        Assert.Equal(alice.PlayerUID, MakerOf(RequireMash(boiler)));

        Pour(bob, boilerBlock, boiler.Pos, Mash(bob.PlayerUID, qualityRank: 20, mod: 2.0f));
        ItemStack mixed = RequireMash(boiler);
        Assert.True(ProsequorStackPedigree.TryGetPrimaryBlob(mixed, out ProsequorBlob mash));
        Assert.True(
            string.IsNullOrEmpty(mash.MakerUid),
            $"Mixed makers should strip mash prestige, got maker '{mash.MakerUid}'.");
        Assert.Empty(mash.Affixes);
        Assert.Equal(50, mash.QualityRank);
        Assert.Equal(1.5f, FindMod(mash.Mods, "intoxication"), 3);
        Assert.True(ProsequorBlockPedigreeStation.TryGetSoleContributor(boiler, out string? loader));
        Assert.Equal(bob.PlayerUID, loader);

        ItemStack spirit = DistillTwice(boiler, condenser, mixed);
        Assert.Equal(bob.PlayerUID, MakerOf(spirit));
        Assert.NotEqual(alice.PlayerUID, MakerOf(spirit));
        Assert.True(BoilerDistillBatch.TryGet(boiler, out ProsequorBlob locked));
        Assert.Equal(bob.PlayerUID, locked.MakerUid);
        Assert.True(ProsequorStackPedigree.TryGetPrimaryBlob(spirit, out ProsequorBlob outBlob));
        Assert.Equal(locked.ContentHash, outBlob.ContentHash);
    }

    ItemStack DistillTwice(BlockEntityBoiler boiler, BlockEntityCondenser condenser, ItemStack mash)
    {
        DistillationProps props = RequireDistillProps(mash);
        ItemSlot source = boiler.Inventory[0];
        Assert.False(source.Empty);

        bool first = condenser.ReceiveDistillate(source, props);
        Assert.True(first, "First distill tick should fill the condenser bucket.");
        ItemStack afterFirst = RequireSpirit(condenser);
        Assert.Equal(1, afterFirst.StackSize);
        string? maker = MakerOf(afterFirst);
        string affixes = AffixKey(afterFirst);
        Assert.True(BoilerDistillBatch.TryGet(boiler, out ProsequorBlob locked));
        string lockedHash = locked.ContentHash;

        bool second = condenser.ReceiveDistillate(source, props);
        Assert.True(second, "Second distill tick should add to the same bucket.");
        ItemStack afterSecond = RequireSpirit(condenser);
        Assert.Equal(2, afterSecond.StackSize);
        Assert.Equal(maker, MakerOf(afterSecond));
        Assert.Equal(affixes, AffixKey(afterSecond));
        Assert.True(BoilerDistillBatch.TryGet(boiler, out ProsequorBlob stillLocked));
        Assert.Equal(lockedHash, stillLocked.ContentHash);
        return afterSecond;
    }

    void Pour(IPlayer player, Block boilerBlock, BlockPos pos, ItemStack liquid)
    {
        IWorldAccessor world = World.Api.World;
        Block bucketBlock = RequireBlock("game:woodbucket", "woodbucket");
        ItemStack bucket = new(bucketBlock, 1);
        var held = (BlockLiquidContainerBase)bucketBlock;
        int filled = held.TryPutLiquid(bucket, liquid, desiredLitres: 10f);
        Assert.True(filled > 0, "Expected the bucket to accept the mash.");
        ItemStack? heldLiquid = held.GetContent(bucket);
        Assert.NotNull(heldLiquid);
        Assert.Equal(PourUnits, heldLiquid!.StackSize);
        Assert.Equal(MakerOf(liquid), MakerOf(heldLiquid));

        ItemSlot hotbar = player.InventoryManager.ActiveHotbarSlot;
        Assert.NotNull(hotbar);
        hotbar.Itemstack = bucket;
        hotbar.MarkDirty();

        Assert.NotNull(player.WorldData?.EntityControls);
        player.WorldData.EntityControls.ShiftKey = false;
        player.WorldData.EntityControls.CtrlKey = false;

        var container = (BlockLiquidContainerBase)boilerBlock;
        int before = container.GetContent(pos)?.StackSize ?? 0;
        BlockSelection sel = new()
        {
            Position = pos,
            Face = BlockFacing.UP,
            HitPosition = new Vec3d(0.5, 0.5, 0.5)
        };
        bool handled = boilerBlock.OnBlockInteractStart(world, player, sel);
        int after = container.GetContent(pos)?.StackSize ?? 0;
        Assert.True(
            handled && after > before,
            $"Expected a pour into the still. handled={handled} before={before} after={after}.");
    }

    ItemStack Mash(string makerUid, int qualityRank, float mod)
    {
        Item cider = RequireItem("game:ciderportion-apple", "ciderportion-apple");
        ItemStack liquid = new(cider, PourUnits);
        ProsequorBlob blob = new ProsequorBlob(makerUid, contributors: null)
            .WithQualityRank(qualityRank)
            .WithMods(new[] { new ProsequorBlob.ModFactor("intoxication", mod) });
        ProsequorLiquidPedigree.WriteFullBlob(liquid, blob);
        return liquid;
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

        if (world.BlockAccessor.GetBlockEntity(boilerPos) is not BlockEntityBoiler boiler)
        {
            Assert.Fail($"Expected BlockEntityBoiler at {boilerPos}, got {world.BlockAccessor.GetBlockEntity(boilerPos)?.GetType().Name ?? "null"}.");
            throw new InvalidOperationException();
        }

        if (world.BlockAccessor.GetBlockEntity(condenserPos) is not BlockEntityCondenser condenser)
        {
            Assert.Fail($"Expected BlockEntityCondenser at {condenserPos}, got {world.BlockAccessor.GetBlockEntity(condenserPos)?.GetType().Name ?? "null"}.");
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
            fromItem.DistilledStack.Resolve(world, "distill-scenario");
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
        distilled.Resolve(world, "distill-scenario");
        Assert.NotNull(distilled.ResolvedItemstack);
        return new DistillationProps
        {
            Ratio = 0.1f,
            DistilledStack = distilled
        };
    }

    static void GrantDistillQuality(IPlayerProgress progress)
    {
        progress.AddUnlockPoints(10);
        progress.SetSkillLevel(Skill, 50);
        Assert.True(progress.GrantUnlock(Skill, "brewing-expertise"));
        Assert.True(progress.GrantUnlock(Skill, "perfect-presser"));
        Assert.True(progress.GrantUnlock(Skill, "strong-spirits"));
        Assert.True(progress.GrantUnlock(Skill, "strong-spirits"));
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
        Assert.True(bucket!.Collectible is BlockLiquidContainerTopOpened);
        ItemStack? content = ((BlockLiquidContainerTopOpened)bucket.Collectible).GetContent(bucket);
        Assert.NotNull(content);
        return content!;
    }

    static string? MakerOf(ItemStack? stack)
    {
        if (stack == null || !ProsequorStackPedigree.TryGetPrimaryBlob(stack, out ProsequorBlob blob))
        {
            return null;
        }

        return blob.MakerUid;
    }

    static string AffixKey(ItemStack stack)
    {
        if (!ProsequorStackPedigree.TryGetPrimaryBlob(stack, out ProsequorBlob blob))
        {
            return "";
        }

        return string.Join(",", blob.Affixes.Select(entry => entry.Code));
    }

    static float FindMod(IReadOnlyList<ProsequorBlob.ModFactor> mods, string key)
    {
        for (int i = 0; i < mods.Count; i++)
        {
            if (string.Equals(mods[i].Key, key, StringComparison.OrdinalIgnoreCase))
            {
                return mods[i].Factor;
            }
        }

        return 1f;
    }

    void AssertServer() =>
        Assert.Equal(EnumAppSide.Server, World.Api.World.Side);

    static IPlayerProgress RequireProgress(IPlayer player)
    {
        IPlayerProgress? progress = ProsequorModSystem.GetProgress(player);
        Assert.NotNull(progress);
        return progress!;
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
