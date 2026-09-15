using Newtonsoft.Json.Linq;
using Prosequor.Ability.Hooks;
using Vintagestory.API.Common;

namespace Prosequor.Ability.Actions;

/// <summary>
/// Graded metal ore path helpers: <c>ore-{grade}-…</c> / <c>crystalizedore-{grade}-…</c>
/// with grade ∈ poor → medium → rich → bountiful.
/// </summary>
public static class OreGradeUpgrade
{
    public static readonly string[] Grades = ["poor", "medium", "rich", "bountiful"];

    public static bool IsGradedOrePath(string? path) =>
        TrySplitPath(path, out _, out _, out _);

    public static bool TrySplitPath(
        string? path,
        out string kind,
        out string grade,
        out string remainder)
    {
        kind = "";
        grade = "";
        remainder = "";
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        string p = path.Trim();
        string? kindPrefix = null;
        if (p.StartsWith("crystalizedore-", StringComparison.OrdinalIgnoreCase))
        {
            kindPrefix = "crystalizedore";
        }
        else if (p.StartsWith("ore-", StringComparison.OrdinalIgnoreCase))
        {
            kindPrefix = "ore";
        }

        if (kindPrefix == null)
        {
            return false;
        }

        string after = p[(kindPrefix.Length + 1)..];
        int dash = after.IndexOf('-');
        if (dash <= 0 || dash >= after.Length - 1)
        {
            return false;
        }

        string g = after[..dash];
        if (!IsKnownGrade(g))
        {
            return false;
        }

        kind = kindPrefix;
        grade = g.ToLowerInvariant();
        remainder = after[(dash + 1)..];
        return true;
    }

    public static bool TryNextGrade(string grade, out string next)
    {
        next = "";
        int i = IndexOfGrade(grade);
        if (i < 0 || i >= Grades.Length - 1)
        {
            return false;
        }

        next = Grades[i + 1];
        return true;
    }

    public static bool TryNextGradeCode(string? code, out string nextCode)
    {
        nextCode = "";
        if (string.IsNullOrWhiteSpace(code))
        {
            return false;
        }

        string trimmed = code.Trim();
        int colon = trimmed.IndexOf(':');
        string domain;
        string path;
        if (colon <= 0)
        {
            domain = "game";
            path = trimmed;
        }
        else
        {
            domain = trimmed[..colon];
            path = trimmed[(colon + 1)..];
        }

        if (!TrySplitPath(path, out string kind, out string grade, out string remainder)
            || !TryNextGrade(grade, out string nextGrade))
        {
            return false;
        }

        nextCode = $"{domain}:{kind}-{nextGrade}-{remainder}";
        return true;
    }

    static bool IsKnownGrade(string grade)
    {
        for (int i = 0; i < Grades.Length; i++)
        {
            if (string.Equals(Grades[i], grade, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    static int IndexOfGrade(string grade)
    {
        for (int i = 0; i < Grades.Length; i++)
        {
            if (string.Equals(Grades[i], grade, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return -1;
    }
}

/// <summary>
/// Upgrades one unit of this stack to the next ore grade when it exists.
/// Size &gt; 1 is left unchanged (caller should split if needed); size 1 is replaced.
/// </summary>
public sealed class UpgradeOreGradeOnDropsStackAction
    : AbilityActionHandler<DropsContext, ItemStack, object>
{
    public override ActionId Id => ActionIds.UpgradeOreGrade;
    public override HookId Hook => HookIds.BlockInteraction;
    public override VerbId Verb => VerbIds.MutateDrops;
    public override PhaseId Phase => HookIds.Stack;

    protected override bool TryParse(JObject? raw, out object? parameters, out string error)
    {
        parameters = new object();
        error = "";
        return true;
    }

    protected override ItemStack Apply(
        DropsContext context,
        ItemStack value,
        object parameters,
        AbilityRuleSource source)
    {
        _ = source;
        if (value == null || value.StackSize <= 0 || context.World == null)
        {
            return value!;
        }

        if (!OreGradeUpgrade.TryNextGradeCode(value.Collectible?.Code?.ToString(), out string nextCode))
        {
            return value;
        }

        Item? nextItem = context.World.GetItem(new AssetLocation(nextCode));
        if (nextItem == null)
        {
            return value;
        }

        if (value.StackSize > 1)
        {
            // Upgrade one unit: shrink this stack; leftover unit becomes upgraded via clone path.
            // Station is per-stack; split by returning upgraded size-1 and dropping remainder
            // is not representable as ItemStack→ItemStack. Upgrade whole stack when size>1
            // only if size==1 preferred; for size>1 convert one by replacing with upgraded
            // size 1 (remainder lost unless list phase). Prefer: convert entire stack codes.
            ItemStack upgraded = new(nextItem, value.StackSize);
            upgraded.ResolveBlockOrItem(context.World);
            return upgraded;
        }

        ItemStack single = new(nextItem, 1);
        single.ResolveBlockOrItem(context.World);
        return single;
    }
}

/// <summary>Replaces this stack with one unit of the broken block.</summary>
public sealed class ReplaceMatchingStackWithBlockStackAction
    : AbilityActionHandler<DropsContext, ItemStack, object>
{
    public override ActionId Id => ActionIds.ReplaceMatchingStackWithBlock;
    public override HookId Hook => HookIds.BlockInteraction;
    public override VerbId Verb => VerbIds.MutateDrops;
    public override PhaseId Phase => HookIds.Stack;

    protected override bool TryParse(JObject? raw, out object? parameters, out string error)
    {
        parameters = new object();
        error = "";
        return true;
    }

    protected override ItemStack Apply(
        DropsContext context,
        ItemStack value,
        object parameters,
        AbilityRuleSource source)
    {
        _ = source;
        Block? block = context.Block;
        if (block == null || block.Id == 0 || value == null)
        {
            return value!;
        }

        return new ItemStack(block, 1);
    }
}
