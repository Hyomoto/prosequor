using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.GameContent;

namespace Prosequor.Ability;

/// <summary>Field Expertise scythe multi-break quantity (shears unaffected).</summary>
[HarmonyPatch(typeof(ItemShears), nameof(ItemShears.OnBlockBrokenWith))]
public static class ItemScytheMultiBreakBrokenPatch
{
    [HarmonyPrefix]
    public static void Prefix(ItemShears __instance, Entity byEntity)
    {
        if (__instance is not ItemScythe)
        {
            return;
        }

        ScytheMultiBreakStation.Begin(byEntity, __instance.MultiBreakQuantity);
    }

    [HarmonyFinalizer]
    public static void Finalizer(ItemShears __instance)
    {
        if (__instance is ItemScythe)
        {
            ScytheMultiBreakStation.End();
        }
    }
}

[HarmonyPatch(typeof(ItemShears), nameof(ItemShears.OnBlockBreaking))]
public static class ItemScytheMultiBreakBreakingPatch
{
    [HarmonyPrefix]
    public static void Prefix(ItemShears __instance, IPlayer player)
    {
        if (__instance is not ItemScythe || player?.Entity == null)
        {
            return;
        }

        ScytheMultiBreakStation.Begin(player.Entity, __instance.MultiBreakQuantity);
    }

    [HarmonyFinalizer]
    public static void Finalizer(ItemShears __instance)
    {
        if (__instance is ItemScythe)
        {
            ScytheMultiBreakStation.End();
        }
    }
}

[HarmonyPatch(typeof(ItemScythe), "get_MultiBreakQuantity")]
public static class ItemScytheMultiBreakQuantityPatch
{
    [HarmonyPostfix]
    public static void Postfix(ref int __result)
    {
        ScytheMultiBreakStation.TryGetActive(ref __result);
    }
}
