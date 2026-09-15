using Atlas.Api;
using Atlas.XUnit;
using Prosequor.Ability;
using Prosequor.Player;
using Prosequor.Xp.Adapters;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;
using Xunit;

namespace Prosequor.Scenarios;

/// <summary>
/// Planted fruit-tree harvest pays farming XP to the harvester (quantity).
/// Structural growth batches block placements into one maker pay (amount 2 × craftCount).
/// Domesticated comes from the root cutting planter via RootOff.
/// </summary>
public class FruitTreeHarvestXpScenarios : AtlasScenarioBase
{
    const string Farming = "farming";

    [AtlasScenario]
    [Trait("Layer", "Xp")]
    [Trait("Kind", "FruitTreeHarvest")]
    public async Task InteractHarvest_Should_PayHarvester_When_RootHasPlanter()
    {
        ITestPlayer planter = await World.JoinPlayer("FruitPlant");
        ITestPlayer harvester = await World.JoinPlayer("FruitPick");
        EntityBehaviorProgress progress = RequireBehavior(harvester.Player);

        (Block branch, BlockPos pos, BlockEntityFruitTreeBranch be) = PlaceCutting(planter.Player);
        Assert.True(ProsequorBlockPedigreeStation.TryGetFruitTreePlanter(World.Api.World, pos, out string? uid));
        Assert.Equal(planter.Player.PlayerUID, uid);
        Assert.True(OwnerCredit.TryResolvePlanter(World.Api.World, branch, pos, out _));

        Item fruit = RequireFruitItem();
        float before = TotalSkillXp(progress);
        HarvestXp.NotifyInteractHarvest(
            World.Api,
            harvester.Player,
            branch,
            pos,
            [new ItemStack(fruit, 2)]);
        float gained = TotalSkillXp(progress) - before;

        Assert.Equal(0.05f, gained, precision: 3);
        Assert.Equal(0f, TotalSkillXp(RequireBehavior(planter.Player)), precision: 3);
        Assert.NotNull(be);
    }

    [AtlasScenario]
    [Trait("Layer", "Xp")]
    [Trait("Kind", "FruitTreeHarvest")]
    public async Task InteractHarvest_Should_PayNothing_WithoutPlanter()
    {
        ITestPlayer joined = await World.JoinPlayer("FruitWild");
        IPlayer player = joined.Player;
        EntityBehaviorProgress progress = RequireBehavior(player);

        (Block branch, BlockPos pos, _) = PlaceCutting(player, stampPlanter: false);
        Assert.False(OwnerCredit.TryResolvePlanter(World.Api.World, branch, pos, out _));

        Item fruit = RequireFruitItem();
        float before = TotalSkillXp(progress);
        HarvestXp.NotifyInteractHarvest(
            World.Api,
            player,
            branch,
            pos,
            [new ItemStack(fruit, 2)]);
        Assert.Equal(0f, TotalSkillXp(progress) - before, precision: 3);
    }

    [AtlasScenario]
    [Trait("Layer", "Xp")]
    [Trait("Kind", "FruitTreeGrowth")]
    public async Task StructuralBatch_Should_PayPlanterOnce_WithQuantity()
    {
        ITestPlayer joined = await World.JoinPlayer("FruitGrow");
        IPlayer player = joined.Player;
        EntityBehaviorProgress progress = RequireBehavior(player);

        (_, _, BlockEntityFruitTreeBranch be) = PlaceCutting(player);
        float before = TotalSkillXp(progress);
        GrowthXp.NoteFruitTreeBlockGrown(be);
        GrowthXp.NoteFruitTreeBlockGrown(be);
        GrowthXp.NoteFruitTreeBlockGrown(be);
        GrowthXp.FlushFruitTreeStructuralGrowth();
        Assert.Equal(6f, TotalSkillXp(progress) - before, precision: 3);
    }

    [AtlasScenario]
    [Trait("Layer", "Xp")]
    [Trait("Kind", "FruitTreeGrowth")]
    public async Task StructuralBatch_Should_PayNothing_WithoutPlanter()
    {
        ITestPlayer joined = await World.JoinPlayer("FruitGrowWild");
        IPlayer player = joined.Player;
        EntityBehaviorProgress progress = RequireBehavior(player);

        (_, _, BlockEntityFruitTreeBranch be) = PlaceCutting(player, stampPlanter: false);
        float before = TotalSkillXp(progress);
        GrowthXp.NoteFruitTreeBlockGrown(be);
        GrowthXp.NoteFruitTreeBlockGrown(be);
        GrowthXp.FlushFruitTreeStructuralGrowth();
        Assert.Equal(0f, TotalSkillXp(progress) - before, precision: 3);
    }

    (Block branch, BlockPos pos, BlockEntityFruitTreeBranch be) PlaceCutting(
        IPlayer player,
        bool stampPlanter = true)
    {
        IWorldAccessor world = World.Api.World;
        Block branch = RequireFruitTreeBranch();
        BlockPos pos = player.Entity.Pos.AsBlockPos.AddCopy(3, 0, 0);
        EnsureFloor(pos);
        world.BlockAccessor.SetBlock(0, pos);
        world.BlockAccessor.SetBlock(branch.BlockId, pos);

        BlockEntity? raw = world.BlockAccessor.GetBlockEntity(pos);
        if (raw is not BlockEntityFruitTreeBranch be)
        {
            Assert.Fail($"Expected BlockEntityFruitTreeBranch, got {raw?.GetType().Name ?? "null"}.");
            throw new InvalidOperationException();
        }

        be.RootOff = new Vec3i(0, 0, 0);
        if (stampPlanter)
        {
            ProsequorBlockPedigreeStation.StampPlanter(be, player.PlayerUID);
        }

        Assert.True(AbilityBootstrap.IsFruitTreeBlock(world.BlockAccessor.GetBlock(pos)));
        return (branch, pos, be);
    }

    Block RequireFruitTreeBranch()
    {
        IWorldAccessor world = World.Api.World;
        foreach (string path in new[]
                 {
                     "game:fruittree-branch-apple-stem-segment1",
                     "game:fruittree-branch-redapple-stem-segment1",
                     "game:fruittree-branch-cherry-stem-segment1",
                     "game:fruittree-foliage-apple-plain"
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
            if (block != null
                && block.Id != 0
                && AbilityBootstrap.IsFruitTreeBlock(block)
                && block is BlockFruitTreeBranch)
            {
                return block;
            }
        }

        Assert.Fail("[prosequor] No fruit-tree branch block found.");
        throw new InvalidOperationException();
    }

    Item RequireFruitItem()
    {
        IWorldAccessor world = World.Api.World;
        Item? fruit = world.GetItem(new AssetLocation("game:fruit-apple"))
            ?? world.GetItem(new AssetLocation("game:fruit-cherry"))
            ?? world.GetItem(new AssetLocation("game:fruit-redapple"));
        Assert.NotNull(fruit);
        return fruit!;
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

    static float TotalSkillXp(EntityBehaviorProgress progress)
    {
        var skill = progress.State.GetOrCreateSkill(Farming);
        return progress.GetSkillXp(Farming) + skill.Accrued;
    }

    static EntityBehaviorProgress RequireBehavior(IPlayer player)
    {
        EntityBehaviorProgress? progress = player.Entity?.GetBehavior<EntityBehaviorProgress>();
        Assert.NotNull(progress);
        return progress!;
    }
}
