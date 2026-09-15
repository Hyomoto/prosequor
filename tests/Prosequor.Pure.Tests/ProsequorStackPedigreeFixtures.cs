using Prosequor.Ability;
using Vintagestory.API.Common;
using Xunit;

namespace Prosequor.Pure.Tests;

/// <summary>Pure Live/Frozen pedigree fixtures (ItemStack attrs only; no world).</summary>
public static class ProsequorStackPedigreeFixtures
{
    public static void VerifyAll()
    {
        VerifyBlobHashStableAndDistinct();
        VerifyContributorIncrementsWeight();
        VerifySoleContributorReplacesBag();
        VerifyCoalesceMergesEqualHashes();
        VerifyTakeFromFrontSplitsGroup();
        VerifyStampMakerLiveOnSizeOne();
        VerifyStampMakerFrozenOnMulti();
        VerifyAddContributorRewritesFrozen();
        VerifySplitViaAfterTakeOutPromotesLive();
        VerifyMergeAfterMovePeelsFifo();
        VerifyHomogeneousAndUnique();
        VerifyEnsurePadsAnonymous();
        VerifyPeelPreferMatchBuried();
        VerifyPeelPreferMatchMergeKeepsSinkHomogeneous();
        VerifyPeelMinFragmentTakesWholeGroup();
        VerifyPeelMinFragmentHalfSplit();
        VerifyApplyUnitBlobLiveAndFrozen();
        VerifyPlacePickupTreeContract();
        VerifyDuplicatePrimaryExpandsAttribution();
        VerifyQuantityShortfallPreservesBlobQty();
        VerifyRecipeExcludedFromHashFirstWins();
        VerifyAnvilSplitsExcludedFromHashAndRoundTrip();
        VerifyQualityRankJoinsHashAndRoundTrip();
        VerifyAffixesAndModsJoinHashAndRoundTrip();
        VerifyTakeOutPeelsAffixes();
        VerifyLegacyStackAffixesAbsorbIntoBlob();
        VerifyPlacePickupRestoresAffixes();
    }

    static void VerifyBlobHashStableAndDistinct()
    {
        ProsequorBlob a = new("m1", new[] { "c1" }, recipe: null);
        ProsequorBlob a2 = new("m1", new[] { "c1" }, recipe: null);
        ProsequorBlob b = new("m1", new[] { "c2" }, recipe: null);
        if (a.ContentHash != a2.ContentHash || a != a2)
        {
            Assert.Fail("[prosequor] Blob hash fixture failed (stable equality).");
        }

        if (a.ContentHash == b.ContentHash || a == b)
        {
            Assert.Fail("[prosequor] Blob hash fixture failed (distinct contributors).");
        }
    }

    static void VerifyContributorIncrementsWeight()
    {
        ProsequorBlob blob = new ProsequorBlob("m", new[] { "a", "b" }, recipe: null).WithContributor("a");
        if (blob.Contributors.Count != 2
            || !blob.TryGetContributorWeight("a", out int aWeight)
            || aWeight != 2
            || !blob.TryGetContributorWeight("b", out int bWeight)
            || bWeight != 1)
        {
            Assert.Fail("[prosequor] Contributor weight increment fixture failed.");
        }

        // Different weights → different hash.
        ProsequorBlob heavier = blob.WithContributor("a");
        if (heavier.ContentHash == blob.ContentHash)
        {
            Assert.Fail("[prosequor] Contributor weight should affect ContentHash.");
        }
    }

    static void VerifySoleContributorReplacesBag()
    {
        ProsequorBlob multi = new ProsequorBlob("maker", new[] { "a", "b" }, recipe: "r1")
            .WithContributor("a");
        ProsequorBlob sole = multi.WithSoleContributor("starter");
        if (sole.MakerUid != "maker"
            || sole.Recipe != "r1"
            || sole.Contributors.Count != 1
            || !sole.TryGetSoleContributor(out string? uid)
            || uid != "starter"
            || !sole.TryGetContributorWeight("starter", out int weight)
            || weight != 1
            || sole.TryGetContributorWeight("a", out _))
        {
            Assert.Fail("[prosequor] WithSoleContributor should replace the bag and keep maker/recipe.");
        }

        if (ProsequorBlob.Empty.TryGetSoleContributor(out _)
            || multi.TryGetSoleContributor(out _))
        {
            Assert.Fail("[prosequor] TryGetSoleContributor should be false for 0 or 2+ shares.");
        }

        ProsequorBlob cleared = sole.WithSoleContributor(null);
        if (cleared.Contributors.Count != 0 || cleared.MakerUid != "maker")
        {
            Assert.Fail("[prosequor] WithSoleContributor(null) should clear contributors only.");
        }
    }

