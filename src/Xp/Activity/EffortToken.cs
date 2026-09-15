namespace Prosequor.Xp.Activity;

/// <summary>
/// Prosequor standard effort tokens for <c>when.tags</c> / <see cref="Effort.Emit"/>.
/// Prefer this enum over magic strings; custom mod tokens may still use free-form strings.
/// </summary>
public enum EffortToken
{
    Interacting,
    Mounted,
    Riding,
    Boating,
    Helmsman,
    Fishing,
    Moving,
    Sprinting,
    Swimming,
    Sneaking,
    TemporalDrain
}

/// <summary>String forms for <see cref="EffortToken"/> (JSON <c>when.tags</c> / fact tokens).</summary>
public static class EffortTokenTags
{
    public const string Interacting = "interacting";
    public const string Mounted = "mounted";
    public const string Riding = "riding";
    public const string Boating = "boating";
    public const string Helmsman = "helmsman";
    public const string Fishing = "fishing";
    public const string Moving = "moving";
    public const string Sprinting = "sprinting";
    public const string Swimming = "swimming";
    public const string Sneaking = "sneaking";
    public const string TemporalDrain = "temporal-drain";

    public static string ToTag(this EffortToken token) => token switch
    {
        EffortToken.Interacting => Interacting,
        EffortToken.Mounted => Mounted,
        EffortToken.Riding => Riding,
        EffortToken.Boating => Boating,
        EffortToken.Helmsman => Helmsman,
        EffortToken.Fishing => Fishing,
        EffortToken.Moving => Moving,
        EffortToken.Sprinting => Sprinting,
        EffortToken.Swimming => Swimming,
        EffortToken.Sneaking => Sneaking,
        EffortToken.TemporalDrain => TemporalDrain,
        _ => token.ToString().ToLowerInvariant()
    };

    public static bool TryParse(string? raw, out EffortToken token)
    {
        token = default;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        string t = raw.Trim();
        if (t.Equals(Interacting, StringComparison.OrdinalIgnoreCase))
        {
            token = EffortToken.Interacting;
            return true;
        }

        if (t.Equals(Mounted, StringComparison.OrdinalIgnoreCase))
        {
            token = EffortToken.Mounted;
            return true;
        }

        if (t.Equals(Riding, StringComparison.OrdinalIgnoreCase))
        {
            token = EffortToken.Riding;
            return true;
        }

        if (t.Equals(Boating, StringComparison.OrdinalIgnoreCase))
        {
            token = EffortToken.Boating;
            return true;
        }

        if (t.Equals(Helmsman, StringComparison.OrdinalIgnoreCase))
        {
            token = EffortToken.Helmsman;
            return true;
        }

        if (t.Equals(Fishing, StringComparison.OrdinalIgnoreCase))
        {
            token = EffortToken.Fishing;
            return true;
        }

        if (t.Equals(Moving, StringComparison.OrdinalIgnoreCase))
        {
            token = EffortToken.Moving;
            return true;
        }

        if (t.Equals(Sprinting, StringComparison.OrdinalIgnoreCase))
        {
            token = EffortToken.Sprinting;
            return true;
        }

        if (t.Equals(Swimming, StringComparison.OrdinalIgnoreCase))
        {
            token = EffortToken.Swimming;
            return true;
        }

        if (t.Equals(Sneaking, StringComparison.OrdinalIgnoreCase))
        {
            token = EffortToken.Sneaking;
            return true;
        }

        if (t.Equals(TemporalDrain, StringComparison.OrdinalIgnoreCase))
        {
            token = EffortToken.TemporalDrain;
            return true;
        }

        return false;
    }
}
