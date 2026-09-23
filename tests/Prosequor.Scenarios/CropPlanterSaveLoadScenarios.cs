using System.Reflection;
using System.Text;
using Atlas.Api;
using Atlas.XUnit;
using Prosequor.Ability;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.Common;
using Vintagestory.GameContent;
using Vintagestory.Server;
using Xunit;
using Xunit.Abstractions;
using Xunit.Sdk;

namespace Prosequor.Scenarios;

/// <summary>
/// Plants a crop, then checks each hop the planter must survive: the farmland tree,
/// a new block entity restored from those bytes, the chunk serializer, the sqlite row
/// after a real save, an unload/load of that column, and a full host restart.
/// The first failing assert is where the planter disappears.
/// </summary>
[TestCaseOrderer(
    "Prosequor.Scenarios.MethodNameOrderer",
    "Prosequor.Scenarios")]
public class CropPlanterSaveLoadScenarios : AtlasScenarioBase
{
    static int farmlandX;
    static int farmlandY;
    static int farmlandZ;
    static string? planterUid;
    static string? cropCode;
    static bool planted;
    static bool diskHadPlanter;
    static bool columnReloadHadPlanter;
    static WeakReference? hostApi;

    [AtlasScenario(TimeoutMs = 180_000)]
    [Trait("Layer", "Pedigree")]
    [Trait("Kind", "CropPlanterSaveLoad")]
    public async Task A_Save_Should_KeepPlanterThroughDiskAndChunkReload()
    {
        ITestPlayer joined = await World.JoinPlayer("CropSaveLoad");
        try
        {
            IPlayer player = joined.Player;
            (BlockEntityFarmland farmland, BlockPos farmlandPos, BlockPos cropPos, Block crop) =
                PlantCrop(player);

            string uid = player.PlayerUID;
            Assert.True(
                ProsequorBlockPedigreeStation.TryGetCropPlanter(World.Api.World, cropPos, out string? livePlanter),
                "TryPlant left the crop with no planter on the live farmland.");
            Assert.Equal(uid, livePlanter);

            farmlandX = farmlandPos.X;
            farmlandY = farmlandPos.Y;
            farmlandZ = farmlandPos.Z;
            planterUid = uid;
            cropCode = crop.Code?.ToString();
            planted = true;

            IWorldAccessor world = World.Api.World;
            Assert.True(
                ProsequorChunkPedigree.TryGet(world, farmlandPos, out ProsequorChunkPedigree.Box chunkBox)
                && !chunkBox.Blob.IsAnonymous
                && chunkBox.Blob.MakerUid == uid,
                "The chunk moddata bag does not contain the planter, so a world save cannot keep it.");

            if (world.BlockAccessor.GetChunkAtBlockPos(farmlandPos) is not ServerChunk serverChunk)
            {
                Assert.Fail("The planted farmland is not in a loaded server chunk.");
                return;
            }

            Assert.True(serverChunk.DirtyForSaving,
                "The planted farmland chunk is clean, so a world save keeps the previous database row.");

            byte[] liveChunkBytes = serverChunk.ToBytes();
            Assert.True(serverChunk.DirtyForSaving,
                "Serializing the planted chunk cleared DirtyForSaving, so the world save will skip it.");
            AssertDecodedPlanter(world, liveChunkBytes, farmlandPos, uid,
                "Live chunk bytes");

            ServerMain server = RequireServerMain(world);
            ChunkServerThread thread = RequireChunkThread(server);
            await SaveOffThread(thread);

            int chunkSize = ((ICoreServerAPI)World.Api).WorldManager.ChunkSize;
            int chunkX = farmlandPos.X / chunkSize;
            int chunkY = farmlandPos.Y / chunkSize;
            int chunkZ = farmlandPos.Z / chunkSize;
            byte[]? saved = RequireDatabase(thread).GetChunk(chunkX, chunkY, chunkZ);
            Assert.True(saved is { Length: > 0 },
                "The world save finished, but the database has no bytes for the planted chunk.");
            diskHadPlanter = DecodedHasPlanter(world, saved!, farmlandPos, uid);
            Assert.True(diskHadPlanter, DiskMissMessage(saved!, farmlandPos));

            await Disconnect(joined);
            await World.Ticks(5);
            ((ICoreServerAPI)World.Api).WorldManager.UnloadChunkColumn(chunkX, chunkZ);
            try
            {
                await World.Until(() => world.BlockAccessor.GetChunkAtBlockPos(farmlandPos) == null, 200);
            }
            catch (ScenarioTimeoutException)
            {
                Assert.Fail("UnloadChunkColumn left the planted chunk loaded, so a following load would not read the database.");
            }

            ((ICoreServerAPI)World.Api).WorldManager.LoadChunkColumn(chunkX, chunkZ, keepLoaded: true);
            await WaitForFarmland(farmlandPos,
                "The chunk column reload did not bring the farmland block entity back.");
            Assert.Equal(cropCode, world.BlockAccessor.GetBlock(cropPos).Code?.ToString());

            columnReloadHadPlanter = ProsequorBlockPedigreeStation.TryGetPlanter(
                world.BlockAccessor.GetBlockEntity(farmlandPos), out string? reloaded)
                && reloaded == uid;
            Assert.True(columnReloadHadPlanter,
                "The database row contained the planter, but loading that chunk column did not restore it.");
            Assert.True(
                ProsequorBlockPedigreeStation.TryGetCropPlanter(world, cropPos, out string? viaCrop) && viaCrop == uid,
                "The reloaded farmland has the planter, but the crop lookup did not see it.");
            hostApi = new WeakReference(World.Api);
        }
        finally
        {
            await Disconnect(joined);
        }
    }