    static void VerifyCoalesceMergesEqualHashes()
    {
        ProsequorBlob blob = new("m", null);
        var groups = new List<ProsequorStackPedigree.FrozenGroup>
        {
            new(blob, 2),
            new(ProsequorBlob.Empty, 1),
            new(blob, 3),
        };
        List<ProsequorStackPedigree.FrozenGroup> coalesced = ProsequorStackPedigree.Coalesce(groups);
        if (coalesced.Count != 2
            || coalesced[0].Qty != 5
            || coalesced[0].Blob.MakerUid != "m"
            || coalesced[1].Qty != 1
            || !coalesced[1].Blob.IsAnonymous)
        {
            Assert.Fail("[prosequor] Coalesce fixture failed.");
        }
    }

    static void VerifyTakeFromFrontSplitsGroup()
    {
        ProsequorBlob blob = new("m", null);
        var groups = new List<ProsequorStackPedigree.FrozenGroup> { new(blob, 5) };
        List<ProsequorStackPedigree.FrozenGroup> taken =
            ProsequorStackPedigree.TakeFromFront(groups, 2, out List<ProsequorStackPedigree.FrozenGroup> left);
        if (taken.Count != 1 || taken[0].Qty != 2
            || left.Count != 1 || left[0].Qty != 3
            || taken[0].Blob.ContentHash != blob.ContentHash)
        {
            Assert.Fail("[prosequor] TakeFromFront fixture failed.");
        }
    }

    static void VerifyStampMakerLiveOnSizeOne()
    {
        ItemStack stack = new() { StackSize = 1 };
        ProsequorStackPedigree.StampMaker(stack, "maker-a");
        if (!ProsequorStackPedigree.IsLive(stack)
            || !ProsequorStackPedigree.TryGetPrimaryBlob(stack, out ProsequorBlob blob)
            || blob.MakerUid != "maker-a"
            || ProsequorStackPedigree.HasFrozen(stack))
        {
            Assert.Fail("[prosequor] Size-1 StampMaker → Live fixture failed.");
        }
    }

    static void VerifyStampMakerFrozenOnMulti()
    {
        ItemStack stack = new() { StackSize = 4 };
        ProsequorStackPedigree.StampMaker(stack, "maker-b");
        if (ProsequorStackPedigree.IsLive(stack)
            || !ProsequorStackPedigree.HasFrozen(stack)
            || !ProsequorStackPedigree.IsHomogeneous(stack))
        {
            Assert.Fail("[prosequor] Multi StampMaker → Frozen fixture failed.");
        }

        IReadOnlyList<ProsequorStackPedigree.FrozenGroup> groups =
            ProsequorStackPedigree.ReadFrozenGroups(stack);
        if (groups.Count != 1 || groups[0].Qty != 4 || groups[0].Blob.MakerUid != "maker-b")
        {
            Assert.Fail("[prosequor] Multi StampMaker group qty fixture failed.");
        }
    }

    static void VerifyAddContributorRewritesFrozen()
    {
        ItemStack stack = new() { StackSize = 3 };
        ProsequorStackPedigree.StampMaker(stack, "m");
        ProsequorStackPedigree.AddContributor(stack, "firer-1");
        if (!ProsequorStackPedigree.TryGetPrimaryBlob(stack, out ProsequorBlob blob)
            || !blob.TryGetContributorWeight("firer-1", out int weight)
            || weight != 1
            || blob.MakerUid != "m")
        {
            Assert.Fail("[prosequor] AddContributor rewrite fixture failed.");
        }
    }

