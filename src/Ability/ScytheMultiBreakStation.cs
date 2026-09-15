using Prosequor.Ability.Hooks;
using Prosequor.Player;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;

namespace Prosequor.Ability;

/// <summary>
/// Resolves scythe multi-break quantity for Field Expertise. Uses thread-local
/// active quantity because <c>MultiBreakQuantity</c> is read without a player argument.
/// </summary>
public static class ScytheMultiBreakStation
{
    [ThreadStatic]
    static int activeQuantity;

    [ThreadStatic]
    static bool isActive;

    public static void Begin(Entity? byEntity, int baseQuantity)
    {
        isActive = false;
        activeQuantity = baseQuantity;
        if (byEntity is not EntityPlayer entityPlayer || entityPlayer.Player == null)
        {
            return;
        }

        int resolved = Run(entityPlayer.Player, baseQuantity);
        if (resolved == baseQuantity)
        {
            return;
        }

        activeQuantity = Math.Max(1, resolved);
        isActive = true;
    }

    public static void End()
    {
        isActive = false;
        activeQuantity = 0;
    }

    public static bool TryGetActive(ref int quantity)
    {
        if (!isActive)
        {
            return false;
        }

        quantity = activeQuantity;
        return true;
    }

    public static int Run(IPlayer player, int baseQuantity)
    {
        if (player?.Entity == null || baseQuantity <= 0)
        {
            return baseQuantity;
        }

        ProsequorModSystem? mod = ProsequorModSystem.For(player.Entity.Api);
        if (mod?.Pipeline == null)
        {
            return baseQuantity;
        }

        IPlayerProgress? progress = ProsequorModSystem.GetProgress(player);
        if (progress == null)
        {
            return baseQuantity;
        }

        AbilityAction fact = EventFactBuilder.ForPlayer(
            player,
            AbilityBootstrap.VerbScytheMultibreak);

        ScytheMultiBreakContext context = new()
        {
            Player = player,
            Progress = progress,
            Fact = fact,
            BaseValue = baseQuantity
        };

        return Math.Max(1, mod.Pipeline.Run(HookIds.BlockInteraction, VerbIds.ScytheMultibreak, HookIds.Quantity, context, baseQuantity));
    }
}
