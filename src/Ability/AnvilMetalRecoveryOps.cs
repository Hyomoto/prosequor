using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace Prosequor.Ability;

/// <summary>
/// Metal Recovery: count metal <c>OnSplit</c>s on the work-item pedigree; refund one
/// matching metal bit when the count reaches the player's <c>bits-refund</c> threshold.
/// </summary>
public static class AnvilMetalRecoveryOps
{
    /// <summary>
    /// Pure counter step. Returns whether a bit should be granted; <paramref name="next"/> is
    /// the value to store (0 after a refund).
    /// </summary>
    public static bool TryAdvanceSplit(int current, int threshold, out int next)
    {
        if (threshold <= 0)
        {
            next = current;
            return false;
        }

        next = current + 1;
        if (next < threshold)
        {
            return false;
        }

        next = 0;
        return true;
    }

    public static void OnMetalSplit(BlockEntityAnvil anvil, IPlayer player, Vec3i voxelPos)
    {
        if (anvil?.Api?.Side != EnumAppSide.Server
            || anvil.Voxels == null
            || player == null
            || voxelPos == null
            || !AnvilVoxelGrid.InBounds(voxelPos.X, voxelPos.Y, voxelPos.Z)
            || anvil.Voxels[voxelPos.X, voxelPos.Y, voxelPos.Z] != AnvilVoxelGrid.Metal)
        {
            return;
        }

        ItemStack? work = anvil.WorkItemStack;
        if (work == null)
        {
            return;
        }

        int threshold = AnvilWorkStation.ResolveBitsRefund(player);
        if (threshold <= 0)
        {
            return;
        }

        int current = 0;
        if (ProsequorStackPedigree.TryGetPrimaryBlob(work, out ProsequorBlob blob))
        {
            current = blob.AnvilSplits;
        }
        else
        {
            blob = ProsequorBlob.Empty;
        }

        bool refund = TryAdvanceSplit(current, threshold, out int next);
        WriteAnvilSplits(anvil, work, player, blob, next);

        if (!refund)
        {
            return;
        }

        TryGiveMetalBit(anvil, player, work);
    }

    static void WriteAnvilSplits(
        BlockEntityAnvil anvil,
        ItemStack work,
        IPlayer player,
        ProsequorBlob existing,
        int splits)
    {
        ProsequorBlob baseBlob = existing;
        if (baseBlob.IsAnonymous)
        {
            ProsequorStackPedigree.StampMaker(work, player.PlayerUID);
            if (!ProsequorStackPedigree.TryGetPrimaryBlob(work, out baseBlob))
            {
                return;
            }
        }

        ProsequorStackPedigree.ApplyUnitBlob(work, baseBlob.WithAnvilSplits(splits));
        anvil.MarkDirty(redrawOnClient: true);
    }

    static void TryGiveMetalBit(BlockEntityAnvil anvil, IPlayer player, ItemStack work)
    {
        IWorldAccessor? world = anvil.Api?.World;
        if (world == null)
        {
            return;
        }

        if (!TryResolveMetalBitCode(anvil, work, out string metalCode))
        {
            return;
        }

        Item? bitItem = world.GetItem(new AssetLocation("game", "metalbit-" + metalCode));
        if (bitItem == null)
        {
            return;
        }

        ItemStack bit = new(bitItem, 1);
        float temp = work.Collectible.GetTemperature(world, work);
        bit.Collectible.SetTemperature(world, bit, temp, true);

        if (!player.InventoryManager.TryGiveItemstack(bit, slotNotifyEffect: true))
        {
            world.SpawnItemEntity(bit, anvil.Pos.ToVec3d().Add(0.5, 1.5, 0.5));
        }
    }

    static bool TryResolveMetalBitCode(BlockEntityAnvil anvil, ItemStack work, out string metalCode)
    {
        metalCode = "";
        IAnvilWorkable? workable = work.Collectible?.GetCollectibleInterface<IAnvilWorkable>();
        ItemStack? baseMat = workable?.GetBaseMaterial(work);
        string? part = baseMat?.Collectible?.LastCodePart()
            ?? work.Collectible?.Variant?["metal"];
        if (string.IsNullOrWhiteSpace(part))
        {
            return false;
        }

        metalCode = part;
        if (string.Equals(metalCode, "ironbloom", StringComparison.OrdinalIgnoreCase))
        {
            metalCode = "iron";
        }
        else if (string.Equals(metalCode, "steel", StringComparison.OrdinalIgnoreCase)
                 && anvil.Api?.ModLoader?.IsModEnabled("smithingplus") != true)
        {
            metalCode = "blistersteel";
        }

        return true;
    }
}