    static void VerifySplitViaAfterTakeOutPromotesLive()
    {
        ItemStack source = new() { StackSize = 3 };
        ProsequorStackPedigree.StampMaker(source, "m");
        ProsequorStackPedigree.SnapshotForTakeOut(
            source,
            out List<ProsequorStackPedigree.FrozenGroup> bag,
            out bool hadLive,
            out ProsequorBlob? liveBlob);

        // Simulate TakeOut(1): source left at 2, moved size 1.
        source.StackSize = 2;
        ItemStack moved = new() { StackSize = 1 };
        ProsequorStackPedigree.AfterTakeOut(source, moved, bag, hadLive, liveBlob);

        if (!ProsequorStackPedigree.IsLive(moved)
            || !ProsequorStackPedigree.TryGetPrimaryBlob(moved, out ProsequorBlob movedBlob)
            || movedBlob.MakerUid != "m")
        {
            Assert.Fail("[prosequor] TakeOut promote-to-Live fixture failed.");
        }

        if (ProsequorStackPedigree.IsLive(source)
            || ProsequorStackPedigree.TotalQty(ProsequorStackPedigree.ReadFrozenGroups(source)) != 2)
        {
            Assert.Fail("[prosequor] TakeOut source leftover fixture failed.");
        }
    }

    static void VerifyMergeAfterMovePeelsFifo()
    {
        ItemStack sink = new() { StackSize = 2 };
        ProsequorStackPedigree.StampMaker(sink, "sink-maker");

        ItemStack source = new() { StackSize = 5 };
        ProsequorStackPedigree.StampMaker(source, "src-maker");
        // Simulate vanilla moved 3: source left at 2, bag still full until MergeAfterMove.
        source.StackSize = 2;
        sink.StackSize = 5;
        ProsequorStackPedigree.MergeAfterMove(sink, source, movedQty: 3);

        IReadOnlyList<ProsequorStackPedigree.FrozenGroup> sinkGroups =
            ProsequorStackPedigree.ReadFrozenGroups(sink);
        int sinkTotal = ProsequorStackPedigree.TotalQty(sinkGroups);
        int srcTotal = ProsequorStackPedigree.TotalQty(ProsequorStackPedigree.ReadFrozenGroups(source));
        if (sinkTotal != 5 || srcTotal != 2)
        {
            Assert.Fail($"[prosequor] Merge qty fixture failed (sink={sinkTotal}, src={srcTotal}).");
        }

        // Sink should not be homogeneous (two makers).
        if (ProsequorStackPedigree.IsHomogeneous(sink))
        {
            Assert.Fail("[prosequor] Merge should yield heterogeneous Frozen bag.");
        }
    }

    static void VerifyHomogeneousAndUnique()
    {
        ItemStack live = new() { StackSize = 1 };
        ProsequorStackPedigree.StampMaker(live, "m");
        if (!ProsequorStackPedigree.IsHomogeneous(live) || !ProsequorStackPedigree.IsLive(live))
        {
            Assert.Fail("[prosequor] Live homogeneous fixture failed.");
        }

        ItemStack empty = new() { StackSize = 8 };
        if (!ProsequorStackPedigree.IsHomogeneous(empty))
        {
            Assert.Fail("[prosequor] Empty stack should be homogeneous.");
        }
    }

    static void VerifyEnsurePadsAnonymous()
    {
        // Name kept for callers; Ensure now allows shortfall and only clamps over-count.
        ItemStack shortfall = new() { StackSize = 5 };
        ProsequorStackPedigree.WriteFrozenGroups(
            shortfall,
            new List<ProsequorStackPedigree.FrozenGroup> { new(new ProsequorBlob("m", null), 2) });
        ProsequorStackPedigree.EnsureFrozenMatchesStackSize(shortfall);
        int shortTotal = ProsequorStackPedigree.TotalQty(ProsequorStackPedigree.ReadFrozenGroups(shortfall));
        if (shortTotal != 2)
        {
            Assert.Fail($"[prosequor] Ensure should keep shortfall (total={shortTotal}).");
        }

        ItemStack over = new() { StackSize = 2 };
        ProsequorStackPedigree.WriteFrozenGroups(
            over,
            new List<ProsequorStackPedigree.FrozenGroup> { new(new ProsequorBlob("m", null), 5) });
        ProsequorStackPedigree.EnsureFrozenMatchesStackSize(over);
        int overTotal = ProsequorStackPedigree.TotalQty(ProsequorStackPedigree.ReadFrozenGroups(over));
        if (overTotal != 2)
        {
            Assert.Fail($"[prosequor] Ensure clamp fixture failed (total={overTotal}).");
        }
    }

