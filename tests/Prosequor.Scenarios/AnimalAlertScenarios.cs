using System.Reflection;
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
    /// Chickens have a high-priority fleeentity gated on fleeondamage. Panic must arm
    /// the owned threat-flee task and must not start vanilla player-flee.
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
        AssertOwnedFleeArmed(chicken, "Chicken");
        await World.Ticks(1);
    }

    /// <summary>
    /// Fish flee is swimming-gated. On land the owned task must stay idle so
    /// fishoutofwater can flop.
    /// </summary>
    [AtlasScenario(FreshWorld = true)]
    [Trait("Layer", "Action")]
    [Trait("Kind", "AnimalAlert")]
    public async Task BeachedFish_Should_FlopInsteadOfThreatFlee()
    {
        AnimalAlertService.ClearTrackingForTests();

        ITestPlayer joined = await World.JoinPlayer("AlertFishLand");
        IPlayer player = joined.Player;
        Assert.NotNull(player.WorldData);
        player.WorldData.CurrentGameMode = EnumGameMode.Survival;
        await World.Ticks(2);

        BlockPos pad = joined.Position.AddCopy(3, 0, 0);
        IWorldAccessor world = player.Entity.World;
        Block? dirt = world.GetBlock(new AssetLocation("game:soil-low-none"))
            ?? world.GetBlock(new AssetLocation("game:dirt"));
        Assert.NotNull(dirt);
        world.BlockAccessor.SetBlock(dirt!.BlockId, pad);
        BlockPos stand = pad.UpCopy();
        world.BlockAccessor.SetBlock(0, stand);
        world.BlockAccessor.SetBlock(0, stand.UpCopy());

        Entity fish = SpawnPreyNear(joined, PreyFishCodes, stand);
        EnsureTracked(fish);
        AssertSensesPlayers(fish);
        AssertThreatFleeCopiesSourceClip(fish);

        for (int i = 0; i < 30 && !fish.OnGround; i++)
        {
            await World.Ticks(1);
        }

        AnimalAlertService.ForcePanicForTests(fish, player.Entity);
        Assert.True(AnimalAlertService.IsCommitted(fish), "Beached fish should still be panicked.");
        Assert.False(fish.Swimming, "Beached fish should not be swimming.");

        AiTaskThreatFlee? threat = AnimalAlertService.FindThreatFlee(fish);
        Assert.NotNull(threat);
        Assert.False(threat!.ShouldExecute(), "Swimming-gated threat flee must not run on land.");
        Assert.False(threat.ContinueExecute(0.05f), "A land flee must release the slot.");

        EntityBehaviorTaskAI? taskAi = fish.GetBehavior<EntityBehaviorTaskAI>();
        Assert.NotNull(taskAi?.TaskManager);
        bool flopReady = false;
        foreach (IAiTask task in taskAi!.TaskManager.AllTasks)
        {
            if (task is AiTaskFishOutOfWater flop && flop.ShouldExecute())
            {
                flopReady = true;
                break;
            }
        }

        Assert.True(fish.OnGround, "Beached fish should be on the ground.");
        Assert.True(flopReady, "fishoutofwater should be ready once the fish is on the ground.");
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
        AssertOwnedFleeArmed(hare, "Hare");
        await World.Ticks(1);
    }

    /// <summary>
    /// Starting the walk must aim the vanilla flee point and give that leg a distance.
    /// </summary>
    [AtlasScenario(FreshWorld = true)]
    [Trait("Layer", "Action")]
    [Trait("Kind", "AnimalAlert")]
    public async Task PanickedHare_Should_AimFleePointWhenWalkStarts()
    {
        AnimalAlertService.ClearTrackingForTests();

        ITestPlayer joined = await World.JoinPlayer("AlertHareAim");
        IPlayer player = joined.Player;
        Assert.NotNull(player.WorldData);
        player.WorldData.CurrentGameMode = EnumGameMode.Survival;
        await World.Ticks(2);

        Entity hare = SpawnPreyNear(joined, PreyHareCodes);
        EnsureTracked(hare);
        AnimalAlertService.ForcePanicForTests(hare, player.Entity);

        AiTaskThreatFlee? threat = AnimalAlertService.FindThreatFlee(hare);
        Assert.NotNull(threat);
        Assert.True(threat!.ShouldExecute());
        threat.StartExecute();

        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        object? pointValue = typeof(AiTaskFleeEntity).GetField("targetPos", flags)?.GetValue(threat);
        object? legValue = typeof(AiTaskFleeEntity).GetField("nowFleeingDistance", flags)?.GetValue(threat);
        object? configuredValue = typeof(AiTaskFleeEntity).GetField("fleeingDistance", flags)?.GetValue(threat);
        Assert.True(pointValue is Vec3d);
        Assert.True(legValue is float leg && configuredValue is float configured && leg > 0f && leg == configured);

        Vec3d point = (Vec3d)pointValue;
        double dx = point.X - hare.Pos.X;
        double dz = point.Z - hare.Pos.Z;
        double horizontal = Math.Sqrt((dx * dx) + (dz * dz));
        Assert.InRange(horizontal, 8d, 14d);

        double animalToPlayer = Math.Sqrt(
            Math.Pow(hare.Pos.X - player.Entity.Pos.X, 2)
            + Math.Pow(hare.Pos.Z - player.Entity.Pos.Z, 2));
        double pointToPlayer = Math.Sqrt(
            Math.Pow(point.X - player.Entity.Pos.X, 2)
            + Math.Pow(point.Z - player.Entity.Pos.Z, 2));
        Assert.True(
            pointToPlayer > animalToPlayer,
            $"Flee point should be farther from the player. animal={animalToPlayer:0.##} point={pointToPlayer:0.##}");
        await World.Ticks(1);
    }

    /// <summary>
    /// Out of characteristic range, incoming threat hits 0 and the committed latch
    /// clears, which ends the owned flee task.
    /// </summary>
    [AtlasScenario(FreshWorld = true)]
    [Trait("Layer", "Action")]
    [Trait("Kind", "AnimalAlert")]
    public async Task PanickedHare_Should_CoolOff_WhenPlayerLeavesRange()
    {
        AnimalAlertService.ClearTrackingForTests();

        ITestPlayer joined = await World.JoinPlayer("AlertHareCoolOff");
        IPlayer player = joined.Player;
        Assert.NotNull(player.WorldData);
        player.WorldData.CurrentGameMode = EnumGameMode.Survival;
        await World.Ticks(2);

        Entity hare = SpawnPreyNear(joined, PreyHareCodes);
        EnsureTracked(hare);
        AnimalAlertService.ForcePanicForTests(hare, player.Entity);
        AssertOwnedFleeArmed(hare, "Hare before cool-off");

        double far = hare.Pos.X + 80;
        player.Entity.TeleportToDouble(far, hare.Pos.Y, hare.Pos.Z);
        await World.Ticks(1);

        Assert.True(AnimalAlertService.TryGet(hare, out AnimalAlertState? state) && state != null);
        bool cooled = false;
        for (int i = 0; i < 45; i++)
        {
            AnimalAlertService.TickEntity(hare, 1f);
            if (state.CurrentThreat <= AnimalAlertMath.IncomingEpsilon && !state.Committed)
            {
                cooled = true;
                break;
            }
        }

        Assert.True(
            cooled,
            FormatState("Expected incoming 0 and committed clear after leaving range", hare, player, state));
        AiTaskThreatFlee? threat = AnimalAlertService.FindThreatFlee(hare);
        Assert.NotNull(threat);
        Assert.False(threat!.ShouldExecute(), "Threat flee must stop once the latch clears.");
    }

    /// <summary>
    /// A committed latch starts flee again with no new rising-threat roll and no integrate.
    /// </summary>
    [AtlasScenario(FreshWorld = true)]
    [Trait("Layer", "Action")]
    [Trait("Kind", "AnimalAlert")]
    public async Task PanickedHare_Should_ResumeFlee_WithoutNewRoll()
    {
        AnimalAlertService.ClearTrackingForTests();

        ITestPlayer joined = await World.JoinPlayer("AlertHareResume");
        IPlayer player = joined.Player;
        Assert.NotNull(player.WorldData);
        player.WorldData.CurrentGameMode = EnumGameMode.Survival;
        await World.Ticks(2);

        Entity hare = SpawnPreyNear(joined, PreyHareCodes);
        EnsureTracked(hare);
        AnimalAlertService.ForcePanicForTests(hare, player.Entity);

        AiTaskThreatFlee? threat = AnimalAlertService.FindThreatFlee(hare);
        Assert.NotNull(threat);
        Assert.True(threat!.ShouldExecute(), "Committed hare should flee.");
        Assert.True(
            threat.ShouldExecute(),
            "A second check must still flee without integrating or rolling again.");
        await World.Ticks(1);
    }

    /// <summary>
    /// Drifters flee sunlight and hunt the player. That ungated flee must not put
    /// them on the prey meter.
    /// </summary>
    [AtlasScenario(FreshWorld = true)]
    [Trait("Layer", "Action")]
    [Trait("Kind", "AnimalAlert")]
    public async Task Drifter_Should_NotJoinPreyMeter()
    {
        AnimalAlertService.ClearTrackingForTests();

        ITestPlayer joined = await World.JoinPlayer("AlertDrifter");
        IPlayer player = joined.Player;
        Assert.NotNull(player.WorldData);
        player.WorldData.CurrentGameMode = EnumGameMode.Survival;
        await World.Ticks(2);

        Entity drifter = World.SpawnEntity("game:drifter-normal", joined.Position.AddCopy(2, 0, 0));
        EntityBehaviorTaskAI? taskAi = drifter.GetBehavior<EntityBehaviorTaskAI>();
        Assert.NotNull(taskAi);
        AnimalAlertService.ConsiderTracking(taskAi!);

        Assert.True(AnimalAlertService.TryGet(drifter, out AnimalAlertState? state) && state != null);
        Assert.False(state.SensesPlayers, "A drifter hunts the player and must not be metered.");
        Assert.Null(AnimalAlertService.FindThreatFlee(drifter));
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

        AssertOwnedFleeArmed(hare, "Hare after panic");

        await World.Ticks(1);
    }

    static void AssertThreatFleeCopiesSourceClip(Entity animal)
    {
        AiTaskFleeEntity? source = null;
        EntityBehaviorTaskAI? taskAi = animal.GetBehavior<EntityBehaviorTaskAI>();
        Assert.NotNull(taskAi?.TaskManager);
        foreach (IAiTask task in taskAi!.TaskManager.AllTasks)
        {
            if (task is AiTaskFleeEntity flee && task is not AiTaskThreatFlee)
            {
                source = flee;
                break;
            }
        }

        AiTaskThreatFlee? threat = AnimalAlertService.FindThreatFlee(animal);
        Assert.NotNull(source?.animMeta);
        Assert.NotNull(threat?.animMeta);
        Assert.False(
            string.Equals(source!.animMeta.Code, "run", StringComparison.OrdinalIgnoreCase),
            "Fish flee should not be the vanilla run clip.");
        Assert.Equal(source.animMeta.Code, threat!.animMeta.Code);
        Assert.Equal(source.animMeta.AnimationSpeed, threat.animMeta.AnimationSpeed);
    }

    static void AssertOwnedFleeArmed(Entity animal, string label)
    {
        Assert.True(AnimalAlertService.IsCommitted(animal), label + " should be panicked.");
        AiTaskThreatFlee? threat = AnimalAlertService.FindThreatFlee(animal);
        Assert.NotNull(threat);
        Assert.True(threat!.ShouldExecute(), label + " threat-flee ShouldExecute should be true.");
        Assert.True(
            AnimalAlertService.TryProbePanicFlee(animal, out int ungatedReady, out int vanillaStarted),
            $"{label} panic flee probe failed. threatReady={ungatedReady} vanillaPlayerFleeStarted={vanillaStarted}.");
        Assert.True(ungatedReady > 0);
        Assert.Equal(0, vanillaStarted);
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

    static readonly string[] PreyFishCodes =
    [
        "game:fish-freshwater-perch-european-adult",
        "game:fish-freshwater-bass-largemouth-adult",
        "game:fish-freshwater-salmon-coho-adult"
    ];

    Entity SpawnHareNear(ITestPlayer joined) => SpawnPreyNear(joined, PreyHareCodes);

    Entity SpawnPreyNear(ITestPlayer joined, string[] codes) =>
        SpawnPreyNear(joined, codes, joined.Position.AddCopy(2, 0, 0));

    Entity SpawnPreyNear(ITestPlayer joined, string[] codes, BlockPos animalPos)
    {
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
