using Prosequor.Ability.Hooks;
using Vintagestory.API.Common.Entities;
using Vintagestory.GameContent;

namespace Prosequor.Ability;

/// <summary>
/// Maps (hook, verb, phase) to an engine refresh sink. Declared once at bootstrap;
/// attribute score changes look up which sinks to invoke.
/// </summary>
public sealed class PhaseRefreshRegistry
{
    readonly Dictionary<(HookId Hook, VerbId Verb, PhaseId Phase), Action<Entity>> sinks = new();

    public void Register(HookId hook, VerbId verb, PhaseId phase, Action<Entity> apply) =>
        sinks[(hook, verb, phase)] = apply;

    public void Apply(HookId hook, VerbId verb, PhaseId phase, Entity entity)
    {
        if (sinks.TryGetValue((hook, verb, phase), out Action<Entity>? apply))
        {
            apply(entity);
        }
    }

    public static void RegisterBuiltIns(PhaseRefreshRegistry registry)
    {
        void PlayerVerb(VerbId verb, Action<Entity> apply) =>
            registry.Register(HookIds.PlayerInteraction, verb, HookIds.Default, apply);

        PlayerVerb(
            VerbIds.Health,
            entity =>
            {
                if (entity.World.Side != Vintagestory.API.Common.EnumAppSide.Server)
                {
                    return;
                }

                entity.GetBehavior<EntityBehaviorHealth>()?.MarkDirty();
            });

        PlayerVerb(
            VerbIds.Satiety,
            entity =>
            {
                if (entity.World.Side != Vintagestory.API.Common.EnumAppSide.Server)
                {
                    return;
                }

                PlayerInteractionStation.ApplyMaxSatiety(entity);
            });

        PlayerVerb(
            VerbIds.ArmorWalk,
            entity =>
            {
                if (entity.World.Side != Vintagestory.API.Common.EnumAppSide.Server)
                {
                    return;
                }

                PlayerInteractionStation.ApplyArmorWalk(entity);
            });

        PlayerVerb(
            VerbIds.MeleeDamage,
            entity =>
            {
                if (entity.World.Side != Vintagestory.API.Common.EnumAppSide.Server)
                {
                    return;
                }

                PlayerInteractionStation.ApplyMeleeDamage(entity);
            });

        PlayerVerb(
            VerbIds.RangedSpeed,
            entity =>
            {
                if (entity.World.Side != Vintagestory.API.Common.EnumAppSide.Server)
                {
                    return;
                }

                PlayerInteractionStation.ApplyRangedSpeed(entity);
            });

        PlayerVerb(
            VerbIds.RangedAcc,
            entity =>
            {
                if (entity.World.Side != Vintagestory.API.Common.EnumAppSide.Server)
                {
                    return;
                }

                PlayerInteractionStation.ApplyRangedAcc(entity);
            });

        PlayerVerb(
            VerbIds.FallDamageFactor,
            entity =>
            {
                if (entity.World.Side != Vintagestory.API.Common.EnumAppSide.Server)
                {
                    return;
                }

                PlayerInteractionStation.ApplyFallDamageFactor(entity);
            });

        PlayerVerb(
            VerbIds.FallDamageThreshold,
            entity =>
            {
                if (entity.World.Side != Vintagestory.API.Common.EnumAppSide.Server)
                {
                    return;
                }

                PlayerInteractionStation.ApplyFallDamageThreshold(entity);
            });

        PlayerVerb(
            VerbIds.AnimalSeekingRange,
            entity =>
            {
                if (entity.World.Side != Vintagestory.API.Common.EnumAppSide.Server)
                {
                    return;
                }

                PlayerInteractionStation.ApplyAnimalSeekingRange(entity);
            });

        PlayerVerb(
            VerbIds.WholeVesselLootChance,
            entity =>
            {
                if (entity.World.Side != Vintagestory.API.Common.EnumAppSide.Server)
                {
                    return;
                }

                PlayerInteractionStation.ApplyWholeVesselLootChance(entity);
            });

        PlayerVerb(VerbIds.BasicSlots, PlayerInteractionStation.ApplyBasicSlots);
    }
}
