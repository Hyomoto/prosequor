using System.Reflection;
using Atlas.Api;
using Atlas.XUnit;
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
/// Hunting XP when a food animal dies to a bow. The melee case is a left-click
/// attack while the bow is in hand. The shot case is <see cref="ItemBow.OnHeldInteractStop"/>
/// and the spawned arrow's own impact. Both must pay <c>hunting</c> for a
/// non-cleaver-certain kill.
/// </summary>
public class HuntKillXpScenarios : HuntKillXpFixture
{
    [AtlasScenario(TimeoutMs = 180_000)]
    [Trait("Layer", "Xp")]
    [Trait("Kind", "Hunting")]
    public async Task BowMeleeHit_Should_PayHuntingXpOnDeath()
    {
        IPlayer player = await JoinHunter("HuntBowHit");
        Item bow = RequireBow(World.Api.World);
        ItemSlot bowSlot = Hold(player, bow);
        Entity animal = SpawnPrey(player);
        await World.Ticks(2);

        string target = animal.Code.ToString();
        string caller = EventFactBuilder.HeldCode(player) ?? bow.Code.ToString();
        float expected = ExpectedHuntingKill(World.Api.World, player.PlayerUID, caller, target, animal.Properties.Weight);
        Assert.True(expected > 0f, $"Loaded hunting rules should pay for a bow melee kill of {target}.");

        WoundToNextHit(animal);
        float before = HuntingXp(player);
        animal.OnInteract(
            (EntityAgent)player.Entity,
            bowSlot,
            new Vec3d(0.2, 0.3, 0.2),
            EnumInteractMode.Attack);
        await World.Ticks(1);

        Assert.False(
            animal.Alive,
            $"Bow melee hit should kill the wounded {target}. health={HealthOf(animal):0.###} "
            + $"attack={bow.GetAttackPower(bowSlot.Itemstack):0.###} caller={caller}.");
        ScenarioXp.AssertPaid(HuntingXp(player) - before, expected, $"bow melee kill of {target}");
    }

    [AtlasScenario(TimeoutMs = 180_000)]
    [Trait("Layer", "Xp")]
    [Trait("Kind", "Hunting")]
    public async Task BowShotArrow_Should_PayHuntingXpOnDeath()
    {
        await AssertBowShotPays("HuntBowShot", arrowCode: null);
    }
}

/// <summary>
/// Butchering bone arrows (<c>butchering:arrow-bone</c>) fired from the vanilla bow.
/// Same shot and impact path as <see cref="HuntKillXpScenarios.BowShotArrow_Should_PayHuntingXpOnDeath"/>.
/// </summary>
[AtlasWorld(Mods = ["staged-butchering"])]
public class ButcheringBoneArrowHuntScenarios : HuntKillXpFixture
{
    [AtlasScenario(TimeoutMs = 180_000)]
    [Trait("Layer", "Xp")]
    [Trait("Kind", "Hunting")]
    public async Task BoneArrowShot_Should_PayHuntingXpOnDeath()
    {
        await AssertBowShotPays("HuntBoneArrow", "butchering:arrow-bone");
    }
}

public abstract class HuntKillXpFixture : AtlasScenarioBase
{
    protected const string Skill = "hunting";

    static readonly string[] PreyCodes =
    [
        "game:hare-european-adult-male",
        "game:hare-arctic-adult-male",
        "game:hare-european-adult-female",
        "game:chicken-hen"
    ];

    protected async Task AssertBowShotPays(string playerName, string? arrowCode)
    {
        IPlayer player = await JoinHunter(playerName);
        IWorldAccessor world = World.Api.World;
        Item bow = RequireBow(world);
        Item arrow = RequireArrow(world, arrowCode);
        ItemSlot bowSlot = Hold(player, bow);
        Give(player, arrow, hotbarIndex: 1, count: 8);

        Entity animal = SpawnPrey(player);
        await World.Ticks(2);
        Assert.True(animal.Alive, "Prey should be alive before the shot.");

        string loadedArrow = arrow.Code.ToString();
        string? entityCode = arrow.Attributes?["arrowEntityCode"].AsString();
        HashSet<long> existing = EntityIdsNear(player.Entity.Pos.XYZ, 16f);
        ((EntityAgent)player.Entity).Attributes.SetInt("aimingCancel", 0);
        bow.OnHeldInteractStop(1f, bowSlot, (EntityAgent)player.Entity, null, null);

        EntityProjectile shot = RequireNewProjectile(player.Entity.Pos.XYZ, existing);
        string shotArrow = shot.ProjectileStack?.Collectible?.Code?.ToString() ?? "";
        Assert.True(
            shot.FiredBy?.EntityId == player.Entity.EntityId,
            $"The arrow spawned by the bow should record the shooter. loaded={loadedArrow} "
            + $"arrowEntityCode={entityCode ?? "<missing>"} shot={shot.Code} stack={shotArrow}.");
        Assert.Equal(bow.Code.ToString(), shot.WeaponStack?.Collectible?.Code?.ToString());
        Assert.Equal(loadedArrow, shotArrow);

        string target = animal.Code.ToString();
        float expected = ExpectedHuntingKill(
            world,
            player.PlayerUID,
            shotArrow,
            target,
            animal.Properties.Weight);
        Assert.True(
            expected > 0f,
            $"Loaded hunting rules should pay for an arrow kill of {target}. caller={shotArrow}.");

        WoundToNextHit(animal);
        float before = HuntingXp(player);
        Impact(shot, animal);
        await World.Ticks(1);

        Assert.False(
            animal.Alive,
            $"Arrow impact should kill the wounded {target}. health={HealthOf(animal):0.###} "
            + $"arrowDamage={shot.Damage:0.###} firedBy={shot.FiredBy?.Code} "
            + $"projectile={shot.Code} stack={shotArrow} entityAttr={entityCode ?? "<missing>"} "
            + $"arrowAlive={shot.Alive}.");
        ScenarioXp.AssertPaid(
            HuntingXp(player) - before,
            expected,
            $"arrow shot kill of {target} arrow={shotArrow} projectile={shot.Code} damage={shot.Damage:0.###}");
    }

