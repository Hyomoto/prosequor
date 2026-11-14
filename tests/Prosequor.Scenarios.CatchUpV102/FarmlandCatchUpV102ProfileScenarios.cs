using System.Diagnostics;
using System.Reflection;
using Atlas.Api;
using Atlas.XUnit;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;
using Xunit;
using Xunit.Abstractions;

namespace Prosequor.Scenarios.CatchUpV102;

/// <summary>
/// Report-only catch-up profile against the live 1.0.2 package
/// (<c>dist/prosequor-v1.0.2.zip</c>). Plant stamps chunk pedigree via that build's
/// own Harmony hooks; catch-up then FromBytes the whole blob on every damage step.
/// Must live in this project — Atlas binds one prosequor assembly per test host.
/// </summary>
[AtlasWorld(Seed = 20261108)]
public class FarmlandCatchUpV102ProfileScenarios : AtlasScenarioBase
{
    const int FarmColumns = 10;
    const int FarmRows = 5;
    const int FarmCount = FarmColumns * FarmRows;
    const int WarmupFarms = 3;
    const int RandSeed = 42;
    const string ModId = "prosequor";

    const BindingFlags InstanceNonPublic = BindingFlags.Instance | BindingFlags.NonPublic;
    const BindingFlags StaticNonPublic = BindingFlags.Static | BindingFlags.NonPublic;

    static readonly Action<BlockEntityFarmland, float> InvokeUpdate = CreateOpenUpdate();

    static readonly FieldInfo MoistureField =
        typeof(BlockEntitySoilNutrition).GetField("moistureLevel", InstanceNonPublic)
        ?? throw new InvalidOperationException("moistureLevel field missing.");

    static readonly FieldInfo FastForwardRandField =
        typeof(BlockEntityFastForwardGrowth).GetField("rand", StaticNonPublic)
        ?? throw new InvalidOperationException("BlockEntityFastForwardGrowth.rand missing.");

    readonly ITestOutputHelper output;

    public FarmlandCatchUpV102ProfileScenarios(ITestOutputHelper output) => this.output = output;

    [AtlasScenario(TimeoutMs = 600_000, FreshWorld = true)]
    [Trait("Layer", "Profile")]
    [Trait("Kind", "FarmlandCatchUp")]
    public async Task CatchUp_WithProsequorV102_Should_ReportYearBatch()
    {
        Assert.True(World.Api.ModLoader.IsModEnabled(ModId));
        Mod? mod = World.Api.ModLoader.GetMod(ModId);
        Assert.NotNull(mod);
        Assert.Equal("1.0.2", mod!.Info.Version);

        ResetFastForwardRand();
        ITestPlayer joined = await World.JoinPlayer("CatchV102");
        IPlayer player = joined.Player;

        BlockPos origin = player.Entity.Pos.AsBlockPos.AddCopy(3, 0, 0);
        PlaceWaterChannel(origin);
        BlockEntityFarmland[] warmup = PlantGrid(
            player,
            origin.AddCopy(0, 0, FarmRows + 2),
            columns: WarmupFarms,
            rows: 1);
        BlockEntityFarmland[] farms = PlantGrid(player, origin, FarmColumns, FarmRows);
        Assert.Equal(FarmCount, farms.Length);

        for (int i = 0; i < farms.Length; i++)
        {
            MoistureField.SetValue(farms[i], 1f);
        }

        for (int i = 0; i < warmup.Length; i++)
        {
            MoistureField.SetValue(warmup[i], 1f);
        }

        double hoursPerDay = World.Api.World.Calendar.HoursPerDay;
        int daysPerYear = World.Api.World.Calendar.DaysPerYear;
        double catchUpHours = daysPerYear * hoursPerDay;
        World.Calendar.Add((float)catchUpHours);
        double afterHours = World.Api.World.Calendar.TotalHours;

        for (int i = 0; i < warmup.Length; i++)
        {
            InvokeUpdate(warmup[i], 0f);
        }

        int stagesBefore = CountCropStages(farms);
        ResetFastForwardRand();
        long firstTicks = 0;
        Stopwatch sw = Stopwatch.StartNew();
        for (int i = 0; i < farms.Length; i++)
        {
            long started = Stopwatch.GetTimestamp();
            InvokeUpdate(farms[i], 0f);
            long elapsed = Stopwatch.GetTimestamp() - started;
            if (i == 0)
            {
                firstTicks = elapsed;
            }
        }

        sw.Stop();

        int stagesAfter = CountCropStages(farms);
        int stillPresent = CountStandingCrops(farms);
        double residualHours = 0;
        for (int i = 0; i < farms.Length; i++)
        {
            residualHours = Math.Max(residualHours, afterHours - farms[i].TotalHoursLastUpdate);
        }

        Assert.True(
            residualHours < 5.0,
            $"catch-up left more than one interval of lag ({residualHours:F2}h).");

        double totalMs = sw.Elapsed.TotalMilliseconds;
        double meanMs = totalMs / farms.Length;
        double firstMs = firstTicks * 1000.0 / Stopwatch.Frequency;
        ReportLine(
            $"farm-catchup/prosequor-1.0.2 farms={farms.Length} catchUpDays={daysPerYear} " +
            $"first={firstMs:F2}ms mean={meanMs:F2}ms total={totalMs:F2}ms " +
            $"stagesBefore={stagesBefore} stagesAfter={stagesAfter} " +
            $"cropsLeft={stillPresent} residualH={residualHours:F2}");
    }

