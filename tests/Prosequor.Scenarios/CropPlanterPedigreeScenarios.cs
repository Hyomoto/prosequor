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
/// Crop planter MakerUid lives on the farmland BE and survives stage SetBlock.
/// </summary>
public class CropPlanterPedigreeScenarios : AtlasScenarioBase
{
    [AtlasScenario]
    [Trait("Layer", "Pedigree")]
    [Trait("Kind", "CropPlanter")]
    public async Task TryPlant_Should_StampPlanterOnFarmland()
    {
        ITestPlayer joined = await World.JoinPlayer("CropPlantStamp");
        IPlayer player = joined.Player;
        (BlockEntityFarmland farmland, BlockPos farmlandPos, BlockPos cropPos, Block crop1) =
            PlantCrop(player, "crop-plant-maker");

        Assert.True(
            ProsequorBlockPedigreeStation.TryGetCropPlanter(World.Api.World, cropPos, out string? planter));
        Assert.Equal(player.PlayerUID, planter);
        Assert.True(ProsequorBlockPedigreeStation.TryGetBlob(farmland, out ProsequorBlob blob));
        Assert.Equal(player.PlayerUID, blob.MakerUid);
        Assert.True(blob.TryGetContributorWeight(player.PlayerUID, out int planterWeight));
        Assert.Equal(1, planterWeight);

        TreeAttribute tree = new();
        Assert.True(ProsequorChunkPedigree.TryGet(World.Api.World, farmland.Pos, out ProsequorChunkPedigree.Box stored));
        stored.WriteTo(tree);
        ITreeAttribute? live = tree.GetTreeAttribute(ProsequorStackPedigree.LiveAttr);
        Assert.NotNull(live);
        Assert.Equal(player.PlayerUID, ProsequorBlob.ReadFrom(live!).MakerUid);
        Assert.Equal(crop1.Code, World.Api.World.BlockAccessor.GetBlock(cropPos).Code);
        Assert.Equal(farmlandPos, farmland.Pos);
    }

    [AtlasScenario]
    [Trait("Layer", "Pedigree")]
    [Trait("Kind", "CropPlanter")]
    public async Task GrowthStageSetBlock_Should_KeepPlanter()
    {
        ITestPlayer joined = await World.JoinPlayer("CropPlantGrow");
        IPlayer player = joined.Player;
        (BlockEntityFarmland farmland, _, BlockPos cropPos, Block crop1) =
            PlantCrop(player, "unused");

        Block? crop2 = ResolveNextStage(crop1);
        Assert.NotNull(crop2);
        World.Api.World.BlockAccessor.SetBlock(crop2!.BlockId, cropPos);

        Assert.True(
            ProsequorBlockPedigreeStation.TryGetCropPlanter(World.Api.World, cropPos, out string? planter));
        Assert.Equal(player.PlayerUID, planter);
        Assert.True(ProsequorBlockPedigreeStation.TryGetBlob(farmland, out _));
    }

    [AtlasScenario]
    [Trait("Layer", "Pedigree")]
    [Trait("Kind", "CropPlanter")]
    public async Task OnCropBlockBroken_Should_ClearPlanter()
    {
        ITestPlayer joined = await World.JoinPlayer("CropPlantClear");
        IPlayer player = joined.Player;
        (BlockEntityFarmland farmland, _, BlockPos cropPos, _) = PlantCrop(player, "unused");

        Assert.True(ProsequorBlockPedigreeStation.TryGetCropPlanter(World.Api.World, cropPos, out _));
        farmland.OnCropBlockBroken();
        Assert.False(ProsequorBlockPedigreeStation.TryGetCropPlanter(World.Api.World, cropPos, out _));
        Assert.False(ProsequorBlockPedigreeStation.TryGetBlob(farmland, out _));
    }

    (BlockEntityFarmland farmland, BlockPos farmlandPos, BlockPos cropPos, Block crop1) PlantCrop(
        IPlayer player,
        string _)
    {
        IWorldAccessor world = World.Api.World;
        BlockPos farmlandPos = player.Entity.Pos.AsBlockPos.AddCopy(2, 0, 0);
        EnsureFloor(farmlandPos);

        Block farmlandBlock = RequireFarmlandBlock();
        world.BlockAccessor.SetBlock(0, farmlandPos);
        world.BlockAccessor.SetBlock(farmlandBlock.BlockId, farmlandPos);
        if (world.BlockAccessor.GetBlockEntity(farmlandPos) is not BlockEntityFarmland farmland)
        {
            Assert.Fail("[prosequor] Expected BlockEntityFarmland after placing farmland.");
            throw new InvalidOperationException();
        }

        Block crop1 = RequireCropStage1();
        BlockPos cropPos = farmlandPos.UpCopy();
        world.BlockAccessor.SetBlock(0, cropPos);

        ItemStack seed = new(RequireSeedItem(), 1);
        DummySlot slot = new(seed);
        BlockSelection sel = new()
        {
            Position = farmlandPos,
            Face = BlockFacing.UP,
            HitPosition = new Vec3d(0.5, 1, 0.5)
        };

        bool planted = farmland.TryPlant(crop1, slot, player.Entity, sel);
        Assert.True(planted, "[prosequor] TryPlant should succeed on empty farmland.");
        Assert.True(AbilityBootstrap.IsCropBlock(world.BlockAccessor.GetBlock(cropPos)));
        return (farmland, farmlandPos, cropPos, crop1);
    }

    Block RequireFarmlandBlock()
    {
        IWorldAccessor world = World.Api.World;
        Block? block = world.GetBlock(new AssetLocation("game:farmland-dry-low"))
            ?? world.GetBlock(new AssetLocation("game:farmland-dry-medium"))
            ?? world.GetBlock(new AssetLocation("game:farmland-dry-high"));
        Assert.NotNull(block);
        return block!;
    }

    Block RequireCropStage1()
    {
        IWorldAccessor world = World.Api.World;
        foreach (string path in new[]
                 {
                     "game:crop-carrot-1",
                     "game:crop-flax-1",
                     "game:crop-spelt-1",
                     "game:crop-turnip-1"
                 })
        {
            Block? block = world.GetBlock(new AssetLocation(path));
            if (block != null && AbilityBootstrap.IsCropBlock(block))
            {
                return block;
            }
        }

        Assert.Fail("[prosequor] No stage-1 crop block found.");
        throw new InvalidOperationException();
    }

    Item RequireSeedItem()
    {
        IWorldAccessor world = World.Api.World;
        foreach (string path in new[]
                 {
                     "game:seeds-carrot",
                     "game:seeds-flax",
                     "game:seeds-spelt",
                     "game:seeds-turnip"
                 })
        {
            Item? item = world.GetItem(new AssetLocation(path));
            if (item != null)
            {
                return item;
            }
        }

        Assert.Fail("[prosequor] No plantable seed item found.");
        throw new InvalidOperationException();
    }

    Block? ResolveNextStage(Block crop1)
    {
        IWorldAccessor world = World.Api.World;
        AssetLocation? next = crop1.CodeWithParts("2");
        if (next == null)
        {
            return null;
        }

        return world.GetBlock(next);
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
