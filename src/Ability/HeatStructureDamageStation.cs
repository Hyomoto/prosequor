using System;
using Prosequor.Ability.Hooks;
using Prosequor.Player;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace Prosequor.Ability;

/// <summary>
/// Beehive kiln / stone coffin structure heat damage: fold skip chance for fireclay
/// and refractory brick damage placements.
/// </summary>
public static class HeatStructureDamageStation
{
    /// <summary>
    /// True when <paramref name="damaged"/> is a fireclay / refractory structure tile
    /// and the credited player's Construction unlock skips this damage roll.
    /// </summary>
    public static bool ShouldSkipDamage(IPlayer? player, Block? damaged, IWorldAccessor? world)
    {
        if (player?.Entity == null || damaged?.Code == null || world == null)
        {
            return false;
        }

        if (world.Side != EnumAppSide.Server
            && world.Api?.Side != EnumAppSide.Server)
        {
            return false;
        }

        if (!IsHeatStructureBlock(damaged))
        {
            return false;
        }

        ProsequorModSystem? mod = ProsequorModSystem.For(world.Api ?? player.Entity.Api);
        if (mod?.Pipeline == null)
        {
            return false;
        }

        IPlayerProgress? progress = ProsequorModSystem.GetProgress(player);
        if (progress == null)
        {
            return false;
        }

        AbilityAction fact = EventFactBuilder.ForPlayer(
            player,
            VerbIds.HeatStructureDamage.Value,
            target: EventFactBuilder.CodeOf(damaged),
            includeLastCraft: false);

        HeatStructureDamageContext context = new()
        {
            Player = player,
            Progress = progress,
            Fact = fact,
            World = world
        };

        float skip = mod.Pipeline.Run(
            HookIds.BlockInteraction,
            VerbIds.HeatStructureDamage,
            HookIds.Skip,
            context,
            0f);

        if (skip <= 0f)
        {
            return false;
        }

        Random rand = world.Rand ?? new Random(0);
        return rand.NextDouble() < skip;
    }

    public static bool IsHeatStructureBlock(Block? block) =>
        IsHeatStructurePath(block?.Code?.Path);

    public static bool IsHeatStructurePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        return path.StartsWith("claybricks-", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("refractorybricks-", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("refractorybrickgrating-", StringComparison.OrdinalIgnoreCase);
    }

    public static IPlayer? ResolveKilnPlayer(BlockEntityBeeHiveKiln? kiln)
    {
        if (kiln?.Api?.World == null)
        {
            return null;
        }

        string? uid = BeeHiveKilnFirerStation.TryGetSessionFirerUid(kiln);
        return ResolvePlayer(kiln.Api.World, uid);
    }

    public static IPlayer? ResolveCoffinPlayer(BlockEntityStoneCoffin? coffin)
    {
        if (coffin?.Api?.World == null)
        {
            return null;
        }

        if (!ProsequorBlockPedigreeStation.TryGetBlob(coffin, out ProsequorBlob blob))
        {
            return null;
        }

        if (blob.TryGetSoleContributor(out string? sole) && !string.IsNullOrWhiteSpace(sole))
        {
            return ResolvePlayer(coffin.Api.World, sole);
        }

        string? bestUid = null;
        int bestWeight = 0;
        for (int i = 0; i < blob.Contributors.Count; i++)
        {
            ProsequorBlob.Share share = blob.Contributors[i];
            if (string.IsNullOrWhiteSpace(share.PlayerUid) || share.Weight <= bestWeight)
            {
                continue;
            }

            bestUid = share.PlayerUid;
            bestWeight = share.Weight;
        }

        if (bestUid == null && !string.IsNullOrWhiteSpace(blob.MakerUid))
        {
            bestUid = blob.MakerUid;
        }

        return ResolvePlayer(coffin.Api.World, bestUid);
    }

    public static IPlayer? ResolveOwnerAtCenter(IWorldAccessor world, BlockPos centerPos)
    {
        if (world?.BlockAccessor == null || centerPos == null)
        {
            return null;
        }

        BlockEntity? be = world.BlockAccessor.GetBlockEntity(centerPos);
        if (be is BlockEntityBeeHiveKiln kiln)
        {
            return ResolveKilnPlayer(kiln);
        }

        if (be is BlockEntityStoneCoffin coffin)
        {
            return ResolveCoffinPlayer(coffin);
        }

        return null;
    }

    /// <summary>
    /// Heat-damage callback replacement: same resist roll as vanilla, with Construction skip.
    /// </summary>
    public static void ApplyHeatDamage(
        IPlayer? player,
        IWorldAccessor world,
        Block block,
        BlockPos pos,
        Action? onDamaged)
    {
        if (world == null || block == null || pos == null)
        {
            return;
        }

        float resist = block.Attributes != null
            ? block.Attributes["heatResistance"].AsFloat(1f)
            : 1f;
        if (world.Rand.NextDouble() <= resist)
        {
            return;
        }

        Block? damaged = world.GetBlock(block.CodeWithVariant("state", "damaged"));
        if (damaged == null || damaged.Id == 0 || damaged.Id == block.Id)
        {
            return;
        }

        if (ShouldSkipDamage(player, damaged, world))
        {
            return;
        }

        world.BlockAccessor.SetBlock(damaged.Id, pos);
        onDamaged?.Invoke();
    }

    static IPlayer? ResolvePlayer(IWorldAccessor world, string? uid)
    {
        if (string.IsNullOrWhiteSpace(uid))
        {
            return null;
        }

        return world.PlayerByUid(uid.Trim());
    }
}
