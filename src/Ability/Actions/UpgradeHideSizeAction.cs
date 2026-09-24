using Newtonsoft.Json.Linq;
using Prosequor.Ability.Hooks;
using Vintagestory.API.Common;

namespace Prosequor.Ability.Actions;

/// <summary>
/// Hide size ladder: <c>hide-{process}-{size}</c> with size ∈ small → medium → large → huge.
/// Species pelts and already-huge hides are left alone.
/// </summary>
public static class HideSizeUpgrade
{
    public static readonly string[] Sizes = ["small", "medium", "large", "huge"];

    public static bool TrySplitPath(string? path, out string process, out string size)
    {
        process = "";
        size = "";
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        string p = path.Trim();
        if (!p.StartsWith("hide-", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        string after = p["hide-".Length..];
        int lastDash = after.LastIndexOf('-');
        if (lastDash <= 0 || lastDash >= after.Length - 1)
        {
            return false;
        }

        string maybeSize = after[(lastDash + 1)..];
        if (!IsKnownSize(maybeSize))
        {
            return false;
        }

        process = after[..lastDash];
        size = maybeSize.ToLowerInvariant();
        // Species pelts use hide-raw-{species}-… / hide-*-complete — reject when process
        // itself still embeds a size token or species-specific tail beyond the ladder.
        return process.IndexOf('-') < 0
            || process is "raw" or "soaked" or "scraped" or "prepared" or "oiled"
                or "pelt" or "salted" or "chromium";
    }

    public static bool TryNextSize(string size, out string next)
    {
        next = "";
        int i = IndexOfSize(size);
        if (i < 0 || i >= Sizes.Length - 1)
        {
            return false;
        }

        next = Sizes[i + 1];
        return true;
    }

    public static bool TryNextSizeCode(string? code, out string nextCode)
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

        if (!TrySplitPath(path, out string process, out string size)
            || !TryNextSize(size, out string nextSize))
        {
            return false;
        }

        nextCode = $"{domain}:hide-{process}-{nextSize}";
        return true;
    }

    static bool IsKnownSize(string size)
    {
        for (int i = 0; i < Sizes.Length; i++)
        {
            if (string.Equals(Sizes[i], size, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    static int IndexOfSize(string size)
    {
        for (int i = 0; i < Sizes.Length; i++)
        {
            if (string.Equals(Sizes[i], size, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return -1;
    }
}

/// <summary>
/// Upgrades a sized hide drop one step on the entity-interaction mutate-drops stack phase.
/// </summary>
public sealed class UpgradeHideSizeOnDropsStackAction
    : AbilityActionHandler<DropsContext, ItemStack, object>
{
    public override ActionId Id => ActionIds.UpgradeHideSize;
    public override HookId Hook => HookIds.EntityInteraction;
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

        if (!HideSizeUpgrade.TryNextSizeCode(value.Collectible?.Code?.ToString(), out string nextCode))
        {
            return value;
        }

        Item? nextItem = context.World.GetItem(new AssetLocation(nextCode));
        if (nextItem == null)
        {
            return value;
        }

        ItemStack upgraded = new(nextItem, value.StackSize);
        upgraded.ResolveBlockOrItem(context.World);
        return upgraded;
    }
}

/// <summary>Chance gate for entity-interaction mutate-drops / stack (Skilled Skinning).</summary>
public sealed class ChanceEntityDropsStackAction
    : AbilityActionHandler<DropsContext, ItemStack, ChanceGateParams>
{
    readonly IAbilityActionRegistry actions;

    public ChanceEntityDropsStackAction(IAbilityActionRegistry actions) => this.actions = actions;

    public override ActionId Id => ActionIds.Chance;
    public override HookId Hook => HookIds.EntityInteraction;
    public override VerbId Verb => VerbIds.MutateDrops;
    public override PhaseId Phase => HookIds.Stack;

    protected override bool TryParse(JObject? raw, out ChanceGateParams? parameters, out string error) =>
        ChanceGate.TryParseParams(raw, actions, Hook, Verb, Phase, out parameters, out error);

    protected override ItemStack Apply(
        DropsContext context,
        ItemStack value,
        ChanceGateParams parameters,
        AbilityRuleSource source) =>
        ChanceGate.Apply(context, value, parameters, source);
}
