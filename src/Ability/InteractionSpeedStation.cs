using Prosequor.Ability.Hooks;
using Prosequor.Player;
using Vintagestory.API.Common;

namespace Prosequor.Ability;

/// <summary>Shared helpers for interaction-speed thin wrappers.</summary>
public static class InteractionSpeedStation
{
    public static float Run(
        IPlayer player,
        string? targetCode,
        string? heldCode,
        float baseValue,
        EnumBlockMaterial? material = null,
        HookId? hook = null)
    {
        if (player?.Entity == null)
        {
            return baseValue;
        }

        ProsequorModSystem? mod = ProsequorModSystem.For(player.Entity.Api);
        if (mod?.Pipeline == null)
        {
            return baseValue;
        }

        IPlayerProgress? progress = ProsequorModSystem.GetProgress(player);
        if (progress == null)
        {
            return baseValue;
        }

        HookId surface = hook ?? HookIds.BlockInteraction;

        AbilityAction fact = EventFactBuilder.ForPlayer(
            player,
            VerbIds.InteractionSpeed.Value,
            target: targetCode,
            held: heldCode);

        InteractionSpeedContext context = new()
        {
            Hook = surface,
            Player = player,
            Progress = progress,
            Fact = fact,
            Material = material,
            BaseValue = baseValue
        };

        return mod.Pipeline.Run(surface, VerbIds.InteractionSpeed, HookIds.Default, context, baseValue);
    }
}
