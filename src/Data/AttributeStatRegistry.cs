using Prosequor.Ability;
using Prosequor.Ability.Hooks;
using Vintagestory.API.Common;

namespace Prosequor.Data;

public interface IAttributeStatRegistry
{
    IReadOnlyList<AttributeStatDef> All { get; }
    AttributeEffectIndex EffectIndex { get; }
    bool TryGet(string id, out AttributeStatDef def);
}

/// <summary>
/// Loads attribute-stat definitions from <c>config/prosequor/stats/*.json</c>.
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

    public void Register(AttributeStatDef def)
    {
        if (string.IsNullOrWhiteSpace(def.Id))
        {
            return;
        }

        def.Id = def.Id.Trim();
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
            string? canonical = AttributeIds.Canonicalize(attributeId);
            if (canonical == null)
            {
                api.Logger.Warning(
                    "[prosequor] Skipping attribute-stat asset {0}: unknown attribute '{1}'.",
                    kv.Key,
                    attributeId);
                continue;
            }

            attributeId = canonical;
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
                Rules = rules
            });
        }

        EffectIndex = AttributeEffectIndex.Build(this);
        api.Logger.Notification(
            "[prosequor] Loaded {0} attribute-stat definition(s).",
            ordered.Count);
    }
}
