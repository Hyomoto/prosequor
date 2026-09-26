using System.Runtime.CompilerServices;

namespace Prosequor.Client;

/// <summary>
/// Harmony-targettable attachment for tooltips on the Stats panel vital cards.
/// Prosequor supplies no lines; other mods postfix <see cref="Lines"/> and append.
/// </summary>
public static class StatsVitalHover
{
    public const string Health = "health";
    public const string Satiety = "satiety";
    public const string Temperature = "temperature";

    /// <summary>
    /// Tooltip lines for a vital card. Empty means draw nothing.
    /// Marked no-inline so Harmony can postfix the method body.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static List<string> Lines(string slot) => new();
}
