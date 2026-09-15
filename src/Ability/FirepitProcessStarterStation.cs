using System.Runtime.CompilerServices;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.GameContent;

namespace Prosequor.Ability;

/// <summary>
/// Firepit process-starter: notes the last interactor, then stamps a sole contributor
/// when cooking/smelting actually begins (<c>canSmeltInput &amp;&amp; IsBurning</c> rising edge).
/// </summary>
public static class FirepitProcessStarterStation
{
    public const string LastInteractorAttr = "prosequorFirepitLastInteractor";

    static readonly ConditionalWeakTable<BlockEntity, Box> boxes = new();

    sealed class Box
    {
        public string? LastInteractorUid;
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

        Box box = boxes.GetOrCreateValue(firepit);
        box.LastInteractorUid = playerUid.Trim();
        firepit.MarkDirty(redrawOnClient: false);
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

        Box box = boxes.GetOrCreateValue(firepit);
        if (!box.HavePrev)
        {
            box.PrevProcessing = processing;
            box.HavePrev = true;
            return;
        }

        if (!box.PrevProcessing && processing)
        {
            ProsequorBlockPedigreeStation.StampSoleContributor(firepit, box.LastInteractorUid);
        }

        box.PrevProcessing = processing;
    }

    public static bool IsProcessing(BlockEntityFirepit firepit) =>
        firepit.IsBurning && firepit.canSmeltInput();

    public static void WriteToTree(BlockEntityFirepit firepit, ITreeAttribute tree)
    {
        if (!boxes.TryGetValue(firepit, out Box? box)
            || box == null
            || string.IsNullOrEmpty(box.LastInteractorUid))
        {
            return;
        }

        tree.SetString(LastInteractorAttr, box.LastInteractorUid);
    }

    public static void ReadFromTree(BlockEntityFirepit firepit, ITreeAttribute tree)
    {
        if (tree == null)
        {
            return;
        }

        string? last = tree.GetString(LastInteractorAttr);
        if (string.IsNullOrWhiteSpace(last))
        {
            return;
        }

        Box box = boxes.GetOrCreateValue(firepit);
        box.LastInteractorUid = last.Trim();
        // Leave HavePrev false: first OnAfterTick / ObserveProcessing after load
        // establishes the baseline without treating mid-process as a rising edge.
    }
}
