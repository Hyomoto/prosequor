using Prosequor.Client.Tracker;
using Xunit;

namespace Prosequor.Pure.Tests;

/// <summary>Pure tracker mark rules: one candidate, one mark, no world and no GL.</summary>
public static class TrackMarkFixtures
{
    const float Range = 30f;
    const float Focus = 3f;
    const float Angle = 90f;

    public static void VerifyAll()
    {
        VerifyFocusMarks();
        VerifySecondAnimalReplacesOnlyWhenItsFocusCompletes();
        VerifyAimAwayKeepsMark();
        VerifyBriefAimBreakKeepsProgress();
        VerifyLongerAimBreakDecaysProgress();
        VerifyAngleDropsPastThreshold();
        VerifyOutOfRangeDrops();
        VerifyMissingEntityDrops();
        VerifyDeadBodyStays();
        VerifyNoUnlockClears();
        VerifyNeighborOneDegreeCloserStays();
        VerifyNeighborThreeDegreesCloserSwitches();
        VerifySeparatedAnimalSwitches();
        VerifyOutsideConeIgnored();
    }

    static void VerifyFocusMarks()
    {
        TrackMark mark = new();
        mark.Tick(2.9f, true, aimedLivingAnimalId: 7, markedPresent: false, 0f, 0f, Range, Focus, Angle);
        if (mark.MarkedId != null || mark.CandidateId != 7)
        {
            Assert.Fail("[prosequor] Tracker should still be aiming after 2.9s.");
        }

        mark.Tick(0.1f, true, aimedLivingAnimalId: 7, markedPresent: false, 0f, 0f, Range, Focus, Angle);
        if (mark.MarkedId != 7 || mark.CandidateId != null)
        {
            Assert.Fail("[prosequor] Tracker should mark after 3s on the same animal.");
        }
    }

    static void VerifySecondAnimalReplacesOnlyWhenItsFocusCompletes()
    {
        TrackMark mark = Marked(1);
        mark.Tick(2.9f, true, aimedLivingAnimalId: 2, markedPresent: true, 10f, 10f, Range, Focus, Angle);
        if (mark.MarkedId != 1)
        {
            Assert.Fail("[prosequor] A second animal should not replace the mark before its own 3s.");
        }

        mark.Tick(0.1f, true, aimedLivingAnimalId: 2, markedPresent: true, 10f, 10f, Range, Focus, Angle);
        if (mark.MarkedId != 2)
        {
            Assert.Fail("[prosequor] A second animal should replace the mark when its 3s completes.");
        }
    }

    static void VerifyAimAwayKeepsMark()
    {
        TrackMark mark = Marked(1);
        mark.Tick(5f, true, aimedLivingAnimalId: null, markedPresent: true, 10f, 40f, Range, Focus, Angle);
        if (mark.MarkedId != 1 || mark.CandidateId != null)
        {
            Assert.Fail("[prosequor] Looking off the crosshair should keep the mark and clear the candidate.");
        }
    }

    static void VerifyBriefAimBreakKeepsProgress()
    {
        TrackMark mark = new();
        mark.Tick(2f, true, aimedLivingAnimalId: 7, markedPresent: false, 0f, 0f, Range, Focus, Angle);
        float held = mark.CandidateSeconds;
        mark.Tick(
            TrackMark.AimGraceSeconds * 0.5f,
            true,
            aimedLivingAnimalId: null,
            markedPresent: false,
            0f,
            0f,
            Range,
            Focus,
            Angle);
        if (mark.CandidateId != 7 || Math.Abs(mark.CandidateSeconds - held) > 0.001f)
        {
            Assert.Fail("[prosequor] A brief break in aim should keep marking progress.");
        }

        mark.Tick(1f, true, aimedLivingAnimalId: 7, markedPresent: false, 0f, 0f, Range, Focus, Angle);
        if (mark.MarkedId != 7)
        {
            Assert.Fail("[prosequor] Progress kept through a brief break should still finish the mark.");
        }
    }

    static void VerifyLongerAimBreakDecaysProgress()
    {
        TrackMark mark = new();
        mark.Tick(2f, true, aimedLivingAnimalId: 7, markedPresent: false, 0f, 0f, Range, Focus, Angle);
        mark.Tick(
            TrackMark.AimGraceSeconds + 0.5f,
            true,
            aimedLivingAnimalId: null,
            markedPresent: false,
            0f,
            0f,
            Range,
            Focus,
            Angle);
        if (mark.CandidateId != 7 || Math.Abs(mark.CandidateSeconds - 1f) > 0.001f)
        {
            Assert.Fail("[prosequor] Aim lost past the grace window should drain marking progress.");
        }

        mark.Tick(0.6f, true, aimedLivingAnimalId: null, markedPresent: false, 0f, 0f, Range, Focus, Angle);
        if (mark.CandidateId != null || mark.CandidateSeconds > 0f)
        {
            Assert.Fail("[prosequor] A longer break in aim should drain marking progress to zero.");
        }
    }

