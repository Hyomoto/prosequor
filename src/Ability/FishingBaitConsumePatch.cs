using HarmonyLib;
using Prosequor.Ability.Hooks;
using Prosequor.Player;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.GameContent;

namespace Prosequor.Ability;

/// <summary>
/// Thin bait-consume adapter into <see cref="ConsumeBaitStation"/>. Snapshots bait before
/// vanilla clears it, then runs <c>restock</c> (free restore and/or inventory refill).
/// </summary>
[HarmonyPatch(typeof(EntityBobber), nameof(EntityBobber.TryCatchFish))]
[HarmonyBefore("Prosequor.Xp.Adapters.EntityBobberTryCatchFishXpPatch")]
public static class EntityBobberTryCatchFishBaitConsumePatch
{
    [ThreadStatic]
    static ItemStack? baitSnapshot;

    [HarmonyPrefix]
    public static void Prefix(EntityBobber __instance)
    {
        baitSnapshot = null;
        if (__instance?.World?.Side != EnumAppSide.Server || __instance.BaitStack == null)
        {
            return;
        }

        baitSnapshot = __instance.BaitStack.Clone();
    }

    [HarmonyPostfix]
    public static void Postfix(EntityBobber __instance, EntityAgent entityCatcher)
    {
        ApplyConsume(__instance, entityCatcher);
    }

    [HarmonyFinalizer]
    public static void Finalizer(EntityBobber __instance, EntityAgent entityCatcher, Exception? __exception)
    {
        // If Postfix did not run, still clear the snapshot to avoid cross-call leakage.
        if (baitSnapshot != null)
        {
            ApplyConsume(__instance, entityCatcher);
        }
    }

    static void ApplyConsume(EntityBobber? bobber, EntityAgent? entityCatcher)
    {
        ItemStack? snapshot = baitSnapshot;
        baitSnapshot = null;
        if (snapshot == null
            || bobber?.World?.Side != EnumAppSide.Server
            || bobber.BaitStack != null)
        {
            return;
        }

        if (entityCatcher is not EntityPlayer entityPlayer)
        {
            return;
        }

        IPlayer? player = entityPlayer.Player;
        IPlayerProgress? progress = player == null ? null : ProsequorModSystem.GetProgress(player);
        if (player == null || progress == null)
        {
            return;
        }

        ConsumeBaitContext context = ConsumeBaitStation.BuildContext(
            bobber.World,
            player,
            progress,
            snapshot,
            entityCatcher.ActiveHandItemSlot);

        ConsumeBaitStation.TryRestock(context, bobber);
    }
}
