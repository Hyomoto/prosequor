using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace Prosequor.Ability;

[HarmonyPatch(
    typeof(BlockEntityAnvil),
    "OnUseOver",
    typeof(IPlayer),
    typeof(Vec3i),
    typeof(BlockSelection))]
public static class AnvilOnUseOverScopePatch
{
    [HarmonyPrefix]
    [HarmonyPriority(Priority.First)]
    public static void Prefix(IPlayer byPlayer) => AnvilHitScope.Begin(byPlayer);

    [HarmonyFinalizer]
    public static void Finalizer() => AnvilHitScope.End();
}

/// <summary>
/// Before vanilla flatten: Hammer Mastery places 3×3 hit-brush metal into downward recipe cells in radius.
/// After: Heavy Hits clears slag in radius (including slag-only strikes OnHit would no-op).
/// </summary>
[HarmonyPatch(typeof(BlockEntityAnvil), nameof(BlockEntityAnvil.OnHit))]
public static class AnvilOnHitAssistPatch
{
    [HarmonyPrefix]
    public static void Prefix(BlockEntityAnvil __instance, Vec3i voxelPos)
    {
        IPlayer? player = AnvilHitScope.CurrentPlayer;
        if (player == null)
        {
            return;
        }

        AnvilHeavyHitOps.ApplyMastery(__instance, player, voxelPos);
    }

    [HarmonyPostfix]
    public static void Postfix(BlockEntityAnvil __instance, Vec3i voxelPos)
    {
        IPlayer? player = AnvilHitScope.CurrentPlayer;
        if (player == null)
        {
            return;
        }

        AnvilHeavyHitOps.ApplySlagClear(__instance, player, voxelPos);
        AnvilHeatedStrikesOps.OnStrike(__instance, player);
        AnvilXpStation.TryAwardProgress(__instance, player);
    }
}

/// <summary>
/// Before vanilla clear: Metal Recovery counts metal splits on the work-item pedigree
/// and refunds a metal bit when the threshold is reached.
/// After: Heated Strikes shrinks cooling debt.
/// </summary>
[HarmonyPatch(typeof(BlockEntityAnvil), nameof(BlockEntityAnvil.OnSplit))]
public static class AnvilOnSplitRecoveryPatch
{
    [HarmonyPrefix]
    public static void Prefix(BlockEntityAnvil __instance, Vec3i voxelPos)
    {
        IPlayer? player = AnvilHitScope.CurrentPlayer;
        if (player == null)
        {
            return;
        }

        AnvilMetalRecoveryOps.OnMetalSplit(__instance, player, voxelPos);
    }

    [HarmonyPostfix]
    public static void Postfix(BlockEntityAnvil __instance)
    {
        IPlayer? player = AnvilHitScope.CurrentPlayer;
        if (player == null)
        {
            return;
        }

        AnvilHeatedStrikesOps.OnStrike(__instance, player);
        AnvilXpStation.TryAwardProgress(__instance, player);
    }
}

/// <summary>After upset: Heated Strikes shrinks cooling debt; award novel good voxels.</summary>
[HarmonyPatch(typeof(BlockEntityAnvil), nameof(BlockEntityAnvil.OnUpset))]
public static class AnvilOnUpsetHeatedStrikesPatch
{
    [HarmonyPostfix]
    public static void Postfix(BlockEntityAnvil __instance)
    {
        IPlayer? player = AnvilHitScope.CurrentPlayer;
        if (player == null)
        {
            return;
        }

        AnvilHeatedStrikesOps.OnStrike(__instance, player);
        AnvilXpStation.TryAwardProgress(__instance, player);
    }
}

