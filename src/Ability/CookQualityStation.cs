using Prosequor.Xp.Activity;
using Vintagestory.API.Common;
using Vintagestory.GameContent;

namespace Prosequor.Ability;

/// <summary>
/// Cook-complete quality stamp and one crafted deed for the batch this cycle finished.
/// The caller is the firepit block, the oven block, or the pot item that went in.
/// </summary>
public static class CookQualityStation
{
    [ThreadStatic]
    static string? pendingVesselMaker;

    [ThreadStatic]
    static FirepitBefore? pendingFirepit;

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
    /// Snapshot firepit slots before <c>smeltItems</c> replaces them.
    /// </summary>
    public static void NoteInput(BlockEntityFirepit? firepit)
    {
        ItemStack? input = firepit?.inputSlot?.Itemstack;
        pendingVesselMaker = CraftAttribution.TryGetMakerUid(input);
        if (firepit == null)
        {
            pendingFirepit = null;
            return;
        }

        // Capture the slot objects now. After a pot finishes, the input is empty and
        // otherCookingSlots hides these same slots, which is where the ingredients were.
        ItemSlot[] liveCooking = firepit.otherCookingSlots ?? [];
        ItemSlot[] heldCooking = new ItemSlot[liveCooking.Length];
        SlotSnap[] slots = new SlotSnap[liveCooking.Length];
        for (int i = 0; i < liveCooking.Length; i++)
        {
            heldCooking[i] = liveCooking[i];
            slots[i] = SlotSnap.Of(liveCooking[i]?.Itemstack);
        }

        pendingFirepit = new FirepitBefore
        {
            Input = SlotSnap.Of(input),
            InputIsVessel = IsCookingVessel(input),
            Output = SlotSnap.Of(firepit.outputSlot?.Itemstack),
            Cooking = slots,
            CookingSlots = heldCooking,
        };
    }

    /// <summary>
    /// Stamp quality onto a cook product. Meal hosts keep a vessel maker (never the cook)
    /// and record the cooker as a contributor. Other products still take the cooker as maker.
    /// </summary>
    public static bool TryStamp(IWorldAccessor world, string? cookerUid, ItemStack? stack, string? caller)
    {
        if (world?.Side != EnumAppSide.Server
            || string.IsNullOrWhiteSpace(cookerUid)
            || stack?.Collectible == null)
        {
            return false;
        }

        bool mealHost = MealHostCredit.IsMealVessel(stack);
        CraftMutateOutputStation.ApplyAttributes(
            world,
            cookerUid,
            stack,
            stampMaker: !mealHost,
            caller: caller);
        if (mealHost)
        {
            MealHostCredit.AttachToStack(stack, ProsequorBlob.Empty, pendingVesselMaker, world, cookerUid);
        }

        return true;
    }

    /// <summary>
    /// After firepit <c>smeltItems</c>: stamp and pay only the stack this cycle created or grew.
    /// </summary>
    public static void OnAfterFirepitSmelt(BlockEntityFirepit firepit)
    {
        FirepitBefore? before = pendingFirepit;
        if (firepit?.Api?.World?.Side != EnumAppSide.Server || before == null)
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

        try
        {
            PayFirepit(firepit, before, cookerUid);
        }
        finally
        {
            ClearPending();
        }
    }

    /// <summary>
    /// After an oven slot's item code changes. Quantity is the stack that came out.
    /// Ingredients are the stack that went in.
    /// </summary>
    public static void OnAfterOvenBake(BlockEntityOven oven, ItemStack? baked, int beforeSize)
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
            string? caller = EventFactBuilder.CodeOf(oven.Block);
            int quantity = Math.Max(0, baked.StackSize);
            TryStamp(oven.Api.World, cookerUid, baked, caller);
            CraftedProductXp.Emit(
                oven.Api.World,
                cookerUid,
                baked,
                quantity,
                caller,
                ingredients: Math.Max(0, beforeSize));
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

