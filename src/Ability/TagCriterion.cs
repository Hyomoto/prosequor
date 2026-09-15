using Prosequor.Ability.Hooks;
using Prosequor.Data;
using Prosequor.Xp;

namespace Prosequor.Ability;

/// <summary>
/// Closed role set for identity/collection criteria. Authoring may still write <c>held</c>
/// (alias of <see cref="Caller"/>). <c>damage</c> is parse-only and becomes
/// <see cref="DamageIdentityCriterion"/>.
/// </summary>
public enum FactRole
{
    Caller,
    Target,
    Drop,
    LastCraft,
    Ground,
    Mount,
    Op,
    Input
}

/// <summary>Authoring strings for <see cref="FactRole"/> (fingerprint / diagnostics).</summary>
public static class FactRoles
{
    public static string ToTag(this FactRole role) => role switch
    {
        FactRole.Caller => "caller",
        FactRole.Target => "target",
        FactRole.Drop => "drop",
        FactRole.LastCraft => "last-craft",
        FactRole.Ground => "ground",
        FactRole.Mount => "mount",
        FactRole.Op => "op",
        FactRole.Input => "input",
        _ => role.ToString().ToLowerInvariant()
    };

    /// <summary>
    /// Parses a role name. <c>held</c> maps to <see cref="FactRole.Caller"/>.
    /// Does not accept <c>damage</c> (that role is parse-only for damage criteria).
    /// </summary>
    public static bool TryParse(string? raw, out FactRole role)
    {
        role = default;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        string t = raw.Trim();
        if (t.Equals("caller", StringComparison.OrdinalIgnoreCase)
            || t.Equals("held", StringComparison.OrdinalIgnoreCase))
        {
            role = FactRole.Caller;
            return true;
        }

        if (t.Equals("target", StringComparison.OrdinalIgnoreCase))
        {
            role = FactRole.Target;
            return true;
        }

        if (t.Equals("drop", StringComparison.OrdinalIgnoreCase))
        {
            role = FactRole.Drop;
            return true;
        }

        if (t.Equals("last-craft", StringComparison.OrdinalIgnoreCase))
        {
            role = FactRole.LastCraft;
            return true;
        }

        if (t.Equals("ground", StringComparison.OrdinalIgnoreCase))
        {
            role = FactRole.Ground;
            return true;
        }

        if (t.Equals("mount", StringComparison.OrdinalIgnoreCase))
        {
            role = FactRole.Mount;
            return true;
        }

        if (t.Equals("op", StringComparison.OrdinalIgnoreCase))
        {
            role = FactRole.Op;
            return true;
        }

        if (t.Equals("input", StringComparison.OrdinalIgnoreCase))
        {
            role = FactRole.Input;
            return true;
        }

        return false;
    }

