using Newtonsoft.Json.Linq;
using Prosequor.Ability.Hooks;
using Prosequor.Data;
using Vintagestory.API.Common;

namespace Prosequor.Ability.Actions;

public sealed class ApplyQualityParams
{
    public required string Key { get; init; }
    public required string Op { get; init; }
    public required IReadOnlyList<float> Table { get; init; }
    public AffixListDef? AffixList { get; init; }
    public float Bonus { get; init; }

    /// <summary>Inclusive first affix index (default 0).</summary>
    public int AffixFrom { get; init; }

    /// <summary>Inclusive last affix index (default list.Count - 1).</summary>
    public int AffixTo { get; init; }
}

/// <summary>
/// Craft-time quality roll: uses station-warmed knobs, roll max of N samples, lerp attribute table,
/// band affix list. Surface: <c>apply-quality</c> / <c>attributes</c>.
/// </summary>
public sealed class ApplyQualityAction
    : AbilityActionHandler<CraftMutateOutputContext, ItemStack, ApplyQualityParams>
{
    readonly AffixListRegistry affixLists;

    public ApplyQualityAction(AffixListRegistry affixLists) =>
        this.affixLists = affixLists;

    public override ActionId Id => ActionIds.Quality;
    public override HookId Hook => HookIds.CraftingInteraction;
    public override VerbId Verb => VerbIds.ApplyQuality;
    public override PhaseId Phase => HookIds.Attributes;

    protected override bool TryParse(JObject? raw, out ApplyQualityParams? parameters, out string error)
    {
        parameters = null;
        if (raw == null)
        {
            error = "params require key, op, table.";
            return false;
        }

        string? key = raw.Value<string>("key")?.Trim();
        if (string.IsNullOrWhiteSpace(key))
        {
            error = "key is required.";
            return false;
        }

        string? op = raw.Value<string>("op")?.Trim().ToLowerInvariant();
        if (op is not ("add" or "scale" or "set"))
        {
            error = "op must be 'add', 'scale', or 'set'.";
            return false;
        }

        JToken? tableToken = raw["table"];
        if (tableToken is not JArray tableArr || tableArr.Count == 0)
        {
            error = "table must be a non-empty number array.";
            return false;
        }

        float[] table = new float[tableArr.Count];
        for (int i = 0; i < tableArr.Count; i++)
        {
            if (tableArr[i] == null
                || (tableArr[i]!.Type != JTokenType.Integer && tableArr[i]!.Type != JTokenType.Float)
                || !float.IsFinite(tableArr[i]!.Value<float>()))
            {
                error = "table entries must be finite numbers.";
                return false;
            }

            table[i] = tableArr[i]!.Value<float>();
        }

        string? affixes = raw.Value<string>("affixes")?.Trim();
        AffixListDef? list = null;
        int affixFrom = 0;
        int affixTo = 0;
        if (!string.IsNullOrWhiteSpace(affixes))
        {
            if (!affixLists.TryGet(affixes, out AffixListDef resolved) || resolved.Entries.Count == 0)
            {
                error = $"unknown or empty affix list '{affixes}'.";
                return false;
            }

            list = resolved;
            affixTo = list.Entries.Count - 1;
        }

        float bonus = raw.Value<float?>("bonus") ?? 0f;
        if (!float.IsFinite(bonus))
        {
            error = "bonus must be a finite number.";
            return false;
        }

        JToken? rangeToken = raw["affixRange"];
        if (rangeToken != null && rangeToken.Type != JTokenType.Null)
        {
            if (list == null)
            {
                error = "affixRange requires affixes.";
                return false;
            }

            if (rangeToken is not JArray rangeArr || rangeArr.Count != 2)
            {
                error = "affixRange must be [from, to] inclusive indices.";
                return false;
            }

            if (rangeArr[0] == null
                || rangeArr[1] == null
                || (rangeArr[0]!.Type != JTokenType.Integer && rangeArr[0]!.Type != JTokenType.Float)
                || (rangeArr[1]!.Type != JTokenType.Integer && rangeArr[1]!.Type != JTokenType.Float))
            {
                error = "affixRange entries must be integers.";
                return false;
            }

            affixFrom = rangeArr[0]!.Value<int>();
            affixTo = rangeArr[1]!.Value<int>();
            if (affixFrom < 0
                || affixTo < 0
                || affixFrom >= list.Entries.Count
                || affixTo >= list.Entries.Count
                || affixFrom > affixTo)
            {
                error =
                    $"affixRange [{affixFrom}, {affixTo}] is invalid for list '{affixes}' (count {list.Entries.Count}).";
                return false;
            }
        }

        parameters = new ApplyQualityParams
        {
            Key = key,
            Op = op,
            Table = table,
            AffixList = list,
            Bonus = bonus,
            AffixFrom = affixFrom,
            AffixTo = affixTo
        };
        error = "";
        return true;
    }

    protected override ItemStack Apply(
        CraftMutateOutputContext context,
        ItemStack value,
        ApplyQualityParams parameters,
        AbilityRuleSource source)
    {
        if (value == null)
        {
            return value!;
        }

        if (!context.QualityKnobsReady)
        {
            throw new InvalidOperationException(
                "ApplyQualityAction requires station/fixture WarmUp of quality knobs before the attributes fold.");
        }

        Random? rand = context.Rand ?? context.Player?.Entity?.World?.Rand;
        if (rand == null)
        {
            return value;
        }

        _ = source;
        float qualityBase = context.QualityBase;
        float qualityWindow = context.QualityWindow;
        float qualityRolls = context.QualityRolls;
        float globalBonus = context.QualityBonus;

        int rolls = QualityMath.ResolveRollCount(qualityRolls);
        float raw = QualityMath.RollRaw(qualityBase, qualityWindow, rolls, rand);
        float mean = QualityMath.ClampMean(raw, parameters.Bonus, globalBonus);
        QualityGrade.TryStamp(affixLists, context, value, mean);

        int points = QualityMath.ToPoints(raw, parameters.Bonus, globalBonus);
        if (points <= 0)
        {
            return value;
        }

        float amount = QualityMath.LerpTable(parameters.Table, points);
        ApplyAttributeOp(value, parameters.Key, parameters.Op, amount, context.World);

        if (parameters.AffixList != null && parameters.AffixList.Entries.Count > 0)
        {
            int affixIdx = QualityMath.AffixIndexInRange(
                parameters.AffixList.Entries.Count,
                points,
                parameters.AffixFrom,
                parameters.AffixTo);
            if (affixIdx >= 0 && affixIdx < parameters.AffixList.Entries.Count)
            {
                AffixListEntryDef entry = parameters.AffixList.Entries[affixIdx];
                ItemAffixes.Add(value, entry.Code, entry.Lang, entry.Color);
            }
        }

        return value;
    }

    static void ApplyAttributeOp(
        ItemStack stack,
        string key,
        string op,
        float amount,
        IWorldAccessor? world)
    {
        float current = CraftAttributeMods.GetFactor(stack, key);
        float next = op switch
        {
            "add" => current + amount,
            "scale" => current * (1f + amount),
            _ => amount
        };
        AbilityBootstrap.AttributeMutators.Set(stack, key, next, world);
    }
}
