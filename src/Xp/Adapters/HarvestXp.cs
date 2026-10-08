using Prosequor.Ability;
using Prosequor.Xp.Activity;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace Prosequor.Xp.Adapters;

/// <summary>
/// Harvest deeds for crops / berry bushes / fruit trees: quantity from drops.
/// A break emits <c>block-broken</c>. An interact take emits <c>harvested</c>.
/// <c>is-wild</c> is added when the plant has no planter and no place mark.
/// </summary>
public static class HarvestXp
{
    public const string TokenIsWild = DeedTokenTags.IsWild;

    [ThreadStatic]
    static List<Deed.QuantityUnit>? pendingUnits;

    [ThreadStatic]
    static BlockPos? pendingPos;

    [ThreadStatic]
    static int pendingBlockId;

    /// <summary>Remember GetDrops for the matching break emit.</summary>
    public static void NoteBreakDrops(Block? block, BlockPos? pos, ItemStack[]? drops)
    {
        ClearPending();
        if (block == null || pos == null)
        {
            return;
        }

        pendingUnits = ToUnits(drops);
        pendingPos = pos.Copy();
        pendingBlockId = block.Id;
    }

    /// <summary>
    /// Consume pending GetDrops units when they match this break; otherwise null.
    /// </summary>
    public static IReadOnlyList<Deed.QuantityUnit>? TakePendingUnits(Block? block, BlockPos? pos)
    {
        IReadOnlyList<Deed.QuantityUnit>? units = null;
        if (block != null
            && pos != null
            && pendingBlockId == block.Id
            && pendingPos != null
            && pendingPos.Equals(pos))
        {
            units = pendingUnits;
        }

        ClearPending();
        return units;
    }

    /// <summary>Break-path harvest. Same <c>block-broken</c> emit as every other classified break.</summary>
    public static void NotifyBlockBroken(
        ICoreServerAPI sapi,
        IPlayer byPlayer,
        Block broken,
        BlockPos pos) =>
        EmitBlockBroken(sapi, byPlayer?.PlayerUID, byPlayer, broken, pos, callerOverride: null);

    /// <summary>
    /// One <c>block-broken</c> emit. Drops are the noted GetDrops list.
    /// <c>is-wild</c> is added when the wild check passes.
    /// </summary>
    public static void EmitBlockBroken(
        ICoreAPI api,
        string? playerUid,
        IPlayer? byPlayer,
        Block broken,
        BlockPos pos,
        string? callerOverride)
    {
        if (api == null
            || api.Side != EnumAppSide.Server
            || string.IsNullOrWhiteSpace(playerUid)
            || broken == null)
        {
            return;
        }

        IReadOnlyList<Deed.QuantityUnit>? units = TakePendingUnits(broken, pos);
        List<string> tokens = [DeedToken.BlockBroken.ToTag()];
        if (ForagePlayerPlaced.IsWild(api.World, broken, pos))
        {
            tokens.Add(TokenIsWild);
        }

        string caller = !string.IsNullOrWhiteSpace(callerOverride)
            ? callerOverride.Trim()
            : EventFactBuilder.CallerOrHand(byPlayer);

        api.Logger.VerboseDebug(
            "[prosequor] deed {0}{1} {2} caller={3} resistance={4:0.###} units={5} by {6}",
            DeedToken.BlockBroken.ToTag(),
            tokens.Contains(TokenIsWild) ? "+is-wild" : "",
            broken.Code,
            caller,
            broken.Resistance,
            SumUnits(units),
            byPlayer?.PlayerName ?? playerUid);

        Deed.Emit(
            api,
            playerUid,
            tokens,
            caller: caller,
            target: EventFactBuilder.CodeOf(broken),
            lastCraft: EventFactBuilder.LastCraftCode(playerUid),
            position: pos?.Copy(),
            outputs: units);
    }

    /// <summary>Interact / ripe-drop harvest (no block break).</summary>
    public static void NotifyInteractHarvest(
        ICoreAPI api,
        IPlayer byPlayer,
        Block block,
        BlockPos pos,
        ItemStack[]? drops)
    {
        if (!IsCropOrBerry(block)
            && !AbilityBootstrap.IsFruitTreeBlock(block)
            && !ForageBlocks.IsSap(block))
        {
            return;
        }

        Emit(api, byPlayer, block, pos, ToUnits(drops), DeedToken.Harvested.ToTag());
    }

    static void Emit(
        ICoreAPI api,
        IPlayer byPlayer,
        Block block,
        BlockPos pos,
        IReadOnlyList<Deed.QuantityUnit>? quantityUnits,
        string moment)
    {
        if (api == null
            || api.Side != EnumAppSide.Server
            || byPlayer?.PlayerUID == null
            || block == null)
        {
            return;
        }

        if (api.World?.PlayerByUid(byPlayer.PlayerUID) is not IServerPlayer serverPlayer)
        {
            return;
        }

        List<string> tokens = [moment];
        if (ForagePlayerPlaced.IsWild(api.World, block, pos))
        {
            tokens.Add(TokenIsWild);
        }

        string caller = EventFactBuilder.CallerOrHand(serverPlayer);

        api.Logger.VerboseDebug(
            "[prosequor] deed {0}{1} {2} caller={3} units={4} by {5}",
            moment,
            tokens.Contains(TokenIsWild) ? "+is-wild" : "",
            block.Code,
            caller,
            SumUnits(quantityUnits),
            serverPlayer.PlayerName);

        Deed.Emit(
            api,
            serverPlayer.PlayerUID,
            tokens,
            caller: caller,
            target: EventFactBuilder.CodeOf(block),
            lastCraft: EventFactBuilder.LastCraftCode(serverPlayer),
            position: pos.Copy(),
            outputs: quantityUnits);
    }

    public static bool IsCropOrBerry(Block? block) =>
        block != null
        && (AbilityBootstrap.IsCropBlock(block) || AbilityBootstrap.IsBerryBushBlock(block));

    public static List<Deed.QuantityUnit> ToUnits(ItemStack[]? drops)
    {
        List<Deed.QuantityUnit> units = new();
        if (drops == null)
        {
            return units;
        }

        for (int i = 0; i < drops.Length; i++)
        {
            ItemStack? stack = drops[i];
            string? code = EventFactBuilder.CodeOf(stack);
            if (string.IsNullOrWhiteSpace(code) || stack!.StackSize <= 0)
            {
                continue;
            }

            units.Add(new Deed.QuantityUnit(code, stack.StackSize));
        }

        return units;
    }

    static int SumUnits(IReadOnlyList<Deed.QuantityUnit>? units)
    {
        if (units == null || units.Count == 0)
        {
            return 0;
        }

        int sum = 0;
        for (int i = 0; i < units.Count; i++)
        {
            sum += Math.Max(0, units[i].Count);
        }

        return sum;
    }

    static void ClearPending()
    {
        pendingUnits = null;
        pendingPos = null;
        pendingBlockId = 0;
    }
}
