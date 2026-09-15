using Prosequor.Xp;
using Xunit;

namespace Prosequor.Ability;

/// <summary>Pure clay-form XP math fixtures (no world required).</summary>
public static class ClayFormXpFixtures
{
    public static void VerifyAll()
    {
        VerifyCountGoodIgnoresExtrasAndEmpty();
        VerifyAnvilMetalCountGoodIgnoresSlagAndExtras();
        VerifyFirstBindPaysFromZero();
        VerifyDeltaPaysAboveMarkOnly();
        VerifyUndoReplacePaysNothing();
        VerifyRecipeKeyChangeResets();
        VerifyCopyFinishSizedJump();
        VerifyNullRecipePaysNothing();
    }

    static void VerifyCountGoodIgnoresExtrasAndEmpty()
    {
        bool[,,] want = new bool[2, 1, 2];
        bool[,,] have = new bool[2, 1, 2];
        want[0, 0, 0] = true;
        want[1, 0, 1] = true;
        have[0, 0, 0] = true; // good
        have[0, 0, 1] = true; // extra (recipe empty)
        // want[1,0,1] empty → not good
        if (ClayFormXpMath.CountGood(have, want, 1) != 1)
        {
            Assert.Fail("[prosequor] ClayForm XP fixture failed (count good ignores extras/empty).");
        }
    }

    static void VerifyAnvilMetalCountGoodIgnoresSlagAndExtras()
    {
        bool[,,] want = new bool[2, 1, 2];
        byte[,,] have = new byte[2, 1, 2];
        want[0, 0, 0] = true;
        want[1, 0, 1] = true;
        have[0, 0, 0] = AnvilVoxelGrid.Metal; // good
        have[0, 0, 1] = AnvilVoxelGrid.Metal; // extra
        have[1, 0, 1] = AnvilVoxelGrid.Slag; // wanted but slag → not good
        if (ClayFormXpMath.CountGood(have, want, 1, AnvilVoxelGrid.Metal) != 1)
        {
            Assert.Fail("[prosequor] Anvil XP fixture failed (metal count ignores slag/extras).");
        }
    }

    static void VerifyFirstBindPaysFromZero()
    {
        int paid = ClayFormXpMath.TakeDelta(
            good: 5,
            highWater: 0,
            storedKey: null,
            currentKey: "bowl",
            out int mark,
            out string? key);
        if (paid != 5 || mark != 5 || key != "bowl")
        {
            Assert.Fail(string.Format(
                "[prosequor] ClayForm XP fixture failed (first bind). paid={0} mark={1} key={2}.",
                paid,
                mark,
                key));
        }
    }

    static void VerifyDeltaPaysAboveMarkOnly()
    {
        int paid = ClayFormXpMath.TakeDelta(
            good: 5,
            highWater: 3,
            storedKey: "bowl",
            currentKey: "bowl",
            out int mark,
            out string? key);
        if (paid != 2 || mark != 5 || key != "bowl")
        {
            Assert.Fail(string.Format(
                "[prosequor] ClayForm XP fixture failed (delta above mark). paid={0} mark={1} key={2}.",
                paid,
                mark,
                key));
        }

        paid = ClayFormXpMath.TakeDelta(5, 5, "bowl", "bowl", out mark, out _);
        if (paid != 0 || mark != 5)
        {
            Assert.Fail("[prosequor] ClayForm XP fixture failed (no pay at mark).");
        }

        paid = ClayFormXpMath.TakeDelta(4, 5, "bowl", "bowl", out mark, out _);
        if (paid != 0 || mark != 5)
        {
            Assert.Fail("[prosequor] ClayForm XP fixture failed (below mark keeps high water).");
        }
    }

    static void VerifyUndoReplacePaysNothing()
    {
        int mark = 0;
        string? key = "bowl";
        int paid = ClayFormXpMath.TakeDelta(1, mark, key, "bowl", out mark, out key);
        if (paid != 1 || mark != 1)
        {
            Assert.Fail("[prosequor] ClayForm XP fixture failed (first good voxel).");
        }

        // undo: good drops, mark stays
        paid = ClayFormXpMath.TakeDelta(0, mark, key, "bowl", out mark, out key);
        if (paid != 0 || mark != 1)
        {
            Assert.Fail("[prosequor] ClayForm XP fixture failed (undo keeps mark).");
        }

        // replace same voxel
        paid = ClayFormXpMath.TakeDelta(1, mark, key, "bowl", out mark, out key);
        if (paid != 0 || mark != 1)
        {
            Assert.Fail("[prosequor] ClayForm XP fixture failed (replace pays nothing).");
        }
    }

    static void VerifyRecipeKeyChangeResets()
    {
        int paid = ClayFormXpMath.TakeDelta(
            good: 8,
            highWater: 3,
            storedKey: "bowl",
            currentKey: "pot",
            out int mark,
            out string? key);
        if (paid != 0 || mark != 8 || key != "pot")
        {
            Assert.Fail(string.Format(
                "[prosequor] ClayForm XP fixture failed (recipe reset). paid={0} mark={1} key={2}.",
                paid,
                mark,
                key));
        }
    }

    static void VerifyCopyFinishSizedJump()
    {
        int paid = ClayFormXpMath.TakeDelta(
            good: 40,
            highWater: 12,
            storedKey: "mold",
            currentKey: "mold",
            out int mark,
            out _);
        if (paid != 28 || mark != 40)
        {
            Assert.Fail(string.Format(
                "[prosequor] ClayForm XP fixture failed (copy/finish jump). paid={0} mark={1}.",
                paid,
                mark));
        }
    }

    static void VerifyNullRecipePaysNothing()
    {
        int paid = ClayFormXpMath.TakeDelta(5, 0, null, null, out int mark, out string? key);
        if (paid != 0 || mark != 0 || key != null)
        {
            Assert.Fail("[prosequor] ClayForm XP fixture failed (null recipe).");
        }
    }
}
