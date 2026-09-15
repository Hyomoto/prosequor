using Prosequor.Ability.Hooks;
using Prosequor.Player;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace Prosequor.Ability;

/// <summary>
/// Resolves <c>prosequor:spawn-bees-chance</c> for skep break (seed = vanilla beemob chance).
/// </summary>
public static class SkepBeeSpawnStation
{
    public const float DefaultVanillaChance = 0.4f;

    public static float ReadVanillaSpawnChance(BlockSkep? skep)
    {
        if (skep == null)
        {
            return DefaultVanillaChance;
        }

        return skep.Attributes?["beemobSpawnChance"].AsFloat(DefaultVanillaChance) ?? DefaultVanillaChance;
    }

    public static float ResolveSpawnChance(IPlayer? player, BlockSkep skep, BlockPos? pos = null)
    {
        float vanilla = ReadVanillaSpawnChance(skep);
        if (player?.Entity == null)
        {
            return vanilla;
        }

        ProsequorModSystem? mod = ProsequorModSystem.For(player.Entity.Api);
        if (mod?.Pipeline == null)
        {
            return vanilla;
        }

        IPlayerProgress? progress = ProsequorModSystem.GetProgress(player);
        if (progress == null)
        {
            return vanilla;
        }

        AbilityAction fact = EventFactBuilder.ForPlayer(
            player,
            AbilityBootstrap.VerbSpawnBeesChance,
            target: EventFactBuilder.CodeOf(skep),
            position: pos);

        SkepBeeSpawnContext context = new()
        {
            Player = player,
            Progress = progress,
            Fact = fact,
            Block = skep,
            BaseValue = vanilla
        };

        float result = mod.Pipeline.Run(
            HookIds.BlockInteraction,
            VerbIds.SpawnBeesChance,
            HookIds.Default,
            context,
            vanilla);
        return Math.Clamp(result, 0f, 1f);
    }
}
