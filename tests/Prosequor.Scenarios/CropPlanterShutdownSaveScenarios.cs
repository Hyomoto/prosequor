using System.Reflection;
using System.Security.Cryptography;
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

namespace Prosequor.Scenarios;

/// <summary>
/// Plants a crop and disconnects without saving. The host restart is the first write,
/// matching a server stop: players are already gone, and only chunks still marked
/// dirty are written. The earlier save/load scenario writes the row while the
/// player is online, so a shutdown that skips the chunk still reloads a good row.
/// </summary>
[TestCaseOrderer(
    "Prosequor.Scenarios.MethodNameOrderer",
    "Prosequor.Scenarios")]
public class CropPlanterShutdownSaveScenarios : AtlasScenarioBase
{
    static int farmlandX;
    static int farmlandY;
    static int farmlandZ;
    static string? planterUid;
    static string? cropCode;
    static bool planted;
    static bool dirtyAfterDisconnect;
    static string? rowHashBeforeShutdown;
    static WeakReference? hostApi;

    [AtlasScenario(TimeoutMs = 180_000)]
    [Trait("Layer", "Pedigree")]
    [Trait("Kind", "CropPlanterShutdownSave")]
    public async Task A_Disconnect_Should_LeavePlanterDirtyForShutdown()
    {
        ITestPlayer joined = await World.JoinPlayer("CropShutdownSave");
        try
        {
            IPlayer player = joined.Player;
            (BlockEntityFarmland _, BlockPos farmlandPos, BlockPos cropPos, Block crop) = PlantCrop(player);
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

            ServerChunk chunk = RequireChunk(farmlandPos);
            Assert.True(chunk.DirtyForSaving,
                "The planted farmland chunk is clean, so a shutdown save keeps the previous database row.");

            IWorldAccessor world = World.Api.World;
            ServerMain server = (ServerMain)world;
            GameDatabase database = RequireDatabase(RequireChunkThread(server));
            int chunkSize = ((ICoreServerAPI)World.Api).WorldManager.ChunkSize;
            byte[]? already = TryReadChunk(
                database,
                farmlandPos.X / chunkSize,
                farmlandPos.Y / chunkSize,
                farmlandPos.Z / chunkSize);
            rowHashBeforeShutdown = already == null ? null : HashChunk(already);
            Assert.False(
                already != null && DecodedHasPlanter(world, already, farmlandPos, uid),
                "The planted chunk was already in the database before disconnect, so a shutdown skip would still reload the planter.");

            await Disconnect(joined);
            await World.Ticks(5);

            if (world.BlockAccessor.GetChunkAtBlockPos(farmlandPos) is not ServerChunk after)
            {
                Assert.Fail("Disconnect unloaded the planted chunk before shutdown could save it.");
                return;
            }

            dirtyAfterDisconnect = after.DirtyForSaving;
            Assert.True(dirtyAfterDisconnect,
                "Disconnect cleared DirtyForSaving. A shutdown save will keep the previous database row, which has no planter.");
            Assert.True(
                ProsequorBlockPedigreeStation.TryGetPlanter(world.BlockAccessor.GetBlockEntity(farmlandPos), out string? still)
                && still == uid,
                "Disconnect dropped the planter from the loaded farmland before shutdown could write it.");
            hostApi = new WeakReference(World.Api);
        }
        finally
        {
            await Disconnect(joined);
        }
    }

