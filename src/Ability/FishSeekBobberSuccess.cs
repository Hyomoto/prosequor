using Prosequor.Ability.Hooks;
using Prosequor.Player;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace Prosequor.Ability;

/// <summary>
/// Live success-chance pipeline for fish bobber-seek rates.
/// Water membership (freshwater/saltwater) is resolved via collections at match time.
/// Baited seeks stamp <see cref="AbilityBootstrap.TokenUsedBait"/>.
/// </summary>
public static class FishSeekBobberSuccess
{
    public const double PlayerRangeBlocks = 10.0;

    public static bool TryResolveWater(
        IWorldAccessor world,
        BlockPos fishPos,
        out string? liquidCode)
    {
        liquidCode = null;

        Block? block = world.BlockAccessor.GetBlock(fishPos);
        string? path = block?.Code?.Path;
        if (string.IsNullOrEmpty(path) || block?.Code == null)
        {
            return false;
        }

        if (path.Contains("saltwater", StringComparison.OrdinalIgnoreCase)
            || path.Contains("water", StringComparison.OrdinalIgnoreCase))
        {
            liquidCode = block.Code.ToString();
            return true;
        }

        return false;
    }

    public static IPlayer? TryNearestPlayerInRange(EntityFish fish)
    {
        IPlayer? player = fish.World.NearestPlayer(fish.Pos.X, fish.Pos.Y, fish.Pos.Z);
        if (player?.Entity == null)
        {
            return null;
        }

        double dx = player.Entity.Pos.X - fish.Pos.X;
        double dy = player.Entity.Pos.Y - fish.Pos.Y;
        double dz = player.Entity.Pos.Z - fish.Pos.Z;
        if (dx * dx + dy * dy + dz * dz > PlayerRangeBlocks * PlayerRangeBlocks)
        {
            return null;
        }

        return player;
    }

    public static double Run(
        IWorldAccessor world,
        IPlayer player,
        EntityFish fish,
        double baseChance,
        string? liquidCode,
        bool usedBait)
    {
        IPlayerProgress? progress = ProsequorModSystem.GetProgress(player);
        ProsequorModSystem? mod = ProsequorModSystem.For(world.Api);
        if (progress == null || mod?.Pipeline == null)
        {
            return baseChance;
        }

        float baseValue = (float)baseChance;
        string[]? tokens = usedBait ? [AbilityBootstrap.TokenUsedBait] : null;
        AbilityAction fact = EventFactBuilder.ForPlayer(
            player,
            AbilityBootstrap.VerbSeekBobber,
            target: liquidCode,
            tokens: tokens,
            position: fish.Pos.AsBlockPos.Copy());

        SuccessChanceContext context = new()
        {
            World = world,
            Player = player,
            Progress = progress,
            Fact = fact,
            BaseValue = baseValue
        };

        float result = mod.Pipeline.Run(
            HookIds.BlockInteraction,
            VerbIds.SeekBobber,
            HookIds.Default,
            context,
            baseValue);
        return GameMath.Clamp(result, 0f, 1f);
    }

    public static void ApplySeekChance(EntityFish fish, ref double chance, bool usedBait)
    {
        if (fish?.World?.Side != EnumAppSide.Server)
        {
            return;
        }

        IPlayer? player = TryNearestPlayerInRange(fish);
        if (player == null)
        {
            return;
        }

        if (!TryResolveWater(fish.World, fish.Pos.AsBlockPos, out string? liquidCode)
            || string.IsNullOrEmpty(liquidCode))
        {
            return;
        }

        chance = Run(fish.World, player, fish, chance, liquidCode, usedBait);
    }
}
