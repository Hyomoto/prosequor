using Vintagestory.API.Common;

namespace Prosequor.Ability;

/// <summary>
/// Wild forage sources for the Forager passive. Path checks so worldgen and
/// player-placed copies share one classification.
/// </summary>
public static class ForageBlocks
{
    public static bool IsMushroom(Block? block) =>
        StartsWith(block, "mushroom");

    /// <summary>
    /// Tule, cattail (coopers reed), papyrus, and horsetail. Not brownsedge
    /// or other flowers. Horsetail is <c>BlockPlant</c> and uses the normal
    /// <see cref="Block.OnBlockBroken"/> XP postfix (not <c>BlockReeds</c>
    /// or RightClickPickup).
    /// </summary>
    public static bool IsReed(Block? block)
    {
        string? path = block?.Code?.Path;
        if (string.IsNullOrEmpty(path) || block!.Id == 0)
        {
            return false;
        }

        return path.StartsWith("tallplant-tule-", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("tallplant-coopersreed-", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("tallplant-papyrus-", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("flower-horsetail-", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>World-spawned stick blocks, not ground-storage piles.</summary>
    public static bool IsLooseStick(Block? block) =>
        StartsWith(block, "loosestick");

    /// <summary>
    /// Dripping resin logs (<c>log-resin-*</c>), not the harvested cooldown
    /// block. Scoop uses <c>BlockBehaviorHarvestable</c>; do not classify as
    /// harvest on break or axe-felling pays Forager instead of chop.
    /// </summary>
    public static bool IsSap(Block? block)
    {
        if (block == null || block.Id == 0 || block.Code == null)
        {
            return false;
        }

        return string.Equals(block.FirstCodePart(), "log", StringComparison.OrdinalIgnoreCase)
            && string.Equals(block.FirstCodePart(1), "resin", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>0.01 × drop quantity (mushrooms). Berries and crops stay on harvest XP.</summary>
    public static bool IsQuantityForage(Block? block) => IsMushroom(block);

    /// <summary>0.05 flat per harvest (reeds / sticks / sap scoop).</summary>
    public static bool IsFlatForage(Block? block) =>
        IsReed(block) || IsLooseStick(block) || IsSap(block);

    /// <summary>Blocks a player place should mark so later harvests are not wild.</summary>
    public static bool IsPlayerPlaceableForage(Block? block) =>
        IsMushroom(block) || IsReed(block) || IsLooseStick(block) || IsSap(block);

    public static bool IsDropBonusTarget(Block? block) =>
        block != null
        && (IsMushroom(block)
            || IsReed(block)
            || IsLooseStick(block)
            || IsSap(block)
            || AbilityBootstrap.IsCropBlock(block)
            || AbilityBootstrap.IsBerryBushBlock(block));

    static bool StartsWith(Block? block, string prefix)
    {
        if (block == null || block.Id == 0)
        {
            return false;
        }

        string? path = block.Code?.Path;
        return path != null && path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }
}
