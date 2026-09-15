using Prosequor.Ability;
using Prosequor.Ability.Hooks;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace Prosequor.Xp.Activity;

/// <summary>
/// Fire-and-forget discrete amount XP. Emitters pass payee + tokens/roles; rules resolve at the
/// call and pay through <see cref="FatherXp"/> (online or mailbox). No watcher involvement.
/// </summary>
public static class Deed
{
    public const string Activity = "prosequor:deed";

    /// <summary>Metric domain for clay fire amount tables (voxels per unit).</summary>
    public const string MetricDomainClayVoxels = "clay-voxels";

    public readonly record struct PlannedPay(string SkillId, float Amount, AbilityAction Fact);

    /// <summary>One countable quantity unit (code + stack size) for <c>exclude</c> filtering.</summary>
    public readonly record struct QuantityUnit(string Code, int Count);

    /// <summary>Weighted contributor share published on the emit for <c>payee</c> resolution.</summary>
    public readonly record struct ContributorShare(string PlayerUid, float Weight);

    /// <summary>Named emit channels a deed publishes; rules select which ones to read via <c>pay</c>.</summary>
    public readonly record struct Channels(
        float Resistance,
        float ResistanceMin,
        float ResistanceMax,
        bool HasResistance,
        float Voxels,
        float VoxelsMin,
        float VoxelsMax,
        bool HasVoxels,
        int Quantity,
        int Ingredients,
        bool HasIngredients = false,
        IReadOnlyList<QuantityUnit>? QuantityUnits = null,
        IReadOnlyList<ContributorShare>? Contributors = null,
        string? MakerUid = null,
        string? SelectedContributorUid = null);

    /// <summary>Role bag for <c>payee</c> resolution (separate from pay channels).</summary>
    public readonly record struct PayeeRoles(
        string? MakerUid = null,
        string? SelectedContributorUid = null,
        IReadOnlyList<ContributorShare>? Contributors = null);

    public static void Emit(
        ICoreAPI api,
        string playerUid,
        DeedToken token,
        string? caller = null,
        string? target = null,
        string? mount = null,
        string? ground = null,
        string? lastCraft = null,
        float metric = 0f,
        string? metricDomain = null,
        float? metricMin = null,
        float? metricMax = null,
        int totalUnits = 0,
        int craftCount = 1,
        XpAwardMode mode = XpAwardMode.Earn,
        BlockPos? position = null,
        IReadOnlyList<QuantityUnit>? quantityUnits = null,
        IReadOnlyList<ContributorShare>? contributors = null,
        string? makerUid = null,
        string? selectedContributorUid = null) =>
        Emit(
            api,
            playerUid,
            [token.ToTag()],
            caller,
            target,
            mount,
            ground,
            lastCraft,
            metric,
            metricDomain,
            metricMin,
            metricMax,
            totalUnits,
            craftCount,
            mode,
            position,
            quantityUnits,
            contributors,
            makerUid,
            selectedContributorUid);

    public static void Emit(
        ICoreAPI api,
        string playerUid,
        IReadOnlyList<DeedToken> tokens,
        string? caller = null,
        string? target = null,
        string? mount = null,
        string? ground = null,
        string? lastCraft = null,
        float metric = 0f,
        string? metricDomain = null,
        float? metricMin = null,
        float? metricMax = null,
        int totalUnits = 0,
        int craftCount = 1,
        XpAwardMode mode = XpAwardMode.Earn,
        BlockPos? position = null,
        IReadOnlyList<QuantityUnit>? quantityUnits = null,
        IReadOnlyList<ContributorShare>? contributors = null,
        string? makerUid = null,
        string? selectedContributorUid = null)
    {
        if (tokens == null || tokens.Count == 0)
        {
            return;
        }

        string[] tags = new string[tokens.Count];
        for (int i = 0; i < tokens.Count; i++)
        {
            tags[i] = tokens[i].ToTag();
        }

        Emit(
            api,
            playerUid,
            tags,
            caller,
            target,
            mount,
            ground,
            lastCraft,
            metric,
            metricDomain,
            metricMin,
            metricMax,
            totalUnits,
            craftCount,
            mode,
            position,
            quantityUnits,
            contributors,
            makerUid,
            selectedContributorUid);
    }

