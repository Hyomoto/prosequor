using Atlas.Api;
using Atlas.XUnit;
using Prosequor.Ability;
using Prosequor.Player;
using System.Reflection;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;
using Xunit;

namespace Prosequor.Scenarios;

/// <summary>
/// Firepit cook completion: process starter, cooking XP, quality roll, then
/// meal pots keep pedigree until serve while non-serve products take it immediately.
/// </summary>
public class CookQualityScenarios : AtlasScenarioBase
{
    const string Skill = "cooking";

    static int nextOffset = 4;

    [AtlasScenario]
    [Trait("Layer", "Pedigree")]
    [Trait("Kind", "CookQuality")]
    public async Task MealPot_Should_PayAndRollOnFinish_And_PreserveUntilServe()
    {
        ITestPlayer joined = await World.JoinPlayer("CookMeal");
        IPlayer cook = joined.Player;
        GrantCookQuality(RequireProgress(cook));
        AssertServer();

        BlockEntityFirepit firepit = PlaceFirepit(cook);
        StartCooking(firepit, cook);

        Block pot = RequireBlock("game:claypot-blue-fired", "claypot-blue-fired");
        Item grain = RequireItem("game:grain-spelt", "grain-spelt");
        firepit.inputSlot.Itemstack = new ItemStack(pot, 1);
        firepit.inputSlot.MarkDirty();
        Assert.True(firepit.otherCookingSlots.Length >= 2, "Expected firepit cooking slots.");
        // Porridge grain minQuantity is slot count, not stack size.
        firepit.otherCookingSlots[0].Itemstack = new ItemStack(grain, 1);
        firepit.otherCookingSlots[1].Itemstack = new ItemStack(grain, 1);
        firepit.otherCookingSlots[0].MarkDirty();
        firepit.otherCookingSlots[1].MarkDirty();

        float before = TotalCookingXp(cook);
        firepit.smeltItems();

        ItemStack? cooked = firepit.outputSlot.Itemstack;
        Assert.NotNull(cooked);
        Assert.Contains("cooked", cooked!.Collectible.Code.Path, StringComparison.OrdinalIgnoreCase);
        Assert.True(
            string.IsNullOrEmpty(CraftAttribution.TryGetMakerUid(cooked)),
            "Anonymous pot must not gain Created By from the cook.");
        Assert.True(CraftAttribution.HasContributor(cooked, cook.PlayerUID));
        AssertCookQuality(cooked, "cooked pot");
        float gained = TotalCookingXp(cook) - before;
        int servings = MealServings(cooked);
        float expected = ScenarioXp.PlannedCrafted(
            World.Api.World,
            Skill,
            cook.PlayerUID,
            cooked,
            servings,
            ingredients: 2,
            caller: EventFactBuilder.CodeOf(pot));
        ScenarioXp.AssertPaid(gained, expected, $"cooked pot ({servings} servings, 2 ingredients)");

        Block bowl = RequireBlock("game:bowl-blue-fired", "bowl-blue-fired");
        ItemSlot potSlot = new DummySlot(cooked);
        ItemSlot bowlSlot = new DummySlot(new ItemStack(bowl, 1));
        var container = (BlockCookedContainerBase)cooked.Collectible;
        bool served = container.ServeIntoStack(bowlSlot, potSlot, World.Api.World);
        Assert.True(served, "Expected ServeIntoStack to mint a meal bowl.");
        ItemStack? meal = bowlSlot.Itemstack;
        Assert.NotNull(meal);
        Assert.Contains("meal", meal!.Collectible.Code.Path, StringComparison.OrdinalIgnoreCase);
        Assert.True(
            string.IsNullOrEmpty(CraftAttribution.TryGetMakerUid(meal)),
            "Serve must not stamp the cook as the bowl's Created By.");
        Assert.True(CraftAttribution.HasContributor(meal, cook.PlayerUID));
        AssertQualityCopied(cooked, meal, "served meal");
        Assert.True(
            string.IsNullOrEmpty(CraftAttribution.TryGetMakerUid(potSlot.Itemstack)),
            "Serve must not stamp the cook as the pot's Created By.");
        EmptyPot(container, potSlot, bowlSlot, World.Api.World);
        Assert.True(string.IsNullOrEmpty(CraftAttribution.TryGetMakerUid(potSlot.Itemstack)));
        Assert.False(CraftAttribution.HasContributor(potSlot.Itemstack, cook.PlayerUID));
    }

