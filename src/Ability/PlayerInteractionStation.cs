using Prosequor.Ability.Hooks;
using Prosequor.Inventory;
using Prosequor.Player;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace Prosequor.Ability;

/// <summary>Resolves <c>prosequor:player-interaction</c> player verbs via the ability pipeline.</summary>
public static class PlayerInteractionStation
{
    public const string AttrBaseMaxSatiety = "prosequorBaseMaxSatiety";
    public const string AttrSatietyBonus = "prosequorSatietyBonus";

    public static int ResolveHealthDelta(IPlayer player) =>
        RunInt(player, VerbIds.Health);

    public static int ResolveSatietyDelta(IPlayer player) =>
        RunInt(player, VerbIds.Satiety);

    public static int ResolveHungerDelayPercent(IPlayer player) =>
        RunInt(player, VerbIds.HungerDelay);

    public static int ResolveArmorWalkPercent(IPlayer player) =>
        RunInt(player, VerbIds.ArmorWalk);

    public static int ResolveMeleeDamagePercent(IPlayer player) =>
        RunInt(player, VerbIds.MeleeDamage);

    public static int ResolveRangedSpeedPercent(IPlayer player) =>
        RunInt(player, VerbIds.RangedSpeed);

    public static int ResolveRangedAccPercent(IPlayer player) =>
        RunInt(player, VerbIds.RangedAcc);

    public static int ResolveRangedDamagePercent(IPlayer player) =>
        RunInt(player, VerbIds.RangedDamage);

    public static int ResolveRangedDistancePercent(IPlayer player) =>
        RunInt(player, VerbIds.RangedDistance);

    public static int ResolveFallDamageFactorPercent(IPlayer player) =>
        RunInt(player, VerbIds.FallDamageFactor);

    public static int ResolveFallDamageThresholdPercent(IPlayer player) =>
        RunInt(player, VerbIds.FallDamageThreshold);

    public static int ResolveTemporalRecoverRatePercent(IPlayer player) =>
        RunInt(player, VerbIds.TemporalRecoverRate);

    public static int ResolveTemporalDrainRatePercent(IPlayer player) =>
        RunInt(player, VerbIds.TemporalDrainRate);

    public static int ResolveWalkSpeedPercent(IPlayer player) =>
        RunInt(player, VerbIds.WalkSpeed);

    public static int ResolveHungerRatePercent(IPlayer player) =>
        RunInt(player, VerbIds.HungerRate);

    /// <summary>Sprint speed bonus fraction (0 = no change). Applied only while sprinting on foot.</summary>
    public static float ResolveSprintSpeedBonus(IPlayer player) =>
        Math.Max(0f, RunFloat(player, VerbIds.SprintSpeed));

    /// <summary>Swim speed bonus fraction (0 = no change). Applied in the liquid walk-speed path.</summary>
    public static float ResolveSwimSpeedBonus(IPlayer player) =>
        Math.Max(0f, RunFloat(player, VerbIds.SwimSpeed));

    /// <summary>Sneak speed bonus fraction (0 = no change). Applied only while sneaking on foot.</summary>
    public static float ResolveSneakSpeedBonus(IPlayer player) =>
        Math.Max(0f, RunFloat(player, VerbIds.SneakSpeed));

    /// <summary>Animal sense-range multiplier while sneaking (seed 1).</summary>
    public static float ResolveAnimalSenseRangeFactor(IPlayer player) =>
        Math.Max(0f, RunFloat(player, VerbIds.AnimalSenseRange, seed: 1f));

    /// <summary>Animal threat-emission multiplier while sneaking (seed 1).</summary>
    public static float ResolveAnimalThreatSneakFactor(IPlayer player) =>
        Math.Max(0f, RunFloat(player, VerbIds.AnimalThreatSneak, seed: 1f));

    /// <summary>Arrow break chance after Nice Shot fold (seed = current break chance).</summary>
    public static float ResolveArrowBreakChance(IPlayer player, float breakChance) =>
        Math.Max(0f, RunFloat(player, VerbIds.ArrowBreak, seed: Math.Max(0f, breakChance)));

