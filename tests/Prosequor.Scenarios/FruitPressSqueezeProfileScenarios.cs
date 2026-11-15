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
/// Report-only A/B for fruit-press squeeze ticks. Loads a 50-press batch the same way
/// the juice XP scenario does (apple mash, empty bucket, screw turning), primes each
/// press once, then times <c>onTick100msServer</c> — the server listener registered
/// every 25ms. The transfer pass rewinds the liquid clock so each tick moves juice.
/// The hold pass is the next tick with no calendar advance, so the screw is still
/// turning and no juice moves. Durations are logged only — no timing asserts.
/// </summary>
public abstract class FruitPressSqueezeProfileSupport : AtlasScenarioBase
{
    protected const int PressColumns = 10;
    protected const int PressRows = 5;
    protected const int PressCount = PressColumns * PressRows;
    protected const int WarmupPresses = 3;
    const double TransferWindowHours = 0.05;

    /// <summary>Listener period. The method reads the calendar, not this value.</summary>
    const float TickDt = 0.025f;

    static readonly Action<BlockEntityFruitPress, float> InvokeTick = CreateOpenTick();

    const BindingFlags InstanceNonPublic = BindingFlags.Instance | BindingFlags.NonPublic;

    static readonly FieldInfo LastTransferField =
        typeof(BlockEntityFruitPress).GetField("lastLiquidTransferTotalHours", InstanceNonPublic)
        ?? throw new InvalidOperationException("lastLiquidTransferTotalHours field missing.");

    protected readonly ITestOutputHelper Output;

    protected FruitPressSqueezeProfileSupport(ITestOutputHelper output) => Output = output;

    protected async Task RunSqueezeProfile(string label, bool expectProsequor, string playerName)
    {
        Assert.Equal(
            expectProsequor,
            World.Api.ModLoader.IsModEnabled(ProsequorModSystem.ModId));

        ITestPlayer joined = await World.JoinPlayer(playerName);
        IPlayer player = joined.Player;

        BlockPos origin = player.Entity.Pos.AsBlockPos.AddCopy(3, 0, 0);
        BlockEntityFruitPress[] warmup = PlaceRow(
            player,
            origin.AddCopy(0, 0, PressRows + 2),
            WarmupPresses);
        BlockEntityFruitPress[] presses = PlaceGrid(player, origin, PressColumns, PressRows);
        Assert.Equal(PressCount, presses.Length);

        if (expectProsequor)
        {
            for (int i = 0; i < presses.Length; i++)
            {
                Assert.True(
                    ProsequorBlockPedigreeStation.TryGetSoleContributor(presses[i], out _),
                    $"{label}: press {i} has no sole contributor, so the squeeze tick would miss the juice path.");
            }
        }

        // Warm the squeeze method, then prime the measured presses so the timed
        // ticks are later screw frames (the one-shot squeeze sound already played).
        Rewind(warmup, TransferWindowHours);
        for (int i = 0; i < warmup.Length; i++)
        {
            InvokeTick(warmup[i], TickDt);
        }

        Rewind(presses, TransferWindowHours);
        for (int i = 0; i < presses.Length; i++)
        {
            InvokeTick(presses[i], TickDt);
        }

        for (int i = 0; i < presses.Length; i++)
        {
            Assert.True(presses[i].CompressAnimActive, $"{label}: press {i} screw stopped before the timed ticks.");
            Assert.True(JuiceUnits(presses[i]) >= 1, $"{label}: press {i} prime tick moved no juice.");
            Assert.True(BucketHasRoom(presses[i]), $"{label}: press {i} bucket filled on the prime tick.");
            Assert.True(LitresLeft(presses[i]) > 1.0, $"{label}: press {i} mash ran dry on the prime tick.");
        }

        Rewind(presses, TransferWindowHours);
        int transferUnits = TimePass(label, "transfer", presses);
        Assert.True(
            transferUnits >= presses.Length,
            $"{label}: transfer pass added {transferUnits} juice units across {presses.Length} presses.");

        int holdUnits = TimePass(label, "hold", presses);
        Assert.Equal(0, holdUnits);

        double litresLeftMin = LitresLeft(presses[0]);
        for (int i = 1; i < presses.Length; i++)
        {
            litresLeftMin = Math.Min(litresLeftMin, LitresLeft(presses[i]));
        }

        Assert.True(litresLeftMin > 0.5, $"{label}: a press ran dry during the timed ticks ({litresLeftMin:F2} L left).");
    }