    protected async Task<IPlayer> JoinHunter(string name)
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

    protected Entity SpawnPrey(IPlayer player)
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

    /// <summary>
    /// Headless ticks do not sweep the arrow's hit box into the animal.
    /// <c>ImpactOnEntity</c> is the server method a connecting arrow calls.
    /// </summary>
    static void Impact(EntityProjectile shot, Entity animal)
    {
        MethodInfo? impact = typeof(EntityProjectileBase).GetMethod(
            "ImpactOnEntity",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        Assert.NotNull(impact);
        impact!.Invoke(shot, [animal]);
    }

    protected static void WoundToNextHit(Entity animal)
    {
        EntityBehaviorHealth? health = animal.GetBehavior<EntityBehaviorHealth>();
        Assert.NotNull(health);
        Assert.True(animal.Alive && health!.Health > 0f, $"{animal.Code} has no health to wound.");
        health.Health = 0.2f;
    }

    protected static float HealthOf(Entity animal) =>
        animal.GetBehavior<EntityBehaviorHealth>()?.Health ?? -1f;

    protected static float HuntingXp(IPlayer player) => ScenarioXp.TotalSkill(player, Skill);

    protected static float ExpectedHuntingKill(
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

    HashSet<long> EntityIdsNear(Vec3d origin, float range)
    {
        HashSet<long> ids = [];
        foreach (Entity entity in World.Api.World.GetEntitiesAround(origin, range, range, _ => true))
        {
            ids.Add(entity.EntityId);
        }

        return ids;
    }

    EntityProjectile RequireNewProjectile(Vec3d origin, HashSet<long> existing)
    {
        EntityProjectile? shot = null;
        foreach (Entity entity in World.Api.World.GetEntitiesAround(origin, 16f, 8f, entity => entity is EntityProjectile))
        {
            if (existing.Contains(entity.EntityId))
            {
                continue;
            }

            shot = (EntityProjectile)entity;
        }

        Assert.NotNull(shot);
        return shot!;
    }

    protected static ItemSlot Hold(IPlayer player, Item bow)
    {
        IInventory hotbar = player.InventoryManager.GetHotbarInventory();
        Assert.True(hotbar.Count >= 2, "Expected a hotbar with at least two slots.");
        player.InventoryManager.ActiveHotbarSlotNumber = 0;
        ItemSlot? slot = hotbar[0];
        Assert.NotNull(slot);
        Assert.IsNotType<ItemSlotCreative>(slot);
        slot.Itemstack = new ItemStack(bow, 1);
        slot.MarkDirty();
        return slot;
    }

    static void Give(IPlayer player, Item item, int hotbarIndex, int count)
    {
        IInventory hotbar = player.InventoryManager.GetHotbarInventory();
        ItemSlot? slot = hotbar[hotbarIndex];
        Assert.NotNull(slot);
        Assert.IsNotType<ItemSlotCreative>(slot);
        slot.Itemstack = new ItemStack(item, count);
        slot.MarkDirty();
    }

    protected static Item RequireBow(IWorldAccessor world)
    {
        Item? preferred = world.GetItem(new AssetLocation("game:bow-simple"));
        if (preferred is ItemBow found)
        {
            return found;
        }

        foreach (Item item in world.Items)
        {
            if (item is ItemBow && item.Id != 0)
            {
                return item;
            }
        }

        Assert.Fail("No ItemBow is loaded.");
        throw new InvalidOperationException();
    }

    static Item RequireArrow(IWorldAccessor world, string? code)
    {
        string preferredCode = string.IsNullOrEmpty(code) ? "game:arrow-flint" : code;
        Item? preferred = world.GetItem(new AssetLocation(preferredCode));
        if (preferred is { Id: > 0 })
        {
            return preferred;
        }

        if (!string.IsNullOrEmpty(code))
        {
            Assert.Fail($"Arrow {code} is not loaded.");
            throw new InvalidOperationException();
        }

        foreach (Item item in world.Items)
        {
            string? path = item?.Code?.Path;
            if (item != null
                && item.Id != 0
                && item.Code?.Domain == "game"
                && path != null
                && path.StartsWith("arrow-", StringComparison.Ordinal))
            {
                return item;
            }
        }

        Assert.Fail("No arrow item is loaded.");
        throw new InvalidOperationException();
    }
}
