using Atlas.Api;
using Atlas.XUnit;
using Prosequor.Ability;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;
using Xunit;

namespace Prosequor.Scenarios;

/// <summary>
/// Animal alert meter: spawn a prey animal next to a survival player and assert
/// threat/alert respond after ticks.
/// </summary>
public class AnimalAlertScenarios : AtlasScenarioBase
{
    [AtlasScenario(FreshWorld = true)]
    [Trait("Layer", "Action")]
    [Trait("Kind", "AnimalAlert")]
    public async Task NearbySurvivalPlayer_Should_RaiseThreatAfterTicks()
    {
        AnimalAlertService.ClearTrackingForTests();

        ITestPlayer joined = await World.JoinPlayer("AlertNear");
        IPlayer player = joined.Player;
        Assert.NotNull(player.WorldData);
        player.WorldData.CurrentGameMode = EnumGameMode.Survival;
        await World.Ticks(2);

        Entity animal = SpawnHareNear(joined);
        EnsureTracked(animal);
        AssertSensesPlayers(animal);

        // Direct integrate: spawn + tick must raise threat before we chase slice bugs.
        for (int i = 0; i < 5; i++)
        {
            AnimalAlertService.TickEntity(animal, AnimalAlertService.TickMs / 1000f);
            await World.Ticks(1);
        }

        Assert.True(
            AnimalAlertService.TryGet(animal, out AnimalAlertState? state) && state != null,
            "Alert state should still be present after ticks.");
        Assert.True(
            state.CurrentThreat > 0f || state.Alert > 0f,
            FormatState("Expected threat or alert > 0", animal, player, state));
        Assert.True(
            AnimalAlertService.TryReadOverlay(animal, out int alertQ, out int threatQ, out _, out _),
            $"Expected synced overlay attrs. alertQ={alertQ} threatQ={threatQ}.");
        Assert.True(
            alertQ > 0 || threatQ > 0,
            $"Expected synced alert or threat quanta > 0. alertQ={alertQ} threatQ={threatQ}.");
    }

    /// <summary>
    /// Relies on Prosequor's registered game-tick listener alone (no manual Tick).
    /// </summary>
    [AtlasScenario(FreshWorld = true)]
    [Trait("Layer", "Action")]
    [Trait("Kind", "AnimalAlert")]
    public async Task NearbySurvivalPlayer_Should_RaiseThreat_ViaGameTickListener()
    {
        AnimalAlertService.ClearTrackingForTests();

        ITestPlayer joined = await World.JoinPlayer("AlertListen");
        IPlayer player = joined.Player;
        Assert.NotNull(player.WorldData);
        player.WorldData.CurrentGameMode = EnumGameMode.Survival;
        await World.Ticks(2);

        Entity animal = SpawnHareNear(joined);
        EnsureTracked(animal);
        AssertSensesPlayers(animal);

        // Listener fires every 200ms; each fire integrates ~1/5 of the list.
        await World.Ticks(60);

        Assert.True(
            AnimalAlertService.TryGet(animal, out AnimalAlertState? state) && state != null,
            "Alert state missing after game ticks.");
        Assert.True(
            state.CurrentThreat > 0f || state.Alert > 0f,
            FormatState("Listener path: expected threat or alert > 0", animal, player, state));
    }

    [AtlasScenario(FreshWorld = true)]
    [Trait("Layer", "Action")]
    [Trait("Kind", "AnimalAlert")]
    public async Task CreativePlayer_Should_NotRaiseThreat()
    {
        AnimalAlertService.ClearTrackingForTests();

        ITestPlayer joined = await World.JoinPlayer("AlertCreative");
        IPlayer player = joined.Player;
        Assert.NotNull(player.WorldData);
        player.WorldData.CurrentGameMode = EnumGameMode.Creative;
        await World.Ticks(2);

        Entity animal = SpawnPreyNear(joined, PreyHareCodes);
        EnsureTracked(animal);

        for (int i = 0; i < 5; i++)
        {
            AnimalAlertService.TickEntity(animal, AnimalAlertService.TickMs / 1000f);
            await World.Ticks(1);
        }

        Assert.True(AnimalAlertService.TryGet(animal, out AnimalAlertState? state) && state != null);
        Assert.True(
            state.CurrentThreat <= 0f && state.Alert <= 0f,
            FormatState("Creative players must not raise alert threat", animal, player, state));
    }