    /// <summary>Heartseeker damage multiplier (seed 1) when threat is below the folded threshold.</summary>
    public static float ResolveUnawareDamageFactor(IPlayer player) =>
        Math.Max(0f, RunFloat(player, VerbIds.UnawareDamage, HookIds.Amount, seed: 1f));

    /// <summary>Heartseeker threat threshold percent (seed 0).</summary>
    public static float ResolveUnawareDamageThreshold(IPlayer player) =>
        Math.Max(0f, RunFloat(player, VerbIds.UnawareDamage, HookIds.Threshold, seed: 0f));

    /// <summary>Tracker mark range in meters (seed 0). 0 when the node is not owned.</summary>
    public static float ResolveTrackRange(IPlayer player) =>
        Math.Max(0f, RunFloat(player, VerbIds.TrackMark, HookIds.Range, seed: 0f));

    /// <summary>Seconds of crosshair aim before a tracker mark lands (seed 0).</summary>
    public static float ResolveTrackFocusSeconds(IPlayer player) =>
        Math.Max(0f, RunFloat(player, VerbIds.TrackMark, HookIds.Focus, seed: 0f));

    /// <summary>Degrees from look direction past which a tracker mark drops (seed 0).</summary>
    public static float ResolveTrackLoseAngle(IPlayer player) =>
        Math.Max(0f, RunFloat(player, VerbIds.TrackMark, HookIds.Angle, seed: 0f));

    /// <summary>Seconds a bow must stay drawn before Focus Shot scales that shot (seed 0).</summary>
    public static float ResolveFocusShotHoldSeconds(IPlayer player) =>
        Math.Max(0f, RunFloat(player, VerbIds.FocusShot, HookIds.Hold, seed: 0f));

    /// <summary>Focus Shot damage multiplier (seed 1) once the draw meets the hold.</summary>
    public static float ResolveFocusShotDamageFactor(IPlayer player) =>
        Math.Max(0f, RunFloat(player, VerbIds.FocusShot, HookIds.Amount, seed: 1f));

    /// <summary>
    /// Which on-foot locomotion bonus applies. Liquid wins over sprint/sneak.
    /// Sprint and sneak are exclusive (both held → neither). Mounted is never a foot mode.
    /// </summary>
    public static FootLocomotion ClassifyFootLocomotion(
        bool mounted,
        bool swimming,
        bool feetInLiquid,
        bool sprint,
        bool sneak)
    {
        if (mounted)
        {
            return FootLocomotion.None;
        }

        if (swimming || feetInLiquid)
        {
            return FootLocomotion.Swim;
        }

        if (sprint && !sneak)
        {
            return FootLocomotion.Sprint;
        }

        if (sneak && !sprint)
        {
            return FootLocomotion.Sneak;
        }

        return FootLocomotion.None;
    }

    /// <summary>
    /// Multiply vanilla walk speed by the matching skill bonus. No-op when not an on-foot player.
    /// </summary>
    public static void ApplyWalkSpeedBonus(EntityAgent entity, ref double multiplier)
    {
        if (entity is not EntityPlayer entityPlayer || entityPlayer.Player == null)
        {
            return;
        }

        EntityControls controls = entity.ServerControls;
        FootLocomotion mode = ClassifyFootLocomotion(
            entity.MountedOn != null,
            entity.Swimming,
            entity.FeetInLiquid,
            controls.Sprint,
            controls.Sneak);
        float bonus = mode switch
        {
            FootLocomotion.Sprint => ResolveSprintSpeedBonus(entityPlayer.Player),
            FootLocomotion.Swim => ResolveSwimSpeedBonus(entityPlayer.Player),
            FootLocomotion.Sneak => ResolveSneakSpeedBonus(entityPlayer.Player),
            _ => 0f
        };
        if (bonus > 0f)
        {
            multiplier *= 1.0 + bonus;
        }
    }

    public static int ResolveAnimalThreatPercent(IPlayer player) =>
        RunInt(player, VerbIds.AnimalThreat);

    public static int ResolveCritChancePercent(IPlayer player) =>
        RunInt(player, VerbIds.CritChance);

    /// <summary>Crit damage multiplier (seed 2). Applied only after a successful crit roll.</summary>
    public static float ResolveCritDamageMultiplier(IPlayer player) =>
        Math.Max(0f, RunFloat(player, VerbIds.CritDamage, seed: 2f));

