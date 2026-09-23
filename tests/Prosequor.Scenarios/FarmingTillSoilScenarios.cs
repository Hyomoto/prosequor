using Atlas.Api;
using Atlas.XUnit;
using Prosequor.Ability;
using Prosequor.Player;
using Prosequor.Xp.Activity;
using Prosequor.Xp.Adapters;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;
using Xunit;

namespace Prosequor.Scenarios;

/// <summary>
/// Till / plant / fertilize / water each add care credit weight 1 once per crop cycle;
/// growth shares resolve for <c>payee: contributors</c>; harvest clears the bag.
/// </summary>
public class FarmingTillSoilScenarios : AtlasScenarioBase
{
    [AtlasScenario]
    [Trait("Layer", "Pedigree")]
    [Trait("Kind", "TillSoil")]
    public async Task OnSoilConverted_Should_StampTillerContributor()
    {
        ITestPlayer joined = await World.JoinPlayer("TillStamp");
        IPlayer player = joined.Player;
        (BlockEntityFarmland farmland, BlockPos pos, Block soil) = PlaceFarmlandFromSoil(player);

        TillSoilXp.OnSoilConverted(World.Api, player, pos, soil);

        Assert.True(ProsequorBlockPedigreeStation.TryGetBlob(farmland, out ProsequorBlob blob));
        Assert.Null(blob.MakerUid);
        Assert.True(blob.TryGetContributorWeight(player.PlayerUID, out int weight));
        Assert.Equal(1, weight);
    }

    [AtlasScenario]
    [Trait("Layer", "Pedigree")]
    [Trait("Kind", "TillSoil")]
    public async Task TryPlant_Should_KeepTiller_And_AddPlanterContributor()
    {
        ITestPlayer tillerJoin = await World.JoinPlayer("TillKeepA");
        ITestPlayer planterJoin = await World.JoinPlayer("TillKeepB");
        IPlayer tiller = tillerJoin.Player;
        IPlayer planter = planterJoin.Player;

        (BlockEntityFarmland farmland, BlockPos farmlandPos, _) = PlaceFarmlandFromSoil(tiller);
        TillSoilXp.OnSoilConverted(World.Api, tiller, farmlandPos, soil: null);

        PlantOnFarmland(farmland, farmlandPos, planter);

        Assert.True(ProsequorBlockPedigreeStation.TryGetBlob(farmland, out ProsequorBlob blob));
        Assert.Equal(planter.PlayerUID, blob.MakerUid);
        Assert.True(blob.TryGetContributorWeight(tiller.PlayerUID, out int tillerW));
        Assert.Equal(1, tillerW);
        Assert.True(blob.TryGetContributorWeight(planter.PlayerUID, out int planterW));
        Assert.Equal(1, planterW);

        IReadOnlyList<Deed.ContributorShare> shares =
            GrowthXp.ResolveGrowthShares(farmland, planter.PlayerUID);
        Assert.Equal(2, shares.Count);
        Assert.Contains(shares, s => s.PlayerUid == tiller.PlayerUID && Math.Abs(s.Weight - 1f) < 0.001f);
        Assert.Contains(shares, s => s.PlayerUid == planter.PlayerUID && Math.Abs(s.Weight - 1f) < 0.001f);
    }

    [AtlasScenario]
    [Trait("Layer", "Pedigree")]
    [Trait("Kind", "TillSoil")]
    public async Task SamePlayer_TillThenPlant_Should_WeightTwo()
    {
        ITestPlayer joined = await World.JoinPlayer("TillSame");
        IPlayer player = joined.Player;
        (BlockEntityFarmland farmland, BlockPos farmlandPos, _) = PlaceFarmlandFromSoil(player);
        TillSoilXp.OnSoilConverted(World.Api, player, farmlandPos, soil: null);
        PlantOnFarmland(farmland, farmlandPos, player);

        Assert.True(ProsequorBlockPedigreeStation.TryGetBlob(farmland, out ProsequorBlob blob));
        Assert.Equal(player.PlayerUID, blob.MakerUid);
        Assert.True(blob.TryGetContributorWeight(player.PlayerUID, out int weight));
        Assert.Equal(2, weight);
    }

