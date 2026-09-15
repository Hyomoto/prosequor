using System.Diagnostics;
using Atlas.Api;
using Atlas.XUnit;
using Prosequor;
using Prosequor.Ability;
using Prosequor.Ability.Hooks;
using Prosequor.Data;
using Prosequor.Player;
using Prosequor.Xp;
using Prosequor.Xp.Activity;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Server;
using Vintagestory.Common;
using Xunit;
using Xunit.Abstractions;

namespace Prosequor.Scenarios;

/// <summary>
/// Shared helpers for report-only profile scenarios (empty-vs-full timings).
/// </summary>
public abstract class ProfileScenarioSupport : AtlasScenarioBase
{
    protected const string Tailoring = "tailoring";
    protected const string SkipExclusive = "armorproficiency";
    protected const int WarmupTakes = 10;
    protected const int TimedTakes = 80;
    protected const int WarmupXp = 20;
    protected const int TimedXp = 200;
    protected const int WarmupWatch = 5;
    protected const int TimedWatch = 40;

    protected readonly ITestOutputHelper Output;

    protected ProfileScenarioSupport(ITestOutputHelper output) => Output = output;

    protected void TimeCraftCell(string label, IPlayer player, IPlayerProgress progress, CraftSetup setup)
    {
        InventoryCraftingGrid craft = RequireCraftGrid(player);
        ComposeMemo? memo = TryMemo(progress);

        PlaceCraft(craft, setup);
        Assert.False(
            craft[craft.Count - 1].Empty,
            $"{label}: expected matched output for {setup.OutputPath}.");

        for (int i = 0; i < WarmupTakes; i++)
        {
            Assert.True(TakeOnce(player, craft, setup), $"{label}: warmup take {i} failed.");
        }

        int hitsBefore = memo?.HitCount ?? 0;
        int missBefore = memo?.MissCount ?? 0;

        long firstTicks = 0;
        var sw = Stopwatch.StartNew();
        for (int i = 0; i < TimedTakes; i++)
        {
            long before = Stopwatch.GetTimestamp();
            Assert.True(TakeOnce(player, craft, setup), $"{label}: timed take {i} failed.");
            long elapsed = Stopwatch.GetTimestamp() - before;
            if (i == 0)
            {
                firstTicks = elapsed;
            }
        }

        sw.Stop();
        int hits = (memo?.HitCount ?? 0) - hitsBefore;
        int misses = (memo?.MissCount ?? 0) - missBefore;
        double totalMs = sw.Elapsed.TotalMilliseconds;
        double meanUs = totalMs * 1000.0 / TimedTakes;
        double firstUs = firstTicks * 1_000_000.0 / Stopwatch.Frequency;
        ReportLine(
            $"{label} n={TimedTakes} first={firstUs:F1}µs mean={meanUs:F1}µs total={totalMs:F2}ms memoHit={hits} memoMiss={misses}");
    }

    protected void TimeQtyCell(string label, IPlayer player, IPlayerProgress progress, Item item)
    {
        const int recipeBase = 1;
        ComposeMemo? memo = TryMemo(progress);

        for (int i = 0; i < WarmupTakes; i++)
        {
            ItemStack stack = new(item, recipeBase);
            _ = CraftMutateOutputStation.ResolveQuantity(player, stack, recipeBase);
        }

        int hitsBefore = memo?.HitCount ?? 0;
        int missBefore = memo?.MissCount ?? 0;
        long firstTicks = 0;
        var sw = Stopwatch.StartNew();
        for (int i = 0; i < TimedTakes; i++)
        {
            ItemStack stack = new(item, recipeBase);
            long before = Stopwatch.GetTimestamp();
            _ = CraftMutateOutputStation.ResolveQuantity(player, stack, recipeBase);
            long elapsed = Stopwatch.GetTimestamp() - before;
            if (i == 0)
            {
                firstTicks = elapsed;
            }
        }

        sw.Stop();
        ReportTimedCell(label, TimedTakes, sw, firstTicks, memo, hitsBefore, missBefore);
    }

