using Vintagestory.API.Common;
using Vintagestory.GameContent;

namespace Prosequor.Ability;

/// <summary>
/// Fishing catch classification for XP facts. Size is inferred from fillet yield
/// (<see cref="CollectibleBehaviorGroundStoredProcessable.ProcessedStacks"/> quantity avg);
/// unknown / unreadable fish default to <see cref="SmallTag"/>.
/// </summary>
public static class FishClassification
{
    public const string VerbFish = "prosequor:fish";

    /// <summary>Non-fish items received during an scoped fishing catch (junk, treasure, etc.).</summary>
    public const string VerbCatch = "prosequor:catch";

    /// <summary>Target tag stamped on fish catch facts; required by fish-only drop rules.</summary>
    public const string FishTag = "fish";

    public const string SmallTag = "small-fish";
    public const string MediumTag = "medium-fish";
    public const string LargeTag = "large-fish";

    public const string FreshwaterTag = "freshwater";
    public const string SaltwaterTag = "saltwater";
    public const string ReefTag = "reef";

    public const string AdultTag = "adult";
    public const string JuvenileTag = "juvenile";

    /// <summary>True for dead catch items (<c>fishraw-*</c>) and living fish items (<c>creature-fish-*</c>).</summary>
    public static bool IsFishItem(CollectibleObject? collectible)
    {
        string? path = collectible?.Code?.Path;
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        return path.StartsWith("fishraw-", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("creature-fish-", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsFishItem(ItemStack? stack) => IsFishItem(stack?.Collectible);

    /// <summary>True when an asset code path is a fish catch item (with or without domain).</summary>
    public static bool IsFishCode(string? code)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return false;
        }

        string path = code;
        int colon = code.IndexOf(':');
        if (colon >= 0 && colon + 1 < code.Length)
        {
            path = code[(colon + 1)..];
        }

        return path.StartsWith("fishraw-", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("creature-fish-", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Adds fish + size + habitat + age tags for fish items.</summary>
    public static void AddContextualTags(ItemStack? stack, HashSet<string> tags)
    {
        if (stack?.Collectible?.Code == null || !IsFishItem(stack))
        {
            return;
        }

        tags.Add(FishTag);
        string path = stack.Collectible.Code.Path;
        AddHabitatAndAgeTags(path, tags);
        tags.Add(ClassifySize(stack));
    }

    public static string ClassifySize(ItemStack? stack)
    {
        if (stack?.Collectible == null || !IsFishItem(stack))
        {
            return SmallTag;
        }

        float? avg = TryFilletQuantityAvg(stack.Collectible);
        if (avg == null)
        {
            avg = TryCookSizeHint(stack.Collectible);
        }

        if (avg == null)
        {
            return SmallTag;
        }

        if (avg.Value <= 2f)
        {
            return SmallTag;
        }

        if (avg.Value <= 5f)
        {
            return MediumTag;
        }

        return LargeTag;
    }

    static float? TryFilletQuantityAvg(CollectibleObject collectible)
    {
        CollectibleBehaviorGroundStoredProcessable? processable =
            collectible.GetCollectibleBehavior<CollectibleBehaviorGroundStoredProcessable>(
                withInheritance: true);
        BlockDropItemStack[]? stacks = processable?.ProcessedStacks;
        if (stacks == null || stacks.Length == 0 || stacks[0]?.Quantity == null)
        {
            return null;
        }

        float avg = stacks[0].Quantity.avg;
        return avg > 0f ? avg : null;
    }

    /// <summary>
    /// Fallback from whole-fish cook output: <c>fishchunk-*-{2|4|7|9}-*</c> digit, or tiny → 1.
    /// </summary>
    static float? TryCookSizeHint(CollectibleObject collectible)
    {
        JsonItemStack? smelted = collectible.CombustibleProps?.SmeltedStack;
        string? path = smelted?.Code?.Path;
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        if (path.StartsWith("fish-cooked", StringComparison.OrdinalIgnoreCase)
            || string.Equals(path, "fish-cooked", StringComparison.OrdinalIgnoreCase))
        {
            return 1f;
        }

        if (!path.StartsWith("fishchunk-", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        string[] parts = path.Split('-');
        foreach (string part in parts)
        {
            if (part is "2" or "4" or "7" or "9"
                && float.TryParse(part, out float size))
            {
                return size;
            }
        }

        return null;
    }

    static void AddHabitatAndAgeTags(string path, HashSet<string> tags)
    {
        // fishraw-{habitat}-… or creature-fish-{habitat}-…
        string rest = path;
        if (rest.StartsWith("fishraw-", StringComparison.OrdinalIgnoreCase))
        {
            rest = rest["fishraw-".Length..];
        }
        else if (rest.StartsWith("creature-fish-", StringComparison.OrdinalIgnoreCase))
        {
            rest = rest["creature-fish-".Length..];
        }
        else
        {
            return;
        }

        string[] parts = rest.Split('-', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            return;
        }

        string habitat = parts[0];
        if (habitat.Equals("freshwater", StringComparison.OrdinalIgnoreCase))
        {
            tags.Add(FreshwaterTag);
        }
        else if (habitat.Equals("saltwater", StringComparison.OrdinalIgnoreCase))
        {
            tags.Add(SaltwaterTag);
        }
        else if (habitat.Equals("reef", StringComparison.OrdinalIgnoreCase))
        {
            tags.Add(ReefTag);
        }

        string age = parts[^1];
        if (age.Equals("adult", StringComparison.OrdinalIgnoreCase))
        {
            tags.Add(AdultTag);
        }
        else if (age.Equals("juvenile", StringComparison.OrdinalIgnoreCase))
        {
            tags.Add(JuvenileTag);
        }
    }
}
