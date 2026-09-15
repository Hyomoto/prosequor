using Newtonsoft.Json.Linq;
using Prosequor.Ability.Hooks;
using Prosequor.Data;
using Vintagestory.API.Common;

namespace Prosequor.Ability.Actions;

public sealed class ApplyQualityRankParams
{
    public required IReadOnlyList<float> Table { get; init; }
    public float Bonus { get; init; }
}

/// <summary>
/// Craft-time quality roll that writes an authored rank (no bonus affix, no attribute mutator).
/// <c>key</c> must be <c>qualityRank</c> (or <c>rank</c>). Table is lerped over ranks 0–30
/// and stored on the pedigree blob. The quality grade footer stamps because a rank was applied.
/// Surface: <c>apply-quality</c> / <c>attributes</c>.
/// </summary>
public sealed class ApplyQualityRankAction
    : AbilityActionHandler<CraftMutateOutputContext, ItemStack, ApplyQualityRankParams>
{
    readonly AffixListRegistry affixLists;

    public ApplyQualityRankAction(AffixListRegistry affixLists) =>
        this.affixLists = affixLists;

    public override ActionId Id => ActionIds.QualityRank;
    public override HookId Hook => HookIds.CraftingInteraction;
    public override VerbId Verb => VerbIds.ApplyQuality;
    public override PhaseId Phase => HookIds.Attributes;

    protected override bool TryParse(JObject? raw, out ApplyQualityRankParams? parameters, out string error)
    {
        parameters = null;
        if (raw == null)
        {
            error = "params require key and table.";
            return false;
        }

        string? key = raw.Value<string>("key")?.Trim();
        if (!IsQualityRankKey(key))
        {
            error = "key must be 'qualityRank' (or 'rank').";
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

        float bonus = raw.Value<float?>("bonus") ?? 0f;
        if (!float.IsFinite(bonus))
        {
            error = "bonus must be a finite number.";
            return false;
        }

        if (raw["op"] != null && raw["op"]!.Type != JTokenType.Null)
        {
            error = "quality-rank does not take op (rank is always set from the table).";
            return false;
        }

        if (raw["affixes"] != null && raw["affixes"]!.Type != JTokenType.Null)
        {
            error = "quality-rank does not take affixes.";
            return false;
        }

        if (raw["affixRange"] != null && raw["affixRange"]!.Type != JTokenType.Null)
        {
            error = "quality-rank does not take affixRange.";
            return false;
        }

        parameters = new ApplyQualityRankParams
        {
            Table = table,
            Bonus = bonus
        };
        error = "";
        return true;
    }

    protected override ItemStack Apply(
        CraftMutateOutputContext context,
        ItemStack value,
        ApplyQualityRankParams parameters,
        AbilityRuleSource source)
    {
        if (value == null)
        {
            return value!;
        }

        if (!context.QualityKnobsReady)
        {
            throw new InvalidOperationException(
                "ApplyQualityRankAction requires station/fixture WarmUp of quality knobs before the attributes fold.");
        }

        Random? rand = context.Rand ?? context.Player?.Entity?.World?.Rand;
        if (rand == null)
        {
            return value;
        }

        _ = source;
        int rolls = QualityMath.ResolveRollCount(context.QualityRolls);
        float raw = QualityMath.RollRaw(context.QualityBase, context.QualityWindow, rolls, rand);
        float mean = QualityMath.ClampMean(raw, parameters.Bonus, context.QualityBonus);
        int points = QualityMath.ToPoints(raw, parameters.Bonus, context.QualityBonus);

        if (mean > QualityMath.RawMin && mean >= context.BestQualityMean && points > 0)
        {
            float amount = QualityMath.LerpTable(parameters.Table, points);
            int rank = (int)Math.Round(amount, MidpointRounding.AwayFromZero);
            if (rank > 0)
            {
                ProsequorStackPedigree.StampQualityRank(value, rank);
                context.BestQualityMean = mean;
            }
        }

        QualityGrade.TryStamp(affixLists, context, value, mean);
        return value;
    }

    static bool IsQualityRankKey(string? key) =>
        key != null
        && (string.Equals(key, ProsequorBlob.QualityRankKey, StringComparison.OrdinalIgnoreCase)
            || string.Equals(key, "rank", StringComparison.OrdinalIgnoreCase));
}