    static void PayFirepit(BlockEntityFirepit firepit, FirepitBefore before, string cookerUid)
    {
        IWorldAccessor world = firepit.Api.World;
        string? caller = before.InputIsVessel
            ? before.Input.Code
            : EventFactBuilder.CodeOf(firepit.Block);
        int consumedAside = 0;
        ItemSlot[] cooking = before.CookingSlots;
        int cookingCount = Math.Min(before.Cooking.Length, cooking.Length);
        for (int i = 0; i < cookingCount; i++)
        {
            ItemStack? after = cooking[i]?.Itemstack;
            SlotSnap now = SlotSnap.Of(after);
            SlotSnap was = before.Cooking[i];
            if (was.SameCode(now))
            {
                consumedAside += Math.Max(0, was.Size - now.Size);
                continue;
            }

            if (now.Code == null)
            {
                consumedAside += was.Size;
                continue;
            }

            PayCreated(world, cookerUid, caller, after, now.Size, was.Size);
        }

        ItemStack? output = firepit.outputSlot?.Itemstack;
        SlotSnap outputNow = SlotSnap.Of(output);
        if (outputNow.Code == null || output == null)
        {
            return;
        }

        bool created = !before.Output.SameCode(outputNow);
        int grown = created ? outputNow.Size : Math.Max(0, outputNow.Size - before.Output.Size);
        if (grown <= 0)
        {
            return;
        }

        int quantity = created && MealHostCredit.IsMealVessel(output)
            ? Servings(output)
            : grown;
        int ingredients = before.InputIsVessel
            ? consumedAside
            : UnitsLost(before.Input, firepit.inputSlot?.Itemstack);
        if (created)
        {
            TryStamp(world, cookerUid, output, caller);
        }

        CraftedProductXp.Emit(world, cookerUid, output, quantity, caller, ingredients);
    }

    static void PayCreated(
        IWorldAccessor world,
        string cookerUid,
        string? caller,
        ItemStack? stack,
        int quantity,
        int ingredients)
    {
        if (stack == null || quantity <= 0)
        {
            return;
        }

        TryStamp(world, cookerUid, stack, caller);
        CraftedProductXp.Emit(world, cookerUid, stack, quantity, caller, ingredients);
    }

    static bool IsCookingVessel(ItemStack? stack) =>
        stack?.Collectible is BlockCookingContainer || MealHostCredit.IsMealVessel(stack);

    static int UnitsLost(SlotSnap before, ItemStack? after)
    {
        if (before.Size <= 0)
        {
            return 0;
        }

        SlotSnap now = SlotSnap.Of(after);
        if (now.Code == null || !before.SameCode(now))
        {
            return before.Size;
        }

        return Math.Max(0, before.Size - now.Size);
    }

    static int Servings(ItemStack stack)
    {
        float servings = stack.Attributes.GetFloat("quantityServings", 0f);
        if (servings > 0.001f)
        {
            return Math.Max(1, (int)(servings + 0.001f));
        }

        return Math.Max(1, stack.StackSize);
    }

    static void ClearPending()
    {
        pendingVesselMaker = null;
        pendingFirepit = null;
    }

    sealed class FirepitBefore
    {
        public SlotSnap Input;
        public bool InputIsVessel;
        public SlotSnap Output;
        public SlotSnap[] Cooking = [];
        public ItemSlot[] CookingSlots = [];
    }

    readonly struct SlotSnap
    {
        public SlotSnap(string? code, int size)
        {
            Code = string.IsNullOrWhiteSpace(code) ? null : code;
            Size = Code == null ? 0 : Math.Max(0, size);
        }

        public string? Code { get; }

        public int Size { get; }

        public static SlotSnap Of(ItemStack? stack) =>
            new(EventFactBuilder.CodeOf(stack), stack?.StackSize ?? 0);

        public bool SameCode(SlotSnap other) =>
            string.Equals(Code, other.Code, StringComparison.OrdinalIgnoreCase);
    }
}