    [AtlasScenario(RestartWorld = true, TimeoutMs = 180_000)]
    [Trait("Layer", "Pedigree")]
    [Trait("Kind", "CropPlanterSaveLoad")]
    public async Task B_WorldRestart_Should_RestorePlanter()
    {
        Assert.True(planted && planterUid != null,
            "The plant scenario did not run on this host before the world restart, so there is no planted crop to load.");
        Assert.False(
            hostApi?.IsAlive == true && ReferenceEquals(hostApi.Target, World.Api),
            "World restart kept the same server, so this did not boot the save.");

        IWorldAccessor world = World.Api.World;
        ICoreServerAPI serverApi = (ICoreServerAPI)World.Api;
        int chunkSize = serverApi.WorldManager.ChunkSize;
        int chunkX = farmlandX / chunkSize;
        int chunkZ = farmlandZ / chunkSize;
        BlockPos farmlandPos = new(farmlandX, farmlandY, farmlandZ);
        BlockPos cropPos = farmlandPos.UpCopy();

        serverApi.WorldManager.LoadChunkColumn(chunkX, chunkZ, keepLoaded: true);
        await WaitForFarmland(farmlandPos,
            "World restart did not load the planted farmland. The column is missing from the save.");
        Assert.Equal(cropCode, world.BlockAccessor.GetBlock(cropPos).Code?.ToString());

        bool farmlandHasPlanter = ProsequorBlockPedigreeStation.TryGetPlanter(
            world.BlockAccessor.GetBlockEntity(farmlandPos), out string? restored)
            && restored == planterUid;
        Assert.True(farmlandHasPlanter, RestartMissMessage());
        Assert.True(
            ProsequorBlockPedigreeStation.TryGetCropPlanter(world, cropPos, out string? viaCrop) && viaCrop == planterUid,
            "World restart restored the farmland planter, but the crop lookup did not see it.");
    }

    async Task SaveOffThread(ChunkServerThread thread)
    {
        Assert.False(thread.runOffThreadSaveNow,
            "A world save was already running, so this scenario cannot tell when its own save finishes.");
        InvokeSave(thread);
        if (!thread.runOffThreadSaveNow)
        {
            return;
        }

        try
        {
            await World.Until(() => !thread.runOffThreadSaveNow, 1200);
        }
        catch (ScenarioTimeoutException)
        {
            Assert.Fail("The off-thread world save did not finish within 1200 ticks.");
        }
    }

    async Task WaitForFarmland(BlockPos farmlandPos, string timeoutMessage)
    {
        IWorldAccessor world = World.Api.World;
        try
        {
            await World.Until(
                () => world.BlockAccessor.GetBlockEntity(farmlandPos) is BlockEntityFarmland,
                400);
        }
        catch (ScenarioTimeoutException)
        {
            Assert.Fail(timeoutMessage);
        }
    }