    static void VerifyPeelPreferMatchBuried()
    {
        ProsequorBlob other = new("other", null);
        ProsequorBlob match = new("match", null);
        var groups = new List<ProsequorStackPedigree.FrozenGroup>
        {
            new(other, 2),
            new(match, 3),
        };
        HashSet<string> preferred = new(StringComparer.Ordinal) { match.ContentHash };
        List<ProsequorStackPedigree.FrozenGroup> taken =
            ProsequorStackPedigree.PeelPreferMatch(groups, 1, preferred, out List<ProsequorStackPedigree.FrozenGroup> left);
        if (taken.Count != 1 || taken[0].Blob.MakerUid != "match" || taken[0].Qty != 1)
        {
            Assert.Fail("[prosequor] PreferMatch buried peel fixture failed (taken).");
        }

        if (ProsequorStackPedigree.TotalQty(left) != 4
            || left.All(g => g.Blob.MakerUid != "other"))
        {
            Assert.Fail("[prosequor] PreferMatch buried peel fixture failed (leftover).");
        }
    }

    static void VerifyPeelPreferMatchMergeKeepsSinkHomogeneous()
    {
        ItemStack sink = new() { StackSize = 2 };
        ProsequorStackPedigree.StampMaker(sink, "same");

        ItemStack source = new() { StackSize = 4 };
        ProsequorBlob other = new("other", null);
        ProsequorBlob same = new("same", null);
        ProsequorStackPedigree.WriteFrozenGroups(
            source,
            new List<ProsequorStackPedigree.FrozenGroup>
            {
                new(other, 2),
                new(same, 2),
            });

        source.StackSize = 3;
        sink.StackSize = 3;
        ProsequorStackPedigree.MergeAfterMove(sink, source, movedQty: 1);

        if (!ProsequorStackPedigree.IsHomogeneous(sink)
            || !ProsequorStackPedigree.TryGetPrimaryBlob(sink, out ProsequorBlob sinkBlob)
            || sinkBlob.MakerUid != "same")
        {
            Assert.Fail("[prosequor] PreferMatch merge should keep sink homogeneous.");
        }
    }

    static void VerifyPeelMinFragmentTakesWholeGroup()
    {
        ProsequorBlob a = new("A", null);
        ProsequorBlob b = new("B", null);
        ProsequorBlob c = new("C", null);
        var groups = new List<ProsequorStackPedigree.FrozenGroup>
        {
            new(a, 3),
            new(b, 2),
            new(c, 1),
        };
        List<ProsequorStackPedigree.FrozenGroup> taken =
            ProsequorStackPedigree.PeelMinFragment(groups, 3, out List<ProsequorStackPedigree.FrozenGroup> left);
        if (taken.Count != 1 || taken[0].Blob.MakerUid != "A" || taken[0].Qty != 3)
        {
            Assert.Fail("[prosequor] MinFragment should take whole A:3.");
        }

        if (left.Count != 2 || ProsequorStackPedigree.TotalQty(left) != 3)
        {
            Assert.Fail("[prosequor] MinFragment leftover should be B+C.");
        }
    }

    static void VerifyPeelMinFragmentHalfSplit()
    {
        ProsequorBlob blob = new("m", null);
        var groups = new List<ProsequorStackPedigree.FrozenGroup> { new(blob, 5) };
        List<ProsequorStackPedigree.FrozenGroup> taken =
            ProsequorStackPedigree.PeelMinFragment(groups, 3, out List<ProsequorStackPedigree.FrozenGroup> left);
        if (taken.Count != 1 || taken[0].Qty != 3 || left.Count != 1 || left[0].Qty != 2)
        {
            Assert.Fail("[prosequor] MinFragment half-split fixture failed.");
        }
    }

