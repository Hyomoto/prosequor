using Prosequor.Ability.Hooks;
using Prosequor.Data;
using Prosequor.Progress;
using Vintagestory.API.Common;
using Vintagestory.API.Server;

namespace Prosequor.Player;

/// <summary>
/// In-memory UID park of progress for offline reads this uptime.
/// Truth stays on player ModData; join evicts and reloads.
/// </summary>
public sealed class ProgressPark
{
    readonly Dictionary<string, ParkedPlayerProgress> parked = new(StringComparer.OrdinalIgnoreCase);

    public int Count => parked.Count;

    public int MaxPlayerLevel { get; set; } = XpCurves.PlayerMaxLevel;

    public bool TryGet(string? playerUid, out ParkedPlayerProgress progress)
    {
        progress = null!;
        if (string.IsNullOrWhiteSpace(playerUid))
        {
            return false;
        }

        return parked.TryGetValue(playerUid.Trim(), out progress!);
    }

    public IPlayerProgress? TryGet(string? playerUid) =>
        TryGet(playerUid, out ParkedPlayerProgress progress) ? progress : null;

    public void Evict(string? playerUid)
    {
        if (string.IsNullOrWhiteSpace(playerUid))
        {
            return;
        }

        parked.Remove(playerUid.Trim());
    }

    public void Clear() => parked.Clear();

    /// <summary>Steal the live state after <see cref="EntityBehaviorProgress.FlushSave"/>.</summary>
    public void ParkLive(
        string? playerUid,
        EntityBehaviorProgress? behavior,
        AbilityRuleIndex? ruleIndex,
        ISkillRegistry? registry = null,
        ITraitAttributeRegistry? traits = null)
    {
        if (string.IsNullOrWhiteSpace(playerUid) || behavior == null || !behavior.HasSyncedMirror)
        {
            return;
        }

        behavior.CaptureClassProfile();
        parked[playerUid.Trim()] = new ParkedPlayerProgress(
            behavior.State,
            ruleIndex,
            registry,
            CloneAccess(behavior.SkillAccess),
            MaxPlayerLevel,
            traits);
    }

    public void ParkStored(
        string? playerUid,
        PlayerProgressState state,
        AbilityRuleIndex? ruleIndex,
        ISkillRegistry? registry = null,
        SkillAccess? skillAccess = null,
        ITraitAttributeRegistry? traits = null)
    {
        if (string.IsNullOrWhiteSpace(playerUid) || state == null)
        {
            return;
        }

        parked[playerUid.Trim()] = new ParkedPlayerProgress(
            state,
            ruleIndex,
            registry,
            skillAccess,
            MaxPlayerLevel,
            traits);
    }

    static SkillAccess CloneAccess(SkillAccess source)
    {
        SkillAccess copy = new();
        if (source == null || source.IsUnbound)
        {
            return copy;
        }

        copy.Update(SkillAccess.ClassKey, source.Union);
        return copy;
    }

    /// <summary>
    /// Load ModData for every known account UID. Missing bytes are skipped (original degrade).
    /// </summary>
    public int Preload(
        ICoreServerAPI sapi,
        ISkillRegistry registry,
        AbilityRuleIndex? ruleIndex,
        IReadOnlySet<string>? defaultSkillAccess = null,
        IAttributeStatRegistry? stats = null,
        ITraitAttributeRegistry? traits = null)
    {
        if (sapi?.PlayerData?.PlayerDataByUid == null || registry == null)
        {
            return 0;
        }

        int loaded = 0;
        foreach (string uid in sapi.PlayerData.PlayerDataByUid.Keys)
        {
            if (string.IsNullOrWhiteSpace(uid) || TryGet(uid, out _))
            {
                continue;
            }

            IPlayer? player = sapi.World?.PlayerByUid(uid);
            if (!ProgressStore.TryHydrateStored(
                    ProgressStore.ReadModData(player),
                    registry,
                    out PlayerProgressState state,
                    stats,
                    MaxPlayerLevel))
            {
                continue;
            }

            SkillAccess? access = null;
            if (defaultSkillAccess != null)
            {
                access = new SkillAccess();
                access.Update(SkillAccess.ClassKey, defaultSkillAccess);
            }

            ParkStored(uid, state, ruleIndex, registry, access, traits);
            loaded++;
        }

        return loaded;
    }
}
