using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;

namespace Prosequor.Ability;

/// <summary>
/// Appends Favorite Seraph on the entity info card via the same <see cref="OwnerCredit"/>
/// chrome as Planted By / Created By.
/// </summary>
[HarmonyPatch(typeof(Entity), nameof(Entity.GetInfoText))]
public static class HusbandryFavoriteSeraphInfoPatch
{
    [HarmonyPostfix]
    public static void Postfix(Entity __instance, ref string __result)
    {
        if (!HusbandryFriendliness.TryGetFavoriteSeraph(__instance, out string? uid))
        {
            return;
        }

        OwnerCredit.Append(ref __result, __instance.World, uid, OwnerCredit.FavoriteSeraphLang);
    }
}
