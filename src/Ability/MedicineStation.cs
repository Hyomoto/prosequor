using System;
using Prosequor.Ability.Hooks;
using Prosequor.Player;
using Prosequor.Xp.Activity;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.GameContent;

namespace Prosequor.Ability;

/// <summary>
/// Medicine folds: bleed-out window, healing-item tend health / application rate,
/// triage holdback after revive, and unlock-gated grid recipes.
/// </summary>
public static class MedicineStation
{
    public const string TokenOther = "other";
    public const string RecipeUnlockAttr = "prosequorUnlock";
    public const int TriageTicks = 4;

    /// <summary>
    /// Remaining downed hours: <c>baseHours * rate - hoursDead</c>.
    /// <paramref name="rate"/> is the bleed-out fold (seed 1).
    /// </summary>
    public static double ScaleBleedOutHoursLeft(double baseHours, double hoursDead, float rate)
    {
        float safeRate = rate > 0f ? rate : 1f;
        return baseHours * safeRate - hoursDead;
    }

    /// <summary>
    /// Heal total after stack regen factor and optional First Aid tend scale.
    /// <paramref name="tendRate"/> is the tend / health fold (seed 1 when tending other).
    /// </summary>
    public static float FoldHealTotal(float typeHealth, float regenFactor, float tendRate)
    {
        float regen = regenFactor > 0f ? regenFactor : 1f;
        float tend = tendRate > 0f ? tendRate : 1f;
        return typeHealth * regen * tend;
    }

    /// <summary>
    /// Application seconds after First Aid rate fold. Seed rate 1 → unchanged.
    /// </summary>
    public static float FoldApplicationSeconds(float baseSeconds, float applicationRate)
    {
        float rate = applicationRate > 0f ? applicationRate : 1f;
        return baseSeconds / rate;
    }

    /// <summary>
    /// Post-revive holdback: patient starts at <c>max * (1 - healthFraction)</c>,
    /// then regenerates <c>max * healthFraction</c> over <paramref name="durationSec"/>.
    /// </summary>
    public static void ResolveTriageHoldback(
        float maxHealth,
        float healthFraction,
        float durationSec,
        out float startHealth,
        out float regenTotal,
        out float duration)
    {
        float fraction = Math.Clamp(healthFraction, 0f, 1f);
        startHealth = maxHealth * (1f - fraction);
        regenTotal = maxHealth * fraction;
        duration = Math.Max(0f, durationSec);
    }

    /// <summary>
    /// Fraction of heal capacity that can land: missing HP / heal total, clamped to [0, 1].
    /// Full health or zero capacity → 0.
    /// </summary>
    public static float UsableHealFraction(float missingHealth, float healTotal)
    {
        if (healTotal <= 0f || missingHealth <= 0f)
        {
            return 0f;
        }

        return Math.Clamp(missingHealth / healTotal, 0f, 1f);
    }

    /// <summary>
    /// Pay medicine XP for a healing-item apply. <paramref name="usableFraction"/> is the
    /// effort metric on [0, 1] (how much of the heal capacity can land).
    /// </summary>
    public static void EmitHealed(
        IPlayer caregiver,
        ItemStack? stack,
        Entity? patient,
        float usableFraction)
    {
        if (caregiver?.PlayerUID == null || caregiver.Entity?.Api == null)
        {
            return;
        }

        if (usableFraction <= 0f)
        {
            return;
        }

        string? caller = stack?.Collectible?.Code?.ToString();
        string? target = patient?.Code?.ToString();
        Deed.Emit(
            caregiver.Entity.Api,
            caregiver.PlayerUID,
            DeedToken.Healed,
            caller: caller,
            target: target,
            metric: Math.Clamp(usableFraction, 0f, 1f),
            metricMin: 0f,
            metricMax: 1f);
    }

    /// <summary>Bleed-out rate fold for the wounded player (seed 1).</summary>
    public static float ResolveBleedOutRate(IPlayer player)
    {
        if (player?.Entity == null)
        {
            return 1f;
        }

        ProsequorModSystem? mod = ProsequorModSystem.For(player.Entity.Api);
        if (mod?.Pipeline == null)
        {
            return 1f;
        }

        IPlayerProgress? progress = ProsequorModSystem.GetProgress(player);
        if (progress == null)
        {
            return 1f;
        }

        AbilityAction fact = EventFactBuilder.ForPlayer(
            player,
            VerbIds.BleedOut.Value,
            includeLastCraft: false);

        PlayerInteractionContext context = new()
        {
            Player = player,
            Progress = progress,
            Fact = fact,
            BaseValue = 1f
        };

        float rate = mod.Pipeline.Run(
            HookIds.PlayerInteraction,
            VerbIds.BleedOut,
            HookIds.Rate,
            context,
            1f);
        return rate > 0f ? rate : 1f;
    }

    /// <summary>
    /// Fold heal total for a healing-item application. Tend scale only when
    /// <paramref name="patient"/> is another player.
    /// </summary>
    public static float ResolveHealTotal(
        IPlayer caregiver,
        ItemStack? stack,
        float typeHealth,
        Entity? patient)
    {
        float regen = stack == null
            ? 1f
            : CraftAttributeMods.GetFactor(stack, RegenAttributeMutator.KeyName);
        bool other = IsOtherPlayer(caregiver, patient);
        float tendRate = other ? ResolveTendHealthRate(caregiver) : 1f;
        return FoldHealTotal(typeHealth, regen, tendRate);
    }

    /// <summary>Tend / health fold (seed 1). Emits token <c>other</c>.</summary>
    public static float ResolveTendHealthRate(IPlayer caregiver) =>
        ResolveTendFloat(caregiver, HookIds.Health, 1f);

