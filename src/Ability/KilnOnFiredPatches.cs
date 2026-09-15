using HarmonyLib;
using Prosequor.Xp;
using Prosequor.Xp.Activity;
using System.Reflection;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace Prosequor.Ability;

/// <summary>Pit / beehive kiln contribution stamps and fire-complete XP settle.</summary>
public static class KilnFireXpPatches
{
    [HarmonyPatch(typeof(BlockEntityPitKiln), nameof(BlockEntityPitKiln.OnPlayerInteractStart))]
    public static class PitKilnBuildContributePatch
    {
        static readonly AccessTools.FieldRef<BlockEntityPitKiln, int>? CurrentBuildStageField =
            AccessTools.FieldRefAccess<BlockEntityPitKiln, int>("currentBuildStage");

        [HarmonyPrefix]
        public static void Prefix(BlockEntityPitKiln __instance, out int __state) =>
            __state = CurrentBuildStageField != null ? CurrentBuildStageField(__instance) : -1;

        [HarmonyPostfix]
        public static void Postfix(BlockEntityPitKiln __instance, IPlayer player, int __state)
        {
            if (__instance?.Api?.Side != EnumAppSide.Server
                || player?.PlayerUID == null
                || CurrentBuildStageField == null
                || __state < 0
                || CurrentBuildStageField(__instance) <= __state)
            {
                return;
            }

            ContributeContentSlots(__instance, player);
        }
    }

    [HarmonyPatch(typeof(BlockEntityPitKiln), nameof(BlockEntityPitKiln.OnCreated))]
    public static class PitKilnCreatedContributePatch
    {
        [HarmonyPostfix]
        public static void Postfix(BlockEntityPitKiln __instance, IPlayer byPlayer)
        {
            if (__instance?.Api?.Side != EnumAppSide.Server || byPlayer?.PlayerUID == null)
            {
                return;
            }

            ContributeContentSlots(__instance, byPlayer);
        }
    }

    [HarmonyPatch(typeof(BlockEntityPitKiln), nameof(BlockEntityPitKiln.TryIgnite))]
    public static class PitKilnTryIgniteStampPatch
    {
        [HarmonyPostfix]
        public static void Postfix(BlockEntityPitKiln __instance, IPlayer byPlayer)
        {
            if (__instance?.Api?.Side != EnumAppSide.Server
                || byPlayer?.PlayerUID == null
                || !__instance.Lit)
            {
                return;
            }

            ContributeContentSlots(__instance, byPlayer);
        }
    }

    [HarmonyPatch(typeof(BlockEntityBeeHiveKiln), "OnServerTick3s")]
    public static class BeeHiveKilnHeatSessionPatch
    {
        [HarmonyPostfix]
        public static void Postfix(BlockEntityBeeHiveKiln __instance) =>
            BeeHiveKilnFirerStation.OnAfterServerTick(__instance);
    }

    [HarmonyPatch(typeof(BlockEntityBeeHiveKiln), nameof(BlockEntityBeeHiveKiln.ToTreeAttributes))]
    public static class BeeHiveKilnFirerToTreePatch
    {
        [HarmonyPostfix]
        public static void Postfix(BlockEntityBeeHiveKiln __instance, ITreeAttribute tree) =>
            BeeHiveKilnFirerStation.WriteToTree(__instance, tree);
    }

    [HarmonyPatch(typeof(BlockEntityBeeHiveKiln), nameof(BlockEntityBeeHiveKiln.FromTreeAttributes))]
    public static class BeeHiveKilnFirerFromTreePatch
    {
        [HarmonyPostfix]
        public static void Postfix(BlockEntityBeeHiveKiln __instance, ITreeAttribute tree) =>
            BeeHiveKilnFirerStation.ReadFromTree(__instance, tree);
    }

