using System.Text;
using Newtonsoft.Json;

namespace Prosequor.Xp;

/// <summary>One coalesced skill tab owed to a player who was not present to collect it.</summary>
public readonly record struct FatherXpGrant(string SkillId, float Amount);

/// <summary>
/// World-scoped tab of skill XP FatherXp could not deliver yet.
/// Coalesces by player + skill so this stays a tab, not a bank statement.
/// </summary>
public sealed class FatherXpMailbox
{
    public const int CurrentSchema = 1;

    readonly Dictionary<string, Dictionary<string, float>> pending = new(StringComparer.OrdinalIgnoreCase);

    public int PlayerCount => pending.Count;

    public void Enqueue(string playerUid, string skillId, float amount)
    {
        if (string.IsNullOrWhiteSpace(playerUid) || string.IsNullOrWhiteSpace(skillId) || amount <= 0f)
        {
            return;
        }

        playerUid = playerUid.Trim();
        skillId = skillId.Trim();
        if (!pending.TryGetValue(playerUid, out Dictionary<string, float>? bySkill) || bySkill == null)
        {
            bySkill = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
            pending[playerUid] = bySkill;
        }

        bySkill.TryGetValue(skillId, out float current);
        bySkill[skillId] = current + amount;
    }

    public bool TryPeek(string playerUid, string skillId, out float amount)
    {
        amount = 0f;
        if (string.IsNullOrWhiteSpace(playerUid) || string.IsNullOrWhiteSpace(skillId))
        {
            return false;
        }

        if (!pending.TryGetValue(playerUid.Trim(), out Dictionary<string, float>? bySkill)
            || bySkill == null
            || !bySkill.TryGetValue(skillId.Trim(), out amount)
            || amount <= 0f)
        {
            amount = 0f;
            return false;
        }

        return true;
    }

    public bool TryTake(string playerUid, out IReadOnlyList<FatherXpGrant> grants)
    {
        if (string.IsNullOrWhiteSpace(playerUid)
            || !pending.Remove(playerUid.Trim(), out Dictionary<string, float>? bySkill)
            || bySkill == null
            || bySkill.Count == 0)
        {
            grants = Array.Empty<FatherXpGrant>();
            return false;
        }

        List<FatherXpGrant> taken = new(bySkill.Count);
        foreach (KeyValuePair<string, float> kv in bySkill.OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase))
        {
            if (kv.Value > 0f)
            {
                taken.Add(new FatherXpGrant(kv.Key, kv.Value));
            }
        }

        grants = taken;
        return taken.Count > 0;
    }

    public byte[] Serialize()
    {
        FatherXpMailboxState state = new()
        {
            Schema = CurrentSchema,
            Pending = new Dictionary<string, Dictionary<string, float>>(StringComparer.OrdinalIgnoreCase)
        };

        foreach (KeyValuePair<string, Dictionary<string, float>> player in pending)
        {
            Dictionary<string, float> copy = new(StringComparer.OrdinalIgnoreCase);
            foreach (KeyValuePair<string, float> skill in player.Value)
            {
                if (skill.Value > 0f)
                {
                    copy[skill.Key] = skill.Value;
                }
            }

            if (copy.Count > 0)
            {
                state.Pending[player.Key] = copy;
            }
        }

        return Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(state));
    }

    public static FatherXpMailbox Deserialize(byte[]? bytes)
    {
        FatherXpMailbox mailbox = new();
        if (bytes == null || bytes.Length == 0)
        {
            return mailbox;
        }

        FatherXpMailboxState? state = JsonConvert.DeserializeObject<FatherXpMailboxState>(
            Encoding.UTF8.GetString(bytes));
        if (state?.Pending == null)
        {
            return mailbox;
        }

        foreach (KeyValuePair<string, Dictionary<string, float>> player in state.Pending)
        {
            if (string.IsNullOrWhiteSpace(player.Key) || player.Value == null)
            {
                continue;
            }

            foreach (KeyValuePair<string, float> skill in player.Value)
            {
                mailbox.Enqueue(player.Key, skill.Key, skill.Value);
            }
        }

        return mailbox;
    }

    sealed class FatherXpMailboxState
    {
        public int Schema { get; set; } = CurrentSchema;
        public Dictionary<string, Dictionary<string, float>> Pending { get; set; } = new();
    }
}
