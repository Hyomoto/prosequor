using Prosequor.Ability;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Xunit;

namespace Prosequor.Pure.Tests;

/// <summary>
/// Still batch quality: one locked blob per load, copied onto later distillate.
/// </summary>
public static class BoilerDistillBatchFixtures
{
    public static void VerifyAll()
    {
        VerifyLockedTreeRoundtripKeepsEmptyRoll();
        VerifyUnlockedTreeIsNotABatch();
        VerifyFirstDropStampsBatch();
        VerifySecondDropKeepsSamePrestige();
        VerifyAnonymousLeftoverTakesTheBatch();
    }

    static void VerifyLockedTreeRoundtripKeepsEmptyRoll()
    {
        TreeAttribute tree = new();
        BoilerDistillBatch.WriteLocked(tree, ProsequorBlob.Empty);
        if (!BoilerDistillBatch.TryReadLocked(tree, out ProsequorBlob read) || read.HasPersistable)
        {
            Assert.Fail("[prosequor] An empty distill roll must stay locked so the next tick does not reroll.");
        }

        ItemAffixEntry pure = new("intoxication-5", "prosequor:affix-intoxication-5", "#2A4849");
        ProsequorBlob batch = new ProsequorBlob("alice", null)
            .WithAffixes(new[] { pure })
            .WithMods(new[] { new ProsequorBlob.ModFactor("intoxication", 1.2f) });
        TreeAttribute labeled = new();
        BoilerDistillBatch.WriteLocked(labeled, batch);
        if (!BoilerDistillBatch.TryReadLocked(labeled, out ProsequorBlob restored)
            || restored.MakerUid != "alice"
            || restored.Affixes.Count != 1
            || Math.Abs(FindMod(restored.Mods, "intoxication") - 1.2f) > 0.0001f)
        {
            Assert.Fail("[prosequor] Locked distill batch should round-trip maker, affix, and mod.");
        }
    }

    static void VerifyUnlockedTreeIsNotABatch()
    {
        TreeAttribute tree = new();
        if (BoilerDistillBatch.TryReadLocked(tree, out _))
        {
            Assert.Fail("[prosequor] Missing distill batch must not look locked.");
        }
    }

    static void VerifyFirstDropStampsBatch()
    {
        ItemAffixEntry pure = new("intoxication-5", "prosequor:affix-intoxication-5", "#2A4849");
        ProsequorBlob batch = new ProsequorBlob("alice", null)
            .WithAffixes(new[] { pure })
            .WithMods(new[] { new ProsequorBlob.ModFactor("intoxication", 1.2f) });
        ItemStack content = new() { StackSize = 1, Attributes = new TreeAttribute() };

        BoilerDistillBatch.ApplyToPortion(content, batch, sinkQtyBefore: 0, moved: 1);
        if (!ProsequorStackPedigree.TryGetPrimaryBlob(content, out ProsequorBlob stamped)
            || stamped.MakerUid != "alice"
            || stamped.Affixes.Count != 1
            || ItemAffixes.GetAll(content).Count != 1)
        {
            Assert.Fail("[prosequor] First distill drop should take the locked batch as its pedigree.");
        }
    }

    static void VerifySecondDropKeepsSamePrestige()
    {
        ItemAffixEntry pure = new("intoxication-5", "prosequor:affix-intoxication-5", "#2A4849");
        ProsequorBlob batch = new ProsequorBlob("alice", null)
            .WithAffixes(new[] { pure })
            .WithMods(new[] { new ProsequorBlob.ModFactor("intoxication", 1.2f) });
        ItemStack content = new() { StackSize = 1, Attributes = new TreeAttribute() };
        BoilerDistillBatch.ApplyToPortion(content, batch, sinkQtyBefore: 0, moved: 1);

        content.StackSize = 2;
        BoilerDistillBatch.ApplyToPortion(content, batch, sinkQtyBefore: 1, moved: 1);
        if (!ProsequorStackPedigree.TryGetPrimaryBlob(content, out ProsequorBlob kept)
            || kept.MakerUid != "alice"
            || kept.Affixes.Count != 1
            || ItemAffixes.GetAll(content).Count != 1
            || Math.Abs(FindMod(kept.Mods, "intoxication") - 1.2f) > 0.0001f)
        {
            Assert.Fail("[prosequor] A later distill tick must copy the same batch, not reroll prestige.");
        }
    }

    static void VerifyAnonymousLeftoverTakesTheBatch()
    {
        ItemAffixEntry pure = new("intoxication-5", "prosequor:affix-intoxication-5", "#2A4849");
        ProsequorBlob batch = new ProsequorBlob("alice", null).WithAffixes(new[] { pure });
        ItemStack content = new() { StackSize = 2, Attributes = new TreeAttribute() };

        BoilerDistillBatch.ApplyToPortion(content, batch, sinkQtyBefore: 1, moved: 1);
        if (!ProsequorStackPedigree.TryGetPrimaryBlob(content, out ProsequorBlob claimed)
            || claimed.MakerUid != "alice"
            || claimed.Affixes.Count != 1)
        {
            Assert.Fail("[prosequor] Distill into unlabeled leftover should take the locked batch, not strip it.");
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