    public static int ResolveWholeVesselLootChancePercent(IPlayer player) =>
        RunInt(player, VerbIds.WholeVesselLootChance);

    public static int ResolveMechanicalsDamagePercent(IPlayer player) =>
        RunInt(player, VerbIds.MechanicalsDamage);

    /// <summary>
    /// Per-hit roll for Inconspicuity crits. Runs after Strength/ranged stat multipliers
    /// are already baked into <paramref name="damage"/>; success multiplies by
    /// <see cref="ResolveCritDamageMultiplier"/>.
    /// </summary>
    public static void TryApplyCrit(DamageSource damageSource, Entity victim, ref float damage)
    {
        if (victim?.World == null || victim.World.Side != EnumAppSide.Server)
        {
            return;
        }

        if (damageSource == null || damage <= 0f)
        {
            return;
        }

        EnumDamageType type = damageSource.Type;
        if (type != EnumDamageType.BluntAttack
            && type != EnumDamageType.SlashingAttack
            && type != EnumDamageType.PiercingAttack)
        {
            return;
        }

        if (damageSource.GetCauseEntity() is not EntityPlayer attacker
            || attacker.Player == null
            || attacker.EntityId == victim.EntityId)
        {
            return;
        }

        int chancePct = ResolveCritChancePercent(attacker.Player);
        if (chancePct < 1)
        {
            return;
        }

        if (victim.World.Rand.NextDouble() * 100.0 >= chancePct)
        {
            return;
        }

        damage *= ResolveCritDamageMultiplier(attacker.Player);
    }

    /// <summary>Harmony target: vanilla flee <c>ExecutionChance</c>.</summary>
    public static double GetScaledFleeExecutionChance(AiTaskFleeEntity self) =>
        ReadExecutionChance(self);

    /// <summary>Harmony target: vanilla seek <c>ExecutionChance</c>.</summary>
    public static double GetScaledSeekExecutionChance(AiTaskSeekEntity self) =>
        ReadExecutionChance(self);

    static double ReadExecutionChance(AiTaskBase task)
    {
        object? raw = AnimalResponseRatePatch.ExecutionChanceField.GetValue(task);
        return raw switch
        {
            double d => d,
            float f => f,
            _ => 0.1
        };
    }

    /// <summary>Harmony target: returns <paramref name="vanillaFactor"/>.</summary>
    public static float AdjustFleeFearReductionFactor(AiTaskFleeEntity self, float vanillaFactor) =>
        vanillaFactor;

    /// <summary>Harmony target: returns <paramref name="vanillaFactor"/>.</summary>
    public static float AdjustMeleeFearReductionFactor(AiTaskMeleeAttack self, float vanillaFactor) =>
        vanillaFactor;

    public const double VanillaTemporalRecoverDivisor = 200.0;
    public const double VanillaTemporalDrainDivisor = 800.0;

    public static double GetRecoveryDivisor(EntityBehaviorTemporalStabilityAffected self)
    {
        if (self?.entity is not EntityPlayer entityPlayer || entityPlayer.Player == null)
        {
            return VanillaTemporalRecoverDivisor;
        }

        int pct = ResolveTemporalRecoverRatePercent(entityPlayer.Player);
        if (pct < 1)
        {
            return VanillaTemporalRecoverDivisor;
        }

        return VanillaTemporalRecoverDivisor / (pct / 100.0);
    }

    public static double GetDrainDivisor(EntityBehaviorTemporalStabilityAffected self)
    {
        if (self?.entity is not EntityPlayer entityPlayer || entityPlayer.Player == null)
        {
            return VanillaTemporalDrainDivisor;
        }

        int pct = ResolveTemporalDrainRatePercent(entityPlayer.Player);
        if (pct < 1)
        {
            return VanillaTemporalDrainDivisor;
        }

        return VanillaTemporalDrainDivisor / (pct / 100.0);
    }

    /// <summary>
    /// Client hook when basic-slot Count changes (inventory dialog may need rebuild).
    /// </summary>
    public static Action<ICoreClientAPI>? ClientAfterBasicSlotsResized;