    protected void TimeAttrsCell(string label, IPlayer player, IPlayerProgress progress, Item item)
    {
        ComposeMemo? memo = TryMemo(progress);

        for (int i = 0; i < WarmupTakes; i++)
        {
            ItemStack stack = new(item, 1);
            CraftMutateOutputStation.ApplyAttributes(player, stack);
        }

        int hitsBefore = memo?.HitCount ?? 0;
        int missBefore = memo?.MissCount ?? 0;
        long firstTicks = 0;
        var sw = Stopwatch.StartNew();
        for (int i = 0; i < TimedTakes; i++)
        {
            ItemStack stack = new(item, 1);
            long before = Stopwatch.GetTimestamp();
            CraftMutateOutputStation.ApplyAttributes(player, stack);
            long elapsed = Stopwatch.GetTimestamp() - before;
            if (i == 0)
            {
                firstTicks = elapsed;
            }
        }

        sw.Stop();
        ReportTimedCell(label, TimedTakes, sw, firstTicks, memo, hitsBefore, missBefore);
    }

    protected void ReportTimedCell(
        string label,
        int n,
        Stopwatch sw,
        long firstTicks,
        ComposeMemo? memo,
        int hitsBefore,
        int missBefore)
    {
        int hits = (memo?.HitCount ?? 0) - hitsBefore;
        int misses = (memo?.MissCount ?? 0) - missBefore;
        double totalMs = sw.Elapsed.TotalMilliseconds;
        double meanUs = totalMs * 1000.0 / n;
        double firstUs = firstTicks * 1_000_000.0 / Stopwatch.Frequency;
        ReportLine(
            $"{label} n={n} first={firstUs:F1}µs mean={meanUs:F1}µs total={totalMs:F2}ms memoHit={hits} memoMiss={misses}");
    }

    protected void TimeWatcherCell(string label, ActivityWatchService watch, IServerPlayer player, bool emitEffort)
    {
        for (int i = 0; i < WarmupWatch; i++)
        {
            if (emitEffort)
            {
                EmitEffortLoad(player);
            }

            watch.Tick([player]);
        }

        var sw = Stopwatch.StartNew();
        for (int i = 0; i < TimedWatch; i++)
        {
            if (emitEffort)
            {
                EmitEffortLoad(player);
            }

            watch.Tick([player]);
        }

        sw.Stop();
        double totalMs = sw.Elapsed.TotalMilliseconds;
        double meanUs = totalMs * 1000.0 / TimedWatch;
        ReportLine($"{label} n={TimedWatch} mean={meanUs:F1}µs total={totalMs:F2}ms");
    }

    protected void TimeDeedXpCell(string label, IPlayerProgress progress, AbilityAction fact)
    {
        ComposeMemo? memo = TryMemo(progress);
        for (int i = 0; i < WarmupXp; i++)
        {
            progress.AddSkillXp(Tailoring, 1f, fact, XpAwardMode.Grant);
        }

        int hitsBefore = memo?.HitCount ?? 0;
        int missBefore = memo?.MissCount ?? 0;
        long firstTicks = 0;
        var sw = Stopwatch.StartNew();
        for (int i = 0; i < TimedXp; i++)
        {
            long before = Stopwatch.GetTimestamp();
            progress.AddSkillXp(Tailoring, 1f, fact, XpAwardMode.Grant);
            long elapsed = Stopwatch.GetTimestamp() - before;
            if (i == 0)
            {
                firstTicks = elapsed;
            }
        }

        sw.Stop();
        int hits = (memo?.HitCount ?? 0) - hitsBefore;
        int misses = (memo?.MissCount ?? 0) - missBefore;
        double totalMs = sw.Elapsed.TotalMilliseconds;
        double meanUs = totalMs * 1000.0 / TimedXp;
        double firstUs = firstTicks * 1_000_000.0 / Stopwatch.Frequency;
        ReportLine(
            $"{label} n={TimedXp} first={firstUs:F1}µs mean={meanUs:F1}µs total={totalMs:F2}ms memoHit={hits} memoMiss={misses}");
    }

