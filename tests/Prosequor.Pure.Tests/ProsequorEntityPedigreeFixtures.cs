using Prosequor.Ability;
using Prosequor.Xp.Activity;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Xunit;

namespace Prosequor.Pure.Tests;

/// <summary>
/// Pure Live tree contract for entity pedigree (no world). Entity host scenarios cover
/// WatchedAttributes roundtrip on a live player entity.
/// </summary>
public static class ProsequorEntityPedigreeFixtures
{
    public static void VerifyAll()
    {
        VerifyNullGuards();
        VerifyTreeContractStampAndContributors();
        VerifyCaptureApplyStackRoundtrip();
        VerifyFacadeDelegates();
    }

    static void VerifyNullGuards()
    {
        ProsequorEntityPedigreeStation.StampMaker(null, "uid");
        ProsequorEntityPedigreeStation.StampMaker(null, null);
        ProsequorEntityPedigreeStation.AddContributor(null, "uid");
        ProsequorEntityPedigreeStation.ClearContributors(null);
        ProsequorEntityPedigreeStation.Clear(null);
        ProsequorEntityPedigreeStation.CaptureFromStack(null, null);
        ProsequorEntityPedigreeStation.ApplyToStack(null, null);
        ProsequorEntityPedigreeStation.Copy(null, null);
        ProsequorEntityPedigreeStation.ApplyBlob(null, ProsequorBlob.Empty);

        if (ProsequorEntityPedigreeStation.TryGetBlob(null, out ProsequorBlob blob)
            || !blob.IsAnonymous)
        {
            Assert.Fail("[prosequor] TryGetBlob should fail on null entity.");
        }
    }

    static void VerifyTreeContractStampAndContributors()
    {
        TreeAttribute attrs = new();
        ProsequorBlob stamped = new ProsequorBlob("maker-a", new[] { "c1" }, recipe: null)
            .WithContributor("c1");
        ProsequorEntityPedigreeStation.WriteToTree(attrs, stamped);

        if (!ProsequorEntityPedigreeStation.TryReadFromTree(attrs, out ProsequorBlob got)
            || got.MakerUid != "maker-a"
            || !got.TryGetContributorWeight("c1", out int w)
            || w != 2
            || got.ContentHash != stamped.ContentHash)
        {
            Assert.Fail("[prosequor] Entity Live tree stamp/contributor contract failed.");
        }

        ProsequorBlob cleared = stamped.WithClearedContributors();
        if (cleared.MakerUid != "maker-a"
            || cleared.Contributors.Count != 0
            || cleared.ContentHash == stamped.ContentHash)
        {
            Assert.Fail("[prosequor] WithClearedContributors should keep maker and drop shares.");
        }

        IReadOnlyList<Deed.ContributorShare> real = HusbandryContributorXp.RealContributorShares(
            stamped.WithContributor(HusbandryFriendliness.AnonContributorUid, 9));
        if (real.Count != 1 || real[0].PlayerUid != "c1")
        {
            Assert.Fail("[prosequor] RealContributorShares should omit @ sentinels.");
        }

        // Clear-shaped: anonymous write is a no-op; remove Live manually for empty read.
        attrs.RemoveAttribute(ProsequorStackPedigree.LiveAttr);
        if (ProsequorEntityPedigreeStation.TryReadFromTree(attrs, out _))
        {
            Assert.Fail("[prosequor] Empty Live tree should read anonymous.");
        }
    }

    static void VerifyCaptureApplyStackRoundtrip()
    {
        ItemStack held = new() { StackSize = 1 };
        ProsequorStackPedigree.StampMaker(held, "from-stack");
        ProsequorStackPedigree.AddContributor(held, "firer");
        if (!ProsequorStackPedigree.TryGetPrimaryBlob(held, out ProsequorBlob unit))
        {
            Assert.Fail("[prosequor] Capture/apply: missing primary blob.");
            return;
        }

        // Simulate entity WatchedAttributes Live after CaptureFromStack.
        TreeAttribute watched = new();
        ProsequorEntityPedigreeStation.WriteToTree(watched, unit);

        if (!ProsequorEntityPedigreeStation.TryReadFromTree(watched, out ProsequorBlob onEntity)
            || onEntity.ContentHash != unit.ContentHash)
        {
            Assert.Fail("[prosequor] CaptureFromStack tree write should match stack primary.");
        }

        ItemStack drop = new() { StackSize = 1 };
        ProsequorStackPedigree.ApplyUnitBlob(drop, onEntity);
        if (!ProsequorStackPedigree.IsLive(drop)
            || !ProsequorStackPedigree.TryGetPrimaryBlob(drop, out ProsequorBlob dropBlob)
            || dropBlob.MakerUid != "from-stack"
            || !dropBlob.TryGetContributorWeight("firer", out int firer)
            || firer != 1)
        {
            Assert.Fail("[prosequor] Entity→stack ApplyUnitBlob roundtrip failed.");
        }

        // ApplyToStack skip when stack already has Live.
        ItemStack already = new() { StackSize = 1 };
        ProsequorStackPedigree.StampMaker(already, "keep-me");
        // Without a live Entity, skip path is covered by HasLive guard in station;
        // assert stack pedigree APIs agree.
        if (!ProsequorStackPedigree.HasLive(already))
        {
            Assert.Fail("[prosequor] expected Live on already-stamped stack.");
        }
    }

    static void VerifyFacadeDelegates()
    {
        ItemStack stack = new() { StackSize = 1 };
        ProsequorPedigree.StampMaker(stack, "facade-maker");
        ProsequorPedigree.AddContributor(stack, "facade-c");
        if (!ProsequorPedigree.TryGetBlob(stack, out ProsequorBlob blob)
            || blob.MakerUid != "facade-maker"
            || !blob.TryGetContributorWeight("facade-c", out int w)
            || w != 1)
        {
            Assert.Fail("[prosequor] ProsequorPedigree stack facade failed.");
        }

        ProsequorPedigree.Clear(stack);
        if (ProsequorPedigree.TryGetBlob(stack, out _))
        {
            Assert.Fail("[prosequor] ProsequorPedigree.Clear should remove stack Live.");
        }

        ProsequorPedigree.StampMaker((BlockEntity?)null, "x");
        ProsequorPedigree.StampMaker((Vintagestory.API.Common.Entities.Entity?)null, "x");
        ProsequorPedigree.Clear((BlockEntity?)null);
        ProsequorPedigree.Clear((Vintagestory.API.Common.Entities.Entity?)null);
        ProsequorPedigree.Copy(null, null);
    }
}