    [AtlasScenario]
    [Trait("Layer", "Pedigree")]
    [Trait("Kind", "TillSoil")]
    public async Task ResolveGrowthShares_EmptyBag_Should_SynthesizePlanter()
    {
        ITestPlayer joined = await World.JoinPlayer("TillSynth");
        IPlayer player = joined.Player;
        (BlockEntityFarmland farmland, BlockPos farmlandPos, _) = PlaceFarmlandFromSoil(player);
        // Maker only — no contributor bag (legacy / direct StampPlanter without AddContributor).
        ProsequorBlockPedigreeStation.StampPlanter(farmland, player.PlayerUID);
        // Clear contributors that StampPlanter no longer wipes — simulate empty bag by
        // writing maker-only via ClearContributors after a fake add, or re-stamp after clear.
        ProsequorBlockPedigreeStation.ClearContributors(farmland);

        Assert.True(ProsequorBlockPedigreeStation.TryGetBlob(farmland, out ProsequorBlob blob));
        Assert.Equal(player.PlayerUID, blob.MakerUid);
        Assert.Empty(blob.Contributors);

        IReadOnlyList<Deed.ContributorShare> shares =
            GrowthXp.ResolveGrowthShares(farmland, player.PlayerUID);
        Assert.Single(shares);
        Assert.Equal(player.PlayerUID, shares[0].PlayerUid);
        Assert.Equal(1f, shares[0].Weight);

        // Plant path still clears on harvest.
        PlantOnFarmland(farmland, farmlandPos, player);
        farmland.OnCropBlockBroken();
        Assert.False(ProsequorBlockPedigreeStation.TryGetBlob(farmland, out _));
    }

    [AtlasScenario]
    [Trait("Layer", "Pedigree")]
    [Trait("Kind", "TillSoil")]
    public async Task CareCredits_Should_AddOncePerRole()
    {
        ITestPlayer joined = await World.JoinPlayer("CareOnce");
        IPlayer player = joined.Player;
        (BlockEntityFarmland farmland, BlockPos farmlandPos, _) = PlaceFarmlandFromSoil(player);

        Assert.True(ProsequorBlockPedigreeStation.TryAddCareCredit(
            farmland, player.PlayerUID, FarmlandCareKind.Till));
        Assert.True(ProsequorBlockPedigreeStation.TryAddCareCredit(
            farmland, player.PlayerUID, FarmlandCareKind.Plant));
        Assert.True(ProsequorBlockPedigreeStation.TryAddCareCredit(
            farmland, player.PlayerUID, FarmlandCareKind.Fertilize));
        Assert.True(ProsequorBlockPedigreeStation.TryAddCareCredit(
            farmland, player.PlayerUID, FarmlandCareKind.Water));
        Assert.False(ProsequorBlockPedigreeStation.TryAddCareCredit(
            farmland, player.PlayerUID, FarmlandCareKind.Water));
        Assert.False(ProsequorBlockPedigreeStation.TryAddCareCredit(
            farmland, player.PlayerUID, FarmlandCareKind.Fertilize));

        Assert.True(ProsequorBlockPedigreeStation.TryGetBlob(farmland, out ProsequorBlob blob));
        Assert.True(blob.TryGetContributorWeight(player.PlayerUID, out int weight));
        Assert.Equal(4, weight);

        Assert.True(ProsequorChunkPedigree.TryGet(World.Api.World, farmland.Pos, out ProsequorChunkPedigree.Box stored));
        TreeAttribute tree = new();
        stored.WriteTo(tree);
        ProsequorBlockPedigreeStation.Clear(farmland);
        Assert.False(ProsequorBlockPedigreeStation.TryGetBlob(farmland, out _));
        ProsequorChunkPedigree.Box restoredBox = new();
        restoredBox.ReadFrom(tree);
        ProsequorChunkPedigree.Set(World.Api.World, farmland.Pos, restoredBox);
        Assert.True(ProsequorBlockPedigreeStation.TryGetBlob(farmland, out ProsequorBlob restored));
        Assert.True(restored.TryGetContributorWeight(player.PlayerUID, out int restoredW));
        Assert.Equal(4, restoredW);
        Assert.False(ProsequorBlockPedigreeStation.TryAddCareCredit(
            farmland, player.PlayerUID, FarmlandCareKind.Water));

        PlantOnFarmland(farmland, farmlandPos, player);
        farmland.OnCropBlockBroken();
        Assert.False(ProsequorBlockPedigreeStation.TryGetBlob(farmland, out _));
        Assert.True(ProsequorBlockPedigreeStation.TryAddCareCredit(
            farmland, player.PlayerUID, FarmlandCareKind.Water));
    }

