using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.GameContent;

namespace Prosequor.Ability;

/// <summary>
/// Player fill → add contribution weight; animal <c>ConsumeOnePortion</c> → take one share.
/// </summary>
[HarmonyPatch(typeof(BlockEntityTrough), "OnInteract")]
public static class TroughContributionFillPatch
{
    [HarmonyPrefix]
    public static void Prefix(BlockEntityTrough __instance, out int __state)
    {
        __state = TroughContributionStation.CountFillPortions(__instance);
    }

    [HarmonyPostfix]
    public static void Postfix(
        BlockEntityTrough __instance,
        IPlayer byPlayer,
        bool __result,
        int __state)
    {
        if (!__result
            || byPlayer == null
            || string.IsNullOrEmpty(byPlayer.PlayerUID)
            || __instance.Api?.World?.Side != EnumAppSide.Server)
        {
            return;
        }

        int afterVanilla = TroughContributionStation.CountFillPortions(__instance);
        int delta = afterVanilla - __state;
        if (delta <= 0)
        {
            return;
        }

        // Fresh fill into an empty trough: drop any stale hopper-desynced entries.
        if (__state <= 0)
        {
            TroughContributionStation.Clear(__instance);
        }

        int qty = TroughFillStation.ResolveFillQuantity(byPlayer);
        int extras = Math.Max(0, (qty - 1) * delta);
        int deposited = delta;
        for (int i = 0; i < extras; i++)
        {
            if (!TroughContributionStation.TryDepositOnePortion(__instance, byPlayer))
            {
                break;
            }

            deposited++;
        }

        TroughContributionStation.AddContribution(__instance, byPlayer.PlayerUID, deposited);
    }
}

[HarmonyPatch(typeof(BlockEntityTrough), nameof(BlockEntityTrough.ConsumeOnePortion))]
public static class TroughContributionConsumePatch
{
    [HarmonyPrefix]
    public static void Prefix(BlockEntityTrough __instance, out string? __state)
    {
        __state = null;
        if (__instance.Inventory == null || __instance.Inventory.Empty)
        {
            return;
        }

        __state = EventFactBuilder.CodeOf(__instance.Inventory[0]?.Itemstack);
    }

    [HarmonyPostfix]
    public static void Postfix(
        BlockEntityTrough __instance,
        Entity entity,
        float __result,
        string? __state)
    {
        if (__result < 1f || __instance.Api?.World?.Side != EnumAppSide.Server)
        {
            return;
        }

        string? uid = null;
        TroughContributionStation.TryTakeContribution(__instance, out uid);

        // Before the friendliness roll so 5→6 does not invent the friendly token.
        AnimalFeedXp.Emit(
            __instance.Api,
            entity,
            uid,
            CallerIdentities.Trough,
            __state,
            __instance.Pos);

        float chance = TroughEatStation.ResolveFriendlinessChance(__instance.Api, uid, entity);
        if (__instance.Api.World.Rand.NextDouble() < chance)
        {
            // Credited meal → contributor uid; empty bag / hopper → @friendliness sentinel.
            HusbandryFriendliness.Add(entity, uid, 1);
        }

        if (TroughContributionStation.CountFillPortions(__instance) <= 0)
        {
            TroughContributionStation.Clear(__instance);
        }
    }
}

[HarmonyPatch(typeof(BlockEntityTrough), nameof(BlockEntityTrough.ToTreeAttributes))]
public static class TroughContributionToTreePatch
{
    [HarmonyPostfix]
    public static void Postfix(BlockEntityTrough __instance, ITreeAttribute tree)
    {
        TroughContributionStation.WriteToTree(__instance, tree);
    }
}

[HarmonyPatch(typeof(BlockEntityTrough), nameof(BlockEntityTrough.FromTreeAttributes))]
public static class TroughContributionFromTreePatch
{
    [HarmonyPostfix]
    public static void Postfix(BlockEntityTrough __instance, ITreeAttribute tree)
    {
        TroughContributionStation.ReadFromTree(__instance, tree);
    }
}
