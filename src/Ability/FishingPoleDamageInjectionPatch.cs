using HarmonyLib;
using Prosequor.Xp.Adapters;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.GameContent;

namespace Prosequor.Ability;

/// <summary>
/// Injects fishing-pole <see cref="CollectibleObject.DamageItem"/> calls at the same
/// sites as vssurvivalmod PR #194 (Vintage Story issue #9241). Vanilla 1.22 never
/// damages poles on catch or rope snap; remove this file once upstream includes those calls.
/// </summary>
[HarmonyPatch(typeof(EntityBobber), nameof(EntityBobber.TryCatchFish))]
[HarmonyBefore("Prosequor.Xp.Adapters.EntityBobberTryCatchFishXpPatch")]
public static class EntityBobberTryCatchFishFishingPoleDamagePatch
{
    [HarmonyPostfix]
    public static void Postfix(EntityBobber __instance, EntityAgent entityCatcher)
    {
        if (__instance?.World?.Side != EnumAppSide.Server || !FishingCatchScope.HadCatchGive)
        {
            return;
        }

        ItemSlot? slot = entityCatcher?.ActiveHandItemSlot;
        CollectibleObject? collectible = slot?.Itemstack?.Collectible;
        if (collectible == null)
        {
            return;
        }

        collectible.DamageItem(__instance.World, entityCatcher, slot);
        slot?.MarkDirty();
    }
}

[HarmonyPatch(typeof(EntityBobber), nameof(EntityBobber.OnRopeRipped))]
public static class EntityBobberOnRopeRippedFishingPoleDamagePatch
{
    [HarmonyPrefix]
    public static void Prefix(EntityBobber __instance)
    {
        if (__instance?.Api?.World?.Side != EnumAppSide.Server)
        {
            return;
        }

        EntityAgent? entity = __instance.Api.World.GetEntityById(__instance.AttachedToEntityId) as EntityAgent;
        ItemSlot? slot = entity?.ActiveHandItemSlot;
        CollectibleObject? item = slot?.Itemstack?.Collectible;
        if (item is ItemFishingPole && slot != null)
        {
            item.DamageItem(__instance.Api.World, entity, slot);
            slot.MarkDirty();
        }
    }
}