    [AtlasScenario]
    [Trait("Layer", "Pedigree")]
    [Trait("Kind", "TillSoil")]
    public async Task Helpers_Should_SplitCareCredits()
    {
        ITestPlayer planterJoin = await World.JoinPlayer("CarePlant");
        ITestPlayer waterJoin = await World.JoinPlayer("CareWater");
        IPlayer planter = planterJoin.Player;
        IPlayer waterer = waterJoin.Player;

        (BlockEntityFarmland farmland, BlockPos farmlandPos, _) = PlaceFarmlandFromSoil(planter);
        PlantOnFarmland(farmland, farmlandPos, planter);
        WateringXp.OnWatered(waterer, farmland);

        Assert.True(ProsequorBlockPedigreeStation.TryGetBlob(farmland, out ProsequorBlob blob));
        Assert.Equal(planter.PlayerUID, blob.MakerUid);
        Assert.True(blob.TryGetContributorWeight(planter.PlayerUID, out int planterW));
        Assert.Equal(1, planterW);
        Assert.True(blob.TryGetContributorWeight(waterer.PlayerUID, out int waterW));
        Assert.Equal(1, waterW);

        IReadOnlyList<Deed.ContributorShare> shares =
            GrowthXp.ResolveGrowthShares(farmland, planter.PlayerUID);
        Assert.Equal(2, shares.Count);
    }

    [AtlasScenario]
    [Trait("Layer", "Pedigree")]
    [Trait("Kind", "FertilizerAbsorb")]
    public async Task Remainder_Should_EmitOnePercent_AfterFourVanillaSteps()
    {
        ITestPlayer joined = await World.JoinPlayer("FertRemain");
        IPlayer player = joined.Player;
        (BlockEntityFarmland farmland, _, _) = PlaceFarmlandFromSoil(player, offsetX: 4);
        ProsequorBlockPedigreeStation.TryAddCareCredit(
            farmland, player.PlayerUID, FarmlandCareKind.Fertilize);

        int paid = 0;
        for (int i = 0; i < 4; i++)
        {
            paid += FertilizerAbsorbXp.OnAbsorbed(farmland, 0.25f);
        }

        Assert.Equal(1, paid);
        Assert.Equal(0f, ProsequorBlockPedigreeStation.GetAbsorbRemainder(farmland));
    }

    [AtlasScenario]
    [Trait("Layer", "Pedigree")]
    [Trait("Kind", "FertilizerAbsorb")]
    public async Task EmptyBag_Should_BankRemainder_And_NotEmit()
    {
        ITestPlayer joined = await World.JoinPlayer("FertEmpty");
        IPlayer player = joined.Player;
        (BlockEntityFarmland farmland, _, _) = PlaceFarmlandFromSoil(player, offsetX: 6);

        Assert.Equal(0, FertilizerAbsorbXp.OnAbsorbed(farmland, 2f));
        Assert.Equal(2f, ProsequorBlockPedigreeStation.GetAbsorbRemainder(farmland));
    }

