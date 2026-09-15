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
/// Process-starter sole-contributor stamps on barrels (seal → MakerUid at craft complete)
/// and firepits (cook/smelt start).
/// </summary>
public class ProcessStarterScenarios : AtlasScenarioBase
{
    [AtlasScenario]
    [Trait("Layer", "Pedigree")]
    [Trait("Kind", "ProcessStarter")]
    public async Task BarrelSeal_Should_StampSoleContributor()
    {
        ITestPlayer joined = await World.JoinPlayer("BarrelSeal");
        IPlayer player = joined.Player;
        BlockEntityBarrel barrel = PlaceBarrel(player);

        ProsequorBlockPedigreeStation.StampSoleContributor(barrel, player.PlayerUID);

        Assert.True(ProsequorBlockPedigreeStation.TryGetSoleContributor(barrel, out string? uid));
        Assert.Equal(player.PlayerUID, uid);
        Assert.True(ProsequorBlockPedigreeStation.TryGetBlob(barrel, out ProsequorBlob blob));
        Assert.Single(blob.Contributors);
        Assert.Equal(1, blob.Contributors[0].Weight);
    }

    [AtlasScenario]
    [Trait("Layer", "Pedigree")]
    [Trait("Kind", "ProcessStarter")]
    public async Task BarrelSeal_Should_ReplaceNotAccumulate()
    {
        ITestPlayer aJoin = await World.JoinPlayer("BarrelA");
        ITestPlayer bJoin = await World.JoinPlayer("BarrelB");
        BlockEntityBarrel barrel = PlaceBarrel(aJoin.Player);

        ProsequorBlockPedigreeStation.StampSoleContributor(barrel, aJoin.Player.PlayerUID);
        ProsequorBlockPedigreeStation.StampSoleContributor(barrel, bJoin.Player.PlayerUID);

        Assert.True(ProsequorBlockPedigreeStation.TryGetSoleContributor(barrel, out string? uid));
        Assert.Equal(bJoin.Player.PlayerUID, uid);
        Assert.True(ProsequorBlockPedigreeStation.TryGetBlob(barrel, out ProsequorBlob blob));
        Assert.Single(blob.Contributors);
        Assert.False(blob.TryGetContributorWeight(aJoin.Player.PlayerUID, out _));
    }

    [AtlasScenario]
    [Trait("Layer", "Pedigree")]
    [Trait("Kind", "ProcessStarter")]
    public async Task BarrelLegacyMadeBy_Should_PromoteToSoleContributor()
    {
        ITestPlayer joined = await World.JoinPlayer("BarrelLegacy");
        BlockEntityBarrel barrel = PlaceBarrel(joined.Player);

        TreeAttribute tree = new();
        barrel.ToTreeAttributes(tree);
        tree.SetBool("sealed", true);
        tree.SetString(CraftAttribution.MakerAttr, "legacy-sealer");

        ProsequorBlockPedigreeStation.Clear(barrel);
        barrel.FromTreeAttributes(tree, World.Api.World);

        Assert.True(ProsequorBlockPedigreeStation.TryGetSoleContributor(barrel, out string? uid));
        Assert.Equal("legacy-sealer", uid);
    }

    [AtlasScenario]
    [Trait("Layer", "Pedigree")]
    [Trait("Kind", "ProcessStarter")]
    public async Task BarrelCraftComplete_Should_StampMakerFromSoleContributor()
    {
        ITestPlayer joined = await World.JoinPlayer("BarrelMaker");
        IPlayer player = joined.Player;
        BlockEntityBarrel barrel = PlaceBarrel(player);

        Item? leather = World.Api.World.GetItem(new AssetLocation("game:leather-normal-plain"));
        Assert.NotNull(leather);
        ItemStack stack = new(leather, 1);
        barrel.Inventory[0].Itemstack = stack;
        barrel.Inventory[0].MarkDirty();

        ProsequorBlockPedigreeStation.StampSoleContributor(barrel, player.PlayerUID);
        Assert.True(ProsequorBlockPedigreeStation.TryGetSoleContributor(barrel, out string? starter));
        Assert.Equal(player.PlayerUID, starter);

        // Same egress as BarrelCraftCompletePatch after sealed→unsealed.
        CraftAttribution.StampMakerUid(barrel.Inventory[0].Itemstack, starter);
        Assert.Equal(player.PlayerUID, CraftAttribution.TryGetMakerUid(barrel.Inventory[0].Itemstack));
    }

    [AtlasScenario]
    [Trait("Layer", "Pedigree")]
    [Trait("Kind", "ProcessStarter")]
    public async Task CookServe_Should_CopyPotPedigreeOntoMealStack()
    {
        ITestPlayer joined = await World.JoinPlayer("CookServe");
        IPlayer player = joined.Player;

        Block? cookedPot = World.Api.World.GetBlock(new AssetLocation("game:claypot-blue-cooked"))
            ?? World.Api.World.GetBlock(new AssetLocation("game:claypot-orange-cooked"));
        Block? mealBlock = World.Api.World.GetBlock(new AssetLocation("game:bowl-blue-meal"))
            ?? World.Api.World.GetBlock(new AssetLocation("game:bowl-brown-meal"));
        Assert.NotNull(cookedPot);
        Assert.NotNull(mealBlock);

        ItemStack pot = new(cookedPot, 1);
        CraftAttribution.StampMakerUid(pot, player.PlayerUID);
        ProsequorStackPedigree.StampQualityRank(pot, 4);
        CraftAttributeMods.SetFactor(pot, FreshnessAttributeMutator.KeyName, 1.25f);

        Assert.True(ProsequorStackPedigree.TryGetPrimaryBlob(pot, out ProsequorBlob potBlob));
        Assert.True(potBlob.HasPersistable);

        ItemStack meal = new(mealBlock, 1);
        ProsequorStackPedigree.ApplyUnitBlob(meal, potBlob);
        AbilityBootstrap.AttributeMutators.Rematerialize(meal, World.Api.World);

        Assert.Equal(player.PlayerUID, CraftAttribution.TryGetMakerUid(meal));
        Assert.True(ProsequorStackPedigree.TryGetQualityRank(meal, out int rank));
        Assert.Equal(4, rank);
        Assert.Equal(1.25f, CraftAttributeMods.GetFactor(meal, FreshnessAttributeMutator.KeyName), 3);
    }