    public static void Emit(
        ICoreAPI api,
        IPlayer player,
        IReadOnlyList<string> tokens,
        string? caller = null,
        string? target = null,
        string? mount = null,
        string? ground = null,
        string? lastCraft = null,
        float metric = 0f,
        string? metricDomain = null,
        float? metricMin = null,
        float? metricMax = null,
        int totalUnits = 0,
        int craftCount = 1,
        XpAwardMode mode = XpAwardMode.Earn,
        BlockPos? position = null,
        IReadOnlyList<QuantityUnit>? quantityUnits = null,
        IReadOnlyList<ContributorShare>? contributors = null,
        string? makerUid = null,
        string? selectedContributorUid = null)
    {
        if (player?.PlayerUID == null)
        {
            return;
        }

        ICoreAPI? core = api ?? player.Entity?.Api;
        if (core == null)
        {
            return;
        }

        Emit(
            core,
            player.PlayerUID,
            tokens,
            caller,
            target,
            mount,
            ground,
            lastCraft,
            metric,
            metricDomain,
            metricMin,
            metricMax,
            totalUnits,
            craftCount,
            mode,
            position,
            quantityUnits,
            contributors,
            makerUid,
            selectedContributorUid);
    }

    public static void Emit(
        ICoreAPI api,
        string playerUid,
        IReadOnlyList<string> tokens,
        string? caller = null,
        string? target = null,
        string? mount = null,
        string? ground = null,
        string? lastCraft = null,
        float metric = 0f,
        string? metricDomain = null,
        float? metricMin = null,
        float? metricMax = null,
        int totalUnits = 0,
        int craftCount = 1,
        XpAwardMode mode = XpAwardMode.Earn,
        BlockPos? position = null,
        IReadOnlyList<QuantityUnit>? quantityUnits = null,
        IReadOnlyList<ContributorShare>? contributors = null,
        string? makerUid = null,
        string? selectedContributorUid = null,
        string? activity = null,
        IReadOnlyList<string>? inputs = null)
    {
        if (api == null || api.Side != EnumAppSide.Server)
        {
            return;
        }

        IReadOnlyList<ContributorShare> shares = NormalizeContributors(contributors);
        string actor = string.IsNullOrWhiteSpace(playerUid) ? "" : playerUid.Trim();
        string? maker = NormalizeUid(makerUid);
        string? selected = NormalizeUid(selectedContributorUid);
        // Passive triggers may omit actor when paying maker / contributor(s).
        if (actor.Length == 0
            && maker == null
            && selected == null
            && shares.Count == 0)
        {
            return;
        }

        HashSet<string> tokenSet = BuildTokenSet(tokens);

        ProsequorModSystem? mod = ProsequorModSystem.For(api);
        FatherXp? father = mod?.FatherXp;
        IXpRuleRegistry? rules = mod?.XpRules;
        if (father == null || rules == null)
        {
            return;
        }

        ResolveMetricRange(mod, metricDomain, metricMin, metricMax, out float min, out float max);
        Channels channels = BuildChannels(
            metric,
            min,
            max,
            metricDomain,
            totalUnits,
            craftCount,
            quantityUnits,
            shares,
            maker,
            selected);

        IReadOnlyList<PlannedPay> pays = PlanPays(
            rules,
            mod?.Collections?.Index ?? new CollectionIndex(),
            actor,
            tokenSet,
            caller,
            target,
            mount,
            ground,
            lastCraft,
            channels,
            position,
            activity,
            inputs);

        foreach (PlannedPay pay in pays)
        {
            father.Pay(pay.Fact.ActorUid, pay.SkillId, pay.Amount, pay.Fact, mode);
        }
    }

    /// <summary>Legacy <see cref="XpAction"/> entry — activity forced to <see cref="Activity"/>.</summary>
    public static void Emit(ICoreAPI api, XpAction action)
    {
        if (action == null || string.IsNullOrWhiteSpace(action.ActorUid))
        {
            return;
        }

        Emit(
            api,
            action.ActorUid,
            action.Tokens.Count > 0 ? action.Tokens.ToList() : Array.Empty<string>(),
            action.Held,
            action.Target,
            action.Mount,
            action.Ground,
            action.LastCraft,
            action.Hardness,
            action.HardnessDomain,
            metricMin: null,
            metricMax: null,
            action.TotalUnits,
            action.CraftCount > 0 ? action.CraftCount : 1,
            XpAwardMode.Earn,
            action.Position);
    }

