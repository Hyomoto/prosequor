using System.Reflection;
using Atlas.Api;
using Atlas.XUnit;
using HarmonyLib;
using Prosequor;
using Prosequor.Ability;
using Prosequor.Player;
using Prosequor.Xp;
using Prosequor.Xp.Activity;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;
using Xunit;

namespace Prosequor.Scenarios;

/// <summary>
/// Combat Overhaul fork + legacy runtime: foreign-projectile kill XP, spawn folds,
/// fall-damage coefficient, and Harmony bind checks.
/// </summary>
[AtlasWorld(Mods = ["staged-co-legacy", "staged-co-fork", "staged-jsonpatches", "staged-vsimgui"])]
public class CombatOverhaulCompatScenarios : AtlasScenarioBase
{
    const string Skill = "hunting";
    const string ArrowEntityCode = "combatoverhaul:arrow-flint";
    const string ArrowItemCode = "game:arrow-flint";
    const string FallStatKey = "prosequor-co-fall-test";

    static readonly string[] PreyCodes =
    [
        "game:hare-european-adult-male",
        "game:hare-arctic-adult-male",
        "game:hare-european-adult-female",
        "game:chicken-hen"
    ];

    [AtlasScenario(TimeoutMs = 180_000)]
    [Trait("Layer", "Compat")]
    [Trait("Kind", "CombatOverhaul")]
    public async Task CombatOverhaulPatches_Should_Bind()
    {
        await World.Ticks(1);

        Type? projectileSystem = AccessTools.TypeByName(CombatOverhaulCompat.ProjectileSystemTypeName);
        Type? rangeClient = AccessTools.TypeByName(CombatOverhaulCompat.RangeWeaponClientTypeName);
        Type? harmonyPatches = AccessTools.TypeByName(CombatOverhaulCompat.HarmonyPatchesTypeName);
        Assert.NotNull(projectileSystem);
        Assert.NotNull(rangeClient);
        Assert.NotNull(harmonyPatches);

        MethodInfo? spawn = AccessTools.Method(projectileSystem, "SpawnProjectile");
        Assert.NotNull(spawn);
        Patches? spawnInfo = Harmony.GetPatchInfo(spawn);
        Assert.True(
            spawnInfo?.Postfixes.Any(p => p.owner == "prosequor") == true,
            "Expected a Prosequor postfix on ProjectileSystemServer.SpawnProjectile.");

        int speedPostfixes = 0;
        foreach (MethodInfo method in AccessTools.GetDeclaredMethods(rangeClient!))
        {
            if (method.Name != "GetAnimationSpeed" || method.ReturnType != typeof(float))
            {
                continue;
            }

            ParameterInfo[] parameters = method.GetParameters();
            if (parameters.Length < 1 || parameters[0].ParameterType != typeof(Entity))
            {
                continue;
            }

            Patches? info = Harmony.GetPatchInfo(method);
            if (info?.Postfixes.Any(p => p.owner == "prosequor") == true)
            {
                speedPostfixes++;
            }
        }

        Assert.True(
            speedPostfixes >= 2,
            $"Expected Prosequor postfixes on both RangeWeaponClient.GetAnimationSpeed overloads; found {speedPostfixes}.");

        MethodInfo? fall = AccessTools.Method(harmonyPatches, "OnFallToGround");
        Assert.NotNull(fall);
        Patches? fallInfo = Harmony.GetPatchInfo(fall);
        Assert.True(
            fallInfo?.Transpilers.Any(p => p.owner == "prosequor") == true,
            "Expected a Prosequor transpiler on HarmonyPatches.OnFallToGround.");
    }

