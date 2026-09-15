using System.Reflection;
using HarmonyLib;
using Prosequor.Ability.Hooks;
using Prosequor.Xp.Activity;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace Prosequor.Ability;

/// <summary>
/// When a populated hive swarms into an empty skep: pay source contributors
/// (<c>skep-propagate</c>) and carry destination Live onto the new Beehive BE.
/// </summary>
public static class SkepPropagateXp
{
    static readonly FieldInfo? SkepToPopField =
        AccessTools.Field(typeof(BlockEntityBeehive), "skepToPop");

    [ThreadStatic]
    static ProsequorBlob? pendingDestBlob;

    [ThreadStatic]
    static BlockPos? pendingDestPos;

    [ThreadStatic]
    static List<Deed.ContributorShare>? pendingSourceShares;

    [ThreadStatic]
    static BlockPos? pendingSourcePos;

    [ThreadStatic]
    static string? pendingSourceTargetCode;

    /// <summary>
    /// Before <c>SetBlock</c>: snapshot empty dest pedigree and source contributor shares.
    /// </summary>
    public static void PreparePop(BlockEntityBeehive? source)
    {
        ClearPending();
        if (source?.Api?.World == null
            || source.Api.World.Side != EnumAppSide.Server
            || SkepToPopField == null)
        {
            return;
        }

        if (SkepToPopField.GetValue(source) is not BlockPos destPos)
        {
            return;
        }

        pendingDestPos = destPos.Copy();
        pendingSourcePos = source.Pos?.Copy();
        pendingSourceTargetCode = EventFactBuilder.CodeOf(source.Block);

        BlockEntity? destBe = source.Api.World.BlockAccessor.GetBlockEntity(destPos);
        if (ProsequorBlockPedigreeStation.TryGetBlob(destBe, out ProsequorBlob destBlob)
            && !destBlob.IsAnonymous)
        {
            pendingDestBlob = destBlob;
        }

        if (ProsequorBlockPedigreeStation.TryGetBlob(source, out ProsequorBlob sourceBlob))
        {
            IReadOnlyList<Deed.ContributorShare> shares =
                HusbandryContributorXp.RealContributorShares(sourceBlob);
            if (shares.Count > 0)
            {
                pendingSourceShares = new List<Deed.ContributorShare>(shares);
            }
        }
    }

    /// <summary>
    /// After <c>SetBlock</c>: restamp dest Live onto new Beehive BE; emit propagate XP.
    /// </summary>
    public static void CompletePop(BlockEntityBeehive? source)
    {
        ProsequorBlob? destBlob = pendingDestBlob;
        BlockPos? destPos = pendingDestPos;
        IReadOnlyList<Deed.ContributorShare>? sourceShares = pendingSourceShares;
        BlockPos? sourcePos = pendingSourcePos;
        string? sourceTarget = pendingSourceTargetCode;
        ClearPending();

        ICoreAPI? api = source?.Api;
        IWorldAccessor? world = api?.World;
        if (world == null || world.Side != EnumAppSide.Server || destPos == null)
        {
            return;
        }

        Block? destBlock = world.BlockAccessor.GetBlock(destPos);
        if (destBlock is not BlockSkep destSkep || destSkep.IsEmpty())
        {
            return;
        }

        BlockEntity? newBe = world.BlockAccessor.GetBlockEntity(destPos);
        if (destBlob != null && !destBlob.IsAnonymous && newBe != null)
        {
            ProsequorBlockPedigreeStation.ApplyBlob(newBe, destBlob);
        }

        if (sourceShares == null || sourceShares.Count == 0 || api == null)
        {
            return;
        }

        Deed.Emit(
            api,
            playerUid: "",
            tokens: [DeedTokenTags.SkepPropagate],
            caller: CallerIdentities.Hand,
            target: sourceTarget ?? EventFactBuilder.CodeOf(destSkep),
            position: sourcePos?.Copy() ?? destPos.Copy(),
            contributors: sourceShares);
    }

    static void ClearPending()
    {
        pendingDestBlob = null;
        pendingDestPos = null;
        pendingSourceShares = null;
        pendingSourcePos = null;
        pendingSourceTargetCode = null;
    }
}

/// <summary>Prefix/postfix on private swarm populate.</summary>
[HarmonyPatch(typeof(BlockEntityBeehive), "TryPopCurrentSkep")]
public static class BeehiveTryPopCurrentSkepPatch
{
    [HarmonyPrefix]
    public static void Prefix(BlockEntityBeehive __instance) =>
        SkepPropagateXp.PreparePop(__instance);

    [HarmonyPostfix]
    public static void Postfix(BlockEntityBeehive __instance) =>
        SkepPropagateXp.CompletePop(__instance);
}
