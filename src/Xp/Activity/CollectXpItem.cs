namespace Prosequor.Xp.Activity;

/// <summary>
/// Amount XP for stamped collectible pickup (buffer flush). Same payment engine as
/// <see cref="Deed"/>, separate activity bucket so rules never compete with generic deeds.
/// </summary>
public static class CollectXpItem
{
    public const string Activity = "prosequor:collect-xp-item";
}
