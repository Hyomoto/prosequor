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
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;
using Vintagestory.GameContent;
using Xunit;

namespace Prosequor.Scenarios;

/// <summary>
/// Live clay-form high-water XP: good places pay, undo/replace does not farm,
/// wrong voxels do not raise the mark.
/// </summary>
public class ClayFormXpScenarios : AtlasScenarioBase
{
    const string Skill = "clayforming";
    const float XpPerVoxel = 0.01f;

    [AtlasScenario]
    [Trait("Layer", "Xp")]
    [Trait("Kind", "ClayForm")]
    public async Task GoodVoxelPlace_Should_AwardClayformingXp()
    {
        ITestPlayer joined = await World.JoinPlayer("ClayXpGood");
        IPlayer player = joined.Player;
        EntityBehaviorProgress progress = RequireBehavior(player);

        BlockEntityClayForm form = PrepareForm(player, out ClayFormingRecipe recipe);
        List<Vec3i> goods = CollectGoodVoxels(recipe, max: 12);
        Assert.True(goods.Count >= 10, $"Expected a recipe with ≥10 voxels, got {goods.Count}.");

        float xpBefore = progress.GetSkillXp(Skill);
        float accruedBefore = progress.State.GetOrCreateSkill(Skill).Accrued;

        for (int i = 0; i < 10; i++)
        {
            Assert.True(
                ClayFormVoxelOps.OnAdd(form, goods[i].Y, goods[i], radius: 0, player),
                $"Expected good place at {goods[i]}.");
        }

        float xpAfter = progress.GetSkillXp(Skill);
        float accruedAfter = progress.State.GetOrCreateSkill(Skill).Accrued;
        // 0.01f is not binary-exact; ten clicks may sit entirely in Accrued below MinAward.
        float gained = (xpAfter - xpBefore) + (accruedAfter - accruedBefore);
        Assert.True(
            gained >= 0.099f,
            $"Expected ~{10 * XpPerVoxel} clayforming XP from 10 good voxels (xp {xpBefore}->{xpAfter}, accrued {accruedBefore}->{accruedAfter}), got {gained}.");
    }

    [AtlasScenario]
    [Trait("Layer", "Xp")]
    [Trait("Kind", "ClayForm")]
    public async Task UndoReplace_Should_NotFarmXp()
    {
        ITestPlayer joined = await World.JoinPlayer("ClayXpUndo");
        IPlayer player = joined.Player;
        EntityBehaviorProgress progress = RequireBehavior(player);

        BlockEntityClayForm form = PrepareForm(player, out ClayFormingRecipe recipe);
        Vec3i good = CollectGoodVoxels(recipe, max: 1)[0];

        Assert.True(ClayFormVoxelOps.OnAdd(form, good.Y, good, radius: 0, player));
        float accruedAfterPlace = progress.State.GetOrCreateSkill(Skill).Accrued;
        Assert.Equal(XpPerVoxel, accruedAfterPlace, precision: 4);

        Assert.True(
            ClayFormVoxelOps.OnRemove(form, good.Y, good, BlockFacing.UP, radius: 0, player));
        Assert.Equal(
            accruedAfterPlace,
            progress.State.GetOrCreateSkill(Skill).Accrued,
            precision: 4);

        Assert.True(ClayFormVoxelOps.OnAdd(form, good.Y, good, radius: 0, player));
        Assert.Equal(
            accruedAfterPlace,
            progress.State.GetOrCreateSkill(Skill).Accrued,
            precision: 4);
    }

    /// <summary>
    /// Color variants share one recipe Name (bowl.json → blue/fire/red). The voxel table
    /// may keep only the first; <c>clay-formed</c> must still list every output or the
    /// deed dies on <c>target:&lt;clay-formed&gt;</c> after matching crafting + @hand.
    /// </summary>
    [AtlasScenario]
    [Trait("Layer", "Xp")]
    [Trait("Kind", "ClayForm")]
    public async Task ClayFormedCollection_Should_ContainEveryRecipeOutput_And_PlanPaysRedBowl()
    {
        ITestPlayer joined = await World.JoinPlayer("ClayXpCatalog");
        IPlayer player = joined.Player;
        _ = RequireBehavior(player);

        ProsequorModSystem? mod = ProsequorModSystem.For(World.Api);
        Assert.NotNull(mod);
        ClayFormingRecipeCatalog? catalog = mod!.ClayFormingRecipes;
        Assert.NotNull(catalog);
        CollectionIndex collections = mod.Collections.Index;

        const string redBowl = "game:bowl-red-raw";
        Assert.True(
            collections.Contains("clay-formed", redBowl),
            $"Expected {redBowl} in <clay-formed> (later color variant after name-dedup).");
        Assert.Contains(redBowl, catalog!.OutputCodes, StringComparer.OrdinalIgnoreCase);

        List<string> missing = new();
        foreach (ClayFormingRecipe recipe in World.Api.GetClayformingRecipes())
        {
            foreach (string? code in RecipeOutputCodes(recipe))
            {
                if (!collections.Contains("clay-formed", code))
                {
                    missing.Add(code);
                }
            }
        }

        Assert.True(
            missing.Count == 0,
            "Expected every clayforming recipe Output.Code and resolved collectible in <clay-formed>. Missing: "
            + string.Join(", ", missing.Distinct(StringComparer.OrdinalIgnoreCase)));

        IReadOnlyList<Deed.PlannedPay> pays = Deed.PlanPays(
            mod.XpRules,
            collections,
            player.PlayerUID,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { DeedTokenTags.Crafting },
            caller: CallerIdentities.Hand,
            target: redBowl,
            mount: null,
            ground: null,
            lastCraft: null,
            metric: 0f,
            metricMin: 0f,
            metricMax: 0f,
            totalUnits: 0,
            craftCount: 1);

        Assert.Contains(pays, p => p.SkillId == Skill && p.Amount > 0f);
    }

