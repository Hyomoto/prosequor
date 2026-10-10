using System.Reflection;
using Atlas.Api;
using Atlas.XUnit;
using Prosequor.Ability;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Xunit;

namespace Prosequor.Scenarios;

/// <summary>
/// Immersive Woodworking chopping-block auto-restock: keep holding an axe after a
/// log splits and the block should pull the next matching log from the hotbar.
/// Drives the same catch-up the server uses in <c>callOnUsingBlock</c>, with
/// Prosequor's block-interaction speed prefix loaded beside Immersive Woodworking.
/// </summary>
[AtlasWorld(Mods = ["staged-iw"])]
public class ChoppingBlockRestockScenarios : AtlasScenarioBase
{
    [AtlasScenario(TimeoutMs = 120_000)]
    [Trait("Layer", "Compat")]
    [Trait("Kind", "Woodworking")]
    public async Task HeldChop_Should_RestockNextLogFromHotbar()
    {
        ITestPlayer joined = await World.JoinPlayer("IwChopRestock");
        IPlayer player = joined.Player;
        IWorldAccessor world = World.Api.World;

        Block choppingBlock = RequireBlock(world, "immersivewoodworking", "choppingblock");
        Block log = RequireLog(world);
        Item axe = RequireItem(world, "game:axe-felling-copper");

        IPlayerInventoryManager inventories = player.InventoryManager
            ?? throw new Xunit.Sdk.XunitException("Joined player has no inventory manager.");
        EntityPlayer body = player.Entity
            ?? throw new Xunit.Sdk.XunitException("Joined player has no entity.");
        IInventory hotbar = inventories.GetHotbarInventory();
        Assert.True(hotbar.Count >= 2, "Expected a hotbar with at least two slots.");
        ItemSlot axeSlot = Slot(hotbar, 0);
        ItemSlot spareSlot = Slot(hotbar, 1);
        axeSlot.Itemstack = new ItemStack(axe, 1);
        spareSlot.Itemstack = new ItemStack(log, 2);
        inventories.ActiveHotbarSlotNumber = 1;

        EntityControls controls = body.Controls;
        controls.ShiftKey = false;
        controls.CtrlKey = false;
        controls.RightMouseDown = true;

        BlockPos pos = body.Pos.AsBlockPos.AddCopy(2, 0, 0);
        world.BlockAccessor.SetBlock(0, pos);
        world.BlockAccessor.SetBlock(choppingBlock.BlockId, pos);
        BlockEntity? entity = world.BlockAccessor.GetBlockEntity(pos);
        Assert.NotNull(entity);
        IInventory blockInv = RequireInventory(entity);
        ItemSlot blockSlot = Slot(blockInv, 0);

        BlockSelection sel = new()
        {
            Position = pos,
            Face = BlockFacing.UP,
            HitPosition = new Vec3d(0.5, 0.9, 0.5)
        };

        Assert.True(
            choppingBlock.OnBlockInteractStart(world, player, sel),
            "Placing a log on the chopping block should start an interaction.");
        Assert.True(IsLog(blockSlot.Itemstack), $"Block did not accept {Code(log)}.");
        Assert.Equal(1, spareSlot.Itemstack?.StackSize ?? 0);

        inventories.ActiveHotbarSlotNumber = 0;
        Assert.Equal(EnumTool.Axe, inventories.ActiveTool);
        Assert.True(
            choppingBlock.OnBlockInteractStart(world, player, sel),
            "Holding an axe on a loaded chopping block should begin the chop.");

        controls.HandUse = EnumHandInteract.BlockInteract;
        controls.UsingBeginMS = world.ElapsedMilliseconds;
        controls.UsingCount = 0;
        long origin = controls.UsingBeginMS;

        float speed = InteractionSpeedStation.Run(
            player,
            choppingBlock.Code?.ToString(),
            axe.Code?.ToString(),
            1f,
            choppingBlock.BlockMaterial);
        bool restocked = false;
        int guard = 0;
        while (world.ElapsedMilliseconds - origin < 10_000 && guard++ < 500)
        {
            await World.Ticks(2);
            float seconds = (world.ElapsedMilliseconds - origin) / 1000f;
            for (int burst = 0; burst < 4 && controls.HandUse != EnumHandInteract.None; burst++)
            {
                bool keep = choppingBlock.OnBlockInteractStep(seconds, world, player, sel);
                controls.HandUse = keep ? EnumHandInteract.BlockInteract : EnumHandInteract.None;
                controls.UsingCount++;
                seconds += 0.02f;
            }

            if (IsLog(blockSlot.Itemstack) && (spareSlot.Itemstack == null || spareSlot.Itemstack.StackSize == 0))
            {
                restocked = true;
                break;
            }
        }

        float held = (world.ElapsedMilliseconds - origin) / 1000f;
        int spare = spareSlot.Itemstack?.StackSize ?? 0;
        string onBlock = blockSlot.Itemstack?.Collectible?.Code?.ToString() ?? "(empty)";
        Assert.True(
            restocked,
            $"Chopping block did not pull the next log after {held:0.00}s of held chopping "
            + $"(interaction speed {speed:0.000}, on block {onBlock}, spare logs {spare}, "
            + $"nearby pieces {CountNearbyPieces(world, pos)}).");
        Assert.True(IsLog(blockSlot.Itemstack));
        Assert.Equal(0, spare);
    }

