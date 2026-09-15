using System;
using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;

namespace Prosequor.Ability;

/// <summary>
/// Animal friendliness score and Husbandry fear formulas.
/// Score is the sum of pedigree contributor weights on the entity Live blob
/// (care attribution). Legacy flat <c>prosequorFriendliness</c> is promoted on read.
/// </summary>
public static class HusbandryFriendliness
{
    /// <summary>Legacy flat score (compat-read / promote only).</summary>
    public const string AttrKey = "prosequorFriendliness";

    /// <summary>
    /// Sentinel contributor for unattributed care (empty trough bag, admin Set).
    /// </summary>
    public const string AnonContributorUid = "@friendliness";

    public const string SkillId = "husbandry";
    public const int EffectCap = 100;

    /// <summary>Pipeline seed / no-unlock multiplier as percent (100 = ×1).</summary>
    public const int MultSeedPercent = 100;

    /// <summary>Pet / deed gate: friendliness score greater than 5.</summary>
    public const int FriendlyThreshold = 5;

    /// <summary>
    /// Chance to refresh favorite seraph (<c>MakerUid</c>) when friendliness was already
    /// above <see cref="FriendlyThreshold"/> and a point is gained.
    /// </summary>
    public const float FavoriteSeraphChance = 0.05f;

    /// <summary>
    /// Calendar-hour step for the post-gain friendliness gate
    /// (<c>step × random 1..6</c> → 4–24 hours).
    /// </summary>
    public const double GainCooldownStepHours = 4.0;

    /// <summary>Inclusive min multiplier for <see cref="GainCooldownStepHours"/>.</summary>
    public const int GainCooldownStepsMin = 1;

    /// <summary>Inclusive max multiplier for <see cref="GainCooldownStepHours"/>.</summary>
    public const int GainCooldownStepsMax = 6;

    /// <summary>True when <see cref="Get"/> exceeds <see cref="FriendlyThreshold"/>.</summary>
    public static bool IsFriendly(Entity? entity) =>
        entity != null && Get(entity) > FriendlyThreshold;

    /// <summary>Favorite seraph = Live blob maker UID when set to a real player.</summary>
    public static bool TryGetFavoriteSeraph(Entity? entity, out string? playerUid)
    {
        playerUid = null;
        if (entity == null
            || !ProsequorEntityPedigreeStation.TryGetBlob(entity, out ProsequorBlob blob)
            || string.IsNullOrEmpty(blob.MakerUid)
            || IsSentinelUid(blob.MakerUid))
        {
            return false;
        }

        playerUid = blob.MakerUid;
        return true;
    }

    /// <summary>
    /// Returns <paramref name="baseTokens"/> plus <c>friendly</c> when the animal qualifies.
    /// </summary>
    public static string[] WithFriendlyToken(Entity? entity, params string[] baseTokens)
    {
        if (baseTokens == null || baseTokens.Length == 0)
        {
            return IsFriendly(entity)
                ? [Xp.Activity.DeedTokenTags.Friendly]
                : [];
        }

        if (!IsFriendly(entity))
        {
            return baseTokens;
        }

        string[] next = new string[baseTokens.Length + 1];
        Array.Copy(baseTokens, next, baseTokens.Length);
        next[^1] = Xp.Activity.DeedTokenTags.Friendly;
        return next;
    }

    public static int Get(Entity entity)
    {
        if (entity?.WatchedAttributes == null)
        {
            return 0;
        }

        if (entity.World?.Side == EnumAppSide.Server)
        {
            PromoteLegacyFlat(entity);
        }

        if (ProsequorEntityPedigreeStation.TryGetBlob(entity, out ProsequorBlob blob))
        {
            return TotalWeight(blob);
        }

        // Client / pre-promote: fall back to legacy flat.
        return Math.Max(0, entity.WatchedAttributes.GetInt(AttrKey, 0));
    }

    /// <summary>
    /// Replace score with <paramref name="value"/> (admin / tests). Preserves maker;
    /// contributor bag becomes a single <see cref="AnonContributorUid"/> share.
    /// </summary>
    public static void Set(Entity entity, int value)
    {
        if (entity?.WatchedAttributes == null || entity.World?.Side != EnumAppSide.Server)
        {
            return;
        }

        ClearLegacyFlat(entity);
        int clamped = Math.Max(0, value);
        string? maker = null;
        if (ProsequorEntityPedigreeStation.TryGetBlob(entity, out ProsequorBlob current)
            && !string.IsNullOrEmpty(current.MakerUid))
        {
            maker = current.MakerUid;
        }

        ProsequorEntityPedigreeStation.Clear(entity);
        if (maker != null)
        {
            ProsequorEntityPedigreeStation.StampMaker(entity, maker);
        }

        if (clamped > 0)
        {
            ProsequorEntityPedigreeStation.AddContributor(entity, AnonContributorUid, clamped);
        }
    }

