using Prosequor.Ability;
using Prosequor.Xp.Activity;
using Vintagestory.API.Common;
using Vintagestory.API.Server;

namespace Prosequor.Xp.Adapters;

/// <summary>
/// XP from clay-form / anvil good-voxel progress. One flat <c>crafting</c> deed per novel voxel.
/// </summary>
public class ClayFormXpAdapter
{
    public const string VerbClayForm = AbilityBootstrap.VerbClayForm;

    readonly ICoreServerAPI sapi;

    public ClayFormXpAdapter(ICoreServerAPI sapi, XpActionDispatcher dispatcher)
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

    /// <param name="voxelCount">Novel good-voxel delta. One flat emit per voxel.</param>
    /// <param name="targetCode">Selected recipe output collectible code.</param>
    public void NotifyProgress(IPlayer byPlayer, int voxelCount, string? targetCode)
    {
        if (byPlayer == null || voxelCount <= 0)
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
                caller: CallerIdentities.Hand,
                target: targetCode,
                lastCraft: lastCraft);
        }
    }
}