    [AtlasScenario(TimeoutMs = 180_000)]
    [Trait("Layer", "Compat")]
    [Trait("Kind", "CombatOverhaul")]
    public async Task ForeignProjectileKill_Should_PayHuntingXp()
    {
        IPlayer player = await JoinHunter("CoKillXp");
        IWorldAccessor world = World.Api.World;
        Item arrowItem = RequireItem(world, ArrowItemCode);
        Entity projectile = World.SpawnEntity(ArrowEntityCode, player.Entity.Pos.AsBlockPos.AddCopy(1, 1, 0));
        SetStackProp(projectile, "ProjectileStack", new ItemStack(arrowItem, 1));
        await World.Ticks(1);

        Entity animal = SpawnPrey(player);
        await World.Ticks(2);

        string arrowCode = arrowItem.Code.ToString();
        string target = animal.Code.ToString();
        float expected = ExpectedHuntingKill(
            world,
            player.PlayerUID,
            arrowCode,
            target,
            animal.Properties.Weight);
        Assert.True(expected > 0f, $"Loaded hunting rules should pay for a CO arrow kill of {target}.");

        DamageSource source = new()
        {
            Source = EnumDamageSource.Entity,
            Type = EnumDamageType.PiercingAttack,
            SourceEntity = projectile,
            CauseEntity = player.Entity
        };

        Assert.True(
            ForeignProjectileResolver.TryResolve(source, out IPlayer? resolved, out string? caller),
            "Foreign projectile resolver should accept a CO projectile damage source.");
        Assert.Equal(player.PlayerUID, resolved?.PlayerUID);
        Assert.Equal(arrowCode, caller);
        Assert.Equal(
            player.PlayerUID,
            HeartseekerStation.ResolveAttackingPlayer(source)?.PlayerUID);

        WoundToNextHit(animal);
        float before = HuntingXp(player);
        Assert.True(
            animal.ReceiveDamage(source, 50f),
            "CO-style projectile damage should land on the wounded animal.");
        await World.Ticks(1);

        Assert.False(
            animal.Alive,
            $"Foreign projectile kill should drop the wounded {target}. health={HealthOf(animal):0.###}.");
        ScenarioXp.AssertPaid(
            HuntingXp(player) - before,
            expected,
            $"CO foreign projectile kill of {target} arrow={arrowCode}");
    }

    [AtlasScenario(TimeoutMs = 180_000)]
    [Trait("Layer", "Compat")]
    [Trait("Kind", "CombatOverhaul")]
    public async Task SpawnProjectile_Should_FoldFlightAndBreakChance()
    {
        IPlayer player = await JoinHunter("CoSpawnFold");
        IWorldAccessor world = World.Api.World;
        Item arrowItem = RequireItem(world, ArrowItemCode);
        ItemStack arrowStack = new(arrowItem, 1);
        CraftAttributeMods.SetFactor(arrowStack, FlightAttributeMutator.KeyName, 1.5f);

        const string drawKey = "prosequor-co-draw-test";
        player.Entity.Stats.Set("bowDrawingStrength", drawKey, 0.25f, false);
        try
        {
            const float dropChance = 0.75f;
            const double velocityX = 2.0;
            Entity projectile = InvokeSpawnProjectile(
                world.Api,
                player.Entity,
                arrowStack,
                ArrowEntityCode,
                dropChance,
                velocityX);

            Assert.Equal(
                velocityX * 1.5 * 1.25,
                projectile.Pos.Motion.X,
                3);
            Assert.True(
                TryReadFloatProp(projectile, "DropOnImpactChance", out float afterDrop),
                "Spawned CO projectile should expose DropOnImpactChance.");
            float expectedBreak = PlayerInteractionStation.ResolveArrowBreakChance(
                player,
                1f - dropChance);
            Assert.Equal(1f - expectedBreak, afterDrop, 3);
        }
        finally
        {
            player.Entity.Stats.Remove("bowDrawingStrength", drawKey);
        }
    }

