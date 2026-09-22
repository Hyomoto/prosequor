using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;

namespace Prosequor.Ability;

/// <summary>
/// Medicine bleed-out: lengthens the mortally-wounded revive window by Medicine level.
/// </summary>
[HarmonyPatch(typeof(EntityPlayer), nameof(EntityPlayer.RevivableIngameHoursLeft))]
public static class BleedOutAbilityPatches
{
    [HarmonyPostfix]
    public static void Postfix(EntityPlayer __instance, ref double __result)
    {
        if (__instance == null || __instance.Alive || __result < 0)
        {
            return;
        }

        IPlayer? player = __instance.Player;
        if (player == null)
        {
            return;
        }

        float rate = MedicineStation.ResolveBleedOutRate(player);
        if (Math.Abs(rate - 1f) < 0.0001f)
        {
            return;
        }

        double deathTotalHours = __instance.WatchedAttributes.GetDouble("deathTotalHours", -9999);
        double hoursDead = __instance.Api.World.Calendar.TotalHours - deathTotalHours;
        double baseHours = __instance.Api.World.Config.GetDecimal("playerRevivableHourAmount", 0.5);
        __result = MedicineStation.ScaleBleedOutHoursLeft(baseHours, hoursDead, rate);
    }
}
