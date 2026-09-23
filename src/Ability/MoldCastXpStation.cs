using System.Runtime.CompilerServices;
using Prosequor.Xp.Activity;
using Vintagestory.API.Common;
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
/// Per-cavity pourer + paid flags for tool and ingot molds on
/// <see cref="ProsequorChunkPedigree"/>. Rising-edge hardened baselines are ephemeral.
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

    static readonly ConditionalWeakTable<BlockEntity, EdgeBox> edges = new();

    sealed class EdgeBox
    {
        public bool WasHardened;
        public bool WasHardenedLeft;
        public bool WasHardenedRight;
        public bool HavePrev;
        public bool HavePrevLeft;
        public bool HavePrevRight;
    }

    public static void StampToolPourer(BlockEntityToolMold mold, string? uid)
    {
        if (mold == null || string.IsNullOrWhiteSpace(uid))
        {
            return;
        }

        ProsequorBlockPedigreeStation.Mutate(mold, box => box.MoldToolPourerUid = uid.Trim());
    }

    public static void StampIngotPourer(BlockEntityIngotMold mold, bool right, string? uid)
    {
        if (mold == null || string.IsNullOrWhiteSpace(uid))
        {
            return;
        }

        string trimmed = uid.Trim();
        ProsequorBlockPedigreeStation.Mutate(mold, box =>
        {
            if (right)
            {
                box.MoldRightPourerUid = trimmed;
            }
            else
            {
                box.MoldLeftPourerUid = trimmed;
            }
        });
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

        if (!ProsequorBlockPedigreeStation.TryGetBox(mold, out ProsequorChunkPedigree.Box host))
        {
            host = new ProsequorChunkPedigree.Box();
        }

        EdgeBox edge = edges.GetOrCreateValue(mold);
        int fill = Math.Max(0, mold.FillLevel);
        bool full = mold.IsFull && fill > 0;
        bool hardened = mold.IsHardened;
        bool paid = host.MoldToolPaid;

        if (!edge.HavePrev)
        {
            edge.WasHardened = hardened;
            edge.HavePrev = true;
            if (hardened && !paid && !string.IsNullOrWhiteSpace(host.MoldToolPourerUid))
            {
                ProsequorBlockPedigreeStation.Mutate(mold, b => b.MoldToolPaid = true);
            }

            return;
        }

        if (!TrySettleRisingEdge(
                full,
                hardened,
                hasPourer: !string.IsNullOrWhiteSpace(host.MoldToolPourerUid),
                ref edge.WasHardened,
                ref paid))
        {
            if (!full && host.MoldToolPaid)
            {
                ProsequorBlockPedigreeStation.Mutate(mold, b => b.MoldToolPaid = false);
            }

            return;
        }

        ProsequorBlockPedigreeStation.Mutate(mold, b => b.MoldToolPaid = true);
        EmitCast(
            mold.Api,
            host.MoldToolPourerUid,
            TargetFromTool(mold),
            IngredientsFromFill(fill));
    }

    /// <summary>Rising-edge harden settle for each ingot cavity independently.</summary>
    public static void OnIngotTick(BlockEntityIngotMold mold)
    {
        if (mold?.Api?.Side != EnumAppSide.Server)
        {
            return;
        }

        if (!ProsequorBlockPedigreeStation.TryGetBox(mold, out ProsequorChunkPedigree.Box host))
        {
            host = new ProsequorChunkPedigree.Box();
        }

        EdgeBox edge = edges.GetOrCreateValue(mold);
        int required = Math.Max(1, mold.RequiredUnits);

        SettleIngotSide(
            mold,
            edge,
            host,
            right: false,
            fill: Math.Max(0, mold.FillLevelLeft),
            required,
            hardened: mold.IsHardenedLeft,
            pourer: host.MoldLeftPourerUid,
            paid: host.MoldLeftPaid,
            target: EventFactBuilder.CodeOf(mold.GetStateAwareContentsLeft()));

        if (!ProsequorBlockPedigreeStation.TryGetBox(mold, out host))
        {
            host = new ProsequorChunkPedigree.Box();
        }

        SettleIngotSide(
            mold,
            edge,
            host,
            right: true,
            fill: Math.Max(0, mold.FillLevelRight),
            required,
            hardened: mold.IsHardenedRight,
            pourer: host.MoldRightPourerUid,
            paid: host.MoldRightPaid,
            target: EventFactBuilder.CodeOf(mold.GetStateAwareContentsRight()));
    }

    static void SettleIngotSide(
        BlockEntityIngotMold mold,
        EdgeBox edge,
        ProsequorChunkPedigree.Box host,
        bool right,
        int fill,
        int required,
        bool hardened,
        string? pourer,
        bool paid,
        string? target)
    {
        bool full = fill >= required && fill > 0;
        ref bool wasHardened = ref right ? ref edge.WasHardenedRight : ref edge.WasHardenedLeft;
        ref bool havePrev = ref right ? ref edge.HavePrevRight : ref edge.HavePrevLeft;

        if (!havePrev)
        {
            wasHardened = hardened;
            havePrev = true;
            if (hardened && !paid && !string.IsNullOrWhiteSpace(pourer))
            {
                ProsequorBlockPedigreeStation.Mutate(mold, b =>
                {
                    if (right)
                    {
                        b.MoldRightPaid = true;
                    }
                    else
                    {
                        b.MoldLeftPaid = true;
                    }
                });
            }

            return;
        }

        if (!TrySettleRisingEdge(
                full,
                hardened,
                hasPourer: !string.IsNullOrWhiteSpace(pourer),
                ref wasHardened,
                ref paid))
        {
            if (!full && (right ? host.MoldRightPaid : host.MoldLeftPaid))
            {
                ProsequorBlockPedigreeStation.Mutate(mold, b =>
                {
                    if (right)
                    {
                        b.MoldRightPaid = false;
                    }
                    else
                    {
                        b.MoldLeftPaid = false;
                    }
                });
            }

            return;
        }

        ProsequorBlockPedigreeStation.Mutate(mold, b =>
        {
            if (right)
            {
                b.MoldRightPaid = true;
            }
            else
            {
                b.MoldLeftPaid = true;
            }
        });
        EmitCast(mold.Api, pourer, target, IngredientsFromFill(fill));
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
}
