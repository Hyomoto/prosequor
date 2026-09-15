using System.Collections.Concurrent;
using Vintagestory.API.Common;
using Vintagestory.GameContent;

namespace Prosequor.Ability;

/// <summary>
/// Sticky per-player last craft-grid product fact (updated on take Prefix, before consume).
/// </summary>
public static class LastCraftStation
{
    public const string VerbCraft = CraftMutateOutputStation.VerbCraft;

    static readonly ConcurrentDictionary<string, AbilityAction> byPlayerUid =
        new(StringComparer.Ordinal);

    public static void Remember(IPlayer? player, ItemStack? output)
    {
        if (player == null || string.IsNullOrWhiteSpace(player.PlayerUID) || output == null)
        {
            return;
        }

        AbilityAction fact = EventFactBuilder.Build(
            VerbCraft,
            player.PlayerUID,
            target: EventFactBuilder.CodeOf(output));

        Remember(player.PlayerUID, fact);
    }

    /// <summary>Test / adapter helper to stamp a last-craft fact by UID.</summary>
    public static void Remember(string playerUid, AbilityAction fact)
    {
        if (string.IsNullOrWhiteSpace(playerUid) || fact == null)
        {
            return;
        }

        byPlayerUid[playerUid] = fact;
    }

    public static bool TryGet(string? playerUid, out AbilityAction fact)
    {
        if (!string.IsNullOrWhiteSpace(playerUid)
            && byPlayerUid.TryGetValue(playerUid, out AbilityAction? found)
            && found != null)
        {
            fact = found;
            return true;
        }

        fact = null!;
        return false;
    }

    public static bool TryGet(IPlayer? player, out AbilityAction fact) =>
        TryGet(player?.PlayerUID, out fact);

    /// <summary>
    /// Tags the craft product as clothing or armor for last-craft matching.
    /// Linen/cloth bolts and non-wearables return no tags.
    /// </summary>
    public static HashSet<string> ClassifyProductTags(ItemStack? stack)
    {
        HashSet<string> tags = new(StringComparer.OrdinalIgnoreCase);
        if (stack?.Collectible == null)
        {
            return tags;
        }

        CollectibleBehaviorWearable? wearable =
            stack.Collectible.GetCollectibleBehavior<CollectibleBehaviorWearable>(false);
        if (wearable != null)
        {
            DummySlot slot = new(stack);
            if (wearable.IsArmorType(slot)
                || stack.ItemAttributes?["protectionModifiers"] != null)
            {
                tags.Add(RepairStation.TagArmor);
            }
            else
            {
                tags.Add(RepairStation.TagClothing);
            }

            return tags;
        }

        return ClassifyProductTags(stack.Collectible.Code?.Path);
    }

    /// <summary>Path heuristics for fixtures (no live wearable behavior).</summary>
    public static HashSet<string> ClassifyProductTags(string? path)
    {
        HashSet<string> tags = new(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrEmpty(path))
        {
            return tags;
        }

        if (path.Contains("armor", StringComparison.OrdinalIgnoreCase))
        {
            tags.Add(RepairStation.TagArmor);
            return tags;
        }

        if (path.StartsWith("clothes-", StringComparison.OrdinalIgnoreCase)
            || path.Contains("clothing", StringComparison.OrdinalIgnoreCase))
        {
            tags.Add(RepairStation.TagClothing);
        }

        return tags;
    }
}