    async Task Disconnect(ITestPlayer joined)
    {
        if (!joined.IsConnected)
        {
            return;
        }

        if (joined.Player is not IServerPlayer serverPlayer)
        {
            Assert.Fail("The joined player is not an IServerPlayer, so the world restart cannot drop them first.");
            return;
        }

        serverPlayer.Disconnect("crop save/load handoff");
        try
        {
            await World.Until(() => !joined.IsConnected, 80);
        }
        catch (ScenarioTimeoutException)
        {
            Assert.Fail("The test player was still connected after disconnect. A world restart will refuse to save.");
        }
    }

    void AssertDecodedPlanter(IWorldAccessor world, byte[] bytes, BlockPos farmlandPos, string uid, string stage)
    {
        if (DecodedHasPlanter(world, bytes, farmlandPos, uid))
        {
            return;
        }

        Assert.Fail(stage + " " + DescribeDecoded(world, bytes, farmlandPos));
    }

    bool DecodedHasPlanter(IWorldAccessor world, byte[] bytes, BlockPos farmlandPos, string uid)
    {
        ServerChunk decoded = DecodeChunk(RequireServerMain(world), bytes);
        return ProsequorChunkPedigree.TryGetFromChunk(decoded, farmlandPos, out ProsequorChunkPedigree.Box box)
            && !box.Blob.IsAnonymous
            && box.Blob.MakerUid == uid;
    }

    string DiskMissMessage(byte[] saved, BlockPos farmlandPos)
    {
        IWorldAccessor world = World.Api.World;
        bool liveAttr = ContainsUtf8(saved, ProsequorChunkPedigree.ModDataKey)
            || ContainsUtf8(saved, ProsequorStackPedigree.LiveAttr);
        string decoded = DescribeDecoded(world, saved, farmlandPos);
        if (liveAttr)
        {
            return "The database chunk contains pedigree moddata, but the restored entry has no planter. " + decoded;
        }

        return "The database chunk does not contain pedigree moddata. The planter never reached disk. " + decoded;
    }

    string RestartMissMessage()
    {
        if (diskHadPlanter && columnReloadHadPlanter)
        {
            return "The planter was in the database and survived a chunk reload, but the world restart loaded farmland with no planter.";
        }

        if (diskHadPlanter)
        {
            return "The planter was in the database before restart, but the booted world loaded farmland with no planter.";
        }

        return "The booted world loaded farmland with no planter. The database check before restart also found none.";
    }

    static string DescribeDecoded(IWorldAccessor world, byte[] bytes, BlockPos farmlandPos)
    {
        ServerChunk decoded = DecodeChunk(RequireServerMain(world), bytes);
        if (FindFarmland(decoded, farmlandPos) is not BlockEntity farmland)
        {
            return "Decoded chunk farmland: " + DescribeFarmland(decoded) + ".";
        }

        ProsequorBlockPedigreeStation.TryGetPlanter(farmland, out string? planter);
        return $"Decoded farmland planter is '{planter ?? "none"}'.";
    }

    BlockEntity RestoreFarmland(TreeAttribute tree, Block farmlandBlock)
    {
        ICoreAPI api = World.Api;
        string? className = api.ClassRegistry.GetBlockEntityClass(typeof(BlockEntityFarmland));
        Assert.False(string.IsNullOrEmpty(className), "BlockEntityFarmland is not registered.");
        BlockEntity created = api.ClassRegistry.CreateBlockEntity(className!);
        created.CreateBehaviors(farmlandBlock, api.World);
        Assert.Null(created.Api);
        created.FromTreeAttributes(tree, api.World);
        return created;
    }

