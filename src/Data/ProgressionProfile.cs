namespace Prosequor.Data;

/// <summary>
/// Resolved player cap, skill-point levels, specialization levels, and skill XP gain.
/// </summary>
public sealed class ProgressionProfile
{
    public const string MultiplayerPresetId = "prosequor:multiplayer";
    public const string SinglePlayerPresetId = "prosequor:singleplayer";
    public const string MmoPresetId = "prosequor:mmo";
    public const float DefaultXpGain = 1f;

    public static ProgressionProfile Baseline { get; } = new(
        MultiplayerPresetId,
        maxPlayerLevel: 50,
        LevelSet.Parse("1..50", XpCurves.PlayerMaxLevel),
        LevelSet.Parse("20,40,60,80,100", XpCurves.SkillMaxLevel),
        LevelSet.Parse("10,20,30,40,50", XpCurves.PlayerMaxLevel),
        DefaultXpGain);

    public string PresetId { get; }
    public int MaxPlayerLevel { get; }
    public LevelSet SkillPointsPerPlayerLevel { get; }
    public LevelSet SkillPointsPerSkillLevel { get; }
    public LevelSet SpecializationLevels { get; }
    public float XpGain { get; }

    public ProgressionProfile(
        string presetId,
        int maxPlayerLevel,
        LevelSet skillPointsPerPlayerLevel,
        LevelSet skillPointsPerSkillLevel,
        LevelSet specializationLevels,
        float xpGain)
    {
        PresetId = string.IsNullOrWhiteSpace(presetId) ? MultiplayerPresetId : presetId;
        MaxPlayerLevel = maxPlayerLevel < XpCurves.PlayerMinLevel
            ? XpCurves.PlayerMaxLevel
            : maxPlayerLevel;
        SkillPointsPerPlayerLevel = skillPointsPerPlayerLevel;
        SkillPointsPerSkillLevel = skillPointsPerSkillLevel;
        SpecializationLevels = specializationLevels;
        XpGain = xpGain < 0f ? DefaultXpGain : xpGain;
    }

    public int SkillPointsForPlayerLevel(int before, int after) =>
        SkillPointsPerPlayerLevel.CountCrossed(before, after);

    public int SkillPointsForSkillLevel(int before, int after) =>
        SkillPointsPerSkillLevel.CountCrossed(before, after);

    public int SpecializationSlotCount(int playerLevel) =>
        playerLevel < XpCurves.PlayerMinLevel ? 0 : SpecializationLevels.CountAtMost(playerLevel);

    /// <summary>Scale an Earn amount. 1 leaves the amount unchanged.</summary>
    public float ScaleEarn(float amount)
    {
        if (amount <= 0f || XpGain == DefaultXpGain)
        {
            return amount;
        }

        float scaled = amount * XpGain;
        return scaled > 0f ? scaled : 0f;
    }

    public ProgressionProfile With(
        string? presetId = null,
        int? maxPlayerLevel = null,
        LevelSet? skillPointsPerPlayerLevel = null,
        LevelSet? skillPointsPerSkillLevel = null,
        LevelSet? specializationLevels = null,
        float? xpGain = null) =>
        new(
            presetId ?? PresetId,
            maxPlayerLevel ?? MaxPlayerLevel,
            skillPointsPerPlayerLevel ?? SkillPointsPerPlayerLevel,
            skillPointsPerSkillLevel ?? SkillPointsPerSkillLevel,
            specializationLevels ?? SpecializationLevels,
            xpGain ?? XpGain);

    public static ProgressionProfile FromRuns(
        int maxPlayerLevel,
        IEnumerable<LevelRun>? skillPointsPerPlayerLevel,
        IEnumerable<LevelRun>? skillPointsPerSkillLevel,
        IEnumerable<LevelRun>? specializationLevels) =>
        new(
            MultiplayerPresetId,
            maxPlayerLevel,
            LevelSet.FromRuns(skillPointsPerPlayerLevel),
            LevelSet.FromRuns(skillPointsPerSkillLevel),
            LevelSet.FromRuns(specializationLevels),
            DefaultXpGain);
}
