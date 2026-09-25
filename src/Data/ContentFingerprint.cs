using System.Collections;
using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Prosequor;
using Prosequor.Ability;
using Prosequor.Ability.Hooks;
using Prosequor.Xp;

namespace Prosequor.Data;

/// <summary>
/// Join-time diagnostic of compiled authored content (skills, stats, collections,
/// pools, affixes, traits, level-ups). World-expanded collection membership is excluded so
/// client and dedicated server can match before GameReady.
/// </summary>
public sealed class ContentFingerprint
{
    public const int Schema = 1;

    public int Version { get; }
    public string Hash { get; }

    public ContentFingerprint(int version, string hash)
    {
        Version = version;
        Hash = hash ?? "";
    }

    public static ContentFingerprint Compute(
        ISkillRegistry skills,
        IAttributeStatRegistry? stats = null,
        CollectionIndex? collections = null,
        OutputPoolRegistry? pools = null,
        AffixListRegistry? affixes = null,
        ITraitAttributeRegistry? traits = null,
        ILevelUpRegistry? levelUps = null)
    {
        string canonical = BuildCanonical(skills, stats, collections, pools, affixes, traits, levelUps);
        return new ContentFingerprint(Schema, HashText(canonical));
    }

    public static ContentFingerprintReport Compare(
        ContentFingerprint local,
        int remoteSchema,
        string? remoteHash)
    {
        string remote = remoteHash ?? "";
        bool match = local.Version == remoteSchema
            && string.Equals(local.Hash, remote, StringComparison.Ordinal);
        return new ContentFingerprintReport(match, local.Version, local.Hash, remoteSchema, remote);
    }

    public static string Abbreviate(string hash, int chars = 16)
    {
        if (string.IsNullOrEmpty(hash))
        {
            return "-";
        }

        return hash.Length <= chars ? hash : hash[..chars];
    }

    internal static string BuildCanonical(
        ISkillRegistry skills,
        IAttributeStatRegistry? stats,
        CollectionIndex? collections,
        OutputPoolRegistry? pools,
        AffixListRegistry? affixes,
        ITraitAttributeRegistry? traits,
        ILevelUpRegistry? levelUps = null)
    {
        StringBuilder sb = new(4096);
        sb.Append("v").Append(Schema).Append('\n');
        AppendSkills(sb, skills);
        AppendStats(sb, stats);
        AppendCollections(sb, collections);
        AppendPools(sb, pools);
        AppendAffixes(sb, affixes);
        AppendTraits(sb, traits);
        AppendLevelUps(sb, levelUps);
        return sb.ToString();
    }

    internal static string HashText(string canonical)
    {
        byte[] digest = SHA256.HashData(Encoding.UTF8.GetBytes(canonical));
        return Convert.ToHexString(digest).ToLowerInvariant();
    }

    static void AppendSkills(StringBuilder sb, ISkillRegistry skills)
    {
        sb.Append("skills\n");
        foreach (SkillDef skill in skills.All.OrderBy(s => s.Id, StringComparer.OrdinalIgnoreCase))
        {
            sb.Append(skill.Id)
                .Append(" kind=").Append(skill.Kind)
                .Append(" max=").Append(skill.MaxLevel)
                .Append(" optional=").Append(skill.IsOptional ? '1' : '0')
                .Append(" name=").Append(skill.NameLang)
                .Append(" desc=").Append(skill.DescriptionLang)
                .Append(" icon=").Append(skill.Icon)
                .Append(" params=").Append(CanonicalJson(skill.DescriptionParamSpecs))
                .Append('\n');

            foreach (AttributeScoreEntry score in skill.AttributeScores
                         .OrderBy(s => s.Id, StringComparer.OrdinalIgnoreCase))
            {
                sb.Append(" attr ").Append(score.Id).Append('=').Append(F(score.Value)).Append('\n');
            }

            foreach (XpRule rule in skill.XpRules.OrderBy(r => r.Id, StringComparer.OrdinalIgnoreCase))
            {
                sb.Append(" xp ").Append(rule.Id)
                    .Append(" act=").Append(rule.Activity)
                    .Append(" amt=").Append(F(rule.Amount))
                    .Append(" rate=").Append(F(rule.Rate))
                    .Append(" pay=").Append(XpPayChannels.Canonical(rule.Pay))
                    .Append(" payee=").Append(XpPayees.Canonical(rule.Payee))
                    .Append(" include=").Append((rule.Include ?? XpQuantityExclude.Empty).Canonical())
                    .Append(" exclude=").Append((rule.Exclude ?? XpQuantityExclude.Empty).Canonical())
                    .Append(" pri=").Append(rule.Priority)
                    .Append(" ord=").Append(rule.SourceOrder)
                    .Append(" score=").Append(rule.MatchScore)
                    .Append(" table=").Append(rule.AmountTable == null
                        ? "-"
                        : string.Join(',', rule.AmountTable.Select(F)))
                    .Append(" when=").Append(CanonicalWhen(rule.Criteria))
                    .Append('\n');
            }

            if (skill.Tree == null)
            {
                sb.Append(" tree=-\n");
            }
            else
            {
                sb.Append(" tree cols=").Append(skill.Tree.ColumnSpan)
                    .Append(" layers=").Append(skill.Tree.MaxLayer)
                    .Append('\n');
                foreach (SkillTreeNodeDef node in skill.Tree.Nodes
                             .OrderBy(n => n.Id, StringComparer.OrdinalIgnoreCase))
                {
                    AppendNode(sb, node);
                }
            }

            foreach (AbilityRule rule in skill.Rules.OrderBy(r => r.SourceOrder).ThenBy(r => r.RuleId, StringComparer.Ordinal))
            {
                AppendRule(sb, rule);
            }
        }
    }

