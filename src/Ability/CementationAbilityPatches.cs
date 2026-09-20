using HarmonyLib;
using System.Reflection;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace Prosequor.Ability;

/// <summary>
/// Cementation furnace: load/fuel contributor stamps and processComplete XP settle.
/// </summary>
public static class CementationAbilityPatches
{
    [HarmonyPatch(typeof(BlockEntityStoneCoffin), nameof(BlockEntityStoneCoffin.Interact))]
    public static class StoneCoffinInteractScopePatch
    {
        [HarmonyPrefix]
        public static void Prefix(IPlayer byPlayer) => CementationLoadScope.Begin(byPlayer);

        [HarmonyPostfix]
        public static void Postfix() => CementationLoadScope.End();
    }

    [HarmonyPatch(typeof(BlockEntityStoneCoffin), "AddIngot")]
    public static class StoneCoffinAddIngotContributePatch
    {
        [HarmonyPostfix]
        public static void Postfix(BlockEntityStoneCoffin __instance, bool __result)
        {
            if (__result)
            {
                CementationXpStation.ContributeFromLoad(__instance);
            }
        }
    }

    [HarmonyPatch(typeof(BlockEntityStoneCoffin), "AddCoal")]
    public static class StoneCoffinAddCoalContributePatch
    {
        [HarmonyPostfix]
        public static void Postfix(BlockEntityStoneCoffin __instance, bool __result)
        {
            if (__result)
            {
                CementationXpStation.ContributeFromLoad(__instance);
            }
        }
    }

    [HarmonyPatch(typeof(BlockEntityStoneCoffin), "onServerTick3s")]
    public static class StoneCoffinProcessCompletePatch
    {
        [HarmonyPostfix]
        public static void Postfix(BlockEntityStoneCoffin __instance) =>
            CementationXpStation.OnAfterServerTick(__instance);
    }

    [HarmonyPatch(typeof(BlockEntityStoneCoffin), nameof(BlockEntityStoneCoffin.ToTreeAttributes))]
    public static class StoneCoffinPaidToTreePatch
    {
        [HarmonyPostfix]
        public static void Postfix(BlockEntityStoneCoffin __instance, ITreeAttribute tree) =>
            CementationXpStation.WriteToTree(__instance, tree);
    }

    [HarmonyPatch(typeof(BlockEntityStoneCoffin), nameof(BlockEntityStoneCoffin.FromTreeAttributes))]
    public static class StoneCoffinPaidFromTreePatch
    {
        [HarmonyPostfix]
        public static void Postfix(BlockEntityStoneCoffin __instance, ITreeAttribute tree) =>
            CementationXpStation.ReadFromTree(__instance, tree);
    }

    /// <summary>Player-lit coal piles under a cementation furnace contribute.</summary>
    [HarmonyPatch(typeof(BlockCoalPile), nameof(BlockCoalPile.OnTryIgniteBlockOver))]
    public static class CementationCoalPileIgnitePatch
    {
        [HarmonyPostfix]
        public static void Postfix(EntityAgent byEntity, BlockPos pos)
        {
            if (byEntity?.World?.Side != EnumAppSide.Server || pos == null)
            {
                return;
            }

            IPlayer? player = byEntity is EntityPlayer ep
                ? byEntity.World.PlayerByUid(ep.PlayerUID)
                : null;
            CementationXpStation.ContributeAtFuel(byEntity.World, pos, player);
        }
    }

    /// <summary>Adding coal to a pile under a cementation furnace contributes.</summary>
    [HarmonyPatch(typeof(BlockEntityCoalPile), nameof(BlockEntityCoalPile.OnPlayerInteract))]
    public static class CementationCoalPileFuelContributePatch
    {
        [HarmonyPostfix]
        public static void Postfix(BlockEntityCoalPile __instance, IPlayer byPlayer, bool __result)
        {
            if (!__result
                || __instance?.Api?.Side != EnumAppSide.Server
                || byPlayer?.PlayerUID == null
                || __instance.Pos == null)
            {
                return;
            }

            CementationXpStation.ContributeAtFuel(
                __instance.Api.World,
                __instance.Pos,
                byPlayer);
        }
    }

    /// <summary>
    /// Optional: <c>BEBehaviorBurning.OnFirePlaced</c> — fuel lit via fire spread / placed fire.
    /// Wired from <see cref="TryPatchOptionalIgniters"/>.
    /// </summary>
    public static class CementationBurningOnFirePlacedPatch
    {
        public static void Postfix(
            BlockPos firePos,
            BlockPos fuelPos,
            string startedByPlayerUid,
            BEBehaviorBurning __instance)
        {
            ICoreAPI? api = __instance?.Blockentity?.Api;
            if (api?.Side != EnumAppSide.Server
                || string.IsNullOrEmpty(startedByPlayerUid)
                || fuelPos == null)
            {
                return;
            }

            IPlayer? player = api.World.PlayerByUid(startedByPlayerUid);
            CementationXpStation.ContributeAtFuel(api.World, fuelPos, player);
        }
    }

    /// <summary>Patches fuel ignite helpers that may be missing on some VS builds.</summary>
    public static void TryPatchOptionalIgniters(Harmony harmony)
    {
        if (harmony == null)
        {
            return;
        }

        MethodInfo? onFirePlaced = AccessTools.Method(
            typeof(BEBehaviorBurning),
            "OnFirePlaced",
            [typeof(BlockPos), typeof(BlockPos), typeof(string)]);
        if (onFirePlaced == null)
        {
            return;
        }

        harmony.Patch(
            onFirePlaced,
            postfix: new HarmonyMethod(
                typeof(CementationBurningOnFirePlacedPatch),
                nameof(CementationBurningOnFirePlacedPatch.Postfix)));
    }
}
