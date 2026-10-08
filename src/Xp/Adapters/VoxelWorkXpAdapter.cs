using Prosequor.Ability;
using Prosequor.Xp.Activity;
using Vintagestory.API.Common;
using Vintagestory.API.Server;

namespace Prosequor.Xp.Adapters;

/// <summary>
/// Voxel crafting XP. Progress is one flat <c>voxel-work</c> deed per novel voxel.
/// Completion is one <c>voxel-finished</c> deed whose subject is the output stack.
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
    /// One completion deed for a finished voxel workpiece. Caller is <c>@hand</c>.
    /// <paramref name="subject"/> carries the recipe stamp <c>pay: voxels</c> reads.
    /// </summary>
    public void NotifyFinished(IPlayer? byPlayer, ItemStack? subject)
    {
        if (byPlayer == null || subject == null)
        {
            return;
        }

        if (sapi.World.PlayerByUid(byPlayer.PlayerUID) is not IServerPlayer serverPlayer)
        {
            return;
        }

        Deed.Emit(
            sapi,
            serverPlayer.PlayerUID,
            DeedToken.VoxelFinished,
            caller: CallerIdentities.Hand,
            target: EventFactBuilder.CodeOf(subject),
            subject: subject);
    }
}
