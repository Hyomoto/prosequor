using HarmonyLib;
using Prosequor.Ability;
using Vintagestory.API.Common;
using Vintagestory.GameContent;

namespace Prosequor.Xp.Adapters;

/// <summary>
/// Ground-storage process complete (<see cref="CollectibleBehaviorGroundStoredProcessable"/>)
/// → <c>crafted</c> deed for the remaining item (hide scrape, parchment trim, …).
/// Barrel soak/prepare and grid scrape already emit through existing craft adapters.
/// </summary>
public static class GroundProcessXp
{
    public sealed class ProcessCapture
    {
        public float ProcessTime;
        public string? InputCode;
        public int InputSize;
        public ItemStack? Remaining;
    }

    [HarmonyPatch(
        typeof(CollectibleBehaviorGroundStoredProcessable),
        nameof(CollectibleBehaviorGroundStoredProcessable.OnContainedInteractStop))]
    public static class GroundStoredProcessableStopXpPatch
    {
        [HarmonyPrefix]
        public static void Prefix(
            CollectibleBehaviorGroundStoredProcessable __instance,
            ItemSlot slot,
            IPlayer byPlayer,
            out ProcessCapture? __state)
        {
            __state = null;
            if (byPlayer == null || slot?.Itemstack?.Collectible == null)
            {
                return;
            }

            ItemStack? remaining = __instance.RemainingItem?.ResolvedItemstack?.Clone();
            if (remaining?.Collectible == null)
            {
                return;
            }

            __state = new ProcessCapture
            {
                ProcessTime = __instance.ProcessTime,
                InputCode = EventFactBuilder.CodeOf(slot.Itemstack),
                InputSize = Math.Max(0, slot.Itemstack.StackSize),
                Remaining = remaining
            };
        }

        [HarmonyPostfix]
        public static void Postfix(
            float secondsUsed,
            BlockEntityContainer be,
            ItemSlot slot,
            IPlayer byPlayer,
            ProcessCapture? __state)
        {
            if (__state?.Remaining == null || byPlayer == null)
            {
                return;
            }

            IWorldAccessor? world = be?.Api?.World ?? byPlayer.Entity?.World;
            if (world?.Side != EnumAppSide.Server
                || secondsUsed <= __state.ProcessTime - 0.05f)
            {
                return;
            }

            string? afterCode = EventFactBuilder.CodeOf(slot?.Itemstack);
            int afterSize = slot?.Itemstack == null ? 0 : Math.Max(0, slot.Itemstack.StackSize);
            bool replaced = !string.Equals(afterCode, __state.InputCode, StringComparison.OrdinalIgnoreCase);
            bool consumed = afterSize < __state.InputSize;
            if (!replaced && !consumed)
            {
                return;
            }

            int count = Math.Max(1, __state.Remaining.StackSize);
            CraftedProductXp.Emit(world, byPlayer.PlayerUID, __state.Remaining, count);
        }
    }
}