    /// <summary>True when <paramref name="left"/> is a known role prefix including <c>damage</c>.</summary>
    public static bool IsKnownRolePrefix(string left) =>
        TryParse(left, out _)
        || left.Equals("damage", StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// One compiled <c>when.tags</c> criterion. Roles use the first colon as the split when
/// the left side is a known role; otherwise the whole string is a target identity.
/// Bare tokens and bare <c>&lt;key&gt;</c> default to role <c>target</c> (tokens stay bare).
/// </summary>
public abstract class TagCriterion
{
    public abstract bool Matches(AbilityAction? fact, CollectionIndex? collections);

    /// <summary>Specificity for XP winner scoring: identity &gt; pattern &gt; collection &gt; token.</summary>
    public abstract int SpecificityScore { get; }

    /// <summary>
    /// Bakes collection membership into every <see cref="RoleCollectionCriterion"/> in
    /// skills and attribute stats. Call after GameReady membership fill.
    /// </summary>
    public static void BindAll(
        ISkillRegistry skills,
        IAttributeStatRegistry attributeStats,
        CollectionIndex collections)
    {
        foreach (SkillDef skill in skills.All)
        {
            foreach (AbilityRule rule in skill.Rules)
            {
                BindCriteria(rule.When.Criteria, collections);
            }

            foreach (XpRule xpRule in skill.XpRules)
            {
                BindCriteria(xpRule.Criteria, collections);
            }
        }

        foreach (AttributeStatDef attr in attributeStats.All)
        {
            foreach (AbilityRule rule in attr.Rules)
            {
                BindCriteria(rule.When.Criteria, collections);
            }
        }
    }

    static void BindCriteria(IReadOnlyList<TagCriterion> criteria, CollectionIndex collections)
    {
        foreach (TagCriterion criterion in criteria)
        {
            if (criterion is RoleCollectionCriterion roleCollection)
            {
                roleCollection.Bind(collections);
            }
        }
    }
}

public sealed class TokenCriterion : TagCriterion
{
    public required string Token { get; init; }

    public override int SpecificityScore => 100;

    public override bool Matches(AbilityAction? fact, CollectionIndex? collections) =>
        fact != null && fact.Tokens.Contains(Token);
}

public sealed class RoleIdentityCriterion : TagCriterion
{
    /// <summary>Reserved identity: role has no code (empty hands, missing target, …).</summary>
    public const string NoneIdentity = "none";

    public required FactRole Role { get; init; }
    public required string Identity { get; init; }

    public override int SpecificityScore => 1_000_000;

    public override bool Matches(AbilityAction? fact, CollectionIndex? collections)
    {
        if (fact == null)
        {
            return false;
        }

        if (IsNoneIdentity(Identity))
        {
            if (Role == FactRole.Input)
            {
                return fact.Inputs.Count == 0;
            }

            return string.IsNullOrWhiteSpace(ResolveRole(fact, Role));
        }

        if (Role == FactRole.Input)
        {
            return RoleMatchesAnyInput(fact, Identity);
        }

        string? value = ResolveRole(fact, Role);
        return string.Equals(value, Identity, StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsNoneIdentity(string? identity) =>
        string.Equals(identity, NoneIdentity, StringComparison.OrdinalIgnoreCase);

    public static string? ResolveRole(AbilityAction fact, FactRole role) =>
        role switch
        {
            FactRole.Caller => fact.Caller,
            FactRole.Target => fact.Target,
            FactRole.Drop => fact.Drop,
            FactRole.LastCraft => fact.LastCraft,
            FactRole.Ground => fact.Ground,
            FactRole.Mount => fact.Mount,
            FactRole.Op => fact.Op,
            _ => null
        };

    public static bool RoleMatchesAnyInput(AbilityAction fact, string identity)
    {
        for (int i = 0; i < fact.Inputs.Count; i++)
        {
            if (string.Equals(fact.Inputs[i], identity, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}

public sealed class RoleCollectionCriterion : TagCriterion
{
    public required FactRole Role { get; init; }

    /// <summary>One or more collection ids; match if the role identity is in any of them.</summary>
    public required IReadOnlyList<string> CollectionIds { get; init; }

    /// <summary>
    /// Union of collection membership after <see cref="Bind"/>. Null until GameReady bake
    /// (or in pure tests that never bind).
    /// </summary>
    public IReadOnlySet<string>? Members { get; private set; }

    public override int SpecificityScore => 1_000;

    /// <summary>Unions membership for <see cref="CollectionIds"/> into <see cref="Members"/>.</summary>
    public void Bind(CollectionIndex collections)
    {
        HashSet<string> union = new(StringComparer.OrdinalIgnoreCase);
        foreach (string id in CollectionIds)
        {
            foreach (string code in collections.Codes(id))
            {
                union.Add(code);
            }
        }

        Members = union;
    }

    public override bool Matches(AbilityAction? fact, CollectionIndex? collections)
    {
        if (fact == null || CollectionIds.Count == 0)
        {
            return false;
        }

        if (Role == FactRole.Input)
        {
            for (int i = 0; i < fact.Inputs.Count; i++)
            {
                if (CodeMatches(fact.Inputs[i], collections))
                {
                    return true;
                }
            }

            return false;
        }

        string? value = RoleIdentityCriterion.ResolveRole(fact, Role);
        return CodeMatches(value, collections);
    }

    bool CodeMatches(string? code, CollectionIndex? collections)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return false;
        }

        if (Members != null)
        {
            return Members.Contains(code);
        }

        return collections != null && collections.ContainsAny(CollectionIds, code);
    }
}

public sealed class DamageIdentityCriterion : TagCriterion
{
    public required string Kind { get; init; }

    public override int SpecificityScore => 1_000_000;

    public override bool Matches(AbilityAction? fact, CollectionIndex? collections) =>
        fact != null && fact.Damage.Contains(Kind);
}

/// <summary>Parses authoring tag strings into compiled criteria.</summary>
public static class TagCriterionParser
{
    public static bool TryParse(
        string raw,
        CollectionIndex collections,
        out TagCriterion? criterion,
        out string error)
    {
        criterion = null;
        if (string.IsNullOrWhiteSpace(raw))
        {
            error = "empty tag.";
            return false;
        }

        string trimmed = raw.Trim();

        // Bare tag (no role:): collection → target collection; else open token.
        int colon = trimmed.IndexOf(':');
        if (colon <= 0)
        {
            if (TryParseCollectionRef(trimmed, out List<string>? bareCollections, out string? bareRefError))
            {
                if (bareCollections == null)
                {
                    error = bareRefError ?? "invalid collection ref.";
                    return false;
                }

                foreach (string collectionId in bareCollections)
                {
                    if (!collections.Exists(collectionId))
                    {
                        error = $"unknown collection '<{collectionId}>'.";
                        return false;
                    }
                }

                criterion = new RoleCollectionCriterion
                {
                    Role = FactRole.Target,
                    CollectionIds = bareCollections
                };
                error = "";
                return true;
            }

            if (trimmed.Contains('*'))
            {
                error =
                    $"wildcard '{trimmed}' is not allowed in when.tags; define a collection with includes.";
                return false;
            }

            criterion = new TokenCriterion { Token = Xp.Activity.DeedTokenTags.Canonical(trimmed) };
            error = "";
            return true;
        }

        string left = trimmed[..colon];
        string roleRaw;
        string rest;
        if (FactRoles.IsKnownRolePrefix(left))
        {
            roleRaw = left;
            rest = trimmed[(colon + 1)..];
        }
        else
        {
            // Namespaced identity (game:torch) → target role, full code.
            roleRaw = "target";
            rest = trimmed;
        }

        if (string.IsNullOrWhiteSpace(rest))
        {
            error = $"empty value for role '{roleRaw}'.";
            return false;
        }

        // held:none / caller:none / target:none — reserved empty-role sentinel (not a collection).
        if (RoleIdentityCriterion.IsNoneIdentity(rest))
        {
            if (roleRaw.Equals("damage", StringComparison.OrdinalIgnoreCase)
                || roleRaw.Equals("op", StringComparison.OrdinalIgnoreCase))
            {
                error = $"role '{roleRaw}' does not accept '{RoleIdentityCriterion.NoneIdentity}'.";
                return false;
            }

            if (!FactRoles.TryParse(roleRaw, out FactRole noneRole))
            {
                error = $"unknown role '{roleRaw}'.";
                return false;
            }

            criterion = new RoleIdentityCriterion
            {
                Role = noneRole,
                Identity = RoleIdentityCriterion.NoneIdentity
            };
            error = "";
            return true;
        }

        // caller:@hand / caller:@grid / caller:@trough / … — reserved caller identities.
        if (rest.StartsWith('@'))
        {
            bool callerRole = roleRaw.Equals("caller", StringComparison.OrdinalIgnoreCase)
                || roleRaw.Equals("held", StringComparison.OrdinalIgnoreCase);
            if (!callerRole)
            {
                error = $"reserved identity '{rest}' is only valid on caller (or held alias).";
                return false;
            }

            if (!CallerIdentities.TryNormalizeReserved(rest, out string reserved))
            {
                error = $"unknown reserved caller '{rest}'; use @hand, @grid, @trough, @crop, or @loose.";
                return false;
            }

            criterion = new RoleIdentityCriterion
            {
                Role = FactRole.Caller,
                Identity = reserved
            };
            error = "";
            return true;
        }

        if (roleRaw.Equals("damage", StringComparison.OrdinalIgnoreCase))
        {
            if (TryParseCollectionRef(rest, out _, out _))
            {
                error = $"damage role does not accept collections ('{rest}'); use damage:frost.";
                return false;
            }

            criterion = new DamageIdentityCriterion { Kind = rest };
            error = "";
            return true;
        }

        if (!FactRoles.TryParse(roleRaw, out FactRole role))
        {
            error = $"unknown role '{roleRaw}'.";
            return false;
        }

        if (role == FactRole.Op)
        {
            if (TryParseCollectionRef(rest, out _, out _))
            {
                error = $"op role does not accept collections ('{rest}'); use op:place.";
                return false;
            }

            criterion = new RoleIdentityCriterion { Role = FactRole.Op, Identity = rest };
            error = "";
            return true;
        }

        if (role == FactRole.Input && !TryParseCollectionRef(rest, out _, out _))
        {
            // input:game:flaxtwine — identity among Inputs.
            if (rest.Contains('*'))
            {
                error =
                    $"wildcard '{rest}' is not allowed in when.tags; define a collection with includes.";
                return false;
            }

            criterion = new RoleIdentityCriterion { Role = FactRole.Input, Identity = rest };
            error = "";
            return true;
        }

        if (TryParseCollectionRef(rest, out List<string>? collectionIds, out string? refError))
        {
            if (collectionIds == null)
            {
                error = refError ?? "invalid collection ref.";
                return false;
            }

            foreach (string collectionId in collectionIds)
            {
                if (!collections.Exists(collectionId))
                {
                    error = $"unknown collection '<{collectionId}>'.";
                    return false;
                }
            }

            criterion = new RoleCollectionCriterion
            {
                Role = role,
                CollectionIds = collectionIds
            };
            error = "";
            return true;
        }

        if (rest.Contains('*'))
        {
            error =
                $"wildcard '{rest}' is not allowed in when.tags; define a collection with includes.";
            return false;
        }

        criterion = new RoleIdentityCriterion
        {
            Role = role,
            Identity = rest
        };
        error = "";
        return true;
    }

    /// <summary><c>held</c> authoring alias compiles to role <c>caller</c>.</summary>
    public static string NormalizeAuthoringRole(string role) =>
        FactRoles.TryParse(role, out FactRole parsed) ? parsed.ToTag() : role;

    /// <summary>
    /// Parses <c>&lt;id&gt;</c> or <c>&lt;a, b&gt;</c>. Returns false when <paramref name="rest"/>
    /// is not a collection ref. Returns true with null ids when the ref is malformed.
    /// </summary>
    public static bool TryParseCollectionRef(
        string rest,
        out List<string>? ids,
        out string? error)
    {
        ids = null;
        error = null;
        if (rest.Length < 3 || rest[0] != '<' || rest[^1] != '>')
        {
            return false;
        }

        string inner = rest[1..^1].Trim();
        if (inner.Length == 0)
        {
            error = "empty collection ref '<>'.";
            ids = null;
            return true;
        }

        string[] parts = inner.Split(',');
        List<string> parsed = new(parts.Length);
        foreach (string part in parts)
        {
            string id = part.Trim();
            if (id.Length == 0)
            {
                error = $"empty collection id in '{rest}'.";
                ids = null;
                return true;
            }

            parsed.Add(id);
        }

        ids = parsed;
        return true;
    }
}
