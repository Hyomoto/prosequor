using Vintagestory.API.Common;
using Vintagestory.API.Config;

namespace Prosequor;

/// <summary>Result of <see cref="CompanionSupport.QuerySupport"/>.</summary>
public readonly struct SupportAnswer
{
    public bool Allowed { get; init; }

    /// <summary>Empty when <see cref="Allowed"/>; otherwise why the companion should not run.</summary>
    public string Reason { get; init; }
}

/// <summary>
/// Expectations Prosequor holds for optional companions. Unknown mod ids are allowed
/// (no opinion). Known companions below their minimum are denied with a reason string
/// the caller (or Prosequor's watchdog) can log.
/// </summary>
public static class CompanionSupport
{
    public const string PlusModId = "prosequorplus";

    /// <summary>First Plus release this Prosequor will load with. 1.0.2 and older are rejected.</summary>
    public const string PlusMinimumVersion = "1.0.3";

    /// <summary>
    /// Unknown <paramref name="modId"/> → allowed. Known companions must meet their
    /// minimum version (blank version fails).
    /// </summary>
    public static SupportAnswer QuerySupport(string? modId, string? version)
    {
        if (string.IsNullOrWhiteSpace(modId)
            || !string.Equals(modId, PlusModId, StringComparison.OrdinalIgnoreCase))
        {
            return Allowed();
        }

        if (string.IsNullOrWhiteSpace(version)
            || !GameVersion.IsAtLeastVersion(version, PlusMinimumVersion))
        {
            string found = string.IsNullOrWhiteSpace(version) ? "unknown" : version.Trim();
            return Denied(FormatPlusError(found));
        }

        return Allowed();
    }

    /// <summary>
    /// If Plus is enabled and too old, log the denial. Does not throw — Prosequor must
    /// always finish loading so world startup is not poisoned.
    /// </summary>
    public static void WatchdogLog(ICoreAPI api)
    {
        if (api?.ModLoader == null || !api.ModLoader.IsModEnabled(PlusModId))
        {
            return;
        }

        string version = api.ModLoader.GetMod(PlusModId)?.Info?.Version ?? "";
        SupportAnswer answer = QuerySupport(PlusModId, version);
        if (!answer.Allowed)
        {
            api.Logger.Error(answer.Reason);
        }
    }

    public static string FormatPlusError(string foundVersion) =>
        $"[prosequor] Prosequor Plus {foundVersion} cannot run with this Prosequor. " +
        $"Update Prosequor Plus to {PlusMinimumVersion} or newer; both mods must be updated together.";

    static SupportAnswer Allowed() => new() { Allowed = true, Reason = "" };

    static SupportAnswer Denied(string reason) => new() { Allowed = false, Reason = reason };
}
