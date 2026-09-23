using System.Reflection;
using Atlas.Api;
using Atlas.XUnit;
using Prosequor.Ability;
using Prosequor.Xp;
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
/// Stamps every former side-table host key onto chunk pedigree, disconnects without
/// saving, and lets the host restart be the first write. Asserts the packed entry
/// under <see cref="ProsequorChunkPedigree.ModDataKey"/> — not BE ToTree attrs.
/// </summary>
[TestCaseOrderer(
    "Prosequor.Scenarios.MethodNameOrderer",
    "Prosequor.Scenarios")]
public class SideTableShutdownSurfaceScenarios : AtlasScenarioBase
{
    sealed class Subject
    {
        public string Name = "";
        public string AttrKey = "";
        public int X;
        public int Y;
        public int Z;
    }

    static readonly List<Subject> subjects = new();
    static WeakReference? hostApi;

    [AtlasScenario(TimeoutMs = 180_000)]
    [Trait("Layer", "Pedigree")]
    [Trait("Kind", "SideTableShutdownSurface")]
    public async Task A_Disconnect_Should_LeaveEachSideTableDirtyForShutdown()
    {
        subjects.Clear();
        ITestPlayer joined = await World.JoinPlayer("SideTableSurf");
        try
        {
            IPlayer player = joined.Player;
            BlockPos origin = player.Entity.Pos.AsBlockPos;
            string uid = player.PlayerUID;

            StampAnvil(origin.AddCopy(2, 0, 0));
            StampClayForm(origin.AddCopy(4, 0, 0));
            StampFirepit(origin.AddCopy(6, 0, 0), uid);
            StampOven(origin.AddCopy(8, 0, 0), uid);
            StampSaplingGrowth(origin.AddCopy(10, 0, 0));
            StampClimateFarmland(origin.AddCopy(12, 0, 0));
            StampFruitTreeChance(origin.AddCopy(14, 0, 0));
            StampBoilerBatch(origin.AddCopy(16, 0, 0), uid);
            StampTrough(origin.AddCopy(18, 0, 0), uid);
            StampCollectXp(origin.AddCopy(20, 0, 0));

            IWorldAccessor world = World.Api.World;
            foreach (Subject subject in subjects)
            {
                BlockPos pos = new(subject.X, subject.Y, subject.Z, 0);
                BlockEntity? be = world.BlockAccessor.GetBlockEntity(pos);
                Assert.True(be != null, $"{subject.Name} has no block entity.");
                Assert.True(HasChunkKey(world, pos, subject.AttrKey),
                    $"{subject.Name} chunk pedigree is missing {subject.AttrKey} before disconnect.");
                Assert.True(RequireChunk(pos).DirtyForSaving,
                    $"{subject.Name} chunk is clean, so a shutdown save keeps the previous database row.");
            }

            hostApi = new WeakReference(World.Api);
            await Disconnect(joined);

            foreach (Subject subject in subjects)
            {
                BlockPos pos = new(subject.X, subject.Y, subject.Z, 0);
                Assert.True(RequireChunk(pos).DirtyForSaving,
                    $"Disconnect cleared the dirty flag on {subject.Name}.");
                Assert.True(HasChunkKey(world, pos, subject.AttrKey),
                    $"Disconnect cleared the live chunk pedigree on {subject.Name}.");
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
    [Trait("Kind", "SideTableShutdownSurface")]
    public async Task B_ShutdownSave_Should_KeepEverySideTableAttr()
    {
        Assert.NotEmpty(subjects);
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

            if (!HasChunkKeyOnChunk(chunk, pos, subject.AttrKey))
            {
                missing.Add($"{subject.Name}: chunk saved, {subject.AttrKey} omitted from pedigree");
            }
        }

        Assert.True(missing.Count == 0,
            "Shutdown save dropped pedigree host keys. " + string.Join("; ", missing) + ".");

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
            if (!HasChunkKey(world, pos, subject.AttrKey))
            {
                unrestored.Add($"{subject.Name} ({subject.AttrKey})");
            }
        }

        Assert.True(unrestored.Count == 0,
            "Shutdown row kept the key, but the loaded chunk pedigree did not: "
            + string.Join(", ", unrestored) + ".");
    }

    void StampAnvil(BlockPos pos)
    {
        BlockEntityAnvil anvil = PlaceBe<BlockEntityAnvil>(pos, "game:anvil-copper", "game:anvil-bronze", "game:anvil-iron");
        AnvilXpStation.Stamp(anvil, 7, "side-table-anvil");
        Remember("anvil", pos, AnvilXpStation.HighWaterAttr);
    }

    void StampClayForm(BlockPos pos)
    {
        BlockEntityClayForm form = PlaceBe<BlockEntityClayForm>(pos, "game:clayform");
        ClayFormXpStation.Stamp(form, 11, "side-table-clay");
        Remember("clayform", pos, ClayFormXpStation.HighWaterAttr);
    }

    void StampFirepit(BlockPos pos, string uid)
    {
        BlockEntityFirepit firepit = PlaceBe<BlockEntityFirepit>(
            pos,
            "game:firepit-cold",
            "game:firepit-extinct",
            "game:firepit-lit");
        FirepitProcessStarterStation.NoteInteractor(firepit, uid);
        Remember("firepit", pos, FirepitProcessStarterStation.LastInteractorAttr);
    }

    void StampOven(BlockPos pos, string uid)
    {
        BlockEntityOven oven = PlaceBeByEntityClass<BlockEntityOven>(pos, "oven");
        OvenCookStarterStation.NoteInteractor(oven, uid);
        Remember("oven", pos, OvenCookStarterStation.LastInteractorAttr);
    }

    void StampSaplingGrowth(BlockPos pos)
    {
        BlockEntitySapling sapling = PlaceBe<BlockEntitySapling>(pos, RequireSaplingCode());
        SaplingGrowthDuration.StampMultiplier(sapling, 0.55f);
        Remember("sapling-growth", pos, SaplingGrowthDuration.GrowthTimeMultiplierAttr);
    }

    void StampClimateFarmland(BlockPos pos)
    {
        BlockEntityFarmland farmland = PlaceBe<BlockEntityFarmland>(
            pos,
            "game:farmland-dry-low",
            "game:farmland-dry-medium",
            "game:farmland-dry-high");
        CropClimateWindow.StampHalfDelta(farmland, 1.25f);
        Remember("climate", pos, CropClimateWindow.HalfDeltaAttr);
    }

    void StampFruitTreeChance(BlockPos pos)
    {
        BlockEntityFruitTreeBranch branch = PlaceBeByEntityClass<BlockEntityFruitTreeBranch>(pos, "fruittree");
        FruitTreeCuttingSuccess.StampEstablishChance(branch, 0.42f);
        Remember("fruit-chance", pos, FruitTreeCuttingSuccess.EstablishChanceAttr);
    }

    void StampBoilerBatch(BlockPos pos, string uid)
    {
        BlockEntityBoiler boiler = PlaceBeByEntityClass<BlockEntityBoiler>(pos, "boiler");
        BoilerDistillBatch.Store(boiler, new ProsequorBlob(uid, Array.Empty<ProsequorBlob.Share>()));
        Remember("boiler-batch", pos, "distillLocked");
    }

    void StampTrough(BlockPos pos, string uid)
    {
        BlockEntityTrough trough = PlaceBeByEntityClass<BlockEntityTrough>(pos, "trough");
        TroughContributionStation.AddContribution(trough, uid, 2);
        Remember("trough", pos, ProsequorStackPedigree.LiveAttr);
    }

    void StampCollectXp(BlockPos pos)
    {
        BlockEntityFirepit be = PlaceBe<BlockEntityFirepit>(
            pos,
            "game:firepit-cold",
            "game:firepit-extinct");
        CollectXpBlockStampStation.Set(be);
        Remember("collect-xp", pos, CollectXpStamp.AttrKey);
    }

    T PlaceBe<T>(BlockPos pos, params string[] codes) where T : BlockEntity
    {
        Block block = RequireFirstBlock(codes);
        return PlaceBlockAs<T>(pos, block);
    }

    T PlaceBeByEntityClass<T>(BlockPos pos, string pathHint) where T : BlockEntity
    {
        IWorldAccessor world = World.Api.World;
        string want = typeof(T).Name;
        foreach (Block block in world.Blocks)
        {
            if (block == null || block.Id == 0 || string.IsNullOrEmpty(block.EntityClass))
            {
                continue;
            }

            string? path = block.Code?.Path;
            if (path == null || !path.Contains(pathHint, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            EnsureFloor(pos);
            world.BlockAccessor.SetBlock(0, pos);
            world.BlockAccessor.SetBlock(block.BlockId, pos);
            if (world.BlockAccessor.GetBlockEntity(pos) is T be)
            {
                return be;
            }
        }

        Assert.Fail($"[prosequor] No block with EntityClass for {want} (hint '{pathHint}').");
        throw new InvalidOperationException();
    }

    T PlaceBlockAs<T>(BlockPos pos, Block block) where T : BlockEntity
    {
        IWorldAccessor world = World.Api.World;
        EnsureFloor(pos);
        world.BlockAccessor.SetBlock(0, pos);
        world.BlockAccessor.SetBlock(block.BlockId, pos);
        if (world.BlockAccessor.GetBlockEntity(pos) is not T be)
        {
            Assert.Fail($"Expected {typeof(T).Name} for {block.Code}, got {world.BlockAccessor.GetBlockEntity(pos)?.GetType().Name ?? "null"}.");
            throw new InvalidOperationException();
        }

        return be;
    }

    string RequireSaplingCode()
    {
        foreach (Block block in World.Api.World.Blocks)
        {
            if (block is BlockSapling && block.Id != 0 && block.Code != null)
            {
                return block.Code.ToString();
            }
        }

        Assert.Fail("[prosequor] No sapling block found.");
        throw new InvalidOperationException();
    }

    Block RequireFirstBlock(params string[] codes)
    {
        IWorldAccessor world = World.Api.World;
        foreach (string code in codes)
        {
            Block? block = world.GetBlock(new AssetLocation(code));
            if (block != null && block.Id != 0)
            {
                return block;
            }
        }

        Assert.Fail("[prosequor] Missing blocks: " + string.Join(", ", codes));
        throw new InvalidOperationException();
    }

    static void Remember(string name, BlockPos pos, string attrKey)
    {
        subjects.Add(new Subject
        {
            Name = name,
            AttrKey = attrKey,
            X = pos.X,
            Y = pos.Y,
            Z = pos.Z
        });
    }

    static bool HasChunkKey(IWorldAccessor world, BlockPos pos, string attrKey)
    {
        if (!ProsequorChunkPedigree.TryGet(world, pos, out ProsequorChunkPedigree.Box box))
        {
            return false;
        }

        return BoxHasKey(box, attrKey);
    }

    static bool HasChunkKeyOnChunk(IWorldChunk chunk, BlockPos pos, string attrKey) =>
        ProsequorChunkPedigree.TryGetFromChunk(chunk, pos, out ProsequorChunkPedigree.Box box)
        && BoxHasKey(box, attrKey);

    static bool BoxHasKey(ProsequorChunkPedigree.Box box, string attrKey)
    {
        TreeAttribute tree = new();
        box.WriteTo(tree);
        if (string.Equals(attrKey, "distillLocked", StringComparison.Ordinal))
        {
            return box.DistillLocked;
        }

        if (string.Equals(attrKey, ProsequorStackPedigree.LiveAttr, StringComparison.Ordinal))
        {
            return box.Blob.HasPersistable || tree.HasAttribute(ProsequorStackPedigree.LiveAttr);
        }

        return tree.HasAttribute(attrKey);
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