    /// <summary>
    /// Pure match/pay planning for tests and callers that deliver XP themselves.
    /// Legacy metric args map into <see cref="Channels"/> the same way as <see cref="Emit"/>.
    /// </summary>
    public static IReadOnlyList<PlannedPay> PlanPays(
        IXpRuleRegistry rules,
        CollectionIndex collections,
        string playerUid,
        IReadOnlySet<string> tokens,
        string? caller,
        string? target,
        string? mount,
        string? ground,
        string? lastCraft,
        float metric,
        float metricMin,
        float metricMax,
        int totalUnits,
        int craftCount,
        BlockPos? position = null,
        string? metricDomain = null) =>
        PlanPays(
            rules,
            collections,
            playerUid,
            tokens,
            caller,
            target,
            mount,
            ground,
            lastCraft,
            BuildChannels(metric, metricMin, metricMax, metricDomain, totalUnits, craftCount),
            position);

    /// <summary>Pure match/pay planning against an explicit channel bag.</summary>
    public static IReadOnlyList<PlannedPay> PlanPays(
        IXpRuleRegistry rules,
        CollectionIndex collections,
        string playerUid,
        IReadOnlySet<string> tokens,
        string? caller,
        string? target,
        string? mount,
        string? ground,
        string? lastCraft,
        Channels channels,
        BlockPos? position = null,
        string? activity = null,
        IReadOnlyList<string>? inputs = null)
    {
        if (rules == null)
        {
            return Array.Empty<PlannedPay>();
        }

        string actor = string.IsNullOrWhiteSpace(playerUid) ? "" : playerUid.Trim();
        IReadOnlyList<ContributorShare> shares = NormalizeContributors(channels.Contributors);
        string? maker = NormalizeUid(channels.MakerUid);
        string? selected = NormalizeUid(channels.SelectedContributorUid);
        if (actor.Length == 0
            && maker == null
            && selected == null
            && shares.Count == 0)
        {
            return Array.Empty<PlannedPay>();
        }

        tokens = CanonicalizeTokens(tokens);
        string activityId = string.IsNullOrWhiteSpace(activity)
            ? Activity
            : XpRuleRegistry.NormalizeActivity(activity.Trim());

        IReadOnlyList<XpRule> candidates = rules.ByActivityAmount(activityId);
        if (candidates.Count == 0)
        {
            return Array.Empty<PlannedPay>();
        }

        collections ??= new CollectionIndex();
        Channels resolvedChannels = channels with
        {
            Contributors = shares.Count > 0 ? shares : null,
            MakerUid = maker,
            SelectedContributorUid = selected
        };

        XpMatchFact match = new()
        {
            Activity = activityId,
            Caller = CallerIdentities.ForDeed(caller),
            Target = target,
            LastCraft = lastCraft,
            Ground = ground,
            Mount = mount,
            Tokens = tokens,
            Inputs = NormalizeInputs(inputs)
        };

        List<PlannedPay> pays = new();
        foreach (IGrouping<string, XpRule> skillGroup in candidates.GroupBy(
                     r => r.SkillId,
                     StringComparer.OrdinalIgnoreCase))
        {
            XpRule? winner = XpRuleMatcher.PickWinner(skillGroup, match, collections);
            if (winner == null || !winner.IsAmountRule)
            {
                continue;
            }

            float grant = ResolveGrant(winner, resolvedChannels, target, collections);
            if (grant <= 0f)
            {
                continue;
            }

            foreach ((string payeeUid, float amount) in ResolvePayees(
                         winner.Payee,
                         actor,
                         maker,
                         selected,
                         shares,
                         grant))
            {
                if (amount <= 0f || string.IsNullOrWhiteSpace(payeeUid))
                {
                    continue;
                }

                AbilityAction fact = new()
                {
                    Verb = activityId,
                    ActorUid = payeeUid.Trim(),
                    Caller = CallerIdentities.ForDeed(caller),
                    Target = target,
                    LastCraft = lastCraft,
                    Ground = ground,
                    Mount = mount,
                    Tokens = tokens,
                    Inputs = match.Inputs,
                    Position = position?.Copy()
                };

                pays.Add(new PlannedPay(winner.SkillId, amount, fact));
            }
        }

        return pays;
    }

