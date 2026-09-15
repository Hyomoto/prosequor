using Atlas.Api;
using Atlas.XUnit;
using Prosequor.Data;
using Prosequor.Player;
using Prosequor.Progress;
using Vintagestory.API.Common;
using Xunit;

namespace Prosequor.Scenarios;

/// <summary>
/// Atlas has no client process — <see cref="Atlas.Api.IClientObservations"/> only sees
/// server→client traffic. This scenario proves the server mirrors unlock points onto the
/// entity (what a real client's <see cref="EntityBehaviorProgress"/> would hydrate).
/// In-game HUD path: use <c>/prosequor skillhint</c> on a real client.
/// </summary>
public class SkillWaitingHudScenarios : AtlasScenarioBase
{
    [AtlasScenario]
    [Trait("Layer", "Server")]
    [Trait("Kind", "Hud")]
    public async Task UnlockPoints_Should_MirrorToEntity_When_Granted()
    {
        ITestPlayer joined = await World.JoinPlayer("SkillHint");

        EntityBehaviorProgress progress = RequireBehavior(joined.Player);
        Assert.Equal(0, progress.UnlockPoints);

        progress.AddUnlockPoints(1);
        Assert.Equal(1, progress.UnlockPoints);

        await World.Until(
            () =>
            {
                PlayerProgressState? mirrored = ProgressStore.ReadFromEntity(joined.Entity);
                return mirrored is { UnlockPoints: > 0 };
            },
            timeoutTicks: 2000);

        PlayerProgressState? after = ProgressStore.ReadFromEntity(joined.Entity);
        Assert.NotNull(after);
        Assert.Equal(1, after.UnlockPoints);
        Assert.True(progress.HasSyncedMirror);
    }

    static EntityBehaviorProgress RequireBehavior(IPlayer player)
    {
        EntityBehaviorProgress? progress = player.Entity?.GetBehavior<EntityBehaviorProgress>();
        Assert.NotNull(progress);
        return progress;
    }
}
