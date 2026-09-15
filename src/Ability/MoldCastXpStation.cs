using System.Runtime.CompilerServices;
using Prosequor.Xp.Activity;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.GameContent;

namespace Prosequor.Ability;

/// <summary>Thread-local pourer while a crucible fills a liquid-metal sink.</summary>
public static class MoldPourScope
{
    [ThreadStatic]
    static IPlayer? currentPlayer;

    public static IPlayer? CurrentPlayer => currentPlayer;

    public static void Begin(IPlayer? player) => currentPlayer = player;

    public static void End() => currentPlayer = null;
}

/// <summary>
/// Per-cavity pourer + paid flags for tool and ingot molds. Ingot molds have two cavities;
/// do not share a single BE pedigree blob.
/// </summary>
public static class MoldCastXpStation
{
    public const string ToolPourerAttr = "prosequorMoldPourer";
    public const string ToolPaidAttr = "prosequorMoldPaid";
    public const string LeftPourerAttr = "prosequorMoldPourerL";
    public const string RightPourerAttr = "prosequorMoldPourerR";
    public const string LeftPaidAttr = "prosequorMoldPaidL";
    public const string RightPaidAttr = "prosequorMoldPaidR";

    /// <summary>Map raw mold fill units onto the ingredients 1–40 domain (÷10).</summary>
    public static int IngredientsFromFill(int fillLevel) =>
        Math.Max(1, fillLevel / 10);

    /// <summary>
    /// Rising-edge settle decision. When not full, clears paid and adopts hardened.
    /// When full and rising harden with a pourer and not yet paid → pay.
    /// </summary>
    public static bool TrySettleRisingEdge(
        bool full,
        bool hardened,
        bool hasPourer,
        ref bool wasHardened,
        ref bool paid)
    {
        if (!full)
        {
            paid = false;
            wasHardened = hardened;
            return false;
        }

        bool rising = hardened && !wasHardened;
        wasHardened = hardened;
        if (!rising || paid || !hasPourer)
        {
            return false;
        }

        paid = true;
        return true;
    }

    static readonly ConditionalWeakTable<BlockEntity, ToolBox> toolBoxes = new();
    static readonly ConditionalWeakTable<BlockEntity, IngotBox> ingotBoxes = new();

    sealed class ToolBox
    {
        public string? PourerUid;
        public bool Paid;
        public bool WasHardened;
    }

    sealed class IngotBox
    {
        public string? LeftPourerUid;
        public string? RightPourerUid;
        public bool LeftPaid;
        public bool RightPaid;
        public bool WasHardenedLeft;
        public bool WasHardenedRight;
    }

    public static void StampToolPourer(BlockEntityToolMold mold, string? uid)
    {
        if (mold == null || string.IsNullOrWhiteSpace(uid))
        {
            return;
        }

        ToolBox box = toolBoxes.GetOrCreateValue(mold);
        box.PourerUid = uid.Trim();
        mold.MarkDirty(redrawOnClient: false);
    }

    public static void StampIngotPourer(BlockEntityIngotMold mold, bool right, string? uid)
    {
        if (mold == null || string.IsNullOrWhiteSpace(uid))
        {
            return;
        }

        IngotBox box = ingotBoxes.GetOrCreateValue(mold);
        if (right)
        {
            box.RightPourerUid = uid.Trim();
        }
        else
        {
            box.LeftPourerUid = uid.Trim();
        }

        mold.MarkDirty(redrawOnClient: false);
    }

    /// <summary>
    /// Rising-edge harden settle for a tool mold. Clears paid when fill drops below capacity.
    /// </summary>
    public static void OnToolTick(BlockEntityToolMold mold)
    {
        if (mold?.Api?.Side != EnumAppSide.Server)
        {
            return;
        }

        ToolBox box = toolBoxes.GetOrCreateValue(mold);
        int fill = Math.Max(0, mold.FillLevel);
        bool full = mold.IsFull && fill > 0;
        bool hardened = mold.IsHardened;

        if (!TrySettleRisingEdge(
                full,
                hardened,
                hasPourer: !string.IsNullOrWhiteSpace(box.PourerUid),
                ref box.WasHardened,
                ref box.Paid))
        {
            return;
        }

        EmitCast(
            mold.Api,
            box.PourerUid,
            TargetFromTool(mold),
            IngredientsFromFill(fill));
        mold.MarkDirty(redrawOnClient: false);
    }

    /// <summary>Rising-edge harden settle for each ingot cavity independently.</summary>
    public static void OnIngotTick(BlockEntityIngotMold mold)
    {
        if (mold?.Api?.Side != EnumAppSide.Server)
        {
            return;
        }

        IngotBox box = ingotBoxes.GetOrCreateValue(mold);
        int required = Math.Max(1, mold.RequiredUnits);

        SettleIngotSide(
            mold,
            box,
            right: false,
            fill: Math.Max(0, mold.FillLevelLeft),
            required,
            hardened: mold.IsHardenedLeft,
            wasHardened: ref box.WasHardenedLeft,
            paid: ref box.LeftPaid,
            pourer: box.LeftPourerUid,
            target: EventFactBuilder.CodeOf(mold.GetStateAwareContentsLeft()));

        SettleIngotSide(
            mold,
            box,
            right: true,
            fill: Math.Max(0, mold.FillLevelRight),
            required,
            hardened: mold.IsHardenedRight,
            wasHardened: ref box.WasHardenedRight,
            paid: ref box.RightPaid,
            pourer: box.RightPourerUid,
            target: EventFactBuilder.CodeOf(mold.GetStateAwareContentsRight()));
    }