    /// <summary>
    /// Chickens have a high-priority fleeentity gated on fleeondamage. Panic must drive
    /// the unrestricted fleeentity (ShouldExecute true) and must not force the gated one
    /// while calm — otherwise they stand still in "panic."
    /// </summary>
    [AtlasScenario(FreshWorld = true)]
    [Trait("Layer", "Action")]
    [Trait("Kind", "AnimalAlert")]
    public async Task PanickedChicken_Should_ArmUngatedFleeNotGated()
    {
        AnimalAlertService.ClearTrackingForTests();

        ITestPlayer joined = await World.JoinPlayer("AlertChickenFlee");
        IPlayer player = joined.Player;
        Assert.NotNull(player.WorldData);
        player.WorldData.CurrentGameMode = EnumGameMode.Survival;
        await World.Ticks(2);

        Entity chicken = SpawnPreyNear(joined, PreyChickenCodes);
        EnsureTracked(chicken);
        AssertSensesPlayers(chicken);
        Assert.True(
            AnimalAlertService.HasUngatedPlayerFlee(chicken),
            "Expected an unrestricted player fleeentity (not emotion-gated).");

        AnimalAlertService.ForcePanicForTests(chicken, player.Entity);
        Assert.True(AnimalAlertService.IsCommitted(chicken), "Chicken should be panicked.");

        // Drive TaskAI explicitly — Atlas world ticks alone may not path entities.
        EntityBehaviorTaskAI? taskAi = chicken.GetBehavior<EntityBehaviorTaskAI>();
        Assert.NotNull(taskAi);
        for (int i = 0; i < 10; i++)
        {
            Assert.True(
                AnimalAlertService.TryProbePanicFlee(
                    chicken,
                    out int ungatedReady,
                    out int gatedForced),
                $"Chicken panic flee probe failed. ungatedReady={ungatedReady} gatedForcedWhileCalm={gatedForced}.");
            taskAi!.OnGameTick(0.05f);
            AnimalAlertService.TickEntity(chicken, AnimalAlertService.TickMs / 1000f);
            await World.Ticks(1);
        }
    }

    [AtlasScenario(FreshWorld = true)]
    [Trait("Layer", "Action")]
    [Trait("Kind", "AnimalAlert")]
    public async Task PanickedHare_Should_ArmFlee()
    {
        AnimalAlertService.ClearTrackingForTests();

        ITestPlayer joined = await World.JoinPlayer("AlertHareFlee");
        IPlayer player = joined.Player;
        Assert.NotNull(player.WorldData);
        player.WorldData.CurrentGameMode = EnumGameMode.Survival;
        await World.Ticks(2);

        Entity hare = SpawnPreyNear(joined, PreyHareCodes);
        EnsureTracked(hare);
        AssertSensesPlayers(hare);

        AnimalAlertService.ForcePanicForTests(hare, player.Entity);
        Assert.True(AnimalAlertService.IsCommitted(hare), "Hare should be panicked.");

        Assert.True(
            AnimalAlertService.TryProbePanicFlee(hare, out int ungatedReady, out int gatedForced),
            $"Hare panic flee probe failed. ungatedReady={ungatedReady} gatedForcedWhileCalm={gatedForced}.");
        Assert.True(ungatedReady > 0);
        Assert.Equal(0, gatedForced);

        await World.Ticks(1);
    }

