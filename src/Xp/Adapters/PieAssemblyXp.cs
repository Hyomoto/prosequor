using HarmonyLib;
using Prosequor.Ability;
using Prosequor.Xp.Activity;
using Vintagestory.API.Common;
using Vintagestory.GameContent;

namespace Prosequor.Xp.Adapters;

/// <summary>
/// Pie assembly in the world. Each add pays for the stack size that actually left the
/// player's slot. Cycling the top crust, and any add that takes nothing, pays nothing.
/// </summary>
public static class PieAssemblyXp
{
    public static void PayRemoved(
        IWorldAccessor? world,
        IPlayer? player,
        Block? pie,
        string? ingredientCode,
        int beforeSize,
        ItemSlot? slot)
    {
        if (world?.Side != EnumAppSide.Server
            || string.IsNullOrWhiteSpace(player?.PlayerUID)
            || pie == null)
        {
            return;
        }

        int taken = UnitsRemoved(
            ingredientCode,
            beforeSize,
            EventFactBuilder.CodeOf(slot?.Itemstack),
            slot?.Itemstack?.StackSize ?? 0);
        if (taken <= 0)
        {
            return;
        }

        string? caller = EventFactBuilder.CodeOf(pie);
        if (string.IsNullOrWhiteSpace(caller))
        {
            return;
        }

        Deed.Emit(
            world.Api,
            player.PlayerUID,
            DeedToken.Crafted,
            caller: caller,
            target: ingredientCode,
            inputs: [new Deed.QuantityUnit(ingredientCode!, taken)]);
    }

    /// <summary>
    /// Units that left the slot. An emptied or replaced slot counts the whole before-size.
    /// </summary>
    public static int UnitsRemoved(string? ingredientCode, int beforeSize, string? afterCode, int afterSize)
    {
        if (beforeSize <= 0 || string.IsNullOrWhiteSpace(ingredientCode))
        {
            return 0;
        }

        if (string.IsNullOrWhiteSpace(afterCode)
            || !string.Equals(afterCode, ingredientCode, StringComparison.OrdinalIgnoreCase))
        {
            return beforeSize;
        }

        return Math.Max(0, beforeSize - Math.Max(0, afterSize));
    }
}

/// <summary>Shell placement and later fills both remove from the held slot.</summary>
public static class PieAssemblyPatches
{
    public sealed class SlotBefore
    {
        public string? IngredientCode;
        public int BeforeSize;
        public ItemSlot? Slot;
    }

    [HarmonyPatch(typeof(BlockEntityPie), nameof(BlockEntityPie.OnPlaced))]
    public static class PieOnPlacedAssemblyPatch
    {
        [HarmonyPrefix]
        public static void Prefix(IPlayer? byPlayer, out SlotBefore __state)
        {
            ItemSlot? slot = byPlayer?.InventoryManager?.ActiveHotbarSlot;
            __state = Snap(slot);
        }

        [HarmonyPostfix]
        public static void Postfix(BlockEntityPie __instance, IPlayer? byPlayer, SlotBefore __state) =>
            PieAssemblyXp.PayRemoved(
                __instance?.Api?.World,
                byPlayer,
                __instance?.Block,
                __state.IngredientCode,
                __state.BeforeSize,
                __state.Slot);
    }

    [HarmonyPatch(typeof(BlockEntityPie), "TryAddIngredientFrom")]
    public static class PieTryAddIngredientAssemblyPatch
    {
        [HarmonyPrefix]
        public static void Prefix(ItemSlot slot, out SlotBefore __state) =>
            __state = Snap(slot);

        [HarmonyPostfix]
        public static void Postfix(BlockEntityPie __instance, IPlayer? byPlayer, SlotBefore __state) =>
            PieAssemblyXp.PayRemoved(
                __instance?.Api?.World,
                byPlayer,
                __instance?.Block,
                __state.IngredientCode,
                __state.BeforeSize,
                __state.Slot);
    }

    static SlotBefore Snap(ItemSlot? slot) =>
        new()
        {
            Slot = slot,
            IngredientCode = EventFactBuilder.CodeOf(slot?.Itemstack),
            BeforeSize = Math.Max(0, slot?.Itemstack?.StackSize ?? 0),
        };
}
