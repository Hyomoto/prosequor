using System.Reflection;
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
/// Stamps pedigree on several block entities and disconnects without saving.
/// The host restart is the first write. Each subject uses the block-entity
/// pedigree table: farmland via TryPlant, bush / sapling / fruit-tree via
/// StampPlanter, barrel via the placed-stack copy. A missing planter on one
/// subject and not another says which store the shutdown save drops.
/// </summary>
[TestCaseOrderer(
    "Prosequor.Scenarios.MethodNameOrderer",
    "Prosequor.Scenarios")]
public class PedigreeShutdownSurfaceScenarios : AtlasScenarioBase
{
    sealed class Subject
    {
        public string Name = "";
        public int X;
        public int Y;
        public int Z;
        public string Uid = "";
    }

    static readonly List<Subject> subjects = new();
    static WeakReference? hostApi;

    [AtlasScenario(TimeoutMs = 180_000)]
    [Trait("Layer", "Pedigree")]
    [Trait("Kind", "PedigreeShutdownSurface")]
    public async Task A_Disconnect_Should_LeaveEachPedigreeDirtyForShutdown()
    {
        subjects.Clear();
        ITestPlayer joined = await World.JoinPlayer("PedigreeSurface");
        try
        {
            IPlayer player = joined.Player;
            BlockPos origin = player.Entity.Pos.AsBlockPos;
            PlantCrop(player, origin.AddCopy(2, 0, 0));
            PlaceStamped(player, "bush", origin.AddCopy(4, 0, 0), RequireBerryBush());
            PlaceStamped(player, "sapling", origin.AddCopy(6, 0, 0), RequireSapling());
            PlaceStamped(player, "fruit-tree", origin.AddCopy(8, 0, 0), RequireFruitTreeBranch());
            PlaceBarrel(player, origin.AddCopy(10, 0, 0));

            IWorldAccessor world = World.Api.World;
            ServerMain server = (ServerMain)world;
            GameDatabase database = RequireDatabase(RequireChunkThread(server));
            int chunkSize = ((ICoreServerAPI)World.Api).WorldManager.ChunkSize;
            foreach (Subject subject in subjects)
            {
                BlockPos pos = new(subject.X, subject.Y, subject.Z, 0);
                Assert.True(
                    ProsequorBlockPedigreeStation.TryGetPlanter(world.BlockAccessor.GetBlockEntity(pos)!, out string? live)
                    && live == subject.Uid,
                    $"{subject.Name} lost its live planter before disconnect.");
                Assert.True(RequireChunk(pos).DirtyForSaving,
                    $"{subject.Name} chunk is clean, so a shutdown save keeps the previous database row.");

                byte[]? already = TryReadChunk(
                    database,
                    pos.X / chunkSize,
                    pos.Y / chunkSize,
                    pos.Z / chunkSize);
                if (already != null && DecodedPlanter(world, already, pos) == subject.Uid)
                {
                    Assert.Fail(
                        $"{subject.Name} was already in the database before disconnect, so a shutdown skip would still reload it.");
                }
            }

            hostApi = new WeakReference(World.Api);
            await Disconnect(joined);

            foreach (Subject subject in subjects)
            {
                BlockPos pos = new(subject.X, subject.Y, subject.Z, 0);
                Assert.True(RequireChunk(pos).DirtyForSaving,
                    $"Disconnect cleared the dirty flag on {subject.Name}.");
                Assert.True(
                    ProsequorBlockPedigreeStation.TryGetPlanter(world.BlockAccessor.GetBlockEntity(pos)!, out string? live)
                    && live == subject.Uid,
                    $"Disconnect cleared the live planter on {subject.Name}.");
            }
        }
        finally
        {
            if (joined.IsConnected)
            {
                await Disconnect(joined);
            }
        }
    }

