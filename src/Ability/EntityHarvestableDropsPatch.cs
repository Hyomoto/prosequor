using HarmonyLib;
using Prosequor.Ability.Hooks;
using Prosequor.Xp.Adapters;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.GameContent;

namespace Prosequor.Ability;

/// <summary>
/// Knife / rip harvest: run entity-interaction mutate-drops on the carcass
/// inventory after vanilla <see cref="EntityBehaviorHarvestable.GenerateDrops"/>.
/// </summary>
[HarmonyPatch(typeof(EntityBehaviorHarvestable), nameof(EntityBehaviorHarvestable.GenerateDrops))]
public static class EntityHarvestableDropsPatch
{
    [HarmonyPrefix]
    public static void Prefix(EntityBehaviorHarvestable __instance, out bool __state) =>
        __state = __instance.DropsGenerated;

    [HarmonyPostfix]
    public static void Postfix(
        EntityBehaviorHarvestable __instance,
        IPlayer byPlayer,
        bool __state)
    {
        if (__state || !__instance.DropsGenerated)
        {
            return;
        }

        Entity? entity = __instance.entity;
        IWorldAccessor? world = entity?.World;
        if (world?.Side != EnumAppSide.Server || byPlayer == null || entity == null)
        {
            return;
        }

        InventoryBase? inv = __instance.Inventory;
        if (inv == null)
        {
            return;
        }

        ItemStack[] current = ReadSlots(inv);
        AbilityAction fact = DropsFactBuilder.ForEntity(byPlayer, entity);
        ItemStack[] next = DropsStation.Run(
            world,
            byPlayer,
            current,
            fact,
            hook: HookIds.EntityInteraction);

        WriteSlots(inv, next);
        SyncHarvestableInv(entity, inv);
        ButcherXp.NotifyHarvest(byPlayer, entity, next);
    }

    static ItemStack[] ReadSlots(InventoryBase inv)
    {
        List<ItemStack> list = new(inv.Count);
        for (int i = 0; i < inv.Count; i++)
        {
            ItemStack? stack = inv[i]?.Itemstack;
            if (stack != null && stack.StackSize > 0)
            {
                list.Add(stack);
            }
        }

        return list.ToArray();
    }

    static void WriteSlots(InventoryBase inv, ItemStack[] drops)
    {
        if (drops.Length > inv.Count && inv is InventoryGeneric generic)
        {
            generic.AddSlots(drops.Length - inv.Count);
        }

        for (int i = 0; i < inv.Count; i++)
        {
            ItemSlot? slot = inv[i];
            if (slot == null)
            {
                continue;
            }

            slot.Itemstack = i < drops.Length ? drops[i] : null;
            slot.MarkDirty();
        }
    }

    static void SyncHarvestableInv(Entity entity, InventoryBase inv)
    {
        TreeAttribute tree = new();
        inv.ToTreeAttributes(tree);
        entity.WatchedAttributes["harvestableInv"] = tree;
        entity.WatchedAttributes.MarkPathDirty("harvestableInv");
    }
}
