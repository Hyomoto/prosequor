using Newtonsoft.Json.Linq;
using Prosequor.Ability;
using Prosequor.Ability.Hooks;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;

namespace Prosequor.Xp;

/// <summary>
/// GameReady weights of animals whose harvest drops include raw meat or fat.
/// <see cref="MetricDomain"/> is that whole set. <see cref="MetricDomainTrappable"/> is the
/// subset with a trap chance above zero. Amount tables with <c>pay: effort</c> lerp against
/// one of those spans. Resolved <see cref="EntityProperties.Weight"/> already includes
/// <c>weightByType</c>.
/// </summary>
public sealed class AnimalWeightCatalog
{
    public const string MetricDomain = "animal-weight";

    public const string MetricDomainTrappable = "animal-weight-trappable";

    /// <summary>
    /// Cleaver slaughter chance is <c>generation / 3</c>. At this generation the hit always kills.
    /// </summary>
    public const int CleaverCertainGeneration = 3;

    public readonly record struct Range(float Min, float Max, int EntityCount);

    readonly Range? food;
    readonly Range? trappable;
    readonly HashSet<string> foodCodes;

    AnimalWeightCatalog(Range? food, Range? trappable, HashSet<string> foodCodes)
    {
        this.food = food;
        this.trappable = trappable;
        this.foodCodes = foodCodes;
    }

    public int EntityCount => food?.EntityCount ?? 0;

    public float Min => food?.Min ?? 0f;

    public float Max => food?.Max ?? 0f;

    public int TrappableCount => trappable?.EntityCount ?? 0;

    public static AnimalWeightCatalog Build(ICoreAPI api, CollectionIndex? collections)
    {
        if (api?.World?.EntityTypes == null || collections == null)
        {
            return new AnimalWeightCatalog(null, null, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
        }

        float foodMin = float.MaxValue;
        float foodMax = float.MinValue;
        int foodCount = 0;
        float trapMin = float.MaxValue;
        float trapMax = float.MinValue;
        int trapCount = 0;
        HashSet<string> codes = new(StringComparer.OrdinalIgnoreCase);

        foreach (EntityProperties props in api.World.EntityTypes)
        {
            if (props?.Code == null || props.Weight <= 0f || !DropsRawFood(props, collections))
            {
                continue;
            }

            string code = props.Code.ToString();
            codes.Add(code);
            float weight = props.Weight;
            foodMin = Math.Min(foodMin, weight);
            foodMax = Math.Max(foodMax, weight);
            foodCount++;

            if (!HasTrapChance(props))
            {
                continue;
            }

            trapMin = Math.Min(trapMin, weight);
            trapMax = Math.Max(trapMax, weight);
            trapCount++;
        }

        Range? foodSpan = foodCount > 0 ? new Range(foodMin, foodMax, foodCount) : null;
        Range? trapSpan = trapCount > 0 ? new Range(trapMin, trapMax, trapCount) : null;
        return new AnimalWeightCatalog(foodSpan, trapSpan, codes);
    }

    public bool IsListed(Entity? entity) => IsListed(entity?.Code?.ToString());

    public bool IsListed(string? code) =>
        !string.IsNullOrWhiteSpace(code) && foodCodes.Contains(code.Trim());

    public bool TryGetRange(out float min, out float max) => TryGet(food, out min, out max);

    public bool TryGetTrappableRange(out float min, out float max) =>
        TryGet(trappable, out min, out max);

    public Range? TryGet() => food;

    public Range? TryGetTrappable() => trappable;

    /// <summary>True when a cleaver hit is a certain slaughter (<c>generation &gt;= 3</c>).</summary>
    public static bool CanCleaverSlaughter(int generation) =>
        generation >= CleaverCertainGeneration;

    public static bool CanCleaverSlaughter(Entity? entity) =>
        entity?.WatchedAttributes != null
        && CanCleaverSlaughter(entity.WatchedAttributes.GetInt("generation"));

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

    static bool TryGet(Range? span, out float min, out float max)
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

    static bool DropsRawFood(EntityProperties props, CollectionIndex collections)
    {
        JsonObject[]? behaviors = props.Server?.BehaviorsAsJsonObj;
        if (behaviors == null)
        {
            return false;
        }

        for (int i = 0; i < behaviors.Length; i++)
        {
            JsonObject behavior = behaviors[i];
            if (behavior == null
                || !string.Equals(behavior["code"].AsString(), "harvestable", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!behavior["drops"].Exists)
            {
                continue;
            }

            JsonObject[]? drops = behavior["drops"].AsArray();
            if (drops == null)
            {
                continue;
            }

            for (int d = 0; d < drops.Length; d++)
            {
                if (DropIsRawFood(drops[d], collections))
                {
                    return true;
                }
            }
        }

        return false;
    }

    static bool DropIsRawFood(JsonObject drop, CollectionIndex collections)
    {
        if (drop == null)
        {
            return false;
        }

        if (IsRawFoodCode(drop["code"].AsString(), collections))
        {
            return true;
        }

        if (!drop["codeByType"].Exists || drop["codeByType"].Token is not JObject byType)
        {
            return false;
        }

        foreach (JProperty prop in byType.Properties())
        {
            if (prop.Value.Type == JTokenType.String
                && IsRawFoodCode(prop.Value.ToString(), collections))
            {
                return true;
            }
        }

        return false;
    }

    static bool IsRawFoodCode(string? raw, CollectionIndex collections)
    {
        if (string.IsNullOrWhiteSpace(raw) || raw.Contains('{') || raw.Contains('*'))
        {
            return false;
        }

        AssetLocation loc = new(raw.Trim());
        string code = loc.ToString();
        return collections.Contains("meat", code) || collections.Contains("fat", code);
    }

    static bool HasTrapChance(EntityProperties props)
    {
        JsonObject? attributes = props.Attributes;
        if (attributes == null)
        {
            return false;
        }

        return PositiveTrapChance(attributes["trappable"])
            || PositiveTrapChance(attributes["trappableByType"]);
    }

    static bool PositiveTrapChance(JsonObject node)
    {
        if (node == null || !node.Exists || node.Token == null)
        {
            return false;
        }

        foreach (JToken chance in node.Token.SelectTokens("$..trapChance"))
        {
            if (chance.Type is JTokenType.Float or JTokenType.Integer && chance.Value<double>() > 0d)
            {
                return true;
            }
        }

        return false;
    }
}
