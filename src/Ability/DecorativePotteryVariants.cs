using Prosequor.Ability.Hooks;
using Vintagestory.API.Common;

namespace Prosequor.Ability;

/// <summary>
/// Registers decorative pottery replacements into <see cref="CollectibleVariantTable"/>
/// under <c>{base}-decorative</c> keys (storagevessel, flowerpot, clayplanter).
/// </summary>
public static class DecorativePotteryVariants
{
    public const string Family = "decorative";

    public static readonly string[] Bases =
    [
        AbilityBootstrap.StorageVesselTag,
        AbilityBootstrap.FlowerpotTag,
        AbilityBootstrap.ClayPlanterTag
    ];

    public static void Populate(ICoreAPI api, CollectibleVariantTable table)
    {
        if (api?.World == null || table == null)
        {
            return;
        }

        foreach (Item item in api.World.Items)
        {
            Consider(item, table);
        }

        foreach (Block block in api.World.Blocks)
        {
            Consider(block, table);
        }

        api.Logger.Notification(
            "[prosequor] Decorative pottery variants: storagevessel {0}, flowerpot {1}, clayplanter {2}.",
            table.Count(CollectibleVariantTable.Key(AbilityBootstrap.StorageVesselTag, Family)),
            table.Count(CollectibleVariantTable.Key(AbilityBootstrap.FlowerpotTag, Family)),
            table.Count(CollectibleVariantTable.Key(AbilityBootstrap.ClayPlanterTag, Family)));
    }

    static void Consider(CollectibleObject? collectible, CollectibleVariantTable table)
    {
        if (collectible?.Code == null || collectible.Id == 0)
        {
            return;
        }

        string path = collectible.Code.Path;
        if (path.EndsWith("raw", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith("fired", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        foreach (string bas in Bases)
        {
            if (!path.Contains(bas, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            table.Add(CollectibleVariantTable.Key(bas, Family), collectible);
        }
    }
}
