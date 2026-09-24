using Prosequor.Ability.Hooks;
using Prosequor.Xp.Activity;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace Prosequor.Ability;

/// <summary>
/// Skep harvest XP: place stamps placer as contributor; break / sneak-RMB emit
/// <c>skep-harvest</c> with contributors × honeycomb quantity (post Sticky Fingers).
/// Non-breaking extract wipes shares then re-adds the harvester.
/// </summary>
public static class SkepHarvestXp
{
    public const string HoneycombPath = "honeycomb";

    [ThreadStatic]
    static List<Deed.ContributorShare>? pendingShares;

    [ThreadStatic]
    static List<Deed.QuantityUnit>? pendingUnits;

    [ThreadStatic]
    static BlockPos? pendingPos;

    [ThreadStatic]
    static int pendingBlockId;

    /// <summary>True for placed skep blocks (<see cref="BlockSkep"/>).</summary>
    public static bool IsSkepBlock(Block? block) => block is BlockSkep;

    /// <summary>Place: stamp placing player as contributor weight 1 (after stack capture).</summary>
    public static void OnSkepPlaced(BlockEntity? be, string? placerUid)
    {
        if (be == null || string.IsNullOrWhiteSpace(placerUid))
        {
            return;
        }

        ProsequorBlockPedigreeStation.AddContributor(be, placerUid, 1);
    }

    /// <summary>
    /// Before break removes the BE: add harvester +1 and snapshot real shares.
    /// </summary>
    public static void PrepareBreak(
        BlockEntityBeehive? beh,
        Block? block,
        BlockPos? pos,
        IPlayer? byPlayer)
    {
        ClearPending();
        if (beh == null
            || !beh.Harvestable
            || block == null
            || pos == null
            || byPlayer?.PlayerUID == null
            || byPlayer.Entity?.World?.Side != EnumAppSide.Server)
        {
            return;
        }

        ProsequorBlockPedigreeStation.AddContributor(beh, byPlayer.PlayerUID, 1);
        if (!ProsequorBlockPedigreeStation.TryGetBlob(beh, out ProsequorBlob blob))
        {
            return;
        }

        pendingShares = new List<Deed.ContributorShare>(HusbandryContributorXp.RealContributorShares(blob));
        pendingPos = pos.Copy();
        pendingBlockId = block.Id;
    }

    /// <summary>Record honeycomb quantity from mutated GetDrops (break path).</summary>
    public static void NoteBreakDrops(Block? block, BlockPos? pos, ItemStack[]? drops)
    {
        if (block == null
            || pos == null
            || pendingPos == null
            || pendingBlockId != block.Id
            || !pendingPos.Equals(pos)
            || !IsSkepBlock(block))
        {
            return;
        }

        pendingUnits = ToHoneycombUnits(drops);
    }

    /// <summary>After break drops: emit if prepare+note matched.</summary>
    public static void CompleteBreak(
        ICoreAPI? api,
        IPlayer? byPlayer,
        Block? block,
        BlockPos? pos)
    {
        IReadOnlyList<Deed.ContributorShare>? shares = pendingShares;
        IReadOnlyList<Deed.QuantityUnit>? units = pendingUnits;
        bool match = block != null
            && pos != null
            && pendingPos != null
            && pendingBlockId == block.Id
            && pendingPos.Equals(pos)
            && shares != null
            && shares.Count > 0
            && units != null
            && SumUnits(units) > 0;
        ClearPending();

        if (!match
            || api == null
            || byPlayer?.PlayerUID == null
            || block == null
            || pos == null)
        {
            return;
        }

        Emit(api, byPlayer.PlayerUID, block, pos, shares!, units!);
    }

    /// <summary>
    /// Non-breaking extract: add harvester, emit, wipe contributors, re-add harvester at 1.
    /// </summary>
    public static void SettleExtract(
        BlockEntity? be,
        Block? block,
        BlockPos? pos,
        IPlayer? byPlayer,
        ItemStack[]? honeycomb)
    {
        ICoreAPI? api = byPlayer?.Entity?.World?.Api ?? byPlayer?.Entity?.Api ?? be?.Api;
        if (be == null
            || block == null
            || pos == null
            || byPlayer?.PlayerUID == null
            || byPlayer.Entity?.World?.Side != EnumAppSide.Server
            || api == null)
        {
            return;
        }

        IReadOnlyList<Deed.QuantityUnit> units = ToHoneycombUnits(honeycomb);
        if (SumUnits(units) <= 0)
        {
            return;
        }

        string uid = byPlayer.PlayerUID;
        ProsequorBlockPedigreeStation.AddContributor(be, uid, 1);
        if (!ProsequorBlockPedigreeStation.TryGetBlob(be, out ProsequorBlob blob))
        {
            return;
        }

        IReadOnlyList<Deed.ContributorShare> shares = HusbandryContributorXp.RealContributorShares(blob);
        if (shares.Count == 0)
        {
            return;
        }

        Emit(api, uid, block, pos, shares, units);

        ProsequorBlockPedigreeStation.ClearContributors(be);
        ProsequorBlockPedigreeStation.AddContributor(be, uid, 1);
    }

    public static List<Deed.QuantityUnit> ToHoneycombUnits(ItemStack[]? stacks)
    {
        if (stacks == null || stacks.Length == 0)
        {
            return [];
        }

        List<Deed.QuantityUnit> list = new(stacks.Length);
        for (int i = 0; i < stacks.Length; i++)
        {
            ItemStack? stack = stacks[i];
            if (stack?.Collectible?.Code == null
                || stack.StackSize <= 0
                || !IsHoneycomb(stack))
            {
                continue;
            }

            list.Add(new Deed.QuantityUnit(stack.Collectible.Code.ToShortString(), stack.StackSize));
        }

        return list;
    }

    public static bool IsHoneycomb(ItemStack? stack) =>
        stack?.Collectible?.Code?.Path == HoneycombPath;

    static void Emit(
        ICoreAPI api,
        string harvesterUid,
        Block block,
        BlockPos pos,
        IReadOnlyList<Deed.ContributorShare> shares,
        IReadOnlyList<Deed.QuantityUnit> units)
    {
        Deed.Emit(
            api,
            playerUid: harvesterUid,
            tokens: [DeedTokenTags.SkepHarvest],
            caller: CallerIdentities.Hand,
            target: EventFactBuilder.CodeOf(block),
            position: pos.Copy(),
            outputs: units,
            contributors: shares);
    }

    static int SumUnits(IReadOnlyList<Deed.QuantityUnit> units)
    {
        int total = 0;
        for (int i = 0; i < units.Count; i++)
        {
            total += Math.Max(0, units[i].Count);
        }

        return total;
    }

    static void ClearPending()
    {
        pendingShares = null;
        pendingUnits = null;
        pendingPos = null;
        pendingBlockId = 0;
    }
}
