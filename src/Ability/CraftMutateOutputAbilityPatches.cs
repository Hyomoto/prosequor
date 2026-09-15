using HarmonyLib;
using Prosequor.Xp;
using Prosequor.Xp.Adapters;
using Vintagestory.API.Common;
using Vintagestory.Common;

namespace Prosequor.Ability;

/// <summary>
/// Snapshots crafting-grid ingredients, stamps last-craft, and awards craft XP on successful take.
/// Craft count comes from <see cref="InventoryCraftingGrid.ConsumeIngredients"/> calls during the
/// take — vanilla shift multi-craft (<c>CraftMany</c>) does not return total moved quantity.
/// </summary>
[HarmonyPatch(typeof(ItemSlotCraftingOutput), nameof(ItemSlotCraftingOutput.TryPutInto))]
public static class CraftMutateOutputAbilityPatches
{
    [ThreadStatic]
    static ItemStack? takeOutput;

    [HarmonyPrefix]
    public static void TryPutIntoPrefix(ItemSlotCraftingOutput __instance)
    {
        takeOutput = null;
        CraftTakeScope.Begin();

        InventoryBase? inv = __instance?.Inventory;
        IWorldAccessor? world = inv?.Api?.World;
        if (world == null || world.Side != EnumAppSide.Server)
        {
            CraftGridScope.Push(Array.Empty<ItemStack>());
            return;
        }

        CraftGridScope.Push(CraftGridScope.CaptureIngredients(inv));

        IPlayer? player = null;
        if (inv is InventoryBasePlayer playerInv)
        {
            player = playerInv.Player;
        }

        ItemStack? output = null;
        if (inv != null && inv.Count > 0)
        {
            output = inv[inv.Count - 1]?.Itemstack;
        }

        takeOutput = output?.Clone();
        LastCraftStation.Remember(player, output);
    }

    [HarmonyPostfix]
    public static void TryPutIntoPostfix(
        ItemSlotCraftingOutput __instance,
        ItemSlot sinkSlot,
        int __result)
    {
        try
        {
            InventoryBase? inv = __instance?.Inventory;
            IWorldAccessor? world = inv?.Api?.World;
            if (world == null || world.Side != EnumAppSide.Server)
            {
                return;
            }

            IPlayer? player = null;
            if (inv is InventoryBasePlayer playerInv)
            {
                player = playerInv.Player;
            }

            if (player == null || takeOutput == null)
            {
                return;
            }

            // Prefer consume count: CraftMany returns only the last iteration's MovedQuantity.
            int craftCount = CraftTakeScope.ConsumeCount;
            if (craftCount <= 0)
            {
                // No full recipe completion (e.g. leftovers-only take) → no XP / yield.
                return;
            }

            if (__result > 0 && sinkSlot != null)
            {
                CraftMutateOutputStation.ApplyTakeYield(player, sinkSlot, takeOutput, craftCount);
            }

            CraftXpAdapter? adapter = ProsequorModSystem.For(world.Api)?.CraftXp;
            if (adapter == null)
            {
                return;
            }

            int beforeUnits = 0;
            if (CraftGridScope.TryGet(out IReadOnlyList<ItemStack> before))
            {
                beforeUnits = CraftXpAdapter.SumTotalUnits(before);
            }

            int afterUnits = CraftXpAdapter.SumIngredientUnitsInGrid(inv!);
            int consumed = Math.Max(0, beforeUnits - afterUnits);
            int unitsPerCraft = XpGrantFormulas.UnitsPerCraft(consumed, craftCount);

            adapter.NotifyTake(player, takeOutput, unitsPerCraft, craftCount);
        }
        finally
        {
            takeOutput = null;
        }
    }

    [HarmonyFinalizer]
    public static void TryPutIntoFinalizer()
    {
        takeOutput = null;
        CraftTakeScope.End();
        CraftGridScope.Pop();
    }
}

/// <summary>
/// Marks craft consume for item-interaction craft verb, counts multi-craft completions, then runs refund.
/// </summary>
[HarmonyPatch(typeof(InventoryCraftingGrid), "ConsumeIngredients")]
public static class CraftConsumeIngredientsScopePatch
{
    [HarmonyPrefix]
    public static void Prefix() => CraftConsumeScope.Push();

    [HarmonyPostfix]
    public static void Postfix(InventoryCraftingGrid __instance)
    {
        CraftTakeScope.NoteConsume();

        if (__instance?.Api?.World?.Side != EnumAppSide.Server)
        {
            return;
        }

        IPlayer? player = null;
        if (__instance is InventoryBasePlayer playerInv)
        {
            player = playerInv.Player;
        }

        if (player == null)
        {
            return;
        }

        CraftMutateOutputStation.TryRefundIngredients(player, __instance);
    }

    [HarmonyFinalizer]
    public static void Finalizer() => CraftConsumeScope.Pop();
}
