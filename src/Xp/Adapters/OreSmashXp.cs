using HarmonyLib;
using Prosequor.Ability;
using Prosequor.Xp.Activity;
using Vintagestory.API.Common;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace Prosequor.Xp.Adapters;

/// <summary>
/// Ground-storage hammer smash (<see cref="ItemOre.OnContainedInteractStop"/>) →
/// <c>crafted</c> deed with quantity = ores processed (same token as grid nugget crafts).
/// </summary>
public static class OreSmashXp
{
    public sealed class SmashCapture
    {
        public int StackBefore;
        public string? NuggetCode;
    }

    [HarmonyPatch(typeof(ItemOre), nameof(ItemOre.OnContainedInteractStop))]
    public static class ItemOreContainedInteractStopXpPatch
    {
        [HarmonyPrefix]
        public static void Prefix(
            ItemSlot slot,
            IPlayer byPlayer,
            out SmashCapture? __state)
        {
            __state = null;
            if (byPlayer == null || slot?.Itemstack?.Collectible == null)
            {
                return;
            }

            if (!TryResolveNuggetCode(slot.Itemstack, out string? nuggetCode)
                || string.IsNullOrWhiteSpace(nuggetCode))
            {
                return;
            }

            __state = new SmashCapture
            {
                StackBefore = Math.Max(0, slot.Itemstack.StackSize),
                NuggetCode = nuggetCode
            };
        }

        [HarmonyPostfix]
        public static void Postfix(
            float secondsUsed,
            ItemSlot slot,
            IPlayer byPlayer,
            SmashCapture? __state)
        {
            if (__state == null
                || byPlayer?.PlayerUID == null
                || secondsUsed < 1.95f
                || __state.StackBefore <= 0
                || string.IsNullOrWhiteSpace(__state.NuggetCode))
            {
                return;
            }

            ICoreAPI? api = byPlayer.Entity?.World?.Api ?? slot?.Inventory?.Api;
            if (api?.Side != EnumAppSide.Server || api.World == null)
            {
                return;
            }

            if (api.World.PlayerByUid(byPlayer.PlayerUID) is not IServerPlayer serverPlayer)
            {
                return;
            }

            int stackAfter = slot?.Itemstack == null ? 0 : Math.Max(0, slot.Itemstack.StackSize);
            int oresProcessed = __state.StackBefore - stackAfter;
            if (oresProcessed <= 0)
            {
                return;
            }

            // Vanilla caps a smash at 4 ores; clamp in case of unexpected stack churn.
            oresProcessed = Math.Min(oresProcessed, 4);

            string? tool = EventFactBuilder.HeldCode(serverPlayer);
            string caller = string.IsNullOrWhiteSpace(tool) ? CallerIdentities.Hand : tool;

            api.Logger.VerboseDebug(
                "[prosequor] deed crafted smash {0} ores={1} caller={2} by {3}",
                __state.NuggetCode,
                oresProcessed,
                caller,
                serverPlayer.PlayerName);

            Deed.Emit(
                api,
                serverPlayer.PlayerUID,
                DeedToken.Crafted,
                caller: caller,
                target: __state.NuggetCode,
                lastCraft: EventFactBuilder.LastCraftCode(serverPlayer),
                craftCount: oresProcessed,
                quantityUnits: [new Deed.QuantityUnit(__state.NuggetCode!, oresProcessed)]);
        }
    }

    /// <summary>
    /// Mirrors vanilla smash nugget naming: variant ore key with quartz_/galena_ stripped.
    /// </summary>
    public static bool TryResolveNuggetCode(ItemStack? oreStack, out string? nuggetCode)
    {
        nuggetCode = null;
        if (oreStack?.Collectible?.Code == null)
        {
            return false;
        }

        string? oreKey = null;
        if (oreStack.Collectible.Variant != null
            && oreStack.Collectible.Variant.TryGetValue("ore", out string? fromVariant))
        {
            oreKey = fromVariant;
        }

        if (string.IsNullOrWhiteSpace(oreKey))
        {
            return false;
        }

        oreKey = oreKey
            .Replace("quartz_", "", StringComparison.OrdinalIgnoreCase)
            .Replace("galena_", "", StringComparison.OrdinalIgnoreCase);

        if (string.IsNullOrWhiteSpace(oreKey))
        {
            return false;
        }

        string domain = oreStack.Collectible.Code.Domain ?? "game";
        nuggetCode = $"{domain}:nugget-{oreKey}";
        return true;
    }
}
