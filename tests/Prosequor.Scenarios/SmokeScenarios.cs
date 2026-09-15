using Atlas.Api;
using Atlas.XUnit;
using Prosequor;
using Prosequor.Player;
using Xunit;

namespace Prosequor.Scenarios;

public class SmokeScenarios : AtlasScenarioBase
{
    [AtlasScenario]
    public async Task Mod_Should_LoadWithSkillsAndProgress_When_ServerBoots()
    {
        ProsequorModSystem? mod = ProsequorModSystem.For(World.Api);
        Assert.NotNull(mod);
        Assert.NotEmpty(mod.Registry.All);

        ITestPlayer player = await World.JoinPlayer("Tester");
        IPlayerProgress? progress = ProsequorModSystem.GetProgress(player.Player);
        Assert.NotNull(progress);
    }
}
