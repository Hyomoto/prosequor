using Vintagestory.API.MathTools;

namespace Prosequor.Ability;

/// <summary>Immutable server-side completed-action fact for ability matching.</summary>
public sealed class AbilityAction
{
    public required string Verb { get; init; }
    public required string ActorUid { get; init; }

    /// <summary>
    /// Instrument / source of the event (<c>caller</c> in when.tags). Legacy authoring: <c>held</c>.
    /// </summary>
    public string? Caller { get; init; }

    /// <summary>Legacy alias for <see cref="Caller"/> (JSON <c>held:</c> still parses to this field).</summary>
    public string? Held
    {
        get => Caller;
        init => Caller = value;
    }

    /// <summary>The thing this event is about (block, catch, product, mount, …).</summary>
    public string? Target { get; init; }

    /// <summary>
    /// Current drop stack code while iterating mutate-drops quantity/stack phases.
    /// </summary>
    public string? Drop { get; init; }

    /// <summary>Sticky last craft-grid product code, when known.</summary>
    public string? LastCraft { get; init; }

    /// <summary>Underfoot / water when distinct from <see cref="Target"/>.</summary>
    public string? Ground { get; init; }

    /// <summary>Mount / boat entity code when distinct from <see cref="Target"/>.</summary>
    public string? Mount { get; init; }

    /// <summary>Voxel operation identity (place, remove, refill, …).</summary>
    public string? Op { get; init; }

    /// <summary>
    /// Craft-grid ingredient codes present before consume (for <c>input:&lt;collection&gt;</c>).
    /// </summary>
    public IReadOnlyList<string> Inputs { get; init; } = Array.Empty<string>();

    /// <summary>Event tokens such as <c>moving</c> / <c>helmsman</c>.</summary>
    public IReadOnlySet<string> Tokens { get; init; } =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>Damage kinds such as <c>frost</c> / <c>weather</c>.</summary>
    public IReadOnlySet<string> Damage { get; init; } =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    public BlockPos? Position { get; init; }
}
