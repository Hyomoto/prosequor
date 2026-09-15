using Prosequor.Ability;
using Vintagestory.API.Common;
using Xunit;

namespace Prosequor.Pure.Tests;

/// <summary>
/// Liquid pedigree reduce: prestige keep/strip + weighted mod average (blob-only; no collectible).
/// </summary>
public static class ProsequorLiquidPedigreeFixtures
{
    public static void VerifyAll()
    {
        VerifySamePrestigeRequiresMakerAndAffixes();
        VerifyAverageModsWeightedAndMissingIsOne();
        VerifyAverageQualityRankWeighted();
        VerifyForDistillateStripsQualityAndRank();
        VerifyQualityBaseBonusFromMashRank();
        VerifyMergeKeepsPrestigeAveragesMods();
        VerifyMergeAveragesQualityRank();
        VerifyMergeStripsDifferentMakers();
        VerifyMergeStripsDifferentAffixes();
        VerifyMergeLabeledIntoAnonymousStrips();
        VerifyMergeBothUnlabeledKeepsNoLabel();
        VerifyHashEqualIsIdentity();
        VerifyCollapseGroupsMatchesPairwise();
        VerifyApplyMergeWritePath();
    }

    static void VerifySamePrestigeRequiresMakerAndAffixes()
    {
        ItemAffixEntry pure = new("intoxication-5", "prosequor:affix-intoxication-5", "#2A4849");
        ItemAffixEntry crown = new("liquor-stock-5", "prosequor:affix-liquor-stock-5", "#84c7ff");
        ProsequorBlob a = new ProsequorBlob("alice", null).WithAffixes(new[] { pure, crown });
        ProsequorBlob same = new ProsequorBlob("alice", null)
            .WithAffixes(new[] { pure, crown })
            .WithMods(new[] { new ProsequorBlob.ModFactor("intoxication", 1.33f) });
        ProsequorBlob otherMaker = new ProsequorBlob("bob", null).WithAffixes(new[] { pure, crown });
        ProsequorBlob otherAffix = new ProsequorBlob("alice", null).WithAffixes(new[] { pure });
        ProsequorBlob anon = ProsequorBlob.Empty;

        if (!ProsequorLiquidPedigree.SamePrestige(a, same)
            || ProsequorLiquidPedigree.SamePrestige(a, otherMaker)
            || ProsequorLiquidPedigree.SamePrestige(a, otherAffix)
            || !ProsequorLiquidPedigree.SamePrestige(anon, ProsequorBlob.Empty)
            || ProsequorLiquidPedigree.SamePrestige(a, anon))
        {
            Assert.Fail("[prosequor] SamePrestige maker+affix contract failed.");
        }
    }

    static void VerifyAverageModsWeightedAndMissingIsOne()
    {
        // 2 L @ 1.33 + 1 L missing key (→1.0) => (2*1.33 + 1*1.0) / 3 = 1.22
        IReadOnlyList<ProsequorBlob.ModFactor> sink = new[]
        {
            new ProsequorBlob.ModFactor("intoxication", 1.33f),
            new ProsequorBlob.ModFactor("price", 1.2f),
        };
        IReadOnlyList<ProsequorBlob.ModFactor> src = new[]
        {
            new ProsequorBlob.ModFactor("price", 1.1f),
        };

        IReadOnlyList<ProsequorBlob.ModFactor> avg =
            ProsequorLiquidPedigree.AverageMods(sink, 2, src, 1);
        float intox = FindMod(avg, "intoxication");
        float price = FindMod(avg, "price");
        if (Math.Abs(intox - ((2f * 1.33f) + 1f) / 3f) > 0.0001f
            || Math.Abs(price - ((2f * 1.2f) + 1.1f) / 3f) > 0.0001f)
        {
            Assert.Fail($"[prosequor] AverageMods failed (intox={intox}, price={price}).");
        }
    }

    static void VerifyAverageQualityRankWeighted()
    {
        if (ProsequorLiquidPedigree.AverageQualityRank(10, 2, 20, 1) != 13
            || ProsequorLiquidPedigree.AverageQualityRank(12, 1, 0, 1) != 6
            || ProsequorLiquidPedigree.AverageQualityRank(8, 0, 15, 3) != 15
            || ProsequorLiquidPedigree.AverageQualityRank(0, 0, 0, 0) != 0)
        {
            Assert.Fail("[prosequor] AverageQualityRank weighted contract failed.");
        }
    }

