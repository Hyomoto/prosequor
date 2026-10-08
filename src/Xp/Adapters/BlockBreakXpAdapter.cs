using Prosequor.Ability;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace Prosequor.Xp.Adapters;

/// <summary>
/// XP from broken blocks. Every classified break emits <c>block-broken</c> once.
/// The class fills drops, and <c>is-wild</c> when the wild check passes.
/// Caller = tool, bomb, or <c>@hand</c>; target = broken block.
/// </summary>
public class BlockBreakXpAdapter
{
    readonly ICoreServerAPI sapi;

    public BlockBreakXpAdapter(ICoreServerAPI sapi)
    {
        this.sapi = sapi;
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

        // Reed / ore overrides and Block.OnBlockBroken can both fire for one break.
        if (!TryClaimBreak(playerUid, broken, pos, sapi.World))
        {
            return;
        }

        IPlayer? player = byPlayer ?? sapi.World.PlayerByUid(playerUid);
        if (BlockBreakClassification.ClassifyToken(broken) == null)
        {
            return;
        }

        HarvestXp.EmitBlockBroken(sapi, playerUid, player, broken, pos, callerOverride);
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
}
