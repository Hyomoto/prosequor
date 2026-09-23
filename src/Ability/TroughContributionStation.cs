using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.GameContent;

namespace Prosequor.Ability;

/// <summary>
/// Weighted contributor bag on a trough, stored as Live pedigree contributors
/// (<c>playerUid → portion count</c>). Fill increments weight; eat picks a random
/// contributor proportional to weight and decrements by 1.
/// </summary>
public static class TroughContributionStation
{
    public const string TreeKey = "prosequorTroughContributors";
    public const string CountKey = "n";
    public const string UidKey = "uid";
    public const string WeightKey = "w";

    /// <summary>Add <paramref name="amount"/> to <paramref name="uid"/>'s weight (flat bag).</summary>
    public static void AddContribution(Dictionary<string, int> weights, string? uid, int amount = 1)
    {
        if (weights == null || string.IsNullOrEmpty(uid) || amount <= 0)
        {
            return;
        }

        weights.TryGetValue(uid, out int current);
        weights[uid] = current + amount;
    }

    /// <summary>
    /// Weighted-random pick; decrements that uid by 1 (removes at 0).
    /// <paramref name="rand"/> null → <see cref="Random.Shared"/>.
    /// </summary>
    public static bool TryTakeContribution(
        Dictionary<string, int> weights,
        out string? uid,
        Random? rand = null)
    {
        uid = null;
        if (weights == null || weights.Count == 0)
        {
            return false;
        }

        int total = 0;
        foreach (KeyValuePair<string, int> pair in weights)
        {
            if (pair.Value > 0)
            {
                total += pair.Value;
            }
        }

        if (total <= 0)
        {
            weights.Clear();
            return false;
        }

        int roll = (rand ?? Random.Shared).Next(total);
        int cursor = 0;
        string? picked = null;
        foreach (KeyValuePair<string, int> pair in weights)
        {
            if (pair.Value <= 0)
            {
                continue;
            }

            cursor += pair.Value;
            if (roll < cursor)
            {
                picked = pair.Key;
                break;
            }
        }

        if (string.IsNullOrEmpty(picked))
        {
            return false;
        }

        int next = weights[picked] - 1;
        if (next <= 0)
        {
            weights.Remove(picked);
        }
        else
        {
            weights[picked] = next;
        }

        uid = picked;
        return true;
    }

    /// <summary>Snapshot as deed contributor shares (weight = remaining portions credited).</summary>
    public static IReadOnlyList<Xp.Activity.Deed.ContributorShare> ToShares(
        Dictionary<string, int> weights)
    {
        if (weights == null || weights.Count == 0)
        {
            return Array.Empty<Xp.Activity.Deed.ContributorShare>();
        }

        List<Xp.Activity.Deed.ContributorShare> list = new(weights.Count);
        foreach (KeyValuePair<string, int> pair in weights)
        {
            if (pair.Value > 0 && !string.IsNullOrEmpty(pair.Key))
            {
                list.Add(new Xp.Activity.Deed.ContributorShare(pair.Key, pair.Value));
            }
        }

        return list;
    }

    public static int TotalWeight(Dictionary<string, int> weights)
    {
        if (weights == null || weights.Count == 0)
        {
            return 0;
        }

        int total = 0;
        foreach (KeyValuePair<string, int> pair in weights)
        {
            if (pair.Value > 0)
            {
                total += pair.Value;
            }
        }

        return total;
    }

    public static void AddContribution(BlockEntityTrough? trough, string? uid, int amount = 1)
    {
        if (trough == null || string.IsNullOrEmpty(uid) || amount <= 0)
        {
            return;
        }

        ProsequorBlockPedigreeStation.AddContributor(trough, uid, amount);
    }

    public static bool TryTakeContribution(BlockEntityTrough? trough, out string? uid)
    {
        uid = null;
        if (trough == null
            || !ProsequorBlockPedigreeStation.TryGetBlob(trough, out ProsequorBlob blob)
            || blob.Contributors.Count == 0)
        {
            return false;
        }

        Dictionary<string, int> weights = SharesToDict(blob);
        Random? rand = trough.Api?.World?.Rand;
        if (!TryTakeContribution(weights, out uid, rand) || string.IsNullOrEmpty(uid))
        {
            return false;
        }

        string taken = uid;
        ProsequorBlockPedigreeStation.Mutate(trough, box =>
        {
            box.Blob = box.Blob.WithContributorDecrement(taken, 1);
            if (box.Blob.IsAnonymous && !box.Blob.HasSurface)
            {
                box.Blob = ProsequorBlob.Empty;
            }
        });
        return true;
    }