    static void SettleIngotSide(
        BlockEntityIngotMold mold,
        IngotBox box,
        bool right,
        int fill,
        int required,
        bool hardened,
        ref bool wasHardened,
        ref bool paid,
        string? pourer,
        string? target)
    {
        bool full = fill >= required && fill > 0;
        if (!TrySettleRisingEdge(
                full,
                hardened,
                hasPourer: !string.IsNullOrWhiteSpace(pourer),
                ref wasHardened,
                ref paid))
        {
            return;
        }

        EmitCast(
            mold.Api,
            pourer,
            target,
            IngredientsFromFill(fill));
        mold.MarkDirty(redrawOnClient: false);
        _ = box;
        _ = right;
    }

    static string? TargetFromTool(BlockEntityToolMold mold)
    {
        ItemStack[]? stacks = mold.GetStateAwareMoldedStacks();
        if (stacks != null)
        {
            for (int i = 0; i < stacks.Length; i++)
            {
                string? code = EventFactBuilder.CodeOf(stacks[i]);
                if (!string.IsNullOrWhiteSpace(code))
                {
                    return code;
                }
            }
        }

        return EventFactBuilder.CodeOf(mold.MetalContent);
    }

    static void EmitCast(ICoreAPI? api, string? pourerUid, string? target, int ingredients)
    {
        if (api?.Side != EnumAppSide.Server
            || string.IsNullOrWhiteSpace(pourerUid)
            || ingredients <= 0)
        {
            return;
        }

        Deed.Emit(
            api,
            playerUid: "",
            DeedToken.MoldCast,
            caller: CallerIdentities.Mold,
            target: target,
            totalUnits: ingredients,
            contributors: [new Deed.ContributorShare(pourerUid.Trim(), 1f)]);
    }

    public static void WriteToolToTree(BlockEntityToolMold mold, ITreeAttribute tree)
    {
        if (!toolBoxes.TryGetValue(mold, out ToolBox? box) || box == null)
        {
            return;
        }

        if (!string.IsNullOrEmpty(box.PourerUid))
        {
            tree.SetString(ToolPourerAttr, box.PourerUid);
        }

        if (box.Paid)
        {
            tree.SetBool(ToolPaidAttr, true);
        }

        // Persist hardened snapshot so a load of an already-hard mold does not repay.
        if (box.WasHardened || mold.IsHardened)
        {
            tree.SetBool(ToolPaidAttr + "Was", true);
        }
    }

    public static void ReadToolFromTree(BlockEntityToolMold mold, ITreeAttribute tree)
    {
        if (tree == null)
        {
            return;
        }

        ToolBox box = toolBoxes.GetOrCreateValue(mold);
        box.PourerUid = tree.GetString(ToolPourerAttr);
        if (string.IsNullOrWhiteSpace(box.PourerUid))
        {
            box.PourerUid = null;
        }

        box.Paid = tree.GetBool(ToolPaidAttr);
        // After load, treat already-hardened molds as past the rising edge.
        box.WasHardened = tree.GetBool(ToolPaidAttr + "Was") || mold.IsHardened;
        if (box.WasHardened && !box.Paid && !string.IsNullOrEmpty(box.PourerUid))
        {
            // Legacy / mid-pour save: mark paid so we do not grant on first tick after load.
            box.Paid = true;
        }
    }

    public static void WriteIngotToTree(BlockEntityIngotMold mold, ITreeAttribute tree)
    {
        if (!ingotBoxes.TryGetValue(mold, out IngotBox? box) || box == null)
        {
            return;
        }

        if (!string.IsNullOrEmpty(box.LeftPourerUid))
        {
            tree.SetString(LeftPourerAttr, box.LeftPourerUid);
        }

        if (!string.IsNullOrEmpty(box.RightPourerUid))
        {
            tree.SetString(RightPourerAttr, box.RightPourerUid);
        }

        if (box.LeftPaid)
        {
            tree.SetBool(LeftPaidAttr, true);
        }

        if (box.RightPaid)
        {
            tree.SetBool(RightPaidAttr, true);
        }

        if (box.WasHardenedLeft || mold.IsHardenedLeft)
        {
            tree.SetBool(LeftPaidAttr + "Was", true);
        }

        if (box.WasHardenedRight || mold.IsHardenedRight)
        {
            tree.SetBool(RightPaidAttr + "Was", true);
        }
    }

    public static void ReadIngotFromTree(BlockEntityIngotMold mold, ITreeAttribute tree)
    {
        if (tree == null)
        {
            return;
        }

        IngotBox box = ingotBoxes.GetOrCreateValue(mold);
        box.LeftPourerUid = NullIfBlank(tree.GetString(LeftPourerAttr));
        box.RightPourerUid = NullIfBlank(tree.GetString(RightPourerAttr));
        box.LeftPaid = tree.GetBool(LeftPaidAttr);
        box.RightPaid = tree.GetBool(RightPaidAttr);
        box.WasHardenedLeft = tree.GetBool(LeftPaidAttr + "Was") || mold.IsHardenedLeft;
        box.WasHardenedRight = tree.GetBool(RightPaidAttr + "Was") || mold.IsHardenedRight;
        if (box.WasHardenedLeft && !box.LeftPaid && box.LeftPourerUid != null)
        {
            box.LeftPaid = true;
        }

        if (box.WasHardenedRight && !box.RightPaid && box.RightPourerUid != null)
        {
            box.RightPaid = true;
        }
    }

    static string? NullIfBlank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