    /// <summary>
    /// Stalk gate: before panic, flee tasks must not sense the player; after panic,
    /// fused CanSensePlayer accepts the alert target (eligibility, not vanilla range).
    /// </summary>
    [AtlasScenario(FreshWorld = true)]
    [Trait("Layer", "Action")]
    [Trait("Kind", "AnimalAlert")]
    public async Task StalkGate_CanSensePlayer_OnlyWhenPanicked()
    {
        AnimalAlertService.ClearTrackingForTests();

        ITestPlayer joined = await World.JoinPlayer("AlertStalkGate");
        IPlayer player = joined.Player;
        Assert.NotNull(player.WorldData);
        player.WorldData.CurrentGameMode = EnumGameMode.Survival;
        await World.Ticks(2);

        Entity hare = SpawnPreyNear(joined, PreyHareCodes);
        EnsureTracked(hare);
        AssertSensesPlayers(hare);
        Assert.True(player.Entity is EntityPlayer);

        Assert.True(
            AnimalAlertService.TryCanSensePlayer(hare, (EntityPlayer)player.Entity, out bool before),
            "Expected ungated flee task for CanSensePlayer probe.");
        Assert.False(before, "Before panic, meter animals must not sense the player.");
        Assert.False(AnimalAlertService.IsCommitted(hare));

        AnimalAlertService.ForcePanicForTests(hare, player.Entity);
        Assert.True(AnimalAlertService.IsCommitted(hare));

        Assert.True(
            AnimalAlertService.TryCanSensePlayer(hare, (EntityPlayer)player.Entity, out bool after),
            "Expected ungated flee after panic.");
        Assert.True(after, "After panic, alert target must be sensed (fused eligibility).");

        Assert.True(
            AnimalAlertService.TryProbePanicFlee(hare, out int ungatedReady, out int gatedForced),
            $"After panic flee ShouldExecute failed. ungatedReady={ungatedReady} gatedForced={gatedForced}.");

        await World.Ticks(1);
    }

    static readonly string[] PreyHareCodes =
    [
        "game:hare-european-adult-male",
        "game:hare-arctic-adult-male",
        "game:hare-european-adult-female"
    ];

    static readonly string[] PreyChickenCodes =
    [
        "game:chicken-hen",
        "game:chicken-rooster"
    ];

    Entity SpawnHareNear(ITestPlayer joined) => SpawnPreyNear(joined, PreyHareCodes);

    Entity SpawnPreyNear(ITestPlayer joined, string[] codes)
    {
        BlockPos animalPos = joined.Position.AddCopy(2, 0, 0);
        Exception? last = null;
        foreach (string code in codes)
        {
            try
            {
                return World.SpawnEntity(code, animalPos);
            }
            catch (Exception ex)
            {
                last = ex;
            }
        }

        throw new InvalidOperationException(
            "Could not spawn prey for alert scenario: " + string.Join(", ", codes),
            last);
    }

    static void EnsureTracked(Entity animal)
    {
        EntityBehaviorTaskAI? taskAi = animal.GetBehavior<EntityBehaviorTaskAI>();
        Assert.NotNull(taskAi);
        AnimalAlertService.ConsiderTracking(taskAi!);
    }

    static void AssertSensesPlayers(Entity animal)
    {
        Assert.True(
            AnimalAlertService.TryGet(animal, out AnimalAlertState? state) && state != null,
            "Expected alert state after TaskAI init / ConsiderTracking.");
        Assert.True(state.SensesPlayers, "Prey should sense players (flee/idle stop codes).");
        Assert.True(
            state.CharacteristicRange > 0f,
            $"Expected positive characteristic range, got {state.CharacteristicRange}.");
    }

    static string FormatState(
        string headline,
        Entity animal,
        IPlayer player,
        AnimalAlertState state) =>
        $"{headline}. threat={state.CurrentThreat:0.###} alert={state.Alert:0.###} "
        + $"range={state.CharacteristicRange:0.#} "
        + $"dist={animal.Pos.DistanceTo(player.Entity.Pos):0.##} "
        + $"mode={player.WorldData.CurrentGameMode} senses={state.SensesPlayers}.";
}
