using Prosequor.Ability.Hooks;
using Prosequor.Player;
using Vintagestory.API.Common;

namespace Prosequor.Ability;

/// <summary>
/// Runs <c>item-interaction</c> clay-form / voxel-copy / voxel-refill phases for the active player.
/// </summary>
public static class VoxelWorkStation
{
    public const int AssistRadiusOff = -1;
    public const int VanillaVoxelCopy = 4;
    public const int VanillaVoxelRefill = 25;

    /// <summary>How many voxels a layer-copy moves. Vanilla 4 plus any skill bonus.</summary>
    public static int ResolveVoxelCopy(IPlayer player) =>
        Math.Max(1, VanillaVoxelCopy + Math.Max(0, RunBonus(player, VerbIds.VoxelCopy)));

    /// <summary>How many voxels one clay piece refills. Vanilla 25 plus any skill bonus.</summary>
    public static int ResolveVoxelRefill(IPlayer player) =>
        Math.Max(1, VanillaVoxelRefill + Math.Max(0, RunBonus(player, VerbIds.VoxelRefill)));

    public static int RunInt(
        IPlayer player,
        string verb,
        IEnumerable<string> opTags,
        PhaseId phase,
        int seed)
    {
        if (!TryBuild(player, VerbIds.ClayForm.Value, opTags, out VoxelWorkContext? context, out AbilityPipeline? pipeline)
            || context == null
            || pipeline == null)
        {
            return seed;
        }

        return pipeline.Run(HookIds.ItemInteraction, VerbIds.ClayForm, phase, context, seed);
    }

    public static float RunFloat(
        IPlayer player,
        string verb,
        IEnumerable<string> opTags,
        PhaseId phase,
        float seed)
    {
        if (!TryBuild(player, VerbIds.ClayForm.Value, opTags, out VoxelWorkContext? context, out AbilityPipeline? pipeline)
            || context == null
            || pipeline == null)
        {
            return seed;
        }

        return pipeline.Run(HookIds.ItemInteraction, VerbIds.ClayForm, phase, context, seed);
    }

    /// <summary>Strips a leading <c>op:</c> prefix so facts store bare ops (<c>place</c>).</summary>
    public static string? NormalizeOp(string? tag)
    {
        if (string.IsNullOrWhiteSpace(tag))
        {
            return null;
        }

        string trimmed = tag.Trim();
        if (trimmed.StartsWith("op:", StringComparison.OrdinalIgnoreCase))
        {
            trimmed = trimmed[3..].Trim();
        }

        return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
    }

    static int RunBonus(IPlayer player, VerbId verb)
    {
        if (!TryBuild(player, verb.Value, Array.Empty<string>(), out VoxelWorkContext? context, out AbilityPipeline? pipeline)
            || context == null
            || pipeline == null)
        {
            return 0;
        }

        return pipeline.Run(HookIds.ItemInteraction, verb, HookIds.Default, context, 0);
    }

    static bool TryBuild(
        IPlayer player,
        string verb,
        IEnumerable<string> opTags,
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

        string? op = null;
        foreach (string tag in opTags)
        {
            op = NormalizeOp(tag);
            if (op != null)
            {
                break;
            }
        }

        AbilityAction fact = EventFactBuilder.ForPlayer(player, verb, op: op);

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
