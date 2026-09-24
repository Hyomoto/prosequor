using Prosequor.Ability;
using Prosequor.Ability.Hooks;
using Prosequor.Xp;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;

namespace Prosequor.Xp.Activity;

/// <summary>
/// Fire-and-forget discrete amount XP. Callsites pass who, the token, role identities,
/// and output/input unit lists. Resistance, crop lifetime, growth stages, catalog ranges,
/// voxels-per-unit (from <c>subject</c>), and list sums are resolved here.
/// Pays through <see cref="FatherXp"/> (online or mailbox). No watcher involvement.
/// </summary>
public static class Deed
{
    public const string Activity = "prosequor:deed";

    /// <summary>Metric domain for clay fire amount tables (voxels per unit).</summary>
    public const string MetricDomainClayVoxels = "clay-voxels";

    /// <summary>Metric domain for crop growth lifetime amount tables (growth days).</summary>
    public const string MetricDomainCropLifetime = "crop-lifetime";

    public readonly record struct PlannedPay(string SkillId, float Amount, AbilityAction Fact);

    /// <summary>One countable unit (code + count) for <c>include</c> / <c>exclude</c> filtering.</summary>
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
        IReadOnlyList<QuantityUnit>? IngredientUnits = null,
        IReadOnlyList<ContributorShare>? Contributors = null,
        string? MakerUid = null,
        string? SelectedContributorUid = null,
        float Lifetime = 0f,
        float LifetimeMin = 0f,
        float LifetimeMax = 0f,
        bool HasLifetime = false,
        int GrowthStages = 0);

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
        BlockPos? position = null,
        IReadOnlyList<QuantityUnit>? outputs = null,
        IReadOnlyList<QuantityUnit>? inputs = null,
        IReadOnlyList<ContributorShare>? contributors = null,
        string? makerUid = null,
        string? selectedContributorUid = null,
        ItemStack? subject = null,
        string? activity = null,
        XpAwardMode mode = XpAwardMode.Earn) =>
        Emit(
            api,
            playerUid,
            [token.ToTag()],
            caller,
            target,
            mount,
            ground,
            lastCraft,
            position,
            outputs,
            inputs,
            contributors,
            makerUid,
            selectedContributorUid,
            subject,
            activity,
            mode);

    public static void Emit(
        ICoreAPI api,
        string playerUid,
        IReadOnlyList<DeedToken> tokens,
        string? caller = null,
        string? target = null,
        string? mount = null,
        string? ground = null,
        string? lastCraft = null,
        BlockPos? position = null,
        IReadOnlyList<QuantityUnit>? outputs = null,
        IReadOnlyList<QuantityUnit>? inputs = null,
        IReadOnlyList<ContributorShare>? contributors = null,
        string? makerUid = null,
        string? selectedContributorUid = null,
        ItemStack? subject = null,
        string? activity = null,
        XpAwardMode mode = XpAwardMode.Earn)
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
            position,
            outputs,
            inputs,
            contributors,
            makerUid,
            selectedContributorUid,
            subject,
            activity,
            mode);
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
        BlockPos? position = null,
        IReadOnlyList<QuantityUnit>? outputs = null,
        IReadOnlyList<QuantityUnit>? inputs = null,
        IReadOnlyList<ContributorShare>? contributors = null,
        string? makerUid = null,
        string? selectedContributorUid = null,
        ItemStack? subject = null,
        string? activity = null,
        XpAwardMode mode = XpAwardMode.Earn)
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
            position,
            outputs,
            inputs,
            contributors,
            makerUid,
            selectedContributorUid,
            subject,
            activity,
            mode);
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
        BlockPos? position = null,
        IReadOnlyList<QuantityUnit>? outputs = null,
        IReadOnlyList<QuantityUnit>? inputs = null,
        IReadOnlyList<ContributorShare>? contributors = null,
        string? makerUid = null,
        string? selectedContributorUid = null,
        ItemStack? subject = null,
        string? activity = null,
        XpAwardMode mode = XpAwardMode.Earn)
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

        string? resolvedTarget = string.IsNullOrWhiteSpace(target)
            ? EventFactBuilder.CodeOf(subject)
            : target.Trim();

        Channels channels = ResolveChannels(
            api,
            mod,
            tokenSet,
            resolvedTarget,
            subject,
            outputs,
            inputs,
            shares,
            maker,
            selected);

        Deliver(
            father,
            rules,
            mod?.Collections?.Index ?? new CollectionIndex(),
            actor,
            tokenSet,
            caller,
            resolvedTarget,
            mount,
            ground,
            lastCraft,
            channels,
            position,
            activity,
            CodesOf(inputs),
            mode);
    }

    /// <summary>
    /// Legacy <see cref="XpAction"/> entry. The action already carries a resolved sample
    /// (no world lookup). Activity is forced to <see cref="Activity"/>.
    /// </summary>
    public static void Emit(ICoreAPI api, XpAction action)
    {
        if (api == null
            || api.Side != EnumAppSide.Server
            || action == null
            || string.IsNullOrWhiteSpace(action.ActorUid))
        {
            return;
        }

        ProsequorModSystem? mod = ProsequorModSystem.For(api);
        FatherXp? father = mod?.FatherXp;
        IXpRuleRegistry? rules = mod?.XpRules;
        if (father == null || rules == null)
        {
            return;
        }

        int quantity = action.CraftCount > 0 ? action.CraftCount : 1;
        ResolveMetricRange(mod, action.HardnessDomain, metricMin: null, metricMax: null, out float min, out float max);
        Channels channels = BuildChannels(
            action.Hardness,
            min,
            max,
            action.HardnessDomain,
            action.TotalUnits,
            quantity);

        Deliver(
            father,
            rules,
            mod?.Collections?.Index ?? new CollectionIndex(),
            action.ActorUid.Trim(),
            BuildTokenSet(action.Tokens.Count > 0 ? action.Tokens.ToList() : Array.Empty<string>()),
            action.Held,
            action.Target,
            action.Mount,
            action.Ground,
            action.LastCraft,
            channels,
            action.Position,
            activity: null,
            inputs: null,
            XpAwardMode.Earn);
    }

    static void Deliver(
        FatherXp father,
        IXpRuleRegistry rules,
        CollectionIndex collections,
        string actor,
        IReadOnlySet<string> tokens,
        string? caller,
        string? target,
        string? mount,
        string? ground,
        string? lastCraft,
        Channels channels,
        BlockPos? position,
        string? activity,
        IReadOnlyList<string>? inputs,
        XpAwardMode mode)
    {
        IReadOnlyList<PlannedPay> pays = PlanPays(
            rules,
            collections,
            actor,
            tokens,
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

    /// <summary>
    /// Pure match/pay planning for tests. There is no world here, so resistance, lifetime,
    /// ranges, and counts are passed in. <see cref="Emit"/> reads those itself.
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
        string? metricDomain = null,
        int growthStages = 0) =>
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
            BuildChannels(metric, metricMin, metricMax, metricDomain, totalUnits, craftCount, growthStages: growthStages),
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
    /// Map an already-resolved sample into named channels (tests / PlanPays).
    /// dig/mine/chop → resistance; clay-voxels → voxels; crop-lifetime → lifetime;
    /// totalUnits → ingredients measure; craftCount → quantity when
    /// <paramref name="quantityUnits"/> is empty (otherwise the stacks are summed).
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
        string? selectedContributorUid = null,
        int growthStages = 0,
        IReadOnlyList<QuantityUnit>? ingredientUnits = null)
    {
        int quantity = craftCount > 0 ? craftCount : 1;
        int ingredients = Math.Max(0, totalUnits);
        bool hasResistance = false;
        bool hasVoxels = false;
        bool hasLifetime = false;
        float resistance = 0f;
        float resistanceMin = 0f;
        float resistanceMax = 0f;
        float voxels = 0f;
        float voxelsMin = 0f;
        float voxelsMax = 0f;
        float lifetime = 0f;
        float lifetimeMin = 0f;
        float lifetimeMax = 0f;

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
        else if (IsLifetimeDomain(domain))
        {
            hasLifetime = true;
            lifetime = metric;
            lifetimeMin = metricMin;
            lifetimeMax = metricMax;
        }
        else if (domain == null && (metric > 0f || metricMax > metricMin))
        {
            // Test / explicit-range convenience: unlabeled metric is resistance.
            hasResistance = true;
            resistance = metric;
            resistanceMin = metricMin;
            resistanceMax = metricMax;
        }

        int outputSum = SumCounts(quantityUnits);
        if (outputSum > 0)
        {
            quantity = outputSum;
        }

        int inputSum = SumCounts(ingredientUnits);
        if (inputSum > 0)
        {
            ingredients = inputSum;
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
            HasIngredients: ingredients > 0 || (ingredientUnits != null && ingredientUnits.Count > 0),
            quantityUnits,
            ingredientUnits,
            shares.Count > 0 ? shares : null,
            NormalizeUid(makerUid),
            NormalizeUid(selectedContributorUid),
            lifetime,
            lifetimeMin,
            lifetimeMax,
            hasLifetime,
            Math.Max(0, growthStages));
    }

    static Channels ResolveChannels(
        ICoreAPI api,
        ProsequorModSystem? mod,
        IReadOnlySet<string> tokens,
        string? target,
        ItemStack? subject,
        IReadOnlyList<QuantityUnit>? outputs,
        IReadOnlyList<QuantityUnit>? inputs,
        IReadOnlyList<ContributorShare> shares,
        string? maker,
        string? selected)
    {
        float resistance = 0f;
        float resistanceMin = 0f;
        float resistanceMax = 0f;
        bool hasResistance = false;
        float voxels = 0f;
        float voxelsMin = 0f;
        float voxelsMax = 0f;
        bool hasVoxels = false;
        float lifetime = 0f;
        float lifetimeMin = 0f;
        float lifetimeMax = 0f;
        bool hasLifetime = false;
        int growthStages = 0;

        if (TryMeasureBlockBreak(api, mod, target, out float r, out float rMin, out float rMax))
        {
            hasResistance = true;
            resistance = r;
            resistanceMin = rMin;
            resistanceMax = rMax;
        }
        else if (TryMeasureAnimalWeight(api, mod, target, out float w, out float wMin, out float wMax))
        {
            hasResistance = true;
            resistance = w;
            resistanceMin = wMin;
            resistanceMax = wMax;
        }

        if (TryMeasureCropLifetime(api, mod, target, out float days, out float dMin, out float dMax, out int stages))
        {
            hasLifetime = true;
            lifetime = days;
            lifetimeMin = dMin;
            lifetimeMax = dMax;
            growthStages = stages;
        }

        if (TryMeasureVoxels(mod, subject, out float v, out float vMin, out float vMax))
        {
            hasVoxels = true;
            voxels = v;
            voxelsMin = vMin;
            voxelsMax = vMax;
        }

        int outputSum = SumCounts(outputs);
        int quantity = outputSum > 0 ? outputSum : 1;
        int ingredients = SumCounts(inputs);

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
            HasIngredients: ingredients > 0 || (inputs != null && inputs.Count > 0),
            outputs,
            inputs,
            shares.Count > 0 ? shares : null,
            maker,
            selected,
            lifetime,
            lifetimeMin,
            lifetimeMax,
            hasLifetime,
            growthStages);
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
        collections ??= new CollectionIndex();
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
        else if (XpPayChannels.UsesIngredients(pay))
        {
            int ingredients = ResolveIngredients(rule, channels, collections);
            if (ingredients <= 0)
            {
                return 0f;
            }

            return AmountTableMath.ResolveAmount(
                rule.Amount,
                rule.AmountTable,
                ingredients,
                AmountTableMath.IngredientsMin,
                AmountTableMath.IngredientsMax);
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

        if (XpPayChannels.UsesLifetime(pay))
        {
            int stages = Math.Max(1, channels.GrowthStages);
            return baseAmount / stages;
        }

        if (XpPayChannels.IsFlat(pay) || !XpPayChannels.UsesQuantity(pay))
        {
            return baseAmount;
        }

        int quantity = ResolveQuantity(rule, channels, target, collections);
        if (quantity <= 0)
        {
            return 0f;
        }

        return baseAmount * quantity;
    }

    /// <summary>
    /// Apply <c>include</c> then <c>exclude</c> to output quantity.
    /// Prefer per-unit <see cref="Channels.QuantityUnits"/>;
    /// otherwise gate the whole quantity on the deed <paramref name="target"/>.
    /// </summary>
    public static int ResolveQuantity(
        XpRule rule,
        Channels channels,
        string? target,
        CollectionIndex collections)
    {
        collections ??= new CollectionIndex();
        XpQuantityExclude include = rule.Include ?? XpQuantityExclude.Empty;
        XpQuantityExclude exclude = rule.Exclude ?? XpQuantityExclude.Empty;
        IReadOnlyList<QuantityUnit>? units = channels.QuantityUnits;
        if (units != null && units.Count > 0)
        {
            return SumFiltered(units, include, exclude, collections);
        }

        int raw = channels.Quantity > 0 ? channels.Quantity : 1;
        if (!include.IsEmpty && !include.Matches(target, collections))
        {
            return 0;
        }

        if (exclude.Matches(target, collections))
        {
            return 0;
        }

        return raw;
    }

    /// <summary>
    /// Apply <c>include</c> to ingredient units. Prefer <see cref="Channels.IngredientUnits"/>;
    /// otherwise use the scalar ingredients channel (tests).
    /// </summary>
    public static int ResolveIngredients(
        XpRule rule,
        Channels channels,
        CollectionIndex collections)
    {
        collections ??= new CollectionIndex();
        XpQuantityExclude include = rule.Include ?? XpQuantityExclude.Empty;
        IReadOnlyList<QuantityUnit>? units = channels.IngredientUnits;
        if (units != null && units.Count > 0)
        {
            return SumFiltered(units, include, XpQuantityExclude.Empty, collections);
        }

        if (!include.IsEmpty)
        {
            // Scalar channel cannot satisfy a positive include.
            return 0;
        }

        return Math.Max(0, channels.Ingredients);
    }

    static int SumFiltered(
        IReadOnlyList<QuantityUnit> units,
        XpQuantityExclude include,
        XpQuantityExclude exclude,
        CollectionIndex collections)
    {
        int kept = 0;
        for (int i = 0; i < units.Count; i++)
        {
            QuantityUnit unit = units[i];
            if (!include.IsEmpty && !include.Matches(unit.Code, collections))
            {
                continue;
            }

            if (exclude.Matches(unit.Code, collections))
            {
                continue;
            }

            kept += Math.Max(0, unit.Count);
        }

        return kept;
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
            case XpPayChannel.Lifetime when channels.HasLifetime:
                value = channels.Lifetime;
                min = channels.LifetimeMin;
                max = channels.LifetimeMax;
                return;
        }

        // Selected but empty → missing metric → AmountTableMath uses index 0.
    }

    static bool IsResistanceDomain(string? domain) =>
        domain != null
        && (domain.Equals(BlockBreakHardnessCatalog.DomainDig, StringComparison.OrdinalIgnoreCase)
            || domain.Equals(BlockBreakHardnessCatalog.DomainMine, StringComparison.OrdinalIgnoreCase)
            || domain.Equals(BlockBreakHardnessCatalog.DomainChop, StringComparison.OrdinalIgnoreCase)
            || domain.Equals(AnimalWeightCatalog.MetricDomain, StringComparison.OrdinalIgnoreCase));

    static bool IsLifetimeDomain(string? domain) =>
        domain != null
        && domain.Equals(MetricDomainCropLifetime, StringComparison.OrdinalIgnoreCase);

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

    static IReadOnlyList<string> CodesOf(IReadOnlyList<QuantityUnit>? units)
    {
        if (units == null || units.Count == 0)
        {
            return Array.Empty<string>();
        }

        List<string> list = new(units.Count);
        for (int i = 0; i < units.Count; i++)
        {
            if (!string.IsNullOrWhiteSpace(units[i].Code))
            {
                list.Add(units[i].Code.Trim());
            }
        }

        return list;
    }

    static int SumCounts(IReadOnlyList<QuantityUnit>? units)
    {
        if (units == null || units.Count == 0)
        {
            return 0;
        }

        int sum = 0;
        for (int i = 0; i < units.Count; i++)
        {
            sum += Math.Max(0, units[i].Count);
        }

        return sum;
    }

    static bool TryMeasureBlockBreak(
        ICoreAPI api,
        ProsequorModSystem? mod,
        string? target,
        out float resistance,
        out float min,
        out float max)
    {
        resistance = 0f;
        min = 0f;
        max = 0f;
        Block? block = ResolveBlock(api, target);
        if (block == null)
        {
            return false;
        }

        string? classify = BlockBreakClassification.ClassifyToken(block);
        if (classify is not (BlockBreakClassification.TokenDig
            or BlockBreakClassification.TokenMine
            or BlockBreakClassification.TokenChop))
        {
            return false;
        }

        resistance = block.Resistance;
        ResolveMetricRange(mod, classify, metricMin: null, metricMax: null, out min, out max);
        return true;
    }

    static bool TryMeasureAnimalWeight(
        ICoreAPI api,
        ProsequorModSystem? mod,
        string? target,
        out float weight,
        out float min,
        out float max)
    {
        weight = 0f;
        min = 0f;
        max = 0f;
        if (string.IsNullOrWhiteSpace(target) || api?.World == null)
        {
            return false;
        }

        EntityProperties? props = api.World.GetEntityType(new AssetLocation(target.Trim()));
        if (!AnimalWeightCatalog.IsCatalogAnimal(props))
        {
            return false;
        }

        weight = props!.Weight;
        ResolveMetricRange(
            mod,
            AnimalWeightCatalog.MetricDomain,
            metricMin: null,
            metricMax: null,
            out min,
            out max);
        if (min <= 0f && max <= 0f)
        {
            min = weight;
            max = weight;
        }

        return true;
    }

    static bool TryMeasureCropLifetime(
        ICoreAPI api,
        ProsequorModSystem? mod,
        string? target,
        out float days,
        out float min,
        out float max,
        out int stages)
    {
        days = 0f;
        min = 0f;
        max = 0f;
        stages = 0;
        Block? block = ResolveBlock(api, target);
        if (block?.CropProps == null || !AbilityBootstrap.IsCropBlock(block))
        {
            return false;
        }

        float daysPerMonth = (float)(api.World?.Calendar?.DaysPerMonth ?? 0);
        days = CropLifetimeMath.TotalGrowthDays(block.CropProps, daysPerMonth);
        if (days <= 0f)
        {
            return false;
        }

        stages = CropLifetimeMath.GrowthStages(block.CropProps);
        ResolveMetricRange(mod, MetricDomainCropLifetime, metricMin: null, metricMax: null, out min, out max);
        return true;
    }

    static bool TryMeasureVoxels(
        ProsequorModSystem? mod,
        ItemStack? subject,
        out float voxels,
        out float min,
        out float max)
    {
        voxels = 0f;
        min = 0f;
        max = 0f;
        ClayFormingRecipeCatalog? catalog = mod?.ClayFormingRecipes;
        if (catalog == null
            || !ProsequorStackPedigree.TryGetRecipeKey(subject, out string? recipeKey)
            || !catalog.TryGet(recipeKey, out ClayFormingRecipeCatalog.RecipeInfo info))
        {
            return false;
        }

        voxels = info.VoxelsPerUnit;
        ResolveMetricRange(mod, MetricDomainClayVoxels, metricMin: null, metricMax: null, out min, out max);
        return true;
    }

    static Block? ResolveBlock(ICoreAPI api, string? code)
    {
        if (api.World == null || string.IsNullOrWhiteSpace(code))
        {
            return null;
        }

        Block? block = api.World.GetBlock(new AssetLocation(code.Trim()));
        if (block == null || block.Id == 0)
        {
            return null;
        }

        return block;
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

        if (domain.Equals(AnimalWeightCatalog.MetricDomain, StringComparison.OrdinalIgnoreCase)
            && mod.AnimalWeight != null
            && mod.AnimalWeight.TryGetRange(out min, out max))
        {
            return;
        }

        if (domain.Equals(MetricDomainClayVoxels, StringComparison.OrdinalIgnoreCase)
            && mod.ClayFormingRecipes != null)
        {
            min = mod.ClayFormingRecipes.MinVoxelsPerUnit;
            max = mod.ClayFormingRecipes.MaxVoxelsPerUnit;
            return;
        }

        if (domain.Equals(MetricDomainCropLifetime, StringComparison.OrdinalIgnoreCase)
            && mod.CropLifetime != null
            && mod.CropLifetime.TryGetRange(out min, out max))
        {
            return;
        }
    }
}