    [AtlasScenario(RestartWorld = true, TimeoutMs = 180_000)]
    [Trait("Layer", "Pedigree")]
    [Trait("Kind", "CropPlanterShutdownSave")]
    public async Task B_ShutdownSave_Should_RestorePlanter()
    {
        Assert.True(planted && planterUid != null,
            "The disconnect scenario did not run on this host first, so shutdown had no planted crop to save.");
        Assert.True(dirtyAfterDisconnect,
            "The planted chunk was already clean before shutdown, so this restart did not write it.");
        Assert.False(
            hostApi?.IsAlive == true && ReferenceEquals(hostApi.Target, World.Api),
            "World restart kept the same server, so this did not boot the shutdown save.");

        IWorldAccessor world = World.Api.World;
        ICoreServerAPI serverApi = (ICoreServerAPI)World.Api;
        int chunkSize = serverApi.WorldManager.ChunkSize;
        int chunkX = farmlandX / chunkSize;
        int chunkY = farmlandY / chunkSize;
        int chunkZ = farmlandZ / chunkSize;
        BlockPos farmlandPos = new(farmlandX, farmlandY, farmlandZ);
        BlockPos cropPos = farmlandPos.UpCopy();

        byte[]? saved = TryReadChunk(RequireDatabase(RequireChunkThread((ServerMain)world)), chunkX, chunkY, chunkZ);
        Assert.True(saved is { Length: > 0 },
            "Shutdown left no database row for the planted chunk.");
        string savedHash = HashChunk(saved!);
        Assert.False(savedHash == rowHashBeforeShutdown,
            "Shutdown left the previous database row in place. The planted chunk was still dirty after disconnect, but the stop did not write it.");
        Assert.True(DecodedHasPlanter(world, saved!, farmlandPos, planterUid!),
            RowMissMessage(saved!));

        serverApi.WorldManager.LoadChunkColumn(chunkX, chunkZ, keepLoaded: true);
        await WaitForFarmland(farmlandPos,
            "Shutdown save did not contain the planted farmland column.");
        Assert.Equal(cropCode, world.BlockAccessor.GetBlock(cropPos).Code?.ToString());
        Assert.True(
            ProsequorBlockPedigreeStation.TryGetPlanter(world.BlockAccessor.GetBlockEntity(farmlandPos), out string? restored)
            && restored == planterUid,
            "The shutdown row contains the planter, but the booted farmland does not.");
        Assert.True(
            ProsequorBlockPedigreeStation.TryGetCropPlanter(world, cropPos, out string? viaCrop) && viaCrop == planterUid,
            "The booted farmland has the planter, but the crop lookup does not.");
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

        serverPlayer.Disconnect("crop shutdown save");
        try
        {
            await World.Until(() => !joined.IsConnected, 80);
        }
        catch (ScenarioTimeoutException)
        {
            Assert.Fail("The test player was still connected after disconnect. A world restart will refuse to save.");
        }
    }

    ServerChunk RequireChunk(BlockPos pos)
    {
        if (World.Api.World.BlockAccessor.GetChunkAtBlockPos(pos) is not ServerChunk chunk)
        {
            Assert.Fail("The planted farmland is not in a loaded server chunk.");
            throw new InvalidOperationException();
        }

        return chunk;
    }

    static string RowMissMessage(byte[] saved)
    {
        bool liveAttr = saved.AsSpan().IndexOf(Encoding.UTF8.GetBytes(ProsequorStackPedigree.LiveAttr)) >= 0;
        if (liveAttr)
        {
            return "Shutdown replaced the chunk row. The bytes contain prosequorLive, but the farmland block entity in that row has no planter.";
        }

        return "Shutdown replaced the chunk row. The new row does not contain prosequorLive.";
    }

    static string HashChunk(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));

    static bool DecodedHasPlanter(IWorldAccessor world, byte[] bytes, BlockPos farmlandPos, string uid)
    {
        ServerChunk decoded = DecodeChunk((ServerMain)world, bytes);
        return FindFarmland(decoded, farmlandPos) is BlockEntity farmland
            && ProsequorBlockPedigreeStation.TryGetPlanter(farmland, out string? planter)
            && planter == uid;
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

    static byte[]? TryReadChunk(GameDatabase database, int chunkX, int chunkY, int chunkZ)
    {
        try
        {
            byte[] bytes = database.GetChunk(chunkX, chunkY, chunkZ);
            return bytes is { Length: > 0 } ? bytes : null;
        }
        catch (Exception)
        {
            return null;
        }
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