    [AtlasScenario(TimeoutMs = 180_000)]
    [Trait("Layer", "Compat")]
    [Trait("Kind", "CombatOverhaul")]
    public async Task FallDamage_Should_HonorFallDamageFactor()
    {
        ITestPlayer joined = await World.JoinPlayer("CoFall");
        IPlayer player = joined.Player;
        Assert.NotNull(player.Entity);
        Assert.NotNull(player.WorldData);
        player.WorldData.CurrentGameMode = EnumGameMode.Survival;
        EntityBehaviorHealth? health = player.Entity.GetBehavior<EntityBehaviorHealth>();
        Assert.NotNull(health);
        Assert.True(
            player.Entity.Properties.FallDamageMultiplier > 0f,
            "Survival player should take fall damage.");

        player.Entity.Stats.Set("fallDamageFactor", FallStatKey, -0.5f, false);
        try
        {
            float scaledLoss = RunFall(player, health!, fallDistance: 10.0);
            float expectedScaled = ExpectedFallDamage(player, 10.0, factor: 0.5f);
            Assert.True(expectedScaled > 0f, "Scaled fall expectation should be positive.");
            Assert.Equal(expectedScaled, scaledLoss, 2);

            player.Entity.Stats.Remove("fallDamageFactor", FallStatKey);
            float rawLoss = RunFall(player, health!, fallDistance: 10.0);
            float expectedRaw = ExpectedFallDamage(player, 10.0, factor: 1f);
            Assert.Equal(expectedRaw, rawLoss, 2);
        }
        finally
        {
            player.Entity.Stats.Remove("fallDamageFactor", FallStatKey);
        }
    }

    [AtlasScenario(TimeoutMs = 180_000)]
    [Trait("Layer", "Compat")]
    [Trait("Kind", "CombatOverhaul")]
    public async Task RangedWeaponsSpeed_Should_ScaleAnimationSpeedPostfix()
    {
        ITestPlayer joined = await World.JoinPlayer("CoChargeSpeed");
        IPlayer player = joined.Player;
        Assert.NotNull(player.Entity);

        const string speedKey = "prosequor-co-ranged-speed-test";
        player.Entity.Stats.Set("rangedWeaponsSpeed", speedKey, 0.5f, false);
        try
        {
            float result = 1f;
            float expected = player.Entity.Stats.GetBlended("rangedWeaponsSpeed");
            Assert.True(expected > 1.0001f, "Test offset should raise rangedWeaponsSpeed.");
            CombatOverhaulCompat.GetAnimationSpeedPostfix(player.Entity, ref result);
            Assert.Equal(expected, result, 3);
        }
        finally
        {
            player.Entity.Stats.Remove("rangedWeaponsSpeed", speedKey);
        }
    }

    async Task<IPlayer> JoinHunter(string name)
    {
        ITestPlayer joined = await World.JoinPlayer(name);
        IPlayer player = joined.Player;
        Assert.NotNull(player.Entity);
        Assert.NotNull(player.WorldData);
        player.WorldData.CurrentGameMode = EnumGameMode.Survival;
        if (player is IServerPlayer server && !server.HasPrivilege(Privilege.attackcreatures))
        {
            server.SetRole("suplayer");
        }

        await World.Ticks(2);
        EntityBehaviorProgress? progress = player.Entity.GetBehavior<EntityBehaviorProgress>();
        Assert.NotNull(progress);
        Assert.True(
            progress!.HasSkillAccess(Skill),
            "The joined player should have the hunting skill.");
        return player;
    }

    Entity SpawnPrey(IPlayer player)
    {
        BlockPos origin = player.Entity.Pos.AsBlockPos.AddCopy(2, 0, 0);
        Exception? last = null;
        foreach (string code in PreyCodes)
        {
            try
            {
                Entity animal = World.SpawnEntity(code, origin);
                animal.WatchedAttributes.SetInt("generation", 0);
                Assert.True(
                    ProsequorModSystem.For(World.Api)?.AnimalWeight?.IsListed(animal) == true,
                    $"{animal.Code} is not a huntable animal.");
                Assert.False(
                    AnimalWeightCatalog.CanCleaverSlaughter(animal),
                    $"{animal.Code} spawned at cleaver-certain generation.");
                return animal;
            }
            catch (Exception ex) when (ex is not Xunit.Sdk.XunitException)
            {
                last = ex;
            }
        }

        throw new InvalidOperationException(
            "Could not spawn a listed prey animal: " + string.Join(", ", PreyCodes),
            last);
    }

