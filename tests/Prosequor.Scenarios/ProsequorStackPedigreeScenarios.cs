using Atlas.Api;
using Atlas.XUnit;
using Prosequor.Ability;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Xunit;

namespace Prosequor.Scenarios;

/// <summary>
/// Live TakeOut / TryPutInto / merge paths for Live/Frozen pedigree movement.
/// </summary>
public class ProsequorStackPedigreeScenarios : AtlasScenarioBase
{
    public ProsequorStackPedigreeScenarios()
    {
        ProsequorStackPedigree.EnsurePedigreeIgnoredForMerge();
    }

    [AtlasScenario]
    [Trait("Layer", "Pedigree")]
    [Trait("Kind", "Stamp")]
    public async Task Stamp_SizeOne_Should_BeLive()
    {
        _ = await World.JoinPlayer("PedStampLive");
        ItemStack stack = NewStackable(1);
        ProsequorStackPedigree.StampMaker(stack, "maker-live");

        Assert.True(ProsequorStackPedigree.IsLive(stack));
        Assert.True(ProsequorStackPedigree.TryGetPrimaryBlob(stack, out ProsequorBlob blob));
        Assert.Equal("maker-live", blob.MakerUid);
        AssertPedigreeMatchesSize(stack);
    }

    [AtlasScenario]
    [Trait("Layer", "Pedigree")]
    [Trait("Kind", "Stamp")]
    public async Task Stamp_Multi_Should_BeFrozenHomogeneous()
    {
        _ = await World.JoinPlayer("PedStampMulti");
        ItemStack stack = NewStackable(4);
        ProsequorStackPedigree.StampMaker(stack, "maker-multi");

        Assert.False(ProsequorStackPedigree.IsLive(stack));
        Assert.True(ProsequorStackPedigree.HasFrozen(stack));
        Assert.True(ProsequorStackPedigree.IsHomogeneous(stack));
        IReadOnlyList<ProsequorStackPedigree.FrozenGroup> groups =
            ProsequorStackPedigree.ReadFrozenGroups(stack);
        Assert.Single(groups);
        Assert.Equal(4, groups[0].Qty);
        Assert.Equal("maker-multi", groups[0].Blob.MakerUid);
        AssertPedigreeMatchesSize(stack);
    }

    [AtlasScenario]
    [Trait("Layer", "Pedigree")]
    [Trait("Kind", "TakeOut")]
    public async Task TakeOut_Half_FromHomogeneous_Should_PreserveMakerAndQtys()
    {
        _ = await World.JoinPlayer("PedHalf");
        ItemSlot slot = new DummySlot(NewStackable(5));
        ProsequorStackPedigree.StampMaker(slot.Itemstack, "half-maker");

        int take = (int)Math.Ceiling(slot.StackSize / 2f);
        ItemStack? moved = slot.TakeOut(take);
        Assert.NotNull(moved);
        Assert.Equal(take, moved!.StackSize);
        Assert.Equal(5 - take, slot.StackSize);

        AssertPedigreeMatchesSize(moved);
        AssertPedigreeMatchesSize(slot.Itemstack);
        Assert.True(ProsequorStackPedigree.TryGetPrimaryBlob(moved, out ProsequorBlob movedBlob));
        Assert.Equal("half-maker", movedBlob.MakerUid);
        if (slot.StackSize == 1)
        {
            Assert.True(ProsequorStackPedigree.IsLive(slot.Itemstack));
        }
        else
        {
            Assert.True(ProsequorStackPedigree.HasFrozen(slot.Itemstack));
        }

        if (moved.StackSize == 1)
        {
            Assert.True(ProsequorStackPedigree.IsLive(moved));
        }
    }

    [AtlasScenario]
    [Trait("Layer", "Pedigree")]
    [Trait("Kind", "TakeOut")]
    public async Task TakeOut_One_FromMulti_Should_PromoteMovedToLive()
    {
        _ = await World.JoinPlayer("PedTakeOne");
        ItemSlot slot = new DummySlot(NewStackable(4));
        ProsequorStackPedigree.StampMaker(slot.Itemstack, "one-maker");

        ItemStack? moved = slot.TakeOut(1);
        Assert.NotNull(moved);
        Assert.Equal(1, moved!.StackSize);
        Assert.Equal(3, slot.StackSize);
        Assert.True(ProsequorStackPedigree.IsLive(moved));
        Assert.True(ProsequorStackPedigree.TryGetPrimaryBlob(moved, out ProsequorBlob blob));
        Assert.Equal("one-maker", blob.MakerUid);
        Assert.Equal(3, ProsequorStackPedigree.TotalQty(ProsequorStackPedigree.ReadFrozenGroups(slot.Itemstack)));
        AssertPedigreeMatchesSize(moved);
        AssertPedigreeMatchesSize(slot.Itemstack);
    }