    /// <summary>
    /// Expand a grant into payee uid + amount slices according to <paramref name="payee"/>.
    /// Missing role data never falls back to another role.
    /// </summary>
    public static IReadOnlyList<(string PlayerUid, float Amount)> ResolvePayees(
        XpPayee payee,
        string? actorUid,
        string? makerUid,
        string? selectedContributorUid,
        IReadOnlyList<ContributorShare>? contributors,
        float grant)
    {
        if (grant <= 0f)
        {
            return Array.Empty<(string, float)>();
        }

        switch (payee)
        {
            case XpPayee.Maker:
            {
                string? maker = NormalizeUid(makerUid);
                return maker == null
                    ? Array.Empty<(string, float)>()
                    : [(maker, grant)];
            }
            case XpPayee.Contributor:
            {
                string? selected = NormalizeUid(selectedContributorUid);
                return selected == null
                    ? Array.Empty<(string, float)>()
                    : [(selected, grant)];
            }
            case XpPayee.Contributors:
            {
                IReadOnlyList<ContributorShare> shares = NormalizeContributors(contributors);
                if (shares.Count == 0)
                {
                    return Array.Empty<(string, float)>();
                }

                float sum = 0f;
                for (int i = 0; i < shares.Count; i++)
                {
                    sum += shares[i].Weight;
                }

                if (sum <= 0f)
                {
                    return Array.Empty<(string, float)>();
                }

                List<(string, float)> slices = new(shares.Count);
                for (int i = 0; i < shares.Count; i++)
                {
                    ContributorShare share = shares[i];
                    float slice = grant * (share.Weight / sum);
                    if (slice > 0f)
                    {
                        slices.Add((share.PlayerUid, slice));
                    }
                }

                return slices;
            }
            default:
            {
                if (string.IsNullOrWhiteSpace(actorUid))
                {
                    return Array.Empty<(string, float)>();
                }

                return [(actorUid.Trim(), grant)];
            }
        }
    }

    /// <summary>Legacy overload — actor only; no maker / selected / shares.</summary>
    public static IReadOnlyList<(string PlayerUid, float Amount)> ResolvePayees(
        XpPayee payee,
        string? actorUid,
        IReadOnlyList<ContributorShare>? contributors,
        float grant) =>
        ResolvePayees(
            payee,
            actorUid,
            makerUid: null,
            selectedContributorUid: null,
            contributors,
            grant);

    /// <summary>
    /// Map legacy Emit metric args into named channels.
    /// dig/mine/chop → resistance; clay-voxels → voxels; totalUnits → ingredients measure;
    /// craftCount → quantity fallback; quantityUnits (when present) override quantity as summed stack counts.
    /// When domain is blank but a metric range is present (tests), publish as resistance.
    /// </summary>
    public static Channels BuildChannels(
        float metric,
        float metricMin,
        float metricMax,
        string? metricDomain,
        int totalUnits,
        int craftCount,
        IReadOnlyList<QuantityUnit>? quantityUnits = null,
        IReadOnlyList<ContributorShare>? contributors = null,
        string? makerUid = null,
        string? selectedContributorUid = null)
    {
        int quantity = craftCount > 0 ? craftCount : 1;
        int ingredients = Math.Max(0, totalUnits);
        bool hasResistance = false;
        bool hasVoxels = false;
        float resistance = 0f;
        float resistanceMin = 0f;
        float resistanceMax = 0f;
        float voxels = 0f;
        float voxelsMin = 0f;
        float voxelsMax = 0f;

        string? domain = string.IsNullOrWhiteSpace(metricDomain) ? null : metricDomain.Trim();
        if (IsResistanceDomain(domain))
        {
            hasResistance = true;
            resistance = metric;
            resistanceMin = metricMin;
            resistanceMax = metricMax;
        }
        else if (IsVoxelsDomain(domain))
        {
            hasVoxels = true;
            voxels = metric;
            voxelsMin = metricMin;
            voxelsMax = metricMax;
        }
        else if (domain == null && (metric > 0f || metricMax > metricMin))
        {
            // Test / explicit-range convenience: unlabeled metric is resistance.
            hasResistance = true;
            resistance = metric;
            resistanceMin = metricMin;
            resistanceMax = metricMax;
        }

        if (quantityUnits != null && quantityUnits.Count > 0)
        {
            int summed = 0;
            for (int i = 0; i < quantityUnits.Count; i++)
            {
                summed += Math.Max(0, quantityUnits[i].Count);
            }

            if (summed > 0)
            {
                quantity = summed;
            }
        }

        IReadOnlyList<ContributorShare> shares = NormalizeContributors(contributors);

        return new Channels(
            resistance,
            resistanceMin,
            resistanceMax,
            hasResistance,
            voxels,
            voxelsMin,
            voxelsMax,
            hasVoxels,
            quantity,
            ingredients,
            HasIngredients: ingredients > 0,
            quantityUnits,
            shares.Count > 0 ? shares : null,
            NormalizeUid(makerUid),
            NormalizeUid(selectedContributorUid));
    }

