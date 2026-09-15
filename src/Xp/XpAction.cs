using Vintagestory.API.MathTools;
using Prosequor.Ability;
using Prosequor.Xp.Activity;

namespace Prosequor.Xp;

/// <summary>Discrete completed-action fact from adapters.</summary>
public sealed class XpAction
{
    public required string Activity { get; init; }
    public required string ActorUid { get; init; }
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
    public BlockPos? Position { get; init; }

    /// <summary>
    /// Ingredient total units for <b>one</b> craft completion (craft XP scaling).
    /// </summary>
    public int TotalUnits { get; init; }

    /// <summary>
    /// How many recipe completions this take represents (shift multi-craft). Default 1.
    /// </summary>
    public int CraftCount { get; init; } = 1;

    /// <summary>
    /// Hardness/metric for amount-table lookup (block Resistance, clay voxels, …).
    /// Zero means missing → table index 0 when a table is configured.
    /// </summary>
    public float Hardness { get; init; }

    /// <summary>
    /// Metric domain for catalog range (dig / mine / chop / clay-voxels).
    /// </summary>
    public string? HardnessDomain { get; init; }

    public XpMatchFact ToMatchFact() => new()
    {
        Activity = Activity,
        Caller = Caller,
        Target = Target,
        LastCraft = LastCraft,
        Ground = Ground,
        Mount = Mount,
        Tokens = Tokens
    };
}

/// <summary>Unified match surface for discrete actions and activity facts.</summary>
public sealed class XpMatchFact
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

    /// <summary>Eaten collectible or craft-grid inputs. Empty when the deed published none.</summary>
    public IReadOnlyList<string> Inputs { get; init; } = Array.Empty<string>();

    public static XpMatchFact FromActivity(ActivityFact fact) => new()
    {
        Activity = fact.Activity,
        Caller = fact.Caller,
        Target = fact.Target,
        LastCraft = fact.LastCraft,
        Ground = fact.Ground,
        Mount = fact.Mount,
        Tokens = fact.Tokens
    };

    public AbilityAction ToAbilityAction() => new()
    {
        Verb = Activity,
        ActorUid = "",
        Caller = Caller,
        Target = Target,
        LastCraft = LastCraft,
        Ground = Ground,
        Mount = Mount,
        Tokens = Tokens,
        Inputs = Inputs
    };
}
