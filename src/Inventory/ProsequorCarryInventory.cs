using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.Common;

namespace Prosequor.Inventory;

/// <summary>
/// Honest Strength carry sidecar (<c>prosequorcarry</c>). Visible via
/// <see cref="IPlayerInventoryManager.Inventories"/> / <c>InventoriesOrdered</c>;
/// not disguised as backpack content.
/// </summary>
public sealed class ProsequorCarryInventory : InventoryBasePlayer
{
    public const string InventoryClassName = "prosequorcarry";

    /// <summary>IconUtil CustomIcons key for empty carry-slot watermark.</summary>
    public const string SlotBackgroundIcon = "prosequor-strength";

    static readonly AssetLocation StrengthIconLoc =
        new("prosequor", "textures/icons/strength-attribute.svg");

    ItemSlot[] slots = Array.Empty<ItemSlot>();

    public ProsequorCarryInventory(string className, string playerUID, ICoreAPI api)
        : base(className ?? InventoryClassName, playerUID, api)
    {
        baseWeight = 0.5f;
    }

    public ProsequorCarryInventory(string inventoryID, ICoreAPI api)
        : base(inventoryID, api)
    {
        if (string.IsNullOrEmpty(className))
        {
            className = InventoryClassName;
        }

        baseWeight = 0.5f;
    }

    public override int Count => slots.Length;

    public override ItemSlot this[int slotId]
    {
        get
        {
            if (slotId < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(slotId));
            }

            // InventoryContents packets index slots before ApplyBasicSlots may have run.
            EnsureSlotCapacity(slotId + 1);
            return slots[slotId];
        }
        set
        {
            if (slotId < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(slotId));
            }

