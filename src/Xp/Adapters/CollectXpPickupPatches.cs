using HarmonyLib;
using Prosequor.Ability;
using Prosequor.Xp.Activity;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.Common;

namespace Prosequor.Xp.Adapters;

/// <summary>
/// Universal pickup gate for collect-XP. Most game gives go through
/// <see cref="PlayerInventoryManager.TryGiveItemstack"/> (henbox, block pickups);
/// fishing-style paths use <see cref="EntityPlayer.TryGiveItemStack"/>.
/// Only the moved quantity is enqueued; leftover stamped units keep the stamp.
/// </summary>
public static class CollectXpPickupPatches
{
    public sealed class Capture
    {
        public bool HadStamp;
        public string? Code;
        public int BeforeQty;
        public string? PlayerUid;
    }

    static Capture? CaptureIfStamped(IPlayer? player, ItemStack? itemstack)
    {
        if (player?.Entity?.World?.Side != EnumAppSide.Server
            || itemstack == null
            || !CollectXpStamp.Has(itemstack))
        {
            return null;
        }

        string? code = EventFactBuilder.CodeOf(itemstack);
        int qty = Math.Max(0, itemstack.StackSize);
        if (string.IsNullOrWhiteSpace(code) || qty <= 0)
        {
            return null;
        }

        string? uid = player.PlayerUID;
        if (string.IsNullOrWhiteSpace(uid))
        {
            return null;
        }

        return new Capture
        {
            HadStamp = true,
            Code = code,
            BeforeQty = qty,
            PlayerUid = uid
        };
    }

    static void Finish(ICoreAPI? api, ItemStack? itemstack, bool success, Capture? state)
    {
        if (state is not { HadStamp: true }
            || string.IsNullOrWhiteSpace(state.Code)
            || string.IsNullOrWhiteSpace(state.PlayerUid)
            || !success)
        {
            return;
        }

        int after = itemstack == null ? 0 : Math.Max(0, itemstack.StackSize);
        int moved = state.BeforeQty - after;
        if (moved <= 0)
        {
            return;
        }

        // Leftover units keep the stamp; fully consumed source is gone.
        if (after <= 0 && itemstack != null)
        {
            CollectXpStamp.Clear(itemstack);
        }

        ProsequorModSystem.For(api)?.ActivityWatch?.CollectXp
            .Enqueue(state.PlayerUid, state.Code, moved);
    }

    [HarmonyPatch(typeof(PlayerInventoryManager), nameof(PlayerInventoryManager.TryGiveItemstack))]
    public static class PlayerInventoryManagerTryGiveItemstackCollectXpPatch
    {
        [HarmonyPrefix]
        public static void Prefix(
            PlayerInventoryManager __instance,
            ItemStack itemstack,
            ref Capture? __state) =>
            __state = CaptureIfStamped(__instance?.player, itemstack);

        [HarmonyPostfix]
        [HarmonyPriority(Priority.Last)]
        public static void Postfix(
            PlayerInventoryManager __instance,
            ItemStack itemstack,
            bool __result,
            Capture? __state) =>
            Finish(__instance?.player?.Entity?.World?.Api, itemstack, __result, __state);
    }

    [HarmonyPatch(typeof(EntityPlayer), nameof(EntityPlayer.TryGiveItemStack))]
    public static class EntityPlayerTryGiveItemStackCollectXpPatch
    {
        [HarmonyPrefix]
        public static void Prefix(EntityPlayer __instance, ItemStack itemstack, ref Capture? __state) =>
            __state = CaptureIfStamped(__instance?.Player, itemstack);

        [HarmonyPostfix]
        public static void Postfix(
            EntityPlayer __instance,
            ItemStack itemstack,
            bool __result,
            Capture? __state) =>
            Finish(__instance?.World?.Api, itemstack, __result, __state);
    }
}
