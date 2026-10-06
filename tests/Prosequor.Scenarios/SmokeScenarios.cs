using Atlas.Api;
using Atlas.XUnit;
using Prosequor;
using Prosequor.Player;
using Vintagestory.API.Common;
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
        AssertNoProsequorBootFaults();

        ITestPlayer player = await World.JoinPlayer("Tester");
        IPlayerProgress? progress = ProsequorModSystem.GetProgress(player.Player);
        Assert.NotNull(progress);
    }

    void AssertNoProsequorBootFaults()
    {
        // Atlas moves a leading "[name] " into SourceHint and leaves Source as "unknown"
        // when the line came through api.Logger rather than the mod's own logger.
        List<string> faults = new();
        foreach (BootDiagnosticEntry entry in World.BootDiagnostics)
        {
            if (entry.Level is not (EnumLogType.Error or EnumLogType.Fatal) || !IsProsequor(entry))
            {
                continue;
            }

            faults.Add($"{entry.Level} [{entry.DescribeSource()}] {entry.Message}");
        }

        Assert.True(
            faults.Count == 0,
            "Prosequor logged Error or Fatal during boot:" + Environment.NewLine + string.Join(Environment.NewLine, faults));
    }

    static bool IsProsequor(BootDiagnosticEntry entry) =>
        string.Equals(entry.Source, ProsequorModSystem.ModId, StringComparison.OrdinalIgnoreCase)
        || string.Equals(entry.SourceHint, ProsequorModSystem.ModId, StringComparison.OrdinalIgnoreCase)
        || entry.Message.Contains("[prosequor]", StringComparison.OrdinalIgnoreCase);
}
