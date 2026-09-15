using Atlas.Api;
using Atlas.XUnit;
using Prosequor.Ability;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;
using Xunit;

namespace Prosequor.Scenarios;

/// <summary>
/// Place → BE tree → pick/break must carry the unit blob (gap before CraftAttribution migration).
/// Uses fired crock (has entityClass); raw bowls are Unplaceable/GroundStorable.
/// </summary>
public class ProsequorBlockPedigreeScenarios : AtlasScenarioBase
{
    [AtlasScenario]
    [Trait("Layer", "Pedigree")]
    [Trait("Kind", "PlacePickup")]
    public async Task Place_Should_StashPrimaryBlobOnBlockEntity()
    {
        ITestPlayer joined = await World.JoinPlayer("PedPlaceStash");
        (Block block, BlockPos pos, ItemStack placed) = PlaceStampedCrock(joined.Player, "stash-maker");

        BlockEntity? be = World.Api.World.BlockAccessor.GetBlockEntity(pos);
        Assert.NotNull(be);
        Assert.True(ProsequorBlockPedigreeStation.TryGetBlob(be, out ProsequorBlob blob));
        Assert.Equal("stash-maker", blob.MakerUid);

        TreeAttribute tree = new();
        be!.ToTreeAttributes(tree);
        ITreeAttribute? live = tree.GetTreeAttribute(ProsequorStackPedigree.LiveAttr);
        Assert.NotNull(live);
        Assert.Equal("stash-maker", ProsequorBlob.ReadFrom(live).MakerUid);
        Assert.Equal(placed.Collectible?.Code, block.Code);
    }

    [AtlasScenario]
    [Trait("Layer", "Pedigree")]
    [Trait("Kind", "PlacePickup")]
    public async Task OnPickBlock_Should_RestoreLiveBlob()
    {
        ITestPlayer joined = await World.JoinPlayer("PedPlacePick");
        (Block block, BlockPos pos, _) = PlaceStampedCrock(joined.Player, "pick-maker");

        ItemStack picked = block.OnPickBlock(World.Api.World, pos);
        Assert.NotNull(picked);
        Assert.True(ProsequorStackPedigree.IsLive(picked));
        Assert.True(ProsequorStackPedigree.TryGetPrimaryBlob(picked, out ProsequorBlob blob));
        Assert.Equal("pick-maker", blob.MakerUid);
    }

    [AtlasScenario]
    [Trait("Layer", "Pedigree")]
    [Trait("Kind", "PlacePickup")]
    public async Task GetDrops_Should_RestoreLiveBlobOnMatchingDrop()
    {
        ITestPlayer joined = await World.JoinPlayer("PedPlaceDrop");
        (Block block, BlockPos pos, _) = PlaceStampedCrock(joined.Player, "drop-maker");

        ItemStack[] drops = block.GetDrops(World.Api.World, pos, joined.Player, 1f);
        Assert.NotNull(drops);
        Assert.NotEmpty(drops);

        ItemStack? match = drops.FirstOrDefault(s =>
            s != null
            && s.Collectible != null
            && (s.Collectible.Id == block.Id || s.Collectible.Code?.Equals(block.Code) == true));
        Assert.NotNull(match);
        Assert.True(ProsequorStackPedigree.TryGetPrimaryBlob(match, out ProsequorBlob blob));
        Assert.Equal("drop-maker", blob.MakerUid);
    }

    [AtlasScenario]
    [Trait("Layer", "Pedigree")]
    [Trait("Kind", "PlacePickup")]
    public async Task PlaceFromFrozenStack_Should_CarryPrimaryUnitOnly()
    {
        ITestPlayer joined = await World.JoinPlayer("PedPlaceFrozen");
        Block? block = ResolvePlaceableCrock();
        Assert.NotNull(block);

        // Crock may be MaxStackSize 1; build a Frozen bag then peel one Live unit to place.
        ItemStack bag = new ItemStack(block, Math.Max(3, block!.MaxStackSize > 1 ? 3 : 1));
        if (block.MaxStackSize <= 1)
        {
            bag = new() { StackSize = 3 };
            ProsequorStackPedigree.StampMaker(bag, "frozen-maker");
        }
        else
        {
            ProsequorStackPedigree.StampMaker(bag, "frozen-maker");
        }

        Assert.True(ProsequorStackPedigree.TryGetPrimaryBlob(bag, out ProsequorBlob unit));
        ItemStack held = new ItemStack(block, 1);
        ProsequorStackPedigree.ApplyUnitBlob(held, unit);

        BlockPos pos = joined.Player.Entity.Pos.AsBlockPos.AddCopy(3, 0, 0);
        EnsureFloor(pos);
        World.Api.World.BlockAccessor.SetBlock(0, pos);
        World.Api.World.BlockAccessor.SetBlock(block.BlockId, pos, held);
        EnsurePlaceCaptured(block, pos, held);

        BlockEntity? be = World.Api.World.BlockAccessor.GetBlockEntity(pos);
        Assert.NotNull(be);
        Assert.True(ProsequorBlockPedigreeStation.TryGetBlob(be, out ProsequorBlob blob));
        Assert.Equal("frozen-maker", blob.MakerUid);

        ItemStack picked = block.OnPickBlock(World.Api.World, pos);
        Assert.Equal(1, picked.StackSize);
        Assert.True(ProsequorStackPedigree.IsLive(picked));
        Assert.True(ProsequorStackPedigree.TryGetPrimaryBlob(picked, out ProsequorBlob pickBlob));
        Assert.Equal("frozen-maker", pickBlob.MakerUid);
    }

