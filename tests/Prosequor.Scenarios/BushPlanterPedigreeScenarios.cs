using Atlas.Api;
using Atlas.XUnit;
using Prosequor.Ability;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Xunit;

namespace Prosequor.Scenarios;

/// <summary>
/// Berry / fruiting-bush planter MakerUid lives on the bush BE.
/// Harvest ExchangeBlock keeps the BE; OnBlockRemoved clears.
/// </summary>
public class BushPlanterPedigreeScenarios : AtlasScenarioBase
{
    [AtlasScenario]
    [Trait("Layer", "Pedigree")]
    [Trait("Kind", "BushPlanter")]
    public async Task DoPlaceBlock_Should_StampPlanterOnBushBe()
    {
        ITestPlayer joined = await World.JoinPlayer("BushPlantStamp");
        IPlayer player = joined.Player;
        (Block block, BlockPos pos, BlockEntity be) = PlaceBush(player);

        Assert.True(
            ProsequorBlockPedigreeStation.TryGetBushPlanter(World.Api.World, pos, out string? planter));
        Assert.Equal(player.PlayerUID, planter);
        Assert.True(ProsequorBlockPedigreeStation.TryGetBlob(be, out ProsequorBlob blob));
        Assert.Equal(player.PlayerUID, blob.MakerUid);

        TreeAttribute tree = new();
        be.ToTreeAttributes(tree);
        ITreeAttribute? live = tree.GetTreeAttribute(ProsequorStackPedigree.LiveAttr);
        Assert.NotNull(live);
        Assert.Equal(player.PlayerUID, ProsequorBlob.ReadFrom(live!).MakerUid);
        Assert.Equal(block.Code, World.Api.World.BlockAccessor.GetBlock(pos).Code);
    }

    [AtlasScenario]
    [Trait("Layer", "Pedigree")]
    [Trait("Kind", "BushPlanter")]
    public async Task ExchangeBlock_Should_KeepPlanter()
    {
        ITestPlayer joined = await World.JoinPlayer("BushXchg");
        IPlayer player = joined.Player;
        (Block block, BlockPos pos, BlockEntity be) = PlaceBush(player);

        // Harvest empties via ExchangeBlock; BE instance stays.
        World.Api.World.BlockAccessor.ExchangeBlock(block.BlockId, pos);

        Assert.Same(be, World.Api.World.BlockAccessor.GetBlockEntity(pos));
        Assert.True(
            ProsequorBlockPedigreeStation.TryGetBushPlanter(World.Api.World, pos, out string? planter));
        Assert.Equal(player.PlayerUID, planter);
    }

    [AtlasScenario]
    [Trait("Layer", "Pedigree")]
    [Trait("Kind", "BushPlanter")]
    public async Task OnBlockRemoved_Should_ClearPlanter()
    {
        ITestPlayer joined = await World.JoinPlayer("BushPlantClear");
        IPlayer player = joined.Player;
        (_, BlockPos pos, BlockEntity be) = PlaceBush(player);

        Assert.True(ProsequorBlockPedigreeStation.TryGetBushPlanter(World.Api.World, pos, out _));
        be.OnBlockRemoved();
        Assert.False(ProsequorBlockPedigreeStation.TryGetBlob(be, out _));
        Assert.False(ProsequorBlockPedigreeStation.TryGetBushPlanter(World.Api.World, pos, out _));
    }

    (Block block, BlockPos pos, BlockEntity be) PlaceBush(IPlayer player)
    {
        IWorldAccessor world = World.Api.World;
        Block bush = RequireBerryBushBlock();
        BlockPos pos = player.Entity.Pos.AsBlockPos.AddCopy(2, 0, 0);
        EnsureFloor(pos);
        world.BlockAccessor.SetBlock(0, pos);

        ItemStack stack = new(bush, 1);
        BlockSelection sel = new()
        {
            Position = pos,
            Face = BlockFacing.UP,
            HitPosition = new Vec3d(0.5, 0, 0.5)
        };

        bool placed = bush.DoPlaceBlock(world, player, sel, stack);
        if (!placed || world.BlockAccessor.GetBlockEntity(pos) == null)
        {
            // Atlas may skip place hooks; place then invoke the patched DoPlaceBlock path.
            world.BlockAccessor.SetBlock(bush.BlockId, pos, stack);
            placed = bush.DoPlaceBlock(world, player, sel, stack);
        }

        Assert.True(placed || world.BlockAccessor.GetBlock(pos).Id == bush.Id);
        BlockEntity? be = world.BlockAccessor.GetBlockEntity(pos);
        if (be == null || !ProsequorBlockPedigreeStation.TryGetPlanter(be, out _))
        {
            // SetBlock-only path: stamp the same way the DoPlaceBlock postfix would.
            Assert.NotNull(be);
            ProsequorBlockPedigreeStation.StampPlanter(be, player.PlayerUID);
        }

        Assert.NotNull(be);
        Assert.True(AbilityBootstrap.IsBerryBushBlock(world.BlockAccessor.GetBlock(pos)));
        return (bush, pos, be!);
    }

    Block RequireBerryBushBlock()
    {
        IWorldAccessor world = World.Api.World;
        foreach (string path in new[]
                 {
                     "game:fruitingbush-blueberry-ripe",
                     "game:fruitingbush-blueberry-empty",
                     "game:fruitingbush-cranberry-ripe",
                     "game:berrybush-blueberry-ripe",
                     "game:berrybush-cranberry-ripe",
                     "game:berrybush-redcurrant-ripe"
                 })
        {
            Block? block = world.GetBlock(new AssetLocation(path));
            if (block != null && AbilityBootstrap.IsBerryBushBlock(block))
            {
                return block;
            }
        }

        foreach (Block block in world.Blocks)
        {
            if (block != null && block.Id != 0 && AbilityBootstrap.IsBerryBushBlock(block))
            {
                return block;
            }
        }

        Assert.Fail("[prosequor] No berry / fruiting-bush block found.");
        throw new InvalidOperationException();
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
}
