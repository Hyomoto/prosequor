namespace Prosequor.Xp;

/// <summary>
/// How an XP award interacts with skill saturation meters.
/// Commit amount is always lifetime XP; only <see cref="Earn"/> lets sat scale it.
/// Player-track awards are unmetered (`nb`/`fb` are no-ops there).
/// </summary>
public enum XpAwardMode
{
    /// <summary>Sat-scaled flush via skill buckets (gameplay default for skills).</summary>
    Earn = 0,

    /// <summary>Full declared commit; do not touch meters.</summary>
    Grant = 1,

    /// <summary>Full declared commit; also <c>+= raw</c> on the skill meter (no sat on this award).</summary>
    GrantAndFill = 2
}