    /// <summary>
    /// Player-attack step-down. Score above <see cref="FriendlyThreshold"/> drops to that
    /// threshold (favorite kept). Score at or below drops to 0. When the result is 0 and
    /// <paramref name="attackerUid"/> is the Favorite Seraph, that maker is cleared.
    /// </summary>
    public static void ApplyAttackPenalty(Entity entity, string? attackerUid)
    {
        if (entity?.WatchedAttributes == null || entity.World?.Side != EnumAppSide.Server)
        {
            return;
        }

        if (Get(entity) > FriendlyThreshold)
        {
            Set(entity, FriendlyThreshold);
            return;
        }

        Set(entity, 0);
        if (string.IsNullOrWhiteSpace(attackerUid)
            || !TryGetFavoriteSeraph(entity, out string? favorite)
            || !string.Equals(favorite, attackerUid.Trim(), StringComparison.Ordinal))
        {
            return;
        }

        ProsequorEntityPedigreeStation.ClearMaker(entity);
    }

    /// <summary>
    /// True when the animal may gain friendliness now. Missing stamp → ready-at 0 → always true.
    /// </summary>
    public static bool CanGain(Entity? entity, double? nowTotalHours = null)
    {
        if (entity?.WatchedAttributes == null)
        {
            return false;
        }

        double now = nowTotalHours
            ?? entity.World?.Calendar?.TotalHours
            ?? 0;
        double readyAt = TryGetFriendlinessReadyAt(entity, out double stamped) ? stamped : 0;
        return now >= readyAt;
    }

    /// <summary>Live pedigree friendliness-ready <c>TotalHours</c>, or false when absent/≤0.</summary>
    public static bool TryGetFriendlinessReadyAt(Entity? entity, out double totalHours)
    {
        totalHours = 0;
        if (entity == null
            || !ProsequorEntityPedigreeStation.TryGetBlob(entity, out ProsequorBlob blob)
            || blob.FriendlinessReadyAtTotalHours <= 0)
        {
            return false;
        }

        totalHours = blob.FriendlinessReadyAtTotalHours;
        return true;
    }

    /// <summary>
    /// Sets friendliness-ready <c>TotalHours</c> on the Live blob (tests / admin).
    /// ≤0 clears the stamp. No-op when the blob is anonymous.
    /// </summary>
    public static void StampFriendlinessReadyAt(Entity? entity, double totalHours)
    {
        if (entity?.WatchedAttributes == null
            || entity.World?.Side != EnumAppSide.Server
            || !ProsequorEntityPedigreeStation.TryGetBlob(entity, out ProsequorBlob blob))
        {
            return;
        }

        ProsequorEntityPedigreeStation.ApplyBlob(entity, blob.WithFriendlinessReadyAt(totalHours));
    }

    /// <summary>
    /// Adds care weight for <paramref name="contributorUid"/> (default amount 1).
    /// Gated by pedigree <c>friendlinessReadyAt</c>; on success stamps
    /// <c>now + 4h × (1..6)</c>.
    /// </summary>
    public static void Add(
        Entity entity,
        string? contributorUid,
        int amount = 1,
        Random? rand = null)
    {
        if (amount <= 0
            || entity?.WatchedAttributes == null
            || entity.World?.Side != EnumAppSide.Server)
        {
            return;
        }

        PromoteLegacyFlat(entity);

        double now = entity.World.Calendar?.TotalHours ?? 0;
        if (!CanGain(entity, now))
        {
            return;
        }

        // Favorite roll requires friendliness already above threshold before this gain.
        int before = Get(entity);

        string? uid = string.IsNullOrWhiteSpace(contributorUid)
            ? AnonContributorUid
            : contributorUid.Trim();

        ProsequorBlob current = ProsequorEntityPedigreeStation.TryGetBlob(entity, out ProsequorBlob blob)
            ? blob
            : ProsequorBlob.Empty;

        Random rng = rand ?? entity.World.Rand ?? Random.Shared;
        int steps = rng.Next(GainCooldownStepsMin, GainCooldownStepsMax + 1);
        double readyAt = now + GainCooldownStepHours * steps;

        ProsequorEntityPedigreeStation.ApplyBlob(
            entity,
            current.WithContributor(uid, amount).WithFriendlinessReadyAt(readyAt));

        if (before > FriendlyThreshold)
        {
            MaybeRefreshFavoriteSeraph(entity, rng);
        }
    }

    /// <summary>
    /// Favorite-seraph refresh after a friendliness gain that was already above threshold:
    /// roll <see cref="FavoriteSeraphChance"/> → on pass, if there is no pickable
    /// contributor leave <c>MakerUid</c> unchanged → else stamp the highest-weight real
    /// contributor (ties broken at random). Sentinels such as
    /// <see cref="AnonContributorUid"/> are not pickable.
    /// </summary>
    public static void MaybeRefreshFavoriteSeraph(Entity entity, Random? rand = null)
    {
        if (entity?.WatchedAttributes == null || entity.World?.Side != EnumAppSide.Server)
        {
            return;
        }

        Random rng = rand ?? entity.World.Rand ?? Random.Shared;
        if (rng.NextDouble() >= FavoriteSeraphChance)
        {
            return;
        }

        // Pass → empty / sentinel-only contributors → keep current favorite.
        if (!TryPickTopContributor(entity, rng, out string? favorite) || favorite == null)
        {
            return;
        }

        ProsequorEntityPedigreeStation.StampMaker(entity, favorite);
    }