    static void AppendNode(StringBuilder sb, SkillTreeNodeDef node)
    {
        sb.Append("  node ").Append(node.Id)
            .Append(" spec=").Append(node.IsSpecialization ? '1' : '0')
            .Append(" compact=").Append(node.Compact ? '1' : '0')
            .Append(" layer=").Append(node.Layer)
            .Append(" order=").Append(node.Order)
            .Append(" col=").Append(node.GridColumn)
            .Append(" off=").Append(node.LayerOffset)
            .Append(" bias=").Append(node.ColumnBias?.ToString(CultureInfo.InvariantCulture) ?? "-")
            .Append(" name=").Append(node.NameLang)
            .Append(" icon=").Append(node.Icon)
            .Append(" req=").Append(CanonicalRequires(node.RequireGroups))
            .Append(" excl=").Append(string.Join(',', node.Excludes.OrderBy(id => id, StringComparer.OrdinalIgnoreCase)))
            .Append('\n');

        for (int i = 0; i < node.Tiers.Count; i++)
        {
            SkillTreeTierDef tier = node.Tiers[i];
            sb.Append("   tier ").Append(i + 1)
                .Append(" cost=").Append(tier.Cost)
                .Append(" min=").Append(tier.MinSkillLevel)
                .Append(" desc=").Append(tier.DescriptionLang)
                .Append(" args=").Append(CanonicalJson(tier.DescriptionArgs))
                .Append(" totals=").Append(CanonicalJson(tier.TotalParams))
                .Append('\n');
        }
    }

    static void AppendStats(StringBuilder sb, IAttributeStatRegistry? stats)
    {
        sb.Append("stats\n");
        if (stats == null)
        {
            return;
        }

        foreach (AttributeStatDef stat in stats.All.OrderBy(s => s.Id, StringComparer.OrdinalIgnoreCase))
        {
            sb.Append(stat.Id).Append('\n');
            foreach (AbilityRule rule in stat.Rules.OrderBy(r => r.SourceOrder).ThenBy(r => r.RuleId, StringComparer.Ordinal))
            {
                AppendRule(sb, rule);
            }
        }
    }

    static void AppendCollections(StringBuilder sb, CollectionIndex? collections)
    {
        sb.Append("collections\n");
        if (collections == null)
        {
            return;
        }

        foreach (string id in collections.Ids.OrderBy(s => s, StringComparer.OrdinalIgnoreCase))
        {
            IReadOnlyList<string> includes = collections.IncludePatterns(id);
            IReadOnlyList<string> unions = collections.UnionMembers(id);
            IReadOnlyList<string> excludes = collections.ExcludeIds(id);
            sb.Append(id)
                .Append(" inc=").Append(string.Join(',', includes.OrderBy(s => s, StringComparer.OrdinalIgnoreCase)))
                .Append(" un=").Append(string.Join(',', unions.OrderBy(s => s, StringComparer.OrdinalIgnoreCase)))
                .Append(" ex=").Append(string.Join(',', excludes.OrderBy(s => s, StringComparer.OrdinalIgnoreCase)))
                .Append('\n');
        }
    }

    static void AppendPools(StringBuilder sb, OutputPoolRegistry? pools)
    {
        sb.Append("pools\n");
        if (pools == null)
        {
            return;
        }

        foreach (OutputPoolDef pool in pools.All.OrderBy(p => p.Id, StringComparer.OrdinalIgnoreCase))
        {
            sb.Append(pool.Id).Append('\n');
            foreach (OutputPoolEntryDef entry in pool.Entries.OrderBy(e => e.Code, StringComparer.OrdinalIgnoreCase))
            {
                sb.Append(" ").Append(entry.Code).Append('=').Append(F(entry.Weight)).Append('\n');
            }
        }
    }

