using Prosequor.Ability;
using Prosequor.Ability.Actions;
using Prosequor.Ability.Hooks;
using Prosequor.Player;
using Vintagestory.API.Common;

namespace Prosequor.Data;

/// <summary>
/// Formats an owned tier's <see cref="TotalParamSpec"/> list by evaluating each
/// targeted number operand against the player's skill level. Not a pipeline fold.
/// </summary>
public static class SkillEffectTotal
{
    /// <summary>
    /// Bracket suffix for the current-rank tooltip, or empty when there is nothing to show.
    /// Includes the leading space so callers can append it directly.
    /// </summary>
    public static string FormatBracket(SkillTreeTierDef tier, IPlayerProgress? progress, bool vtml)
    {
        if (progress == null || tier.TotalParams.Count == 0)
        {
            return "";
        }

        ProgressOnlyHookContext context = new(progress);
        List<string> parts = new(tier.TotalParams.Count);
        foreach (TotalParamSpec spec in tier.TotalParams)
        {
            if (spec.Target < 0 || spec.Target >= tier.Rules.Count)
            {
                continue;
            }

            AbilityRule rule = tier.Rules[spec.Target];
            if (rule.Parameters is not NumberSpec number)
            {
                continue;
            }

            float value = number.Evaluate(context, rule.Source);
            parts.Add(FormatValue(value, spec.Format, vtml));
        }

        return parts.Count == 0 ? "" : " [" + string.Join("/", parts) + "]";
    }

    static string FormatValue(float value, string format, bool vtml)
    {
        FormattedDescriptionArg arg = new()
        {
            Value = (decimal)value,
            Format = format
        };
        return vtml
            ? SkillDescriptionRender.FormatPercentVtml(arg)
            : SkillDescriptionRender.FormatPercentPlain(arg);
    }

    sealed class ProgressOnlyHookContext : IHookContext
    {
        public ProgressOnlyHookContext(IPlayerProgress progress) => Progress = progress;

        public HookId Hook => default;
        public IPlayer? Player => null;
        public IPlayerProgress? Progress { get; }
        public AbilityAction? Fact => null;
    }
}