    static void VerifyApplyUnitBlobLiveAndFrozen()
    {
        ProsequorBlob blob = new("place-maker", new[] { "c1" }, recipe: null);
        ItemStack live = new() { StackSize = 1 };
        ProsequorStackPedigree.ApplyUnitBlob(live, blob);
        if (!ProsequorStackPedigree.IsLive(live)
            || !ProsequorStackPedigree.TryGetPrimaryBlob(live, out ProsequorBlob got)
            || got.MakerUid != "place-maker"
            || !got.TryGetContributorWeight("c1", out int c1)
            || c1 != 1)
        {
            Assert.Fail("[prosequor] ApplyUnitBlob Live fixture failed.");
        }

        ItemStack frozen = new() { StackSize = 3 };
        ProsequorStackPedigree.ApplyUnitBlob(frozen, blob);
        if (ProsequorStackPedigree.IsLive(frozen)
            || ProsequorStackPedigree.TotalQty(ProsequorStackPedigree.ReadFrozenGroups(frozen)) != 1
            || !ProsequorStackPedigree.TryGetPrimaryBlob(frozen, out ProsequorBlob frozenBlob)
            || frozenBlob.MakerUid != "place-maker")
        {
            Assert.Fail("[prosequor] ApplyUnitBlob should stamp blob×1 with shortfall on multi.");
        }
    }

    /// <summary>
    /// Place stores one Live tree on the BE; pickup rehydrates that blob onto a fresh drop.
    /// </summary>
    static void VerifyPlacePickupTreeContract()
    {
        ItemStack held = new() { StackSize = 4 };
        ProsequorStackPedigree.StampMaker(held, "from-stack");
        ProsequorStackPedigree.AddContributor(held, "firer");
        if (!ProsequorStackPedigree.TryGetPrimaryBlob(held, out ProsequorBlob unit))
        {
            Assert.Fail("[prosequor] Place/pickup: missing primary blob.");
            return;
        }

        // Simulate BE.ToTreeAttributes payload (world object = one unit).
        Vintagestory.API.Datastructures.TreeAttribute beTree = new();
        unit.WriteTo(beTree.GetOrAddTreeAttribute(ProsequorStackPedigree.LiveAttr));

        ProsequorBlob restored = ProsequorBlob.ReadFrom(
            beTree.GetTreeAttribute(ProsequorStackPedigree.LiveAttr));
        ItemStack drop = new() { StackSize = 1 };
        ProsequorStackPedigree.ApplyUnitBlob(drop, restored);

        if (!ProsequorStackPedigree.IsLive(drop)
            || !ProsequorStackPedigree.TryGetPrimaryBlob(drop, out ProsequorBlob dropBlob)
            || dropBlob.MakerUid != "from-stack"
            || !dropBlob.TryGetContributorWeight("firer", out int firerWeight)
            || firerWeight != 1
            || dropBlob.ContentHash != unit.ContentHash)
        {
            Assert.Fail("[prosequor] Place/pickup tree contract fixture failed.");
        }
    }

    static void VerifyDuplicatePrimaryExpandsAttribution()
    {
        ItemStack stack = new() { StackSize = 1 };
        ProsequorStackPedigree.StampMaker(stack, "craft-maker");
        stack.StackSize = 3;
        ProsequorStackPedigree.DuplicatePrimaryToMatchStackSize(stack);
        if (ProsequorStackPedigree.TotalQty(ProsequorStackPedigree.ReadFrozenGroups(stack)) != 3
            || !ProsequorStackPedigree.IsHomogeneous(stack)
            || !ProsequorStackPedigree.TryGetPrimaryBlob(stack, out ProsequorBlob blob)
            || blob.MakerUid != "craft-maker")
        {
            Assert.Fail("[prosequor] DuplicatePrimary craft-expand fixture failed.");
        }
    }

    static void VerifyQuantityShortfallPreservesBlobQty()
    {
        ItemStack drop = new() { StackSize = 1 };
        ProsequorStackPedigree.StampMaker(drop, "break-maker");
        drop.StackSize = 3;
        ProsequorStackPedigree.EnsureFrozenMatchesStackSize(drop);
        int total = ProsequorStackPedigree.TotalQty(ProsequorStackPedigree.ReadFrozenGroups(drop));
        if (total != 1
            || !ProsequorStackPedigree.TryGetPrimaryBlob(drop, out ProsequorBlob blob)
            || blob.MakerUid != "break-maker")
        {
            Assert.Fail($"[prosequor] Break shortfall fixture failed (total={total}).");
        }
    }

