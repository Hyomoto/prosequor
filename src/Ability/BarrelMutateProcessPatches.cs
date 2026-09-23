using System.Runtime.CompilerServices;
using HarmonyLib;
using Prosequor.Xp.Activity;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace Prosequor.Ability;

/// <summary>
/// Barrel pour and seal each add a contributor once. Seal stamps the closer as
/// MakerUid on the waiting liquid. Craft completion builds a new pedigree from
/// that sealer (maker + fermented quality roll) and pays the bag. The juice
/// pedigree is not copied. No sealer → anonymous output, no XP.
/// </summary>
public static class BarrelMutateProcessPatches
{
    const string SealerAttr = "prosequorBarrelSealer";
    const int LiquidSlot = 1;

    static readonly ConditionalWeakTable<BlockEntityBarrel, Session> sessions = new();

    sealed class Session
    {
        public string? LastActorUid;
        public string? PendingSealerUid;
    }

    public sealed class SealCompleteState
    {
        public bool WasSealed;
        public string? SealerUid;
        public IReadOnlyList<Deed.ContributorShare> Shares = Array.Empty<Deed.ContributorShare>();
    }

    [HarmonyPatch(typeof(BlockBarrel), nameof(BlockBarrel.OnBlockInteractStart))]
    public static class BarrelPourActorPatch
    {
        [HarmonyPrefix]
        public static void Prefix(
            IWorldAccessor world,
            IPlayer byPlayer,
            BlockSelection blockSel,
            out int __state)
        {
            __state = 0;
            if (!TryGetUnsealed(world, blockSel, out BlockEntityBarrel? barrel) || byPlayer?.PlayerUID == null)
            {
                return;
            }

            RememberActor(barrel!, byPlayer.PlayerUID);
            __state = LiquidSize(barrel!);
        }

        [HarmonyPostfix]
        public static void Postfix(
            IWorldAccessor world,
            IPlayer byPlayer,
            BlockSelection blockSel,
            bool __result,
            int __state)
        {
            if (!__result
                || byPlayer?.PlayerUID == null
                || !TryGetUnsealed(world, blockSel, out BlockEntityBarrel? barrel))
            {
                return;
            }

            // Opening the dialog also returns true. Only a grown liquid slot is a pour.
            // GUI inserts arrive later via slot-modified and use the opener from packet 1000.
            if (LiquidSize(barrel!) <= __state)
            {
                return;
            }

            CreditPour(barrel!, byPlayer.PlayerUID);
        }
    }

    [HarmonyPatch(typeof(BlockEntityBarrel), "Inventory_SlotModified")]
    public static class BarrelSlotPourCreditPatch
    {
        [HarmonyPrefix]
        public static void Prefix(BlockEntityBarrel __instance, out int __state) =>
            __state = LiquidSize(__instance);

        [HarmonyPostfix]
        public static void Postfix(BlockEntityBarrel __instance, int __state)
        {
            if (__instance.Api?.World?.Side != EnumAppSide.Server || __instance.Sealed)
            {
                return;
            }

            if (LiquidSize(__instance) <= __state)
            {
                return;
            }

            CreditPour(__instance, ActorOf(__instance));
        }
    }

    [HarmonyPatch(typeof(BlockEntityBarrel), nameof(BlockEntityBarrel.OnReceivedClientPacket))]
    public static class BarrelSealStarterStampPatch
    {
        [HarmonyPrefix]
        public static void Prefix(BlockEntityBarrel __instance, int packetid, out bool __state) =>
            __state = packetid == 1337 && !__instance.Sealed;

        [HarmonyPostfix]
        public static void Postfix(
            BlockEntityBarrel __instance,
            IPlayer player,
            int packetid,
            bool __state)
        {
            if (player?.PlayerUID == null || __instance.Api?.World?.Side != EnumAppSide.Server)
            {
                return;
            }

            if (packetid == 1000)
            {
                RememberActor(__instance, player.PlayerUID);
                return;
            }

            if (!__state || !__instance.Sealed)
            {
                return;
            }

            LockSealer(__instance, player.PlayerUID);
        }
    }

    [HarmonyPatch(typeof(BlockEntityBarrel), "OnEvery3Second")]
    public static class BarrelCraftCompletePatch
    {
        [HarmonyPrefix]
        public static void Prefix(BlockEntityBarrel __instance, out SealCompleteState __state)
        {
            __state = new SealCompleteState
            {
                WasSealed = __instance.Sealed,
                SealerUid = SealerOf(__instance),
                Shares = SnapshotShares(__instance)
            };
        }

