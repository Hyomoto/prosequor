using Prosequor.Ability;
using Prosequor.Xp.Activity;
using Vintagestory.API.Common;
using Vintagestory.API.Server;
using Vintagestory.Common;

namespace Prosequor.Xp.Adapters;

/// <summary>
/// XP from craft-grid takes. Awards <c>crafted</c> with caller <c>@grid</c>.
/// Quantity channel uses output stack size × recipe completions (same exclude model as drops).
/// </summary>
public class CraftXpAdapter
{
    public const string VerbCraft = CraftMutateOutputStation.VerbCraft;

    readonly ICoreServerAPI sapi;

    public CraftXpAdapter(ICoreServerAPI sapi, XpActionDispatcher dispatcher)
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

    public void NotifyTake(IPlayer byPlayer, ItemStack? crafted, int totalUnits, int craftCount)
    {
        if (byPlayer == null || crafted?.Collectible == null)
        {
            return;
        }

        if (sapi.World.PlayerByUid(byPlayer.PlayerUID) is not IServerPlayer serverPlayer)
        {
            return;
        }

        string? target = EventFactBuilder.CodeOf(crafted);
        if (string.IsNullOrWhiteSpace(target))
        {
            return;
        }

        int reps = Math.Max(1, craftCount);
        int perCraft = Math.Max(0, crafted.StackSize);
        int outputCount = perCraft * reps;
        List<Deed.QuantityUnit>? units = null;
        if (outputCount > 0)
        {
            units = [new Deed.QuantityUnit(target, outputCount)];
        }

        sapi.Logger.VerboseDebug(
            "[prosequor] deed crafted @grid {0} out={1} (×{2}) units/craft={3} by {4}",
            target,
            outputCount,
            reps,
            totalUnits,
            serverPlayer.PlayerName);

        Deed.Emit(
            sapi,
            serverPlayer.PlayerUID,
            DeedToken.Crafted,
            caller: CallerIdentities.Grid,
            target: target,
            lastCraft: EventFactBuilder.LastCraftCode(serverPlayer),
            totalUnits: Math.Max(0, totalUnits),
            craftCount: reps,
            quantityUnits: units);
    }

    public static int SumTotalUnits(IReadOnlyList<ItemStack> stacks)
    {
        int total = 0;
        for (int i = 0; i < stacks.Count; i++)
        {
            ItemStack? stack = stacks[i];
            if (stack != null)
            {
                total += Math.Max(0, stack.StackSize);
            }
        }

        return total;
    }

    public static int SumIngredientUnitsInGrid(InventoryBase inventory)
    {
        if (inventory == null || inventory.Count <= 1)
        {
            return 0;
        }

        int total = 0;
        int ingredientCount = inventory.Count - 1;
        for (int i = 0; i < ingredientCount; i++)
        {
            ItemStack? stack = inventory[i]?.Itemstack;
            if (stack != null)
            {
                total += Math.Max(0, stack.StackSize);
            }
        }

        return total;
    }
}
