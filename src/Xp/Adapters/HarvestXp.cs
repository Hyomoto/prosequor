using Prosequor.Ability;
using Prosequor.Xp.Activity;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace Prosequor.Xp.Adapters;

/// <summary>
/// Harvest deeds for crops / berry bushes / fruit trees: quantity from drops,
/// optional <c>domesticated</c> when the plant has a planter. Actor is the harvester.
/// </summary>
public static class HarvestXp
{
    public const string TokenDomesticated = "domesticated";
    public const string TokenUndomesticated = "undomesticated";

    [ThreadStatic]
    static List<Deed.QuantityUnit>? pendingUnits;

    [ThreadStatic]
    static BlockPos? pendingPos;

    [ThreadStatic]
    static int pendingBlockId;

    /// <summary>
    /// Remember GetDrops result for the matching break-path XP emit
    /// (harvest crops/berries, or specialty dig clay/charcoal/saltpeter).
    /// </summary>
    public static void NoteBreakDrops(Block? block, BlockPos? pos, ItemStack[]? drops)
    {
        ClearPending();
        if (block == null || pos == null)
        {
            return;
        }

        if (!IsCropOrBerry(block)
            && !ForageBlocks.IsMushroom(block)
            && !AbilityBootstrap.IsFruitTreeBlock(block)
            && !BlockBreakClassification.IsSpecialtyDigBlock(block))
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

    /// <summary>
    /// Break-path harvest: uses drops noted from GetDrops when present.
    /// </summary>
    public static void NotifyBlockBroken(
        ICoreServerAPI sapi,
        IPlayer byPlayer,
        Block broken,
        BlockPos pos)
    {
        if (!IsCropOrBerry(broken)
            && !ForageBlocks.IsMushroom(broken)
            && !AbilityBootstrap.IsFruitTreeBlock(broken))
        {
            ClearPending();
            return;
        }

        IReadOnlyList<Deed.QuantityUnit>? units = TakePendingUnits(broken, pos);
        Emit(sapi, byPlayer, broken, pos, units);
    }

    /// <summary>Interact / ripe-drop harvest (no block break).</summary>
    public static void NotifyInteractHarvest(
        ICoreAPI api,
        IPlayer byPlayer,
        Block block,
        BlockPos pos,
        ItemStack[]? drops)
    {
        if (ForageBlocks.IsSap(block))
        {
            Emit(api, byPlayer, block, pos, quantityUnits: null);
            return;
        }

        if (!IsCropOrBerry(block) && !AbilityBootstrap.IsFruitTreeBlock(block))
        {
            return;
        }

        Emit(api, byPlayer, block, pos, ToUnits(drops));
    }

    static void Emit(
        ICoreAPI api,
        IPlayer byPlayer,
        Block block,
        BlockPos pos,
        IReadOnlyList<Deed.QuantityUnit>? quantityUnits)
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

        List<string> tokens = [DeedToken.Harvested.ToTag()];
        if (OwnerCredit.TryResolvePlanter(api.World, block, pos, out _))
        {
            tokens.Add(TokenDomesticated);
        }
        else if (ForagePlayerPlaced.IsWild(api.World, block, pos))
        {
            tokens.Add(TokenUndomesticated);
        }

        string caller = EventFactBuilder.CallerOrHand(serverPlayer);

        api.Logger.VerboseDebug(
            "[prosequor] deed harvested{0} {1} caller={2} units={3} by {4}",
            tokens.Contains(TokenDomesticated) ? "+domesticated" : "",
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
