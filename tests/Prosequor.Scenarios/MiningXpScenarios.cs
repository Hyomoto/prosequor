using Atlas.Api;
using Atlas.XUnit;
using Prosequor;
using Prosequor.Ability;
using Prosequor.Ability.Hooks;
using Prosequor.Player;
using Prosequor.Xp;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Xunit;

namespace Prosequor.Scenarios;

/// <summary>
/// Mining XP from pickaxe ore breaks: <c>ore</c> collection membership plus the live
/// <c>block-broken</c> deed (adapter notify, <see cref="Block.OnBlockBroken"/>, pickaxe swing).
/// </summary>
public class MiningXpScenarios : AtlasScenarioBase
{
    const string Mining = "mining";

    [AtlasScenario]
    [Trait("Layer", "Xp")]
    [Trait("Kind", "Mining")]
    public async Task OreCollection_Should_ContainMineClassOres_AndPlanPays()
    {
        ITestPlayer joined = await World.JoinPlayer("MineOreCol");
        IPlayer player = joined.Player;

        CollectionIndex collections = RequireCollections();
        Block ore = RequireOre();
        Item pickaxe = RequirePickaxe();
        string oreCode = Code(ore);
        string pickaxeCode = EventFactBuilder.CodeOf(pickaxe) ?? pickaxe.Code.ToString();

        Assert.Equal(BlockBreakClassification.TokenMine, BlockBreakClassification.ClassifyToken(ore));
        Assert.True(
            collections.Contains(AbilityBootstrap.OreTag, oreCode),
            $"ore collection missing {oreCode} (material={ore.BlockMaterial}, type={ore.GetType().Name})");
        Assert.False(
            collections.Contains(AbilityBootstrap.GemstoneTag, oreCode),
            $"non-gem ore {oreCode} must not join gemstone");
        Assert.True(
            collections.Contains("pickaxe", pickaxeCode),
            $"pickaxe collection missing {pickaxeCode}");
        Assert.True(
            collections.CodeCount(AbilityBootstrap.OreTag) > 0,
            "ore collection should not be empty after GameReady fill.");

        List<string> missing = [];
        foreach (Block block in World.Api.World.Blocks)
        {
            if (block == null
                || block.Id == 0
                || block.BlockMaterial != EnumBlockMaterial.Ore
                || AbilityBootstrap.IsGemstoneOre(block)
                || BlockBreakClassification.ClassifyToken(block) != BlockBreakClassification.TokenMine)
            {
                continue;
            }

            string code = Code(block);
            if (!collections.Contains(AbilityBootstrap.OreTag, code))
            {
                missing.Add($"{code} ({block.GetType().Name})");
            }
        }

        Assert.True(
            missing.Count == 0,
            "Mine-class ore blocks missing from the ore collection: "
            + string.Join(", ", missing.Take(12))
            + (missing.Count > 12 ? $" … +{missing.Count - 12} more" : ""));

        float planned = ScenarioXp.PlannedBlockBroken(
            World.Api.World,
            Mining,
            player.PlayerUID,
            pickaxeCode,
            oreCode,
            ore.Resistance,
            BlockBreakHardnessCatalog.DomainMine);
        Assert.True(
            planned > 0f,
            $"PlanPays should grant mining XP for pickaxe + {oreCode}; got {planned}. "
            + $"oreInCollection={collections.Contains(AbilityBootstrap.OreTag, oreCode)} "
            + $"pickaxeInCollection={collections.Contains("pickaxe", pickaxeCode)}");
    }

    [AtlasScenario]
    [Trait("Layer", "Xp")]
    [Trait("Kind", "Mining")]
    public async Task NotifyBlockBrokenXp_Should_PayMining_When_PickaxeBreaksOre()
    {
        ITestPlayer joined = await World.JoinPlayer("MineOreNotify");
        IPlayer player = joined.Player;
        IPlayerProgress progress = RequireProgress(player);

        Block ore = RequireOre();
        Item pickaxe = EquipPickaxe(player);
        BlockPos pos = PlaceNear(player, ore);
        AssertPaidOreBreak(
            progress,
            player,
            ore,
            pickaxe,
            () => ProsequorModSystem.For(World.Api)!.NotifyBlockBrokenXp(player, ore, pos),
            "adapter notify");
    }

    [AtlasScenario]
    [Trait("Layer", "Xp")]
    [Trait("Kind", "Mining")]
    public async Task OnBlockBroken_Should_PayMining_When_PickaxeBreaksOre()
    {
        ITestPlayer joined = await World.JoinPlayer("MineOreBreak");
        IPlayer player = joined.Player;
        IPlayerProgress progress = RequireProgress(player);

        Block ore = RequireOre();
        Item pickaxe = EquipPickaxe(player);
        BlockPos pos = PlaceNear(player, ore);
        AssertPaidOreBreak(
            progress,
            player,
            ore,
            pickaxe,
            () => ore.OnBlockBroken(World.Api.World, pos, player),
            "OnBlockBroken");
    }

