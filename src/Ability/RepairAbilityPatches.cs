using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.GameContent;

namespace Prosequor.Ability;

/// <summary>
/// Thin adapters into <see cref="RepairStation"/> for clothing condition merges and armor craft repairs.
/// </summary>
[HarmonyPatch]
public static class RepairAbilityPatches
{
    /// <summary>
    /// Clothing drop-repair: multiply <c>clothingRepairStrength</c> before applying condition.
    /// Reimplements the vanilla DirectMerge repair branch so wear/decay via <c>ChangeCondition</c> stays untouched.
    /// </summary>
    [HarmonyPrefix]
    [HarmonyPatch(typeof(CollectibleBehaviorWearable), nameof(CollectibleBehaviorWearable.TryMergeStacks))]
    public static bool TryMergeStacksPrefix(
        CollectibleBehaviorWearable __instance,
        ItemStackMergeOperation op,
        ref EnumHandling handling)
    {
        if (op.CurrentPriority != EnumMergePriority.DirectMerge)
        {
            return true;
        }

        ItemStack? source = op.SourceSlot?.Itemstack;
        ItemStack? sink = op.SinkSlot?.Itemstack;
        if (source == null || sink == null)
        {
            return true;
        }

        JsonObject? attrs = source.ItemAttributes;
        float strength = attrs?["clothingRepairStrength"].AsFloat(0f) ?? 0f;
        if (strength <= 0f)
        {
            return true;
        }

        if (sink.Attributes.GetFloat("condition", 0f) >= 1f)
        {
            return true;
        }

        IPlayer? player = op.ActingPlayer ?? RepairStation.TryResolvePlayerFromSlot(op.SinkSlot);
        if (player != null
            && (player.Entity?.World?.Side == EnumAppSide.Server
                || player.Entity?.Api?.Side == EnumAppSide.Server))
        {
            float mult = RepairStation.ResolveAddDurability(player, sink);
            strength *= mult;
        }

        __instance.ChangeCondition(op.SinkSlot, strength);
        op.MovedQuantity = 1;
        op.SourceSlot!.TakeOut(1);
        handling = EnumHandling.PreventDefault;
        return false;
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(CollectibleBehaviorWearable), nameof(CollectibleBehaviorWearable.CalculateRepairValue))]
    public static void CalculateRepairValuePostfix(
        ItemSlot[] inSlots,
        ItemSlot outputSlot,
        ref float repairValue,
        ref int matCostPerMatType)
    {
        _ = inSlots;
        _ = matCostPerMatType;

        ItemStack? stack = outputSlot?.Itemstack;
        if (stack == null)
        {
            return;
        }

        IPlayer? player = RepairStation.TryResolvePlayerFromSlot(outputSlot);
        if (player?.Entity == null)
        {
            return;
        }

        if (player.Entity.World?.Side != EnumAppSide.Server
            && player.Entity.Api?.Side != EnumAppSide.Server)
        {
            return;
        }

        float mult = RepairStation.ResolveAddDurability(player, stack);
        repairValue *= mult;
    }
}
