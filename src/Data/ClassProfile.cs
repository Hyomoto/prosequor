using Newtonsoft.Json.Linq;

namespace Prosequor.Data;

/// <summary>One skill's starting nodes from a character-class <c>prosequor.unlocks</c> entry.</summary>
public sealed class ClassUnlockGroup
{
    public string Skill { get; init; } = "";
    public IReadOnlyList<string> Nodes { get; init; } = Array.Empty<string>();
}

/// <summary>
/// Compiled <c>prosequor</c> object from a character class.
/// Attribute scores are absolute; omitted catalog ids are <see cref="AttributeGrowth.DefaultScore"/>.
/// </summary>
public sealed class ClassProfile
{
    public string Code { get; init; } = "";

    public IReadOnlyDictionary<string, int> Attributes { get; init; } =
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<string> Skills { get; init; } = Array.Empty<string>();
    public IReadOnlyList<ClassTraitEdit> TraitEdits { get; init; } = Array.Empty<ClassTraitEdit>();
    public IReadOnlyList<ClassUnlockGroup> Unlocks { get; init; } = Array.Empty<ClassUnlockGroup>();

    /// <summary>
    /// Fill every catalog id with 10, overlay authored scores, and keep skill ids and unlock groups.
    /// Unknown attribute ids and, when <paramref name="skills"/> is set, unknown skill ids are skipped.
    /// </summary>
    public static ClassProfile? Compile(
        string? classCode,
        ClassProfileJson? row,
        IReadOnlyList<string> catalog,
        ISkillRegistry? skills,
        Action<string>? warn)
    {
        if (string.IsNullOrWhiteSpace(classCode) || row == null)
        {
            return null;
        }

        string code = classCode.Trim();
        IReadOnlyList<string> ids = catalog ?? AttributeIds.All;
        Dictionary<string, int> attributes = new(StringComparer.OrdinalIgnoreCase);
        foreach (string id in ids)
        {
            attributes[id] = AttributeGrowth.DefaultScore;
        }

        if (row.attributes != null)
        {
            foreach (KeyValuePair<string, int> pair in row.attributes)
            {
                string? attrId = AttributeIds.Canonicalize(pair.Key, ids);
                if (attrId == null)
                {
                    warn?.Invoke(
                        $"[prosequor] Class '{code}': unknown attribute '{pair.Key}'.");
                    continue;
                }

                attributes[attrId] = pair.Value;
            }
        }

        List<string> skillIds = new();
        HashSet<string> seenSkills = new(StringComparer.OrdinalIgnoreCase);
        if (row.skills != null)
        {
            foreach (string? rawSkill in row.skills)
            {
                if (string.IsNullOrWhiteSpace(rawSkill))
                {
                    warn?.Invoke($"[prosequor] Class '{code}': skipping blank skill id.");
                    continue;
                }

                string skillId = rawSkill.Trim();
                if (skills != null)
                {
                    if (!skills.TryGet(skillId, out SkillDef? found) || found == null)
                    {
                        warn?.Invoke(
                            $"[prosequor] Class '{code}': unknown skill '{skillId}'.");
                        continue;
                    }

                    skillId = found.Id;
                }

                if (!seenSkills.Add(skillId))
                {
                    continue;
                }

                skillIds.Add(skillId);
            }
        }

        List<ClassTraitEdit> traitEdits = new();
        if (row.traits != null)
        {
            foreach (string? rawTrait in row.traits)
            {
                if (string.IsNullOrWhiteSpace(rawTrait))
                {
                    warn?.Invoke($"[prosequor] Class '{code}': skipping blank trait edit.");
                    continue;
                }

                string token = rawTrait.Trim();
                bool remove = token[0] == '-';
                string traitCode = remove ? token[1..].Trim() : token;
                if (traitCode.Length == 0)
                {
                    warn?.Invoke($"[prosequor] Class '{code}': skipping trait edit '{token}'.");
                    continue;
                }

                traitEdits.Add(new ClassTraitEdit
                {
                    Code = traitCode,
                    Remove = remove
                });
            }
        }

        List<ClassUnlockGroup> unlocks = new();
        if (row.unlocks != null)
        {
            foreach (ClassUnlockJson? group in row.unlocks)
            {
                if (group == null || string.IsNullOrWhiteSpace(group.skill))
                {
                    warn?.Invoke($"[prosequor] Class '{code}': skipping unlock group with no skill.");
                    continue;
                }

                string skillId = group.skill.Trim();
                List<string> nodes = new();
                if (group.nodes != null)
                {
                    foreach (string? rawNode in group.nodes)
                    {
                        if (string.IsNullOrWhiteSpace(rawNode))
                        {
                            warn?.Invoke(
                                $"[prosequor] Class '{code}' skill '{skillId}': skipping blank unlock node.");
                            continue;
                        }

                        nodes.Add(rawNode.Trim());
                    }
                }

                if (nodes.Count == 0)
                {
                    warn?.Invoke(
                        $"[prosequor] Class '{code}' skill '{skillId}': skipping unlock group with no nodes.");
                    continue;
                }

                unlocks.Add(new ClassUnlockGroup
                {
                    Skill = skillId,
                    Nodes = nodes
                });
            }
        }

        return new ClassProfile
        {
            Code = code,
            Attributes = attributes,
            Skills = skillIds.Count == 0 ? Array.Empty<string>() : skillIds,
            TraitEdits = traitEdits.Count == 0 ? Array.Empty<ClassTraitEdit>() : traitEdits,
            Unlocks = unlocks.Count == 0 ? Array.Empty<ClassUnlockGroup>() : unlocks
        };
    }