    [AtlasScenario]
    [Trait("Layer", "Pedigree")]
    [Trait("Kind", "TakeOut")]
    public async Task TakeOutWhole_Should_MoveFullPedigree()
    {
        _ = await World.JoinPlayer("PedTakeWhole");
        ItemSlot slot = new DummySlot(NewStackable(3));
        ProsequorStackPedigree.StampMaker(slot.Itemstack, "whole-maker");

        ItemStack? moved = slot.TakeOut(3);
        Assert.NotNull(moved);
        Assert.True(slot.Empty);
        Assert.Equal(3, moved!.StackSize);
        Assert.True(ProsequorStackPedigree.TryGetPrimaryBlob(moved, out ProsequorBlob blob));
        Assert.Equal("whole-maker", blob.MakerUid);
        AssertPedigreeMatchesSize(moved);
    }

    [AtlasScenario]
    [Trait("Layer", "Pedigree")]
    [Trait("Kind", "PutInto")]
    public async Task TryPutInto_EmptySink_Should_CarryPedigree()
    {
        ITestPlayer joined = await World.JoinPlayer("PedPutEmpty");
        ItemSlot source = new DummySlot(NewStackable(4));
        ProsequorStackPedigree.StampMaker(source.Itemstack, "put-maker");
        ItemSlot sink = new DummySlot();

        ItemStackMoveOperation op = NewOp(joined.Player, requestedQuantity: 2);
        int moved = source.TryPutInto(sink, ref op);
        Assert.Equal(2, moved);
        Assert.Equal(2, sink.StackSize);
        Assert.Equal(2, source.StackSize);
        Assert.True(ProsequorStackPedigree.TryGetPrimaryBlob(sink.Itemstack, out ProsequorBlob blob));
        Assert.Equal("put-maker", blob.MakerUid);
        AssertPedigreeMatchesSize(sink.Itemstack);
        AssertPedigreeMatchesSize(source.Itemstack);
    }

    [AtlasScenario]
    [Trait("Layer", "Pedigree")]
    [Trait("Kind", "Merge")]
    public async Task DepositOne_OntoMatchingSink_Should_StayHomogeneous()
    {
        ITestPlayer joined = await World.JoinPlayer("PedDepositMatch");
        ItemSlot sink = new DummySlot(NewStackable(2));
        ProsequorStackPedigree.StampMaker(sink.Itemstack, "same");
        ItemSlot source = new DummySlot(NewStackable(3));
        ProsequorStackPedigree.StampMaker(source.Itemstack, "same");

        ItemStackMoveOperation op = NewOp(joined.Player, requestedQuantity: 1);
        int moved = source.TryPutInto(sink, ref op);
        Assert.Equal(1, moved);
        Assert.Equal(3, sink.StackSize);
        Assert.Equal(2, source.StackSize);
        Assert.True(ProsequorStackPedigree.IsHomogeneous(sink.Itemstack));
        Assert.True(ProsequorStackPedigree.TryGetPrimaryBlob(sink.Itemstack, out ProsequorBlob blob));
        Assert.Equal("same", blob.MakerUid);
        AssertPedigreeMatchesSize(sink.Itemstack);
        AssertPedigreeMatchesSize(source.Itemstack);
    }

