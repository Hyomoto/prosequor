using System.Runtime.CompilerServices;
using Vintagestory.API.Common;
using Vintagestory.GameContent;

namespace Prosequor.Ability;

/// <summary>
/// Firepit process-starter: notes the last interactor on the pedigree host, then stamps a
/// sole contributor when cooking/smelting actually begins (rising edge). Edge baseline is
/// ephemeral (re-established after load).
/// </summary>
public static class FirepitProcessStarterStation
{
    public const string LastInteractorAttr = "prosequorFirepitLastInteractor";

    static readonly ConditionalWeakTable<BlockEntity, EdgeBox> edges = new();

    sealed class EdgeBox
    {
        public bool PrevProcessing;
        public bool HavePrev;
    }

    public static void NoteInteractor(BlockEntityFirepit? firepit, IPlayer? player)
    {
        if (firepit?.Api?.Side != EnumAppSide.Server || string.IsNullOrEmpty(player?.PlayerUID))
        {
            return;
        }

        NoteInteractor(firepit, player!.PlayerUID);
    }

    public static void NoteInteractor(BlockEntityFirepit? firepit, string? playerUid)
    {
        if (firepit?.Api?.Side != EnumAppSide.Server || string.IsNullOrWhiteSpace(playerUid))
        {
            return;
        }

        ProsequorBlockPedigreeStation.Mutate(
            firepit,
            box => box.LastInteractorUid = playerUid.Trim());
    }

    /// <summary>
    /// After each burn tick / interact: stamp sole contributor on process rising edge.
    /// </summary>
    public static void OnAfterTick(BlockEntityFirepit? firepit)
    {
        if (firepit?.Api?.Side != EnumAppSide.Server)
        {
            return;
        }

        ObserveProcessing(firepit, IsProcessing(firepit));
    }

    /// <summary>
    /// Rising-edge sole-contributor stamp. Mid-process stays put; a later rising edge
    /// replaces the starter. Exposed for scenarios that drive the edge without a full smelt.
    /// </summary>
    public static void ObserveProcessing(BlockEntityFirepit? firepit, bool processing)
    {
        if (firepit?.Api?.Side != EnumAppSide.Server)
        {
            return;
        }

        EdgeBox edge = edges.GetOrCreateValue(firepit);
        if (!edge.HavePrev)
        {
            edge.PrevProcessing = processing;
            edge.HavePrev = true;
            return;
        }

        if (!edge.PrevProcessing && processing)
        {
            string? uid = null;
            if (ProsequorBlockPedigreeStation.TryGetBox(firepit, out ProsequorChunkPedigree.Box box))
            {
                uid = box.LastInteractorUid;
            }

            ProsequorBlockPedigreeStation.StampSoleContributor(firepit, uid);
        }

        edge.PrevProcessing = processing;
    }

    public static bool IsProcessing(BlockEntityFirepit firepit) =>
        firepit.IsBurning && firepit.canSmeltInput();
}