    static int CountNearbyPieces(IWorldAccessor world, BlockPos pos)
    {
        int count = 0;
        foreach (Entity entity in world.GetEntitiesAround(pos.ToVec3d().Add(0.5, 0.5, 0.5), 5f, 5f, e => e is EntityItem))
        {
            if (entity is not EntityItem item)
            {
                continue;
            }

            string? path = item.Itemstack?.Collectible?.Code?.Path;
            if (path != null && (path.Contains("halflog", StringComparison.Ordinal) || path.Contains("firewood", StringComparison.Ordinal)))
            {
                count += item.Itemstack!.StackSize;
            }
        }

        return count;
    }

    static ItemSlot Slot(IInventory inventory, int index)
    {
        ItemSlot? slot = inventory[index];
        if (slot == null)
        {
            throw new Xunit.Sdk.XunitException($"Inventory slot {index} is missing.");
        }

        return slot;
    }

    static IInventory RequireInventory(BlockEntity entity)
    {
        FieldInfo? field = entity.GetType().GetField(
            "inventory",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        IInventory? inventory = field.GetValue(entity) as IInventory;
        Assert.NotNull(inventory);
        return inventory;
    }

    static bool IsLog(ItemStack? stack)
    {
        string? path = stack?.Block?.Code?.Path;
        return path != null && path.StartsWith("log-", StringComparison.Ordinal);
    }

    static Block RequireBlock(IWorldAccessor world, string domain, string pathPrefix)
    {
        foreach (Block block in world.Blocks)
        {
            if (block?.Code == null || block.Id == 0)
            {
                continue;
            }

            if (block.Code.Domain == domain && block.Code.Path.StartsWith(pathPrefix, StringComparison.Ordinal))
            {
                return block;
            }
        }

        throw new Xunit.Sdk.XunitException($"No block {domain}:{pathPrefix}* is loaded.");
    }

    static Block RequireLog(IWorldAccessor world)
    {
        Block? exact = world.GetBlock(new AssetLocation("game:log-placed-oak-ud"));
        if (exact != null && exact.Id != 0)
        {
            return exact;
        }

        foreach (Block block in world.Blocks)
        {
            string? path = block?.Code?.Path;
            if (block?.Code?.Domain == "game"
                && block.Id != 0
                && path != null
                && path.StartsWith("log-placed-", StringComparison.Ordinal)
                && path.EndsWith("-ud", StringComparison.Ordinal))
            {
                return block;
            }
        }

        throw new Xunit.Sdk.XunitException("No game log block is loaded.");
    }

    static Item RequireItem(IWorldAccessor world, string code)
    {
        Item? item = world.GetItem(new AssetLocation(code));
        if (item == null || item.Id == 0)
        {
            throw new Xunit.Sdk.XunitException($"Item {code} is not loaded.");
        }

        return item;
    }

    static string Code(CollectibleObject collectible) =>
        collectible.Code?.ToString() ?? collectible.GetType().Name;
}
