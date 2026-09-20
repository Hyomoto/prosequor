using Prosequor.Data;
using Prosequor.Network;

namespace Prosequor.Player;

/// <summary>Builds owner-channel snapshot/delta packets from live progress state.</summary>
public static class ProgressChannelCodec
{
    public static ProgressSnapshotPacket BuildSnapshot(PlayerProgressState state, int seq)
    {
        ProgressSnapshotPacket packet = new()
        {
            Seq = seq,
            PlayerLevel = state.PlayerLevel,
            PlayerXp = state.PlayerXp,
            UnlockPoints = state.UnlockPoints
        };

        foreach (KeyValuePair<string, SkillProgressState> kv in state.Skills)
        {
            if (kv.Value.Level <= 0 && kv.Value.Xp <= 0f)
            {
                continue;
            }

            packet.Skills.Add(new ProgressSkillTrackDto
            {
                SkillId = kv.Key,
                Level = kv.Value.Level,
                Xp = kv.Value.Xp
            });
        }

        PlayerProgressState.EnsureAttributeEntries(state);
        foreach (string id in AttributeIds.All)
        {
            packet.AttributeBuckets.Add(new ProgressAttributeBucketDto
            {
                Id = id,
                Fill = state.AttributeBuckets[id]
            });
        }

        return packet;
    }

    public static ProgressDeltaPacket BuildDelta(
        PlayerProgressState state,
        PendingProgressFlush pending,
        int seq)
    {
        ProgressDeltaPacket packet = new() { Seq = seq };

        if (pending.PlayerTrack)
        {
            packet.HasPlayerTrack = true;
            packet.PlayerLevel = state.PlayerLevel;
            packet.PlayerXp = state.PlayerXp;
            packet.UnlockPoints = state.UnlockPoints;
        }

        foreach (string skillId in pending.SkillTracks)
        {
            if (!state.Skills.TryGetValue(skillId, out SkillProgressState? skill))
            {
                continue;
            }

            packet.Skills.Add(new ProgressSkillTrackDto
            {
                SkillId = skillId,
                Level = skill.Level,
                Xp = skill.Xp
            });
        }

        if (pending.AttributeBuckets)
        {
            packet.HasAttributeBuckets = true;
            PlayerProgressState.EnsureAttributeEntries(state);
            foreach (string id in AttributeIds.All)
            {
                packet.AttributeBuckets.Add(new ProgressAttributeBucketDto
                {
                    Id = id,
                    Fill = state.AttributeBuckets[id]
                });
            }
        }

        return packet;
    }

    public static bool NeedsChannel(PendingProgressFlush pending) =>
        pending.PlayerTrack
        || pending.AttributeBuckets
        || pending.SkillTracks.Count > 0;

    public static bool NeedsPublic(PendingProgressFlush pending) =>
        pending.AttributeScores || pending.Unlocks.Count > 0;
}