    static void VerifyMergeAveragesQualityRank()
    {
        ItemAffixEntry quality = new(ItemAffixes.QualityCode, "prosequor:affix-quality-3", null);
        ProsequorBlob sink = new ProsequorBlob("alice", null)
            .WithAffixes(new[] { quality })
            .WithQualityRank(10);
        ProsequorBlob src = new ProsequorBlob("alice", null)
            .WithAffixes(new[] { quality })
            .WithQualityRank(20);

        ProsequorBlob kept = ProsequorLiquidPedigree.MergeBlobs(sink, 1, src, 1);
        if (kept.MakerUid != "alice" || kept.QualityRank != 15)
        {
            Assert.Fail("[prosequor] Prestige-match merge should average qualityRank.");
        }

        ProsequorBlob otherMaker = new ProsequorBlob("bob", null)
            .WithAffixes(new[] { quality })
            .WithQualityRank(20);
        ProsequorBlob stripped = ProsequorLiquidPedigree.MergeBlobs(sink, 1, otherMaker, 1);
        if (stripped.MakerUid != null
            || stripped.Affixes.Count != 0
            || stripped.QualityRank != 15)
        {
            Assert.Fail("[prosequor] Prestige-miss merge should strip labels and keep averaged qualityRank.");
        }
    }

    static void VerifyForDistillateStripsQualityAndRank()
    {
        ItemAffixEntry quality = new(ItemAffixes.QualityCode, "prosequor:affix-quality-3", null);
        ItemAffixEntry pure = new("intoxication-5", "prosequor:affix-intoxication-5", null);
        ProsequorBlob mash = new ProsequorBlob("alice", null)
            .WithAffixes(new[] { quality, pure })
            .WithQualityRank(4)
            .WithMods(new[] { new ProsequorBlob.ModFactor("intoxication", 1.2f) });

        ProsequorBlob spirit = ProsequorLiquidPedigree.ForDistillate(mash);
        if (spirit.MakerUid != "alice"
            || spirit.QualityRank != 0
            || spirit.Affixes.Count != 1
            || spirit.Affixes[0].Code != "intoxication-5"
            || Math.Abs(FindMod(spirit.Mods, "intoxication") - 1.2f) > 0.0001f)
        {
            Assert.Fail("[prosequor] ForDistillate must keep maker/mods, drop quality grade and rank.");
        }

        IReadOnlyList<ItemAffixEntry> stripped = ItemAffixes.WithoutQuality(mash.Affixes);
        if (stripped.Count != 1 || stripped[0].Code != "intoxication-5")
        {
            Assert.Fail("[prosequor] WithoutQuality should drop only the quality grade.");
        }

        ItemStack stack = new() { StackSize = 1 };
        ProsequorLiquidPedigree.WriteFullBlob(
            stack,
            mash.WithAffixes(new[] { quality, pure }).WithQualityRank(4));
        ProsequorLiquidPedigree.StripQualityAndRank(stack);
        if (!ProsequorStackPedigree.TryGetPrimaryBlob(stack, out ProsequorBlob after)
            || after.QualityRank != 0
            || ItemAffixes.GetAll(stack).Count != 1
            || ItemAffixes.GetAll(stack)[0].Code != "intoxication-5"
            || ProsequorStackPedigree.TryGetQualityRank(stack, out _))
        {
            Assert.Fail("[prosequor] StripQualityAndRank must drop grade and rank, keep other affixes.");
        }
    }

    static void VerifyQualityBaseBonusFromMashRank()
    {
        if (Math.Abs(ProsequorLiquidPedigree.QualityBaseBonus(4) - 4f) > 0.0001f
            || ProsequorLiquidPedigree.QualityBaseBonus(0) != 0f
            || ProsequorLiquidPedigree.QualityBaseBonus(-3) != 0f)
        {
            Assert.Fail("[prosequor] Mash qualityRank must add to quality-base when set.");
        }
    }

    static void VerifyMergeKeepsPrestigeAveragesMods()
    {
        ItemAffixEntry pure = new("intoxication-5", "prosequor:affix-intoxication-5", null);
        ProsequorBlob sink = new ProsequorBlob("alice", new[] { "helper" }, recipe: null)
            .WithAffixes(new[] { pure })
            .WithMods(new[] { new ProsequorBlob.ModFactor("intoxication", 1.2f) });
        ProsequorBlob src = new ProsequorBlob("alice", null)
            .WithAffixes(new[] { pure })
            .WithMods(new[] { new ProsequorBlob.ModFactor("intoxication", 1.4f) });

        ProsequorBlob merged = ProsequorLiquidPedigree.MergeBlobs(sink, 1, src, 1);
        if (merged.MakerUid != "alice"
            || merged.Affixes.Count != 1
            || merged.Affixes[0].Code != "intoxication-5"
            || merged.Contributors.Count != 1
            || Math.Abs(FindMod(merged.Mods, "intoxication") - 1.3f) > 0.0001f)
        {
            Assert.Fail("[prosequor] Prestige-match merge should keep labels and average mods.");
        }
    }