            EnsureSlotCapacity(slotId + 1);
            slots[slotId] = value ?? throw new ArgumentNullException(nameof(value));
        }
    }

    /// <summary>
    /// Ensures a registered carry inventory exists on the player's manager.
    /// Returns null when the player link or inventory manager is not ready yet
    /// (entity spawn and entity-loaded packets both run behavior init before
    /// <c>IPlayer.Entity</c> is wired).
    /// </summary>
    /// <param name="api">Optional API fallback when <c>player.Entity</c> is still null.</param>
    public static ProsequorCarryInventory? Ensure(IPlayer player, ICoreAPI? api = null)
    {
        ICoreAPI? resolvedApi = api ?? player?.Entity?.Api;
        if (player == null || resolvedApi == null || player.InventoryManager == null)
        {
            return null;
        }

        if (player.InventoryManager.GetOwnInventory(InventoryClassName) is ProsequorCarryInventory existing)
        {
            return existing;
        }

        ProsequorCarryInventory created = new(InventoryClassName, player.PlayerUID, resolvedApi);
        if (player.InventoryManager is PlayerInventoryManager mgr)
        {
            mgr.Inventories[created.InventoryID] = created;
        }
        else
        {
            // Fallback: interface getter returns a copy — will not persist, but avoids a hard crash.
            resolvedApi.Logger.Warning(
                "[prosequor] InventoryManager is not PlayerInventoryManager; carry slots may not register.");
        }

        return created;
    }

    /// <summary>
    /// Grows the slot array without dirtying (network sync may arrive before ApplyBasicSlots).
    /// </summary>
    public void EnsureSlotCapacity(int minCount)
    {
        minCount = Math.Max(0, minCount);
        if (slots.Length >= minCount)
        {
            return;
        }

        ItemSlot[] previous = slots;
        slots = GenEmptySlots(minCount);
        for (int i = 0; i < previous.Length; i++)
        {
            slots[i].Itemstack = previous[i].Itemstack;
            previous[i].Itemstack = null;
        }
    }

    /// <summary>
    /// Rebuilds slots to <paramref name="size"/>; overflow stacks spawn at the player's feet.
    /// Does not mark empty new slots dirty (avoids InventoryUpdate before the client has resized).
    /// </summary>
    public void SetSize(int size)
    {
        size = Math.Max(0, size);
        if (slots.Length == size)
        {
            return;
        }

        ItemSlot[] previous = slots;
        slots = GenEmptySlots(size);
        for (int i = 0; i < size && i < previous.Length; i++)
        {
            ItemStack? stack = previous[i].Itemstack;
            previous[i].Itemstack = null;
            if (stack == null)
            {
                continue;
            }

            slots[i].Itemstack = stack;
            // Only dirty when content actually moved into a kept slot.
            slots[i].MarkDirty();
        }

        IPlayer? player = Player;
        Vec3d? dropPos = player?.Entity?.Pos?.XYZ;
        for (int i = size; i < previous.Length; i++)
        {
            ItemStack? stack = previous[i].Itemstack;
            if (stack == null)
            {
                continue;
            }

            if (Api?.Side == EnumAppSide.Server && dropPos != null && Api.World != null)
            {
                Api.World.SpawnItemEntity(stack, dropPos);
            }

            previous[i].Itemstack = null;
        }
    }

    public override void FromTreeAttributes(ITreeAttribute tree)
    {
        // Pass null so qslots from the tree always rebuilds the array length.
        slots = tree == null ? Array.Empty<ItemSlot>() : (SlotsFromTreeAttributes(tree, null) ?? Array.Empty<ItemSlot>());
        ApplySlotChrome();
    }

    public override void ToTreeAttributes(ITreeAttribute tree) =>
        SlotsToTreeAttributes(slots, tree);

    protected override ItemSlot NewSlot(int i)
    {
        ItemSlot slot = base.NewSlot(i);
        slot.BackgroundIcon = SlotBackgroundIcon;
        return slot;
    }

    void ApplySlotChrome()
    {
        for (int i = 0; i < slots.Length; i++)
        {
            if (slots[i] != null)
            {
                slots[i].BackgroundIcon = SlotBackgroundIcon;
            }
        }
    }

    /// <summary>
    /// Registers the strength attribute SVG as a slot background icon (client only).
    /// </summary>
    public static void RegisterSlotBackgroundIcon(ICoreClientAPI capi)
    {
        if (capi.Gui.Icons.CustomIcons.ContainsKey(SlotBackgroundIcon))
        {
            return;
        }

        IAsset? asset = capi.Assets.TryGet(StrengthIconLoc);
        if (asset == null)
        {
            capi.Logger.Warning("[prosequor] Missing carry-slot icon {0}.", StrengthIconLoc);
            return;
        }

        capi.Gui.Icons.CustomIcons[SlotBackgroundIcon] = capi.Gui.Icons.SvgIconSource(asset);
    }

    /// <summary>Registers the inventory class with the engine ClassRegistry (both sides).</summary>
    public static void RegisterClass(ICoreAPI api)
    {
        object? registryNative = GetClassRegistryNative(api);
        if (registryNative is not ClassRegistry registry)
        {
            api.Logger.Warning("[prosequor] Could not resolve ClassRegistryNative; carry inventory may not persist.");
            return;
        }

        registry.RegisterInventoryClass(InventoryClassName, typeof(ProsequorCarryInventory));
    }

    static object? GetClassRegistryNative(ICoreAPI api)
    {
        Type? apiType = api.GetType();
        while (apiType != null)
        {
            System.Reflection.PropertyInfo? prop = apiType.GetProperty(
                "ClassRegistryNative",
                System.Reflection.BindingFlags.Instance
                | System.Reflection.BindingFlags.Public
                | System.Reflection.BindingFlags.NonPublic);
            if (prop != null)
            {
                return prop.GetValue(api);
            }

            System.Reflection.FieldInfo? field = apiType.GetField(
                "ClassRegistryNative",
                System.Reflection.BindingFlags.Instance
                | System.Reflection.BindingFlags.Public
                | System.Reflection.BindingFlags.NonPublic);
            if (field != null)
            {
                return field.GetValue(api);
            }

            apiType = apiType.BaseType;
        }

        return null;
    }
}