    /// <summary>Pipeline seed for <c>basic-slots</c> before any provider rules add.</summary>
    public const int BasicSlotsBase = 0;

    public static int ResolveBasicSlots(IPlayer player) =>
        RunInt(player, VerbIds.BasicSlots, BasicSlotsBase);

    public const string StatArmorWalkKey = "prosequor-attr-str-armor";
    public const string StatMeleeDamageKey = "prosequor-attr-str-melee";
    public const string StatRangedSpeedKey = "prosequor-attr-per-ranged-speed";
    public const string StatRangedAccKey = "prosequor-attr-per-ranged-acc";
    public const string StatRangedDamageKey = "prosequor-attr-per-ranged-damage";
    public const string StatRangedDistanceKey = "prosequor-attr-per-ranged-distance";
    public const string StatBowQualityAccKey = "prosequor-bow-quality-acc";
    public const string StatFallDamageFactorKey = "prosequor-attr-res-fall-factor";
    public const string StatFallDamageThresholdKey = "prosequor-attr-res-fall-threshold";
    public const string StatWalkSpeedKey = "prosequor-attr-con-walk-speed";
    public const string StatHungerRateKey = "prosequor-attr-res-hunger-rate";
    public const string StatAnimalSeekingRangeKey = "prosequor-attr-inc-animal-seek";
    public const string StatWholeVesselLootChanceKey = "prosequor-attr-inc-vessel-loot";
    public const string StatMechanicalsDamageKey = "prosequor-attr-inc-mechanicals";

    /// <summary>
    /// Writes absolute pipeline percent onto <c>armorWalkSpeedAffectedness</c>
    /// as an additive offset (<c>pct/100 - 1</c>).
    /// </summary>
    public static void ApplyArmorWalk(Entity entity)
    {
        if (entity.World.Side != EnumAppSide.Server)
        {
            return;
        }

        if (entity is not EntityPlayer entityPlayer || entityPlayer.Player == null || entity.Stats == null)
        {
            return;
        }

        int pct = ResolveArmorWalkPercent(entityPlayer.Player);
        float offset = pct / 100f - 1f;
        if (Math.Abs(offset) < 0.0001f)
        {
            entity.Stats.Remove("armorWalkSpeedAffectedness", StatArmorWalkKey);
        }
        else
        {
            entity.Stats.Set("armorWalkSpeedAffectedness", StatArmorWalkKey, offset, false);
        }
    }

    /// <summary>
    /// Writes absolute pipeline percent onto <c>meleeWeaponsDamage</c>
    /// as an additive offset (<c>pct/100 - 1</c>).
    /// </summary>
    public static void ApplyMeleeDamage(Entity entity)
    {
        if (entity.World.Side != EnumAppSide.Server)
        {
            return;
        }

        if (entity is not EntityPlayer entityPlayer || entityPlayer.Player == null || entity.Stats == null)
        {
            return;
        }

        int pct = ResolveMeleeDamagePercent(entityPlayer.Player);
        float offset = pct / 100f - 1f;
        if (Math.Abs(offset) < 0.0001f)
        {
            entity.Stats.Remove("meleeWeaponsDamage", StatMeleeDamageKey);
        }
        else
        {
            entity.Stats.Set("meleeWeaponsDamage", StatMeleeDamageKey, offset, false);
        }
    }

    /// <summary>
    /// Writes absolute pipeline percent onto <c>rangedWeaponsSpeed</c>
    /// as an additive offset (<c>pct/100 - 1</c>).
    /// </summary>
    public static void ApplyRangedSpeed(Entity entity)
    {
        if (entity.World.Side != EnumAppSide.Server)
        {
            return;
        }

        if (entity is not EntityPlayer entityPlayer || entityPlayer.Player == null || entity.Stats == null)
        {
            return;
        }

        int pct = ResolveRangedSpeedPercent(entityPlayer.Player);
        float offset = pct / 100f - 1f;
        if (Math.Abs(offset) < 0.0001f)
        {
            entity.Stats.Remove("rangedWeaponsSpeed", StatRangedSpeedKey);
        }
        else
        {
            entity.Stats.Set("rangedWeaponsSpeed", StatRangedSpeedKey, offset, false);
        }
    }

