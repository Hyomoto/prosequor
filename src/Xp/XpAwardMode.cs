namespace Prosequor.Xp;

/// <summary>
/// How an XP award interacts with saturation meters.
/// Commit amount is always lifetime XP; only <see cref="Earn"/> lets sat scale it.
/// </summary>
public enum XpAwardMode
{
    /// <summary>Sat-scaled flush via buckets (gameplay default).</summary>
    Earn = 0,

    /// <summary>Full declared commit; do not touch meters.</summary>
    Grant = 1,

    /// <summary>Full declared commit; also <c>+= raw</c> on meters (no sat on this award).</summary>
    GrantAndFill = 2
}
