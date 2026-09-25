using Atlas.Api;
using Atlas.XUnit;
using Prosequor.Ability;
using Prosequor.Xp;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;
using Xunit;

namespace Prosequor.Scenarios;

/// <summary>
/// Cooking-pot stinkbait. Plain fennel, Grown By fennel, and fennel carrying the
/// collect-XP stamp must resolve the same recipe and the same bait item.
/// Bushmeat or dough avoids the soup recipes that share raw redmeat, fish, and poultry.
/// </summary>
public class StinkbaitScenarios : AtlasScenarioBase
{
    static int nextOffset = 4;

    [AtlasScenario]
    [Trait("Layer", "Pedigree")]
    [Trait("Kind", "Stinkbait")]
    public async Task Stinkbait_Should_CookSameBait_When_FennelHasStackMarks()
    {
        ITestPlayer joined = await World.JoinPlayer("Stinkbait");
        IPlayer player = joined.Player;
        IWorldAccessor world = World.Api.World;
        Assert.Equal(EnumAppSide.Server, world.Side);

        Block potBlock = RequireBlock("game:claypot-blue-fired", "claypot-blue-fired");
        Assert.True(
            potBlock is BlockCookingContainer,
            $"Expected a cooking pot, got {potBlock.GetType().Name}.");
        var pot = (BlockCookingContainer)potBlock;

        ItemStack[] plainInputs = FindStinkbaitInputs(world, pot, out string discovery);
        int fennelIndex = IndexOfPath(plainInputs, "fennel");
        Assert.True(fennelIndex >= 0, "Stinkbait inputs did not include fennel. " + discovery);

        ItemStack[] grownInputs = CloneAll(plainInputs);
        OwnerCredit.StampGrownBy(grownInputs[fennelIndex], player.PlayerUID);
        Assert.Equal(OwnerCredit.GrownByLang, OwnerCredit.TryGetCreditLang(grownInputs[fennelIndex]));
        Assert.Equal(player.PlayerUID, CraftAttribution.TryGetMakerUid(grownInputs[fennelIndex]));
        Assert.True(
            string.IsNullOrEmpty(OwnerCredit.TryGetCreditLang(plainInputs[fennelIndex])),
            "Plain fennel must not carry Grown By.");

        ItemStack[] collectedInputs = CloneAll(plainInputs);
        CollectXpStamp.Set(collectedInputs[fennelIndex]);
        Assert.True(CollectXpStamp.Has(collectedInputs[fennelIndex]));

        CookingRecipe? plainRecipe = pot.GetMatchingCookingRecipe(world, plainInputs, out int plainServings);
        Assert.True(
            plainRecipe != null && IsStinkbait(plainRecipe) && plainServings >= 1,
            $"Plain fennel must match a stinkbait recipe. Got '{plainRecipe?.Code ?? "none"}' "
            + $"servings={plainServings}. Inputs: {Describe(plainInputs)}. {discovery}");

        AssertSameRecipe(pot, world, grownInputs, plainRecipe!, plainServings, "Grown By fennel");
        AssertSameRecipe(pot, world, collectedInputs, plainRecipe!, plainServings, "Collect-XP fennel");

        ItemStack plainBait = Cook(player, potBlock, plainInputs);
        ItemStack grownBait = Cook(player, potBlock, grownInputs);
        Assert.Contains("fishingbait", plainBait.Collectible.Code.Path, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(plainBait.Collectible.Code.ToString(), grownBait.Collectible.Code.ToString());
        Assert.True(
            string.IsNullOrEmpty(OwnerCredit.TryGetCreditLang(plainBait))
            && string.IsNullOrEmpty(OwnerCredit.TryGetCreditLang(grownBait)),
            "Cooked stinkbait must not inherit the fennel's Grown By tag.");
        Assert.False(CollectXpStamp.Has(plainBait));
        Assert.False(CollectXpStamp.Has(grownBait));
    }

    static void AssertSameRecipe(
        BlockCookingContainer pot,
        IWorldAccessor world,
        ItemStack[] inputs,
        CookingRecipe plainRecipe,
        int plainServings,
        string label)
    {
        CookingRecipe? hit = pot.GetMatchingCookingRecipe(world, inputs, out int servings);
        Assert.True(
            hit != null && IsStinkbait(hit) && servings >= 1,
            $"{label} must match a stinkbait recipe. Got '{hit?.Code ?? "none"}' servings={servings}. "
            + $"Plain matched '{plainRecipe.Code}' servings={plainServings}. Inputs: {Describe(inputs)}.");
        Assert.Equal(plainRecipe.Code, hit!.Code);
    }

    ItemStack Cook(IPlayer player, Block pot, ItemStack[] ingredients)
    {
        BlockEntityFirepit firepit = PlaceFirepit(player);
        firepit.inputSlot.Itemstack = new ItemStack(pot, 1);
        firepit.inputSlot.MarkDirty();
        Assert.True(
            firepit.otherCookingSlots.Length >= ingredients.Length,
            $"Firepit has {firepit.otherCookingSlots.Length} cooking slots, need {ingredients.Length}.");

        for (int i = 0; i < firepit.otherCookingSlots.Length; i++)
        {
            firepit.otherCookingSlots[i].Itemstack = i < ingredients.Length ? ingredients[i].Clone() : null;
            firepit.otherCookingSlots[i].MarkDirty();
        }

        firepit.smeltItems();
        ItemStack? bait = FindStack(firepit, "fishingbait");
        Assert.True(
            bait != null && bait.StackSize >= 1,
            $"Expected a fishing bait stack from {Describe(ingredients)}. Slots: {DescribeFirepit(firepit)}.");
        return bait!;
    }

    static ItemStack[] FindStinkbaitInputs(IWorldAccessor world, BlockCookingContainer pot, out string discovery)
    {
        List<CookingRecipe>? recipes = world.Api.GetCookingRecipes();
        Assert.NotNull(recipes);
        var attempts = new List<string>();
        int stinkbaitRecipes = 0;
        foreach (CookingRecipe recipe in recipes!)
        {
            if (!IsStinkbait(recipe))
            {
                continue;
            }

            stinkbaitRecipes++;
            if (!TryBuildInputs(recipe, world, out ItemStack[] built))
            {
                attempts.Add($"{recipe.Code}: could not build fennel + bushmeat/dough");
                continue;
            }

            CookingRecipe? hit = pot.GetMatchingCookingRecipe(world, built, out int servings);
            attempts.Add($"{recipe.Code} -> pot '{hit?.Code ?? "none"}' servings={servings} [{Describe(built)}]");
            if (hit != null && IsStinkbait(hit) && servings >= 1)
            {
                discovery = string.Join("; ", attempts);
                return built;
            }
        }

        discovery = string.Join("; ", attempts);
        Assert.Fail(
            $"No stinkbait recipe accepted fennel with bushmeat or dough "
            + $"({stinkbaitRecipes} stinkbait recipes). {discovery}");
        throw new InvalidOperationException();
    }

    static bool TryBuildInputs(CookingRecipe recipe, IWorldAccessor world, out ItemStack[] built)
    {
        built = [];
        if (recipe.Ingredients == null)
        {
            return false;
        }

        var stacks = new List<ItemStack>();
        bool haveFennel = false;
        bool haveProtein = false;
        foreach (CookingRecipeIngredient ingredient in recipe.Ingredients)
        {
            if (ingredient.MinQuantity <= 0)
            {
                continue;
            }

            ItemStack? chosen = null;
            if (!haveFennel)
            {
                chosen = FirstAccepting(ingredient, world, IsFennel);
                if (chosen != null)
                {
                    haveFennel = true;
                }
            }

            if (chosen == null && !haveProtein)
            {
                chosen = FirstAccepting(ingredient, world, IsBushmeat)
                    ?? FirstAccepting(ingredient, world, IsDough);
                if (chosen != null)
                {
                    haveProtein = true;
                }
            }

            chosen ??= FirstAccepting(ingredient, world, stack => !IsSoupMeat(stack))
                ?? FirstAccepting(ingredient, world, _ => true);
            if (chosen == null)
            {
                return false;
            }

            for (int n = 0; n < ingredient.MinQuantity; n++)
            {
                stacks.Add(chosen.Clone());
            }
        }

        if (!haveFennel || !haveProtein || stacks.Count == 0 || stacks.Count > 4)
        {
            return false;
        }

        built = stacks.ToArray();
        return true;
    }

    static ItemStack? FirstAccepting(
        CookingRecipeIngredient ingredient,
        IWorldAccessor world,
        System.Func<ItemStack, bool> predicate)
    {
        if (ingredient.ValidStacks == null)
        {
            return null;
        }

        foreach (CookingRecipeStack entry in ingredient.ValidStacks)
        {
            foreach (ItemStack candidate in Expand(entry, world))
            {
                if (!predicate(candidate) || ingredient.GetMatchingStack(candidate) == null)
                {
                    continue;
                }

                candidate.StackSize = StackSizeForOneServing(candidate, ingredient);
                if (ingredient.GetMatchingStack(candidate) == null)
                {
                    continue;
                }

                return candidate;
            }
        }

        return null;
    }

    static int StackSizeForOneServing(ItemStack stack, CookingRecipeIngredient ingredient)
    {
        CookingRecipeStack? matched = ingredient.GetMatchingStack(stack);
        int unit = Math.Max(1, matched?.StackSize ?? 1);
        WaterTightContainableProps? props = BlockLiquidContainerBase.GetContainableProps(stack);
        if (props != null && ingredient.PortionSizeLitres > 0f && props.ItemsPerLitre > 0f)
        {
            return Math.Max(1, (int)(unit * props.ItemsPerLitre * ingredient.PortionSizeLitres));
        }

        return unit;
    }

    static List<ItemStack> Expand(CookingRecipeStack entry, IWorldAccessor world)
    {
        var found = new List<ItemStack>();
        if (entry.ResolvedItemstack?.Collectible != null)
        {
            found.Add(entry.ResolvedItemstack.Clone());
            return found;
        }

        AssetLocation? code = entry.Code;
        if (code == null || string.IsNullOrEmpty(code.Path))
        {
            return found;
        }

        if (!code.Path.Contains('*'))
        {
            Item? item = world.GetItem(code);
            if (item != null)
            {
                found.Add(new ItemStack(item));
            }
            else
            {
                Block? block = world.GetBlock(code);
                if (block != null)
                {
                    found.Add(new ItemStack(block));
                }
            }

            return found;
        }

        foreach (Item item in world.Items)
        {
            if (item?.Code != null && item.WildCardMatch(code))
            {
                found.Add(new ItemStack(item));
            }
        }

        foreach (Block block in world.Blocks)
        {
            if (block?.Code != null && block.Id != 0 && block.WildCardMatch(code))
            {
                found.Add(new ItemStack(block));
            }
        }

        return found;
    }

    static bool IsStinkbait(CookingRecipe recipe)
    {
        if (Contains(recipe.Code, "stinkbait"))
        {
            return true;
        }

        string? into = recipe.CooksInto?.ResolvedItemstack?.Collectible?.Code?.Path
            ?? recipe.CooksInto?.Code?.Path;
        return Contains(into, "stinkbait");
    }

    static bool IsFennel(ItemStack stack)
    {
        string path = stack.Collectible?.Code?.Path ?? "";
        return Contains(path, "fennel") && !Contains(path, "seed");
    }

    static bool IsBushmeat(ItemStack stack) =>
        Contains(stack.Collectible?.Code?.Path, "bushmeat");

    static bool IsDough(ItemStack stack) =>
        Contains(stack.Collectible?.Code?.Path, "dough");

    static bool IsSoupMeat(ItemStack stack)
    {
        string path = stack.Collectible?.Code?.Path ?? "";
        return Contains(path, "redmeat")
            || Contains(path, "poultry")
            || Contains(path, "fish");
    }

    static bool Contains(string? value, string token) =>
        value != null && value.Contains(token, StringComparison.OrdinalIgnoreCase);

    static int IndexOfPath(ItemStack[] stacks, string token)
    {
        for (int i = 0; i < stacks.Length; i++)
        {
            if (Contains(stacks[i].Collectible?.Code?.Path, token))
            {
                return i;
            }
        }

        return -1;
    }

    static ItemStack[] CloneAll(ItemStack[] stacks)
    {
        var copy = new ItemStack[stacks.Length];
        for (int i = 0; i < stacks.Length; i++)
        {
            copy[i] = stacks[i].Clone();
        }

        return copy;
    }

    static string Describe(ItemStack[] stacks)
    {
        var parts = new string[stacks.Length];
        for (int i = 0; i < stacks.Length; i++)
        {
            parts[i] = stacks[i].StackSize + "x " + (stacks[i].Collectible?.Code?.ToString() ?? "?");
        }

        return string.Join(", ", parts);
    }

    static ItemStack? FindStack(BlockEntityFirepit firepit, string pathContains)
    {
        InventoryBase? inv = firepit.Inventory;
        if (inv == null)
        {
            return null;
        }

        for (int i = 0; i < inv.Count; i++)
        {
            ItemStack? stack = inv[i]?.Itemstack;
            if (Contains(stack?.Collectible?.Code?.Path, pathContains))
            {
                return stack;
            }
        }

        return null;
    }

    static string DescribeFirepit(BlockEntityFirepit firepit)
    {
        InventoryBase? inv = firepit.Inventory;
        if (inv == null)
        {
            return "no inventory";
        }

        var parts = new List<string>();
        for (int i = 0; i < inv.Count; i++)
        {
            ItemStack? stack = inv[i]?.Itemstack;
            if (stack?.Collectible?.Code == null)
            {
                continue;
            }

            parts.Add($"{i}:{stack.StackSize}x {stack.Collectible.Code}");
        }

        return parts.Count == 0 ? "empty" : string.Join(", ", parts);
    }

    Block RequireBlock(string code, string fallback)
    {
        IWorldAccessor world = World.Api.World;
        Block? block = world.GetBlock(new AssetLocation(code))
            ?? world.GetBlock(new AssetLocation(fallback));
        Assert.NotNull(block);
        return block!;
    }

    BlockEntityFirepit PlaceFirepit(IPlayer player)
    {
        IWorldAccessor world = World.Api.World;
        int offset = nextOffset;
        nextOffset += 3;
        BlockPos pos = player.Entity.Pos.AsBlockPos.AddCopy(offset, 0, 0);
        EnsureFloor(pos);

        Block firepit = RequireBlock("game:firepit-cold", "firepit-cold");
        world.BlockAccessor.SetBlock(0, pos);
        world.BlockAccessor.SetBlock(firepit.BlockId, pos);
        if (world.BlockAccessor.GetBlockEntity(pos) is not BlockEntityFirepit be)
        {
            Assert.Fail($"Expected BlockEntityFirepit at {pos}.");
            throw new InvalidOperationException();
        }

        return be;
    }

    void EnsureFloor(BlockPos pos)
    {
        IWorldAccessor world = World.Api.World;
        BlockPos below = pos.DownCopy();
        Block? dirt = world.GetBlock(new AssetLocation("game:soil-low-none"))
            ?? world.GetBlock(new AssetLocation("game:dirt"));
        if (dirt != null && world.BlockAccessor.GetBlock(below).Id == 0)
        {
            world.BlockAccessor.SetBlock(dirt.BlockId, below);
        }
    }
}
