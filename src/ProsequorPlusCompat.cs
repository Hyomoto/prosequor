using Vintagestory.API.Common;
using Vintagestory.API.Config;

namespace Prosequor;

/// <summary>
/// Optional companion <c>prosequorplus</c>: absent is fine; present-but-too-old
/// is a hard fail so an outdated bridge cannot crash and get blamed on Prosequor.
/// </summary>
public static class ProsequorPlusCompat
{
    public const string ModId = "prosequorplus";

    /// <summary>First Plus release this Prosequor will load with. 1.0.2 and older are rejected.</summary>
    public const string MinimumVersion = "1.0.3";

    /// <summary>
    /// <paramref name="loaded"/> false (Plus not enabled) always passes.
    /// Enabled Plus must declare <see cref="MinimumVersion"/> or newer.
    /// </summary>
    public static bool IsCompatible(bool loaded, string? version)
    {
        if (!loaded)
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(version))
        {
            return false;
        }

        return GameVersion.IsAtLeastVersion(version, MinimumVersion);
    }

    public static string FormatError(string foundVersion) =>
        $"[prosequor] Prosequor Plus {foundVersion} cannot run with this Prosequor. " +
        $"Update Prosequor Plus to {MinimumVersion} or newer; both mods must be updated together.";

    public static void ThrowIfIncompatible(ICoreAPI api)
    {
        if (api?.ModLoader == null)
        {
            return;
        }

        bool loaded = api.ModLoader.IsModEnabled(ModId);
        string version = api.ModLoader.GetMod(ModId)?.Info?.Version ?? "";
        if (IsCompatible(loaded, version))
        {
            return;
        }

        string found = string.IsNullOrWhiteSpace(version) ? "unknown" : version;
        string message = FormatError(found);
        api.Logger.Error(message);
        throw new InvalidOperationException(message);
    }
}
