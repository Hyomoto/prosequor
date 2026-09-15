using Newtonsoft.Json.Linq;
using Prosequor.Ability.Hooks;
using Prosequor.Data;
using Vintagestory.API.Common;

namespace Prosequor.Ability.Actions;

public sealed class AddAffixParams
{
    public required string Code { get; init; }
    public required string Lang { get; init; }
    public string? Color { get; init; }
}

/// <summary>
/// Stamps a display affix onto the crafted stack (lang + optional color; no baked markup).
/// Params: inline <c>{ code, lang, color? }</c> or shared-list <c>{ list, item }</c>
/// (<c>item</c> = 0-based index or entry <c>code</c>). List form resolves at compile time.
/// </summary>
public sealed class AddAffixMutateOutputAction
    : AbilityActionHandler<CraftMutateOutputContext, ItemStack, AddAffixParams>
{
    readonly AffixListRegistry affixLists;

    public AddAffixMutateOutputAction(AffixListRegistry affixLists) =>
        this.affixLists = affixLists;

    public override ActionId Id => ActionIds.AddAffix;
    public override HookId Hook => HookIds.CraftingInteraction;
    public override VerbId Verb => VerbIds.MutateOutput;
    public override PhaseId Phase => HookIds.Attributes;

    protected override bool TryParse(JObject? raw, out AddAffixParams? parameters, out string error)
    {
        parameters = null;
        string? list = raw?.Value<string>("list")?.Trim();
        if (!string.IsNullOrWhiteSpace(list))
        {
            if (!affixLists.TryResolve(list, raw?["item"], out AffixListEntryDef entry, out error))
            {
                return false;
            }

            parameters = new AddAffixParams
            {
                Code = entry.Code,
                Lang = entry.Lang,
                Color = entry.Color
            };
            error = "";
            return true;
        }

        string? code = raw?.Value<string>("code")?.Trim();
        string? lang = raw?.Value<string>("lang")?.Trim();
        if (string.IsNullOrWhiteSpace(code))
        {
            error = "code is required (or use list + item).";
            return false;
        }

        if (string.IsNullOrWhiteSpace(lang))
        {
            error = "lang is required (or use list + item).";
            return false;
        }

        string? color = raw?.Value<string>("color")?.Trim();
        if (string.IsNullOrWhiteSpace(color))
        {
            color = null;
        }

        parameters = new AddAffixParams { Code = code, Lang = lang, Color = color };
        error = "";
        return true;
    }

    protected override ItemStack Apply(
        CraftMutateOutputContext context,
        ItemStack value,
        AddAffixParams parameters,
        AbilityRuleSource source)
    {
        _ = context;
        _ = source;
        if (value == null)
        {
            return value!;
        }

        ItemAffixes.Add(value, parameters.Code, parameters.Lang, parameters.Color);
        return value;
    }
}
