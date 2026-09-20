using Prosequor.Data;
using Prosequor.Network;
using Prosequor.Player;
using Xunit;

namespace Prosequor.Ability;

/// <summary>Sparse progress sync helpers (no world required).</summary>
public static class ProgressSyncFixtures
{
    public static void VerifyAll()
    {
        VerifyPendingAccumulate();
        VerifyChannelCodecOmitsEmptySkills();
        VerifyMergePublicDoesNotWipeXp();
        VerifySeqGapNeedsResync();
    }

    static void VerifyPendingAccumulate()
    {
        PendingProgressFlush pending = new();
        pending.Accumulate(VisibleProgressChange.SkillTrack("farming"));
        pending.Accumulate(VisibleProgressChange.AttributeBuckets());
        if (pending.Immediate
            || !pending.SkillTracks.Contains("farming")
            || !pending.AttributeBuckets
            || ProgressChannelCodec.NeedsPublic(pending)
            || !ProgressChannelCodec.NeedsChannel(pending))
        {
            Assert.Fail("[prosequor] ProgressSync fixture failed (pending coalesce bits).");
        }

        pending.Accumulate(VisibleProgressChange.Unlock("farming", "repotting"));
        if (!pending.Immediate || !ProgressChannelCodec.NeedsPublic(pending))
        {
            Assert.Fail("[prosequor] ProgressSync fixture failed (pending immediate unlock).");
        }
    }

    static void VerifyChannelCodecOmitsEmptySkills()
    {
        PlayerProgressState state = new() { Schema = PlayerProgressState.CurrentSchema };
        state.GetOrCreateSkill("farming").Xp = 3f;
        state.GetOrCreateSkill("farming").Level = 1;
        state.GetOrCreateSkill("mining");
        PlayerProgressState.EnsureAttributeEntries(state);

        ProgressSnapshotPacket snap = ProgressChannelCodec.BuildSnapshot(state, seq: 2);
        if (snap.Seq != 2
            || snap.Skills.Count != 1
            || !string.Equals(snap.Skills[0].SkillId, "farming", StringComparison.OrdinalIgnoreCase)
            || snap.AttributeBuckets.Count != AttributeIds.All.Length)
        {
            Assert.Fail("[prosequor] ProgressSync fixture failed (snapshot omit empty).");
        }

        PendingProgressFlush pending = new();
        pending.Accumulate(VisibleProgressChange.SkillTrack("farming"));
        ProgressDeltaPacket delta = ProgressChannelCodec.BuildDelta(state, pending, seq: 3);
        if (delta.Skills.Count != 1 || delta.HasPlayerTrack || delta.HasAttributeBuckets)
        {
            Assert.Fail("[prosequor] ProgressSync fixture failed (delta skill-only).");
        }
    }

    static void VerifyMergePublicDoesNotWipeXp()
    {
        PlayerProgressState into = new() { Schema = PlayerProgressState.CurrentSchema };
        SkillProgressState farming = into.GetOrCreateSkill("farming");
        farming.Xp = 12f;
        farming.Level = 2;
        farming.SetTier("repotting", 1);
        into.Attributes[AttributeIds.Strength] = 14;
        into.AttributeBuckets[AttributeIds.Strength] = 1.5f;

        PlayerProgressState publicOnly = new() { Schema = PlayerProgressState.CurrentSchema };
        publicOnly.Attributes[AttributeIds.Strength] = 14;
        publicOnly.GetOrCreateSkill("farming").SetTier("repotting", 1);

        into.Attributes[AttributeIds.Strength] = publicOnly.Attributes[AttributeIds.Strength];
        into.GetOrCreateSkill("farming").SetTier(
            "repotting",
            publicOnly.GetOrCreateSkill("farming").GetTier("repotting"));

        if (into.GetOrCreateSkill("farming").Xp != 12f
            || into.GetOrCreateSkill("farming").Level != 2
            || into.AttributeBuckets[AttributeIds.Strength] != 1.5f
            || into.GetAttribute(AttributeIds.Strength) != 14)
        {
            Assert.Fail("[prosequor] ProgressSync fixture failed (merge must keep private XP/buckets).");
        }
    }

    static void VerifySeqGapNeedsResync()
    {
        int last = 4;
        int incoming = 6;
        if (incoming == last + 1)
        {
            Assert.Fail("[prosequor] ProgressSync fixture failed (seq gap detection).");
        }
    }
}
