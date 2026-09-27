using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Prosequor.Ability;

namespace Prosequor.Xp.Activity;

/// <summary>One activity marker: activity + standard roles.</summary>
public sealed class ActivityFact
{
    public required string Activity { get; init; }
    public string? Caller { get; init; }

    /// <summary>Legacy alias for <see cref="Caller"/>.</summary>
    public string? Held
    {
        get => Caller;
        init => Caller = value;
    }

    public string? Target { get; init; }
    public string? LastCraft { get; init; }
    public string? Ground { get; init; }
    public string? Mount { get; init; }
    public IReadOnlySet<string> Tokens { get; init; } =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase);
}

/// <summary>Shared per-tick context; ambient held / last-craft for effort materialization.</summary>
public sealed class ActivityCollectContext
{
    public required IServerPlayer Player { get; init; }
    public required EntityAgent Entity { get; init; }
    public string? Held { get; init; }
    public string? LastCraft { get; init; }
    public float DtGameSeconds { get; init; }
    public double NowTotalHours { get; init; }

    public ActivityFact Fact(
        string activity,
        string? target = null,
        string? ground = null,
        IEnumerable<string>? tokens = null,
        string? mount = null) =>
        new()
        {
            Activity = activity,
            Caller = Held,
            Target = target,
            LastCraft = LastCraft,
            Ground = ground,
            Mount = mount,
            Tokens = tokens == null
                ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                : new HashSet<string>(tokens, StringComparer.OrdinalIgnoreCase)
        };

    public Block? BlockUnderFeet()
    {
        BlockPos feet = Entity.Pos.AsBlockPos.DownCopy();
        Block? below = Entity.World.BlockAccessor.GetBlock(feet);
        return below != null && below.Id != 0 ? below : null;
    }
}