    static void VerifyRecipeExcludedFromHashFirstWins()
    {
        ProsequorBlob a = new("m", new[] { "c" }, "game:recipes/clayforming/bowl");
        ProsequorBlob b = new("m", new[] { "c" }, "game:recipes/clayforming/fourbowls");
        if (a.ContentHash != b.ContentHash || a != b)
        {
            Assert.Fail("[prosequor] Recipe must not affect ContentHash / equality.");
        }

        ProsequorBlob readyA = a.WithFriendlinessReadyAt(12.5);
        ProsequorBlob readyB = a.WithFriendlinessReadyAt(99);
        if (readyA.ContentHash != a.ContentHash
            || readyB.ContentHash != a.ContentHash
            || readyA != a
            || readyB != a)
        {
            Assert.Fail("[prosequor] FriendlinessReadyAt must not affect ContentHash / equality.");
        }

        var groups = new List<ProsequorStackPedigree.FrozenGroup>
        {
            new(a, 2),
            new(b, 3),
        };
        List<ProsequorStackPedigree.FrozenGroup> coalesced = ProsequorStackPedigree.Coalesce(groups);
        if (coalesced.Count != 1
            || coalesced[0].Qty != 5
            || coalesced[0].Blob.Recipe != "game:recipes/clayforming/bowl")
        {
            Assert.Fail("[prosequor] Coalesce should first-win recipe and sum qty.");
        }

        ItemStack stack = new() { StackSize = 1 };
        ProsequorStackPedigree.StampMaker(stack, "m");
        ProsequorStackPedigree.StampRecipe(stack, "game:recipes/clayforming/bowl");
        if (!ProsequorStackPedigree.TryGetRecipeKey(stack, out string? key)
            || key != "game:recipes/clayforming/bowl")
        {
            Assert.Fail("[prosequor] StampRecipe / TryGetRecipeKey failed.");
        }
    }

    static void VerifyAnvilSplitsExcludedFromHashAndRoundTrip()
    {
        ProsequorBlob baseBlob = new("smith", Array.Empty<ProsequorBlob.Share>(), recipe: null);
        ProsequorBlob withSplits = baseBlob.WithAnvilSplits(5);
        ProsequorBlob otherSplits = baseBlob.WithAnvilSplits(2);
        if (withSplits.ContentHash != baseBlob.ContentHash
            || otherSplits.ContentHash != baseBlob.ContentHash
            || withSplits != baseBlob
            || otherSplits != baseBlob
            || withSplits.AnvilSplits != 5)
        {
            Assert.Fail("[prosequor] AnvilSplits must not affect ContentHash / equality.");
        }

        ItemStack stack = new() { StackSize = 1 };
        ProsequorStackPedigree.ApplyUnitBlob(stack, withSplits);
        if (!ProsequorStackPedigree.TryGetPrimaryBlob(stack, out ProsequorBlob roundTrip)
            || roundTrip.AnvilSplits != 5
            || roundTrip.MakerUid != "smith")
        {
            Assert.Fail("[prosequor] AnvilSplits Live round-trip failed.");
        }

        ProsequorStackPedigree.ApplyUnitBlob(stack, roundTrip.WithAnvilSplits(0));
        if (!ProsequorStackPedigree.TryGetPrimaryBlob(stack, out ProsequorBlob cleared)
            || cleared.AnvilSplits != 0)
        {
            Assert.Fail("[prosequor] AnvilSplits reset to 0 failed.");
        }
    }