    static void AppendAffixes(StringBuilder sb, AffixListRegistry? affixes)
    {
        sb.Append("affixes\n");
        if (affixes == null)
        {
            return;
        }

        foreach (AffixListDef list in affixes.All.OrderBy(a => a.Id, StringComparer.OrdinalIgnoreCase))
        {
            sb.Append(list.Id).Append('\n');
            for (int i = 0; i < list.Entries.Count; i++)
            {
                AffixListEntryDef entry = list.Entries[i];
                sb.Append(' ').Append(i)
                    .Append(' ').Append(entry.Code)
                    .Append(' ').Append(entry.Lang)
                    .Append(' ').Append(entry.Color ?? "-")
                    .Append('\n');
            }
        }
    }

    static void AppendTraits(StringBuilder sb, ITraitAttributeRegistry? traits)
    {
        sb.Append("traits\n");
        if (traits == null)
        {
            return;
        }

        foreach (KeyValuePair<string, TraitAttributeMapping> kv in traits.ByCode
                     .OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase))
        {
            TraitAttributeMapping mapping = kv.Value;
            sb.Append(mapping.Code)
                .Append(" retain=").Append(mapping.RetainTrait ? '1' : '0')
                .Append(' ');
            bool first = true;
            foreach (KeyValuePair<string, int> attr in mapping.Attributes
                         .OrderBy(a => a.Key, StringComparer.OrdinalIgnoreCase))
            {
                if (!first)
                {
                    sb.Append(',');
                }

                sb.Append(attr.Key).Append('=').Append(attr.Value);
                first = false;
            }

            if (mapping.Skills.Count > 0)
            {
                sb.Append(" skills=").Append(string.Join(
                    ',',
                    mapping.Skills.OrderBy(id => id, StringComparer.OrdinalIgnoreCase)));
            }

            sb.Append('\n');
        }
    }

    static void AppendLevelUps(StringBuilder sb, ILevelUpRegistry? levelUps)
    {
        sb.Append("level-ups\n");
        if (levelUps == null)
        {
            return;
        }

        foreach (LevelUpRuleDef rule in levelUps.Rules
                     .OrderBy(r => r.Priority)
                     .ThenBy(r => r.SourceOrder)
                     .ThenBy(r => r.Id, StringComparer.OrdinalIgnoreCase))
        {
            sb.Append(rule.Id)
                .Append(" action=").Append(rule.Action)
                .Append(" every=").Append(rule.Every?.ToString(CultureInfo.InvariantCulture) ?? "-")
                .Append(" levels=");
            if (rule.Levels == null || rule.Levels.Count == 0)
            {
                sb.Append('-');
            }
            else
            {
                sb.Append(string.Join(',', rule.Levels.OrderBy(l => l)));
            }

            sb.Append(" value=").Append(rule.Value)
                .Append(" key=").Append(rule.AttributeKey ?? "-")
                .Append(" pri=").Append(rule.Priority)
                .Append(" ord=").Append(rule.SourceOrder)
                .Append('\n');
        }
    }

    static void AppendRule(StringBuilder sb, AbilityRule rule)
    {
        AbilityRuleSource src = rule.Source;
        sb.Append(" rule ").Append(rule.RuleId)
            .Append(" hook=").Append(rule.Hook.Value)
            .Append(" verb=").Append(rule.Verb.Value)
            .Append(" phase=").Append(rule.Phase.Value)
            .Append(" action=").Append(rule.Action.Value)
            .Append(" pri=").Append(rule.Priority)
            .Append(" ord=").Append(rule.SourceOrder)
            .Append(" src=").Append(src.SkillId)
            .Append('/').Append(src.NodeId ?? "-")
            .Append('@').Append(src.Tier?.ToString(CultureInfo.InvariantCulture) ?? "-")
            .Append(" attr=").Append(src.AttributeId ?? "-")
            .Append('[').Append(src.MinAttributeScore)
            .Append("..").Append(src.MaxAttributeScore?.ToString(CultureInfo.InvariantCulture) ?? "-")
            .Append(']')
            .Append(" when=").Append(CanonicalWhen(rule.When.Criteria))
            .Append(" params=").Append(CanonicalJson(rule.Parameters))
            .Append('\n');
    }

    static string CanonicalRequires(IReadOnlyList<RequireGroup> groups)
    {
        if (groups.Count == 0)
        {
            return "";
        }

        List<string> encoded = new(groups.Count);
        foreach (RequireGroup group in groups)
        {
            encoded.Add(string.Join('|', group.Alternatives.OrderBy(id => id, StringComparer.OrdinalIgnoreCase)));
        }

        encoded.Sort(StringComparer.OrdinalIgnoreCase);
        return string.Join('&', encoded);
    }

    static string CanonicalWhen(IReadOnlyList<TagCriterion> criteria)
    {
        if (criteria.Count == 0)
        {
            return "";
        }

        List<string> parts = new(criteria.Count);
        foreach (TagCriterion criterion in criteria)
        {
            parts.Add(criterion switch
            {
                TokenCriterion token => "token:" + token.Token,
                RoleIdentityCriterion role => "role:" + role.Role.ToTag() + "=" + role.Identity,
                RoleCollectionCriterion col =>
                    "role:" + col.Role.ToTag() + "<" + string.Join(',',
                        col.CollectionIds.OrderBy(id => id, StringComparer.OrdinalIgnoreCase)) + ">",
                DamageIdentityCriterion dmg => "damage:" + dmg.Kind,
                _ => criterion.GetType().Name
            });
        }

        parts.Sort(StringComparer.Ordinal);
        return string.Join(',', parts);
    }

    static string CanonicalJson(object? value) =>
        SortToken(ToCanonicalToken(value, 0)).ToString(Formatting.None);

    static JToken ToCanonicalToken(object? value, int depth)
    {
        if (value == null || depth > 10)
        {
            return JValue.CreateNull();
        }

        if (value is JToken token)
        {
            return token;
        }

        if (value is string text)
        {
            return text;
        }

        if (value is bool or byte or sbyte or short or ushort or int or uint or long or ulong
            or float or double or decimal)
        {
            return new JValue(value);
        }

        if (value is Enum)
        {
            return value.ToString();
        }

        switch (value)
        {
            case HookId hook:
                return hook.Value;
            case VerbId verb:
                return verb.Value;
            case PhaseId phase:
                return phase.Value;
            case ActionId action:
                return action.Value;
            case NestedActionRef nested:
                return new JObject
                {
                    ["action"] = nested.Action.Value,
                    ["params"] = ToCanonicalToken(nested.Parameters, depth + 1)
                };
        }

        Type type = value.GetType();
        if (typeof(IAbilityActionHandler).IsAssignableFrom(type)
            || typeof(Delegate).IsAssignableFrom(type)
            || typeof(IPlayerProgress).IsAssignableFrom(type)
            || typeof(CollectionIndex).IsAssignableFrom(type))
        {
            return JValue.CreateNull();
        }

        if (value is IDictionary dictionary)
        {
            JObject map = new();
            List<DictionaryEntry> entries = new();
            foreach (DictionaryEntry entry in dictionary)
            {
                entries.Add(entry);
            }

            foreach (DictionaryEntry entry in entries.OrderBy(e => e.Key?.ToString() ?? "", StringComparer.Ordinal))
            {
                map[entry.Key?.ToString() ?? ""] = ToCanonicalToken(entry.Value, depth + 1);
            }

            return map;
        }

        if (value is IEnumerable enumerable)
        {
            JArray array = new();
            foreach (object? item in enumerable)
            {
                array.Add(ToCanonicalToken(item, depth + 1));
            }

            return array;
        }

        JObject obj = new() { ["$t"] = type.Name };
        foreach (FieldInfo field in type
                     .GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                     .OrderBy(f => f.Name, StringComparer.Ordinal))
        {
            if (ShouldSkipField(field.FieldType))
            {
                continue;
            }

            obj[field.Name] = ToCanonicalToken(field.GetValue(value), depth + 1);
        }

        return obj;
    }

    static bool ShouldSkipField(Type type) =>
        typeof(IAbilityActionHandler).IsAssignableFrom(type)
        || typeof(Delegate).IsAssignableFrom(type)
        || typeof(IPlayerProgress).IsAssignableFrom(type)
        || typeof(CollectionIndex).IsAssignableFrom(type);

    static JToken SortToken(JToken token)
    {
        if (token is JObject obj)
        {
            JObject sorted = new();
            foreach (JProperty property in obj.Properties().OrderBy(p => p.Name, StringComparer.Ordinal))
            {
                sorted.Add(property.Name, SortToken(property.Value));
            }

            return sorted;
        }

        if (token is JArray arr)
        {
            JArray copy = new();
            foreach (JToken child in arr)
            {
                copy.Add(SortToken(child));
            }

            return copy;
        }

        return token;
    }

    static string F(float value) => value.ToString("G9", CultureInfo.InvariantCulture);
}

public readonly record struct ContentFingerprintReport(
    bool Match,
    int LocalSchema,
    string LocalHash,
    int RemoteSchema,
    string RemoteHash);
