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

public interface IActivityWrapper
{
    string Id { get; }

    /// <summary>
    /// Legacy collect into arbitrary activity facts. Prefer <see cref="Effort.RegisterPoll"/> /
    /// <see cref="Effort.Emit"/> for <c>prosequor:effort</c> rate XP.
    /// </summary>
    void Collect(ActivityCollectContext context, List<ActivityFact> into);
}

public interface IActivityWrapperRegistry
{
    IReadOnlyList<IActivityWrapper> All { get; }
    void Register(IActivityWrapper wrapper);
}

public sealed class ActivityWrapperRegistry : IActivityWrapperRegistry
{
    readonly List<IActivityWrapper> ordered = new();
    readonly Dictionary<string, IActivityWrapper> byId = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<IActivityWrapper> All => ordered;

    public void Register(IActivityWrapper wrapper)
    {
        if (wrapper == null || string.IsNullOrWhiteSpace(wrapper.Id))
        {
            return;
        }

        string id = wrapper.Id.Trim();
        if (byId.TryGetValue(id, out IActivityWrapper? existing))
        {
            ordered.Remove(existing);
        }

        byId[id] = wrapper;
        ordered.Add(wrapper);
    }
}
