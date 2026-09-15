using Atlas.Api;
using Atlas.XUnit;
using Prosequor;
using Prosequor.Ability;
using Prosequor.Ability.Hooks;
using Prosequor.Player;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;
using Xunit;

namespace Prosequor.Scenarios;

/// <summary>
/// Calm Hives / Sticky Fingers / Apiary Master pipeline resolves, plus live
/// skep break / sneak-RMB paths (crash smoke for populated harvestable skeps).
/// </summary>
public class HusbandryApiaryScenarios : AtlasScenarioBase
{
    const string Skill = HusbandryFriendliness.SkillId;

    [AtlasScenario]
    [Trait("Layer", "Action")]
    [Trait("Kind", "HusbandryApiary")]
    public async Task CalmHives_Should_ScaleSpawnBeesChance()
    {
        ITestPlayer joined = await World.JoinPlayer("HSCalmHive");
        IPlayer player = joined.Player;
        IPlayerProgress progress = RequireProgress(player);

        float vanilla = SkepBeeSpawnStation.DefaultVanillaChance;
        Assert.Equal(vanilla, ResolveSpawnWithDummySkep(player), precision: 4);

        progress.AddUnlockPoints(10);
        progress.SetSkillLevel(Skill, 40);
        Assert.True(progress.GrantUnlock(Skill, "calm-hives"));
        Assert.Equal(vanilla * 0.90f, ResolveSpawnWithDummySkep(player), precision: 3);

        Assert.True(progress.GrantUnlock(Skill, "calm-hives"));
        Assert.Equal(vanilla * 0.80f, ResolveSpawnWithDummySkep(player), precision: 3);

        Assert.True(progress.GrantUnlock(Skill, "calm-hives"));
        Assert.Equal(vanilla * 0.67f, ResolveSpawnWithDummySkep(player), precision: 3);
    }

    [AtlasScenario]
    [Trait("Layer", "Action")]
    [Trait("Kind", "HusbandryApiary")]
    public async Task ApiaryMaster_Should_AllowHarvest_And_SetBreakChance()
    {
        ITestPlayer joined = await World.JoinPlayer("HSApiary");
        IPlayer player = joined.Player;
        IPlayerProgress progress = RequireProgress(player);

        Assert.False(SkepHarvestStation.ResolveAllowRightClickHarvest(player, skep: null));
        Assert.Equal(0f, SkepHarvestStation.ResolveRightClickBreakChance(player, skep: null), precision: 4);

        progress.AddUnlockPoints(20);
        progress.SetPlayerLevel(30);
        progress.SetSkillLevel(Skill, 100);
        Assert.True(progress.GrantUnlock(Skill, "calm-hives"));
        Assert.True(progress.GrantUnlock(Skill, "gentle-spirit"));
        Assert.True(progress.GrantUnlock(Skill, "feedhand"));
        Assert.True(progress.GrantUnlock(Skill, "rancher"));
        Assert.True(progress.GrantUnlock(Skill, "sticky-fingers"));
        Assert.True(progress.GrantUnlock(Skill, "apiary-master"));

        Assert.True(ResolveAllowWithPipeline(player));
        Assert.Equal(0.25f, ResolveBreakWithPipeline(player), precision: 4);

        Assert.True(progress.GrantUnlock(Skill, "apiary-master"));
        Assert.Equal(0.10f, ResolveBreakWithPipeline(player), precision: 4);

        Assert.True(progress.GrantUnlock(Skill, "apiary-master"));
        Assert.Equal(0f, ResolveBreakWithPipeline(player), precision: 4);
    }

