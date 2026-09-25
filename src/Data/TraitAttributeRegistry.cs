using Prosequor.Progress;
using Vintagestory.API.Common;

namespace Prosequor.Data;

public interface ITraitAttributeRegistry
{
    IReadOnlyDictionary<string, TraitAttributeMapping> ByCode { get; }
    IReadOnlyDictionary<string, Dictionary<string, int>> ClassStartingScores { get; }

    /// <summary>Shared base: every registered non-optional skill. Empty until skills are bound.</summary>
    IReadOnlySet<string> BaseSkillSet { get; }

    /// <summary>Class code → skill ids that class can learn (base plus trait grants).</summary>
    IReadOnlyDictionary<string, IReadOnlySet<string>> ClassSkillSets { get; }

    bool TryGet(string code, out TraitAttributeMapping mapping);
    void SetClassStartingScores(string classCode, Dictionary<string, int> scores);
    void ClearClassStartingScores();

    /// <summary>Remember original trait codes for a class (pre-strip).</summary>
    void SetClassOriginalTraits(string classCode, IReadOnlyList<string> traits);

    /// <summary>Clear class skill sets and original-trait cache.</summary>
    void ClearClassSkillSets();

    /// <summary>Clear remembered class trait codes (pre-strip).</summary>
    void ClearClassOriginalTraits();

    /// <summary>
    /// Build the shared base and per-class skill sets from original traits + the skill registry.
    /// No-op when the skill registry is empty. Idempotent.
    /// </summary>
    void RebuildClassSkillSets(ISkillRegistry skills, ICoreAPI? api = null);

    /// <summary>
    /// Class set for <paramref name="classCode"/>, or the base set when the class is unknown.
    /// </summary>
    IReadOnlySet<string> SkillSetForClass(string? classCode);

    /// <summary>
    /// Class set plus registered skill ids granted by <paramref name="extraTraits"/>.
    /// Returns the class set reference when extras add nothing new.
    /// </summary>
    IReadOnlySet<string> SkillSetForClass(
        string? classCode,
        IEnumerable<string>? extraTraits,
        ISkillRegistry skills,
        ICoreAPI? api = null);
}

/// <summary>
/// Loads trait→attribute lookups from <c>config/prosequor/trait-attributes</c> (array JSON; last-win by code).
/// </summary>
public sealed class TraitAttributeRegistry : ITraitAttributeRegistry
{
    readonly Dictionary<string, TraitAttributeMapping> byCode = new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<string, Dictionary<string, int>> classStartingScores =
        new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<string, string[]> classOriginalTraits =
        new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<string, IReadOnlySet<string>> classSkillSets =
        new(StringComparer.OrdinalIgnoreCase);

    IReadOnlySet<string> baseSkillSet = SkillAccess.EmptySet;

    public IReadOnlyDictionary<string, TraitAttributeMapping> ByCode => byCode;

    public IReadOnlyDictionary<string, Dictionary<string, int>> ClassStartingScores =>
        classStartingScores;

    public IReadOnlySet<string> BaseSkillSet => baseSkillSet;

    public IReadOnlyDictionary<string, IReadOnlySet<string>> ClassSkillSets => classSkillSets;

    public bool TryGet(string code, out TraitAttributeMapping mapping)
    {
        if (!string.IsNullOrWhiteSpace(code) && byCode.TryGetValue(code.Trim(), out TraitAttributeMapping? found) && found != null)
        {
            mapping = found;
            return true;
        }

        mapping = null!;
        return false;
    }

    public void SetClassStartingScores(string classCode, Dictionary<string, int> scores)
    {
        if (string.IsNullOrWhiteSpace(classCode) || scores == null)
        {
            return;
        }

        classStartingScores[classCode.Trim()] = scores;
    }

    public void ClearClassStartingScores() => classStartingScores.Clear();

    public void SetClassOriginalTraits(string classCode, IReadOnlyList<string> traits)
    {
        if (string.IsNullOrWhiteSpace(classCode))
        {
            return;
        }

        string[] copy = traits == null || traits.Count == 0
            ? Array.Empty<string>()
            : traits.Where(t => !string.IsNullOrWhiteSpace(t)).Select(t => t.Trim()).ToArray();
        classOriginalTraits[classCode.Trim()] = copy;
    }

    public void ClearClassSkillSets()
    {
        classSkillSets.Clear();
        baseSkillSet = SkillAccess.EmptySet;
    }

    public void ClearClassOriginalTraits() => classOriginalTraits.Clear();

    public IReadOnlySet<string> SkillSetForClass(string? classCode)
    {
        if (!string.IsNullOrWhiteSpace(classCode)
            && classSkillSets.TryGetValue(classCode.Trim(), out IReadOnlySet<string>? set)
            && set != null)
        {
            return set;
        }

        return baseSkillSet;
    }

    public IReadOnlySet<string> SkillSetForClass(
        string? classCode,
        IEnumerable<string>? extraTraits,
        ISkillRegistry skills,
        ICoreAPI? api = null)
    {
        IReadOnlySet<string> classSet = SkillSetForClass(classCode);
        if (extraTraits == null || skills == null)
        {
            return classSet;
        }

        HashSet<string>? extras = null;
        foreach (string raw in extraTraits)
        {
            if (string.IsNullOrWhiteSpace(raw) || !TryGet(raw.Trim(), out TraitAttributeMapping mapping))
            {
                continue;
            }

            foreach (string skillId in mapping.Skills)
            {
                if (classSet.Contains(skillId))
                {
                    continue;
                }

                if (!skills.TryGet(skillId, out _))
                {
                    api?.Logger.Warning(
                        "[prosequor] Skipping trait-attribute skill '{0}' on trait '{1}': unknown skill.",
                        skillId,
                        mapping.Code);
                    continue;
                }

                extras ??= new HashSet<string>(classSet, StringComparer.OrdinalIgnoreCase);
                extras.Add(skillId);
            }
        }

        return extras == null ? classSet : extras;
    }