    static void WoundToNextHit(Entity animal)
    {
        EntityBehaviorHealth? health = animal.GetBehavior<EntityBehaviorHealth>();
        Assert.NotNull(health);
        Assert.True(animal.Alive && health!.Health > 0f, $"{animal.Code} has no health to wound.");
        health.Health = 0.2f;
    }

    static float HealthOf(Entity animal) =>
        animal.GetBehavior<EntityBehaviorHealth>()?.Health ?? -1f;

    static float HuntingXp(IPlayer player) => ScenarioXp.TotalSkill(player, Skill);

    static float ExpectedHuntingKill(
        IWorldAccessor world,
        string playerUid,
        string caller,
        string target,
        float weight)
    {
        ProsequorModSystem? mod = ProsequorModSystem.For(world.Api);
        Assert.NotNull(mod);
        Assert.NotNull(mod!.AnimalWeight);
        Assert.True(
            mod.AnimalWeight!.TryGetRange(out float min, out float max),
            "Animal weight catalog has no span.");
        IReadOnlyList<Deed.PlannedPay> pays = Deed.PlanPays(
            mod.XpRules,
            mod.Collections.Index,
            playerUid,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { DeedTokenTags.Killed },
            caller,
            target,
            mount: null,
            ground: null,
            lastCraft: null,
            weight,
            min,
            max,
            totalUnits: 0,
            craftCount: 1,
            metricDomain: AnimalWeightCatalog.MetricDomain);
        float total = 0f;
        foreach (Deed.PlannedPay pay in pays)
        {
            if (!string.Equals(pay.SkillId, Skill, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!string.Equals(pay.Fact.ActorUid, playerUid, StringComparison.Ordinal))
            {
                continue;
            }

            total += pay.Amount;
        }

        return total;
    }

    static float ExpectedFallDamage(IPlayer player, double heightDelta, float factor)
    {
        double threshold = Math.Max(player.Entity.Stats.GetBlended("fallDamageThreshold"), 0.001f);
        double effective = heightDelta / threshold;
        double raw = Math.Max(0.0, effective - 4.5)
            * player.Entity.Properties.FallDamageMultiplier
            * CombatOverhaulCompat.FallDamageCoefficient
            * factor;
        return (float)raw;
    }

    static float RunFall(IPlayer player, EntityBehaviorHealth health, double heightDelta)
    {
        Entity entity = player.Entity;
        float before = health.Health;
        entity.PositionBeforeFalling.Set(
            entity.Pos.X,
            entity.Pos.Y + heightDelta,
            entity.Pos.Z);
        MethodInfo? fall = AccessTools.Method(
            AccessTools.TypeByName(CombatOverhaulCompat.HarmonyPatchesTypeName),
            "OnFallToGround");
        Assert.NotNull(fall);
        object[] args = [health, -1.0];
        object? skippedVanilla = fall!.Invoke(null, args);
        Assert.Equal(
            false,
            skippedVanilla,
            $"OnFallToGround should skip vanilla after applying CO fall damage. "
            + $"height={heightDelta:0.###} threshold={entity.Stats.GetBlended("fallDamageThreshold"):0.###} "
            + $"fallMult={entity.Properties.FallDamageMultiplier:0.###} mode={player.WorldData?.CurrentGameMode}.");
        return before - health.Health;
    }

    static Entity InvokeSpawnProjectile(
        ICoreAPI api,
        Entity shooter,
        ItemStack projectileStack,
        string entityCode,
        float dropChance,
        double velocityX)
    {
        Type? systemType = AccessTools.TypeByName(CombatOverhaulCompat.ProjectileSystemTypeName);
        MethodInfo? spawn = AccessTools.Method(systemType, "SpawnProjectile");
        Assert.NotNull(spawn);

        Type statsType = AccessTools.TypeByName("CombatOverhaul.RangedSystems.ProjectileStats")
            ?? throw new Xunit.Sdk.XunitException("ProjectileStats type missing.");
        Type spawnStatsType = AccessTools.TypeByName("CombatOverhaul.RangedSystems.ProjectileSpawnStats")
            ?? throw new Xunit.Sdk.XunitException("ProjectileSpawnStats type missing.");
        Type vectorType = AccessTools.TypeByName("OpenTK.Mathematics.Vector3d")
            ?? throw new Xunit.Sdk.XunitException("OpenTK.Mathematics.Vector3d type missing.");

        object stats = Activator.CreateInstance(statsType)
            ?? throw new Xunit.Sdk.XunitException("Could not create ProjectileStats.");
        statsType.GetProperty("EntityCode")!.SetValue(stats, entityCode);
        statsType.GetProperty("DropChance")!.SetValue(stats, dropChance);

        object spawnStats = Activator.CreateInstance(spawnStatsType)
            ?? throw new Xunit.Sdk.XunitException("Could not create ProjectileSpawnStats.");
        object position = Activator.CreateInstance(
            vectorType,
            shooter.Pos.X,
            shooter.Pos.Y + 1.0,
            shooter.Pos.Z)!;
        object velocity = Activator.CreateInstance(vectorType, velocityX, 0.0, 0.0)!;
        spawnStatsType.GetProperty("Position")!.SetValue(spawnStats, position);
        spawnStatsType.GetProperty("Velocity")!.SetValue(spawnStats, velocity);
        spawnStatsType.GetProperty("DamageMultiplier")!.SetValue(spawnStats, 1f);
        spawnStatsType.GetProperty("DamageTier")!.SetValue(spawnStats, 0);
        spawnStatsType.GetProperty("ProducerEntityId")!.SetValue(spawnStats, shooter.EntityId);

        Assert.NotNull(
            api.World.GetEntityType(new AssetLocation(entityCode)),
            $"Entity type {entityCode} is not loaded.");

        object?[] args =
        [
            Guid.NewGuid(),
            projectileStack,
            null,
            stats,
            spawnStats,
            api,
            shooter,
            shooter,
            null
        ];
        try
        {
            spawn!.Invoke(null, args);
        }
        catch (TargetInvocationException ex)
        {
            throw new Xunit.Sdk.XunitException(
                $"SpawnProjectile threw: {ex.InnerException?.GetType().Name}: {ex.InnerException?.Message}",
                ex.InnerException);
        }

        Assert.True(
            args[8] is Entity,
            $"SpawnProjectile should produce a projectile entity. out={args[8]?.GetType().FullName ?? "null"}.");
        return (Entity)args[8]!;
    }

    static Item RequireItem(IWorldAccessor world, string code)
    {
        Item? item = world.GetItem(new AssetLocation(code));
        Assert.True(item is { Id: > 0 }, $"Item {code} is not loaded.");
        return item!;
    }

    static void SetStackProp(Entity entity, string name, ItemStack stack)
    {
        PropertyInfo? prop = entity.GetType().GetProperty(
            name,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        Assert.NotNull(prop);
        prop!.SetValue(entity, stack);
    }

    static bool TryReadFloatProp(Entity entity, string name, out float value)
    {
        value = 0f;
        PropertyInfo? prop = entity.GetType().GetProperty(
            name,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (prop == null)
        {
            return false;
        }

        object? raw = prop.GetValue(entity);
        if (raw is not float f)
        {
            return false;
        }

        value = f;
        return true;
    }
}
