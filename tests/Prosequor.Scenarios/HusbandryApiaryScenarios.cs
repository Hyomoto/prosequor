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

/// <summary>Calm Hives / Sticky Fingers / Apiary Master pipeline resolves.</summary>
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

        SkepHarvestXp.SettleExtract(be, skep, pos, player, honeycomb);

        Assert.True(ProsequorBlockPedigreeStation.TryGetBlob(be, out ProsequorBlob after));
        Assert.False(after.TryGetContributorWeight("placer", out _));
        Assert.True(after.TryGetContributorWeight(player.PlayerUID, out int harvester));
        Assert.Equal(1, harvester);
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