    [AtlasScenario]
    [Trait("Layer", "Pedigree")]
    [Trait("Kind", "CookQuality")]
    public async Task MealServe_Should_KeepPotAndBowlMakers_And_CreditCookAsContributor()
    {
        const string potterA = "potter-a";
        const string potterC = "potter-c";
        ITestPlayer joined = await World.JoinPlayer("CookMealCredit");
        IPlayer cook = joined.Player;
        GrantCookQuality(RequireProgress(cook));
        AssertServer();

        BlockEntityFirepit firepit = PlaceFirepit(cook);
        StartCooking(firepit, cook);

        Block pot = RequireBlock("game:claypot-blue-fired", "claypot-blue-fired");
        Item grain = RequireItem("game:grain-spelt", "grain-spelt");
        ItemStack potStack = new ItemStack(pot, 1);
        CraftAttribution.StampMakerUid(potStack, potterA);
        firepit.inputSlot.Itemstack = potStack;
        firepit.inputSlot.MarkDirty();
        Assert.True(firepit.otherCookingSlots.Length >= 2, "Expected firepit cooking slots.");
        firepit.otherCookingSlots[0].Itemstack = new ItemStack(grain, 1);
        firepit.otherCookingSlots[1].Itemstack = new ItemStack(grain, 1);
        firepit.otherCookingSlots[0].MarkDirty();
        firepit.otherCookingSlots[1].MarkDirty();

        firepit.smeltItems();

        ItemStack? cooked = firepit.outputSlot.Itemstack;
        Assert.NotNull(cooked);
        Assert.Equal(potterA, CraftAttribution.TryGetMakerUid(cooked));
        Assert.True(CraftAttribution.HasContributor(cooked, cook.PlayerUID));
        Assert.False(
            string.Equals(CraftAttribution.TryGetMakerUid(cooked), cook.PlayerUID, StringComparison.Ordinal),
            "Cook must not replace the potter as Created By.");
        AssertCookQuality(cooked!, "cooked pot");

        Block bowl = RequireBlock("game:bowl-blue-fired", "bowl-blue-fired");
        ItemStack bowlStack = new ItemStack(bowl, 1);
        CraftAttribution.StampMakerUid(bowlStack, potterC);
        ItemSlot potSlot = new DummySlot(cooked);
        ItemSlot bowlSlot = new DummySlot(bowlStack);
        var container = (BlockCookedContainerBase)cooked!.Collectible;
        bool served = container.ServeIntoStack(bowlSlot, potSlot, World.Api.World);
        Assert.True(served, "Expected ServeIntoStack to mint a meal bowl.");
        ItemStack? meal = bowlSlot.Itemstack;
        Assert.NotNull(meal);
        Assert.Equal(potterC, CraftAttribution.TryGetMakerUid(meal));
        Assert.True(CraftAttribution.HasContributor(meal, cook.PlayerUID));
        AssertContributorWeight(meal!, cook.PlayerUID, 1);
        AssertQualityCopied(cooked, meal!, "served meal");
        Assert.Equal(potterA, CraftAttribution.TryGetMakerUid(potSlot.Itemstack));
        EmptyPot(container, potSlot, bowlSlot, World.Api.World);
        Assert.Equal(potterA, CraftAttribution.TryGetMakerUid(potSlot.Itemstack));
        Assert.False(
            CraftAttribution.HasContributor(potSlot.Itemstack, cook.PlayerUID),
            "Emptying the pot must drop the cook and keep the potter.");
        Assert.False(MealHostCredit.StillHasMeal(potSlot.Itemstack));
    }