    /// <summary>Tend / application-rate fold (seed 1). Emits token <c>other</c>.</summary>
    public static float ResolveTendApplicationRate(IPlayer caregiver) =>
        ResolveTendFloat(caregiver, HookIds.ApplicationRate, 1f);

    /// <summary>
    /// Application seconds when tending another player; otherwise returns
    /// <paramref name="baseSeconds"/>.
    /// </summary>
    public static float ResolveApplicationSeconds(
        IPlayer caregiver,
        float baseSeconds,
        Entity? patient)
    {
        if (!IsOtherPlayer(caregiver, patient))
        {
            return baseSeconds;
        }

        float rate = ResolveTendApplicationRate(caregiver);
        return FoldApplicationSeconds(baseSeconds, rate);
    }

    /// <summary>
    /// After a successful revive, hold back Y% of max health and start a heal DoT.
    /// No-op when Triage health fold is 0.
    /// </summary>
    public static void ApplyTriageAfterRevive(IPlayer reviver, Entity patient)
    {
        if (reviver?.Entity == null
            || patient == null
            || patient.World?.Side != EnumAppSide.Server)
        {
            return;
        }

        ProsequorModSystem? mod = ProsequorModSystem.For(reviver.Entity.Api);
        if (mod?.Pipeline == null)
        {
            return;
        }

        IPlayerProgress? progress = ProsequorModSystem.GetProgress(reviver);
        if (progress == null)
        {
            return;
        }

        AbilityAction fact = EventFactBuilder.ForPlayer(
            reviver,
            VerbIds.Revive.Value,
            includeLastCraft: false);

        ReviveContext context = new()
        {
            Player = reviver,
            Progress = progress,
            Fact = fact,
            World = patient.World
        };

        float healthFraction = mod.Pipeline.Run(
            HookIds.ItemInteraction,
            VerbIds.Revive,
            HookIds.Health,
            context,
            0f);
        if (healthFraction <= 0f)
        {
            return;
        }

        float durationSec = mod.Pipeline.Run(
            HookIds.ItemInteraction,
            VerbIds.Revive,
            HookIds.Duration,
            context,
            0f);
        if (durationSec <= 0f)
        {
            return;
        }

        EntityBehaviorHealth? health = patient.GetBehavior<EntityBehaviorHealth>();
        if (health == null || health.MaxHealth <= 0f)
        {
            return;
        }

        ResolveTriageHoldback(
            health.MaxHealth,
            healthFraction,
            durationSec,
            out float startHealth,
            out float regenTotal,
            out float duration);

        health.Health = Math.Clamp(startHealth, 0f, health.MaxHealth);
        if (regenTotal <= 0f || duration <= 0f)
        {
            return;
        }

        patient.ReceiveDamage(
            new DamageSource
            {
                Source = EnumDamageSource.Internal,
                Type = EnumDamageType.Heal,
                DamageTier = 0,
                Duration = TimeSpan.FromSeconds(duration),
                TicksPerDuration = TriageTicks
            },
            regenTotal);
    }

    /// <summary>
    /// True when the recipe carries <c>prosequorUnlock</c> and the crafter's
    /// <c>recipe-available</c> fold is true.
    /// </summary>
    public static bool IsRecipeAvailable(IPlayer? player, GridRecipe? recipe)
    {
        if (recipe?.Attributes == null
            || !recipe.Attributes.KeyExists(RecipeUnlockAttr))
        {
            return true;
        }

        if (player?.Entity == null)
        {
            return false;
        }

        ProsequorModSystem? mod = ProsequorModSystem.For(player.Entity.Api);
        if (mod?.Pipeline == null)
        {
            return false;
        }

        IPlayerProgress? progress = ProsequorModSystem.GetProgress(player);
        if (progress == null)
        {
            return false;
        }

        AbilityAction fact = EventFactBuilder.ForPlayer(
            player,
            VerbIds.RecipeAvailable.Value,
            includeLastCraft: false);

        RecipeAvailableContext context = new()
        {
            Player = player,
            Progress = progress,
            Fact = fact
        };

        int allow = mod.Pipeline.Run(
            HookIds.CraftingInteraction,
            VerbIds.RecipeAvailable,
            HookIds.Default,
            context,
            0);
        return allow > 0;
    }

    public static bool IsOtherPlayer(IPlayer? caregiver, Entity? patient)
    {
        if (caregiver?.PlayerUID == null || patient is not EntityPlayer entityPlayer)
        {
            return false;
        }

        string? patientUid = entityPlayer.PlayerUID;
        return !string.IsNullOrWhiteSpace(patientUid)
            && !string.Equals(caregiver.PlayerUID, patientUid, StringComparison.Ordinal);
    }

    static float ResolveTendFloat(IPlayer caregiver, PhaseId phase, float seed)
    {
        if (caregiver?.Entity == null)
        {
            return seed;
        }

        ProsequorModSystem? mod = ProsequorModSystem.For(caregiver.Entity.Api);
        if (mod?.Pipeline == null)
        {
            return seed;
        }

        IPlayerProgress? progress = ProsequorModSystem.GetProgress(caregiver);
        if (progress == null)
        {
            return seed;
        }

        AbilityAction fact = EventFactBuilder.ForPlayer(
            caregiver,
            VerbIds.Tend.Value,
            tokens: [TokenOther],
            includeLastCraft: false);

        TendContext context = new()
        {
            Player = caregiver,
            Progress = progress,
            Fact = fact,
            World = caregiver.Entity.World
        };

        float folded = mod.Pipeline.Run(
            HookIds.ItemInteraction,
            VerbIds.Tend,
            phase,
            context,
            seed);
        return folded > 0f ? folded : seed;
    }
}