    static void VerifyAngleDropsPastThreshold()
    {
        TrackMark atEdge = Marked(1);
        atEdge.Tick(0.1f, true, aimedLivingAnimalId: null, markedPresent: true, 10f, 90f, Range, Focus, Angle);
        if (atEdge.MarkedId != 1)
        {
            Assert.Fail("[prosequor] A mark at exactly 90 degrees should stay.");
        }

        TrackMark past = Marked(1);
        past.Tick(0.1f, true, aimedLivingAnimalId: null, markedPresent: true, 10f, 90.1f, Range, Focus, Angle);
        if (past.MarkedId != null)
        {
            Assert.Fail("[prosequor] A mark past 90 degrees should drop.");
        }
    }

    static void VerifyOutOfRangeDrops()
    {
        TrackMark atEdge = Marked(1);
        atEdge.Tick(0.1f, true, aimedLivingAnimalId: null, markedPresent: true, Range, 0f, Range, Focus, Angle);
        if (atEdge.MarkedId != 1)
        {
            Assert.Fail("[prosequor] A mark at exactly the range should stay.");
        }

        TrackMark past = Marked(1);
        past.Tick(0.1f, true, aimedLivingAnimalId: null, markedPresent: true, Range + 0.01f, 0f, Range, Focus, Angle);
        if (past.MarkedId != null)
        {
            Assert.Fail("[prosequor] A mark past range should drop.");
        }
    }

    static void VerifyMissingEntityDrops()
    {
        TrackMark mark = Marked(1);
        mark.Tick(0.1f, true, aimedLivingAnimalId: null, markedPresent: false, 0f, 0f, Range, Focus, Angle);
        if (mark.MarkedId != null)
        {
            Assert.Fail("[prosequor] A missing marked entity should drop the mark.");
        }
    }

    static void VerifyDeadBodyStays()
    {
        TrackMark mark = Marked(1);
        mark.Tick(1f, true, aimedLivingAnimalId: null, markedPresent: true, 12f, 20f, Range, Focus, Angle);
        if (mark.MarkedId != 1)
        {
            Assert.Fail("[prosequor] A dead body that is still present, in range, and in front should stay marked.");
        }
    }

    static void VerifyNoUnlockClears()
    {
        TrackMark mark = Marked(1);
        mark.Tick(0.1f, hasUnlock: false, aimedLivingAnimalId: 1, markedPresent: true, 5f, 0f, Range, Focus, Angle);
        if (mark.MarkedId != null || mark.CandidateId != null)
        {
            Assert.Fail("[prosequor] Losing Tracker should clear the mark.");
        }
    }

    static void VerifyNeighborOneDegreeCloserStays()
    {
        TrackAim.AimSample[] samples =
        [
            new(1, 2f, 0f),
            new(2, 1f, 0f),
        ];
        if (TrackAim.Select(heldId: 1, samples) != 1)
        {
            Assert.Fail("[prosequor] A neighbor only 1° closer to the cursor should stay unselected.");
        }
    }

    static void VerifyNeighborThreeDegreesCloserSwitches()
    {
        TrackAim.AimSample[] samples =
        [
            new(1, 4f, 0f),
            new(2, 1f, 0f),
        ];
        if (TrackAim.Select(heldId: 1, samples) != 2)
        {
            Assert.Fail("[prosequor] A neighbor 3° closer to the cursor should take the aim.");
        }
    }

    static void VerifySeparatedAnimalSwitches()
    {
        TrackAim.AimSample[] samples =
        [
            new(1, 3f, 0f),
            new(2, 1f, 20f),
        ];
        if (TrackAim.Select(heldId: 1, samples) != 2)
        {
            Assert.Fail("[prosequor] An animal far from the current one and closer to the cursor should take the aim.");
        }
    }

    static void VerifyOutsideConeIgnored()
    {
        TrackAim.AimSample[] outside = [new(1, 5f, 0f)];
        if (TrackAim.Select(heldId: null, outside) != null)
        {
            Assert.Fail("[prosequor] An animal outside the 4° cone should be ignored.");
        }

        TrackAim.AimSample[] mixed =
        [
            new(1, 2f, 0f),
            new(2, 4.5f, 1f),
        ];
        if (TrackAim.Select(heldId: null, mixed) != 1)
        {
            Assert.Fail("[prosequor] An animal outside the 4° cone should lose to one inside it.");
        }
    }

    static TrackMark Marked(long id)
    {
        TrackMark mark = new();
        mark.Tick(Focus, true, aimedLivingAnimalId: id, markedPresent: false, 0f, 0f, Range, Focus, Angle);
        if (mark.MarkedId != id)
        {
            Assert.Fail("[prosequor] Tracker fixture failed to establish a mark.");
        }

        return mark;
    }
}
