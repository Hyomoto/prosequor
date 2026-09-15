using Atlas.Api;
using Atlas.XUnit;
using Prosequor;
using Prosequor.Ability;
using Prosequor.Ability.Actions;
using Prosequor.Ability.Hooks;
using Prosequor.Data;
using Prosequor.Player;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Xunit;

namespace Prosequor.Scenarios;

/// <summary>
/// Last Stand needs a live entity (WatchedAttributes + calendar). Asserts the
/// killing-blow contract: reduce damage when ready, pass through on cooldown, ready again
/// after the stamped hours elapse.
/// </summary>
public class LastStandScenarios : AtlasScenarioBase
{
    /// <summary>Shipped resilience mapping endpoints (score 14 → 10h cooldown).</summary>
    static readonly MappedNumberParams ResilienceParams = new()
    {
        FromScore = 14,
        FromValue = 10f,
        ToScore = 18,
        ToValue = 2f,
        Round = "ceil"
    };

    static AbilityRuleSource ResilienceSource() => new()
    {
        SkillId = AttributeIds.Resilience,
        AttributeId = AttributeIds.Resilience
    };

    [AtlasScenario]
    [Trait("Layer", "Action")]
    [Trait("Kind", "LastStand")]
    public async Task LastStand_Should_ReduceKillingBlow_WhenReady()
    {
        ITestPlayer joined = await World.JoinPlayer("LSReady");
        IPlayer player = joined.Player;
        Entity entity = player.Entity;
        IPlayerProgress progress = RequireProgress(player);
        progress.SetAttribute(AttributeIds.Resilience, 14);
        ClearLastStandCooldown(entity);

        const float health = 5f;
        const float killingBlow = 20f;
        float damage = ApplyLastStand(entity, progress, health, killingBlow);

        // Leave 1 HP ⇒ deal CurrentHealth - 1.
        Assert.Equal(health - 1f, damage, precision: 3);

        double now = entity.World.Calendar?.TotalHours ?? 0.0;
        double readyAt = entity.WatchedAttributes.GetDouble(TakeDamageStation.AttrLastStandReadyHours, 0.0);
        Assert.True(readyAt > now, $"Expected cooldown stamp after now ({now}), got {readyAt}.");
        Assert.Equal(now + 10.0, readyAt, precision: 3);
    }

    [AtlasScenario]
    [Trait("Layer", "Action")]
    [Trait("Kind", "LastStand")]
    public async Task LastStand_Should_PassThrough_WhenOnCooldown()
    {
        ITestPlayer joined = await World.JoinPlayer("LSOnCd");
        IPlayer player = joined.Player;
        Entity entity = player.Entity;
        IPlayerProgress progress = RequireProgress(player);
        progress.SetAttribute(AttributeIds.Resilience, 14);

        double now = entity.World.Calendar?.TotalHours ?? 0.0;
        entity.WatchedAttributes.SetDouble(TakeDamageStation.AttrLastStandReadyHours, now + 5.0);

        const float health = 5f;
        const float killingBlow = 20f;
        float damage = ApplyLastStand(entity, progress, health, killingBlow);

        Assert.Equal(killingBlow, damage, precision: 3);
    }

    [AtlasScenario]
    [Trait("Layer", "Action")]
    [Trait("Kind", "LastStand")]
    public async Task LastStand_Should_ProcAgain_AfterCooldownElapses()
    {
        ITestPlayer joined = await World.JoinPlayer("LSReset");
        IPlayer player = joined.Player;
        Entity entity = player.Entity;
        IPlayerProgress progress = RequireProgress(player);
        progress.SetAttribute(AttributeIds.Resilience, 14);
        ClearLastStandCooldown(entity);

        const float health = 8f;
        const float killingBlow = 50f;

        float first = ApplyLastStand(entity, progress, health, killingBlow);
        Assert.Equal(health - 1f, first, precision: 3);

        double readyAt = entity.WatchedAttributes.GetDouble(TakeDamageStation.AttrLastStandReadyHours, 0.0);
        Assert.True(readyAt > 0.0);

        // Still on cooldown: full damage.
        float mid = ApplyLastStand(entity, progress, health, killingBlow);
        Assert.Equal(killingBlow, mid, precision: 3);

        // Simulate time passing past the stamped ready hour.
        double now = entity.World.Calendar?.TotalHours ?? 0.0;
        entity.WatchedAttributes.SetDouble(TakeDamageStation.AttrLastStandReadyHours, now - 0.01);

        float again = ApplyLastStand(entity, progress, health, killingBlow);
        Assert.Equal(health - 1f, again, precision: 3);

        double nextReady = entity.WatchedAttributes.GetDouble(TakeDamageStation.AttrLastStandReadyHours, 0.0);
        Assert.True(nextReady > now, $"Expected a new cooldown stamp after reset. now={now} ready={nextReady}");
    }

    static IPlayerProgress RequireProgress(IPlayer player)
    {
        IPlayerProgress? progress = ProsequorModSystem.GetProgress(player);
        Assert.NotNull(progress);
        return progress!;
    }

    static void ClearLastStandCooldown(Entity entity) =>
        entity.WatchedAttributes.SetDouble(TakeDamageStation.AttrLastStandReadyHours, 0.0);

    static float ApplyLastStand(
        Entity entity,
        IPlayerProgress progress,
        float currentHealth,
        float damage)
    {
        var context = new TakeDamageContext
        {
            Progress = progress,
            Entity = entity,
            CurrentHealth = currentHealth,
            DamageSource = new DamageSource { Type = EnumDamageType.BluntAttack }
        };
        float hours = (float)new AddMappedNumberOnDamageAction(HookIds.LastStand)
            .Apply(context, 0f, ResilienceParams, ResilienceSource());
        TakeDamageStation.TryApplyLastStand(context, hours, ref damage);
        return damage;
    }
}