    int TimePass(string label, string pass, BlockEntityFruitPress[] presses)
    {
        int juiceBefore = CountJuice(presses);
        long firstTicks = 0;
        long lastTicks = 0;
        long maxTicks = 0;
        Stopwatch sw = Stopwatch.StartNew();
        for (int i = 0; i < presses.Length; i++)
        {
            long started = Stopwatch.GetTimestamp();
            InvokeTick(presses[i], TickDt);
            long elapsed = Stopwatch.GetTimestamp() - started;
            if (i == 0)
            {
                firstTicks = elapsed;
            }

            lastTicks = elapsed;
            if (elapsed > maxTicks)
            {
                maxTicks = elapsed;
            }
        }

        sw.Stop();
        int juiceAfter = CountJuice(presses);
        double totalMs = sw.Elapsed.TotalMilliseconds;
        double meanUs = totalMs * 1000.0 / presses.Length;
        double firstUs = firstTicks * 1_000_000.0 / Stopwatch.Frequency;
        double lastUs = lastTicks * 1_000_000.0 / Stopwatch.Frequency;
        double maxUs = maxTicks * 1_000_000.0 / Stopwatch.Frequency;
        ReportLine(
            $"{label} pass={pass} n={presses.Length} first={firstUs:F1}µs last={lastUs:F1}µs " +
            $"max={maxUs:F1}µs mean={meanUs:F1}µs total={totalMs:F2}ms juiceUnits={juiceAfter - juiceBefore}");
        return juiceAfter - juiceBefore;
    }

    BlockEntityFruitPress[] PlaceGrid(IPlayer player, BlockPos origin, int columns, int rows)
    {
        List<BlockEntityFruitPress> presses = new(columns * rows);
        for (int z = 0; z < rows; z++)
        {
            for (int x = 0; x < columns; x++)
            {
                presses.Add(PlaceLoadedPress(player, origin.AddCopy(x, 0, z)));
            }
        }

        return presses.ToArray();
    }

    BlockEntityFruitPress[] PlaceRow(IPlayer player, BlockPos origin, int count)
    {
        BlockEntityFruitPress[] presses = new BlockEntityFruitPress[count];
        for (int i = 0; i < count; i++)
        {
            presses[i] = PlaceLoadedPress(player, origin.AddCopy(i, 0, 0));
        }

        return presses;
    }

    BlockEntityFruitPress PlaceLoadedPress(IPlayer player, BlockPos pos)
    {
        IWorldAccessor world = World.Api.World;
        EnsureFloor(pos);
        world.BlockAccessor.SetBlock(0, pos);
        Block pressBlock = RequireBlock("game:fruitpress-ns", "fruitpress-ns");
        world.BlockAccessor.SetBlock(pressBlock.BlockId, pos);
        if (world.BlockAccessor.GetBlockEntity(pos) is not BlockEntityFruitPress press)
        {
            Assert.Fail($"{pos}: expected BlockEntityFruitPress after place.");
            throw new InvalidOperationException();
        }

        Item apple = RequireItem("game:fruit-redapple", "fruit-redapple");
        ItemStack mash = new(apple, 16);
        mash.Attributes.SetDouble("juiceableLitresLeft", 8);
        mash.Attributes.SetDouble("squeezeRel", 0.99);
        press.MashSlot.Itemstack = mash;
        press.MashSlot.MarkDirty();

        Block bucket = RequireBlock("game:woodbucket", "woodbucket");
        press.BucketSlot.Itemstack = new ItemStack(bucket, 1);
        press.BucketSlot.MarkDirty();

        if (World.Api.ModLoader.IsModEnabled(ProsequorModSystem.ModId))
        {
            ProsequorBlockPedigreeStation.StampSoleContributor(press, player.PlayerUID);
        }

        StartScrew(press);
        Assert.True(press.CompressAnimActive, $"{pos}: fruit press screw did not start.");
        return press;
    }

    static void Rewind(BlockEntityFruitPress[] presses, double hoursAgo)
    {
        for (int i = 0; i < presses.Length; i++)
        {
            double now = presses[i].Api.World.Calendar.TotalHours;
            LastTransferField.SetValue(presses[i], now - hoursAgo);
        }
    }

