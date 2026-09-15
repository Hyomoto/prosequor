using Prosequor.Ability.Hooks;
using Prosequor.Player;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;

namespace Prosequor.Ability;

/// <summary>
/// Shared item-interaction durability station. Adapters stamp facts and apply the returned amount;
/// they do not decide keep/lose themselves.
/// </summary>
public static class ItemDurabilityStation
{
    public static int RunAmount(ItemDurabilityContext context, VerbId verb, int amount)
    {
        if (amount <= 0 || context.World?.Api == null)
        {
            return amount;
        }

        ProsequorModSystem? mod = ProsequorModSystem.For(context.World.Api);
        if (mod?.Pipeline == null)
        {
            return amount;
        }

        return mod.Pipeline.Run(HookIds.ItemInteraction, verb, HookIds.Amount, context, amount);
    }

    public static VerbId ResolveVerb(bool craftConsume, bool blockBreak) =>
        craftConsume
            ? VerbIds.CraftDamaged
            : blockBreak
                ? VerbIds.BlockDamaged
                : VerbIds.ItemDamage;
}