    [AtlasScenario(TimeoutMs = 180_000, RestartWorld = true)]
    [Trait("Layer", "Pedigree")]
    [Trait("Kind", "PedigreeShutdownSurface")]
    public async Task B_ShutdownSave_Should_KeepPlanterOnEveryBlockEntity()
    {
        Assert.NotEmpty(subjects);
        Assert.NotNull(hostApi);
        Assert.False(
            hostApi?.IsAlive == true && ReferenceEquals(hostApi.Target, World.Api),
            "World restart kept the same server, so this did not boot the shutdown save.");

        IWorldAccessor world = World.Api.World;
        ServerMain server = (ServerMain)world;
        GameDatabase database = RequireDatabase(RequireChunkThread(server));
        int chunkSize = ((ICoreServerAPI)World.Api).WorldManager.ChunkSize;
        Dictionary<(int X, int Y, int Z), ServerChunk?> decoded = new();
        List<string> missing = new();

        foreach (Subject subject in subjects)
        {
            BlockPos pos = new(subject.X, subject.Y, subject.Z, 0);
            (int X, int Y, int Z) key = (pos.X / chunkSize, pos.Y / chunkSize, pos.Z / chunkSize);
            if (!decoded.TryGetValue(key, out ServerChunk? chunk))
            {
                byte[]? row = TryReadChunk(database, key.X, key.Y, key.Z);
                chunk = row == null ? null : DecodeChunk(world, row);
                decoded[key] = chunk;
            }

            if (chunk == null)
            {
                missing.Add($"{subject.Name}: no chunk row");
                continue;
            }

            if (!ProsequorChunkPedigree.TryGetFromChunk(chunk, pos, out ProsequorChunkPedigree.Box box)
                || box.Blob.IsAnonymous
                || box.Blob.MakerUid != subject.Uid)
            {
                missing.Add($"{subject.Name}: chunk saved, planter omitted");
            }
        }

        Assert.True(missing.Count == 0, "Shutdown save dropped pedigree. " + string.Join("; ", missing) + ".");

        HashSet<(int X, int Z)> columns = new();
        foreach (Subject subject in subjects)
        {
            columns.Add((subject.X / chunkSize, subject.Z / chunkSize));
        }

        foreach ((int X, int Z) column in columns)
        {
            ((ICoreServerAPI)World.Api).WorldManager.LoadChunkColumn(column.X, column.Z, keepLoaded: true);
        }

        await World.Until(() => subjects.TrueForAll(subject =>
            world.BlockAccessor.GetChunkAtBlockPos(new BlockPos(subject.X, subject.Y, subject.Z, 0)) != null));

        List<string> unrestored = new();
        foreach (Subject subject in subjects)
        {
            BlockPos pos = new(subject.X, subject.Y, subject.Z, 0);
            BlockEntity? live = world.BlockAccessor.GetBlockEntity(pos);
            if (live == null
                || !ProsequorBlockPedigreeStation.TryGetPlanter(live, out string? planter)
                || planter != subject.Uid)
            {
                unrestored.Add(subject.Name);
            }
        }

        Assert.True(unrestored.Count == 0,
            "Shutdown row kept the planter, but the loaded block entity did not: " + string.Join(", ", unrestored) + ".");
    }

    void PlantCrop(IPlayer player, BlockPos farmlandPos)
    {
        IWorldAccessor world = World.Api.World;
        EnsureFloor(farmlandPos);
        Block farmlandBlock = RequireFarmland();
        world.BlockAccessor.SetBlock(0, farmlandPos);
        world.BlockAccessor.SetBlock(farmlandBlock.BlockId, farmlandPos);
        if (world.BlockAccessor.GetBlockEntity(farmlandPos) is not BlockEntityFarmland farmland)
        {
            Assert.Fail("Expected BlockEntityFarmland after placing farmland.");
            return;
        }

        Block crop = RequireCropStage1();
        world.BlockAccessor.SetBlock(0, farmlandPos.UpCopy());
        ItemStack seed = new(RequireSeedItem(), 1);
        BlockSelection sel = new()
        {
            Position = farmlandPos,
            Face = BlockFacing.UP,
            HitPosition = new Vec3d(0.5, 1, 0.5)
        };
        Assert.True(farmland.TryPlant(crop, new DummySlot(seed), player.Entity, sel),
            "TryPlant failed for the surface crop.");
        Remember("farmland", farmlandPos, player.PlayerUID);
    }

    Block RequireFarmland()
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
        foreach (string path in new[] { "game:crop-carrot-1", "game:crop-spelt-1", "game:crop-turnip-1" })
        {
            Block? block = World.Api.World.GetBlock(new AssetLocation(path));
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
        foreach (string path in new[] { "game:seeds-carrot", "game:seeds-spelt", "game:seeds-turnip" })
        {
            Item? item = World.Api.World.GetItem(new AssetLocation(path));
            if (item != null)
            {
                return item;
            }
        }

        Assert.Fail("[prosequor] No plantable seed item found.");
        throw new InvalidOperationException();
    }

    void PlaceStamped(IPlayer player, string name, BlockPos pos, Block block)
    {
        IWorldAccessor world = World.Api.World;
        EnsureFloor(pos);
        world.BlockAccessor.SetBlock(0, pos);
        world.BlockAccessor.SetBlock(block.BlockId, pos);
        BlockEntity? be = world.BlockAccessor.GetBlockEntity(pos);
        Assert.True(be != null, $"{name} did not create a block entity.");
        ProsequorBlockPedigreeStation.StampPlanter(be, player.PlayerUID);
        Remember(name, pos, player.PlayerUID);
    }

    void PlaceBarrel(IPlayer player, BlockPos pos)
    {
        IWorldAccessor world = World.Api.World;
        Block barrel = RequireContainer();
        ItemStack stack = new(barrel, 1);
        new ProsequorBlob(player.PlayerUID, Array.Empty<ProsequorBlob.Share>())
            .WriteTo(stack.Attributes.GetOrAddTreeAttribute(ProsequorStackPedigree.LiveAttr));
        EnsureFloor(pos);
        world.BlockAccessor.SetBlock(0, pos);
        world.BlockAccessor.SetBlock(barrel.BlockId, pos, stack);
        BlockEntity? be = world.BlockAccessor.GetBlockEntity(pos);
        Assert.True(be != null, $"{barrel.Code} did not create a block entity.");
        ProsequorBlockPedigreeStation.CaptureFromPlacedStack(be, stack);
        Remember(barrel.Code.Path, pos, player.PlayerUID);
    }

