using Atlas.Api;
using Atlas.XUnit;
using Prosequor;
using Prosequor.Ability;
using Prosequor.Data;
using Prosequor.Player;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;
using Xunit;

namespace Prosequor.Scenarios;

/// <summary>
/// Health must not ratcheting-heal from XP. Deed pay must not re-apply Constitution;
/// a real Constitution score change must keep fill against the pre-change pool.
/// </summary>
public class XpHealthSideEffectScenarios : AtlasScenarioBase
{
    const string Forager = "forager";

    [AtlasScenario(FreshWorld = true)]
    [Trait("Layer", "Xp")]
    [Trait("Kind", "HealthSideEffect")]
    public async Task KnifeCutCattail_Should_NotRestoreHealth_WhenDamaged()
    {
        ITestPlayer joined = await World.JoinPlayer("XpHealCattail");
        IPlayer player = joined.Player;
        Entity entity = player.Entity;
        EntityBehaviorHealth health = RequireHealth(entity);
        EntityBehaviorProgress progress = RequireBehavior(player);

        health.UpdateMaxHealth();
        float maxHealth = health.MaxHealth;
        Assert.True(maxHealth > 5f, $"Expected a usable max health, got {maxHealth}.");

        Wound(entity, health, amount: 10f);
        float damaged = health.Health;
        Assert.True(
            damaged < maxHealth - 1f,
            $"Expected a wound before the cut. health={damaged}/{maxHealth}.");

        Block cattail = RequireCattail();
        BlockPos pos = PlaceWild(player, cattail);
        Item knife = RequireKnife();
        player.InventoryManager.ActiveHotbarSlot.Itemstack = new ItemStack(knife, 1);
        player.InventoryManager.ActiveHotbarSlot.MarkDirty();

        BlockSelection sel = new()
        {
            Position = pos,
            Face = BlockFacing.UP,
            HitPosition = new Vec3d(0.5, 0.1, 0.5),
            Block = cattail
        };

        float xpBefore = ScenarioXp.TotalSkill(progress, Forager);
        bool broken = knife.OnBlockBrokenWith(
            World.Api.World,
            entity,
            player.InventoryManager.ActiveHotbarSlot,
            sel);
        if (!broken)
        {
            cattail.OnBlockBroken(World.Api.World, pos, player);
        }

        float gained = ScenarioXp.TotalSkill(progress, Forager) - xpBefore;
        Assert.True(
            gained > 0f,
            $"Expected forager XP from cutting cattail (broken={broken}), got {gained}. block={Code(cattail)} knife={knife.Code}");

        float after = health.Health;
        Assert.True(
            after <= damaged + 0.05f,
            $"Cutting cattail restored health: {damaged:0.###} → {after:0.###} / {maxHealth:0.###} (forager XP +{gained:0.###}).");
    }

    [AtlasScenario(FreshWorld = true)]
    [Trait("Layer", "Xp")]
    [Trait("Kind", "HealthSideEffect")]
    public async Task ConstitutionScoreGain_Should_PreserveFillRatio()
    {
        ITestPlayer joined = await World.JoinPlayer("ConFillKeep");
        IPlayer player = joined.Player;
        Entity entity = player.Entity;
        EntityBehaviorHealth health = RequireHealth(entity);
        IPlayerProgress progress = RequireProgress(player);

        progress.SetAttribute(AttributeIds.Constitution, 10);
        health.UpdateMaxHealth();
        float beforeMax = health.MaxHealth;
        Assert.True(beforeMax > 5f, $"Expected a usable max health at CON 10, got {beforeMax}.");

        // Half pool so fill is unambiguous (not full, not near-empty).
        health.Health = beforeMax * 0.5f;
        float beforeHealth = health.Health;
        float beforeFill = beforeHealth / beforeMax;

        progress.SetAttribute(AttributeIds.Constitution, 11);

        float afterMax = health.MaxHealth;
        float afterHealth = health.Health;
        Assert.True(
            afterMax > beforeMax + 0.05f,
            $"Expected CON 11 to raise max health. before={beforeMax:0.###} after={afterMax:0.###}.");

        float afterFill = afterHealth / afterMax;
        Assert.True(
            Math.Abs(afterFill - beforeFill) <= 0.02f,
            $"Constitution score change must keep fill. before={beforeHealth:0.###}/{beforeMax:0.###} "
            + $"({beforeFill:0.###}) after={afterHealth:0.###}/{afterMax:0.###} ({afterFill:0.###}).");
    }