        [HarmonyPostfix]
        public static void Postfix(BlockEntityBarrel __instance, SealCompleteState __state)
        {
            if (__instance.Api?.World?.Side != EnumAppSide.Server
                || !__state.WasSealed
                || __instance.Sealed)
            {
                return;
            }

            // Fresh recipe stack. No sealer → do not fall back to the juice maker.
            if (string.IsNullOrEmpty(__state.SealerUid))
            {
                ClearProcess(__instance);
                return;
            }

            bool changed = false;
            string sealer = __state.SealerUid;
            for (int i = 0; i < __instance.Inventory.Count; i++)
            {
                ItemSlot slot = __instance.Inventory[i];
                if (slot?.Itemstack == null)
                {
                    continue;
                }

                if (MutateProcessStation.TryApply(
                        __instance.Api.World,
                        sealer,
                        slot,
                        AbilityBootstrap.TokenBarrel))
                {
                    changed = true;
                }

                if (slot.Itemstack == null)
                {
                    continue;
                }

                string? before = CraftAttribution.TryGetMakerUid(slot.Itemstack);
                CraftMutateOutputStation.ApplyAttributes(__instance.Api.World, sealer, slot.Itemstack);
                if (!string.Equals(before, CraftAttribution.TryGetMakerUid(slot.Itemstack), StringComparison.Ordinal))
                {
                    changed = true;
                }

                CraftedProductXp.Emit(
                    __instance.Api.World,
                    sealer,
                    slot.Itemstack,
                    slot.Itemstack.StackSize,
                    contributors: __state.Shares,
                    makerUid: sealer);
            }

            ClearProcess(__instance);
            if (changed)
            {
                __instance.MarkDirty(true);
            }
        }
    }

    /// <summary>
    /// Restore the closer after chunk unpack from Live maker, or one-shot legacy
    /// <c>prosequorBarrelSealer</c> / flat maker on an already-sealed barrel.
    /// Runs before <c>Initialize</c>, so write chunk pedigree via <paramref name="world"/>.
    /// </summary>
    [HarmonyPatch(typeof(BlockEntityBarrel), nameof(BlockEntityBarrel.FromTreeAttributes))]
    public static class BarrelLegacySealerMigratePatch
    {
        [HarmonyPostfix]
        public static void Postfix(
            BlockEntityBarrel __instance,
            ITreeAttribute tree,
            IWorldAccessor worldForResolving)
        {
            if (tree == null || worldForResolving == null || !string.IsNullOrEmpty(SealerOf(__instance, worldForResolving)))
            {
                return;
            }

            string? sealer = tree.GetString(SealerAttr);
            if (string.IsNullOrWhiteSpace(sealer) && tree.GetBool("sealed", false))
            {
                sealer = tree.GetString(CraftAttribution.MakerAttr);
                if (string.IsNullOrWhiteSpace(sealer)
                    && TryGetSoleContributorAt(worldForResolving, __instance.Pos, out string? sole))
                {
                    sealer = sole;
                }
            }

            if (string.IsNullOrWhiteSpace(sealer))
            {
                return;
            }

            AdoptSealerAt(worldForResolving, __instance, sealer);
        }
    }

    static void LockSealer(BlockEntityBarrel barrel, string uid)
    {
        AdoptSealer(barrel, uid);
        StampLiquidMaker(barrel, uid.Trim());
        barrel.MarkDirty(true);
    }

    static void AdoptSealer(BlockEntityBarrel barrel, string uid)
    {
        if (barrel.Api?.World is IWorldAccessor world)
        {
            AdoptSealerAt(world, barrel, uid);
            return;
        }

        // Pre-Initialize: remember until a later call with world.
        SessionOf(barrel).PendingSealerUid = uid.Trim();
    }

    static void AdoptSealerAt(IWorldAccessor world, BlockEntityBarrel barrel, string uid)
    {
        string trimmed = uid.Trim();
        if (sessions.TryGetValue(barrel, out Session? session))
        {
            session.LastActorUid = null;
            session.PendingSealerUid = null;
        }

        ProsequorChunkPedigree.Box box = ProsequorChunkPedigree.GetOrCreate(world, barrel.Pos);
        box.Blob = box.Blob.WithMaker(trimmed);
        if (WeightOfBlob(box.Blob, trimmed) <= 0)
        {
            box.Blob = box.Blob.WithContributor(trimmed, 1);
        }

        ProsequorChunkPedigree.Set(world, barrel.Pos, box);
    }

    static void RememberActor(BlockEntityBarrel barrel, string uid) =>
        SessionOf(barrel).LastActorUid = uid.Trim();

    static void CreditPour(BlockEntityBarrel barrel, string? uid)
    {
        if (barrel.Sealed)
        {
            return;
        }

        AddOnce(barrel, uid);
    }