    [AtlasScenario]
    [Trait("Layer", "Xp")]
    [Trait("Kind", "Mining")]
    public async Task OnBlockBrokenWith_Should_PayMining_When_PickaxeBreaksOre()
    {
        ITestPlayer joined = await World.JoinPlayer("MineOreSwing");
        IPlayer player = joined.Player;
        IPlayerProgress progress = RequireProgress(player);

        Block ore = RequireOre();
        Item pickaxe = EquipPickaxe(player);
        BlockPos pos = PlaceNear(player, ore);
        ItemSlot hotbar = player.InventoryManager.ActiveHotbarSlot;
        BlockSelection sel = new()
        {
            Position = pos,
            Face = BlockFacing.UP,
            HitPosition = new Vec3d(0.5, 0.5, 0.5),
            Block = ore
        };

        AssertPaidOreBreak(
            progress,
            player,
            ore,
            pickaxe,
            () =>
            {
                bool broken = pickaxe.OnBlockBrokenWith(World.Api.World, player.Entity, hotbar, sel);
                if (World.Api.World.BlockAccessor.GetBlock(pos).Id == ore.Id)
                {
                    ore.OnBlockBroken(World.Api.World, pos, player);
                    Assert.True(
                        broken || World.Api.World.BlockAccessor.GetBlock(pos).Id != ore.Id,
                        $"Pickaxe OnBlockBrokenWith did not break {Code(ore)} (broken={broken}); OnBlockBroken fallback also left it.");
                }
            },
            "OnBlockBrokenWith");
    }

    [AtlasScenario]
    [Trait("Layer", "Xp")]
    [Trait("Kind", "Mining")]
    public async Task OnBlockBroken_Should_NotPayMining_WithoutPickaxe()
    {
        ITestPlayer joined = await World.JoinPlayer("MineOreHand");
        IPlayer player = joined.Player;
        IPlayerProgress progress = RequireProgress(player);

        Block ore = RequireOre();
        BlockPos pos = PlaceNear(player, ore);
        Assert.Equal(CallerIdentities.Hand, EventFactBuilder.CallerOrHand(player));

        float before = ScenarioXp.TotalSkill(progress, Mining);
        ore.OnBlockBroken(World.Api.World, pos, player);
        float gained = ScenarioXp.TotalSkill(progress, Mining) - before;
        Assert.Equal(0f, gained);
    }

    void AssertPaidOreBreak(
        IPlayerProgress progress,
        IPlayer player,
        Block ore,
        Item pickaxe,
        Action emit,
        string path)
    {
        string oreCode = Code(ore);
        string pickaxeCode = EventFactBuilder.CodeOf(pickaxe) ?? pickaxe.Code.ToString();
        Assert.Equal(pickaxeCode, EventFactBuilder.CallerOrHand(player));

        float expected = ScenarioXp.PlannedBlockBroken(
            World.Api.World,
            Mining,
            player.PlayerUID,
            pickaxeCode,
            oreCode,
            ore.Resistance,
            BlockBreakHardnessCatalog.DomainMine);
        Assert.True(expected > 0f, $"{path}: PlanPays was 0 for {oreCode} with {pickaxeCode}.");

        float before = ScenarioXp.TotalSkill(progress, Mining);
        emit();
        float gained = ScenarioXp.TotalSkill(progress, Mining) - before;
        ScenarioXp.AssertPaid(gained, expected, $"{path} {oreCode}");
        Assert.True(
            gained > 0f,
            $"Expected mining XP from {path} ore break, got {gained}. block={oreCode} type={ore.GetType().Name} pickaxe={pickaxeCode}");
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

    static string Code(Block block) =>
        EventFactBuilder.CodeOf(block) ?? block.Code.ToString();

    Item EquipPickaxe(IPlayer player)
    {
        Item pickaxe = RequirePickaxe();
        player.InventoryManager.ActiveHotbarSlot.Itemstack = new ItemStack(pickaxe, 1);
        player.InventoryManager.ActiveHotbarSlot.MarkDirty();
        return pickaxe;
    }

    Item RequirePickaxe()
    {
        IWorldAccessor world = World.Api.World;
        Item? named = world.GetItem(new AssetLocation("game:pickaxe-copper"));
        if (named != null && named.Id != 0 && named.Tool == EnumTool.Pickaxe)
        {
            return named;
        }

        foreach (Item item in world.Items)
        {
            if (item != null && item.Id != 0 && item.Tool == EnumTool.Pickaxe)
            {
                return item;
            }
        }

        Assert.Fail("[prosequor] Expected a pickaxe item in the atlas world.");
        return null!;
    }

    Block RequireOre()
    {
        IWorldAccessor world = World.Api.World;
        foreach (string path in new[]
                 {
                     "game:ore-poor-nativecopper-granite",
                     "game:ore-poor-nativecopper-basalt",
                     "game:ore-medium-nativecopper-granite",
                     "game:ore-bituminouscoal-rich-granite"
                 })
        {
            Block? named = world.GetBlock(new AssetLocation(path));
            if (named != null
                && named.Id != 0
                && named.BlockMaterial == EnumBlockMaterial.Ore
                && !AbilityBootstrap.IsGemstoneOre(named)
                && BlockBreakClassification.ClassifyToken(named) == BlockBreakClassification.TokenMine)
            {
                return named;
            }
        }

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
