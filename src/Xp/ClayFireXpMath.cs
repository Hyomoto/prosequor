using Prosequor.Xp.Activity;

namespace Prosequor.Xp;

/// <summary>
/// Pure helpers for kiln fire XP: tokens and voxel amount tables.
/// Payees come from rule <c>payee</c> + emit contributor shares (no dual-role opinionation).
/// </summary>
public static class ClayFireXpMath
{
    /// <summary>Fire XP amount activity (<see cref="Deed.Activity"/>).</summary>
    public const string Activity = Deed.Activity;

    public const string TokenKilnFired = DeedTokenTags.KilnFired;
    public const string TokenPitKiln = DeedTokenTags.PitKiln;
    public const string TokenBeehiveKiln = DeedTokenTags.BeehiveKiln;
    public const string SkillId = "clayforming";

    /// <summary>Deed tokens for kiln settle.</summary>
    public static List<string> BuildTokens() => [TokenKilnFired];

    /// <summary>
    /// Snapshot weighted shares from a pedigree blob (empty when anonymous / missing).
    /// </summary>
    public static IReadOnlyList<Deed.ContributorShare> SharesFromBlob(Ability.ProsequorBlob? blob) =>
        blob == null || blob.IsAnonymous
            ? Array.Empty<Deed.ContributorShare>()
            : blob.ToDeedShares();

    /// <summary>
    /// Map voxels-per-unit onto an amount table using catalog min/max.
    /// Same lerp as other XP measure grants.
    /// </summary>
    public static float PickAmount(
        IReadOnlyList<float> table,
        int voxelsPerUnit,
        int minVoxelsPerUnit,
        int maxVoxelsPerUnit) =>
        AmountTableMath.LerpAmount(table, voxelsPerUnit, minVoxelsPerUnit, maxVoxelsPerUnit);

    /// <summary>Scalar amount, or table lookup when <paramref name="amountTable"/> is set.</summary>
    public static float ResolveAmount(
        float scalarAmount,
        IReadOnlyList<float>? amountTable,
        int voxelsPerUnit,
        int minVoxelsPerUnit,
        int maxVoxelsPerUnit) =>
        AmountTableMath.ResolveAmount(
            scalarAmount,
            amountTable,
            voxelsPerUnit,
            minVoxelsPerUnit,
            maxVoxelsPerUnit);
}
