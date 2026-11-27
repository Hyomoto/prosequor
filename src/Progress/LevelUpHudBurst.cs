using Prosequor.Network;

namespace Prosequor.Progress;

/// <summary>
/// Folds level-up HUD packets that landed together into one step per skill,
/// then one player-level step when the character level changed.
/// </summary>
public static class LevelUpHudBurst
{
    public static List<LevelUpHudPacket> Compact(IReadOnlyList<LevelUpHudPacket> packets)
    {
        if (packets.Count <= 1)
        {
            return new List<LevelUpHudPacket>(packets);
        }

        LevelUpHudPacket first = packets[0];
        LevelUpHudPacket last = packets[^1];
        int playerBefore = first.PlayerLevelBefore;
        int playerAfter = last.PlayerLevelAfter;
        float barBefore = first.PlayerBarFillBefore;
        float barAfter = last.PlayerBarFillAfter;
        bool playerLeveled = playerAfter > playerBefore;

        List<string> attributes = CollectAttributes(packets);
        List<string> skillOrder = new();
        Dictionary<string, (int Before, int After)> skills = new(StringComparer.Ordinal);
        foreach (LevelUpHudPacket packet in packets)
        {
            if (!packet.SkillLeveledUp)
            {
                continue;
            }

            string id = packet.SkillId.Trim();
            if (!skills.TryGetValue(id, out (int Before, int After) span))
            {
                skillOrder.Add(id);
                skills[id] = (packet.SkillLevelBefore, packet.SkillLevelAfter);
            }
            else
            {
                skills[id] = (span.Before, packet.SkillLevelAfter);
            }
        }

        int shownLevel = playerLeveled ? playerBefore : playerAfter;
        List<LevelUpHudPacket> result = new();
        for (int i = 0; i < skillOrder.Count; i++)
        {
            string id = skillOrder[i];
            (int before, int after) = skills[id];
            if (after <= before)
            {
                continue;
            }

            result.Add(new LevelUpHudPacket
            {
                SkillId = id,
                SkillLevelBefore = before,
                SkillLevelAfter = after,
                PlayerLevelBefore = shownLevel,
                PlayerLevelAfter = shownLevel,
                PlayerBarFillBefore = barBefore,
                PlayerBarFillAfter = barBefore,
                PlayerLeveledUp = false,
                AttributeGains = new List<string>()
            });
        }

        if (!playerLeveled && result.Count > 0)
        {
            result[^1].PlayerBarFillAfter = barAfter;
        }

        if (!playerLeveled && attributes.Count > 0 && result.Count > 0)
        {
            result[^1].AttributeGains = attributes;
        }

        if (playerLeveled)
        {
            result.Add(new LevelUpHudPacket
            {
                SkillId = "",
                SkillLevelBefore = 0,
                SkillLevelAfter = 0,
                PlayerLevelBefore = playerBefore,
                PlayerLevelAfter = playerAfter,
                PlayerBarFillBefore = barBefore,
                PlayerBarFillAfter = barAfter,
                PlayerLeveledUp = true,
                AttributeGains = attributes
            });
        }

        if (result.Count == 0)
        {
            return new List<LevelUpHudPacket> { last };
        }

        return result;
    }

    static List<string> CollectAttributes(IReadOnlyList<LevelUpHudPacket> packets)
    {
        List<string> attributes = new();
        HashSet<string> seen = new(StringComparer.Ordinal);
        foreach (LevelUpHudPacket packet in packets)
        {
            if (packet.AttributeGains == null)
            {
                continue;
            }

            foreach (string id in packet.AttributeGains)
            {
                if (string.IsNullOrWhiteSpace(id) || !seen.Add(id))
                {
                    continue;
                }

                attributes.Add(id);
            }
        }

        return attributes;
    }
}
