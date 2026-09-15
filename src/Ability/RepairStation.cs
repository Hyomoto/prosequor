using Prosequor.Ability.Hooks;
using Prosequor.Player;
using Vintagestory.API.Common;

namespace Prosequor.Ability;

/// <summary>
/// Resolves item-interaction <c>repair</c> / <c>add-durability</c> multipliers for clothing and armor repairs.
/// </summary>
public static class RepairStation
{
    public const string VerbRepair = "prosequor:repair";
    public const string TagClothing = "clothing";
    public const string TagArmor = "armor";

    /// <summary>
    /// Returns a multiplier (seed 1) for the restored repair amount.
    /// Collection membership (clothing/armor) is resolved at match time.
    /// </summary>
    public static float ResolveAddDurability(IPlayer player, ItemStack? repaired)
    {
        if (player?.Entity == null || repaired?.Collectible == null)
        {
            return 1f;
        }

        ProsequorModSystem? mod = ProsequorModSystem.For(player.Entity.Api);
        if (mod?.Pipeline == null)
        {
            return 1f;
        }

        IPlayerProgress? progress = ProsequorModSystem.GetProgress(player);
        if (progress == null)
        {
            return 1f;
        }

        AbilityAction fact = EventFactBuilder.ForPlayer(
            player,
            VerbRepair,
            target: EventFactBuilder.CodeOf(repaired));

        RepairContext context = new()
        {
            Player = player,
            Progress = progress,
            Fact = fact,
            Repaired = repaired
        };

        return Math.Max(0f, mod.Pipeline.Run(HookIds.ItemInteraction, VerbIds.Repair, HookIds.AddDurability, context, 1f));
    }

    public static IPlayer? TryResolvePlayerFromSlot(ItemSlot? slot)
    {
        if (slot?.Inventory is InventoryBasePlayer playerInv)
        {
            return playerInv.Player;
        }

        return null;
    }
}