    [AtlasScenario]
    [Trait("Layer", "Pedigree")]
    [Trait("Kind", "ProcessStarter")]
    public async Task FirepitRisingEdge_Should_StampInteractor_And_NotRestampMidProcess()
    {
        ITestPlayer aJoin = await World.JoinPlayer("PitStartA");
        ITestPlayer bJoin = await World.JoinPlayer("PitStartB");
        BlockEntityFirepit firepit = PlaceFirepit(aJoin.Player);

        FirepitProcessStarterStation.NoteInteractor(firepit, aJoin.Player);
        FirepitProcessStarterStation.ObserveProcessing(firepit, processing: false);
        Assert.False(ProsequorBlockPedigreeStation.TryGetSoleContributor(firepit, out _));

        FirepitProcessStarterStation.ObserveProcessing(firepit, processing: true);
        Assert.True(ProsequorBlockPedigreeStation.TryGetSoleContributor(firepit, out string? first));
        Assert.Equal(aJoin.Player.PlayerUID, first);

        FirepitProcessStarterStation.NoteInteractor(firepit, bJoin.Player);
        FirepitProcessStarterStation.ObserveProcessing(firepit, processing: true);
        Assert.True(ProsequorBlockPedigreeStation.TryGetSoleContributor(firepit, out string? mid));
        Assert.Equal(aJoin.Player.PlayerUID, mid);
        Assert.True(ProsequorBlockPedigreeStation.TryGetBlob(firepit, out ProsequorBlob midBlob));
        Assert.Single(midBlob.Contributors);

        FirepitProcessStarterStation.ObserveProcessing(firepit, processing: false);
        FirepitProcessStarterStation.ObserveProcessing(firepit, processing: true);
        Assert.True(ProsequorBlockPedigreeStation.TryGetSoleContributor(firepit, out string? next));
        Assert.Equal(bJoin.Player.PlayerUID, next);
        Assert.True(ProsequorBlockPedigreeStation.TryGetBlob(firepit, out ProsequorBlob nextBlob));
        Assert.Single(nextBlob.Contributors);
    }

    BlockEntityBarrel PlaceBarrel(IPlayer player)
    {
        IWorldAccessor world = World.Api.World;
        BlockPos pos = player.Entity.Pos.AsBlockPos.AddCopy(2, 0, 0);
        EnsureFloor(pos);

        Block barrel = RequireBarrelBlock();
        world.BlockAccessor.SetBlock(0, pos);
        world.BlockAccessor.SetBlock(barrel.BlockId, pos);
        if (world.BlockAccessor.GetBlockEntity(pos) is not BlockEntityBarrel be)
        {
            Assert.Fail($"Expected BlockEntityBarrel at {pos}, got {world.BlockAccessor.GetBlockEntity(pos)?.GetType().Name ?? "null"}.");
            throw new InvalidOperationException();
        }

        return be;
    }

    BlockEntityFirepit PlaceFirepit(IPlayer player)
    {
        IWorldAccessor world = World.Api.World;
        BlockPos pos = player.Entity.Pos.AsBlockPos.AddCopy(3, 0, 0);
        EnsureFloor(pos);

        Block firepit = RequireFirepitBlock();
        world.BlockAccessor.SetBlock(0, pos);
        world.BlockAccessor.SetBlock(firepit.BlockId, pos);
        if (world.BlockAccessor.GetBlockEntity(pos) is not BlockEntityFirepit be)
        {
            Assert.Fail($"Expected BlockEntityFirepit at {pos}, got {world.BlockAccessor.GetBlockEntity(pos)?.GetType().Name ?? "null"}.");
            throw new InvalidOperationException();
        }

        return be;
    }

    Block RequireBarrelBlock()
    {
        IWorldAccessor world = World.Api.World;
        Block? block = world.GetBlock(new AssetLocation("game:barrel-burned"))
            ?? world.GetBlock(new AssetLocation("game:barrel"))
            ?? world.GetBlock(new AssetLocation("barrel-burned"))
            ?? world.GetBlock(new AssetLocation("barrel"));
        Assert.NotNull(block);
        return block!;
    }

    Block RequireFirepitBlock()
    {
        IWorldAccessor world = World.Api.World;
        Block? block = world.GetBlock(new AssetLocation("game:firepit-cold"))
            ?? world.GetBlock(new AssetLocation("game:firepit-extinct"))
            ?? world.GetBlock(new AssetLocation("game:firepit-lit"))
            ?? world.GetBlock(new AssetLocation("firepit-cold"));
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