    static void VerifyQualityRankJoinsHashAndRoundTrip()
    {
        ProsequorBlob baseBlob = new("cook", Array.Empty<ProsequorBlob.Share>(), recipe: null);
        ProsequorBlob ranked = baseBlob.WithQualityRank(12);
        ProsequorBlob other = baseBlob.WithQualityRank(18);
        if (ranked.ContentHash == baseBlob.ContentHash
            || ranked.ContentHash == other.ContentHash
            || ranked == baseBlob
            || ranked == other
            || ranked.QualityRank != 12
            || baseBlob.WithQualityRank(0).ContentHash != baseBlob.ContentHash)
        {
            Assert.Fail("[prosequor] QualityRank must join ContentHash / equality (0 = unset).");
        }

        ItemStack stack = new() { StackSize = 1 };
        ProsequorStackPedigree.ApplyUnitBlob(stack, ranked);
        if (!ProsequorStackPedigree.TryGetQualityRank(stack, out int rank)
            || rank != 12
            || !ProsequorStackPedigree.TryGetPrimaryBlob(stack, out ProsequorBlob roundTrip)
            || roundTrip.QualityRank != 12
            || roundTrip.MakerUid != "cook")
        {
            Assert.Fail("[prosequor] QualityRank Live round-trip failed.");
        }

        ProsequorStackPedigree.StampQualityRank(stack, 0);
        if (ProsequorStackPedigree.TryGetQualityRank(stack, out _)
            || !ProsequorStackPedigree.TryGetPrimaryBlob(stack, out ProsequorBlob cleared)
            || cleared.QualityRank != 0)
        {
            Assert.Fail("[prosequor] QualityRank reset to 0 failed.");
        }
    }

    static void VerifyAffixesAndModsJoinHashAndRoundTrip()
    {
        ProsequorBlob baseBlob = new("m", new[] { "c" }, recipe: null);
        ItemAffixEntry sturdy = new("sturdy", "prosequor:affix-sturdy", "#84ff84");
        ItemAffixEntry keen = new("keen", "prosequor:affix-keen", "#ff8484");
        ProsequorBlob.ModFactor durability = new("durability", 1.25f);
        ProsequorBlob withAffix = baseBlob.WithAffixes(new[] { sturdy });
        ProsequorBlob otherAffix = baseBlob.WithAffixes(new[] { keen });
        ProsequorBlob withMod = baseBlob.WithMods(new[] { durability });
        if (withAffix.ContentHash == baseBlob.ContentHash
            || withAffix.ContentHash == otherAffix.ContentHash
            || withMod.ContentHash == baseBlob.ContentHash
            || withAffix == baseBlob
            || withAffix == otherAffix)
        {
            Assert.Fail("[prosequor] Affixes and mods must affect ContentHash / equality.");
        }

        if (baseBlob.WithRecipe("r1").ContentHash != baseBlob.ContentHash)
        {
            Assert.Fail("[prosequor] Recipe must still be excluded from hash after surface migrate.");
        }

        ItemStack stack = new() { StackSize = 1 };
        ProsequorStackPedigree.ApplyUnitBlob(stack, withAffix.WithMods(new[] { durability }));
        if (!ProsequorStackPedigree.TryGetPrimaryBlob(stack, out ProsequorBlob roundTrip)
            || roundTrip.Affixes.Count != 1
            || roundTrip.Affixes[0].Code != "sturdy"
            || roundTrip.Mods.Count != 1
            || roundTrip.Mods[0].Key != "durability"
            || ItemAffixes.GetAll(stack).Count != 1
            || ItemAffixes.GetAll(stack)[0].Code != "sturdy"
            || Math.Abs(CraftAttributeMods.GetFactor(stack, "durability") - 1.25f) > 0.0001f)
        {
            Assert.Fail("[prosequor] Affix/mod Live round-trip / materialize failed.");
        }
    }

