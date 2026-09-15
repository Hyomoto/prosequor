using Prosequor.Xp.Activity;
using Vintagestory.API.Common;
using Vintagestory.GameContent;

namespace Prosequor.Ability;

/// <summary>
/// Cook-complete quality stamp: resolves the firepit/oven process starter and runs
/// crafting-interaction apply-quality onto the finished stack.
/// </summary>
public static class CookQualityStation
{
    [ThreadStatic]
    static string? pendingVesselMaker;

    [ThreadStatic]
    static int pendingIngredientUnits;

    [ThreadStatic]
    static int pendingSmeltInputUnits;

    public static string? TryResolveFirepitCookerUid(BlockEntityFirepit? firepit)
    {
        if (firepit == null)
        {
            return null;
        }

        if (ProsequorBlockPedigreeStation.TryGetSoleContributor(firepit, out string? uid)
            && !string.IsNullOrWhiteSpace(uid))
        {
            return uid.Trim();
        }

        return null;
    }

    /// <summary>
    /// Snapshot the input pot's maker and cooking-slot ingredient units before
    /// <c>smeltItems</c> replaces those stacks.
    /// </summary>
    public static void NoteInput(BlockEntityFirepit? firepit)
    {
        ItemStack? input = firepit?.inputSlot?.Itemstack;
        pendingVesselMaker = CraftAttribution.TryGetMakerUid(input);
        pendingIngredientUnits = SumCookingIngredients(firepit);
        // Raw smelt input is the ingredient. A pot in the input slot is the vessel, not an ingredient.
        pendingSmeltInputUnits = input != null && !MealHostCredit.IsMealVessel(input)
            ? Math.Max(0, input.StackSize)
            : 0;
    }

    /// <summary>Snapshot the input pot's maker before <c>smeltItems</c> replaces the stack.</summary>
    public static void NoteInputVesselMaker(ItemStack? input) =>
        pendingVesselMaker = CraftAttribution.TryGetMakerUid(input);

    /// <summary>
    /// Stamp quality onto a cook product. Meal hosts keep a vessel maker (never the cook)
    /// and record the cooker as a contributor. Other products still take the cooker as maker.
    /// </summary>
    public static bool TryStamp(IWorldAccessor world, string? cookerUid, ItemStack? stack)
    {
        if (world?.Side != EnumAppSide.Server
            || string.IsNullOrWhiteSpace(cookerUid)
            || stack?.Collectible == null)
        {
            return false;
        }

        bool mealHost = MealHostCredit.IsMealVessel(stack);
        CraftMutateOutputStation.ApplyAttributes(world, cookerUid, stack, stampMaker: !mealHost);
        if (mealHost)
        {
            MealHostCredit.AttachToStack(stack, ProsequorBlob.Empty, pendingVesselMaker, world, cookerUid);
        }

        return true;
    }

    /// <summary>
    /// After firepit <c>smeltItems</c>: stamp output, cooking-slot products, and CooksInto leftovers.
    /// </summary>
    public static void OnAfterFirepitSmelt(BlockEntityFirepit firepit)
    {
        if (firepit?.Api?.World?.Side != EnumAppSide.Server)
        {
            ClearPending();
            return;
        }

        string? cookerUid = TryResolveFirepitCookerUid(firepit);
        if (string.IsNullOrWhiteSpace(cookerUid))
        {
            ClearPending();
            return;
        }

        IWorldAccessor world = firepit.Api.World;
        // Output is the meal pot or SmeltedStack. Only a meal host gets cooking-pot;
        // a leftover dirty pot is the same code and must not.
        ItemStack? output = firepit.outputSlot?.Itemstack;
        TryStampAndPay(world, cookerUid, output, cookingPot: MealHostCredit.IsMealVessel(output));
        TryStampAndPay(world, cookerUid, firepit.inputSlot?.Itemstack);
        InventoryBase? inv = firepit.Inventory;
        if (inv == null)
        {
            ClearPending();
            return;
        }

        // InventorySmelting: 0 fuel, 1 input, 2 output, 3+ cooking (CooksInto products).
        for (int i = 3; i < inv.Count; i++)
        {
            TryStampAndPay(world, cookerUid, inv[i]?.Itemstack);
        }

        ClearPending();
    }

    /// <summary>
    /// After an oven slot finishes baking into a new stack.
    /// </summary>
    public static void OnAfterOvenBake(BlockEntityOven oven, ItemStack? baked)
    {
        if (oven?.Api?.World?.Side != EnumAppSide.Server || baked?.Collectible == null)
        {
            ClearPending();
            return;
        }

        string? cookerUid = OvenCookStarterStation.TryGetLastInteractor(oven);
        if (string.IsNullOrWhiteSpace(cookerUid))
        {
            ClearPending();
            return;
        }

        try
        {
            TryStampAndPay(oven.Api.World, cookerUid, baked);
        }
        finally
        {
            ClearPending();
        }
    }

    /// <summary>
    /// Pre-bake maker, used by the oven patch because bake replaces the slot stack
    /// before <see cref="OnAfterOvenBake"/>.
    /// </summary>
    public static void NoteBakeVesselMaker(string? makerUid)
    {
        pendingVesselMaker = string.IsNullOrWhiteSpace(makerUid) ? null : makerUid.Trim();
    }

    static int SumCookingIngredients(BlockEntityFirepit? firepit)
    {
        ItemSlot[]? slots = firepit?.otherCookingSlots;
        if (slots == null || slots.Length == 0)
        {
            return 0;
        }

        int total = 0;
        for (int i = 0; i < slots.Length; i++)
        {
            ItemStack? stack = slots[i]?.Itemstack;
            if (stack != null)
            {
                total += Math.Max(0, stack.StackSize);
            }
        }

        return total;
    }

    static void ClearPending()
    {
        pendingVesselMaker = null;
        pendingIngredientUnits = 0;
        pendingSmeltInputUnits = 0;
    }

    static void TryStampAndPay(IWorldAccessor world, string cookerUid, ItemStack? stack, bool cookingPot = false)
    {
        if (!TryStamp(world, cookerUid, stack) || stack == null)
        {
            return;
        }

        EmitCrafted(world, cookerUid, stack, cookingPot);
    }

    /// <summary>
    /// Cooking XP rules listen for <c>crafted</c>. Quantity is meals produced (servings, else
    /// output stack size). Ingredients is cooking-slot units, or the raw smelt input when
    /// there is no pot. A meal-host output also carries <c>cooking-pot</c>; leftover pots do not.
    /// </summary>
    static void EmitCrafted(IWorldAccessor world, string cookerUid, ItemStack stack, bool cookingPot)
    {
        int quantity = MealsProduced(stack);
        int ingredients = pendingIngredientUnits > 0 ? pendingIngredientUnits : pendingSmeltInputUnits;
        IReadOnlyList<string>? extra = cookingPot ? [DeedTokenTags.CookingPot] : null;
        CraftedProductXp.Emit(world, cookerUid, stack, quantity, ingredients, extraTokens: extra);
    }

    static int MealsProduced(ItemStack stack)
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
}