    [AtlasScenario]
    [Trait("Layer", "Pedigree")]
    [Trait("Kind", "PlacePickup")]
    public async Task GroundStorage_InventoryStack_Should_RetainPedigree()
    {
        // Unplaceable pottery rides in GS inventory; blob must stay on the ItemStack.
        ITestPlayer joined = await World.JoinPlayer("PedGsRetain");
        IWorldAccessor world = World.Api.World;
        Block? bowl = world.GetBlock(new AssetLocation("game:bowl-blue-raw"))
            ?? world.GetBlock(new AssetLocation("game:bowl-fire-raw"))
            ?? world.GetBlock(new AssetLocation("game:bowl-raw-blue"))
            ?? world.GetBlock(new AssetLocation("game:crock-blue-raw"));
        Assert.NotNull(bowl);

        ItemStack stack = new ItemStack(bowl, 1);
        ProsequorStackPedigree.StampMaker(stack, "gs-maker");
        ItemStack clone = stack.Clone();

        Assert.True(ProsequorStackPedigree.IsLive(clone));
        Assert.True(ProsequorStackPedigree.TryGetPrimaryBlob(clone, out ProsequorBlob blob));
        Assert.Equal("gs-maker", blob.MakerUid);

        // Simulate a GS slot holding the clone (what TryPutItem stores).
        ItemSlot slot = new DummySlot(clone);
        ItemStack? taken = slot.TakeOut(1);
        Assert.NotNull(taken);
        Assert.True(ProsequorStackPedigree.IsLive(taken));
        Assert.True(ProsequorStackPedigree.TryGetPrimaryBlob(taken, out ProsequorBlob takenBlob));
        Assert.Equal("gs-maker", takenBlob.MakerUid);
    }

    [AtlasScenario]
    [Trait("Layer", "Pedigree")]
    [Trait("Kind", "PlacePickup")]
    public async Task GetDrops_QuantityLuck_Should_KeepAttributedShortfall()
    {
        ITestPlayer joined = await World.JoinPlayer("PedQtyShort");
        (Block block, BlockPos pos, _) = PlaceStampedCrock(joined.Player, "short-maker");

        ItemStack[] drops = block.GetDrops(World.Api.World, pos, joined.Player, 1f);
        Assert.NotNull(drops);
        ItemStack? match = drops.FirstOrDefault(s => s != null && s.StackSize > 0);
        Assert.NotNull(match);

        // Simulate mutate-drops quantity luck after pedigree stamp.
        match!.StackSize = Math.Max(3, match.StackSize + 2);
        ProsequorStackPedigree.EnsureFrozenMatchesStackSize(match);

        int attributed = ProsequorStackPedigree.IsLive(match)
            ? 1
            : ProsequorStackPedigree.TotalQty(ProsequorStackPedigree.ReadFrozenGroups(match));
        Assert.Equal(1, attributed);
        Assert.True(ProsequorStackPedigree.TryGetPrimaryBlob(match, out ProsequorBlob blob));
        Assert.Equal("short-maker", blob.MakerUid);
        Assert.True(match.StackSize > attributed);
    }

    (Block block, BlockPos pos, ItemStack placed) PlaceStampedCrock(IPlayer player, string makerUid)
    {
        Block? block = ResolvePlaceableCrock();
        Assert.NotNull(block);

        ItemStack placed = new ItemStack(block, 1);
        ProsequorStackPedigree.StampMaker(placed, makerUid);
        Assert.True(ProsequorStackPedigree.IsLive(placed));

        BlockPos pos = player.Entity.Pos.AsBlockPos.AddCopy(2, 0, 0);
        EnsureFloor(pos);
        World.Api.World.BlockAccessor.SetBlock(0, pos);
        World.Api.World.BlockAccessor.SetBlock(block!.BlockId, pos, placed);
        EnsurePlaceCaptured(block, pos, placed);

        BlockEntity? be = World.Api.World.BlockAccessor.GetBlockEntity(pos);
        Assert.NotNull(be);
        return (block, pos, placed);
    }

    /// <summary>
    /// Atlas SetBlock may skip place hooks; invoke patched OnBlockPlaced entry points.
    /// </summary>
    void EnsurePlaceCaptured(Block block, BlockPos pos, ItemStack placed)
    {
        BlockEntity? be = World.Api.World.BlockAccessor.GetBlockEntity(pos);
        if (be != null && ProsequorBlockPedigreeStation.TryGetBlob(be, out _))
        {
            return;
        }

        block.OnBlockPlaced(World.Api.World, pos, placed);
        be = World.Api.World.BlockAccessor.GetBlockEntity(pos);
        be?.OnBlockPlaced(placed);
    }

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

    Block? ResolvePlaceableCrock()
    {
        IWorldAccessor world = World.Api.World;
        return world.GetBlock(new AssetLocation("game:crock-blue-fired"))
            ?? world.GetBlock(new AssetLocation("game:crock-fire-fired"))
            ?? world.GetBlock(new AssetLocation("game:crock-red-fired"))
            ?? world.GetBlock(new AssetLocation("game:flowerpot-fired-blue"))
            ?? world.GetBlock(new AssetLocation("game:ingotmold-raw-copper"));
    }
}
