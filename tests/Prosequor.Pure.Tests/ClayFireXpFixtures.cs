using Prosequor.Ability;
using Prosequor.Xp.Activity;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Xunit;

namespace Prosequor.Xp;

/// <summary>Pure clay fire XP settle fixtures (no world required).</summary>
public static class ClayFireXpFixtures
{
    public static void VerifyAll()
    {
        VerifySharesFromBlob();
        VerifyBuildTokens();
        VerifyLegacyFlatCompatPromote();
        VerifyContributorIncrementsNotReorder();
        VerifyAmountTableLookup();
    }

    static void VerifySharesFromBlob()
    {
        ProsequorBlob empty = ProsequorBlob.Empty;
        if (ClayFireXpMath.SharesFromBlob(empty).Count != 0
            || ClayFireXpMath.SharesFromBlob(null).Count != 0)
        {
            Assert.Fail("[prosequor] anonymous blob should yield no kiln shares.");
        }

        ProsequorBlob blob = new ProsequorBlob("maker", Array.Empty<ProsequorBlob.Share>())
            .WithContributor("a", 2)
            .WithContributor("b", 1);
        IReadOnlyList<Deed.ContributorShare> shares = ClayFireXpMath.SharesFromBlob(blob);
        if (shares.Count != 2
            || Math.Abs(shares.First(s => s.PlayerUid == "a").Weight - 2f) > 0.001f
            || Math.Abs(shares.First(s => s.PlayerUid == "b").Weight - 1f) > 0.001f)
        {
            Assert.Fail("[prosequor] SharesFromBlob should expose weighted pedigree shares.");
        }
    }

    static void VerifyBuildTokens()
    {
        List<string> tokens = ClayFireXpMath.BuildTokens();
        if (tokens.Count != 1 || tokens[0] != ClayFireXpMath.TokenKilnFired)
        {
            Assert.Fail("[prosequor] ClayFire XP fixture failed (build tokens).");
        }
    }

    static void VerifyLegacyFlatCompatPromote()
    {
        ItemStack stack = new() { StackSize = 1 };
        stack.Attributes.SetString(CraftAttribution.MakerAttr, "legacy-maker");
        stack.Attributes.SetString(CraftAttribution.FirerAttr, "legacy-firer");

        if (CraftAttribution.TryGetMakerUid(stack) != "legacy-maker"
            || !CraftAttribution.HasContributor(stack, "legacy-firer"))
        {
            Assert.Fail("[prosequor] Legacy flat compat-read failed.");
        }

        if (!ProsequorStackPedigree.TryGetPrimaryBlob(stack, out ProsequorBlob blob)
            || blob.MakerUid != "legacy-maker"
            || !blob.TryGetContributorWeight("legacy-firer", out int weight)
            || weight != 1)
        {
            Assert.Fail("[prosequor] Legacy flat promote-into-blob failed.");
        }

        if (stack.Attributes.HasAttribute(CraftAttribution.MakerAttr)
            || stack.Attributes.HasAttribute(CraftAttribution.FirerAttr))
        {
            Assert.Fail("[prosequor] Legacy flats should be removed after promote.");
        }

        ItemStack fresh = new() { StackSize = 1 };
        CraftAttribution.StampMakerUid(fresh, "new-maker");
        CraftAttribution.StampFirerUid(fresh, "new-firer");
        if (fresh.Attributes.HasAttribute(CraftAttribution.MakerAttr)
            || fresh.Attributes.HasAttribute(CraftAttribution.FirerAttr)
            || CraftAttribution.TryGetMakerUid(fresh) != "new-maker"
            || !CraftAttribution.HasContributor(fresh, "new-firer")
            || CraftAttribution.TryGetFirerUid(fresh) != "new-firer")
        {
            Assert.Fail("[prosequor] New stamp should be pedigree-only.");
        }
    }

    static void VerifyContributorIncrementsNotReorder()
    {
        ProsequorBlob blob = new ProsequorBlob("m", new[] { "a", "b" }, recipe: null).WithContributor("a");
        if (!blob.TryGetContributorWeight("a", out int aWeight)
            || aWeight != 2
            || !blob.TryGetContributorWeight("b", out int bWeight)
            || bWeight != 1
            || blob.Contributors.Count != 2)
        {
            Assert.Fail("[prosequor] WithContributor should increment weight, not reorder.");
        }

        // Legacy ordered list migrates to weight 1 each.
        TreeAttribute tree = new();
        ITreeAttribute contrib = tree.GetOrAddTreeAttribute(ProsequorBlob.ContributorsKey);
        contrib.SetString("0", "x");
        contrib.SetString("1", "y");
        contrib.SetInt(ProsequorBlob.ContributorCountKey, 2);
        tree.SetString(ProsequorBlob.MakerKey, "m");
        ProsequorBlob migrated = ProsequorBlob.ReadFrom(tree);
        if (!migrated.TryGetContributorWeight("x", out int xw)
            || xw != 1
            || !migrated.TryGetContributorWeight("y", out int yw)
            || yw != 1)
        {
            Assert.Fail("[prosequor] Legacy ordered contributor list should migrate to weight 1.");
        }
    }

    static void VerifyAmountTableLookup()
    {
        float[] table = [1f, 5f, 10f];
        if (ClayFireXpMath.PickAmount(table, voxelsPerUnit: 0, minVoxelsPerUnit: 10, maxVoxelsPerUnit: 100) != 1f)
        {
            Assert.Fail("[prosequor] Missing voxels should pick table[0].");
        }

        if (ClayFireXpMath.PickAmount(table, 10, 10, 100) != 1f
            || ClayFireXpMath.PickAmount(table, 100, 10, 100) != 10f
            || ClayFireXpMath.PickAmount(table, 55, 10, 100) != 5f)
        {
            Assert.Fail("[prosequor] Voxel amount table band pick failed.");
        }

        // Four bowls: total voxels divided by 4.
        int total = 80;
        int perUnit = (int)Math.Ceiling(total / 4.0);
        if (perUnit != 20)
        {
            Assert.Fail("[prosequor] Output division fixture failed.");
        }
    }
}