    static ServerChunk DecodeChunk(ServerMain server, byte[] bytes)
    {
        FieldInfo? field = typeof(ServerMain).GetField("serverChunkDataPool", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        object? pool = field!.GetValue(server);
        Assert.NotNull(pool);
        MethodInfo? fromBytes = typeof(ServerChunk).GetMethod(nameof(ServerChunk.FromBytes), BindingFlags.Public | BindingFlags.Static);
        Assert.NotNull(fromBytes);
        try
        {
            return (ServerChunk)fromBytes!.Invoke(null, [bytes, pool, server])!;
        }
        catch (TargetInvocationException ex)
        {
            Assert.Fail("ServerChunk.FromBytes threw: " + ex.InnerException);
            throw;
        }
    }

    static BlockEntity? FindFarmland(IWorldChunk chunk, BlockPos farmlandPos)
    {
        if (chunk.BlockEntities == null)
        {
            return null;
        }

        foreach (KeyValuePair<BlockPos, BlockEntity> entry in chunk.BlockEntities)
        {
            if (entry.Value is not BlockEntityFarmland)
            {
                continue;
            }

            BlockPos key = entry.Key;
            if (key.X == farmlandPos.X && key.Z == farmlandPos.Z
                && (key.Y == farmlandPos.Y || key.InternalY == farmlandPos.Y))
            {
                return entry.Value;
            }
        }

        return null;
    }

    static string DescribeFarmland(IWorldChunk chunk)
    {
        if (chunk.BlockEntities == null)
        {
            return "none (no block-entity map)";
        }

        List<string> found = [];
        foreach (KeyValuePair<BlockPos, BlockEntity> entry in chunk.BlockEntities)
        {
            if (entry.Value is BlockEntityFarmland)
            {
                found.Add(entry.Key.ToString() ?? "?");
            }
        }

        return found.Count == 0 ? "none" : string.Join(", ", found);
    }

    static void InvokeSave(ChunkServerThread thread)
    {
        FieldInfo? field = typeof(ChunkServerThread).GetField("loadsavegame", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        object? system = field!.GetValue(thread);
        Assert.NotNull(system);
        MethodInfo? method = system!.GetType().GetMethod("OnWorldBeingSaved", BindingFlags.Instance | BindingFlags.Public);
        Assert.NotNull(method);
        try
        {
            method!.Invoke(system, null);
        }
        catch (TargetInvocationException ex)
        {
            Assert.Fail("World save threw: " + ex.InnerException);
        }
    }

    static ServerMain RequireServerMain(IWorldAccessor world)
    {
        Assert.True(world is ServerMain, "The scenario world is not the dedicated server.");
        return (ServerMain)world;
    }

    static ChunkServerThread RequireChunkThread(ServerMain server)
    {
        FieldInfo? field = typeof(ServerMain).GetField("chunkThread", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        if (field!.GetValue(server) is not ChunkServerThread thread)
        {
            Assert.Fail("The server chunk thread was not found.");
            throw new InvalidOperationException();
        }

        return thread;
    }

    static GameDatabase RequireDatabase(ChunkServerThread thread)
    {
        FieldInfo? field = typeof(ChunkServerThread).GetField("gameDatabase", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        if (field!.GetValue(thread) is not GameDatabase database)
        {
            Assert.Fail("The game database was not open.");
            throw new InvalidOperationException();
        }

        return database;
    }

    static bool ContainsUtf8(byte[] haystack, string needle) =>
        haystack.AsSpan().IndexOf(Encoding.UTF8.GetBytes(needle)) >= 0;

    (BlockEntityFarmland farmland, BlockPos farmlandPos, BlockPos cropPos, Block crop) PlantCrop(IPlayer player)
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

        Block crop = RequireCropStage1();
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

        Assert.True(farmland.TryPlant(crop, slot, player.Entity, sel),
            "[prosequor] TryPlant should succeed on empty farmland.");
        Assert.True(AbilityBootstrap.IsCropBlock(world.BlockAccessor.GetBlock(cropPos)));
        return (farmland, farmlandPos, cropPos, crop);
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

/// <summary>
/// Runs <c>A_</c> before <c>B_</c>. The restart scenario has to see the world the plant scenario saved.
/// </summary>
public sealed class MethodNameOrderer : ITestCaseOrderer
{
    public MethodNameOrderer(IMessageSink diagnosticMessageSink)
    {
        _ = diagnosticMessageSink;
    }

    public IEnumerable<TTestCase> OrderTestCases<TTestCase>(IEnumerable<TTestCase> testCases)
        where TTestCase : ITestCase
        => testCases.OrderBy(testCase => testCase.TestMethod.Method.Name, StringComparer.Ordinal);
}