    static void VerifyMergeStripsDifferentMakers()
    {
        ItemAffixEntry pure = new("intoxication-5", "prosequor:affix-intoxication-5", null);
        ProsequorBlob sink = new ProsequorBlob("alice", new[] { "c" }, recipe: null)
            .WithAffixes(new[] { pure })
            .WithMods(new[] { new ProsequorBlob.ModFactor("intoxication", 1.33f) });
        ProsequorBlob src = new ProsequorBlob("bob", null)
            .WithAffixes(new[] { pure })
            .WithMods(new[] { new ProsequorBlob.ModFactor("intoxication", 1.1f) });

        ProsequorBlob merged = ProsequorLiquidPedigree.MergeBlobs(sink, 2, src, 1);
        float expected = (2f * 1.33f + 1.1f) / 3f;
        if (merged.MakerUid != null
            || merged.Affixes.Count != 0
            || merged.Contributors.Count != 0
            || Math.Abs(FindMod(merged.Mods, "intoxication") - expected) > 0.0001f)
        {
            Assert.Fail("[prosequor] Different makers must strip prestige and keep averaged strength.");
        }
    }

    static void VerifyMergeStripsDifferentAffixes()
    {
        ItemAffixEntry pure = new("intoxication-5", "prosequor:affix-intoxication-5", null);
        ItemAffixEntry mild = new("intoxication-1", "prosequor:affix-intoxication-1", null);
        ProsequorBlob sink = new ProsequorBlob("alice", null).WithAffixes(new[] { pure });
        ProsequorBlob src = new ProsequorBlob("alice", null).WithAffixes(new[] { mild });

        ProsequorBlob merged = ProsequorLiquidPedigree.MergeBlobs(sink, 1, src, 1);
        if (merged.MakerUid != null || merged.Affixes.Count != 0)
        {
            Assert.Fail("[prosequor] Different affixes must strip prestige.");
        }
    }

    static void VerifyMergeLabeledIntoAnonymousStrips()
    {
        ItemAffixEntry crown = new("liquor-stock-5", "prosequor:affix-liquor-stock-5", null);
        ProsequorBlob labeled = new ProsequorBlob("alice", null)
            .WithAffixes(new[] { crown })
            .WithMods(new[] { new ProsequorBlob.ModFactor("price", 1.5f) });
        ProsequorBlob anon = ProsequorBlob.Empty;

        ProsequorBlob merged = ProsequorLiquidPedigree.MergeBlobs(labeled, 1, anon, 1);
        if (merged.MakerUid != null
            || merged.Affixes.Count != 0
            || Math.Abs(FindMod(merged.Mods, "price") - 1.25f) > 0.0001f)
        {
            Assert.Fail("[prosequor] Labeled into anonymous must strip and average toward 1.0.");
        }
    }

    static void VerifyMergeBothUnlabeledKeepsNoLabel()
    {
        ProsequorBlob a = new ProsequorBlob(null, null)
            .WithMods(new[] { new ProsequorBlob.ModFactor("intoxication", 1.2f) });
        ProsequorBlob b = new ProsequorBlob(null, null)
            .WithMods(new[] { new ProsequorBlob.ModFactor("intoxication", 1.4f) });

        ProsequorBlob merged = ProsequorLiquidPedigree.MergeBlobs(a, 1, b, 1);
        if (merged.MakerUid != null
            || merged.Affixes.Count != 0
            || Math.Abs(FindMod(merged.Mods, "intoxication") - 1.3f) > 0.0001f)
        {
            Assert.Fail("[prosequor] Unlabeled wells should stay unlabeled with averaged mods.");
        }
    }

    static void VerifyHashEqualIsIdentity()
    {
        ItemAffixEntry pure = new("intoxication-5", "prosequor:affix-intoxication-5", null);
        ProsequorBlob blob = new ProsequorBlob("alice", null)
            .WithAffixes(new[] { pure })
            .WithMods(new[] { new ProsequorBlob.ModFactor("intoxication", 1.25f) });

        ProsequorBlob merged = ProsequorLiquidPedigree.MergeBlobs(blob, 3, blob, 2);
        if (merged.ContentHash != blob.ContentHash
            || merged.MakerUid != "alice"
            || merged.Affixes.Count != 1
            || Math.Abs(FindMod(merged.Mods, "intoxication") - 1.25f) > 0.0001f)
        {
            Assert.Fail("[prosequor] Hash-equal dilute should be an identity keep.");
        }
    }