    [AtlasScenario]
    [Trait("Layer", "Pedigree")]
    [Trait("Kind", "FertilizerAbsorb")]
    public async Task Harvest_Should_KeepAbsorbMul_And_DelayPay_UntilNewCredit()
    {
        ITestPlayer joined = await World.JoinPlayer("FertHarvest");
        IPlayer player = joined.Player;
        (BlockEntityFarmland farmland, BlockPos farmlandPos, _) = PlaceFarmlandFromSoil(player, offsetX: 8);
        ProsequorBlockPedigreeStation.StampAbsorbMultiplier(farmland, 1.25f);
        ProsequorBlockPedigreeStation.TryAddCareCredit(
            farmland, player.PlayerUID, FarmlandCareKind.Fertilize);

        Assert.True(ProsequorChunkPedigree.TryGet(World.Api.World, farmland.Pos, out ProsequorChunkPedigree.Box stored));
        TreeAttribute tree = new();
        stored.WriteTo(tree);
        ProsequorBlockPedigreeStation.Clear(farmland);
        ProsequorChunkPedigree.Box restoredBox = new();
        restoredBox.ReadFrom(tree);
        ProsequorChunkPedigree.Set(World.Api.World, farmland.Pos, restoredBox);
        Assert.Equal(1.25f, ProsequorBlockPedigreeStation.GetAbsorbMultiplier(farmland));

        PlantOnFarmland(farmland, farmlandPos, player);
        farmland.OnCropBlockBroken();
        Assert.False(ProsequorBlockPedigreeStation.TryGetBlob(farmland, out _));
        Assert.Equal(1.25f, ProsequorBlockPedigreeStation.GetAbsorbMultiplier(farmland));
        Assert.Equal(0, FertilizerAbsorbXp.OnAbsorbed(farmland, 1.5f));
        Assert.Equal(1.5f, ProsequorBlockPedigreeStation.GetAbsorbRemainder(farmland));

        ProsequorBlockPedigreeStation.TryAddCareCredit(
            farmland, player.PlayerUID, FarmlandCareKind.Fertilize);
        Assert.Equal(2, FertilizerAbsorbXp.OnAbsorbed(farmland, 0.6f));
        Assert.InRange(ProsequorBlockPedigreeStation.GetAbsorbRemainder(farmland), 0.09f, 0.11f);
    }

    [AtlasScenario]
    [Trait("Layer", "Pedigree")]
    [Trait("Kind", "FertilizerAbsorb")]
    public async Task AbsorbXp_Should_BeCompostingNeutral()
    {
        ITestPlayer joined = await World.JoinPlayer("FertNeutral");
        IPlayer player = joined.Player;
        (BlockEntityFarmland vanilla, _, _) = PlaceFarmlandFromSoil(player, offsetX: 10);
        (BlockEntityFarmland enriched, _, _) = PlaceFarmlandFromSoil(player, offsetX: 12);
        ProsequorBlockPedigreeStation.TryAddCareCredit(
            vanilla, player.PlayerUID, FarmlandCareKind.Fertilize);
        ProsequorBlockPedigreeStation.TryAddCareCredit(
            enriched, player.PlayerUID, FarmlandCareKind.Fertilize);
        ProsequorBlockPedigreeStation.StampAbsorbMultiplier(enriched, 1.25f);

        int vanillaPaid = DrainSlowRelease(vanilla, total: 56f, step: 0.25f);
        int enrichedPaid = DrainSlowRelease(enriched, total: 56f, step: 0.25f * 1.25f);

        Assert.Equal(56, vanillaPaid);
        Assert.Equal(vanillaPaid, enrichedPaid);
        Assert.Equal(0f, ProsequorBlockPedigreeStation.GetAbsorbRemainder(vanilla));
        Assert.Equal(0f, ProsequorBlockPedigreeStation.GetAbsorbRemainder(enriched));
    }

    static int DrainSlowRelease(BlockEntityFarmland farmland, float total, float step)
    {
        int paid = 0;
        float left = total;
        while (left > 0.0001f)
        {
            float bite = Math.Min(step, left);
            left -= bite;
            paid += FertilizerAbsorbXp.OnAbsorbed(farmland, bite);
        }

        return paid;
    }

