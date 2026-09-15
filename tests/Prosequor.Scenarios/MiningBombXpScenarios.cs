using Atlas.Api;
using Atlas.XUnit;
using Prosequor;
using Prosequor.Ability;
using Prosequor.Ability.Hooks;
using Prosequor.Player;
using Prosequor.Xp;
using Prosequor.Xp.Adapters;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;
using Xunit;

namespace Prosequor.Scenarios;

/// <summary>
/// Mining XP from bomb blasts: same <c>block-broken</c> deed as a pickaxe, caller = bomb.
/// </summary>
public class MiningBombXpScenarios : AtlasScenarioBase
{
    const string Mining = "mining";

    [AtlasScenario]
    [Trait("Layer", "Xp")]
    [Trait("Kind", "Mining")]
    public async Task BombsCollection_Should_ContainOreBlastingBomb()
    {
        ITestPlayer joined = await World.JoinPlayer("MineBombCol");
        _ = joined.Player;

        Block bomb = RequireBomb();
        CollectionIndex collections = RequireCollections();
        string code = EventFactBuilder.CodeOf(bomb) ?? bomb.Code.ToString();
        Assert.True(
            collections.Contains(AbilityBootstrap.BombsTag, code),
            $"bombs collection missing {code}");
    }

    [AtlasScenario]
    [Trait("Layer", "Xp")]
    [Trait("Kind", "Mining")]
    public async Task NotifyBlockExploded_Should_PayMining_When_BombBreaksOre()
    {
        ITestPlayer joined = await World.JoinPlayer("MineBombOre");
        IPlayer player = joined.Player;
        IPlayerProgress progress = RequireProgress(player);

        Block ore = RequireOre();
        Block bomb = RequireBomb();
        BlockPos pos = PlaceNear(player, ore);
        string bombCode = EventFactBuilder.CodeOf(bomb) ?? bomb.Code.ToString();
        string? oreCode = EventFactBuilder.CodeOf(ore);

        float before = ScenarioXp.TotalSkill(progress, Mining);
        ProsequorModSystem.For(World.Api)!.NotifyBlockExplodedXp(
            player.PlayerUID,
            ore,
            pos,
            bombCode);
        float gained = ScenarioXp.TotalSkill(progress, Mining) - before;
        float expected = ScenarioXp.PlannedBlockBroken(
            World.Api.World,
            Mining,
            player.PlayerUID,
            bombCode,
            oreCode,
            ore.Resistance,
            BlockBreakHardnessCatalog.DomainMine);
        ScenarioXp.AssertPaid(gained, expected, $"bomb break {oreCode}");
        Assert.True(gained > 0f, $"Expected mining XP from bomb ore break, got {gained}.");
    }

    [AtlasScenario]
    [Trait("Layer", "Xp")]
    [Trait("Kind", "Mining")]
    public async Task NotifyBlockExploded_Should_NotPayMining_When_BombBreaksSoil()
    {
        ITestPlayer joined = await World.JoinPlayer("MineBombSoil");
        IPlayer player = joined.Player;
        IPlayerProgress progress = RequireProgress(player);

        Block? soil = World.Api.World.GetBlock(new AssetLocation("game:soil-low-none"))
            ?? World.Api.World.GetBlock(new AssetLocation("game:dirt"));
        Assert.NotNull(soil);
        Block bomb = RequireBomb();
        BlockPos pos = PlaceNear(player, soil!);
        string bombCode = EventFactBuilder.CodeOf(bomb) ?? bomb.Code.ToString();

        float before = ScenarioXp.TotalSkill(progress, Mining);
        ProsequorModSystem.For(World.Api)!.NotifyBlockExplodedXp(
            player.PlayerUID,
            soil!,
            pos,
            bombCode);
        float gained = ScenarioXp.TotalSkill(progress, Mining) - before;
        Assert.Equal(0f, gained);
    }

    CollectionIndex RequireCollections()
    {
        CollectionIndex? index = ProsequorModSystem.For(World.Api)?.Collections?.Index;
        Assert.NotNull(index);
        return index!;
    }

    IPlayerProgress RequireProgress(IPlayer player)
    {
        IPlayerProgress? progress = ProsequorModSystem.GetProgress(player);
        Assert.NotNull(progress);
        return progress!;
    }

    Block RequireBomb()
    {
        IWorldAccessor world = World.Api.World;
        Block? named = world.GetBlock(new AssetLocation("game:bomb-ore"));
        if (named is BlockBomb)
        {
            return named;
        }

        foreach (Block block in world.Blocks)
        {
            if (block is BlockBomb && block.Id != 0)
            {
                return block;
            }
        }

        Assert.Fail("[prosequor] Expected a BlockBomb in the atlas world.");
        return null!;
    }

    Block RequireOre()
    {
        IWorldAccessor world = World.Api.World;
        foreach (Block block in world.Blocks)
        {
            if (block == null
                || block.Id == 0
                || block.BlockMaterial != EnumBlockMaterial.Ore
                || AbilityBootstrap.IsGemstoneOre(block)
                || BlockBreakClassification.ClassifyToken(block) != BlockBreakClassification.TokenMine)
            {
                continue;
            }

            return block;
        }

        Assert.Fail("[prosequor] Expected a mine-class ore block in the atlas world.");
        return null!;
    }

    BlockPos PlaceNear(IPlayer nearPlayer, Block block)
    {
        IWorldAccessor world = World.Api.World;
        BlockPos pos = nearPlayer.Entity.Pos.AsBlockPos.AddCopy(2, 0, 0);
        Block? dirt = world.GetBlock(new AssetLocation("game:soil-low-none"))
            ?? world.GetBlock(new AssetLocation("game:dirt"));
        if (dirt != null)
        {
            world.BlockAccessor.SetBlock(dirt.BlockId, pos.DownCopy());
        }

        world.BlockAccessor.SetBlock(0, pos);
        world.BlockAccessor.SetBlock(block.BlockId, pos);
        Assert.Equal(block.Id, world.BlockAccessor.GetBlock(pos).Id);
        return pos;
    }
}