    /// <summary>Drop blank uids and non-positive weights; preserve order.</summary>
    public static IReadOnlyList<ContributorShare> NormalizeContributors(
        IReadOnlyList<ContributorShare>? contributors)
    {
        if (contributors == null || contributors.Count == 0)
        {
            return Array.Empty<ContributorShare>();
        }

        List<ContributorShare> list = new(contributors.Count);
        for (int i = 0; i < contributors.Count; i++)
        {
            ContributorShare share = contributors[i];
            if (string.IsNullOrWhiteSpace(share.PlayerUid) || share.Weight <= 0f)
            {
                continue;
            }

            list.Add(new ContributorShare(share.PlayerUid.Trim(), share.Weight));
        }

        return list;
    }

    static string? NormalizeUid(string? uid)
    {
        if (string.IsNullOrWhiteSpace(uid))
        {
            return null;
        }

        string trimmed = uid.Trim();
        return trimmed.Length == 0 ? null : trimmed;
    }

    public static float ResolveGrant(
        XpRule rule,
        Channels channels,
        string? target = null,
        CollectionIndex? collections = null)
    {
        if (rule == null)
        {
            return 0f;
        }

        XpPayChannel pay = rule.Pay;
        float baseAmount;
        if (XpPayChannels.IsFlat(pay))
        {
            // Flat: amount once. Table without a measure channel → amount[0].
            baseAmount = AmountTableMath.ResolveAmount(
                rule.Amount,
                rule.AmountTable,
                value: 0f,
                min: 0f,
                max: 0f);
        }
        else if (XpPayChannels.UsesMeasure(pay))
        {
            PickMeasure(pay, channels, out float value, out float min, out float max);
            baseAmount = AmountTableMath.ResolveAmount(
                rule.Amount,
                rule.AmountTable,
                value,
                min,
                max);
        }
        else
        {
            // Quantity-only — scalar (or table[0] if misconfigured).
            baseAmount = AmountTableMath.ResolveAmount(
                rule.Amount,
                rule.AmountTable,
                value: 0f,
                min: 0f,
                max: 0f);
        }

        if (baseAmount <= 0f)
        {
            return 0f;
        }

        if (XpPayChannels.IsFlat(pay))
        {
            return baseAmount;
        }

        if (!XpPayChannels.UsesQuantity(pay))
        {
            return baseAmount;
        }

        int quantity = ResolveQuantity(rule, channels, target, collections ?? new CollectionIndex());
        if (quantity <= 0)
        {
            return 0f;
        }

        return baseAmount * quantity;
    }

    /// <summary>
    /// Apply <c>exclude</c> to quantity. Prefer per-unit <see cref="Channels.QuantityUnits"/>;
    /// otherwise gate the whole quantity on the deed <paramref name="target"/>.
    /// </summary>
    public static int ResolveQuantity(
        XpRule rule,
        Channels channels,
        string? target,
        CollectionIndex collections)
    {
        int raw = channels.Quantity > 0 ? channels.Quantity : 1;
        XpQuantityExclude exclude = rule.Exclude ?? XpQuantityExclude.Empty;
        if (exclude.IsEmpty)
        {
            return raw;
        }

        collections ??= new CollectionIndex();
        IReadOnlyList<QuantityUnit>? units = channels.QuantityUnits;
        if (units != null && units.Count > 0)
        {
            int kept = 0;
            for (int i = 0; i < units.Count; i++)
            {
                QuantityUnit unit = units[i];
                if (exclude.Matches(unit.Code, collections))
                {
                    continue;
                }

                kept += Math.Max(0, unit.Count);
            }

            return kept;
        }

        if (exclude.Matches(target, collections))
        {
            return 0;
        }

        return raw;
    }

