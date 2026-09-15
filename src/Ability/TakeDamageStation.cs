using Prosequor.Ability.Hooks;
using Prosequor.Player;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.GameContent;

namespace Prosequor.Ability;

/// <summary>
/// Folds <c>prosequor:on-damage</c> / <c>amount</c>, then resolves <c>last-stand</c>
/// (cooldown hours) and applies fatal-blow survival in the station.
/// </summary>
public static class TakeDamageStation
{
    public const string VerbOnDamage = "prosequor:on-damage";
    public const string TagDamageFrost = "frost";
    public const string TagDamageWeather = "weather";
    public const string AttrLastStandReadyHours = "prosequorLastStandReadyHours";

    public static void Run(EntityBehaviorHealth health, DamageSource damageSource, ref float damage)
    {
        Entity entity = health.entity;
        if (entity == null || entity.World.Side != EnumAppSide.Server)
        {
            return;
        }

        if (damageSource == null || damageSource.Type == EnumDamageType.Heal)
        {
            return;
        }

        if (entity is not EntityPlayer entityPlayer || entityPlayer.Player == null)
        {
            return;
        }

        IPlayer player = entityPlayer.Player;
        ProsequorModSystem? mod = ProsequorModSystem.For(entity.Api);
        if (mod?.Pipeline == null)
        {
            return;
        }

        IPlayerProgress? progress = ProsequorModSystem.GetProgress(player);
        if (progress == null)
        {
            return;
        }

        List<string> damageKinds = new();
        if (damageSource.Type == EnumDamageType.Frost)
        {
            damageKinds.Add(TagDamageFrost);
        }

        if (damageSource.Source == EnumDamageSource.Weather)
        {
            damageKinds.Add(TagDamageWeather);
        }

        AbilityAction fact = EventFactBuilder.ForPlayer(
            player,
            VerbOnDamage,
            damage: damageKinds.Count > 0 ? damageKinds : null);

        TakeDamageContext context = new()
        {
            Player = player,
            Progress = progress,
            Fact = fact,
            Entity = entity,
            CurrentHealth = health.Health,
            DamageSource = damageSource
        };

        damage = mod.Pipeline.Run(
            HookIds.PlayerInteraction,
            VerbIds.OnDamage,
            HookIds.Amount,
            context,
            damage);

        float cooldownHours = mod.Pipeline.Run(
            HookIds.PlayerInteraction,
            VerbIds.OnDamage,
            HookIds.LastStand,
            context,
            0f);
        TryApplyLastStand(context, cooldownHours, ref damage);
    }

    /// <summary>
    /// If <paramref name="cooldownHours"/> is positive, the hit would kill, and the cooldown is ready,
    /// leave 1 HP and stamp <see cref="AttrLastStandReadyHours"/>.
    /// </summary>
    public static void TryApplyLastStand(TakeDamageContext context, float cooldownHours, ref float damage)
    {
        int hours = (int)Math.Floor(cooldownHours);
        if (hours <= 0)
        {
            return;
        }

        if (context.CurrentHealth - damage > 0f)
        {
            return;
        }

        Entity entity = context.Entity;
        double now = entity.World.Calendar?.TotalHours ?? 0.0;
        ITreeAttribute attrs = entity.WatchedAttributes;
        double readyAt = attrs.GetDouble(AttrLastStandReadyHours, 0.0);
        if (now < readyAt)
        {
            return;
        }

        damage = Math.Max(0f, context.CurrentHealth - 1f);
        attrs.SetDouble(AttrLastStandReadyHours, now + hours);
        entity.WatchedAttributes.MarkPathDirty(AttrLastStandReadyHours);
    }
}
