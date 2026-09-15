namespace Prosequor.Ability;

/// <summary>
/// One-shot care roles on a farmland crop cycle. Each kind adds contributor weight 1
/// the first time that player performs it; harvest <see cref="ProsequorBlockPedigreeStation.Clear"/>
/// wipes the flags with the bag.
/// </summary>
[Flags]
public enum FarmlandCareKind
{
    None = 0,
    Till = 1,
    Plant = 2,
    Fertilize = 4,
    Water = 8
}