    static void VerifyCollapseGroupsMatchesPairwise()
    {
        ItemAffixEntry a = new("intoxication-5", "prosequor:affix-intoxication-5", null);
        ItemAffixEntry b = new("intoxication-1", "prosequor:affix-intoxication-1", null);
        ProsequorBlob first = new ProsequorBlob("alice", null)
            .WithAffixes(new[] { a })
            .WithMods(new[] { new ProsequorBlob.ModFactor("intoxication", 1.5f) });
        ProsequorBlob second = new ProsequorBlob("bob", null)
            .WithAffixes(new[] { b })
            .WithMods(new[] { new ProsequorBlob.ModFactor("intoxication", 1.1f) });
        ProsequorBlob third = ProsequorBlob.Empty.WithMods(
            new[] { new ProsequorBlob.ModFactor("intoxication", 1.0f) });

        ProsequorBlob pairwise = ProsequorLiquidPedigree.MergeBlobs(first, 2, second, 1);
        pairwise = ProsequorLiquidPedigree.MergeBlobs(pairwise, 3, third, 1);

        ProsequorBlob collapsed = ProsequorLiquidPedigree.CollapseGroups(
            new List<ProsequorStackPedigree.FrozenGroup>
            {
                new(first, 2),
                new(second, 1),
                new(third, 1),
            });

        if (collapsed.ContentHash != pairwise.ContentHash
            || collapsed.MakerUid != null
            || collapsed.Affixes.Count != 0
            || Math.Abs(
                FindMod(collapsed.Mods, "intoxication")
                - FindMod(pairwise.Mods, "intoxication"))
                > 0.0001f)
        {
            Assert.Fail("[prosequor] CollapseGroups must match successive MergeBlobs.");
        }
    }

    static void VerifyApplyMergeWritePath()
    {
        ItemAffixEntry pure = new("intoxication-5", "prosequor:affix-intoxication-5", null);
        ItemStack sink = new() { StackSize = 2 };
        ProsequorLiquidPedigree.WriteFullBlob(
            sink,
            new ProsequorBlob("alice", null)
                .WithAffixes(new[] { pure })
                .WithMods(new[] { new ProsequorBlob.ModFactor("intoxication", 1.2f) }));

        ProsequorBlob src = new ProsequorBlob("bob", null)
            .WithAffixes(new[] { pure })
            .WithMods(new[] { new ProsequorBlob.ModFactor("intoxication", 1.8f) });

        // Simulate vanilla StackSize++ then ApplyMerge with prior size.
        sink.StackSize = 3;
        ProsequorLiquidPedigree.ApplyMerge(sink, src, sinkQtyBefore: 2, movedQty: 1);

        if (!ProsequorStackPedigree.TryGetPrimaryBlob(sink, out ProsequorBlob after)
            || after.MakerUid != null
            || after.Affixes.Count != 0
            || ItemAffixes.GetAll(sink).Count != 0
            || Math.Abs(FindMod(after.Mods, "intoxication") - 1.4f) > 0.0001f
            || Math.Abs(CraftAttributeMods.GetFactor(sink, "intoxication") - 1.4f) > 0.0001f
            || !ProsequorStackPedigree.IsHomogeneous(sink))
        {
            Assert.Fail("[prosequor] ApplyMerge write path should strip, average, and stay homogeneous.");
        }

        // Prestige match keeps labels on write.
        ItemStack keep = new() { StackSize = 1 };
        ProsequorBlob labeled = new ProsequorBlob("alice", null)
            .WithAffixes(new[] { pure })
            .WithMods(new[] { new ProsequorBlob.ModFactor("intoxication", 1.0f) });
        ProsequorLiquidPedigree.WriteFullBlob(keep, labeled);
        keep.StackSize = 2;
        ProsequorLiquidPedigree.ApplyMerge(
            keep,
            labeled.WithMods(new[] { new ProsequorBlob.ModFactor("intoxication", 1.5f) }),
            sinkQtyBefore: 1,
            movedQty: 1);

        if (!ProsequorStackPedigree.TryGetPrimaryBlob(keep, out ProsequorBlob kept)
            || kept.MakerUid != "alice"
            || kept.Affixes.Count != 1
            || ItemAffixes.GetAll(keep).Count != 1
            || Math.Abs(FindMod(kept.Mods, "intoxication") - 1.25f) > 0.0001f)
        {
            Assert.Fail("[prosequor] ApplyMerge prestige-match write should keep labels.");
        }
    }

    static float FindMod(IReadOnlyList<ProsequorBlob.ModFactor> mods, string key)
    {
        for (int i = 0; i < mods.Count; i++)
        {
            if (string.Equals(mods[i].Key, key, StringComparison.OrdinalIgnoreCase))
            {
                return mods[i].Factor;
            }
        }

        return 1f;
    }
}