    [AtlasScenario]
    [Trait("Layer", "Action")]
    [Trait("Kind", "HusbandryApiary")]
    public async Task SkepPlace_Should_AddPlacerAsContributor()
    {
        ITestPlayer joined = await World.JoinPlayer("HSSkepPlace");
        IPlayer player = joined.Player;

        Block? skep = ResolveEmptySkepBlock() ?? ResolveSkepBlock();
        Assert.NotNull(skep);
        Assert.True(SkepHarvestXp.IsSkepBlock(skep));

        BlockPos pos = player.Entity.Pos.AsBlockPos.AddCopy(2, 0, 0);
        EnsureFloor(pos);
        World.Api.World.BlockAccessor.SetBlock(0, pos);
        ItemStack placed = new ItemStack(skep, 1);
        World.Api.World.BlockAccessor.SetBlock(skep!.BlockId, pos, placed);
        skep.OnBlockPlaced(World.Api.World, pos, placed);
        BlockEntity? be = World.Api.World.BlockAccessor.GetBlockEntity(pos);
        be?.OnBlockPlaced(placed);
        Assert.NotNull(be);
        if (skep is BlockSkep empty && empty.IsEmpty())
        {
            Assert.IsType<BlockEntityProsequorPedigree>(be);
        }

        SkepHarvestXp.OnSkepPlaced(be, player.PlayerUID);

        Assert.True(ProsequorBlockPedigreeStation.TryGetBlob(be, out ProsequorBlob blob));
        Assert.True(blob.TryGetContributorWeight(player.PlayerUID, out int weight));
        Assert.Equal(1, weight);
    }

    [AtlasScenario]
    [Trait("Layer", "Action")]
    [Trait("Kind", "HusbandryApiary")]
    public async Task SkepPropagate_Should_CarryDestPedigreeOntoNewBe()
    {
        ITestPlayer joined = await World.JoinPlayer("HSSkepProp");
        IPlayer player = joined.Player;

        Block? empty = ResolveEmptySkepBlock();
        Block? populated = ResolvePopulatedSkepBlock();
        Assert.NotNull(empty);
        Assert.NotNull(populated);

        BlockPos emptyPos = player.Entity.Pos.AsBlockPos.AddCopy(4, 0, 0);
        EnsureFloor(emptyPos);
        World.Api.World.BlockAccessor.SetBlock(0, emptyPos);
        World.Api.World.BlockAccessor.SetBlock(empty!.BlockId, emptyPos, new ItemStack(empty, 1));
        BlockEntity? emptyBe = World.Api.World.BlockAccessor.GetBlockEntity(emptyPos);
        Assert.NotNull(emptyBe);
        ProsequorBlockPedigreeStation.AddContributor(emptyBe, "empty-placer", 1);
        Assert.True(ProsequorBlockPedigreeStation.TryGetBlob(emptyBe, out ProsequorBlob before));

        // Simulate SetBlock empty → populated + ApplyBlob carry (TryPop postfix).
        World.Api.World.BlockAccessor.SetBlock(populated!.BlockId, emptyPos);
        BlockEntity? newBe = World.Api.World.BlockAccessor.GetBlockEntity(emptyPos);
        Assert.NotNull(newBe);
        Assert.IsType<BlockEntityBeehive>(newBe);
        ProsequorBlockPedigreeStation.ApplyBlob(newBe, before);

        Assert.True(ProsequorBlockPedigreeStation.TryGetBlob(newBe, out ProsequorBlob after));
        Assert.True(after.TryGetContributorWeight("empty-placer", out int w));
        Assert.Equal(1, w);
    }

