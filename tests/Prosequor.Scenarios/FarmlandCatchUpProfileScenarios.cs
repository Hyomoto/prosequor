using System.Diagnostics;
using System.Reflection;
using Atlas.Api;
using Atlas.XUnit;
using Prosequor;
using Prosequor.Ability;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;
using Xunit;
using Xunit.Abstractions;

namespace Prosequor.Scenarios;

/// <summary>
/// Report-only A/B for farmland catch-up cost: plant a 50-plot farm, advance one
/// in-game year, then time a direct <c>Update(0)</c> pass. Mirrors the bug-report
/// path (<c>updateCropDamage</c> + climate pedigree reads) without waiting on the
/// 4.5s farmland tick listener. Durations are logged only — no timing asserts.
/// </summary>
public abstract class FarmlandCatchUpProfileSupport : AtlasScenarioBase
{
    protected const int FarmColumns = 10;
    protected const int FarmRows = 5;
    protected const int FarmCount = FarmColumns * FarmRows;
    protected const int WarmupFarms = 3;
    protected const int RandSeed = 42;

    static readonly Action<BlockEntityFarmland, float> InvokeUpdate =
        CreateOpenUpdate();

    const BindingFlags InstanceNonPublic = BindingFlags.Instance | BindingFlags.NonPublic;
    const BindingFlags StaticNonPublic = BindingFlags.Static | BindingFlags.NonPublic;

    static readonly FieldInfo MoistureField =
        typeof(BlockEntitySoilNutrition).GetField("moistureLevel", InstanceNonPublic)
        ?? throw new InvalidOperationException("moistureLevel field missing.");

    static readonly FieldInfo FastForwardRandField =
        typeof(BlockEntityFastForwardGrowth).GetField("rand", StaticNonPublic)
        ?? throw new InvalidOperationException("BlockEntityFastForwardGrowth.rand missing.");

    protected readonly ITestOutputHelper Output;

    protected FarmlandCatchUpProfileSupport(ITestOutputHelper output) => Output = output;

    /// <param name="stampClimateViaCurrentApi">
    /// Only for the live ProjectReference build. Against an older staged DLL, calling
    /// current Prosequor statics would write into a different assembly's cache and never
    /// flush; plant-time stamps from the loaded mod are enough to populate chunk moddata.
    /// </param>
    /// <param name="expectedVersion">When set, assert the loaded prosequor modinfo version.</param>
    protected async Task RunCatchUpProfile(
        string label,
        bool expectProsequor,
        bool stampClimateViaCurrentApi = false,
        string? expectedVersion = null,
        string playerName = "CatchPro")
    {
        Assert.Equal(
            expectProsequor,
            World.Api.ModLoader.IsModEnabled(ProsequorModSystem.ModId));
        if (expectedVersion != null)
        {
            Mod? mod = World.Api.ModLoader.GetMod(ProsequorModSystem.ModId);
            Assert.NotNull(mod);
            Assert.Equal(expectedVersion, mod!.Info.Version);
        }

        ResetFastForwardRand();
        // Engine names: letters/digits/_/- only, max 16 chars.
        ITestPlayer joined = await World.JoinPlayer(playerName);
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

        if (stampClimateViaCurrentApi)
        {
            // Force a persistable pedigree box so TryGetBox is a hit every damage step.
            for (int i = 0; i < farms.Length; i++)
            {
                CropClimateWindow.StampHalfDelta(farms[i], 1.5f);
                Assert.True(
                    ProsequorBlockPedigreeStation.TryGetBox(farms[i], out _),
                    $"{label}: expected stamped climate pedigree on farm {i}.");
            }
        }

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
        double beforeHours = World.Api.World.Calendar.TotalHours;
        World.Calendar.Add((float)catchUpHours);
        double afterHours = World.Api.World.Calendar.TotalHours;
        Assert.True(
            afterHours - beforeHours >= catchUpHours - 0.01,
            $"{label}: calendar did not advance by a full catch-up window.");

        // Warm the climate / pedigree path off the measured set.
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
            residualHours = Math.Max(
                residualHours,
                afterHours - farms[i].TotalHoursLastUpdate);
        }

        // Year catch-up can wilt every crop; the timed path is updateCropDamage either way.
        Assert.True(
            residualHours < 5.0,
            $"{label}: catch-up left more than one interval of lag ({residualHours:F2}h).");

        double totalMs = sw.Elapsed.TotalMilliseconds;
        double meanMs = totalMs / farms.Length;
        double firstMs = firstTicks * 1000.0 / Stopwatch.Frequency;
        ReportLine(
            $"{label} farms={farms.Length} catchUpDays={daysPerYear} " +
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

        // One cell west of the grid keeps every plot within the 4-block water radius.
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

    protected void ReportLine(string line)
    {
        string text = "[prosequor-profile] " + line;
        Output.WriteLine(text);
        Console.WriteLine(text);
    }
}

/// <summary>50-plot year catch-up with Prosequor loaded (assembly default staged-mod).</summary>
[AtlasWorld(Seed = 20261108)]
public class FarmlandCatchUpProfileScenarios : FarmlandCatchUpProfileSupport
{
    public FarmlandCatchUpProfileScenarios(ITestOutputHelper output) : base(output)
    {
    }

    [AtlasScenario(TimeoutMs = 180_000, FreshWorld = true)]
    [Trait("Layer", "Profile")]
    [Trait("Kind", "FarmlandCatchUp")]
    public Task CatchUp_WithProsequor_Should_ReportYearBatch() =>
        RunCatchUpProfile(
            "farm-catchup/prosequor",
            expectProsequor: true,
            stampClimateViaCurrentApi: true,
            playerName: "CatchPro");
}

/// <summary>Same catch-up workload with Prosequor excluded from the Atlas host.</summary>
[AtlasWorld(ExcludeAssemblyMods = true, Seed = 20261108)]
public class FarmlandCatchUpVanillaProfileScenarios : FarmlandCatchUpProfileSupport
{
    public FarmlandCatchUpVanillaProfileScenarios(ITestOutputHelper output) : base(output)
    {
    }

    [AtlasScenario(TimeoutMs = 180_000, FreshWorld = true)]
    [Trait("Layer", "Profile")]
    [Trait("Kind", "FarmlandCatchUp")]
    public Task CatchUp_WithoutProsequor_Should_ReportYearBatch() =>
        RunCatchUpProfile(
            "farm-catchup/vanilla",
            expectProsequor: false,
            playerName: "CatchVan");
}

