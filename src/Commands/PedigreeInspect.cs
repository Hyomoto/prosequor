using System.Globalization;
using System.Text;
using Prosequor.Ability;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace Prosequor.Commands;

/// <summary>
/// Debug dump of Live/Frozen pedigree. Held item wins; empty hand interrogates
/// the looked-at entity or block. Maker and contributor UIDs resolve to names.
/// </summary>
public static class PedigreeInspect
{
    public const string Invalid = "invalid";
    public const string None = "none";

    public static string Run(ICoreServerAPI api, IServerPlayer player)
    {
        ItemSlot? hand = player.InventoryManager?.ActiveHotbarSlot;
        if (hand?.Itemstack != null && !hand.Empty)
        {
            return FormatHeld(api, hand.Itemstack);
        }

        Entity? entity = player.Entity is EntityPlayer ep ? ep.EntitySelection?.Entity : null;
        if (entity != null)
        {
            return FormatEntity(api, entity);
        }

        BlockSelection? sel = player.CurrentBlockSelection;
        if (sel?.Position != null)
        {
            return FormatBlock(api, sel);
        }

        return Invalid;
    }

    internal static string FormatHeld(ICoreServerAPI api, ItemStack stack)
    {
        List<string> lines = new();
        AppendStack(lines, api, stack, "held " + StackLabel(stack), indent: "");
        return Finish(lines);
    }

    internal static string FormatEntity(ICoreServerAPI api, Entity entity)
    {
        List<string> lines = new() { $"target entity {EntityLabel(entity)}" };
        if (ProsequorPedigree.TryGetBlob(entity, out ProsequorBlob blob) && blob.HasPersistable)
        {
            AppendBlob(lines, api, blob, "  ");
        }

        return Finish(lines);
    }

    internal static string FormatBlock(ICoreServerAPI api, BlockSelection sel)
    {
        BlockPos pos = sel.Position;
        Block? block = api.World.BlockAccessor.GetBlock(pos);
        List<string> lines = new() { $"target block {BlockLabel(block)} ({pos.X}, {pos.Y}, {pos.Z})" };

        BlockEntity? be = api.World.BlockAccessor.GetBlockEntity(pos);
        if (be == null && block != null && (block.CropProps != null || block is BlockCrop))
        {
            be = api.World.BlockAccessor.GetBlockEntity(pos.DownCopy());
        }

        if (be is BlockEntityGroundStorage ground)
        {
            ItemSlot? slot = ground.GetSlotAt(sel);
            if (slot?.Itemstack != null && !slot.Empty)
            {
                AppendStack(lines, api, slot.Itemstack, "  pile " + StackLabel(slot.Itemstack), indent: "  ");
                return Finish(lines);
            }
        }

        if (ProsequorPedigree.TryGetBlob(be, out ProsequorBlob blob) && blob.HasPersistable)
        {
            AppendBlob(lines, api, blob, "  ");
        }

        if (be is BlockEntityContainer container && container.Inventory != null)
        {
            for (int i = 0; i < container.Inventory.Count; i++)
            {
                ItemStack? stack = container.Inventory[i]?.Itemstack;
                if (stack == null)
                {
                    continue;
                }

                AppendStack(lines, api, stack, $"  slot {i} {StackLabel(stack)}", indent: "  ");
            }
        }
        else if (block is BlockLiquidContainerBase liquid)
        {
            ItemStack? content = liquid.GetContent(pos);
            if (content != null)
            {
                AppendStack(lines, api, content, "  content " + StackLabel(content), indent: "  ");
            }
        }

        return Finish(lines);
    }

    internal static string FormatBlob(ProsequorBlob blob, System.Func<string, string> nameOf)
    {
        List<string> lines = new();
        AppendBlob(lines, blob, nameOf, "");
        return string.Join('\n', lines);
    }

    static string Finish(List<string> lines)
    {
        if (lines.Count == 0)
        {
            return Invalid;
        }

        bool hasBody = false;
        for (int i = 1; i < lines.Count; i++)
        {
            if (lines[i].Length > 0)
            {
                hasBody = true;
                break;
            }
        }

        return hasBody ? string.Join('\n', lines) : None;
    }