    [AtlasScenario]
    [Trait("Layer", "Pedigree")]
    [Trait("Kind", "CookQuality")]
    public async Task MealServe_Should_KeepCrockMaker_And_CreditCookAsContributor()
    {
        const string potter = "crock-potter";
        ITestPlayer joined = await World.JoinPlayer("CookCrock");
        IPlayer cook = joined.Player;
        GrantCookQuality(RequireProgress(cook));
        AssertServer();

        BlockEntityFirepit firepit = PlaceFirepit(cook);
        StartCooking(firepit, cook);
        Block pot = RequireBlock("game:claypot-blue-fired", "claypot-blue-fired");
        Item grain = RequireItem("game:grain-spelt", "grain-spelt");
        firepit.inputSlot.Itemstack = new ItemStack(pot, 1);
        firepit.inputSlot.MarkDirty();
        firepit.otherCookingSlots[0].Itemstack = new ItemStack(grain, 1);
        firepit.otherCookingSlots[1].Itemstack = new ItemStack(grain, 1);
        firepit.smeltItems();

        ItemStack? cooked = firepit.outputSlot.Itemstack;
        Assert.NotNull(cooked);
        Block crock = RequireBlock("game:crock-blue-fired", "crock-blue-fired");
        ItemStack crockStack = new ItemStack(crock, 1);
        CraftAttribution.StampMakerUid(crockStack, potter);
        ItemSlot potSlot = new DummySlot(cooked);
        ItemSlot crockSlot = new DummySlot(crockStack);
        var container = (BlockCookedContainerBase)cooked!.Collectible;
        bool served = container.ServeIntoStack(crockSlot, potSlot, World.Api.World);
        Assert.True(served, "Expected ServeIntoStack to fill the crock.");
        ItemStack? filled = crockSlot.Itemstack;
        Assert.NotNull(filled);
        Assert.Equal(potter, CraftAttribution.TryGetMakerUid(filled));
        Assert.True(CraftAttribution.HasContributor(filled, cook.PlayerUID));
        AssertQualityCopied(cooked, filled!, "crock meal");
    }

    [AtlasScenario]
    [Trait("Layer", "Pedigree")]
    [Trait("Kind", "CookQuality")]
    public async Task SmeltedMeat_Should_StampValuesImmediately()
    {
        ITestPlayer joined = await World.JoinPlayer("CookMeat");
        IPlayer cook = joined.Player;
        GrantCookQuality(RequireProgress(cook));
        AssertServer();

        BlockEntityFirepit firepit = PlaceFirepit(cook);
        StartCooking(firepit, cook);

        Item raw = RequireItem("game:redmeat-raw", "redmeat-raw");
        firepit.inputSlot.Itemstack = new ItemStack(raw, 4);
        firepit.inputSlot.MarkDirty();

        float before = TotalCookingXp(cook);
        float miningBefore = ScenarioXp.TotalSkill(cook, "mining");
        firepit.smeltItems();

        ItemStack? cooked = firepit.outputSlot.Itemstack;
        Assert.NotNull(cooked);
        Assert.Equal("redmeat-cooked", cooked!.Collectible.Code.Path);
        Assert.Equal(1, cooked.StackSize);
        Assert.Equal(3, firepit.inputSlot.Itemstack?.StackSize);
        AssertPedigree(cooked, cook.PlayerUID, "smelted meat");
        float one = ScenarioXp.PlannedCrafted(
            World.Api.World,
            Skill,
            cook.PlayerUID,
            cooked,
            quantity: 1,
            ingredients: 1,
            caller: EventFactBuilder.CodeOf(firepit.Block));
        ScenarioXp.AssertPaid(TotalCookingXp(cook) - before, one, "first roast from a stack of 4");

        string? grade = null;
        if (ItemAffixes.TryGetQuality(cooked, out ItemAffixEntry firstGrade))
        {
            grade = firstGrade.LangKey;
        }

        firepit.smeltItems();
        Assert.Equal(2, firepit.outputSlot.Itemstack?.StackSize);
        Assert.Equal(2, firepit.inputSlot.Itemstack?.StackSize);
        ScenarioXp.AssertPaid(TotalCookingXp(cook) - before, one * 2, "second roast still pays one item");
        if (grade != null)
        {
            Assert.True(ItemAffixes.TryGetQuality(firepit.outputSlot.Itemstack, out ItemAffixEntry secondGrade));
            Assert.Equal(grade, secondGrade.LangKey);
        }

        Assert.Equal(miningBefore, ScenarioXp.TotalSkill(cook, "mining"));
    }

