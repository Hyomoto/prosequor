using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Prosequor.Data;
using Prosequor.Xp;

namespace Prosequor.Pure.Tests.ConfigValidation;

/// <summary>
/// Flags keys that the skill JSON DTOs do not declare at that position.
/// Values, ranges, and required fields are out of scope. Bags typed as
/// <see cref="JToken"/> / <see cref="JObject"/> are not walked inward.
/// </summary>
public sealed class SkillShapeRule : IConfigRule
{
    public string Name => "skill-shape";

    public bool AppliesTo(string relativePath) =>
        relativePath.StartsWith("skills/", StringComparison.OrdinalIgnoreCase)
        && relativePath.EndsWith(".json", StringComparison.OrdinalIgnoreCase);

    public void Check(string relativePath, JToken root, IList<ConfigFinding> findings)
    {
        if (root is not JObject skill)
        {
            findings.Add(new ConfigFinding(
                relativePath,
                "/",
                Name,
                "skill file must be a JSON object."));
            return;
        }

        WalkObject(relativePath, "/", skill, typeof(SkillDefJson), "skill", findings);
    }

    static void WalkObject(
        string relativePath,
        string pointer,
        JObject obj,
        Type dtoType,
        string positionLabel,
        IList<ConfigFinding> findings)
    {
        HashSet<string> allowed = AllowedKeys(dtoType);
        Dictionary<string, PropertyInfo> byJsonName = JsonProperties(dtoType);

        foreach (JProperty property in obj.Properties())
        {
            string childPointer = pointer == "/"
                ? "/" + property.Name
                : pointer + "/" + property.Name;

            if (!allowed.Contains(property.Name))
            {
                findings.Add(new ConfigFinding(
                    relativePath,
                    childPointer,
                    "skill-shape",
                    $"key '{property.Name}' is not valid on {Article(positionLabel)}."));
                continue;
            }

            if (!byJsonName.TryGetValue(property.Name, out PropertyInfo? info))
            {
                continue;
            }

            Type propertyType = Nullable.GetUnderlyingType(info.PropertyType) ?? info.PropertyType;
            if (IsOpenBag(propertyType))
            {
                continue;
            }

            if (property.Value is JObject childObject
                && TryClosedObjectType(propertyType, out Type childDto, out string childLabel))
            {
                WalkObject(
                    relativePath,
                    childPointer,
                    childObject,
                    childDto,
                    childLabel,
                    findings);
                continue;
            }

            if (property.Value is JArray array
                && TryClosedElementType(propertyType, out Type elementDto, out string elementLabel))
            {
                for (int i = 0; i < array.Count; i++)
                {
                    string elementPointer = childPointer + "/" + i;
                    if (array[i] is not JObject element)
                    {
                        findings.Add(new ConfigFinding(
                            relativePath,
                            elementPointer,
                            "skill-shape",
                            $"{elementLabel} entries must be objects."));
                        continue;
                    }

                    WalkObject(
                        relativePath,
                        elementPointer,
                        element,
                        elementDto,
                        elementLabel,
                        findings);
                }
            }
        }
    }

    static bool IsOpenBag(Type propertyType) =>
        typeof(JToken).IsAssignableFrom(propertyType)
        || (propertyType.IsArray
            && typeof(JToken).IsAssignableFrom(propertyType.GetElementType()!));

    static bool TryClosedObjectType(Type propertyType, out Type dtoType, out string label)
    {
        dtoType = propertyType;
        if (propertyType == typeof(SkillTreeJson))
        {
            label = "tree";
            return true;
        }

        if (propertyType == typeof(SkillTreeNodeLayoutJson))
        {
            label = "layout";
            return true;
        }

        if (propertyType == typeof(XpRuleWhenJson))
        {
            label = "xp-rule when";
            return true;
        }

        if (propertyType == typeof(AbilityWhenJson))
        {
            label = "effect when";
            return true;
        }

        label = "";
        return false;
    }

    static bool TryClosedElementType(Type propertyType, out Type elementType, out string label)
    {
        elementType = typeof(object);
        label = "";
        if (!propertyType.IsArray)
        {
            return false;
        }

        Type? element = propertyType.GetElementType();
        if (element == null || IsOpenBag(element))
        {
            return false;
        }

        elementType = element;
        if (element == typeof(AttributeScoreJson))
        {
            label = "attribute score";
            return true;
        }

        if (element == typeof(XpRuleJson))
        {
            label = "xp rule";
            return true;
        }

        if (element == typeof(AbilityEffectJson))
        {
            label = "effect";
            return true;
        }

        if (element == typeof(SkillTreeNodeJson))
        {
            label = "node";
            return true;
        }

        if (element == typeof(SkillTreeTierJson))
        {
            label = "tier";
            return true;
        }

        return false;
    }

    static string Article(string label)
    {
        if (label.Length == 0)
        {
            return label;
        }

        char c = char.ToLowerInvariant(label[0]);
        return c is 'a' or 'e' or 'i' or 'o' or 'u'
            ? "an " + label
            : "a " + label;
    }

    static HashSet<string> AllowedKeys(Type dtoType) =>
        new(JsonProperties(dtoType).Keys, StringComparer.Ordinal);

    static Dictionary<string, PropertyInfo> JsonProperties(Type dtoType)
    {
        Dictionary<string, PropertyInfo> map = new(StringComparer.Ordinal);
        foreach (PropertyInfo property in dtoType.GetProperties(
                     BindingFlags.Instance | BindingFlags.Public))
        {
            if (!property.CanRead || property.GetIndexParameters().Length > 0)
            {
                continue;
            }

            JsonPropertyAttribute? attr = property.GetCustomAttribute<JsonPropertyAttribute>();
            string name = attr?.PropertyName ?? property.Name;
            // C# escapes the reserved word with @params; Newtonsoft binds "params".
            if (name.StartsWith('@'))
            {
                name = name[1..];
            }

            map[name] = property;
        }

        return map;
    }
}