    static void AddOnce(BlockEntityBarrel barrel, string? uid)
    {
        if (string.IsNullOrWhiteSpace(uid))
        {
            return;
        }

        string trimmed = uid.Trim();
        if (WeightOf(barrel, trimmed) > 0)
        {
            return;
        }

        ProsequorBlockPedigreeStation.AddContributor(barrel, trimmed, 1);
    }

    static void StampLiquidMaker(BlockEntityBarrel barrel, string uid)
    {
        ItemStack? liquid = LiquidStack(barrel);
        if (liquid == null)
        {
            return;
        }

        CraftAttribution.StampMakerUid(liquid, uid);
        if (barrel.Api != null && barrel.Inventory != null && barrel.Inventory.Count > LiquidSlot)
        {
            barrel.Inventory[LiquidSlot].MarkDirty();
        }
    }

    static void ClearProcess(BlockEntityBarrel barrel)
    {
        ProsequorBlockPedigreeStation.Clear(barrel);
        if (sessions.TryGetValue(barrel, out Session? session))
        {
            session.LastActorUid = null;
            session.PendingSealerUid = null;
        }
    }

    static string? SealerOf(BlockEntityBarrel barrel) =>
        SealerOf(barrel, barrel.Api?.World);

    static string? SealerOf(BlockEntityBarrel barrel, IWorldAccessor? world)
    {
        if (world != null
            && ProsequorChunkPedigree.TryGet(world, barrel.Pos, out ProsequorChunkPedigree.Box box)
            && !string.IsNullOrWhiteSpace(box.Blob.MakerUid))
        {
            return box.Blob.MakerUid;
        }

        if (sessions.TryGetValue(barrel, out Session? session)
            && !string.IsNullOrWhiteSpace(session.PendingSealerUid))
        {
            return session.PendingSealerUid;
        }

        return null;
    }

    static string? ActorOf(BlockEntityBarrel barrel) =>
        sessions.TryGetValue(barrel, out Session? session) ? session.LastActorUid : null;

    static Session SessionOf(BlockEntityBarrel barrel) => sessions.GetOrCreateValue(barrel);

    static bool TryGetSoleContributorAt(IWorldAccessor world, BlockPos pos, out string? uid)
    {
        uid = null;
        return ProsequorChunkPedigree.TryGet(world, pos, out ProsequorChunkPedigree.Box box)
            && box.Blob.TryGetSoleContributor(out uid);
    }

    static int WeightOfBlob(ProsequorBlob blob, string uid)
    {
        for (int i = 0; i < blob.Contributors.Count; i++)
        {
            if (string.Equals(blob.Contributors[i].PlayerUid, uid, StringComparison.Ordinal))
            {
                return blob.Contributors[i].Weight;
            }
        }

        return 0;
    }

    static IReadOnlyList<Deed.ContributorShare> SnapshotShares(BlockEntityBarrel barrel)
    {
        if (!ProsequorBlockPedigreeStation.TryGetBlob(barrel, out ProsequorBlob blob)
            || blob.Contributors.Count == 0)
        {
            return Array.Empty<Deed.ContributorShare>();
        }

        List<Deed.ContributorShare> shares = new(blob.Contributors.Count);
        foreach (ProsequorBlob.Share share in blob.Contributors)
        {
            if (string.IsNullOrEmpty(share.PlayerUid) || share.Weight <= 0)
            {
                continue;
            }

            shares.Add(new Deed.ContributorShare(share.PlayerUid, share.Weight));
        }

        return shares;
    }

    static int WeightOf(BlockEntityBarrel barrel, string uid)
    {
        if (!ProsequorBlockPedigreeStation.TryGetBlob(barrel, out ProsequorBlob blob))
        {
            return 0;
        }

        foreach (ProsequorBlob.Share share in blob.Contributors)
        {
            if (string.Equals(share.PlayerUid, uid, StringComparison.Ordinal))
            {
                return share.Weight;
            }
        }

        return 0;
    }

    static int LiquidSize(BlockEntityBarrel barrel) => LiquidStack(barrel)?.StackSize ?? 0;

    static ItemStack? LiquidStack(BlockEntityBarrel barrel)
    {
        if (barrel.Inventory == null || barrel.Inventory.Count <= LiquidSlot)
        {
            return null;
        }

        return barrel.Inventory[LiquidSlot]?.Itemstack;
    }

    static bool TryGetUnsealed(
        IWorldAccessor? world,
        BlockSelection? blockSel,
        out BlockEntityBarrel? barrel)
    {
        barrel = null;
        if (world?.Side != EnumAppSide.Server || blockSel?.Position == null)
        {
            return false;
        }

        if (world.BlockAccessor.GetBlockEntity(blockSel.Position) is not BlockEntityBarrel found
            || found.Sealed)
        {
            return false;
        }

        barrel = found;
        return true;
    }
}
