using Vintagestory.API.Common;

namespace Prosequor.Ability;

/// <summary>
/// Pedigree-only block entity: no ticks, POI, or particles. Hosts Live stamping for
/// blocks that otherwise have no BE (e.g. empty skeps).
/// </summary>
public class BlockEntityProsequorPedigree : BlockEntity
{
    /// <summary>ClassRegistry / <c>entityClass</c> name.</summary>
    public const string ClassName = "ProsequorPedigree";
}
