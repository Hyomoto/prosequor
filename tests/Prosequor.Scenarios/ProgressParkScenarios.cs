using Atlas.Api;
using Atlas.XUnit;
using Prosequor;
using Prosequor.Player;
using Xunit;

namespace Prosequor.Scenarios;

public class ProgressParkScenarios : AtlasScenarioBase
{
    [AtlasScenario]
    public async Task GetProgressByUid_Should_ReturnLive_When_PlayerIsOnline()
    {
        ITestPlayer joined = await World.JoinPlayer("ParkLive");
        IPlayerProgress? byEntity = ProsequorModSystem.GetProgress(joined.Player);
        IPlayerProgress? byUid = ProsequorModSystem.GetProgress(World.Api, joined.Player.PlayerUID);
        Assert.NotNull(byEntity);
        Assert.Same(byEntity, byUid);
    }
}