    static void VerifyTakeOutPeelsAffixes()
    {
        ItemAffixEntry sturdy = new("sturdy", "prosequor:affix-sturdy", "#84ff84");
        ItemAffixEntry keen = new("keen", "prosequor:affix-keen", "#ff8484");
        ProsequorBlob sturdyBlob = new ProsequorBlob("m", null).WithAffixes(new[] { sturdy });
        ProsequorBlob keenBlob = new ProsequorBlob("m", null).WithAffixes(new[] { keen });
        if (sturdyBlob.ContentHash == keenBlob.ContentHash)
        {
            Assert.Fail("[prosequor] Different affixes must not coalesce.");
        }

        ItemStack source = new() { StackSize = 3 };
        ProsequorStackPedigree.WriteFrozenGroups(
            source,
            new List<ProsequorStackPedigree.FrozenGroup>
            {
                new(sturdyBlob, 2),
                new(keenBlob, 1),
            });
        if (ProsequorStackPedigree.IsHomogeneous(source)
            || ProsequorStackPedigree.ReadFrozenGroups(source).Count != 2)
        {
            Assert.Fail("[prosequor] Mixed affix Frozen bag should stay two groups.");
        }

        IReadOnlyList<ItemAffixEntry> face = ItemAffixes.GetAll(source);
        if (face.Count != 1 || face[0].Code != "sturdy")
        {
            Assert.Fail("[prosequor] Heterogeneous stack should show the primary unit's affixes.");
        }

        ProsequorStackPedigree.SnapshotForTakeOut(
            source,
            out List<ProsequorStackPedigree.FrozenGroup> bag,
            out bool hadLive,
            out ProsequorBlob? liveBlob);
        source.StackSize = 2;
        ItemStack moved = new() { StackSize = 1 };
        ProsequorStackPedigree.AfterTakeOut(source, moved, bag, hadLive, liveBlob);

        IReadOnlyList<ItemAffixEntry> movedAffixes = ItemAffixes.GetAll(moved);
        // Min-fragment peel takes the whole keen×1 rather than splitting sturdy×2.
        if (movedAffixes.Count != 1 || movedAffixes[0].Code != "keen")
        {
            Assert.Fail("[prosequor] TakeOut should materialize the peeled unit's affixes.");
        }

        IReadOnlyList<ItemAffixEntry> leftAffixes = ItemAffixes.GetAll(source);
        if (leftAffixes.Count != 1 || leftAffixes[0].Code != "sturdy")
        {
            Assert.Fail("[prosequor] Source leftover should show remaining primary affixes.");
        }
    }

    static void VerifyLegacyStackAffixesAbsorbIntoBlob()
    {
        ItemStack stack = new() { StackSize = 1 };
        ProsequorStackPedigree.StampMaker(stack, "legacy");
        // Write the stack tree without going through Add (no auto-stamp).
        ItemAffixes.WriteAll(
            stack,
            new[] { new ItemAffixEntry("cool", "prosequor:affix-cool", "#99c9f9") });
        if (ProsequorStackPedigree.TryGetPrimaryBlob(stack, out ProsequorBlob before)
            && before.Affixes.Count != 0)
        {
            Assert.Fail("[prosequor] Legacy write should not have stamped the blob yet.");
        }

        ProsequorStackPedigree.AbsorbLegacySurface(stack);
        if (!ProsequorStackPedigree.TryGetPrimaryBlob(stack, out ProsequorBlob after)
            || after.Affixes.Count != 1
            || after.Affixes[0].Code != "cool")
        {
            Assert.Fail("[prosequor] AbsorbLegacySurface should fold stack affixes into the blob.");
        }
    }

    static void VerifyPlacePickupRestoresAffixes()
    {
        ItemStack held = new() { StackSize = 1 };
        ProsequorStackPedigree.StampMaker(held, "from-stack");
        ItemAffixes.Add(held, "fortified", "prosequor:affix-fortified", "#84ff84");
        if (!ProsequorStackPedigree.TryGetPrimaryBlob(held, out ProsequorBlob unit)
            || unit.Affixes.Count != 1)
        {
            Assert.Fail("[prosequor] Place/pickup affix: missing surface on held blob.");
            return;
        }

        Vintagestory.API.Datastructures.TreeAttribute beTree = new();
        unit.WriteTo(beTree.GetOrAddTreeAttribute(ProsequorStackPedigree.LiveAttr));
        ProsequorBlob restored = ProsequorBlob.ReadFrom(
            beTree.GetTreeAttribute(ProsequorStackPedigree.LiveAttr));
        ItemStack drop = new() { StackSize = 1 };
        ProsequorStackPedigree.ApplyUnitBlob(drop, restored);

        IReadOnlyList<ItemAffixEntry> dropAffixes = ItemAffixes.GetAll(drop);
        if (!ProsequorStackPedigree.IsLive(drop)
            || dropAffixes.Count != 1
            || dropAffixes[0].Code != "fortified"
            || dropAffixes[0].Color != "#84ff84")
        {
            Assert.Fail("[prosequor] Place/pickup should restore affixes onto the rebuilt stack.");
        }
    }
}