    static void AppendStack(
        List<string> lines,
        ICoreServerAPI api,
        ItemStack stack,
        string header,
        string indent)
    {
        CraftAttribution.PromoteLegacyFlats(stack);
        lines.Add(header);
        string child = indent.Length == 0 ? "  " : indent + "  ";

        if (ProsequorStackPedigree.HasFrozen(stack) && !ProsequorStackPedigree.IsHomogeneous(stack))
        {
            IReadOnlyList<ProsequorStackPedigree.FrozenGroup> groups = ProsequorStackPedigree.ReadFrozenGroups(stack);
            int shown = 0;
            for (int i = 0; i < groups.Count; i++)
            {
                ProsequorStackPedigree.FrozenGroup group = groups[i];
                if (group.Qty <= 0 || !group.Blob.HasPersistable)
                {
                    continue;
                }

                lines.Add($"{child}frozen[{shown}] qty={group.Qty}");
                AppendBlob(lines, api, group.Blob, child + "  ");
                shown++;
            }
        }
        else if (ProsequorPedigree.TryGetBlob(stack, out ProsequorBlob blob) && blob.HasPersistable)
        {
            AppendBlob(lines, api, blob, child);
        }

        if (stack.Collectible is BlockLiquidContainerBase vessel)
        {
            ItemStack? content = vessel.GetContent(stack);
            if (content != null)
            {
                AppendStack(lines, api, content, $"{child}content {StackLabel(content)}", child);
            }
        }
    }

    static void AppendBlob(List<string> lines, ICoreServerAPI api, ProsequorBlob blob, string indent) =>
        AppendBlob(lines, blob, uid => ResolvePlayerName(api, uid), indent);

    static void AppendBlob(
        List<string> lines,
        ProsequorBlob blob,
        System.Func<string, string> nameOf,
        string indent)
    {
        lines.Add(indent + "maker " + DisplayUid(blob.MakerUid, nameOf));

        if (blob.Contributors.Count == 0)
        {
            lines.Add(indent + "contributors " + None);
        }
        else
        {
            List<string> shares = new(blob.Contributors.Count);
            for (int i = 0; i < blob.Contributors.Count; i++)
            {
                ProsequorBlob.Share share = blob.Contributors[i];
                shares.Add($"{DisplayUid(share.PlayerUid, nameOf)} {share.Weight}");
            }

            lines.Add(indent + "contributors " + string.Join(", ", shares));
        }

        if (!string.IsNullOrEmpty(blob.Recipe))
        {
            lines.Add(indent + "recipe " + blob.Recipe);
        }

        if (blob.QualityRank > 0)
        {
            lines.Add(indent + "quality " + blob.QualityRank.ToString(CultureInfo.InvariantCulture));
        }

        if (blob.Affixes.Count > 0)
        {
            lines.Add(indent + "affixes " + string.Join(", ", blob.Affixes.Select(a => a.Code)));
        }

        if (blob.Mods.Count > 0)
        {
            lines.Add(indent + "mods " + string.Join(
                ", ",
                blob.Mods.Select(m =>
                    m.Key + "=" + m.Factor.ToString("0.###", CultureInfo.InvariantCulture))));
        }

        if (blob.AnvilSplits > 0)
        {
            lines.Add(indent + "anvilSplits " + blob.AnvilSplits.ToString(CultureInfo.InvariantCulture));
        }

        if (blob.FriendlinessReadyAtTotalHours > 0)
        {
            lines.Add(
                indent + "friendlinessReadyAt " +
                blob.FriendlinessReadyAtTotalHours.ToString("0.###", CultureInfo.InvariantCulture));
        }
    }

    internal static string ResolvePlayerName(ICoreServerAPI api, string uid)
    {
        if (!MealHostCredit.IsPlayerUid(uid))
        {
            return uid;
        }

        IPlayer? online = api.World?.PlayerByUid(uid);
        if (!string.IsNullOrWhiteSpace(online?.PlayerName))
        {
            return online.PlayerName;
        }

        if (api.PlayerData?.PlayerDataByUid != null
            && api.PlayerData.PlayerDataByUid.TryGetValue(uid, out IServerPlayerData? data)
            && !string.IsNullOrWhiteSpace(data?.LastKnownPlayername))
        {
            return data.LastKnownPlayername;
        }

        return uid;
    }

    static string DisplayUid(string? uid, System.Func<string, string> nameOf)
    {
        if (string.IsNullOrWhiteSpace(uid))
        {
            return None;
        }

        return nameOf(uid);
    }

    static string StackLabel(ItemStack stack)
    {
        string code = stack.Collectible?.Code?.ToString() ?? "stack";
        return code + " x" + stack.StackSize.ToString(CultureInfo.InvariantCulture);
    }

    static string EntityLabel(Entity entity)
    {
        string code = entity.Code?.ToString() ?? "entity";
        return code + " (" + entity.EntityId.ToString(CultureInfo.InvariantCulture) + ")";
    }

    static string BlockLabel(Block? block) =>
        block?.Code?.ToString() ?? "block";
}