    /// <summary>
    /// Highest-weight real contributor; ties broken with <paramref name="rand"/>.
    /// False when there are no pickable contributors (empty or sentinel-only).
    /// </summary>
    public static bool TryPickTopContributor(Entity entity, Random rand, out string? playerUid)
    {
        playerUid = null;
        if (!ProsequorEntityPedigreeStation.TryGetBlob(entity, out ProsequorBlob blob)
            || blob.Contributors.Count == 0)
        {
            return false;
        }

        int best = 0;
        List<string> tied = new();
        for (int i = 0; i < blob.Contributors.Count; i++)
        {
            ProsequorBlob.Share share = blob.Contributors[i];
            if (share.Weight <= 0 || IsSentinelUid(share.PlayerUid))
            {
                continue;
            }

            if (share.Weight > best)
            {
                best = share.Weight;
                tied.Clear();
                tied.Add(share.PlayerUid);
            }
            else if (share.Weight == best)
            {
                tied.Add(share.PlayerUid);
            }
        }

        if (tied.Count == 0)
        {
            return false;
        }

        playerUid = tied[rand.Next(tied.Count)];
        return true;
    }

    static bool IsSentinelUid(string? uid) =>
        !string.IsNullOrEmpty(uid)
        && (uid[0] == '@'
            || string.Equals(uid, AnonContributorUid, StringComparison.OrdinalIgnoreCase));

    /// <summary>Friendliness contribution in 0..1 (score clamped to <see cref="EffectCap"/>).</summary>
    public static float FriendlinessFactor(int friendliness) =>
        GameMath.Clamp(friendliness, 0, EffectCap) / (float)EffectCap;

    /// <summary>
    /// Scales vanilla generation-based fear/aggression/brood factor by friendliness × player mult.
    /// <c>factor' = vanilla * (1 - min(1, f * mult))</c>.
    /// </summary>
    public static float ScaleFearReductionFactor(float vanillaFactor, int friendliness, float mult)
    {
        if (vanillaFactor <= 0f || mult <= 0f)
        {
            return vanillaFactor;
        }

        float f = FriendlinessFactor(friendliness);
        float calm = GameMath.Clamp(f * mult, 0f, 1f);
        return vanillaFactor * (1f - calm);
    }

    /// <summary>Scales milking rejection chance: <c>aggro' = aggro * (1 - min(1, f * mult))</c>.</summary>
    public static float ScaleMilkingAggroChance(float aggroChance, int friendliness, float mult)
    {
        if (aggroChance <= 0f || mult <= 0f)
        {
            return aggroChance;
        }

        float f = FriendlinessFactor(friendliness);
        float calm = GameMath.Clamp(f * mult, 0f, 1f);
        return GameMath.Clamp(aggroChance * (1f - calm), 0f, 1f);
    }

    /// <summary>
    /// Passive: flee chance × (1 - min(rule reduction fraction, friendliness as percent)).
    /// </summary>
    public static float ScalePassiveFleeChance(float chance, float reductionFraction, int friendliness)
    {
        if (chance <= 0f || reductionFraction <= 0f)
        {
            return chance;
        }

        float reduction = Math.Min(reductionFraction, FriendlinessFactor(friendliness));
        return GameMath.Clamp(chance * (1f - reduction), 0f, 1f);
    }

    public static float MultFromPercent(int percent) =>
        Math.Max(0, percent) / 100f;

    static int TotalWeight(ProsequorBlob blob)
    {
        int total = 0;
        for (int i = 0; i < blob.Contributors.Count; i++)
        {
            total += Math.Max(0, blob.Contributors[i].Weight);
        }

        return total;
    }

    static void PromoteLegacyFlat(Entity entity)
    {
        if (!entity.WatchedAttributes.HasAttribute(AttrKey))
        {
            return;
        }

        int legacy = Math.Max(0, entity.WatchedAttributes.GetInt(AttrKey, 0));
        ClearLegacyFlat(entity);
        if (legacy <= 0)
        {
            return;
        }

        // Avoid double-counting when Live already has shares.
        if (ProsequorEntityPedigreeStation.TryGetBlob(entity, out ProsequorBlob existing)
            && TotalWeight(existing) > 0)
        {
            return;
        }

        ProsequorEntityPedigreeStation.AddContributor(entity, AnonContributorUid, legacy);
    }

    static void ClearLegacyFlat(Entity entity)
    {
        if (!entity.WatchedAttributes.HasAttribute(AttrKey))
        {
            return;
        }

        entity.WatchedAttributes.RemoveAttribute(AttrKey);
        entity.WatchedAttributes.MarkPathDirty(AttrKey);
    }
}
