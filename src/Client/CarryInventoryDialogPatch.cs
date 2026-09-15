using System.Reflection;
using HarmonyLib;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Prosequor.Ability;
using Prosequor.Inventory;

namespace Prosequor.Client;

/// <summary>
/// Adds a basic-slots grid under the backpack content grid.
/// </summary>
[HarmonyPatch(typeof(GuiComposer), nameof(GuiComposer.Compose))]
public static class CarryInventoryDialogPatch
{
    const string GridKey = "prosequorcarry-grid";
    const int Columns = 6;
    /// <summary>Vanilla backpack bag-equipment slots excluded from the content grid.</summary>
    const int VanillaBagSlotCount = 4;

    static bool recomposeQueued;
    static bool composingInventoryBackpack;

    /// <summary>
    /// Rebuilds the open survival inventory dialog so the basic-slots grid matches Count.
    /// Deferred to avoid re-entering Compose from ApplyBasicSlots during Prefix.
    /// </summary>
    public static void RequestRecomposeIfOpen(ICoreClientAPI capi)
    {
        if (composingInventoryBackpack || recomposeQueued)
        {
            return;
        }

        recomposeQueued = true;
        capi.Event.EnqueueMainThreadTask(
            () =>
            {
                recomposeQueued = false;
                TryRecomposeSurvivalInventory(capi);
            },
            "prosequor-basic-slots-inv");
    }

    static void TryRecomposeSurvivalInventory(ICoreClientAPI capi)
    {
        GuiDialog? dialog = null;
        foreach (GuiDialog opened in capi.Gui.OpenedGuis)
        {
            if (opened.GetType().Name == "GuiDialogInventory")
            {
                dialog = opened;
                break;
            }
        }

        if (dialog == null)
        {
            return;
        }

        MethodInfo? composeGui = AccessTools.Method(
            dialog.GetType(),
            "ComposeGui",
            [typeof(bool)]);
        composeGui?.Invoke(dialog, [false]);
    }

    public static void Prefix(GuiComposer __instance)
    {
        if (__instance.DialogName != "inventory-backpack")
        {
            return;
        }

        ICoreClientAPI? capi = __instance.Api as ICoreClientAPI;
        if (capi == null || __instance.GetElement(GridKey) != null)
        {
            return;
        }

        IPlayer? player = capi.World.Player;
        if (player?.Entity == null)
        {
            return;
        }

        composingInventoryBackpack = true;
        try
        {
            BuildCarryGrid(__instance, capi, player);
        }
        finally
        {
            composingInventoryBackpack = false;
        }
    }

    static void BuildCarryGrid(GuiComposer composer, ICoreClientAPI capi, IPlayer player)
    {
        // Ensure Count matches resolved basic-slots before building the grid.
        PlayerInteractionStation.ApplyBasicSlots(player.Entity);

        IInventory? carry = player.InventoryManager
            .GetOwnInventory(ProsequorCarryInventory.InventoryClassName);
        if (carry == null || carry.Count <= 0)
        {
            return;
        }

        GuiElement? backpackGrid = composer.GetElement("slotgrid");
        if (backpackGrid == null)
        {
            return;
        }

        double slotStep = GuiElementPassiveItemSlot.unscaledSlotSize
            + GuiElementItemSlotGridBase.unscaledSlotPadding;
        int rows = (int)Math.Ceiling(carry.Count / (double)Columns);
        int[] selective = new int[carry.Count];
        for (int i = 0; i < selective.Length; i++)
        {
            selective[i] = i;
        }

        // slotgrid.Bounds.fixedHeight is sized for ceil(backpack.Count/6), which
        // still counts excluded bag slots — so when content length is a multiple of
        // Columns there is one empty row of dead space. Place from content rows instead.
        int contentSlots = CountBackpackContentSlots(player);
        int contentRows = (int)Math.Ceiling(contentSlots / (double)Columns);

        ElementBounds bounds = ElementStdBounds.SlotGrid(
            EnumDialogArea.None,
            0,
            0,
            Math.Min(Columns, carry.Count),
            rows);
        bounds.fixedX = backpackGrid.Bounds.fixedX;
        bounds.fixedY = backpackGrid.Bounds.fixedY + contentRows * slotStep + slotStep * 0.25;
        bounds.WithParent(backpackGrid.Bounds.ParentBounds);

        void SendPacket(object packet) => capi.Network.SendPacketClient(packet);

        composer.AddItemSlotGrid(carry, SendPacket, Columns, selective, bounds, GridKey);
    }

    static int CountBackpackContentSlots(IPlayer player)
    {
        IInventory? backpack = player.InventoryManager.GetOwnInventory("backpack");
        if (backpack == null)
        {
            return 0;
        }

        return Math.Max(0, backpack.Count - VanillaBagSlotCount);
    }
}