    /// <summary>
    /// Writes absolute pipeline percent onto <c>rangedWeaponsAcc</c>
    /// as an additive offset (<c>pct/100 - 1</c>), plus any stamped bow quality factor.
    /// </summary>
    public static void ApplyRangedAcc(Entity entity)
    {
        if (entity.World.Side != EnumAppSide.Server)
        {
            return;
        }

        if (entity is not EntityPlayer entityPlayer || entityPlayer.Player == null || entity.Stats == null)
        {
            return;
        }

        int pct = ResolveRangedAccPercent(entityPlayer.Player);
        float offset = pct / 100f - 1f;
        if (Math.Abs(offset) < 0.0001f)
        {
            entity.Stats.Remove("rangedWeaponsAcc", StatRangedAccKey);
        }
        else
        {
            entity.Stats.Set("rangedWeaponsAcc", StatRangedAccKey, offset, false);
        }

        float bowBonus = 0f;
        ItemStack? held = entityPlayer.RightHandItemSlot?.Itemstack
            ?? entityPlayer.LeftHandItemSlot?.Itemstack;
        if (held != null)
        {
            float factor = CraftAttributeMods.GetFactor(held, RangedAccAttributeMutator.KeyName);
            if (factor > 1.0001f)
            {
                bowBonus = factor - 1f;
            }
        }

        if (Math.Abs(bowBonus) < 0.0001f)
        {
            entity.Stats.Remove("rangedWeaponsAcc", StatBowQualityAccKey);
        }
        else
        {
            entity.Stats.Set("rangedWeaponsAcc", StatBowQualityAccKey, bowBonus, false);
        }
    }

    /// <summary>
    /// Writes signed pipeline percent onto <c>rangedWeaponsDamage</c>
    /// as an additive offset (<c>pct/100</c>).
    /// </summary>
    public static void ApplyRangedDamage(Entity entity) =>
        ApplySignedPercentStat(entity, "rangedWeaponsDamage", StatRangedDamageKey, ResolveRangedDamagePercent);

    /// <summary>
    /// Writes signed pipeline percent onto <c>bowDrawingStrength</c>
    /// as an additive offset (<c>pct/100</c>).
    /// </summary>
    public static void ApplyRangedDistance(Entity entity) =>
        ApplySignedPercentStat(entity, "bowDrawingStrength", StatRangedDistanceKey, ResolveRangedDistancePercent);

    /// <summary>
    /// Writes signed pipeline percent onto <c>walkspeed</c>
    /// as an additive offset (<c>pct/100</c>). Stacks under sprint/swim/sneak multipliers.
    /// </summary>
    public static void ApplyWalkSpeed(Entity entity) =>
        ApplySignedPercentStat(entity, "walkspeed", StatWalkSpeedKey, ResolveWalkSpeedPercent);

    /// <summary>
    /// Writes signed pipeline percent onto <c>hungerrate</c>
    /// as an additive offset (<c>pct/100</c>). Stacks with riding hunger reduction.
    /// </summary>
    public static void ApplyHungerRate(Entity entity) =>
        ApplySignedPercentStat(entity, "hungerrate", StatHungerRateKey, ResolveHungerRatePercent);

    /// <summary>
    /// Writes signed pipeline percent onto <c>mechanicalsDamage</c>
    /// as an additive offset (<c>pct/100</c>).
    /// </summary>
    public static void ApplyMechanicalsDamage(Entity entity) =>
        ApplySignedPercentStat(entity, "mechanicalsDamage", StatMechanicalsDamageKey, ResolveMechanicalsDamagePercent);

    static void ApplySignedPercentStat(
        Entity entity,
        string entityStat,
        string key,
        System.Func<IPlayer, int> resolvePercent)
    {
        if (entity.World.Side != EnumAppSide.Server)
        {
            return;
        }

        if (entity is not EntityPlayer entityPlayer || entityPlayer.Player == null || entity.Stats == null)
        {
            return;
        }

        int pct = resolvePercent(entityPlayer.Player);
        float offset = pct / 100f;
        if (Math.Abs(offset) < 0.0001f)
        {
            entity.Stats.Remove(entityStat, key);
        }
        else
        {
            entity.Stats.Set(entityStat, key, offset, false);
        }
    }