    Block RequireContainer()
    {
        IWorldAccessor world = World.Api.World;
        foreach (string path in new[]
                 {
                     "game:barrel-burned",
                     "game:barrel",
                     "game:chest-east",
                     "game:chest-north"
                 })
        {
            Block? block = world.GetBlock(new AssetLocation(path));
            if (block != null && block.Id != 0 && !string.IsNullOrEmpty(block.EntityClass))
            {
                return block;
            }
        }

        foreach (Block block in world.Blocks)
        {
            string? path = block?.Code?.Path;
            if (block == null || block.Id == 0 || string.IsNullOrEmpty(block.EntityClass) || path == null)
            {
                continue;
            }

            if (path.StartsWith("barrel", StringComparison.Ordinal) || path.StartsWith("chest-", StringComparison.Ordinal))
            {
                return block;
            }
        }

        Assert.Fail("[prosequor] No barrel or chest block with a block entity found.");
        throw new InvalidOperationException();
    }

    static void Remember(string name, BlockPos pos, string uid)
    {
        subjects.Add(new Subject { Name = name, X = pos.X, Y = pos.Y, Z = pos.Z, Uid = uid });
    }

    Block RequireBerryBush()
    {
        IWorldAccessor world = World.Api.World;
        foreach (string path in new[]
                 {
                     "game:fruitingbush-blueberry-ripe",
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

        Assert.Fail("[prosequor] No berry bush block found.");
        throw new InvalidOperationException();
    }

    Block RequireSapling()
    {
        foreach (Block block in World.Api.World.Blocks)
        {
            if (block is BlockSapling && block.Id != 0)
            {
                return block;
            }
        }

        Assert.Fail("[prosequor] No sapling block found.");
        throw new InvalidOperationException();
    }

    Block RequireFruitTreeBranch()
    {
        IWorldAccessor world = World.Api.World;
        foreach (string path in new[]
                 {
                     "game:fruittree-branch-apple-stem-segment1",
                     "game:fruittree-branch-redapple-stem-segment1"
                 })
        {
            Block? block = world.GetBlock(new AssetLocation(path));
            if (block != null && AbilityBootstrap.IsFruitTreeBlock(block))
            {
                return block;
            }
        }

        foreach (Block block in world.Blocks)
        {
            if (block is BlockFruitTreeBranch && block.Id != 0)
            {
                return block;
            }
        }

        Assert.Fail("[prosequor] No fruit-tree branch block found.");
        throw new InvalidOperationException();
    }

    Block RequireBlock(string code, string label)
    {
        Block? block = World.Api.World.GetBlock(new AssetLocation(code));
        Assert.True(block != null && block.Id != 0, $"[prosequor] Missing {label} block {code}.");
        return block!;
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

    string? DecodedPlanter(IWorldAccessor world, byte[] bytes, BlockPos pos)
    {
        return ProsequorChunkPedigree.TryGetFromChunk(DecodeChunk(world, bytes), pos, out ProsequorChunkPedigree.Box box)
            && !box.Blob.IsAnonymous
            ? box.Blob.MakerUid
            : null;
    }

    ServerChunk RequireChunk(BlockPos pos)
    {
        IWorldChunk? chunk = World.Api.World.BlockAccessor.GetChunkAtBlockPos(pos);
        if (chunk is not ServerChunk serverChunk)
        {
            Assert.Fail($"No server chunk at {pos}.");
            throw new InvalidOperationException();
        }

        return serverChunk;
    }

    static ServerChunk DecodeChunk(IWorldAccessor world, byte[] bytes)
    {
        ServerMain server = (ServerMain)world;
        object pool = typeof(ServerMain).GetField("serverChunkDataPool", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(server)!;
        ServerChunk? chunk = ServerChunk.FromBytes(bytes, (ChunkDataPool)pool, world);
        Assert.NotNull(chunk);
        return chunk!;
    }

    static ChunkServerThread RequireChunkThread(ServerMain server)
    {
        object? thread = typeof(ServerMain).GetField("chunkThread", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(server);
        Assert.True(thread is ChunkServerThread, "chunkThread was not a ChunkServerThread.");
        return (ChunkServerThread)thread!;
    }

    static GameDatabase RequireDatabase(ChunkServerThread thread)
    {
        object? database = typeof(ChunkServerThread).GetField("gameDatabase", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(thread);
        Assert.True(database is GameDatabase, "gameDatabase was not a GameDatabase.");
        return (GameDatabase)database!;
    }

    static byte[]? TryReadChunk(GameDatabase database, int chunkX, int chunkY, int chunkZ)
    {
        try
        {
            return database.GetChunk(chunkX, chunkY, chunkZ);
        }
        catch (Exception)
        {
            return null;
        }
    }

    async Task Disconnect(ITestPlayer joined)
    {
        ((IServerPlayer)joined.Player).Disconnect("scenario");
        await World.Until(() => !joined.IsConnected);
    }
}