/// <summary>
/// Aim-aware Bits Forging gate on anvil put: require unlock, capture aimed metal voxel
/// for <see cref="MetalBitAnvilWorkableBehavior.TryPlaceOn"/>.
/// </summary>
[HarmonyPatch(typeof(BlockEntityAnvil), "TryPut")]
public static class AnvilTryPutBitsForgingPatch
{
    [HarmonyPrefix]
    public static bool Prefix(
        BlockEntityAnvil __instance,
        IWorldAccessor world,
        IPlayer byPlayer,
        BlockSelection blockSel)
    {
        ItemStack? stack = byPlayer?.InventoryManager?.ActiveHotbarSlot?.Itemstack;
        if (!AnvilBitsForgingOps.IsMetalBit(stack?.Collectible))
        {
            return true;
        }

        if (!AnvilBitsForgingOps.HasBitsForging(byPlayer))
        {
            AnvilBitsForgingOps.ClientError(
                __instance.Api,
                __instance,
                "bitsforging",
                Lang.Get("prosequor:ingameerror-bitsforging-locked"));
            return false;
        }

        if (AnvilBitsForgingOps.TryDecodeAimVoxel(__instance, blockSel, out Vec3i aim))
        {
            AnvilBitAimScope.Begin(aim);
        }
        else
        {
            AnvilBitAimScope.Begin(null);
        }

        return true;
    }

    [HarmonyFinalizer]
    public static void Finalizer(BlockEntityAnvil __instance, IPlayer byPlayer)
    {
        AnvilBitAimScope.End();
        AnvilXpStation.TryAwardProgress(__instance, byPlayer);
    }
}

/// <summary>
/// With Bits Forging unlocked, holding a metal bit always aims-and-puts (never take /
/// rotate) so fine voxel selection is used for placement without requiring Shift.
/// </summary>
[HarmonyPatch(typeof(BlockEntityAnvil), "OnPlayerInteract")]
public static class AnvilInteractBitsForgingPatch
{
    static readonly System.Reflection.MethodInfo? TryPutMethod = AccessTools.Method(
        typeof(BlockEntityAnvil),
        "TryPut",
        new[] { typeof(IWorldAccessor), typeof(IPlayer), typeof(BlockSelection) });

    [HarmonyPrefix]
    public static bool Prefix(
        BlockEntityAnvil __instance,
        IWorldAccessor world,
        IPlayer byPlayer,
        BlockSelection blockSel,
        ref bool __result)
    {
        ItemStack? stack = byPlayer?.InventoryManager?.ActiveHotbarSlot?.Itemstack;
        if (!AnvilBitsForgingOps.IsMetalBit(stack?.Collectible)
            || !AnvilBitsForgingOps.HasBitsForging(byPlayer)
            || TryPutMethod == null)
        {
            return true;
        }

        // Leave tongs / wrench rotation to vanilla.
        EnumTool? tool = stack!.Collectible.GetTool(byPlayer!.InventoryManager.ActiveHotbarSlot);
        if (tool == EnumTool.Wrench)
        {
            return true;
        }

        object? invoked = TryPutMethod.Invoke(__instance, new object[] { world, byPlayer, blockSel });
        __result = invoked is true;
        return false;
    }
}

/// <summary>
/// High-water XP backstop when a complete-path (Knapster easy smithing) mutates
/// voxels without vanilla <c>OnHit</c>/<c>OnSplit</c>. Duplicate calls pay 0.
/// </summary>
[HarmonyPatch(typeof(BlockEntityAnvil), nameof(BlockEntityAnvil.CheckIfFinished))]
public static class AnvilCheckIfFinishedXpPatch
{
    [HarmonyPostfix]
    public static void Postfix(BlockEntityAnvil __instance, IPlayer byPlayer) =>
        AnvilXpStation.TryAwardProgress(__instance, byPlayer);
}

[HarmonyPatch(typeof(BlockEntityAnvil), nameof(BlockEntityAnvil.ToTreeAttributes))]
public static class AnvilXpToTreePatch
{
    [HarmonyPostfix]
    public static void Postfix(BlockEntityAnvil __instance, ITreeAttribute tree) =>
        AnvilXpStation.WriteToTree(__instance, tree);
}

[HarmonyPatch(typeof(BlockEntityAnvil), nameof(BlockEntityAnvil.FromTreeAttributes))]
public static class AnvilXpFromTreePatch
{
    [HarmonyPostfix]
    public static void Postfix(BlockEntityAnvil __instance, ITreeAttribute tree) =>
        AnvilXpStation.ReadFromTree(__instance, tree);
}
