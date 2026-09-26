using Prosequor.Ability;
using Prosequor.Ability.Hooks;
using Vintagestory.API.Common;
using Vintagestory.API.Config;

namespace Prosequor.Data;

public interface IAttributeStatRegistry
{
    IReadOnlyList<AttributeStatDef> All { get; }
    AttributeEffectIndex EffectIndex { get; }
    bool TryGet(string id, out AttributeStatDef def);

    /// <summary>Canonical casing for a loaded attribute id, or null when unknown.</summary>
    string? Canonicalize(string id);
}

/// <summary>
/// Loads attribute-stat definitions from <c>config/prosequor/stats/*.json</c>.
/// A loaded file's <c>id</c> is the attribute; last-win by id across mods.
/// </summary>
public sealed class AttributeStatRegistry : IAttributeStatRegistry
{
    readonly List<AttributeStatDef> ordered = new();
    readonly Dictionary<string, AttributeStatDef> byId = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<AttributeStatDef> All => ordered;

    public AttributeEffectIndex EffectIndex { get; private set; } = AttributeEffectIndex.Empty;

    public bool TryGet(string id, out AttributeStatDef def)
    {
        if (byId.TryGetValue(id, out AttributeStatDef? found) && found != null)
        {
            def = found;
            return true;
        }

        def = null!;
        return false;
    }

    public string? Canonicalize(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return null;
        }

        if (byId.TryGetValue(id.Trim(), out AttributeStatDef? found) && found != null)
        {
            return found.Id;
        }

