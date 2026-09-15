using Prosequor.Ability;
using Prosequor.Xp.Activity;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace Prosequor.Xp.Adapters;

/// <summary>
/// XP from broken blocks. Dig/mine/chop → <c>block-broken</c>; harvest → <c>harvested</c>.
/// Caller = tool, bomb, or <c>@hand</c>; target = broken block.
/// </summary>
public class BlockBreakXpAdapter
{
    /// <summary>Legacy dig activity id (no longer emitted).</summary>
    public const string VerbDig = "prosequor:dig";

    /// <summary>Legacy chop activity id (no longer emitted).</summary>
    public const string VerbChop = "prosequor:chop";

    /// <summary>Legacy mine activity id (no longer emitted).</summary>
    public const string VerbMine = "prosequor:mine";

    readonly ICoreServerAPI sapi;

    public BlockBreakXpAdapter(ICoreServerAPI sapi, XpActionDispatcher dispatcher)
    {
        this.sapi = sapi;
        _ = dispatcher;
    }

    public void Start()
    {
    }

    public void Dispose()
    {
    }

    public void NotifyBlockBroken(IPlayer byPlayer, Block broken, BlockPos pos) =>
        NotifyBlockBroken(byPlayer?.PlayerUID, broken, pos, callerOverride: null, byPlayer);

    /// <summary>
    /// Bomb blast: same <c>block-broken</c> mine deed as a pickaxe swing, with
    /// <paramref name="bombCaller"/> as the instrument (not the hotbar).
    /// Soil / wood / harvest blocks exploded by a bomb are not a mining path.
    /// </summary>
    public void NotifyBlockExploded(string? playerUid, Block broken, BlockPos pos, string? bombCaller)
    {
        if (string.IsNullOrWhiteSpace(bombCaller)
            || BlockBreakClassification.ClassifyToken(broken) != BlockBreakClassification.TokenMine)
        {
            return;
        }

        NotifyBlockBroken(playerUid, broken, pos, bombCaller, byPlayer: null);
    }

    void NotifyBlockBroken(
        string? playerUid,
        Block broken,
        BlockPos pos,
        string? callerOverride,
        IPlayer? byPlayer)
    {
        if (string.IsNullOrWhiteSpace(playerUid) || broken == null || broken.Id == 0)
        {
            return;
        }

        // Reed override and Block.OnBlockBroken can both fire for one break.
        if (!TryClaimBreak(playerUid, broken, pos, sapi.World))
        {
            return;
        }

        IPlayer? player = byPlayer ?? sapi.World.PlayerByUid(playerUid);
        string? classify = BlockBreakClassification.ClassifyToken(broken);
        if (classify == null)
        {
            return;
        }

        float hardness = 0f;
        string? hardnessDomain = null;
        DeedToken deedToken;
        if (classify is BlockBreakClassification.TokenDig
            or BlockBreakClassification.TokenMine
            or BlockBreakClassification.TokenChop)
        {
            hardness = broken.Resistance;
            hardnessDomain = classify;
            deedToken = DeedToken.BlockBroken;
        }
        else if (classify == BlockBreakClassification.TokenHarvest)
        {
            if (player == null)
            {
                return;
            }

            // Crops / berry bushes / mushrooms: quantity + domesticated tokens.
            // Fruit trees: DropHarvestScope → NotifyInteractHarvest (interact and ripe break).
            if (AbilityBootstrap.IsFruitTreeBlock(broken))
            {
                return;
            }

            if (HarvestXp.IsCropOrBerry(broken) || ForageBlocks.IsMushroom(broken))
            {
                if (ForageBlocks.IsMushroom(broken)
                    && !ForagePlayerPlaced.IsWild(sapi.World, broken, pos))
                {
                    return;
                }

                HarvestXp.NotifyBlockBroken(sapi, player, broken, pos);
                return;
            }

            if (ForageBlocks.IsFlatForage(broken))
            {
                if (!ForagePlayerPlaced.IsWild(sapi.World, broken, pos))
                {
                    return;
                }

                EmitFlatForage(player, broken, pos);
                return;
            }

            deedToken = DeedToken.Harvested;
        }
        else
        {
            return;
        }

        string caller = !string.IsNullOrWhiteSpace(callerOverride)
            ? callerOverride.Trim()
            : EventFactBuilder.CallerOrHand(player);
        IReadOnlyList<Deed.QuantityUnit>? quantityUnits =
            classify == BlockBreakClassification.TokenDig
                ? HarvestXp.TakePendingUnits(broken, pos)
                : null;

        sapi.Logger.VerboseDebug(
            "[prosequor] deed {0} {1} caller={2} hardness={3:0.###} domain={4} units={5} by {6}",
            deedToken.ToTag(),
            broken.Code,
            caller,
            hardness,
            hardnessDomain ?? "-",
            SumUnits(quantityUnits),
            player?.PlayerName ?? playerUid);

        Deed.Emit(
            sapi,
            playerUid,
            deedToken,
            caller: caller,
            target: EventFactBuilder.CodeOf(broken),
            lastCraft: EventFactBuilder.LastCraftCode(playerUid),
            metric: hardness,
            metricDomain: hardnessDomain,
            position: pos?.Copy(),
            quantityUnits: quantityUnits);
    }

    /// <summary>Classify token dig/mine/chop/harvest, or null.</summary>
    public static string? Classify(Block broken) =>
        BlockBreakClassification.ClassifyToken(broken);

    static long lastClaimMs;
    static int lastClaimBlockId;
    static string? lastClaimUid;
    static BlockPos? lastClaimPos;

    /// <summary>Skip a second notify for the same break in the same millisecond.</summary>
    static bool TryClaimBreak(string playerUid, Block broken, BlockPos pos, IWorldAccessor world)
    {
        long now = world.ElapsedMilliseconds;
        if (lastClaimPos != null
            && lastClaimPos.Equals(pos)
            && lastClaimBlockId == broken.Id
            && lastClaimUid == playerUid
            && lastClaimMs == now)
        {
            return false;
        }

        lastClaimMs = now;
        lastClaimBlockId = broken.Id;
        lastClaimUid = playerUid;
        lastClaimPos = pos.Copy();
        return true;
    }

    void EmitFlatForage(IPlayer player, Block broken, BlockPos pos)
    {
        string caller = EventFactBuilder.CallerOrHand(player);
        sapi.Logger.VerboseDebug(
            "[prosequor] deed harvested+undomesticated {0} caller={1} by {2}",
            broken.Code,
            caller,
            player.PlayerName);

        Deed.Emit(
            sapi,
            player.PlayerUID,
            [DeedToken.Harvested.ToTag(), HarvestXp.TokenUndomesticated],
            caller: caller,
            target: EventFactBuilder.CodeOf(broken),
            lastCraft: EventFactBuilder.LastCraftCode(player),
            position: pos?.Copy());
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
}