    [AtlasScenario]
    [Trait("Layer", "Pedigree")]
    [Trait("Kind", "FertilizerAbsorb")]
    public async Task BushStore_Should_PayFarmingXp_ForOnePercent()
    {
        ITestPlayer joined = await World.JoinPlayer("FertBush");
        IPlayer player = joined.Player;
        EntityBehaviorProgress progress = player.Entity.GetBehavior<EntityBehaviorProgress>()
            ?? throw new InvalidOperationException("[prosequor] Joined player has no progress behavior.");
        BlockEntityBerryBushFarmland store = PlaceBushNutritionStore(player, offsetX: 18);
        ProsequorBlockPedigreeStation.TryAddCareCredit(
            store, player.PlayerUID, FarmlandCareKind.Fertilize);

        float before = TotalFarmingXp(progress);
        Assert.Equal(1, FertilizerAbsorbXp.OnAbsorbed(store, 1f));
        Assert.Equal(0.01f, TotalFarmingXp(progress) - before, precision: 3);
    }

    [AtlasScenario]
    [Trait("Layer", "Pedigree")]
    [Trait("Kind", "WaterCredit")]
    public async Task WaterCredit_Should_IgnoreSplash_And_ClampAtOnePour()
    {
        ITestPlayer joined = await World.JoinPlayer("WaterCap");
        IPlayer player = joined.Player;
        (BlockEntityFarmland farmland, _, _) = PlaceFarmlandFromSoil(player, offsetX: 14);

        float moisture = farmland.MoistureLevel;
        Assert.Equal(0f, WateringXp.CreditPour(player, farmland, moisture, emitEffort: true));
        Assert.False(ProsequorBlockPedigreeStation.TryGetBlob(farmland, out _));
        Assert.Equal(0f, ProsequorBlockPedigreeStation.TryAddWaterCredit(farmland, 0.01f));

        Assert.Equal(0.4f, ProsequorBlockPedigreeStation.TryAddWaterCredit(farmland, 0.4f));
        Assert.Equal(0.6f, ProsequorBlockPedigreeStation.TryAddWaterCredit(farmland, 0.8f));
        Assert.Equal(1f, ProsequorBlockPedigreeStation.GetWaterCredit(farmland));
        Assert.Equal(0f, ProsequorBlockPedigreeStation.TryAddWaterCredit(farmland, 0.5f));
        Assert.Equal(1f, ProsequorBlockPedigreeStation.GetWaterCredit(farmland));
    }

    [AtlasScenario]
    [Trait("Layer", "Pedigree")]
    [Trait("Kind", "WaterCredit")]
    public async Task Grow_Should_ResetWaterCredit_Harvest_Should_ClearIt()
    {
        ITestPlayer joined = await World.JoinPlayer("WaterGrow");
        IPlayer player = joined.Player;
        (BlockEntityFarmland farmland, BlockPos farmlandPos, _) = PlaceFarmlandFromSoil(player, offsetX: 16);
        ProsequorBlockPedigreeStation.StampAbsorbMultiplier(farmland, 1.25f);
        Assert.Equal(0.5f, ProsequorBlockPedigreeStation.TryAddWaterCredit(farmland, 0.5f));

        Assert.True(ProsequorChunkPedigree.TryGet(World.Api.World, farmland.Pos, out ProsequorChunkPedigree.Box stored));
        TreeAttribute tree = new();
        stored.WriteTo(tree);
        ProsequorBlockPedigreeStation.Clear(farmland);
        ProsequorChunkPedigree.Box restoredBox = new();
        restoredBox.ReadFrom(tree);
        ProsequorChunkPedigree.Set(World.Api.World, farmland.Pos, restoredBox);
        Assert.Equal(0.5f, ProsequorBlockPedigreeStation.GetWaterCredit(farmland));
        Assert.Equal(1.25f, ProsequorBlockPedigreeStation.GetAbsorbMultiplier(farmland));

        PlantOnFarmland(farmland, farmlandPos, player);
        Assert.True(farmland.TryGrowCrop(World.Api.World.Calendar.TotalHours));
        Assert.Equal(0f, ProsequorBlockPedigreeStation.GetWaterCredit(farmland));
        Assert.Equal(1.25f, ProsequorBlockPedigreeStation.GetAbsorbMultiplier(farmland));

        Assert.Equal(0.4f, ProsequorBlockPedigreeStation.TryAddWaterCredit(farmland, 0.4f));
        farmland.OnCropBlockBroken();
        Assert.Equal(0f, ProsequorBlockPedigreeStation.GetWaterCredit(farmland));
        Assert.Equal(1.25f, ProsequorBlockPedigreeStation.GetAbsorbMultiplier(farmland));
        Assert.False(ProsequorBlockPedigreeStation.TryGetBlob(farmland, out _));
    }