    public static int PendingCount(BlockEntityTrough? trough)
    {
        if (trough == null || !ProsequorBlockPedigreeStation.TryGetBlob(trough, out ProsequorBlob blob))
        {
            return 0;
        }

        int total = 0;
        for (int i = 0; i < blob.Contributors.Count; i++)
        {
            if (blob.Contributors[i].Weight > 0)
            {
                total += blob.Contributors[i].Weight;
            }
        }

        return total;
    }

    public static void Clear(BlockEntityTrough? trough)
    {
        if (trough == null)
        {
            return;
        }

        ProsequorBlockPedigreeStation.ClearContributors(trough);
    }

    /// <summary>Fill levels currently in slot 0 (0 when empty / unknown config).</summary>
    public static int CountFillPortions(BlockEntityTrough? trough)
    {
        if (trough?.Inventory == null || trough.Inventory.Empty)
        {
            return 0;
        }

        ItemSlot slot = trough.Inventory[0];
        ItemStack? stack = slot?.Itemstack;
        if (stack == null || trough.Api?.World == null)
        {
            return 0;
        }

        ContentConfig? config = ItemSlotTrough.getContentConfig(
            trough.Api.World,
            trough.contentConfigs,
            slot);
        if (config == null || config.QuantityPerFillLevel <= 0)
        {
            return 0;
        }

        return stack.StackSize / config.QuantityPerFillLevel;
    }

    /// <summary>
    /// Mirrors one successful vanilla <c>BlockEntityTrough.OnInteract</c> deposit
    /// (one fill level from the active hotbar). Returns false when full, wrong item, or short stack.
    /// </summary>
    public static bool TryDepositOnePortion(BlockEntityTrough? trough, IPlayer? byPlayer)
    {
        if (trough?.Api?.World == null
            || byPlayer?.InventoryManager?.ActiveHotbarSlot == null
            || trough.Inventory == null)
        {
            return false;
        }

        ItemSlot handSlot = byPlayer.InventoryManager.ActiveHotbarSlot;
        if (handSlot.Empty)
        {
            return false;
        }

        ContentConfig? contentConf = ItemSlotTrough.getContentConfig(
            trough.Api.World,
            trough.contentConfigs,
            handSlot);
        if (contentConf == null || contentConf.QuantityPerFillLevel <= 0)
        {
            return false;
        }

        int per = contentConf.QuantityPerFillLevel;
        ItemSlot troughSlot = trough.Inventory[0];
        ItemStack? troughStack = troughSlot.Itemstack;

        if (troughStack == null)
        {
            if (handSlot.StackSize < per)
            {
                return false;
            }

            troughSlot.Itemstack = handSlot.TakeOut(per);
            troughSlot.MarkDirty();
            trough.MarkDirty(redrawOnClient: true);
            return true;
        }

        if (!handSlot.Itemstack.Equals(
                trough.Api.World,
                troughStack,
                GlobalConstants.IgnoredStackAttributes)
            || handSlot.StackSize < per
            || troughStack.StackSize >= per * contentConf.MaxFillLevels)
        {
            return false;
        }

        handSlot.TakeOut(per);
        troughStack.StackSize += per;
        troughSlot.MarkDirty();
        trough.MarkDirty(redrawOnClient: true);
        return true;
    }

    static Dictionary<string, int> SharesToDict(ProsequorBlob blob)
    {
        Dictionary<string, int> weights = new(StringComparer.Ordinal);
        for (int i = 0; i < blob.Contributors.Count; i++)
        {
            ProsequorBlob.Share share = blob.Contributors[i];
            if (share.Weight > 0 && !string.IsNullOrEmpty(share.PlayerUid))
            {
                weights[share.PlayerUid] = share.Weight;
            }
        }

        return weights;
    }
}