    [AtlasScenario]
    [Trait("Layer", "Pedigree")]
    [Trait("Kind", "Merge")]
    public async Task DepositOne_Should_PreferBuriedMatchingBlob()
    {
        ITestPlayer joined = await World.JoinPlayer("PedDepositBuried");
        ItemSlot sink = new DummySlot(NewStackable(2));
        ProsequorStackPedigree.StampMaker(sink.Itemstack, "match");

        ItemSlot source = new DummySlot(NewStackable(4));
        ProsequorBlob other = new("other", null);
        ProsequorBlob match = new("match", null);
        ProsequorStackPedigree.WriteFrozenGroups(
            source.Itemstack,
            new List<ProsequorStackPedigree.FrozenGroup>
            {
                new(other, 2),
                new(match, 2),
            });

        ItemStackMoveOperation op = NewOp(joined.Player, requestedQuantity: 1);
        int moved = source.TryPutInto(sink, ref op);
        Assert.Equal(1, moved);
        Assert.True(
            ProsequorStackPedigree.IsHomogeneous(sink.Itemstack),
            "Sink should stay homogeneous when a matching unit exists on source.");
        Assert.True(ProsequorStackPedigree.TryGetPrimaryBlob(sink.Itemstack, out ProsequorBlob sinkBlob));
        Assert.Equal("match", sinkBlob.MakerUid);

        // Source should still have both makers (or other + remaining match).
        Assert.False(ProsequorStackPedigree.IsHomogeneous(source.Itemstack));
        AssertPedigreeMatchesSize(sink.Itemstack);
        AssertPedigreeMatchesSize(source.Itemstack);
    }

    [AtlasScenario]
    [Trait("Layer", "Pedigree")]
    [Trait("Kind", "Merge")]
    public async Task MergeFull_MatchingMakers_Should_Coalesce()
    {
        ITestPlayer joined = await World.JoinPlayer("PedMergeFull");
        ItemSlot sink = new DummySlot(NewStackable(2));
        ProsequorStackPedigree.StampMaker(sink.Itemstack, "coalesce");
        ItemSlot source = new DummySlot(NewStackable(3));
        ProsequorStackPedigree.StampMaker(source.Itemstack, "coalesce");

        ItemStackMoveOperation op = NewOp(joined.Player, requestedQuantity: 99);
        int moved = source.TryPutInto(sink, ref op);
        Assert.Equal(3, moved);
        Assert.Equal(5, sink.StackSize);
        Assert.True(source.Empty);
        Assert.True(ProsequorStackPedigree.IsHomogeneous(sink.Itemstack));
        Assert.Single(ProsequorStackPedigree.ReadFrozenGroups(sink.Itemstack));
        Assert.Equal(5, ProsequorStackPedigree.TotalQty(ProsequorStackPedigree.ReadFrozenGroups(sink.Itemstack)));
        AssertPedigreeMatchesSize(sink.Itemstack);
    }

    [AtlasScenario]
    [Trait("Layer", "Pedigree")]
    [Trait("Kind", "Merge")]
    public async Task Merge_DifferentMakers_Should_BeHeterogeneous()
    {
        ITestPlayer joined = await World.JoinPlayer("PedMergeDiff");
        ItemSlot sink = new DummySlot(NewStackable(2));
        ProsequorStackPedigree.StampMaker(sink.Itemstack, "maker-a");
        ItemSlot source = new DummySlot(NewStackable(2));
        ProsequorStackPedigree.StampMaker(source.Itemstack, "maker-b");

        ItemStackMoveOperation op = NewOp(joined.Player, requestedQuantity: 99);
        int moved = source.TryPutInto(sink, ref op);
        Assert.Equal(2, moved);
        Assert.Equal(4, sink.StackSize);
        Assert.False(ProsequorStackPedigree.IsHomogeneous(sink.Itemstack));
        Assert.Equal(4, ProsequorStackPedigree.TotalQty(ProsequorStackPedigree.ReadFrozenGroups(sink.Itemstack)));
        AssertPedigreeMatchesSize(sink.Itemstack);
    }

    [AtlasScenario]
    [Trait("Layer", "Pedigree")]
    [Trait("Kind", "Merge")]
    public async Task Merge_DifferentAffixes_Should_StackAndPeelUnitSurface()
    {
        ITestPlayer joined = await World.JoinPlayer("PedMergeAffix");
        ItemSlot sink = new DummySlot(NewStackable(2));
        ProsequorStackPedigree.StampMaker(sink.Itemstack, "maker-a");
        ItemAffixes.Add(sink.Itemstack, "sturdy", "prosequor:affix-sturdy", "#84ff84");

        ItemSlot source = new DummySlot(NewStackable(2));
        ProsequorStackPedigree.StampMaker(source.Itemstack, "maker-a");
        ItemAffixes.Add(source.Itemstack, "keen", "prosequor:affix-keen", "#ff8484");

        ItemStackMoveOperation op = NewOp(joined.Player, requestedQuantity: 99);
        int moved = source.TryPutInto(sink, ref op);
        Assert.Equal(2, moved);
        Assert.Equal(4, sink.StackSize);
        Assert.False(ProsequorStackPedigree.IsHomogeneous(sink.Itemstack));
        Assert.Equal(2, ProsequorStackPedigree.ReadFrozenGroups(sink.Itemstack).Count);
        IReadOnlyList<ItemAffixEntry> sinkFace = ItemAffixes.GetAll(sink.Itemstack);
        Assert.True(sinkFace.Count == 1 && sinkFace[0].Code == "sturdy");

        ItemStack? peeled = sink.TakeOut(1);
        Assert.NotNull(peeled);
        IReadOnlyList<ItemAffixEntry> peeledAffixes = ItemAffixes.GetAll(peeled);
        Assert.True(peeledAffixes.Count == 1 && peeledAffixes[0].Code == "sturdy");
        AssertPedigreeMatchesSize(sink.Itemstack);
        AssertPedigreeMatchesSize(peeled);
    }

