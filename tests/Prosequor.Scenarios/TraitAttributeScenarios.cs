using Atlas.Api;
using Atlas.XUnit;
using Prosequor.Ability;
using Prosequor.Data;
using Prosequor.Player;
using Vintagestory.API.Common;
using Vintagestory.API.Server;
using Vintagestory.GameContent;
using Xunit;

namespace Prosequor.Scenarios;

/// <summary>
/// Mid-save / already-selected characters never send <c>DidSelect</c>. Class scores
/// must still apply from the cached starting profile, not leftover stripped traits.
/// </summary>
public class TraitAttributeScenarios : AtlasScenarioBase
{
    [AtlasScenario]
    [Trait("Layer", "Server")]
    [Trait("Kind", "Traits")]
    public async Task ClassProfile_Should_ApplyFromCache_When_SelectionAlreadyDone()
    {
        ITestPlayer joined = await World.JoinPlayer("MidSaveHunter");
        IServerPlayer player = RequireServerPlayer(joined.Player);
        IPlayerProgress progress = RequireProgress(player);

        player.Entity.WatchedAttributes.SetString("characterClass", "hunter");
        player.SetModData(TraitAttributeConverter.AppliedModDataKey, false);

        ProsequorModSystem mod = ProsequorModSystem.For(World.Api)
            ?? throw new InvalidOperationException("Expected a live Prosequor mod system.");
        CharacterSystem characters = World.Api.ModLoader.GetModSystem<CharacterSystem>()
            ?? throw new InvalidOperationException("Expected CharacterSystem.");

        TraitAttributeConverter.TryApplyOnSelection(player, characters, mod.TraitAttributes, mod.Registry);

        Assert.True(player.GetModData(TraitAttributeConverter.AppliedModDataKey, false));
        Assert.Equal(9, progress.GetAttribute(AttributeIds.Strength));
        Assert.Equal(13, progress.GetAttribute(AttributeIds.Perception));
        Assert.Equal(11, progress.GetAttribute(AttributeIds.Constitution));
        Assert.Equal(10, progress.GetAttribute(AttributeIds.Inconspicuity));
        Assert.Equal(9, progress.GetAttribute(AttributeIds.Resilience));
    }

    [AtlasScenario]
    [Trait("Layer", "Server")]
    [Trait("Kind", "Traits")]
    public async Task GetProgress_Should_ReturnLive_When_EntityIsReady()
    {
        ITestPlayer joined = await World.JoinPlayer("LiveProgress");
        Assert.NotNull(ProsequorModSystem.TryGetLiveProgress(joined.Player));
        Assert.NotNull(ProsequorModSystem.GetProgress(joined.Player));
    }

    static IServerPlayer RequireServerPlayer(IPlayer player)
    {
        Assert.True(player is IServerPlayer, $"Expected IServerPlayer, got {player.GetType().Name}");
        return (IServerPlayer)player;
    }

    static IPlayerProgress RequireProgress(IPlayer player)
    {
        IPlayerProgress? progress = ProsequorModSystem.GetProgress(player);
        Assert.NotNull(progress);
        return progress;
    }
}