    /// <summary>Player-lit coal piles under a beehive kiln note the igniter.</summary>
    [HarmonyPatch(typeof(BlockCoalPile), nameof(BlockCoalPile.OnTryIgniteBlockOver))]
    public static class CoalPileIgniteNotePatch
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
            BeeHiveKilnFirerStation.NoteIgniterAtFuel(byEntity.World, pos, player);
        }
    }

    /// <summary>Adding coal to a pile under a beehive kiln contributes on chamber pottery.</summary>
    [HarmonyPatch(typeof(BlockEntityCoalPile), nameof(BlockEntityCoalPile.OnPlayerInteract))]
    public static class CoalPileFuelContributePatch
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

            BeeHiveKilnFirerStation.ContributeChamberAtFuel(
                __instance.Api.World,
                __instance.Pos,
                byPlayer);
        }
    }

    /// <summary>
    /// Optional: <c>BEBehaviorBurning.OnFirePlaced</c> signature varies by game build.
    /// Wired from <see cref="TryPatchOptionalIgniters"/>.
    /// </summary>
    public static class BurningOnFirePlacedNotePatch
    {
        public static void Postfix(BlockPos firePos, BlockPos fuelPos, string startedByPlayerUid, BEBehaviorBurning __instance)
        {
            ICoreAPI? api = __instance?.Blockentity?.Api;
            if (api?.Side != EnumAppSide.Server || string.IsNullOrEmpty(startedByPlayerUid) || fuelPos == null)
            {
                return;
            }

            IPlayer? player = api.World.PlayerByUid(startedByPlayerUid);
            BeeHiveKilnFirerStation.NoteIgniterAtFuel(api.World, fuelPos, player);
        }
    }

    /// <summary>Patches beehive fuel ignite helpers that may be missing on some VS builds.</summary>
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
                typeof(BurningOnFirePlacedNotePatch),
                nameof(BurningOnFirePlacedNotePatch.Postfix)));
    }

    static void ContributeContentSlots(BlockEntityPitKiln kiln, IPlayer player)
    {
        // Contents are slots 0–3; fuel/build materials are 4–9.
        bool dirty = false;
        for (int i = 0; i < Math.Min(4, kiln.Inventory.Count); i++)
        {
            ItemSlot slot = kiln.Inventory[i];
            if (slot.Itemstack == null)
            {
                continue;
            }

            CraftAttribution.AddContribution(slot.Itemstack, player);
            slot.MarkDirty();
            dirty = true;
        }

        if (dirty)
        {
            kiln.MarkDirty(redrawOnClient: false);
        }
    }
}

/// <summary>Unified kiln firing egress for pit and beehive kilns (Maker's Mark + fire XP).</summary>
public static class KilnOnFiredPatches
{
    public sealed class SlotAttribution
    {
        public IWorldAccessor? World;
        public string?[]? MakerUids;
        public IReadOnlyList<Deed.ContributorShare>?[]? ContributorShares;
        public string?[]? TargetCodes;
        public bool[]? HadStack;
    }

    public sealed class ItemAttribution
    {
        public IWorldAccessor? World;
        public string? MakerUid;
        public IReadOnlyList<Deed.ContributorShare>? ContributorShares;
        public string? TargetCode;
    }

    [HarmonyPatch(typeof(BlockEntityPitKiln), nameof(BlockEntityPitKiln.OnFired))]
    public static class PitKilnOnFiredPatch
    {
        [HarmonyPrefix]
        public static void Prefix(BlockEntityPitKiln __instance, ref SlotAttribution __state)
        {
            // Contents only (0–3); fuel slots are ignored for fire XP / Maker's Mark.
            const int contentSlots = 4;
            int count = Math.Min(contentSlots, __instance.Inventory.Count);
            __state = new SlotAttribution
            {
                // KillFire replaces the BE; Api is often null in postfix — keep world here.
                World = __instance.Api?.World,
                MakerUids = new string?[count],
                ContributorShares = new IReadOnlyList<Deed.ContributorShare>?[count],
                TargetCodes = new string?[count],
                HadStack = new bool[count]
            };
            for (int i = 0; i < count; i++)
            {
                ItemStack? stack = __instance.Inventory[i].Itemstack;
                __state.HadStack[i] = stack != null;
                __state.MakerUids[i] = CraftAttribution.TryGetMakerUid(stack);
                __state.ContributorShares[i] =
                    ProsequorStackPedigree.TryGetPrimaryBlob(stack, out ProsequorBlob blob)
                        ? ClayFireXpMath.SharesFromBlob(blob)
                        : Array.Empty<Deed.ContributorShare>();
                __state.TargetCodes[i] = EventFactBuilder.CodeOf(stack);
            }
        }