    [AtlasScenario]
    [Trait("Layer", "Pedigree")]
    [Trait("Kind", "Merge")]
    public async Task Merge_AnonymousIntoStamped_Should_PreserveStampedAndMatchSize()
    {
        ITestPlayer joined = await World.JoinPlayer("PedAnonInto");
        ItemSlot sink = new DummySlot(NewStackable(2));
        ProsequorStackPedigree.StampMaker(sink.Itemstack, "stamped");
        ItemSlot source = new DummySlot(NewStackable(3)); // no stamp

        ItemStackMoveOperation op = NewOp(joined.Player, requestedQuantity: 99);
        int moved = source.TryPutInto(sink, ref op);
        Assert.Equal(3, moved);
        Assert.Equal(5, sink.StackSize);
        AssertPedigreeMatchesSize(sink.Itemstack);
        Assert.True(ProsequorStackPedigree.TryGetPrimaryBlob(sink.Itemstack, out ProsequorBlob blob));
        Assert.Equal("stamped", blob.MakerUid);
    }

    [AtlasScenario]
    [Trait("Layer", "Pedigree")]
    [Trait("Kind", "Merge")]
    public async Task Merge_StampedIntoAnonymous_Should_PreserveStampedAndMatchSize()
    {
        ITestPlayer joined = await World.JoinPlayer("PedStampIntoAnon");
        ItemSlot sink = new DummySlot(NewStackable(2)); // no stamp
        ItemSlot source = new DummySlot(NewStackable(3));
        ProsequorStackPedigree.StampMaker(source.Itemstack, "stamped");

        ItemStackMoveOperation op = NewOp(joined.Player, requestedQuantity: 99);
        int moved = source.TryPutInto(sink, ref op);
        Assert.Equal(3, moved);
        Assert.Equal(5, sink.StackSize);
        AssertPedigreeMatchesSize(sink.Itemstack);
        Assert.True(ProsequorStackPedigree.TryGetPrimaryBlob(sink.Itemstack, out ProsequorBlob blob));
        Assert.Equal("stamped", blob.MakerUid);
    }

    ItemStack NewStackable(int size)
    {
        IWorldAccessor world = World.Api.World;
        Item? item = world.GetItem(new AssetLocation("game:stick"))
            ?? world.GetItem(new AssetLocation("game:clay-blue"))
            ?? world.GetItem(new AssetLocation("game:flaxfibers"));
        Assert.NotNull(item);
        return new ItemStack(item, size);
    }

    ItemStackMoveOperation NewOp(IPlayer player, int requestedQuantity) =>
        new(
            World.Api.World,
            EnumMouseButton.Left,
            (EnumModifierKey)0,
            EnumMergePriority.AutoMerge,
            requestedQuantity)
        {
            ActingPlayer = player
        };

    static void AssertPedigreeMatchesSize(ItemStack? stack)
    {
        Assert.NotNull(stack);
        int size = stack!.StackSize;
        if (size <= 0)
        {
            return;
        }

        if (ProsequorStackPedigree.IsLive(stack))
        {
            Assert.Equal(1, size);
            Assert.False(ProsequorStackPedigree.HasFrozen(stack));
            return;
        }

        if (!ProsequorStackPedigree.HasFrozen(stack) && !ProsequorStackPedigree.HasLive(stack))
        {
            // Anonymous stack — allowed.
            return;
        }

        int total = ProsequorStackPedigree.TotalQty(ProsequorStackPedigree.ReadFrozenGroups(stack));
        // Shortfall OK (luck / untracked units); over-count is not.
        Assert.True(total <= size, $"pedigree qty {total} exceeds stack size {size}");
    }
}