    (BlockEntityFarmland farmland, BlockPos pos, Block soil) PlaceFarmlandFromSoil(
        IPlayer player,
        int offsetX = 2)
    {
        IWorldAccessor world = World.Api.World;
        BlockPos pos = player.Entity.Pos.AsBlockPos.AddCopy(offsetX, 0, 0);
        EnsureFloor(pos);

        Block soil = RequireSoilBlock();
        world.BlockAccessor.SetBlock(0, pos);
        world.BlockAccessor.SetBlock(soil.BlockId, pos);

        string fertility = soil.LastCodePart(1);
        Block? farmlandBlock = world.GetBlock(new AssetLocation("farmland-dry-" + fertility))
            ?? RequireFarmlandBlock();
        Assert.NotNull(farmlandBlock);

        world.BlockAccessor.SetBlock(farmlandBlock!.BlockId, pos);
        if (world.BlockAccessor.GetBlockEntity(pos) is not BlockEntityFarmland farmland)
        {
            Assert.Fail("[prosequor] Expected BlockEntityFarmland after placing farmland.");
            throw new InvalidOperationException();
        }

        return (farmland, pos, soil);
    }

    void PlantOnFarmland(BlockEntityFarmland farmland, BlockPos farmlandPos, IPlayer planter)
    {
        IWorldAccessor world = World.Api.World;
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

        bool planted = farmland.TryPlant(crop1, slot, planter.Entity, sel);
        Assert.True(planted, "[prosequor] TryPlant should succeed on empty farmland.");
    }

    Block RequireSoilBlock()
    {
        IWorldAccessor world = World.Api.World;
        Block? block = world.GetBlock(new AssetLocation("game:soil-low-none"))
            ?? world.GetBlock(new AssetLocation("game:soil-medium-none"))
            ?? world.GetBlock(new AssetLocation("game:dirt"));
        Assert.NotNull(block);
        return block!;
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

    BlockEntityBerryBushFarmland PlaceBushNutritionStore(IPlayer player, int offsetX)
    {
        IWorldAccessor world = World.Api.World;
        Block bush = RequireFruitingBushBlock();
        BlockPos bushPos = player.Entity.Pos.AsBlockPos.AddCopy(offsetX, 1, 0);
        BlockPos soilPos = bushPos.DownCopy();
        EnsureFloor(bushPos);
        world.BlockAccessor.SetBlock(0, bushPos);
        world.BlockAccessor.SetBlock(bush.BlockId, bushPos);
        Assert.True(AbilityBootstrap.IsBerryBushBlock(world.BlockAccessor.GetBlock(bushPos)));

        if (world.BlockAccessor.GetBlockEntity(soilPos) is not BlockEntityBerryBushFarmland store)
        {
            world.BlockAccessor.SpawnBlockEntity("BerryBushFarmland", soilPos, (ItemStack?)null);
            store = world.BlockAccessor.GetBlockEntity(soilPos) as BlockEntityBerryBushFarmland;
        }

        Assert.NotNull(store);
        return store!;
    }

    Block RequireFruitingBushBlock()
    {
        IWorldAccessor world = World.Api.World;
        foreach (string path in new[]
                 {
                     "game:fruitingbush-blueberry-empty",
                     "game:fruitingbush-blueberry-ripe",
                     "game:fruitingbush-cranberry-empty",
                     "game:berrybush-blueberry-ripe"
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

        Assert.Fail("[prosequor] No fruiting bush block found.");
        throw new InvalidOperationException();
    }

    static float TotalFarmingXp(EntityBehaviorProgress progress)
    {
        var skill = progress.State.GetOrCreateSkill("farming");
        return progress.GetSkillXp("farming") + skill.Accrued;
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
