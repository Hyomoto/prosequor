using Prosequor.Ability;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;

namespace Prosequor.Xp;

/// <summary>
/// GameReady catalog of authored animal <see cref="EntityProperties.Weight"/> min/max.
/// Amount tables with <c>pay: effort</c> on hunted / trapped deeds normalize against this span.
/// </summary>
public sealed class AnimalWeightCatalog
{
    public const string MetricDomain = "animal-weight";

    public readonly record struct Range(float Min, float Max, int EntityCount);

    readonly Range? span;

    AnimalWeightCatalog(Range? span)
    {
        this.span = span;
    }

    public int EntityCount => span?.EntityCount ?? 0;

    public float Min => span?.Min ?? 0f;

    public float Max => span?.Max ?? 0f;

    public static AnimalWeightCatalog Build(ICoreAPI api)
    {
        if (api?.World?.EntityTypes == null)
        {
            return new AnimalWeightCatalog(null);
        }

        float min = float.MaxValue;
        float max = float.MinValue;
        int count = 0;

        foreach (EntityProperties props in api.World.EntityTypes)
        {
            if (!IsCatalogAnimal(props))
            {
                continue;
            }

            float weight = props.Weight;
            min = Math.Min(min, weight);
            max = Math.Max(max, weight);
            count++;
        }

        if (count <= 0)
        {
            return new AnimalWeightCatalog(null);
        }

        return new AnimalWeightCatalog(new Range(min, max, count));
    }

    public bool TryGetRange(out float min, out float max)
    {
        min = 0f;
        max = 0f;
        if (span is not Range range || range.EntityCount <= 0)
        {
            return false;
        }

        min = range.Min;
        max = range.Max;
        return true;
    }

    public Range? TryGet() => span;

    public static bool IsAnimal(Entity? entity) =>
        entity is EntityAgent
        && entity is not EntityPlayer
        && IsCatalogAnimal(entity.Properties);

    public static bool IsCatalogAnimal(EntityProperties? props)
    {
        if (props == null || props.Weight <= 0f)
        {
            return false;
        }

        string path = props.Code?.Path ?? "";
        if (path.Length == 0)
        {
            return false;
        }

        // Exclude projectiles, boats, elevators, and other non-fauna with a weight field.
        if (path.Contains("projectile", StringComparison.OrdinalIgnoreCase)
            || path.Contains("boat", StringComparison.OrdinalIgnoreCase)
            || path.Contains("elevator", StringComparison.OrdinalIgnoreCase)
            || path.Contains("humanoid", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("player", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        string? cls = props.Class;
        if (!string.IsNullOrWhiteSpace(cls)
            && !cls.Equals("EntityAgent", StringComparison.OrdinalIgnoreCase)
            && !cls.Equals("Entity", StringComparison.OrdinalIgnoreCase)
            && cls.Contains("Projectile", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return true;
    }

    public static bool TryMeasure(Entity? entity, out float weight, out float min, out float max)
    {
        weight = 0f;
        min = 0f;
        max = 0f;
        if (!IsAnimal(entity) || entity!.Properties == null)
        {
            return false;
        }

        weight = entity.Properties.Weight;
        ProsequorModSystem? mod = ProsequorModSystem.For(entity.Api);
        if (mod?.AnimalWeight == null || !mod.AnimalWeight.TryGetRange(out min, out max))
        {
            min = weight;
            max = weight;
        }

        return true;
    }
}
