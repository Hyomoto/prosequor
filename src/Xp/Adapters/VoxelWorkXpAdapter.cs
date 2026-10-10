using Prosequor.Ability;
using Prosequor.Xp.Activity;
using Vintagestory.API.Common;
using Vintagestory.API.Server;

namespace Prosequor.Xp.Adapters;

/// <summary>
/// Voxel crafting XP. Progress is one flat <c>voxel-work</c> deed per novel voxel.
/// Completion is one <c>crafted</c> deed. The caller is the station block.
/// </summary>
public class VoxelWorkXpAdapter
{
    public const string VerbClayForm = AbilityBootstrap.VerbClayForm;

    readonly ICoreServerAPI sapi;

    public VoxelWorkXpAdapter(ICoreServerAPI sapi)
    {
        this.sapi = sapi;
    }

    /// <param name="voxelCount">Novel good-voxel delta. One flat emit per voxel.</param>
    /// <param name="targetCode">Selected recipe output collectible code.</param>
    /// <param name="caller">Station block that held the work (anvil or clay form).</param>
    public void NotifyProgress(IPlayer byPlayer, int voxelCount, string? targetCode, string? caller)
    {
        if (byPlayer == null || voxelCount <= 0 || string.IsNullOrWhiteSpace(caller))
        {
            return;
        }

        if (sapi.World.PlayerByUid(byPlayer.PlayerUID) is not IServerPlayer serverPlayer)
        {
            return;
        }

        string? lastCraft = EventFactBuilder.LastCraftCode(serverPlayer);
        for (int i = 0; i < voxelCount; i++)
        {
            Deed.Emit(
                sapi,
                serverPlayer.PlayerUID,
                DeedToken.Crafting,
                caller: caller.Trim(),
                target: targetCode,
                lastCraft: lastCraft);
        }
    }

    /// <summary>
    /// One <c>crafted</c> deed for a finished voxel workpiece.
    /// <paramref name="caller"/> is the station block. <paramref name="subject"/> carries the recipe stamp
    /// <c>pay: voxels</c> reads. Output units are the stack size. Maker and contributor shares are passed
    /// when the stack already has them. An empty bag still emits.
    /// </summary>
    public void NotifyFinished(IPlayer? byPlayer, ItemStack? subject, string? caller)
    {
        if (byPlayer == null || subject == null || string.IsNullOrWhiteSpace(caller))
        {
            return;
        }

        if (sapi.World.PlayerByUid(byPlayer.PlayerUID) is not IServerPlayer serverPlayer)
        {
            return;
        }

        string? target = EventFactBuilder.CodeOf(subject);
        int count = Math.Max(0, subject.StackSize);
        IReadOnlyList<Deed.QuantityUnit>? outputs = null;
        if (!string.IsNullOrWhiteSpace(target) && count > 0)
        {
            outputs = [new Deed.QuantityUnit(target, count)];
        }

        string? makerUid = CraftAttribution.TryGetMakerUid(subject);
        IReadOnlyList<Deed.ContributorShare>? shares = null;
        if (ProsequorStackPedigree.TryGetPrimaryBlob(subject, out ProsequorBlob blob))
        {
            IReadOnlyList<Deed.ContributorShare> fromBlob = blob.ToDeedShares();
            if (fromBlob.Count > 0)
            {
                shares = fromBlob;
            }
        }

        Deed.Emit(
            sapi,
            serverPlayer.PlayerUID,
            DeedToken.Crafted,
            caller: caller.Trim(),
            target: target,
            outputs: outputs,
            contributors: shares,
            makerUid: makerUid,
            subject: subject);
    }
}