    public void RebuildClassSkillSets(ISkillRegistry skills, ICoreAPI? api = null)
    {
        if (skills == null || skills.All.Count == 0)
        {
            return;
        }

        HashSet<string> baseSet = new(StringComparer.OrdinalIgnoreCase);
        foreach (SkillDef skill in skills.All)
        {
            if (skill == null || string.IsNullOrWhiteSpace(skill.Id) || skill.IsOptional)
            {
                continue;
            }

            baseSet.Add(skill.Id);
        }

        baseSkillSet = baseSet;
        classSkillSets.Clear();

        foreach (KeyValuePair<string, string[]> kv in classOriginalTraits)
        {
            classSkillSets[kv.Key] = BuildClassSet(baseSet, kv.Value, skills, api);
        }
    }

    IReadOnlySet<string> BuildClassSet(
        IReadOnlySet<string> baseSet,
        IReadOnlyList<string> traitCodes,
        ISkillRegistry skills,
        ICoreAPI? api)
    {
        HashSet<string>? extras = null;
        foreach (string raw in traitCodes)
        {
            if (string.IsNullOrWhiteSpace(raw) || !TryGet(raw.Trim(), out TraitAttributeMapping mapping))
            {
                continue;
            }

            foreach (string skillId in mapping.Skills)
            {
                if (baseSet.Contains(skillId))
                {
                    continue;
                }

                if (!skills.TryGet(skillId, out _))
                {
                    api?.Logger.Warning(
                        "[prosequor] Skipping trait-attribute skill '{0}' on trait '{1}': unknown skill.",
                        skillId,
                        mapping.Code);
                    continue;
                }

                extras ??= new HashSet<string>(baseSet, StringComparer.OrdinalIgnoreCase);
                extras.Add(skillId);
            }
        }

        return extras == null ? baseSet : extras;
    }

    public void Register(TraitAttributeMapping mapping)
    {
        if (mapping == null || string.IsNullOrWhiteSpace(mapping.Code))
        {
            return;
        }

        byCode[mapping.Code.Trim()] = mapping;
    }

    public void LoadFromAssets(ICoreAPI api, IAttributeStatRegistry? stats = null)
    {
        byCode.Clear();
        IReadOnlyList<string> catalog = stats != null
            ? AttributeIds.CatalogIds(stats)
            : AttributeIds.All;

        List<KeyValuePair<AssetLocation, TraitAttributeMappingJson[]>> assets = api.Assets
            .GetMany<TraitAttributeMappingJson[]>(api.Logger, "config/prosequor/trait-attributes", null)
            .OrderBy(kv => kv.Key.Domain, StringComparer.OrdinalIgnoreCase)
            .ThenBy(kv => kv.Key.Path, StringComparer.OrdinalIgnoreCase)
            .ToList();

        int loaded = 0;
        foreach (KeyValuePair<AssetLocation, TraitAttributeMappingJson[]> kv in assets)
        {
            TraitAttributeMappingJson[]? list = kv.Value;
            if (list == null || list.Length == 0)
            {
                continue;
            }

            foreach (TraitAttributeMappingJson row in list)
            {
                if (row == null || string.IsNullOrWhiteSpace(row.code))
                {
                    api.Logger.Warning(
                        "[prosequor] Skipping trait-attribute row in {0}: missing code.",
                        kv.Key);
                    continue;
                }

                string code = row.code.Trim();
                Dictionary<string, int> deltas = new(StringComparer.OrdinalIgnoreCase);
                if (row.attributes != null)
                {
                    foreach (KeyValuePair<string, int> pair in row.attributes)
                    {
                        string? attrId = AttributeIds.Canonicalize(pair.Key, catalog);
                        if (attrId == null)
                        {
                            api.Logger.Warning(
                                "[prosequor] Trait '{0}' in {1}: unknown attribute '{2}'.",
                                code,
                                kv.Key,
                                pair.Key);
                            continue;
                        }

                        deltas[attrId] = pair.Value;
                    }
                }

                List<string> skillIds = new();
                if (row.skills != null)
                {
                    foreach (string? rawSkill in row.skills)
                    {
                        if (string.IsNullOrWhiteSpace(rawSkill))
                        {
                            api.Logger.Warning(
                                "[prosequor] Skipping blank skill id on trait '{0}' in {1}.",
                                code,
                                kv.Key);
                            continue;
                        }

                        skillIds.Add(rawSkill.Trim());
                    }
                }

                if (byCode.ContainsKey(code))
                {
                    api.Logger.Warning(
                        "[prosequor] Trait-attribute '{0}' redefined by {1}; last-win.",
                        code,
                        kv.Key);
                }

                byCode[code] = new TraitAttributeMapping
                {
                    Code = code,
                    Attributes = deltas,
                    RetainTrait = row.retainTrait,
                    Skills = skillIds.Count == 0 ? Array.Empty<string>() : skillIds.ToArray()
                };
                loaded++;
            }
        }

        api.Logger.Notification(
            "[prosequor] Loaded {0} trait-attribute mapping(s) from {1} file(s).",
            byCode.Count,
            assets.Count);
    }
}
