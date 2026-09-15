using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;

namespace Prosequor.Ability;

/// <summary>
/// Player hit that actually lands steps animal friendliness down.
/// Non-player sources (wolves, fall, fire) do not.
/// </summary>
[HarmonyPatch(typeof(Entity), nameof(Entity.ReceiveDamage))]
public static class HusbandryAttackPenaltyPatch
{
    [HarmonyPostfix]
    public static void Postfix(Entity __instance, DamageSource damageSource, bool __result)
    {
        if (!__result
            || __instance?.World?.Side != EnumAppSide.Server
            || damageSource?.GetCauseEntity() is not EntityPlayer attacker
            || attacker.Player == null
            || attacker.EntityId == __instance.EntityId)
        {
            return;
        }

        HusbandryFriendliness.ApplyAttackPenalty(__instance, attacker.Player.PlayerUID);
    }
}