    static void PickMeasure(
        XpPayChannel pay,
        Channels channels,
        out float value,
        out float min,
        out float max)
    {
        value = 0f;
        min = 0f;
        max = 0f;

        switch (pay)
        {
            case XpPayChannel.Resistance when channels.HasResistance:
                value = channels.Resistance;
                min = channels.ResistanceMin;
                max = channels.ResistanceMax;
                return;
            case XpPayChannel.Voxels when channels.HasVoxels:
                value = channels.Voxels;
                min = channels.VoxelsMin;
                max = channels.VoxelsMax;
                return;
            case XpPayChannel.Ingredients when channels.HasIngredients:
                value = channels.Ingredients;
                min = AmountTableMath.IngredientsMin;
                max = AmountTableMath.IngredientsMax;
                return;
        }

        // Selected but empty → missing metric → AmountTableMath uses index 0.
    }

    static bool IsResistanceDomain(string? domain) =>
        domain != null
        && (domain.Equals(BlockBreakHardnessCatalog.DomainDig, StringComparison.OrdinalIgnoreCase)
            || domain.Equals(BlockBreakHardnessCatalog.DomainMine, StringComparison.OrdinalIgnoreCase)
            || domain.Equals(BlockBreakHardnessCatalog.DomainChop, StringComparison.OrdinalIgnoreCase));

    static bool IsVoxelsDomain(string? domain) =>
        domain != null
        && domain.Equals(MetricDomainClayVoxels, StringComparison.OrdinalIgnoreCase);

    static IReadOnlyList<string> NormalizeInputs(IReadOnlyList<string>? inputs)
    {
        if (inputs == null || inputs.Count == 0)
        {
            return Array.Empty<string>();
        }

        List<string> list = new(inputs.Count);
        for (int i = 0; i < inputs.Count; i++)
        {
            if (!string.IsNullOrWhiteSpace(inputs[i]))
            {
                list.Add(inputs[i].Trim());
            }
        }

        return list;
    }

    static IReadOnlySet<string> CanonicalizeTokens(IReadOnlySet<string>? tokens)
    {
        if (tokens == null || tokens.Count == 0)
        {
            return tokens ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        HashSet<string> set = new(StringComparer.OrdinalIgnoreCase);
        foreach (string raw in tokens)
        {
            if (!string.IsNullOrWhiteSpace(raw))
            {
                set.Add(DeedTokenTags.Canonical(raw));
            }
        }

        return set;
    }

    static HashSet<string> BuildTokenSet(IReadOnlyList<string>? tokens)
    {
        HashSet<string> set = new(StringComparer.OrdinalIgnoreCase);
        if (tokens == null)
        {
            return set;
        }

        foreach (string? raw in tokens)
        {
            if (!string.IsNullOrWhiteSpace(raw))
            {
                set.Add(DeedTokenTags.Canonical(raw));
            }
        }

        return set;
    }

    static void ResolveMetricRange(
        ProsequorModSystem? mod,
        string? metricDomain,
        float? metricMin,
        float? metricMax,
        out float min,
        out float max)
    {
        if (metricMin.HasValue && metricMax.HasValue)
        {
            min = metricMin.Value;
            max = metricMax.Value;
            return;
        }

        min = 0f;
        max = 0f;
        if (mod == null || string.IsNullOrWhiteSpace(metricDomain))
        {
            return;
        }

        string domain = metricDomain.Trim();
        if (mod.BlockBreakHardness != null
            && mod.BlockBreakHardness.TryGetRange(domain, out min, out max))
        {
            return;
        }

        if (domain.Equals(MetricDomainClayVoxels, StringComparison.OrdinalIgnoreCase)
            && mod.ClayFormingRecipes != null)
        {
            min = mod.ClayFormingRecipes.MinVoxelsPerUnit;
            max = mod.ClayFormingRecipes.MaxVoxelsPerUnit;
        }
    }
}