        return null;
    }

    public void Register(AttributeStatDef def)
    {
        if (string.IsNullOrWhiteSpace(def.Id))
        {
            return;
        }

        def.Id = def.Id.Trim();
        if (string.IsNullOrWhiteSpace(def.NameLang))
        {
            def.NameLang = DefaultNameLang(def.Id);
        }

        if (string.IsNullOrWhiteSpace(def.DescriptionLang))
        {
            def.DescriptionLang = DefaultDescriptionLang(def.Id);
        }

        if (string.IsNullOrWhiteSpace(def.Icon))
        {
            def.Icon = DefaultIcon(def.Id);
        }

        if (byId.TryGetValue(def.Id, out AttributeStatDef? existing))
        {
            ordered.Remove(existing);
        }

        byId[def.Id] = def;
        ordered.Add(def);
    }

    public void LoadFromAssets(
        ICoreAPI api,
        IHookRegistry hooks,
        IAbilityActionRegistry actions,
        CollectionIndex collections)
    {
        ordered.Clear();
        byId.Clear();

        List<KeyValuePair<AssetLocation, AttributeStatDefJson>> assets = api.Assets
            .GetMany<AttributeStatDefJson>(api.Logger, "config/prosequor/stats/", null)
            .OrderBy(kv => kv.Key.Domain, StringComparer.OrdinalIgnoreCase)
            .ThenBy(kv => kv.Key.Path, StringComparer.OrdinalIgnoreCase)
            .ToList();

        int sourceOrder = 100_000;
        foreach (KeyValuePair<AssetLocation, AttributeStatDefJson> kv in assets)
        {
            AttributeStatDefJson row = kv.Value;
            if (row == null || string.IsNullOrWhiteSpace(row.id))
            {
                api.Logger.Warning(
                    "[prosequor] Skipping attribute-stat asset {0}: missing id.",
                    kv.Key);
                continue;
            }

            string attributeId = row.id.Trim();
            List<string> errors = new();
            List<AbilityRule>? rules = AttributeRuleCompiler.CompileRules(
                attributeId,
                row.rules,
                hooks,
                actions,
                collections,
                errors,
                ref sourceOrder);

            if (rules == null)
            {
                foreach (string error in errors)
                {
                    api.Logger.Error("[prosequor] {0}", error);
                }

                api.Logger.Error(
                    "[prosequor] Attribute-stat asset {0} failed to compile; skipped.",
                    kv.Key);
                continue;
            }

            if (errors.Count > 0)
            {
                foreach (string error in errors)
                {
                    api.Logger.Warning("[prosequor] {0}", error);
                }
            }

            if (byId.ContainsKey(attributeId))
            {
                api.Logger.Warning(
                    "[prosequor] Attribute-stat '{0}' redefined by {1}; last-win.",
                    attributeId,
                    kv.Key);
            }

            Register(new AttributeStatDef
            {
                Id = attributeId,
                NameLang = string.IsNullOrWhiteSpace(row.nameLang)
                    ? DefaultNameLang(attributeId)
                    : row.nameLang.Trim(),
                DescriptionLang = string.IsNullOrWhiteSpace(row.descriptionLang)
                    ? DefaultDescriptionLang(attributeId)
                    : row.descriptionLang.Trim(),
                Icon = string.IsNullOrWhiteSpace(row.icon)
                    ? DefaultIcon(attributeId)
                    : row.icon.Trim(),
                Rules = rules
            });
        }

        EffectIndex = AttributeEffectIndex.Build(this);
        api.Logger.Notification(
            "[prosequor] Loaded {0} attribute-stat definition(s).",
            ordered.Count);
    }

    /// <summary>Omitted <c>nameLang</c>: <c>attribute-{id}</c> (domain-prefixed when namespaced).</summary>
    public static string DefaultNameLang(string attributeId)
    {
        SplitId(attributeId, out string? domain, out string local);
        string path = "attribute-" + local;
        return domain == null ? path : domain + ":" + path;
    }

    /// <summary>
    /// Omitted <c>descriptionLang</c>: <c>attribute-flavor-{id}</c> (tooltip quote).
    /// </summary>
    public static string DefaultDescriptionLang(string attributeId)
    {
        SplitId(attributeId, out string? domain, out string local);
        string path = "attribute-flavor-" + local;
        return domain == null ? path : domain + ":" + path;
    }

    /// <summary>
    /// Omitted <c>icon</c>: <c>textures/icons/{local}-attribute.svg</c>.
    /// </summary>
    public static string DefaultIcon(string attributeId)
    {
        SplitId(attributeId, out _, out string local);
        return "textures/icons/" + local + "-attribute.svg";
    }

    public static string DisplayName(AttributeStatDef def, string? languageCode = null)
    {
        string key = def.NameLang;
        if (string.IsNullOrWhiteSpace(key))
        {
            return def.Id;
        }

        if (!key.Contains(':'))
        {
            key = "prosequor:" + key;
        }

        string translated = string.IsNullOrWhiteSpace(languageCode)
            ? Lang.Get(key)
            : Lang.GetL(languageCode, key);
        return string.IsNullOrEmpty(translated) || string.Equals(translated, key, StringComparison.Ordinal)
            ? def.Id
            : translated;
    }

    public static string Description(AttributeStatDef def, string? languageCode = null)
    {
        string key = def.DescriptionLang;
        if (string.IsNullOrWhiteSpace(key))
        {
            return "";
        }

        if (!key.Contains(':'))
        {
            key = "prosequor:" + key;
        }

        string translated = string.IsNullOrWhiteSpace(languageCode)
            ? Lang.Get(key)
            : Lang.GetL(languageCode, key);
        return string.IsNullOrEmpty(translated) || string.Equals(translated, key, StringComparison.Ordinal)
            ? ""
            : translated;
    }

    public static AssetLocation? IconLocation(AttributeStatDef def)
    {
        if (string.IsNullOrWhiteSpace(def.Icon))
        {
            return null;
        }

        string path = def.Icon.Trim().Replace('\\', '/').TrimStart('/');
        if (path.Contains(':'))
        {
            return new AssetLocation(path);
        }

        return new AssetLocation("prosequor", path);
    }

    static void SplitId(string id, out string? domain, out string local)
    {
        int colon = id.IndexOf(':');
        if (colon <= 0 || colon >= id.Length - 1)
        {
            domain = null;
            local = id;
            return;
        }

        domain = id[..colon];
        local = id[(colon + 1)..].Replace(':', '-');
    }
}
