using System.Reflection;
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace Prosequor.Ability;

/// <summary>
/// Optional hooks for Knapster easy-complete paths. Clay auto-mode still calls
/// <c>OnAdd</c>/<c>OnRemove</c> (scope must win the Harmony skip race). Smithing
/// writes voxels itself and never enters vanilla <c>OnHit</c>/<c>OnSplit</c>.
/// </summary>
public static class KnapsterCompat
{
    public const string ClayExtensionsTypeName =
        "Knapster.Features.EasyClayForming.Extensions.BlockEntityClayFormExtensions";

    public const string SmithingPatchesTypeName =
        "Knapster.Features.EasySmithing.Patches.EasySmithingUniversalPatches";

    /// <summary>
    /// No-op when Knapster is not loaded. Safe to call from <c>PatchAll</c> smoke.
    /// </summary>
    public static void TryPatch(Harmony harmony)
    {
        if (harmony == null)
        {
            return;
        }

        TryPatchVoxelsPerClay(harmony);
        TryPatchSmithing(harmony);
    }

    static void TryPatchVoxelsPerClay(Harmony harmony)
    {
        Type? type = AccessTools.TypeByName(ClayExtensionsTypeName);
        MethodInfo? method = type == null
            ? null
            : AccessTools.Method(type, "VoxelsPerClay", [typeof(IPlayer)]);
        if (method == null)
        {
            return;
        }

        TryApply(
            harmony,
            method,
            postfix: new HarmonyMethod(typeof(KnapsterCompat), nameof(VoxelsPerClayPostfix)));
    }

    static void TryPatchSmithing(Harmony harmony)
    {
        Type? type = AccessTools.TypeByName(SmithingPatchesTypeName);
        if (type == null)
        {
            return;
        }

        MethodInfo? processMove = AccessTools.Method(type, "ProcessMove");
        MethodInfo? processRemoveSlag = AccessTools.Method(type, "ProcessRemoveSlag");
        MethodInfo? processSplit = AccessTools.Method(type, "ProcessSplit");

        TryApply(
            harmony,
            processMove,
            postfix: new HarmonyMethod(typeof(KnapsterCompat), nameof(ProcessMovePostfix)));
        TryApply(
            harmony,
            processRemoveSlag,
            postfix: new HarmonyMethod(typeof(KnapsterCompat), nameof(ProcessRemoveSlagPostfix)));
        TryApply(
            harmony,
            processSplit,
            prefix: new HarmonyMethod(typeof(KnapsterCompat), nameof(ProcessSplitPrefix)),
            postfix: new HarmonyMethod(typeof(KnapsterCompat), nameof(ProcessSplitPostfix)));
    }

    static void TryApply(
        Harmony harmony,
        MethodInfo? method,
        HarmonyMethod? prefix = null,
        HarmonyMethod? postfix = null)
    {
        if (method == null)
        {
            return;
        }

        try
        {
            harmony.Patch(method, prefix: prefix, postfix: postfix);
        }
        catch
        {
            // Signature drift — leave Knapster's method unpatched.
        }
    }

    /// <summary>
    /// Instant clay complete refills from a hardcoded 25. Fold the same
    /// voxel-refill bonus <see cref="ItemClayRefillPatch"/> uses.
    /// </summary>
    public static void VoxelsPerClayPostfix(IPlayer byPlayer, ref int __result)
    {
        if (byPlayer == null)
        {
            return;
        }

        __result = VoxelWorkStation.ResolveVoxelRefill(byPlayer);
    }

    public static void ProcessMovePostfix(
        BlockEntityAnvil anvil,
        IPlayer byPlayer,
        int x,
        int z,
        int y)
    {
        AfterKnapsterHit(
            anvil,
            byPlayer,
            new Vec3i(x, y, z),
            applyMastery: true,
            applySlag: true);
    }

    public static void ProcessRemoveSlagPostfix(
        BlockEntityAnvil anvil,
        IPlayer byPlayer,
        int x,
        int z,
        int y)
    {
        AfterKnapsterHit(
            anvil,
            byPlayer,
            new Vec3i(x, y, z),
            applyMastery: false,
            applySlag: true);
    }

    public static void ProcessSplitPrefix(
        BlockEntityAnvil anvil,
        IPlayer byPlayer,
        Vec3i usableMetalVoxel)
    {
        if (byPlayer == null || usableMetalVoxel == null)
        {
            return;
        }

        AnvilMetalRecoveryOps.OnMetalSplit(anvil, byPlayer, usableMetalVoxel);
    }

    public static void ProcessSplitPostfix(BlockEntityAnvil anvil, IPlayer byPlayer, Vec3i usableMetalVoxel)
    {
        AfterKnapsterHit(
            anvil,
            byPlayer,
            usableMetalVoxel,
            applyMastery: false,
            applySlag: false);
    }

    static void AfterKnapsterHit(
        BlockEntityAnvil anvil,
        IPlayer? player,
        Vec3i? voxelPos,
        bool applyMastery,
        bool applySlag)
    {
        if (player == null)
        {
            return;
        }

        if (applyMastery && voxelPos != null)
        {
            AnvilHeavyHitOps.ApplyMastery(anvil, player, voxelPos);
        }

        if (applySlag && voxelPos != null)
        {
            AnvilHeavyHitOps.ApplySlagClear(anvil, player, voxelPos);
        }

        AnvilHeatedStrikesOps.OnStrike(anvil, player);
        AnvilXpStation.TryAwardProgress(anvil, player);
    }
}