    protected static AbilityAction MakeCraftFact(string actorUid, string target) =>
        new()
        {
            Verb = Deed.Activity,
            ActorUid = actorUid,
            Held = CallerIdentities.Grid,
            Target = target,
            Tokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                DeedToken.Crafted.ToTag()
            }
        };

    protected void KeepSkillBelowCap(IPlayerProgress progress, string skillId)
    {
        ProsequorModSystem? mod = ProsequorModSystem.For(World.Api);
        Assert.NotNull(mod);
        Assert.True(mod!.Registry.TryGet(skillId, out SkillDef skill));
        int below = Math.Max(1, skill.MaxLevel - 1);
        progress.SetSkillLevel(skillId, below);
        Assert.True(
            progress.GetSkillLevel(skillId) < skill.MaxLevel,
            $"Expected {skillId} below cap for XP path profiling.");
    }

    protected static void EmitEffortLoad(IPlayer player)
    {
        Effort.Emit(
            player,
            new[] { EffortTokenTags.Fishing },
            channel: EffortTokenTags.Fishing);
        Effort.Emit(
            player,
            new[] { EffortTokenTags.Mounted, EffortTokenTags.Riding },
            channel: EffortTokenTags.Riding);
    }

    protected static void PutFishingPoleInHand(IPlayer player)
    {
        Item? pole = player.Entity.World.GetItem(new AssetLocation("game:fishingpole-simple-wood"))
            ?? player.Entity.World.GetItem(new AssetLocation("game:fishingpole-simple-bamboo"))
            ?? player.Entity.World.SearchItems(new AssetLocation("game:fishingpole-*")).FirstOrDefault();
        Assert.NotNull(pole);
        ItemSlot? hotbar = player.InventoryManager?.ActiveHotbarSlot;
        Assert.NotNull(hotbar);
        hotbar!.Itemstack = new ItemStack(pole, 1);
        hotbar.MarkDirty();
    }

    protected void GrantFullTailoring(IPlayerProgress progress)
    {
        ProsequorModSystem? mod = ProsequorModSystem.For(World.Api);
        Assert.NotNull(mod);
        Assert.True(mod!.Registry.TryGet(Tailoring, out SkillDef skill), "tailoring skill missing");
        Assert.NotNull(skill.Tree);

        progress.SetPlayerLevel(50);
        progress.SetSkillLevel(Tailoring, skill.MaxLevel);
        progress.AddUnlockPoints(200);

        bool progressed;
        do
        {
            progressed = false;
            foreach (SkillTreeNodeDef node in skill.Tree!.Nodes)
            {
                if (string.Equals(node.Id, SkipExclusive, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                while (progress.GetUnlockTier(Tailoring, node.Id) < node.MaxTier)
                {
                    if (!progress.GrantUnlock(Tailoring, node.Id))
                    {
                        break;
                    }

                    progressed = true;
                }
            }
        }
        while (progressed);

        int ownedNodes = 0;
        int ownedTiers = 0;
        foreach (SkillTreeNodeDef node in skill.Tree.Nodes)
        {
            if (string.Equals(node.Id, SkipExclusive, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            int tier = progress.GetUnlockTier(Tailoring, node.Id);
            if (tier > 0)
            {
                ownedNodes++;
                ownedTiers += tier;
            }

            Assert.True(
                tier == node.MaxTier,
                $"Expected full grant of {node.Id} ({tier}/{node.MaxTier}).");
        }

        ReportLine(
            $"grant/full-tailoring nodes={ownedNodes} tiers={ownedTiers} level={progress.GetSkillLevel(Tailoring)}");
    }

    protected CraftSetup RequireThreadSetup(IPlayer player)
    {
        Item fibers = RequireItem(player, "game:flaxfibers");
        return new CraftSetup(
            OutputPath: "flaxtwine",
            OutputCode: "game:flaxtwine",
            Place: craft =>
            {
                // twine.json: FF,FF width 2 height 2 → slots 0,1,3,4
                ClearGrid(craft);
                craft[0].Itemstack = new ItemStack(fibers, 999);
                craft[1].Itemstack = new ItemStack(fibers, 999);
                craft[3].Itemstack = new ItemStack(fibers, 999);
                craft[4].Itemstack = new ItemStack(fibers, 999);
                NotifyGrid(craft);
            });
    }

    protected CraftSetup RequireClothingSetup(IPlayer player)
    {
        // Prefer leather bracers (LTL): synthesis clothing craft that hits apply-quality.
        Item? leather = TryItem(player, "game:leather-normal-plain");
        Item? twine = TryItem(player, "game:flaxtwine");
        if (leather != null && twine != null)
        {
            Item leatherItem = leather;
            Item twineItem = twine;
            return new CraftSetup(
                OutputPath: "clothes-arm-leather-bracers",
                OutputCode: "game:clothes-arm-leather-bracers",
                Place: craft =>
                {
                    ClearGrid(craft);
                    // LTL width 3 height 1 → slots 0,1,2; leather quantity 2 each end.
                    craft[0].Itemstack = new ItemStack(leatherItem, 999);
                    craft[1].Itemstack = new ItemStack(twineItem, 999);
                    craft[2].Itemstack = new ItemStack(leatherItem, 999);
                    NotifyGrid(craft);
                });
        }

        // Fallback: leather + cattailroot arm wrap.
        Item leatherFallback = RequireItem(
            player,
            "game:leather-normal-plain",
            "game:hide-pelt-small");
        Item cattail = RequireItem(player, "game:cattailroot");
        return new CraftSetup(
            OutputPath: "clothes-arm-cattail",
            OutputCode: "game:clothes-arm-cattail",
            Place: craft =>
            {
                ClearGrid(craft);
                // L,S width 1 height 2 → slots 0 and 3
                craft[0].Itemstack = new ItemStack(leatherFallback, 999);
                craft[3].Itemstack = new ItemStack(cattail, 999);
                NotifyGrid(craft);
            });
    }

    static void PlaceCraft(InventoryCraftingGrid craft, CraftSetup setup) => setup.Place(craft);

    static bool TakeOnce(IPlayer player, InventoryCraftingGrid craft, CraftSetup setup)
    {
        ItemSlot output = craft[craft.Count - 1];
        if (output.Empty
            || !string.Equals(
                output.Itemstack?.Collectible?.Code?.Path,
                setup.OutputPath,
                StringComparison.OrdinalIgnoreCase))
        {
            PlaceCraft(craft, setup);
            output = craft[craft.Count - 1];
            if (output.Empty)
            {
                return false;
            }
        }

        ItemSlot sink = new DummySlot();
        ItemStackMoveOperation op = new(
            player.Entity.World,
            EnumMouseButton.Left,
            (EnumModifierKey)0,
            EnumMergePriority.AutoMerge,
            requestedQuantity: output.StackSize);
        op.ActingPlayer = player;
        int moved = output.TryPutInto(sink, ref op);
        if (moved <= 0)
        {
            return false;
        }

        PlaceCraft(craft, setup);
        return true;
    }

    static void ClearGrid(InventoryCraftingGrid craft)
    {
        int ingredientSlots = craft.Count - 1;
        for (int i = 0; i < ingredientSlots; i++)
        {
            craft[i].Itemstack = null;
            craft[i].MarkDirty();
        }
    }

    static void NotifyGrid(InventoryCraftingGrid craft)
    {
        int ingredientSlots = craft.Count - 1;
        for (int i = 0; i < ingredientSlots; i++)
        {
            if (!craft[i].Empty)
            {
                craft.OnItemSlotModified(craft[i]);
                return;
            }
        }
    }

    static InventoryCraftingGrid RequireCraftGrid(IPlayer player)
    {
        IInventory? raw = player.InventoryManager.GetOwnInventory(GlobalConstants.craftingInvClassName);
        Assert.NotNull(raw);
        Assert.True(raw is InventoryCraftingGrid, $"Expected InventoryCraftingGrid, got {raw!.GetType().Name}");
        return (InventoryCraftingGrid)raw;
    }

    protected static Item RequireItem(IPlayer player, params string[] codes)
    {
        Item? item = TryItem(player, codes);
        Assert.NotNull(item);
        return item!;
    }

    static Item? TryItem(IPlayer player, params string[] codes)
    {
        foreach (string code in codes)
        {
            Item? item = player.Entity.World.GetItem(new AssetLocation(code));
            if (item != null)
            {
                return item;
            }
        }

        return null;
    }

    static ComposeMemo? TryMemo(IPlayerProgress progress) =>
        progress is IAbilityComposeCache cache ? cache.ComposeMemo : null;

    protected static IPlayerProgress RequireProgress(IPlayer player)
    {
        IPlayerProgress? progress = ProsequorModSystem.GetProgress(player);
        Assert.NotNull(progress);
        return progress!;
    }

    protected static IServerPlayer RequireServerPlayer(IPlayer player)
    {
        Assert.True(player is IServerPlayer, $"Expected IServerPlayer, got {player.GetType().Name}");
        return (IServerPlayer)player;
    }

    protected ActivityWatchService RequireWatch()
    {
        ActivityWatchService? watch = ProsequorModSystem.For(World.Api)?.ActivityWatch;
        Assert.NotNull(watch);
        return watch!;
    }

    protected void ReportLine(string line)
    {
        string text = "[prosequor-profile] " + line;
        Output.WriteLine(text);
        Console.WriteLine(text);
    }

    protected sealed record CraftSetup(string OutputPath, string OutputCode, Action<InventoryCraftingGrid> Place);
}

/// <summary>
/// Report-only empty-vs-full timings for craft takes, activity watcher, and deed XP.
/// No duration asserts — numbers print for local scale feel.
/// </summary>
public class ProfileScenarios : ProfileScenarioSupport
{
    public ProfileScenarios(ITestOutputHelper output) : base(output)
    {
    }

    [AtlasScenario]
    [Trait("Layer", "Profile")]
    [Trait("Kind", "Craft")]
    public async Task CraftTakes_Should_ReportEmptyVsFull_ThreadAndClothing()
    {
        ITestPlayer emptyJoin = await World.JoinPlayer("ProfCraftEmpty");
        ITestPlayer fullJoin = await World.JoinPlayer("ProfCraftFull");
        IPlayer empty = emptyJoin.Player;
        IPlayer full = fullJoin.Player;

        IPlayerProgress emptyProgress = RequireProgress(empty);
        IPlayerProgress fullProgress = RequireProgress(full);
        GrantFullTailoring(fullProgress);

        CraftSetup thread = RequireThreadSetup(empty);
        CraftSetup clothing = RequireClothingSetup(empty);
        ReportLine($"recipes thread={thread.OutputPath} clothing={clothing.OutputPath}");

        TimeCraftCell("thread/empty", empty, emptyProgress, thread);
        TimeCraftCell("thread/full", full, fullProgress, thread);
        TimeCraftCell("clothing/empty", empty, emptyProgress, clothing);
        TimeCraftCell("clothing/full", full, fullProgress, clothing);
    }

    [AtlasScenario]
    [Trait("Layer", "Profile")]
    [Trait("Kind", "Watcher")]
    public async Task ActivityWatch_Should_ReportEmptyVsEffortLoad()
    {
        ITestPlayer emptyJoin = await World.JoinPlayer("ProfWatchEmpty");
        ITestPlayer loadJoin = await World.JoinPlayer("ProfWatchLoad");
        IServerPlayer empty = RequireServerPlayer(emptyJoin.Player);
        IServerPlayer load = RequireServerPlayer(loadJoin.Player);

        GrantFullTailoring(RequireProgress(load));
        PutFishingPoleInHand(load);

        ActivityWatchService watch = RequireWatch();

        // Prime dt clocks (first Tick always returns dt==0).
        watch.Tick([empty]);
        watch.Tick([load]);
        await World.Ticks(8);

        TimeWatcherCell("watcher/empty", watch, empty, emitEffort: false);
        await World.Ticks(4);
        TimeWatcherCell("watcher/load", watch, load, emitEffort: true);
    }

    [AtlasScenario]
    [Trait("Layer", "Profile")]
    [Trait("Kind", "DeedXp")]
    public async Task DeedXp_Should_ReportEmptyVsFull_SkillXpModifiers()
    {
        ITestPlayer emptyJoin = await World.JoinPlayer("ProfXpEmpty");
        ITestPlayer fullJoin = await World.JoinPlayer("ProfXpFull");
        IPlayer empty = emptyJoin.Player;
        IPlayer full = fullJoin.Player;

        IPlayerProgress emptyProgress = RequireProgress(empty);
        IPlayerProgress fullProgress = RequireProgress(full);
        GrantFullTailoring(fullProgress);
        // Cap leaves AddSkillXp as a no-op before skill-xp modifiers; drop one level so Grant runs.
        KeepSkillBelowCap(fullProgress, Tailoring);

        string target = RequireClothingSetup(empty).OutputCode;
        AbilityAction emptyFact = MakeCraftFact(empty.PlayerUID, target);
        AbilityAction fullFact = MakeCraftFact(full.PlayerUID, target);

        TimeDeedXpCell("deed-xp/empty", emptyProgress, emptyFact);
        TimeDeedXpCell("deed-xp/full", fullProgress, fullFact);
    }

    [AtlasScenario]
    [Trait("Layer", "Profile")]
    [Trait("Kind", "CraftProsequor")]
    public async Task CraftProsequor_Should_ReportEmptyVsFull_QuantityAndAttributes()
    {
        ITestPlayer emptyJoin = await World.JoinPlayer("ProfIsoEmpty");
        ITestPlayer fullJoin = await World.JoinPlayer("ProfIsoFull");
        IPlayer empty = emptyJoin.Player;
        IPlayer full = fullJoin.Player;

        IPlayerProgress emptyProgress = RequireProgress(empty);
        IPlayerProgress fullProgress = RequireProgress(full);
        GrantFullTailoring(fullProgress);

        CraftSetup thread = RequireThreadSetup(empty);
        CraftSetup clothing = RequireClothingSetup(empty);
        Item threadItem = RequireItem(empty, thread.OutputCode);
        Item clothingItem = RequireItem(empty, clothing.OutputCode);
        ReportLine($"isolate recipes thread={thread.OutputPath} clothing={clothing.OutputPath}");

        TimeQtyCell("qty/thread/empty", empty, emptyProgress, threadItem);
        TimeQtyCell("qty/thread/full", full, fullProgress, threadItem);
        TimeQtyCell("qty/clothing/empty", empty, emptyProgress, clothingItem);
        TimeQtyCell("qty/clothing/full", full, fullProgress, clothingItem);

        TimeAttrsCell("attrs/thread/empty", empty, emptyProgress, threadItem);
        TimeAttrsCell("attrs/thread/full", full, fullProgress, threadItem);
        TimeAttrsCell("attrs/clothing/empty", empty, emptyProgress, clothingItem);
        TimeAttrsCell("attrs/clothing/full", full, fullProgress, clothingItem);
    }
}

/// <summary>
/// Same craft-take profile as <see cref="ProfileScenarios"/>, with HoRPerformanceOptimizer loaded
/// beside Prosequor (Atlas world mods). Report-only A/B for vanilla rematch cost.
/// </summary>
[AtlasWorld(Mods = ["staged-hor"])]
public class ProfileHorScenarios : ProfileScenarioSupport
{
    public ProfileHorScenarios(ITestOutputHelper output) : base(output)
    {
    }

    [AtlasScenario]
    [Trait("Layer", "Profile")]
    [Trait("Kind", "CraftHor")]
    public async Task CraftTakes_WithHor_Should_ReportEmptyVsFull_ThreadAndClothing()
    {
        Assert.True(
            World.Api.ModLoader.IsModEnabled("horperformanceoptimizer"),
            "HoRPerformanceOptimizer mod did not load (check staged-hor + modinfo).");
        Assert.True(
            World.Api.ModLoader.IsModSystemEnabled("HoRPerformanceOptimizer.PerformanceOptimizerModSystem"),
            "HoR PerformanceOptimizerModSystem is not enabled.");

        ITestPlayer emptyJoin = await World.JoinPlayer("ProfHorEmpty");
        ITestPlayer fullJoin = await World.JoinPlayer("ProfHorFull");
        IPlayer empty = emptyJoin.Player;
        IPlayer full = fullJoin.Player;

        IPlayerProgress emptyProgress = RequireProgress(empty);
        IPlayerProgress fullProgress = RequireProgress(full);
        GrantFullTailoring(fullProgress);

        CraftSetup thread = RequireThreadSetup(empty);
        CraftSetup clothing = RequireClothingSetup(empty);
        ReportLine($"with-hor recipes thread={thread.OutputPath} clothing={clothing.OutputPath}");

        TimeCraftCell("with-hor/thread/empty", empty, emptyProgress, thread);
        TimeCraftCell("with-hor/thread/full", full, fullProgress, thread);
        TimeCraftCell("with-hor/clothing/empty", empty, emptyProgress, clothing);
        TimeCraftCell("with-hor/clothing/full", full, fullProgress, clothing);
    }
}