    static int CountJuice(BlockEntityFruitPress[] presses)
    {
        int total = 0;
        for (int i = 0; i < presses.Length; i++)
        {
            total += JuiceUnits(presses[i]);
        }

        return total;
    }

    static int JuiceUnits(BlockEntityFruitPress press)
    {
        ItemStack? bucket = press.BucketSlot?.Itemstack;
        if (bucket?.Collectible is not BlockLiquidContainerBase container)
        {
            return 0;
        }

        return container.GetContent(bucket)?.StackSize ?? 0;
    }

    static bool BucketHasRoom(BlockEntityFruitPress press)
    {
        ItemStack? bucket = press.BucketSlot?.Itemstack;
        return bucket?.Collectible is BlockLiquidContainerBase container && !container.IsFull(bucket);
    }

    static double LitresLeft(BlockEntityFruitPress press) =>
        press.MashSlot?.Itemstack?.Attributes.GetDouble("juiceableLitresLeft") ?? 0;

    static void StartScrew(BlockEntityFruitPress press)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        object? animUtil = typeof(BlockEntityFruitPress).GetProperty("animUtil", flags)?.GetValue(press);
        Assert.NotNull(animUtil);
        object? meta = typeof(BlockEntityFruitPress).GetField("compressAnimMeta", flags)?.GetValue(press);
        Assert.NotNull(meta);
        MethodInfo? start = animUtil!.GetType().GetMethod("StartAnimation", flags, null, [meta!.GetType()], null)
            ?? animUtil.GetType().GetMethod("StartAnimation", flags);
        Assert.NotNull(start);
        start!.Invoke(animUtil, [meta]);
    }

    static Action<BlockEntityFruitPress, float> CreateOpenTick()
    {
        MethodInfo? method = typeof(BlockEntityFruitPress).GetMethod(
            "onTick100msServer",
            InstanceNonPublic,
            binder: null,
            types: [typeof(float)],
            modifiers: null);
        Assert.NotNull(method);
        return (Action<BlockEntityFruitPress, float>)Delegate.CreateDelegate(
            typeof(Action<BlockEntityFruitPress, float>),
            method!);
    }

    Block RequireBlock(string code, string fallback)
    {
        IWorldAccessor world = World.Api.World;
        Block? block = world.GetBlock(new AssetLocation(code))
            ?? world.GetBlock(new AssetLocation(fallback));
        Assert.NotNull(block);
        return block!;
    }

    Item RequireItem(string code, string fallback)
    {
        IWorldAccessor world = World.Api.World;
        Item? item = world.GetItem(new AssetLocation(code))
            ?? world.GetItem(new AssetLocation(fallback));
        Assert.NotNull(item);
        return item!;
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

/// <summary>50-press squeeze ticks with Prosequor loaded (assembly default staged-mod).</summary>
[AtlasWorld(Seed = 20261115)]
public class FruitPressSqueezeProfileScenarios : FruitPressSqueezeProfileSupport
{
    public FruitPressSqueezeProfileScenarios(ITestOutputHelper output) : base(output)
    {
    }

    [AtlasScenario(TimeoutMs = 180_000, FreshWorld = true)]
    [Trait("Layer", "Profile")]
    [Trait("Kind", "FruitPressSqueeze")]
    public Task Squeeze_WithProsequor_Should_ReportTick() =>
        RunSqueezeProfile("fruit-squeeze/prosequor", expectProsequor: true, playerName: "SqueezePro");
}

/// <summary>Same squeeze workload with Prosequor excluded from the Atlas host.</summary>
[AtlasWorld(ExcludeAssemblyMods = true, Seed = 20261115)]
public class FruitPressSqueezeVanillaProfileScenarios : FruitPressSqueezeProfileSupport
{
    public FruitPressSqueezeVanillaProfileScenarios(ITestOutputHelper output) : base(output)
    {
    }

    [AtlasScenario(TimeoutMs = 180_000, FreshWorld = true)]
    [Trait("Layer", "Profile")]
    [Trait("Kind", "FruitPressSqueeze")]
    public Task Squeeze_WithoutProsequor_Should_ReportTick() =>
        RunSqueezeProfile("fruit-squeeze/vanilla", expectProsequor: false, playerName: "SqueezeVan");
}
