using System.Runtime.CompilerServices;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.GameContent;

namespace Prosequor.Ability;

/// <summary>
/// Notes the last oven interactor so bake completion can stamp quality with their progress.
/// </summary>
public static class OvenCookStarterStation
{
    public const string LastInteractorAttr = "prosequorOvenLastInteractor";

    static readonly ConditionalWeakTable<BlockEntity, Box> boxes = new();

    sealed class Box
    {
        public string? LastInteractorUid;
    }

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

        Box box = boxes.GetOrCreateValue(oven);
        box.LastInteractorUid = playerUid.Trim();
        oven.MarkDirty(redrawOnClient: false);
    }

    public static string? TryGetLastInteractor(BlockEntityOven? oven)
    {
        if (oven == null || !boxes.TryGetValue(oven, out Box? box) || box == null)
        {
            return null;
        }

        return string.IsNullOrWhiteSpace(box.LastInteractorUid) ? null : box.LastInteractorUid;
    }

    public static void WriteToTree(BlockEntityOven oven, ITreeAttribute tree)
    {
        if (!boxes.TryGetValue(oven, out Box? box)
            || box == null
            || string.IsNullOrEmpty(box.LastInteractorUid))
        {
            return;
        }

        tree.SetString(LastInteractorAttr, box.LastInteractorUid);
    }

    public static void ReadFromTree(BlockEntityOven oven, ITreeAttribute tree)
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

        Box box = boxes.GetOrCreateValue(oven);
        box.LastInteractorUid = last.Trim();
    }
}
