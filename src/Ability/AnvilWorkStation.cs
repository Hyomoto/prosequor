using Prosequor.Ability.Hooks;
using Prosequor.Player;
using Vintagestory.API.Common;

namespace Prosequor.Ability;

/// <summary>
/// Resolves anvil <c>item-interaction</c> folds: heavy-hit, split recovery, and
/// strike cooling-debt shrink.
/// </summary>
public static class AnvilWorkStation
{
    public static int ResolveSlagRadius(IPlayer player) =>
        RunInt(player, VerbIds.AnvilHeavyHit, HookIds.SlagRadius, AnvilVoxelGrid.RadiusOff);

    public static int ResolveAssistRadius(IPlayer player) =>
        RunInt(player, VerbIds.AnvilHeavyHit, HookIds.AssistRadius, AnvilVoxelGrid.RadiusOff);

    public static int ResolveMoveCount(IPlayer player) =>
        Math.Max(0, RunInt(player, VerbIds.AnvilHeavyHit, HookIds.MoveCount, 0));

    /// <summary>Splits per refunded metal bit; ≤0 means Metal Recovery inactive.</summary>
    public static int ResolveBitsRefund(IPlayer player) =>
        Math.Max(0, RunInt(player, VerbIds.AnvilSplit, HookIds.BitsRefund, 0));

    /// <summary>Cooling-debt shrink fraction per strike; ≤0 means Heated Strikes inactive.</summary>
    public static float ResolveDecayShrink(IPlayer player) =>
        Math.Max(0f, RunFloat(player, VerbIds.AnvilStrike, HookIds.DecayShrink, 0f));

    static int RunInt(IPlayer player, VerbId verb, PhaseId phase, int seed)
    {
        if (!TryBuild(player, verb, out VoxelWorkContext? context, out AbilityPipeline? pipeline)
            || context == null
            || pipeline == null)
        {
            return seed;
        }

        return pipeline.Run(
            HookIds.ItemInteraction,
            verb,
            phase,
            context,
            seed);
    }

    static float RunFloat(IPlayer player, VerbId verb, PhaseId phase, float seed)
    {
        if (!TryBuild(player, verb, out VoxelWorkContext? context, out AbilityPipeline? pipeline)
            || context == null
            || pipeline == null)
        {
            return seed;
        }

        return pipeline.Run(
            HookIds.ItemInteraction,
            verb,
            phase,
            context,
            seed);
    }

    static bool TryBuild(
        IPlayer player,
        VerbId verb,
        out VoxelWorkContext? context,
        out AbilityPipeline? pipeline)
    {
        context = null;
        pipeline = null;
        if (player?.Entity == null)
        {
            return false;
        }

        ProsequorModSystem? mod = ProsequorModSystem.For(player.Entity.Api);
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
            verb.Value,
            held: EventFactBuilder.CodeOf(player.InventoryManager?.ActiveHotbarSlot?.Itemstack));

        context = new VoxelWorkContext
        {
            World = player.Entity.World,
            Player = player,
            Progress = progress,
            Fact = fact
        };
        pipeline = mod.Pipeline;
        return true;
    }
}
