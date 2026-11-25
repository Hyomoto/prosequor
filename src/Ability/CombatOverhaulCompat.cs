using System.Collections.Concurrent;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace Prosequor.Ability;

/// <summary>
/// Optional Combat Overhaul hooks. Types live in <c>overhaulliblegacycompat</c>;
/// no compile reference. No-op when that assembly is absent.
/// </summary>
public static class CombatOverhaulCompat
{
    public const string ProjectileSystemTypeName =
        "CombatOverhaul.RangedSystems.ProjectileSystemServer";

    public const string RangeWeaponClientTypeName =
        "CombatOverhaul.RangedSystems.RangeWeaponClient";

    public const string HarmonyPatchesTypeName =
        "CombatOverhaul.Integration.HarmonyPatches";

    public const float FallDamageCoefficient = 0.2f;

    static readonly ConcurrentDictionary<Type, PropertyInfo?> dropChanceProps = new();
    static readonly ConcurrentDictionary<Type, PropertyInfo?> projectileStackProps = new();

    /// <summary>
    /// No-op when Combat Overhaul runtime is not loaded. Safe to call from
    /// <c>AcquirePatches</c> beside other optional compat.
    /// </summary>
    public static void TryPatch(Harmony harmony)
    {
        if (harmony == null)
        {
            return;
        }

        TryPatchSpawnProjectile(harmony);
        TryPatchGetAnimationSpeed(harmony);
        TryPatchOnFallToGround(harmony);
    }

    static void TryPatchSpawnProjectile(Harmony harmony)
    {
        Type? type = AccessTools.TypeByName(ProjectileSystemTypeName);
        MethodInfo? method = type == null
            ? null
            : AccessTools.Method(type, "SpawnProjectile");
        TryApply(
            harmony,
            method,
            postfix: new HarmonyMethod(typeof(CombatOverhaulCompat), nameof(SpawnProjectilePostfix)));
    }

    static void TryPatchGetAnimationSpeed(Harmony harmony)
    {
        Type? type = AccessTools.TypeByName(RangeWeaponClientTypeName);
        if (type == null)
        {
            return;
        }

        HarmonyMethod postfix = new(typeof(CombatOverhaulCompat), nameof(GetAnimationSpeedPostfix));
        foreach (MethodInfo method in AccessTools.GetDeclaredMethods(type))
        {
            if (method.Name != "GetAnimationSpeed"
                || method.ReturnType != typeof(float)
                || method.GetParameters().Length < 1
                || method.GetParameters()[0].ParameterType != typeof(Entity))
            {
                continue;
            }

            TryApply(harmony, method, postfix: postfix);
        }
    }

    static void TryPatchOnFallToGround(Harmony harmony)
    {
        Type? type = AccessTools.TypeByName(HarmonyPatchesTypeName);
        MethodInfo? method = type == null
            ? null
            : AccessTools.Method(type, "OnFallToGround");
        TryApply(
            harmony,
            method,
            transpiler: new HarmonyMethod(typeof(CombatOverhaulCompat), nameof(OnFallToGroundTranspiler)));
    }

    static void TryApply(
        Harmony harmony,
        MethodInfo? method,
        HarmonyMethod? postfix = null,
        HarmonyMethod? transpiler = null)
    {
        if (method == null)
        {
            return;
        }

        try
        {
            harmony.Patch(method, postfix: postfix, transpiler: transpiler);
        }
        catch
        {
            // Signature drift — leave Combat Overhaul's method unpatched.
        }
    }

    /// <summary>
    /// After CO writes motion and drop chance: Fletcher flight, bow draw strength, Nice Shot.
    /// </summary>
    public static void SpawnProjectilePostfix(Entity shooter, object[] __args)
    {
        if (shooter?.World?.Side != EnumAppSide.Server
            || shooter is not EntityPlayer ep
            || ep.Player == null
            || __args == null
            || __args.Length < 9
            || __args[8] is not Entity projectile)
        {
            return;
        }

        ItemStack? arrow = ReadProjectileStack(projectile);
        float flight = arrow == null
            ? 1f
            : CraftAttributeMods.GetFactor(arrow, FlightAttributeMutator.KeyName);
        float draw = ep.Stats?.GetBlended("bowDrawingStrength") ?? 1f;
        float motionScale = 1f;
        if (flight > 1.0001f)
        {
            motionScale *= flight;
        }

        if (Math.Abs(draw - 1f) > 0.0001f)
        {
            motionScale *= draw;
        }

        if (Math.Abs(motionScale - 1f) > 0.0001f)
        {
#pragma warning disable CS0618 // ServerPos: CO writes launch motion here before Pos.SetFrom.
            Vec3d motion = projectile.ServerPos.Motion;
            projectile.ServerPos.Motion.Set(
                motion.X * motionScale,
                motion.Y * motionScale,
                motion.Z * motionScale);
            projectile.Pos.SetFrom(projectile.ServerPos);
#pragma warning restore CS0618
        }

        if (arrow == null || !TryReadDropChance(projectile, out float dropChance))
        {
            return;
        }

        float breakChance = 1f - Math.Clamp(dropChance, 0f, 1f);
        float nextBreak = PlayerInteractionStation.ResolveArrowBreakChance(ep.Player, breakChance);
        TryWriteDropChance(projectile, 1f - Math.Clamp(nextBreak, 0f, 1f));
    }