    /// <summary>
    /// Copy <paramref name="classTraits"/> and apply <see cref="TraitEdits"/> in order.
    /// A leading <c>-</c> removes a matching code. Other entries add a code that is not already present.
    /// When <paramref name="resolveAdd"/> is set, an add it cannot resolve is skipped.
    /// </summary>
    public string[] ApplyTraitEdits(
        IReadOnlyList<string>? classTraits,
        Func<string, string?>? resolveAdd,
        Action<string>? warn)
    {
        if (TraitEdits.Count == 0)
        {
            if (classTraits is string[] same)
            {
                return same;
            }

            return classTraits == null ? Array.Empty<string>() : classTraits.ToArray();
        }

        List<string> list = classTraits == null ? new List<string>() : new List<string>(classTraits);
        foreach (ClassTraitEdit edit in TraitEdits)
        {
            if (edit.Remove)
            {
                list.RemoveAll(trait =>
                    string.Equals(trait?.Trim(), edit.Code, StringComparison.OrdinalIgnoreCase));
                continue;
            }

            string code = edit.Code;
            if (resolveAdd != null)
            {
                string? resolved = resolveAdd(code);
                if (string.IsNullOrWhiteSpace(resolved))
                {
                    warn?.Invoke($"[prosequor] Class '{Code}': unknown trait '{code}'.");
                    continue;
                }

                code = resolved.Trim();
            }

            if (list.Any(trait => string.Equals(trait?.Trim(), code, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            list.Add(code);
        }

        return list.ToArray();
    }

    /// <summary>Read a <c>prosequor</c> object off a character-class token. Null when the key is absent.</summary>
    public static ClassProfileJson? ReadJson(JToken? prosequor, out string? error)
    {
        error = null;
        if (prosequor == null || prosequor.Type == JTokenType.Null)
        {
            return null;
        }

        if (prosequor is not JObject)
        {
            error = "prosequor must be an object.";
            return null;
        }

        try
        {
            return prosequor.ToObject<ClassProfileJson>();
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return null;
        }
    }
}

/// <summary>JSON shape of the <c>prosequor</c> object on a character class. Unknown keys are ignored.</summary>
public sealed class ClassProfileJson
{
    public Dictionary<string, int>? attributes { get; set; }
    public string[]? skills { get; set; }
    public string[]? traits { get; set; }
    public ClassUnlockJson[]? unlocks { get; set; }
}

/// <summary>One <c>traits</c> entry. <see cref="Remove"/> is a leading <c>-</c> on the authored code.</summary>
public sealed class ClassTraitEdit
{
    public string Code { get; init; } = "";
    public bool Remove { get; init; }
}

/// <summary>One <c>unlocks</c> entry. Extra keys (for example a future level) are ignored.</summary>
public sealed class ClassUnlockJson
{
    public string? skill { get; set; }
    public string[]? nodes { get; set; }
}