    [AtlasScenario]
    [Trait("Layer", "Pedigree")]
    [Trait("Kind", "CookQuality")]
    public async Task FirepitSmelt_Should_NotPayMining_ForANuggetStack()
    {
        ITestPlayer joined = await World.JoinPlayer("CookNugget");
        IPlayer cook = joined.Player;
        GrantCookQuality(RequireProgress(cook));
        AssertServer();

        BlockEntityFirepit firepit = PlaceFirepit(cook);
        StartCooking(firepit, cook);
        Item nugget = RequireItem("game:nugget-nativecopper", "nugget-nativecopper");
        firepit.inputSlot.Itemstack = new ItemStack(nugget, 8);
        firepit.inputSlot.MarkDirty();

        float miningBefore = ScenarioXp.TotalSkill(cook, "mining");
        float cookingBefore = TotalCookingXp(cook);
        firepit.smeltItems();

        Assert.Equal(miningBefore, ScenarioXp.TotalSkill(cook, "mining"));
        Assert.Equal(cookingBefore, TotalCookingXp(cook));
        Assert.True(
            (firepit.inputSlot.Itemstack?.StackSize ?? 0) >= 7,
            "The unsmelted nuggets must still be in the input.");
    }

    [AtlasScenario]
    [Trait("Layer", "Pedigree")]
    [Trait("Kind", "CookQuality")]
    public async Task OvenBake_Should_PayPartBaked_And_NotPayPerfectAgain()
    {
        ITestPlayer joined = await World.JoinPlayer("CookOven");
        IPlayer cook = joined.Player;
        GrantCookQuality(RequireProgress(cook));
        AssertServer();

        BlockEntityOven oven = PlaceOven(cook);
        OvenCookStarterStation.NoteInteractor(oven, cook);
        Item dough = RequireItem("game:dough-spelt", "dough-spelt");
        Assert.True(oven.Inventory.Count > 0, "Expected an oven inventory.");
        int slot = 0;
        oven.Inventory[slot].Itemstack = new ItemStack(dough, 1);
        oven.Inventory[slot].MarkDirty();

        float before = TotalCookingXp(cook);
        Assert.True(
            BakeUntil(oven, slot, "partbaked"),
            "Expected the oven to turn dough into part-baked bread.");
        ItemStack? part = oven.Inventory[slot].Itemstack;
        Assert.NotNull(part);
        Assert.Contains("partbaked", part!.Collectible.Code.Path, StringComparison.OrdinalIgnoreCase);
        float partPay = ScenarioXp.PlannedCrafted(
            World.Api.World,
            Skill,
            cook.PlayerUID,
            part,
            quantity: 1,
            ingredients: 1,
            caller: EventFactBuilder.CodeOf(oven.Block));
        ScenarioXp.AssertPaid(TotalCookingXp(cook) - before, partPay, "part-baked loaf");

        float afterPart = TotalCookingXp(cook);
        Assert.True(
            BakeUntil(oven, slot, "perfect"),
            "Expected the oven to finish the loaf.");
        Assert.Contains("perfect", oven.Inventory[slot].Itemstack?.Collectible.Code.Path, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(afterPart, TotalCookingXp(cook));
    }

    [AtlasScenario]
    [Trait("Layer", "Pedigree")]
    [Trait("Kind", "CookQuality")]
    public async Task CooksInto_Should_StampProductImmediately_WithoutMealXp()
    {
        ITestPlayer joined = await World.JoinPlayer("CookGlue");
        IPlayer cook = joined.Player;
        GrantCookQuality(RequireProgress(cook));
        AssertServer();

        BlockEntityFirepit firepit = PlaceFirepit(cook);
        StartCooking(firepit, cook);

        Block pot = RequireBlock("game:claypot-blue-fired", "claypot-blue-fired");
        Item resin = RequireItem("game:resin", "resin");
        Item charcoal = RequireItem("game:powder-charcoal", "powder-charcoal");
        firepit.inputSlot.Itemstack = new ItemStack(pot, 1);
        firepit.inputSlot.MarkDirty();
        Assert.True(firepit.otherCookingSlots.Length >= 4, "Expected four cooking slots for glue.");
        firepit.otherCookingSlots[0].Itemstack = new ItemStack(resin, 1);
        firepit.otherCookingSlots[1].Itemstack = new ItemStack(resin, 1);
        firepit.otherCookingSlots[2].Itemstack = new ItemStack(charcoal, 1);
        firepit.otherCookingSlots[3].Itemstack = new ItemStack(charcoal, 1);
        for (int i = 0; i < 4; i++)
        {
            firepit.otherCookingSlots[i].MarkDirty();
        }

        float before = TotalCookingXp(cook);
        firepit.smeltItems();

        ItemStack? glue = FindStack(firepit, pathContains: "glueportion");
        Assert.NotNull(glue);
        Assert.Equal(cook.PlayerUID, CraftAttribution.TryGetMakerUid(glue));
        Assert.True(
            glue!.StackSize >= 1,
            "CooksInto product should be in the firepit immediately, not deferred to serve.");
        float gained = TotalCookingXp(cook) - before;
        Assert.True(
            gained < 0.001f,
            $"Non-meal CooksInto must not pay meal XP (including leftover pots). gained={gained}.");
    }

    static void AssertCookQuality(ItemStack stack, string label)
    {
        Assert.True(
            ItemAffixes.TryGetQuality(stack, out _),
            $"{label} should carry a quality grade after the cook roll.");
        Assert.True(
            CraftAttributeMods.GetFactor(stack, FreshnessAttributeMutator.KeyName) > 1.0001f,
            $"{label} freshness factor should be stamped.");
        Assert.True(
            CraftAttributeMods.GetFactor(stack, SatietyAttributeMutator.KeyName) > 1.0001f,
            $"{label} satiety factor should be stamped.");
        Assert.True(
            CraftAttributeMods.GetFactor(stack, HungerDelayAttributeMutator.KeyName) > 1.0001f,
            $"{label} hungerDelay factor should be stamped.");
    }

    static void AssertQualityCopied(ItemStack pot, ItemStack meal, string label)
    {
        Assert.True(ItemAffixes.TryGetQuality(pot, out ItemAffixEntry potGrade));
        Assert.True(ItemAffixes.TryGetQuality(meal, out ItemAffixEntry mealGrade));
        Assert.Equal(potGrade.LangKey, mealGrade.LangKey);
        Assert.Equal(
            CraftAttributeMods.GetFactor(pot, FreshnessAttributeMutator.KeyName),
            CraftAttributeMods.GetFactor(meal, FreshnessAttributeMutator.KeyName));
        Assert.Equal(
            CraftAttributeMods.GetFactor(pot, SatietyAttributeMutator.KeyName),
            CraftAttributeMods.GetFactor(meal, SatietyAttributeMutator.KeyName));
        Assert.Equal(
            CraftAttributeMods.GetFactor(pot, HungerDelayAttributeMutator.KeyName),
            CraftAttributeMods.GetFactor(meal, HungerDelayAttributeMutator.KeyName));
        _ = label;
    }

    static void AssertContributorWeight(ItemStack stack, string uid, int weight)
    {
        Assert.True(ProsequorStackPedigree.TryGetPrimaryBlob(stack, out ProsequorBlob blob));
        int found = 0;
        foreach (ProsequorBlob.Share share in blob.Contributors)
        {
            if (string.Equals(share.PlayerUid, uid, StringComparison.Ordinal))
            {
                found = share.Weight;
            }
        }

        Assert.Equal(weight, found);
    }

    static void AssertPedigree(ItemStack stack, string makerUid, string label)
    {
        Assert.Equal(makerUid, CraftAttribution.TryGetMakerUid(stack));
        AssertCookQuality(stack, label);
    }

    static void EmptyPot(
        BlockCookedContainerBase container,
        ItemSlot potSlot,
        ItemSlot vesselSlot,
        IWorldAccessor world)
    {
        Block? bowl = vesselSlot.Itemstack?.Block;
        for (int i = 0; i < 8 && MealHostCredit.StillHasMeal(potSlot.Itemstack); i++)
        {
            if (bowl != null && MealHostCredit.StillHasMeal(vesselSlot.Itemstack))
            {
                vesselSlot.Itemstack = new ItemStack(bowl, 1);
            }

            if (!container.ServeIntoStack(vesselSlot, potSlot, world))
            {
                break;
            }
        }
    }

    static void StartCooking(BlockEntityFirepit firepit, IPlayer cook)
    {
        FirepitProcessStarterStation.NoteInteractor(firepit, cook);
        FirepitProcessStarterStation.ObserveProcessing(firepit, processing: false);
        FirepitProcessStarterStation.ObserveProcessing(firepit, processing: true);
        Assert.True(ProsequorBlockPedigreeStation.TryGetSoleContributor(firepit, out string? starter));
        Assert.Equal(cook.PlayerUID, starter);
    }

    static void GrantCookQuality(IPlayerProgress progress)
    {
        progress.AddUnlockPoints(20);
        progress.SetSkillLevel(Skill, 50);
        Assert.True(progress.GrantUnlock(Skill, "cooking-expertise"));
        Assert.True(progress.GrantUnlock(Skill, "cooking-expertise"));
        Assert.True(progress.GrantUnlock(Skill, "cooking-expertise"));
        Assert.True(progress.GrantUnlock(Skill, "clean-cook"));
        Assert.True(progress.GrantUnlock(Skill, "clean-cook"));
        Assert.True(progress.GrantUnlock(Skill, "very-filling"));
        Assert.True(progress.GrantUnlock(Skill, "very-filling"));
        Assert.True(progress.GrantUnlock(Skill, "nothing-wasted"));
        Assert.True(progress.GrantUnlock(Skill, "nothing-wasted"));
        // Unlocks need 50; stay under the cap so the finish deed can still accrue.
        progress.SetSkillLevel(Skill, 49);
    }

    static float TotalCookingXp(IPlayer player)
    {
        EntityBehaviorProgress? progress = player.Entity?.GetBehavior<EntityBehaviorProgress>();
        Assert.NotNull(progress);
        var skill = progress!.State.GetOrCreateSkill(Skill);
        return progress.GetSkillXp(Skill) + skill.Accrued;
    }

    static int MealServings(ItemStack stack)
    {
        if (MealHostCredit.IsMealVessel(stack))
        {
            float servings = stack.Attributes.GetFloat("quantityServings", 0f);
            if (servings > 0.001f)
            {
                return Math.Max(1, (int)(servings + 0.001f));
            }
        }

        return Math.Max(1, stack.StackSize);
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
            string? path = stack?.Collectible?.Code?.Path;
            if (path != null && path.Contains(pathContains, StringComparison.OrdinalIgnoreCase))
            {
                return stack;
            }
        }

        return null;
    }