    /// <summary>
    /// Writes absolute pipeline percent onto <c>fallDamageThreshold</c>
    /// as an additive offset (<c>pct/100 - 1</c>).
    /// </summary>
    public static void ApplyFallDamageThreshold(Entity entity)
    {
        if (entity.World.Side != EnumAppSide.Server)
        {
            return;
        }

        if (entity is not EntityPlayer entityPlayer || entityPlayer.Player == null || entity.Stats == null)
        {
            return;
        }

        int pct = ResolveFallDamageThresholdPercent(entityPlayer.Player);
        float offset = pct / 100f - 1f;
        if (Math.Abs(offset) < 0.0001f)
        {
            entity.Stats.Remove("fallDamageThreshold", StatFallDamageThresholdKey);
        }
        else
        {
            entity.Stats.Set("fallDamageThreshold", StatFallDamageThresholdKey, offset, false);
        }
    }

    /// <summary>
    /// Writes absolute pipeline percent onto <c>fallDamageFactor</c>
    /// as an additive offset (<c>pct/100 - 1</c>).
    /// </summary>
    public static void ApplyFallDamageFactor(Entity entity)
    {
        if (entity.World.Side != EnumAppSide.Server)
        {
            return;
        }

        if (entity is not EntityPlayer entityPlayer || entityPlayer.Player == null || entity.Stats == null)
        {
            return;
        }

        int pct = ResolveFallDamageFactorPercent(entityPlayer.Player);
        float offset = pct / 100f - 1f;
        if (Math.Abs(offset) < 0.0001f)
        {
            entity.Stats.Remove("fallDamageFactor", StatFallDamageFactorKey);
        }
        else
        {
            entity.Stats.Set("fallDamageFactor", StatFallDamageFactorKey, offset, false);
        }
    }

    /// <summary>Removes the Prosequor <c>animalSeekingRange</c> stat key.</summary>
    public static void ClearLegacyAnimalSeekingRange(Entity entity)
    {
        if (entity.World.Side != EnumAppSide.Server)
        {
            return;
        }

        if (entity is not EntityPlayer || entity.Stats == null)
        {
            return;
        }

        entity.Stats.Remove("animalSeekingRange", StatAnimalSeekingRangeKey);
    }

    /// <summary>
    /// Writes pipeline percent onto <c>wholeVesselLootChance</c> as a FlatSum additive.
    /// Vanilla rolls <c>GetBlended - 1</c> (ctor base is 1), so 12 writes +0.12 → 12%.
    /// </summary>
    public static void ApplyWholeVesselLootChance(Entity entity)
    {
        if (entity.World.Side != EnumAppSide.Server)
        {
            return;
        }

        if (entity is not EntityPlayer entityPlayer || entityPlayer.Player == null || entity.Stats == null)
        {
            return;
        }

        int pct = ResolveWholeVesselLootChancePercent(entityPlayer.Player);
        if (pct < 1)
        {
            entity.Stats.Remove("wholeVesselLootChance", StatWholeVesselLootChanceKey);
        }
        else
        {
            entity.Stats.Set("wholeVesselLootChance", StatWholeVesselLootChanceKey, pct / 100f, false);
        }
    }

    /// <summary>
    /// Resizes the honest <c>prosequorcarry</c> sidecar to the resolved basic slot count.
    /// </summary>
    public static void ApplyBasicSlots(Entity entity)
    {
        if (entity is not EntityPlayer entityPlayer || entityPlayer.Player == null)
        {
            return;
        }

        IPlayer player = entityPlayer.Player;

        // Carry inventory is local-player only. Spawn and entity-loaded packets both
        // hydrate remote entities whose IPlayer.Entity is not linked yet.
        if (entity.Api is ICoreClientAPI capi
            && capi.World.Player?.PlayerUID != player.PlayerUID)
        {
            return;
        }

        if (player.InventoryManager == null)
        {
            return;
        }

        int resolved = Math.Max(0, ResolveBasicSlots(player));
        ProsequorCarryInventory? carry = ProsequorCarryInventory.Ensure(player, entity.Api);
        if (carry == null)
        {
            return;
        }

        int before = carry.Count;
        carry.SetSize(resolved);

        if (before != carry.Count && entity.Api is ICoreClientAPI clientApi)
        {
            ClientAfterBasicSlotsResized?.Invoke(clientApi);
        }
    }

