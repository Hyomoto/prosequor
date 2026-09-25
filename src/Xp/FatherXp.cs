using Prosequor.Ability;
using Prosequor.Data;
using Prosequor.Player;
using Vintagestory.API.Common;
using Vintagestory.API.Server;

namespace Prosequor.Xp;

/// <summary>
/// Pays skill XP by player uid. If they can collect now, it goes through
/// <see cref="EntityBehaviorProgress.AddSkillXp"/> (buckets included). If they
/// are not around, the amount sits on a world-save tab until join.
/// </summary>
public sealed class FatherXp
{
    public const string SaveDataKey = "prosequor-father-xp";

    readonly ICoreServerAPI sapi;
    readonly ISkillRegistry skills;
    FatherXpMailbox mailbox = new();

    public FatherXp(ICoreServerAPI sapi, ISkillRegistry skills)
    {
        this.sapi = sapi;
        this.skills = skills;
    }

    public void Load()
    {
        ISaveGame? save = sapi.WorldManager?.SaveGame;
        if (save == null)
        {
            return;
        }

        mailbox = FatherXpMailbox.Deserialize(save.GetData(SaveDataKey));
    }

    public void Save()
    {
        ISaveGame? save = sapi.WorldManager?.SaveGame;
        if (save == null)
        {
            return;
        }

        save.StoreData(SaveDataKey, mailbox.Serialize());
    }

    /// <summary>
    /// Award skill XP now, or hold it until the player is back.
    /// Offline holds always settle as <see cref="XpAwardMode.Earn"/>.
    /// </summary>
    public void Pay(
        string playerUid,
        string skillId,
        float amount,
        AbilityAction? fact = null,
        XpAwardMode mode = XpAwardMode.Earn)
    {
        if (!TryNormalize(playerUid, skillId, amount, out playerUid, out skillId))
        {
            return;
        }

        if (TryPayNow(playerUid, skillId, amount, fact, mode))
        {
            return;
        }

        IPlayerProgress? parked = ProsequorModSystem.GetProgress(sapi, playerUid);
        if (parked != null && !parked.HasSkillAccess(skillId))
        {
            return;
        }

        mailbox.Enqueue(playerUid, skillId, amount);
    }

    public void Pay(
        IPlayer? player,
        string skillId,
        float amount,
        AbilityAction? fact = null,
        XpAwardMode mode = XpAwardMode.Earn)
    {
        if (player?.PlayerUID == null)
        {
            return;
        }

        Pay(player.PlayerUID, skillId, amount, fact, mode);
    }

    /// <summary>Flush this player's tab through the normal award path (hits buckets).</summary>
    public void Collect(IServerPlayer player)
    {
        if (player?.PlayerUID == null || !mailbox.TryTake(player.PlayerUID, out IReadOnlyList<FatherXpGrant> grants))
        {
            return;
        }

        foreach (FatherXpGrant grant in grants)
        {
            if (!skills.TryGet(grant.SkillId, out _))
            {
                continue;
            }

            if (!TryPayNow(player.PlayerUID, grant.SkillId, grant.Amount, fact: null, XpAwardMode.Earn))
            {
                mailbox.Enqueue(player.PlayerUID, grant.SkillId, grant.Amount);
            }
        }
    }

    bool TryNormalize(
        string playerUid,
        string skillId,
        float amount,
        out string uid,
        out string skill)
    {
        uid = string.Empty;
        skill = string.Empty;
        if (amount <= 0f || string.IsNullOrWhiteSpace(playerUid) || string.IsNullOrWhiteSpace(skillId))
        {
            return false;
        }

        if (!skills.TryGet(skillId.Trim(), out SkillDef def))
        {
            return false;
        }

        uid = playerUid.Trim();
        skill = def.Id;
        return !string.IsNullOrWhiteSpace(uid);
    }

    bool TryPayNow(string playerUid, string skillId, float amount, AbilityAction? fact, XpAwardMode mode)
    {
        if (sapi.World.PlayerByUid(playerUid) is not IServerPlayer player || player.Entity == null)
        {
            return false;
        }

        EntityBehaviorProgress? progress = ProsequorModSystem.TryGetLiveProgress(player);
        if (progress == null)
        {
            return false;
        }

        if (!progress.HasSkillAccess(skillId))
        {
            return true;
        }

        // Enroll (AdmitInitialized) owns load + attribute apply. Pay only.
        progress.AddSkillXp(skillId, amount, fact, mode);
        return true;
    }
}