    [AtlasScenario]
    [Trait("Layer", "Action")]
    [Trait("Kind", "HusbandryApiary")]
    public async Task SkepExtract_Should_WipeThenReaddHarvester()
    {
        ITestPlayer joined = await World.JoinPlayer("HSSkepWipe");
        IPlayer player = joined.Player;

        Block? skep = ResolveSkepBlock();
        Assert.NotNull(skep);

        BlockPos pos = player.Entity.Pos.AsBlockPos.AddCopy(3, 0, 0);
        EnsureFloor(pos);
        World.Api.World.BlockAccessor.SetBlock(0, pos);
        World.Api.World.BlockAccessor.SetBlock(skep!.BlockId, pos, new ItemStack(skep, 1));
        BlockEntity? be = World.Api.World.BlockAccessor.GetBlockEntity(pos);
        Assert.NotNull(be);

        ProsequorBlockPedigreeStation.AddContributor(be, "placer", 1);
        ProsequorBlockPedigreeStation.AddContributor(be, player.PlayerUID, 1);

        Item? comb = World.Api.World.GetItem(new AssetLocation("game:honeycomb"));
        Assert.NotNull(comb);
        ItemStack[] honeycomb = [new ItemStack(comb, 3)];

        var units = SkepHarvestXp.ToHoneycombUnits(honeycomb);
        Assert.Single(units);
        Assert.Equal(3, units[0].Count);

        float before = ScenarioXp.TotalSkill(RequireBehavior(player), Skill);
        SkepHarvestXp.SettleExtract(be, skep, pos, player, honeycomb);
        float gained = ScenarioXp.TotalSkill(RequireBehavior(player), Skill) - before;
        Assert.True(gained > 0f, $"Expected skep-harvest XP from SettleExtract, got {gained}.");

        Assert.True(ProsequorBlockPedigreeStation.TryGetBlob(be, out ProsequorBlob after));
        Assert.False(after.TryGetContributorWeight("placer", out _));
        Assert.True(after.TryGetContributorWeight(player.PlayerUID, out int harvester));
        Assert.Equal(1, harvester);
    }

    /// <summary>
    /// Live <see cref="BlockSkep.OnBlockBroken"/> through Harmony (bee spawn +
    /// skep-harvest XP). A silent client crash on populated break should fail here.
    /// </summary>
    [AtlasScenario(FreshWorld = true)]
    [Trait("Layer", "Action")]
    [Trait("Kind", "HusbandryApiary")]
    public async Task PopulatedSkep_OnBlockBroken_Should_NotCrash_WhenHarvestable()
    {
        ITestPlayer joined = await World.JoinPlayer("HSSkepBreak");
        IPlayer player = joined.Player;
        EntityBehaviorProgress progress = RequireBehavior(player);

        BlockSkep skep = RequirePopulatedSkep();
        BlockPos pos = PlaceSkep(player, skep, offset: 2);
        BlockEntityBeehive hive = RequireHarvestableHive(pos);
        ProsequorBlockPedigreeStation.AddContributor(hive, player.PlayerUID, 1);
        Assert.NotNull(player.WorldData);
        player.WorldData.CurrentGameMode = EnumGameMode.Survival;

        ItemStack[]? preview = skep.GetDrops(World.Api.World, pos, player);
        Assert.True(
            preview != null && preview.Any(SkepHarvestXp.IsHoneycomb),
            $"Expected honeycomb drops from a harvestable skep before break. drops={FormatStacks(preview)}");

        float before = ScenarioXp.TotalSkill(progress, Skill);
        skep.OnBlockBroken(World.Api.World, pos, player);

        Block remaining = World.Api.World.BlockAccessor.GetBlock(pos);
        Assert.True(
            remaining.Id == 0 || remaining is not BlockSkep,
            $"Expected the skep to be gone after OnBlockBroken, got {remaining.Code}.");

        float gained = ScenarioXp.TotalSkill(progress, Skill) - before;
        Assert.True(
            gained > 0f,
            $"Expected skep-harvest husbandry XP from breaking a harvestable skep, got {gained}.");
    }