        // Finalizer (not Postfix): OnFired often throws / KillFire replaces the BE in incomplete
        // Atlas setups; settle must still run from the Prefix snapshot.
        [HarmonyFinalizer]
        public static void Finalizer(BlockEntityPitKiln __instance, SlotAttribution __state)
        {
            if (__state is null)
            {
                return;
            }

            IWorldAccessor? world = __state.World ?? __instance.Api?.World;
            if (__state.MakerUids == null
                || __state.ContributorShares == null
                || __state.HadStack == null
                || world == null)
            {
                return;
            }

            BlockPos? pos = __instance.Pos;
            // KillFire replaces the pit kiln with ground storage; prefer those slots for abilities.
            BlockEntityGroundStorage? ground = pos == null
                ? null
                : world.BlockAccessor.GetBlockEntity(pos) as BlockEntityGroundStorage;

            bool changed = false;
            for (int i = 0; i < __state.HadStack.Length; i++)
            {
                if (!__state.HadStack[i])
                {
                    continue;
                }

                string? makerUid = __state.MakerUids[i];
                IReadOnlyList<Deed.ContributorShare>? shares = __state.ContributorShares[i];
                string? targetCode = __state.TargetCodes?[i];

                ItemSlot? slot = null;
                if (ground != null && i < ground.Inventory.Count)
                {
                    slot = ground.Inventory[i];
                }
                else if (i < __instance.Inventory.Count)
                {
                    slot = __instance.Inventory[i];
                }

                if (slot?.Itemstack != null)
                {
                    if (!string.IsNullOrEmpty(makerUid)
                        && string.IsNullOrEmpty(CraftAttribution.TryGetMakerUid(slot.Itemstack)))
                    {
                        CraftAttribution.StampMakerUid(slot.Itemstack, makerUid);
                    }

                    if (shares != null)
                    {
                        for (int s = 0; s < shares.Count; s++)
                        {
                            Deed.ContributorShare share = shares[s];
                            if (!CraftAttribution.HasContributor(slot.Itemstack, share.PlayerUid))
                            {
                                CraftAttribution.AddContributionUid(
                                    slot.Itemstack,
                                    share.PlayerUid,
                                    Math.Max(1, (int)Math.Round(share.Weight)));
                            }
                        }
                    }

                    targetCode ??= EventFactBuilder.CodeOf(slot.Itemstack);

                    if (!string.IsNullOrEmpty(makerUid))
                    {
                        IPlayer? maker = world.PlayerByUid(makerUid);
                        if (maker != null && MutateProcessStation.TryApply(
                                world,
                                maker,
                                slot,
                                AbilityBootstrap.TokenFirePottery))
                        {
                            changed = true;
                        }
                    }
                }

                // XP settle uses snapshotted shares — still pays after KillFire empties the old BE.
                ClayFireXp.Settle(
                    world,
                    ClayFireXpMath.TokenPitKiln,
                    targetCode,
                    slot?.Itemstack,
                    shares);
            }

            if (changed)
            {
                ground?.MarkDirty(redrawOnClient: true);
            }
        }
    }

    [HarmonyPatch(typeof(BlockEntityBeeHiveKiln), "ConvertItemToBurned")]
    public static class BeeHiveKilnConvertOnFiredPatch
    {
        [HarmonyPrefix]
        public static void Prefix(
            BlockEntityBeeHiveKiln __instance,
            ItemSlot itemSlot,
            ref ItemAttribution __state)
        {
            BeeHiveKilnFirerStation.EnsureFirerOnStack(__instance, itemSlot?.Itemstack);
            IReadOnlyList<Deed.ContributorShare> shares =
                ProsequorStackPedigree.TryGetPrimaryBlob(itemSlot?.Itemstack, out ProsequorBlob blob)
                    ? ClayFireXpMath.SharesFromBlob(blob)
                    : Array.Empty<Deed.ContributorShare>();

            string? sessionFirer = BeeHiveKilnFirerStation.TryGetSessionFirerUid(__instance);
            if (shares.Count == 0 && !string.IsNullOrWhiteSpace(sessionFirer))
            {
                shares = [new Deed.ContributorShare(sessionFirer.Trim(), 1f)];
            }

            __state = new ItemAttribution
            {
                World = __instance.Api?.World,
                MakerUid = CraftAttribution.TryGetMakerUid(itemSlot?.Itemstack),
                ContributorShares = shares,
                TargetCode = EventFactBuilder.CodeOf(itemSlot?.Itemstack)
            };
        }

        [HarmonyPostfix]
        public static void Postfix(
            BlockEntityBeeHiveKiln __instance,
            ItemSlot itemSlot,
            ItemAttribution __state)
        {
            IWorldAccessor? world = __state.World ?? __instance.Api?.World;
            string? makerUid = __state.MakerUid;
            IReadOnlyList<Deed.ContributorShare>? shares = __state.ContributorShares;
            string? targetCode = __state.TargetCode;

            if (itemSlot?.Itemstack != null && world != null)
            {
                makerUid ??= CraftAttribution.TryGetMakerUid(itemSlot.Itemstack);
                if ((shares == null || shares.Count == 0)
                    && ProsequorStackPedigree.TryGetPrimaryBlob(itemSlot.Itemstack, out ProsequorBlob blob))
                {
                    shares = ClayFireXpMath.SharesFromBlob(blob);
                }

                targetCode ??= EventFactBuilder.CodeOf(itemSlot.Itemstack);

                if (!string.IsNullOrEmpty(makerUid)
                    && string.IsNullOrEmpty(CraftAttribution.TryGetMakerUid(itemSlot.Itemstack)))
                {
                    CraftAttribution.StampMakerUid(itemSlot.Itemstack, makerUid);
                }

                if (!string.IsNullOrEmpty(makerUid))
                {
                    IPlayer? maker = world.PlayerByUid(makerUid);
                    if (maker != null && MutateProcessStation.TryApply(
                            world,
                            maker,
                            itemSlot,
                            AbilityBootstrap.TokenFirePottery))
                    {
                        __instance.MarkDirty(redrawOnClient: true);
                    }
                }
            }

            if (world == null || shares == null || shares.Count == 0)
            {
                return;
            }

            ClayFireXp.Settle(
                world,
                ClayFireXpMath.TokenBeehiveKiln,
                targetCode,
                itemSlot?.Itemstack,
                shares);
        }
    }
}
