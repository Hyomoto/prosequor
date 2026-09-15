using Atlas.Api;
using Atlas.XUnit;
using Prosequor.Ability;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Xunit;

namespace Prosequor.Scenarios;

/// <summary>
/// Entity Live pedigree on WatchedAttributes — stamp, stack roundtrip, and Copy.
/// Uses the joined player entity (same live-entity pattern as Last Stand).
/// </summary>
public class ProsequorEntityPedigreeScenarios : AtlasScenarioBase
{
    [AtlasScenario]
    [Trait("Layer", "Pedigree")]
    [Trait("Kind", "EntityLive")]
    public async Task Stamp_Should_WriteLiveOnWatchedAttributes()
    {
        ITestPlayer joined = await World.JoinPlayer("EntPedStamp");
        Entity entity = joined.Player.Entity;

        ProsequorEntityPedigreeStation.Clear(entity);
        ProsequorEntityPedigreeStation.StampMaker(entity, "entity-maker");
        ProsequorEntityPedigreeStation.AddContributor(entity, "helper");

        Assert.True(ProsequorEntityPedigreeStation.TryGetBlob(entity, out ProsequorBlob blob));
        Assert.Equal("entity-maker", blob.MakerUid);
        Assert.True(blob.TryGetContributorWeight("helper", out int w));
        Assert.Equal(1, w);

        ITreeAttribute? live = entity.WatchedAttributes.GetTreeAttribute(ProsequorStackPedigree.LiveAttr);
        Assert.NotNull(live);
        Assert.Equal("entity-maker", ProsequorBlob.ReadFrom(live!).MakerUid);
    }

    [AtlasScenario]
    [Trait("Layer", "Pedigree")]
    [Trait("Kind", "EntityLive")]
    public async Task Stack_Should_RoundtripThroughEntity()
    {
        ITestPlayer joined = await World.JoinPlayer("EntPedRound");
        Entity entity = joined.Player.Entity;

        ItemStack held = new() { StackSize = 1 };
        ProsequorStackPedigree.StampMaker(held, "stack-maker");
        ProsequorStackPedigree.AddContributor(held, "firer");

        ProsequorEntityPedigreeStation.Clear(entity);
        ProsequorEntityPedigreeStation.CaptureFromStack(entity, held);
        Assert.True(ProsequorEntityPedigreeStation.TryGetBlob(entity, out ProsequorBlob onEntity));
        Assert.Equal("stack-maker", onEntity.MakerUid);

        ItemStack drop = new() { StackSize = 1 };
        ProsequorEntityPedigreeStation.ApplyToStack(entity, drop);
        Assert.True(ProsequorStackPedigree.IsLive(drop));
        Assert.True(ProsequorStackPedigree.TryGetPrimaryBlob(drop, out ProsequorBlob dropBlob));
        Assert.Equal("stack-maker", dropBlob.MakerUid);
        Assert.True(dropBlob.TryGetContributorWeight("firer", out int firer));
        Assert.Equal(1, firer);
        Assert.Equal(onEntity.ContentHash, dropBlob.ContentHash);
    }

    [AtlasScenario]
    [Trait("Layer", "Pedigree")]
    [Trait("Kind", "EntityLive")]
    public async Task Copy_Should_CarryLiveToAnotherEntity()
    {
        ITestPlayer a = await World.JoinPlayer("EntPedCopyA");
        ITestPlayer b = await World.JoinPlayer("EntPedCopyB");
        Entity from = a.Player.Entity;
        Entity to = b.Player.Entity;

        ProsequorEntityPedigreeStation.Clear(from);
        ProsequorEntityPedigreeStation.Clear(to);
        ProsequorEntityPedigreeStation.StampMaker(from, "copy-maker");
        ProsequorEntityPedigreeStation.AddContributor(from, "c1");

        ProsequorEntityPedigreeStation.Copy(from, to);

        Assert.True(ProsequorEntityPedigreeStation.TryGetBlob(to, out ProsequorBlob copied));
        Assert.Equal("copy-maker", copied.MakerUid);
        Assert.True(copied.TryGetContributorWeight("c1", out int w));
        Assert.Equal(1, w);

        Assert.True(ProsequorEntityPedigreeStation.TryGetBlob(from, out ProsequorBlob source));
        Assert.Equal(source.ContentHash, copied.ContentHash);
    }

    [AtlasScenario]
    [Trait("Layer", "Pedigree")]
    [Trait("Kind", "EntityLive")]
    public async Task Clear_Should_RemoveLive()
    {
        ITestPlayer joined = await World.JoinPlayer("EntPedClear");
        Entity entity = joined.Player.Entity;

        ProsequorEntityPedigreeStation.StampMaker(entity, "temp");
        Assert.True(ProsequorEntityPedigreeStation.TryGetBlob(entity, out _));

        ProsequorEntityPedigreeStation.Clear(entity);
        Assert.False(ProsequorEntityPedigreeStation.TryGetBlob(entity, out _));
        Assert.False(entity.WatchedAttributes.HasAttribute(ProsequorStackPedigree.LiveAttr));
    }

    [AtlasScenario]
    [Trait("Layer", "Pedigree")]
    [Trait("Kind", "EntityLive")]
    public async Task Facade_Should_ResolveEntityHost()
    {
        ITestPlayer joined = await World.JoinPlayer("EntPedFacade");
        Entity entity = joined.Player.Entity;

        ProsequorPedigree.Clear(entity);
        ProsequorPedigree.StampMaker(entity, "facade-e");
        ProsequorPedigree.AddContributor(entity, "facade-c");

        Assert.True(ProsequorPedigree.TryGetBlob(entity, out ProsequorBlob blob));
        Assert.Equal("facade-e", blob.MakerUid);
        Assert.True(blob.TryGetContributorWeight("facade-c", out int w));
        Assert.Equal(1, w);
    }
}