    /// <summary>
    /// Plain right-click (no sneak): Harmony prefix must leave vanilla pickup alone
    /// without throwing.
    /// </summary>
    [AtlasScenario(FreshWorld = true)]
    [Trait("Layer", "Action")]
    [Trait("Kind", "HusbandryApiary")]
    public async Task PopulatedSkep_RightClick_Should_NotCrash_WithoutSneak()
    {
        ITestPlayer joined = await World.JoinPlayer("HSSkepRC");
        IPlayer player = joined.Player;

        BlockSkep skep = RequirePopulatedSkep();
        BlockPos pos = PlaceSkep(player, skep, offset: 2);
        RequireHarvestableHive(pos);
        Assert.NotNull(player.WorldData);
        player.WorldData.CurrentGameMode = EnumGameMode.Survival;
        SetSneak(player, sneak: false);

        BlockSelection sel = SelectionAt(pos, skep);
        bool handled = skep.OnBlockInteractStart(World.Api.World, player, sel);

        // Vanilla picks the skep into inventory when there is room (sets air).
        // Contract: no throw, and sneak-less interact must not run Apiary extract.
        Block after = World.Api.World.BlockAccessor.GetBlock(pos);
        if (after.Id == skep.Id)
        {
            BlockEntityBeehive? hive = World.Api.World.BlockAccessor.GetBlockEntity(pos) as BlockEntityBeehive;
            Assert.NotNull(hive);
            Assert.True(hive!.Harvestable, "Plain right-click must not clear Harvestable.");
        }
        else
        {
            Assert.True(
                handled && after.Id == 0,
                $"Expected vanilla pickup (air) or an intact skep, got handled={handled} block={after.Code}.");
        }
    }

    /// <summary>
    /// Apiary Master sneak+RMB extract (break chance forced to 0 at max tier).
    /// Exercises <see cref="SkepHarvestInteractPatch"/> without the random break branch.
    /// </summary>
    [AtlasScenario(FreshWorld = true)]
    [Trait("Layer", "Action")]
    [Trait("Kind", "HusbandryApiary")]
    public async Task PopulatedSkep_SneakRightClick_Should_NotCrash_WithApiaryMaster()
    {
        ITestPlayer joined = await World.JoinPlayer("HSSkepSneak");
        IPlayer player = joined.Player;
        EntityBehaviorProgress progress = RequireBehavior(player);
        GrantApiaryMasterMax(progress);

        BlockSkep skep = RequirePopulatedSkep();
        BlockPos pos = PlaceSkep(player, skep, offset: 3);
        BlockEntityBeehive hive = RequireHarvestableHive(pos);
        ProsequorBlockPedigreeStation.AddContributor(hive, player.PlayerUID, 1);
        Assert.NotNull(player.WorldData);
        player.WorldData.CurrentGameMode = EnumGameMode.Survival;
        Assert.True(SkepHarvestStation.ResolveAllowRightClickHarvest(player, skep, pos));
        Assert.Equal(0f, SkepHarvestStation.ResolveRightClickBreakChance(player, skep, pos), precision: 4);

        ItemStack[]? preview = skep.GetDrops(World.Api.World, pos, player);
        Assert.True(
            preview != null && preview.Any(SkepHarvestXp.IsHoneycomb),
            $"Expected honeycomb drops before Apiary Master extract. drops={FormatStacks(preview)}");

        float before = ScenarioXp.TotalSkill(progress, Skill);
        SetSneak(player, sneak: true);
        BlockSelection sel = SelectionAt(pos, skep);
        bool handled = skep.OnBlockInteractStart(World.Api.World, player, sel);

        Assert.True(handled, "Expected Apiary Master sneak-harvest to handle the interact.");
        Assert.Equal(skep.Id, World.Api.World.BlockAccessor.GetBlock(pos).Id);
        BlockEntityBeehive? after = World.Api.World.BlockAccessor.GetBlockEntity(pos) as BlockEntityBeehive;
        Assert.NotNull(after);
        Assert.False(after!.Harvestable, "Extract should clear Harvestable until the next cycle.");

        float gained = ScenarioXp.TotalSkill(progress, Skill) - before;
        Assert.True(
            gained > 0f,
            $"Expected skep-harvest husbandry XP from Apiary Master extract, got {gained}. preview={FormatStacks(preview)}");
    }