    /// <summary>
    /// Sets <see cref="EntityBehaviorHunger.MaxSaturation"/> to base + pipeline satiety delta,
    /// preserving fill ratio and clamping nutrient bars.
    /// </summary>
    public static void ApplyMaxSatiety(Entity entity)
    {
        if (entity.World.Side != EnumAppSide.Server)
        {
            return;
        }

        if (entity is not EntityPlayer entityPlayer || entityPlayer.Player == null)
        {
            return;
        }

        EntityBehaviorHunger? hunger = entity.GetBehavior<EntityBehaviorHunger>();
        if (hunger == null)
        {
            return;
        }

        int delta = ResolveSatietyDelta(entityPlayer.Player);
        ITreeAttribute attrs = entity.WatchedAttributes;
        float lastBonus = attrs.GetFloat(AttrSatietyBonus, 0f);
        float baseMax = attrs.GetFloat(AttrBaseMaxSatiety, -1f);
        if (baseMax < 0f)
        {
            baseMax = hunger.MaxSaturation - lastBonus;
            attrs.SetFloat(AttrBaseMaxSatiety, baseMax);
        }

        float beforeMax = hunger.MaxSaturation;
        float beforeSat = hunger.Saturation;
        float ratio = beforeMax > 0.001f
            ? Math.Clamp(beforeSat / beforeMax, 0f, 1f)
            : 1f;

        float newMax = baseMax + delta;
        attrs.SetFloat(AttrSatietyBonus, delta);
        hunger.MaxSaturation = newMax;
        hunger.Saturation = newMax * ratio;

        hunger.FruitLevel = Math.Min(hunger.FruitLevel, newMax);
        hunger.VegetableLevel = Math.Min(hunger.VegetableLevel, newMax);
        hunger.ProteinLevel = Math.Min(hunger.ProteinLevel, newMax);
        hunger.GrainLevel = Math.Min(hunger.GrainLevel, newMax);
        hunger.DairyLevel = Math.Min(hunger.DairyLevel, newMax);

        hunger.UpdateNutrientHealthBoost();
    }

    static int RunInt(IPlayer player, VerbId verb, int seed = 0) =>
        RunPipelineInt(player, HookIds.PlayerInteraction, verb, HookIds.Default, seed);

    static float RunFloat(IPlayer player, VerbId verb, float seed = 0f) =>
        RunFloat(player, verb, HookIds.Default, seed);

    static float RunFloat(IPlayer player, VerbId verb, PhaseId phase, float seed = 0f)
    {
        if (player?.Entity == null)
        {
            return seed;
        }

        ProsequorModSystem? mod = ProsequorModSystem.For(player.Entity.Api);
        if (mod?.Pipeline == null)
        {
            return seed;
        }

        IPlayerProgress? progress = ProsequorModSystem.GetProgress(player);
        if (progress == null)
        {
            return seed;
        }

        PlayerInteractionContext context = new()
        {
            Player = player,
            Progress = progress,
            Fact = new AbilityAction
            {
                Verb = verb.Value,
                ActorUid = player.PlayerUID
            }
        };

        return mod.Pipeline.Run(HookIds.PlayerInteraction, verb, phase, context, seed);
    }

    static int RunPipelineInt(IPlayer player, HookId hook, VerbId verb, PhaseId phase, int seed)
    {
        if (player?.Entity == null)
        {
            return seed;
        }

        ProsequorModSystem? mod = ProsequorModSystem.For(player.Entity.Api);
        if (mod?.Pipeline == null)
        {
            return seed;
        }

        IPlayerProgress? progress = ProsequorModSystem.GetProgress(player);
        if (progress == null)
        {
            return seed;
        }

        AbilityAction fact = new()
        {
            Verb = verb.Value,
            ActorUid = player.PlayerUID
        };

        PlayerInteractionContext context = new()
        {
            Player = player,
            Progress = progress,
            Fact = fact
        };

        return mod.Pipeline.Run(hook, verb, phase, context, seed);
    }
}
