using HarmonyLib;
using Prosequor.Ability;
using Prosequor.Xp.Activity;
using Vintagestory.API.Common;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace Prosequor.Xp.Adapters;

/// <summary>
/// Successful quench or temper on a carried quenchable item: one flat deed each.
/// Pays only when vanilla increments <c>quenchIteration</c> / <c>temperIteration</c>.
/// </summary>
public static class HeatTreatXp
{
    public sealed class Iterations
    {
        public int Quench;
        public int Temper;
    }

    /// <summary>
    /// After <see cref="CollectibleBehaviorQuenchable"/> settles a work item.
    /// One emit per iteration that actually increased.
    /// </summary>
    public static void OnSettled(IWorldAccessor? world, ItemStack? itemstack, Iterations? before)
    {
        if (world?.Api == null
            || world.Side != EnumAppSide.Server
            || itemstack?.Attributes == null
            || before == null)
        {
            return;
        }

        int quench = itemstack.Attributes.GetInt("quenchIteration", 0);
        int temper = itemstack.Attributes.GetInt("temperIteration", 0);
        if (quench > before.Quench)
        {
            Emit(world, itemstack, DeedToken.Quenched);
        }

        if (temper > before.Temper)
        {
            Emit(world, itemstack, DeedToken.Tempered);
        }
    }

    static void Emit(IWorldAccessor world, ItemStack itemstack, DeedToken token)
    {
        IServerPlayer? player = FindCarrier(world, itemstack);
        if (player?.PlayerUID == null)
        {
            return;
        }

        world.Api.Logger.VerboseDebug(
            "[prosequor] deed {0} {1} by {2}",
            token.ToTag(),
            EventFactBuilder.CodeOf(itemstack),
            player.PlayerName);

        Deed.Emit(
            world.Api,
            player.PlayerUID,
            token,
            caller: EventFactBuilder.CallerOrHand(player),
            target: EventFactBuilder.CodeOf(itemstack));
    }

    /// <summary>
    /// Online player who has this stack in an own inventory, else the first
    /// player who has it in an opened inventory.
    /// </summary>
    public static IServerPlayer? FindCarrier(IWorldAccessor world, ItemStack stack)
    {
        IPlayer[]? players = world.AllOnlinePlayers;
        if (players == null || players.Length == 0)
        {
            return null;
        }

        IServerPlayer? opened = null;
        for (int p = 0; p < players.Length; p++)
        {
            if (players[p] is not IServerPlayer server
                || string.IsNullOrEmpty(server.PlayerUID)
                || server.InventoryManager?.Inventories == null)
            {
                continue;
            }

            foreach (IInventory inv in server.InventoryManager.Inventories.Values)
            {
                if (inv == null || !ContainsStack(inv, stack))
                {
                    continue;
                }

                if (inv is InventoryBasePlayer owned
                    && owned.Player?.PlayerUID != null
                    && owned.Player.PlayerUID == server.PlayerUID)
                {
                    return server;
                }

                opened ??= server;
            }
        }

        return opened;
    }

    static bool ContainsStack(IInventory inv, ItemStack stack)
    {
        // Unopened creative inventory throws from Count. A heat-treat settle must not die on it.
        int count;
        try
        {
            count = inv.Count;
        }
        catch (NullReferenceException)
        {
            return false;
        }

        for (int i = 0; i < count; i++)
        {
            ItemSlot? slot;
            try
            {
                slot = inv[i];
            }
            catch (NullReferenceException)
            {
                continue;
            }

            if (ReferenceEquals(slot?.Itemstack, stack))
            {
                return true;
            }
        }

        return false;
    }

    [HarmonyPatch(typeof(CollectibleBehaviorQuenchable), "trySettleWorkItem")]
    public static class QuenchableSettleXpPatch
    {
        [HarmonyPrefix]
        public static void Prefix(ItemStack itemstack, out Iterations __state)
        {
            __state = new Iterations
            {
                Quench = itemstack?.Attributes?.GetInt("quenchIteration", 0) ?? 0,
                Temper = itemstack?.Attributes?.GetInt("temperIteration", 0) ?? 0
            };
        }

        [HarmonyPostfix]
        public static void Postfix(IWorldAccessor world, ItemStack itemstack, Iterations __state)
        {
            OnSettled(world, itemstack, __state);
        }
    }
}