    BlockSkep RequirePopulatedSkep()
    {
        Block? block = ResolvePopulatedSkepBlock();
        Assert.NotNull(block);
        Assert.True(block is BlockSkep skep && !skep.IsEmpty(), $"Expected populated BlockSkep, got {block?.Code}.");
        return (BlockSkep)block!;
    }

    BlockPos PlaceSkep(IPlayer player, BlockSkep skep, int offset)
    {
        BlockPos pos = player.Entity.Pos.AsBlockPos.AddCopy(offset, 0, 0);
        EnsureFloor(pos);
        IWorldAccessor world = World.Api.World;
        world.BlockAccessor.SetBlock(0, pos);
        world.BlockAccessor.SetBlock(skep.BlockId, pos, new ItemStack(skep, 1));
        skep.OnBlockPlaced(world, pos, new ItemStack(skep, 1));
        Assert.Equal(skep.Id, world.BlockAccessor.GetBlock(pos).Id);
        return pos;
    }

    BlockEntityBeehive RequireHarvestableHive(BlockPos pos)
    {
        BlockEntity? raw = World.Api.World.BlockAccessor.GetBlockEntity(pos);
        Assert.NotNull(raw);
        Assert.IsType<BlockEntityBeehive>(raw);
        var hive = (BlockEntityBeehive)raw!;
        hive.Harvestable = true;
        hive.MarkDirty(true);
        Assert.True(hive.Harvestable);
        return hive;
    }

    static BlockSelection SelectionAt(BlockPos pos, Block block) =>
        new()
        {
            Position = pos,
            Face = BlockFacing.UP,
            HitPosition = new Vec3d(0.5, 0.5, 0.5),
            Block = block
        };

    static void SetSneak(IPlayer player, bool sneak)
    {
        Assert.NotNull(player.Entity?.Controls);
        player.Entity.Controls.Sneak = sneak;
        if (player.WorldData?.EntityControls != null)
        {
            player.WorldData.EntityControls.Sneak = sneak;
            player.WorldData.EntityControls.ShiftKey = sneak;
        }
    }

    static void GrantApiaryMasterMax(IPlayerProgress progress)
    {
        progress.AddUnlockPoints(20);
        progress.SetPlayerLevel(30);
        progress.SetSkillLevel(Skill, 100);
        Assert.True(progress.GrantUnlock(Skill, "calm-hives"));
        Assert.True(progress.GrantUnlock(Skill, "gentle-spirit"));
        Assert.True(progress.GrantUnlock(Skill, "feedhand"));
        Assert.True(progress.GrantUnlock(Skill, "rancher"));
        Assert.True(progress.GrantUnlock(Skill, "sticky-fingers"));
        Assert.True(progress.GrantUnlock(Skill, "apiary-master"));
        Assert.True(progress.GrantUnlock(Skill, "apiary-master"));
        Assert.True(progress.GrantUnlock(Skill, "apiary-master"));
        // Drop below the skill cap so later skep-harvest deeds still move TotalSkill.
        progress.SetSkillLevel(Skill, 5);
    }

    static string FormatStacks(ItemStack[]? stacks)
    {
        if (stacks == null || stacks.Length == 0)
        {
            return "(none)";
        }

        return string.Join(", ", stacks.Select(s => $"{s?.Collectible?.Code}x{s?.StackSize}"));
    }

    static EntityBehaviorProgress RequireBehavior(IPlayer player)
    {
        EntityBehaviorProgress? progress = player.Entity?.GetBehavior<EntityBehaviorProgress>();
        Assert.NotNull(progress);
        return progress!;
    }

    Block? ResolveSkepBlock() =>
        ResolvePopulatedSkepBlock() ?? ResolveEmptySkepBlock();

    Block? ResolveEmptySkepBlock()
    {
        IWorldAccessor world = World.Api.World;
        string[] codes =
        [
            "game:skep-reed-empty-east",
            "game:skep-reed-empty-north",
            "game:skep-papyrus-empty-east"
        ];
        for (int i = 0; i < codes.Length; i++)
        {
            Block? block = world.GetBlock(new AssetLocation(codes[i]));
            if (block is BlockSkep skep && skep.IsEmpty())
            {
                return block;
            }
        }

        return world.SearchBlocks(new AssetLocation("game:skep-*-empty-*"))
            .FirstOrDefault(b => b is BlockSkep s && s.IsEmpty());
    }