    /// <summary>
    /// Multiplies CO bow/sling charge animation speed by <c>rangedWeaponsSpeed</c>.
    /// </summary>
    public static void GetAnimationSpeedPostfix(Entity player, ref float __result)
    {
        if (player is not EntityPlayer ep || ep.Stats == null)
        {
            return;
        }

        float speed = ep.Stats.GetBlended("rangedWeaponsSpeed");
        if (Math.Abs(speed - 1f) < 0.0001f)
        {
            return;
        }

        __result *= speed;
    }

    /// <summary>
    /// Replaces the hard-coded 0.2 fall coefficient (shipped as <c>ldc.r8</c>) with one
    /// scaled by <c>fallDamageFactor</c>.
    /// </summary>
    public static IEnumerable<CodeInstruction> OnFallToGroundTranspiler(
        IEnumerable<CodeInstruction> instructions)
    {
        MethodInfo helper = AccessTools.Method(
            typeof(CombatOverhaulCompat),
            nameof(ScaleFallCoefficient));
        List<CodeInstruction> original = [.. instructions];
        List<CodeInstruction> patched = new(original.Count);
        int hits = 0;

        foreach (CodeInstruction code in original)
        {
            if (code.opcode == OpCodes.Ldc_R8
                && code.operand is double value
                && Math.Abs(value - FallDamageCoefficient) < 0.0001)
            {
                hits++;
                patched.Add(new CodeInstruction(OpCodes.Ldarg_0).WithLabels(code.labels).WithBlocks(code.blocks));
                patched.Add(new CodeInstruction(OpCodes.Call, helper));
                continue;
            }

            patched.Add(code);
        }

        if (hits == 1)
        {
            return patched;
        }

        throw new InvalidOperationException(
            $"[prosequor] Combat Overhaul fall transpiler failed. hits={hits} (want 1).");
    }

    /// <summary>
    /// <paramref name="health"/> is the CO <c>OnFallToGround</c> instance arg.
    /// Unset <c>fallDamageFactor</c> blends to 1, so the coefficient stays 0.2.
    /// Returns double to replace the shipped <c>ldc.r8</c>.
    /// </summary>
    public static double ScaleFallCoefficient(EntityBehaviorHealth health)
    {
        if (health?.entity is not EntityPlayer player || player.Stats == null)
        {
            return FallDamageCoefficient;
        }

        float factor = player.Stats.GetBlended("fallDamageFactor");
        if (factor < 0f)
        {
            factor = 0f;
        }

        return FallDamageCoefficient * factor;
    }

    static ItemStack? ReadProjectileStack(Entity projectile)
    {
        PropertyInfo? prop = projectileStackProps.GetOrAdd(
            projectile.GetType(),
            static type => type.GetProperty(
                "ProjectileStack",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic));
        return prop?.GetValue(projectile) as ItemStack;
    }

    static bool TryReadDropChance(Entity projectile, out float dropChance)
    {
        dropChance = 0f;
        PropertyInfo? prop = dropChanceProps.GetOrAdd(
            projectile.GetType(),
            static type => type.GetProperty(
                "DropOnImpactChance",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic));
        if (prop == null || prop.PropertyType != typeof(float))
        {
            return false;
        }

        object? raw = prop.GetValue(projectile);
        if (raw is not float value)
        {
            return false;
        }

        dropChance = value;
        return true;
    }

    static void TryWriteDropChance(Entity projectile, float dropChance)
    {
        PropertyInfo? prop = dropChanceProps.GetOrAdd(
            projectile.GetType(),
            static type => type.GetProperty(
                "DropOnImpactChance",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic));
        if (prop == null || !prop.CanWrite || prop.PropertyType != typeof(float))
        {
            return;
        }

        prop.SetValue(projectile, dropChance);
    }
}