    static void Wound(Entity entity, EntityBehaviorHealth health, float amount)
    {
        float before = health.Health;
        entity.ReceiveDamage(
            new DamageSource
            {
                Source = EnumDamageSource.Internal,
                Type = EnumDamageType.Injury
            },
            amount);

        if (health.Health >= before - 0.1f)
        {
            health.Health = Math.Max(1f, health.MaxHealth - amount);
        }
    }

    Block RequireCattail()
    {
        IWorldAccessor world = World.Api.World;
        foreach (string path in new[]
                 {
                     "game:tallplant-coopersreed-land-normal-free",
                     "game:tallplant-coopersreed-water-normal-free",
                     "game:tallplant-coopersreed-land-harvested-free"
                 })
        {
            Block? block = world.GetBlock(new AssetLocation(path));
            if (block != null && ForageBlocks.IsReed(block))
            {
                return block;
            }
        }

        foreach (Block block in world.Blocks)
        {
            if (block != null
                && block.Id != 0
                && ForageBlocks.IsReed(block)
                && (block.Code?.Path?.Contains("coopersreed", StringComparison.OrdinalIgnoreCase) ?? false))
            {
                return block;
            }
        }

        Assert.Fail("[prosequor] No cattail (coopers reed) block found.");
        throw new InvalidOperationException();
    }

    Item RequireKnife()
    {
        IWorldAccessor world = World.Api.World;
        foreach (string path in new[]
                 {
                     "game:knife-generic-flint",
                     "game:knife-generic-copper",
                     "game:knife-generic-iron"
                 })
        {
            Item? item = world.GetItem(new AssetLocation(path));
            if (item != null)
            {
                return item;
            }
        }

        Assert.Fail("[prosequor] No knife item found.");
        throw new InvalidOperationException();
    }

    BlockPos PlaceWild(IPlayer nearPlayer, Block block)
    {
        IWorldAccessor world = World.Api.World;
        BlockPos pos = nearPlayer.Entity.Pos.AsBlockPos.AddCopy(2, 0, 0);
        EnsureFloor(pos);
        world.BlockAccessor.SetBlock(0, pos);
        world.BlockAccessor.SetBlock(block.BlockId, pos);
        Assert.Equal(block.Id, world.BlockAccessor.GetBlock(pos).Id);
        Assert.True(
            ForagePlayerPlaced.IsWild(world, block, pos),
            $"SetBlock should leave the cattail wild. block={Code(block)}");
        return pos;
    }

    void EnsureFloor(BlockPos pos)
    {
        IWorldAccessor world = World.Api.World;
        Block? dirt = world.GetBlock(new AssetLocation("game:soil-low-none"))
            ?? world.GetBlock(new AssetLocation("game:dirt"));
        if (dirt != null)
        {
            world.BlockAccessor.SetBlock(dirt.BlockId, pos.DownCopy());
        }
    }

    static EntityBehaviorHealth RequireHealth(Entity entity)
    {
        EntityBehaviorHealth? health = entity.GetBehavior<EntityBehaviorHealth>();
        Assert.NotNull(health);
        return health!;
    }

    static EntityBehaviorProgress RequireBehavior(IPlayer player)
    {
        EntityBehaviorProgress? progress = player.Entity?.GetBehavior<EntityBehaviorProgress>();
        Assert.NotNull(progress);
        return progress!;
    }

    static IPlayerProgress RequireProgress(IPlayer player)
    {
        IPlayerProgress? progress = ProsequorModSystem.GetProgress(player);
        Assert.NotNull(progress);
        return progress!;
    }

    static string Code(Block block) => EventFactBuilder.CodeOf(block) ?? block.Code?.ToString() ?? "?";
}
