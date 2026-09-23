using Vintagestory.API.Common;
using Vintagestory.GameContent;

namespace Prosequor.Ability;

/// <summary>
/// Notes the last oven interactor on the pedigree host so bake completion can stamp quality.
/// </summary>
public static class OvenCookStarterStation
{
    public const string LastInteractorAttr = "prosequorOvenLastInteractor";

    public static void NoteInteractor(BlockEntityOven? oven, IPlayer? player)
    {
        if (oven?.Api?.Side != EnumAppSide.Server || string.IsNullOrEmpty(player?.PlayerUID))
        {
            return;
        }

        NoteInteractor(oven, player!.PlayerUID);
    }

    public static void NoteInteractor(BlockEntityOven? oven, string? playerUid)
    {
        if (oven?.Api?.Side != EnumAppSide.Server || string.IsNullOrWhiteSpace(playerUid))
        {
            return;
        }

        string uid = playerUid.Trim();
        ProsequorBlockPedigreeStation.Mutate(oven, box => box.LastInteractorUid = uid);
        ProsequorBlockPedigreeStation.StampSoleContributor(oven, uid);
    }

    public static string? TryGetLastInteractor(BlockEntityOven? oven)
    {
        if (oven != null
            && ProsequorBlockPedigreeStation.TryGetBox(oven, out ProsequorChunkPedigree.Box box)
            && !string.IsNullOrWhiteSpace(box.LastInteractorUid))
        {
            return box.LastInteractorUid;
        }

        if (ProsequorBlockPedigreeStation.TryGetSoleContributor(oven, out string? uid))
        {
            return uid;
        }

        return null;
    }
}
