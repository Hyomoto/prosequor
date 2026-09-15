using Prosequor.Xp.Activity;
using Vintagestory.API.Common;

namespace Prosequor.Ability;

/// <summary>
/// <c>crafted</c> deed for a finished product. <paramref name="quantity"/> is units produced
/// (meal servings, fermented portions, drops). <paramref name="ingredients"/> is units consumed
/// when the caller knows them; omit or pass 0 to leave that channel unpublished.
/// </summary>
public static class CraftedProductXp
{
    public static void Emit(
        IWorldAccessor? world,
        string? actorUid,
        ItemStack? stack,
        int quantity,
        int ingredients = 0,
        IReadOnlyList<Deed.ContributorShare>? contributors = null,
        string? makerUid = null,
        IReadOnlyList<string>? extraTokens = null)
    {
        if (world?.Side != EnumAppSide.Server
            || string.IsNullOrWhiteSpace(actorUid)
            || stack?.Collectible == null
            || quantity <= 0)
        {
            return;
        }

        string? target = EventFactBuilder.CodeOf(stack);
        if (string.IsNullOrWhiteSpace(target))
        {
            return;
        }

        List<string> tokens = [DeedToken.Crafted.ToTag()];
        if (extraTokens != null)
        {
            for (int i = 0; i < extraTokens.Count; i++)
            {
                string? extra = extraTokens[i];
                if (!string.IsNullOrWhiteSpace(extra))
                {
                    tokens.Add(extra.Trim());
                }
            }
        }

        Deed.Emit(
            world.Api,
            actorUid.Trim(),
            tokens,
            caller: CallerIdentities.Grid,
            target: target,
            totalUnits: Math.Max(0, ingredients),
            craftCount: quantity,
            quantityUnits: [new Deed.QuantityUnit(target, quantity)],
            contributors: contributors,
            makerUid: makerUid);
    }
}
