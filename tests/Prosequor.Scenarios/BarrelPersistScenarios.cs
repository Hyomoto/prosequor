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
/// Sealed barrels must survive the dedicated-server chunk pack/unpack path.
/// <c>ServerChunk.AfterDeserialization</c> calls <c>FromTreeAttributes</c> before
/// <c>Initialize</c>, so <c>Api</c> is still null. Vanilla guards recipe lookup; a sealer
/// postfix that dirties the liquid slot does not, and the chunk then discards the BE.
/// </summary>
public class BarrelPersistScenarios : AtlasScenarioBase
{
    const string SealerAttr = "prosequorBarrelSealer";
    const int JuiceUnits = 10;

    static int nextOffset = 8;

    /// <summary>
    /// Same sequence as <c>ServerChunk.AfterDeserialization</c>: create the BE, attach
    /// behaviors, then <c>FromTreeAttributes</c> while <c>Api</c> is still null.
    /// </summary>
    [AtlasScenario]
    [Trait("Layer", "Pedigree")]
    [Trait("Kind", "BarrelPersist")]
    public async Task BarrelSealed_Should_KeepContents_When_DeserializedBeforeInitialize()
    {
        ITestPlayer joined = await World.JoinPlayer("BarrelDeser");
        (BlockEntityBarrel barrel, ItemStack juice) = SealJuiceBarrel(joined.Player);
        string juiceCode = juice.Collectible.Code.ToString();
        int juiceSize = juice.StackSize;

        TreeAttribute tree = new();
        barrel.ToTreeAttributes(tree);
        Assert.Equal(joined.Player.PlayerUID, tree.GetString(SealerAttr));
        Assert.True(tree.GetBool("sealed"));

        ICoreAPI api = World.Api;
        string? className = api.ClassRegistry.GetBlockEntityClass(typeof(BlockEntityBarrel));
        Assert.False(string.IsNullOrEmpty(className), "Expected BlockEntityBarrel to be registered.");
        BlockEntity created = api.ClassRegistry.CreateBlockEntity(className);
        Assert.NotNull(created);
        created.CreateBehaviors(barrel.Block, api.World);
        Assert.Null(created.Api);

        Exception? thrown = Record.Exception(() => created.FromTreeAttributes(tree, api.World));
        Assert.True(thrown == null, thrown?.ToString());

        Assert.True(created is BlockEntityBarrel, $"Expected BlockEntityBarrel, got {created.GetType().Name}.");
        var restored = (BlockEntityBarrel)created;
        Assert.True(restored.Sealed);
        ItemStack? liquid = restored.Inventory[1]?.Itemstack;
        Assert.NotNull(liquid);
        Assert.Equal(juiceCode, liquid!.Collectible.Code.ToString());
        Assert.Equal(juiceSize, liquid.StackSize);
        Assert.Equal(joined.Player.PlayerUID, CraftAttribution.TryGetMakerUid(liquid));

        TreeAttribute roundTrip = new();
        restored.ToTreeAttributes(roundTrip);
        Assert.Equal(joined.Player.PlayerUID, roundTrip.GetString(SealerAttr));
        Assert.True(ProsequorBlockPedigreeStation.TryGetSoleContributor(restored, out string? sealer));
        Assert.Equal(joined.Player.PlayerUID, sealer);
    }

    (BlockEntityBarrel barrel, ItemStack juice) SealJuiceBarrel(IPlayer player)
    {
        IWorldAccessor world = World.Api.World;
        int offset = nextOffset;
        nextOffset += 4;
        BlockPos pos = player.Entity.Pos.AsBlockPos.AddCopy(offset, 1, 0);
        EnsureFloor(pos);

        Block barrelBlock = RequireBlock("game:barrel-burned", "barrel");
        world.BlockAccessor.SetBlock(0, pos);
        world.BlockAccessor.SetBlock(barrelBlock.BlockId, pos);
        if (world.BlockAccessor.GetBlockEntity(pos) is not BlockEntityBarrel barrel)
        {
            Assert.Fail($"Expected BlockEntityBarrel at {pos}, got {world.BlockAccessor.GetBlockEntity(pos)?.GetType().Name ?? "null"}.");
            throw new InvalidOperationException();
        }

        Item juiceItem = RequireItem("game:juiceportion-apple", "juiceportion-apple");
        ItemStack juice = new(juiceItem, JuiceUnits);
        barrel.Inventory[1].Itemstack = juice;
        barrel.Inventory[1].MarkDirty();
        Assert.True(barrel.GetCanSeal(player), "Expected a cider barrel recipe for apple juice.");
        barrel.OnReceivedClientPacket(player, 1337, null);
        Assert.True(barrel.Sealed);
        Assert.Equal(player.PlayerUID, CraftAttribution.TryGetMakerUid(barrel.Inventory[1].Itemstack));
        return (barrel, juice);
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
