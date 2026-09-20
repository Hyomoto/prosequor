namespace Prosequor.Xp;

/// <summary>
/// Deed amount-rule pay mode. Omitted <c>pay</c> is <see cref="Flat"/>.
/// JSON accepts exactly one mode — not an array.
/// </summary>
public enum XpPayChannel
{
    /// <summary>Pay <c>amount</c> once when the deed fires.</summary>
    Flat = 0,

    /// <summary>Lerped amount table against block Resistance (dig / mine / chop catalog).</summary>
    Resistance,

    /// <summary>Lerped amount table against clay voxels-per-unit (<c>clay-voxels</c> catalog).</summary>
    Voxels,

    /// <summary>Multiply by emit quantity (crafts, clay-form voxels, drops). Missing → ×1.</summary>
    Quantity,

    /// <summary>Lerped amount table against recipe ingredient units (fixed range 1–40).</summary>
    Ingredients,

    /// <summary>
    /// Lerped amount table against crop growth days (catalog min–max), then divided by
    /// <c>GrowthStages</c> so each stage deed pays a slice of the plant lifetime.
    /// </summary>
    Lifetime
}

/// <summary>Parse / format helpers for <see cref="XpPayChannel"/> JSON.</summary>
public static class XpPayChannels
{
    public const string Flat = "flat";
    public const string Resistance = "resistance";
    public const string Voxels = "voxels";
    public const string Quantity = "quantity";
    public const string Ingredients = "ingredients";
    public const string Lifetime = "lifetime";

    public static bool TryParseName(string? raw, out XpPayChannel channel, out string? error)
    {
        channel = XpPayChannel.Flat;
        error = null;
        if (string.IsNullOrWhiteSpace(raw))
        {
            error = "pay channel must not be empty";
            return false;
        }

        string key = raw.Trim();
        if (key.Equals(Flat, StringComparison.OrdinalIgnoreCase))
        {
            channel = XpPayChannel.Flat;
            return true;
        }

        if (key.Equals(Resistance, StringComparison.OrdinalIgnoreCase))
        {
            channel = XpPayChannel.Resistance;
            return true;
        }

        if (key.Equals(Voxels, StringComparison.OrdinalIgnoreCase))
        {
            channel = XpPayChannel.Voxels;
            return true;
        }

        if (key.Equals(Quantity, StringComparison.OrdinalIgnoreCase))
        {
            channel = XpPayChannel.Quantity;
            return true;
        }

        if (key.Equals(Ingredients, StringComparison.OrdinalIgnoreCase))
        {
            channel = XpPayChannel.Ingredients;
            return true;
        }

        if (key.Equals(Lifetime, StringComparison.OrdinalIgnoreCase))
        {
            channel = XpPayChannel.Lifetime;
            return true;
        }

        error = $"unknown pay channel '{key}'";
        return false;
    }

    public static string Canonical(XpPayChannel pay) => pay switch
    {
        XpPayChannel.Resistance => Resistance,
        XpPayChannel.Voxels => Voxels,
        XpPayChannel.Quantity => Quantity,
        XpPayChannel.Ingredients => Ingredients,
        XpPayChannel.Lifetime => Lifetime,
        _ => Flat
    };

    public static bool IsFlat(XpPayChannel pay) => pay == XpPayChannel.Flat;

    public static bool UsesMeasure(XpPayChannel pay) =>
        pay is XpPayChannel.Resistance
            or XpPayChannel.Voxels
            or XpPayChannel.Ingredients
            or XpPayChannel.Lifetime;

    public static bool UsesQuantity(XpPayChannel pay) => pay == XpPayChannel.Quantity;

    public static bool UsesIngredients(XpPayChannel pay) => pay == XpPayChannel.Ingredients;

    public static bool UsesLifetime(XpPayChannel pay) => pay == XpPayChannel.Lifetime;
}