    [AtlasScenario]
    [Trait("Layer", "Xp")]
    [Trait("Kind", "ClayForm")]
    public async Task WrongVoxelPlace_Should_NotAwardXp()
    {
        ITestPlayer joined = await World.JoinPlayer("ClayXpWrong");
        IPlayer player = joined.Player;
        EntityBehaviorProgress progress = RequireBehavior(player);

        BlockEntityClayForm form = PrepareForm(player, out ClayFormingRecipe recipe);
        Vec3i? wrong = FindWrongVoxelInBounds(recipe);
        Assert.NotNull(wrong);

        float accruedBefore = progress.State.GetOrCreateSkill(Skill).Accrued;
        Assert.True(ClayFormVoxelOps.OnAdd(form, wrong!.Y, wrong, radius: 0, player));
        Assert.Equal(
            accruedBefore,
            progress.State.GetOrCreateSkill(Skill).Accrued,
            precision: 4);
    }

    static EntityBehaviorProgress RequireBehavior(IPlayer player)
    {
        EntityBehaviorProgress? progress = player.Entity?.GetBehavior<EntityBehaviorProgress>();
        Assert.NotNull(progress);
        return progress!;
    }

    BlockEntityClayForm PrepareForm(IPlayer player, out ClayFormingRecipe recipe)
    {
        ICoreAPI api = World.Api;
        IWorldAccessor world = api.World;

        Block? clayFormBlock = world.GetBlock(new AssetLocation("game:clayform"));
        Assert.NotNull(clayFormBlock);

        BlockPos pos = player.Entity.Pos.AsBlockPos.AddCopy(2, 0, 0);
        world.BlockAccessor.SetBlock(0, pos);
        world.BlockAccessor.SetBlock(clayFormBlock.BlockId, pos);

        BlockEntityClayForm? form = world.BlockAccessor.GetBlockEntity(pos) as BlockEntityClayForm;
        Assert.NotNull(form);

        List<ClayFormingRecipe> recipes = api.GetClayformingRecipes();
        Assert.NotEmpty(recipes);
        recipe = recipes
            .OrderBy(CountRecipeVoxels)
            .First(r => CountRecipeVoxels(r) >= 10 && FindWrongVoxelInBounds(r) != null);

        form.OnReceivedClientPacket(
            player,
            (int)EnumClayFormingPacket.SelectRecipe,
            SerializerUtil.Serialize(recipe.RecipeId));
        Assert.NotNull(form.SelectedRecipe);
        Assert.Equal(ClayFormXpStation.RecipeKeyOf(recipe), ClayFormXpStation.RecipeKeyOf(form.SelectedRecipe));

        // Start from an empty grid so good-count is only what we place.
        form.Voxels = new bool[16, 16, 16];
        form.AvailableVoxels = 250;
        return form;
    }

    static IEnumerable<string> RecipeOutputCodes(ClayFormingRecipe recipe)
    {
        string? outputCode = recipe.Output?.Code?.ToString();
        if (!string.IsNullOrWhiteSpace(outputCode))
        {
            yield return outputCode.Trim();
        }

        string? resolved = recipe.Output?.ResolvedItemstack?.Collectible?.Code?.ToString();
        if (!string.IsNullOrWhiteSpace(resolved))
        {
            yield return resolved.Trim();
        }
    }

    static int CountRecipeVoxels(ClayFormingRecipe recipe)
    {
        int layers = Math.Min(16, recipe.QuantityLayers);
        int count = 0;
        for (int x = 0; x < 16; x++)
        {
            for (int y = 0; y < layers; y++)
            {
                for (int z = 0; z < 16; z++)
                {
                    if (recipe.Voxels[x, y, z])
                    {
                        count++;
                    }
                }
            }
        }

        return count;
    }

    static List<Vec3i> CollectGoodVoxels(ClayFormingRecipe recipe, int max)
    {
        List<Vec3i> list = new(max);
        int layers = Math.Min(16, recipe.QuantityLayers);
        for (int y = 0; y < layers && list.Count < max; y++)
        {
            for (int x = 0; x < 16 && list.Count < max; x++)
            {
                for (int z = 0; z < 16 && list.Count < max; z++)
                {
                    if (recipe.Voxels[x, y, z])
                    {
                        list.Add(new Vec3i(x, y, z));
                    }
                }
            }
        }

        return list;
    }

    static Vec3i? FindWrongVoxelInBounds(ClayFormingRecipe recipe)
    {
        int layers = Math.Min(16, recipe.QuantityLayers);
        for (int y = 0; y < layers; y++)
        {
            Cuboidi bounds = ClayFormVoxelOps.LayerBounds(recipe, y);
            if (bounds.X2 < bounds.X1 || bounds.Z2 < bounds.Z1)
            {
                continue;
            }

            for (int x = bounds.X1; x <= bounds.X2; x++)
            {
                for (int z = bounds.Z1; z <= bounds.Z2; z++)
                {
                    if (!recipe.Voxels[x, y, z])
                    {
                        return new Vec3i(x, y, z);
                    }
                }
            }
        }

        return null;
    }
}