    Block? ResolvePopulatedSkepBlock()
    {
        IWorldAccessor world = World.Api.World;
        string[] codes =
        [
            "game:skep-reed-populated-east",
            "game:skep-reed-populated-north",
            "game:skep-papyrus-populated-east"
        ];
        for (int i = 0; i < codes.Length; i++)
        {
            Block? block = world.GetBlock(new AssetLocation(codes[i]));
            if (block is BlockSkep skep && !skep.IsEmpty())
            {
                return block;
            }
        }

        return world.SearchBlocks(new AssetLocation("game:skep-*-populated-*"))
            .FirstOrDefault(b => b is BlockSkep s && !s.IsEmpty());
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

    static float ResolveSpawnWithDummySkep(IPlayer player)
    {
        ProsequorModSystem? mod = ProsequorModSystem.For(player.Entity.Api);
        Assert.NotNull(mod?.Pipeline);
        IPlayerProgress? progress = ProsequorModSystem.GetProgress(player);
        Assert.NotNull(progress);

        float vanilla = SkepBeeSpawnStation.DefaultVanillaChance;
        AbilityAction fact = EventFactBuilder.ForPlayer(
            player,
            AbilityBootstrap.VerbSpawnBeesChance,
            target: "game:skep-reed-populated-east");

        SkepBeeSpawnContext context = new()
        {
            Player = player,
            Progress = progress,
            Fact = fact,
            BaseValue = vanilla
        };

        return mod!.Pipeline!.Run(
            HookIds.BlockInteraction,
            VerbIds.SpawnBeesChance,
            HookIds.Default,
            context,
            vanilla);
    }

    static bool ResolveAllowWithPipeline(IPlayer player)
    {
        ProsequorModSystem? mod = ProsequorModSystem.For(player.Entity.Api);
        Assert.NotNull(mod?.Pipeline);
        IPlayerProgress? progress = ProsequorModSystem.GetProgress(player);
        Assert.NotNull(progress);

        AbilityAction fact = EventFactBuilder.ForPlayer(
            player,
            AbilityBootstrap.VerbHarvestSkep,
            target: "game:skep-reed-populated-east");

        SkepHarvestContext context = new()
        {
            Player = player,
            Progress = progress,
            Fact = fact,
            BaseValue = 0f
        };

        int allowed = mod!.Pipeline!.Run(
            HookIds.BlockInteraction,
            VerbIds.HarvestSkep,
            HookIds.AllowRightClickHarvest,
            context,
            0);
        return allowed >= 1;
    }

    static float ResolveBreakWithPipeline(IPlayer player)
    {
        ProsequorModSystem? mod = ProsequorModSystem.For(player.Entity.Api);
        Assert.NotNull(mod?.Pipeline);
        IPlayerProgress? progress = ProsequorModSystem.GetProgress(player);
        Assert.NotNull(progress);

        AbilityAction fact = EventFactBuilder.ForPlayer(
            player,
            AbilityBootstrap.VerbHarvestSkep,
            target: "game:skep-reed-populated-east");

        SkepHarvestContext context = new()
        {
            Player = player,
            Progress = progress,
            Fact = fact,
            BaseValue = 0f
        };

        return mod!.Pipeline!.Run(
            HookIds.BlockInteraction,
            VerbIds.HarvestSkep,
            HookIds.RightClickHarvestBreakChance,
            context,
            0f);
    }

    static IPlayerProgress RequireProgress(IPlayer player)
    {
        IPlayerProgress? progress = ProsequorModSystem.GetProgress(player);
        Assert.NotNull(progress);
        return progress!;
    }
}