    BlockEntityFarmland[] PlantGrid(IPlayer player, BlockPos origin, int columns, int rows)
    {
        List<BlockEntityFarmland> farms = new(columns * rows);
        Block farmlandBlock = RequireFarmlandBlock();
        Block crop1 = RequireCropStage1();
        Item seedItem = RequireSeedItem();
        IWorldAccessor world = World.Api.World;

        for (int z = 0; z < rows; z++)
        {
            for (int x = 0; x < columns; x++)
            {
                BlockPos farmlandPos = origin.AddCopy(x, 0, z);
                EnsureFloor(farmlandPos);
                world.BlockAccessor.SetBlock(0, farmlandPos);
                world.BlockAccessor.SetBlock(farmlandBlock.BlockId, farmlandPos);
                if (world.BlockAccessor.GetBlockEntity(farmlandPos) is not BlockEntityFarmland farmland)
                {
                    Assert.Fail($"{farmlandPos}: expected BlockEntityFarmland after place.");
                    throw new InvalidOperationException();
                }

                BlockPos cropPos = farmlandPos.UpCopy();
                world.BlockAccessor.SetBlock(0, cropPos);
                ItemStack seed = new(seedItem, 1);
                DummySlot slot = new(seed);
                BlockSelection sel = new()
                {
                    Position = farmlandPos,
                    Face = BlockFacing.UP,
                    HitPosition = new Vec3d(0.5, 1, 0.5)
                };
                Assert.True(
                    farmland.TryPlant(crop1, slot, player.Entity, sel),
                    $"{farmlandPos}: TryPlant failed.");
                farms.Add(farmland);
            }
        }

        return farms.ToArray();
    }

    void PlaceWaterChannel(BlockPos farmOrigin)
    {
        IWorldAccessor world = World.Api.World;
        Block? water = world.GetBlock(new AssetLocation("game:water-still-7"))
            ?? world.GetBlock(new AssetLocation("game:water-still-1"))
            ?? world.SearchBlocks(new AssetLocation("game:water-*")).FirstOrDefault();
        Assert.NotNull(water);

        for (int z = 0; z < FarmRows; z++)
        {
            BlockPos waterPos = farmOrigin.AddCopy(-1, 0, z);
            EnsureFloor(waterPos);
            world.BlockAccessor.SetBlock(water!.BlockId, waterPos);
        }
    }

    static int CountCropStages(BlockEntityFarmland[] farms)
    {
        int total = 0;
        for (int i = 0; i < farms.Length; i++)
        {
            Block? crop = farms[i].GetCrop();
            if (crop != null)
            {
                total += farms[i].GetCropStage(crop);
            }
        }

        return total;
    }

    static int CountStandingCrops(BlockEntityFarmland[] farms)
    {
        int count = 0;
        for (int i = 0; i < farms.Length; i++)
        {
            if (farms[i].GetCrop() != null)
            {
                count++;
            }
        }

        return count;
    }

    static void ResetFastForwardRand() =>
        FastForwardRandField.SetValue(null, new Random(RandSeed));

    static Action<BlockEntityFarmland, float> CreateOpenUpdate()
    {
        MethodInfo? method = typeof(BlockEntityFastForwardGrowth).GetMethod(
            "Update",
            InstanceNonPublic,
            binder: null,
            types: [typeof(float)],
            modifiers: null);
        Assert.NotNull(method);
        return (Action<BlockEntityFarmland, float>)Delegate.CreateDelegate(
            typeof(Action<BlockEntityFarmland, float>),
            method!);
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
            if (block?.CropProps != null)
            {
                return block;
            }
        }

        Assert.Fail("No stage-1 crop block found.");
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

        Assert.Fail("No plantable seed item found.");
        throw new InvalidOperationException();
    }

    void EnsureFloor(BlockPos above)
    {
        IWorldAccessor world = World.Api.World;
        BlockPos floor = above.DownCopy();
        if (world.BlockAccessor.GetBlock(floor).Id == 0)
        {
            Block? dirt = world.GetBlock(new AssetLocation("game:soil-low-none"))
                ?? world.GetBlock(new AssetLocation("game:dirt"));
            Assert.NotNull(dirt);
            world.BlockAccessor.SetBlock(dirt!.BlockId, floor);
        }
    }

    void ReportLine(string line)
    {
        string text = "[prosequor-profile] " + line;
        output.WriteLine(text);
        Console.WriteLine(text);
    }
}