    void AssertServer() =>
        Assert.Equal(EnumAppSide.Server, World.Api.World.Side);

    static IPlayerProgress RequireProgress(IPlayer player)
    {
        IPlayerProgress? progress = ProsequorModSystem.GetProgress(player);
        Assert.NotNull(progress);
        return progress!;
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

    BlockEntityOven PlaceOven(IPlayer player)
    {
        IWorldAccessor world = World.Api.World;
        int offset = nextOffset;
        nextOffset += 3;
        BlockPos pos = player.Entity.Pos.AsBlockPos.AddCopy(offset, 0, 0);
        EnsureFloor(pos);
        Block oven = RequireBlock("game:clayoven-north", "clayoven-north");
        world.BlockAccessor.SetBlock(0, pos);
        world.BlockAccessor.SetBlock(oven.BlockId, pos);
        if (world.BlockAccessor.GetBlockEntity(pos) is not BlockEntityOven be)
        {
            Assert.Fail($"Expected BlockEntityOven at {pos}.");
            throw new InvalidOperationException();
        }

        return be;
    }

    static bool BakeUntil(BlockEntityOven oven, int slot, string pathContains)
    {
        HeatOven(oven);
        MethodInfo? bake = typeof(BlockEntityOven).GetMethod(
            "IncrementallyBake",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        Assert.NotNull(bake);
        ParameterInfo[] parameters = bake!.GetParameters();
        object?[] args = new object[parameters.Length];
        for (int i = 0; i < parameters.Length; i++)
        {
            Type type = parameters[i].ParameterType;
            if (type == typeof(int))
            {
                args[i] = slot;
            }
            else if (type == typeof(float))
            {
                args[i] = 5f;
            }
            else if (type == typeof(double))
            {
                args[i] = 5d;
            }
            else
            {
                args[i] = type.IsValueType ? Activator.CreateInstance(type) : null;
            }
        }

        for (int i = 0; i < 400; i++)
        {
            bake.Invoke(oven, args);
            string? path = oven.Inventory[slot].Itemstack?.Collectible?.Code?.Path;
            if (path != null && path.Contains(pathContains, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    static void HeatOven(BlockEntityOven oven)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        FieldInfo? dataField = typeof(BlockEntityOven).GetField("bakingData", flags);
        if (dataField?.GetValue(oven) is not Array data)
        {
            return;
        }

        for (int i = 0; i < data.Length; i++)
        {
            object? entry = data.GetValue(i);
            FieldInfo? temp = entry?.GetType().GetField("temp", flags);
            temp?.SetValue(entry, 800f);
        }
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
