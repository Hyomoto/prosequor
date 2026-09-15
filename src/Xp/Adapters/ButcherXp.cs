using Prosequor.Ability;
using Prosequor.Ability.Hooks;
using Prosequor.Xp.Activity;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Server;

namespace Prosequor.Xp.Adapters;

/// <summary>
/// Cooking butcher deed: <c>butchered</c> with <c>pay: quantity</c> = meat + fat
/// after the cooking yield fold.
/// </summary>
public static class ButcherXp
{
    public const string MeatCollectionId = "meat";
    public const string FatCollectionId = "fat";

    public static void NotifyHarvest(IPlayer byPlayer, Entity entity, ItemStack[]? drops)
    {
        ICoreAPI? api = entity?.World?.Api ?? byPlayer?.Entity?.Api;
        if (api == null
            || api.Side != EnumAppSide.Server
            || byPlayer?.PlayerUID == null
            || entity == null)
        {
            return;
        }

        if (api.World?.PlayerByUid(byPlayer.PlayerUID) is not IServerPlayer serverPlayer)
        {
            return;
        }

        CollectionIndex? tags = ProsequorModSystem.For(api)?.Collections?.Index;
        List<Deed.QuantityUnit> units = ToMeatFatUnits(drops, tags);
        if (units.Count == 0)
        {
            return;
        }

        string? tool = EventFactBuilder.HeldCode(serverPlayer);
        string caller = string.IsNullOrWhiteSpace(tool) ? CallerIdentities.Hand : tool;

        api.Logger.VerboseDebug(
            "[prosequor] deed butchered {0} units={1} by {2}",
            entity.Code,
            SumUnits(units),
            serverPlayer.PlayerName);

        Deed.Emit(
            api,
            serverPlayer.PlayerUID,
            DeedToken.Butchered,
            caller: caller,
            target: EventFactBuilder.CodeOf(entity),
            lastCraft: EventFactBuilder.LastCraftCode(serverPlayer),
            craftCount: 1,
            position: entity.Pos?.AsBlockPos?.Copy(),
            quantityUnits: units);
    }

    public static List<Deed.QuantityUnit> ToMeatFatUnits(
        ItemStack[]? drops,
        CollectionIndex? tags)
    {
        List<Deed.QuantityUnit> units = new();
        if (drops == null || tags == null)
        {
            return units;
        }

        for (int i = 0; i < drops.Length; i++)
        {
            ItemStack? stack = drops[i];
            string? code = EventFactBuilder.CodeOf(stack);
            if (string.IsNullOrWhiteSpace(code) || stack!.StackSize <= 0)
            {
                continue;
            }

            if (!tags.StackMatches(MeatCollectionId, stack)
                && !tags.StackMatches(FatCollectionId, stack))
            {
                continue;
            }

            units.Add(new Deed.QuantityUnit(code, stack.StackSize));
        }

        return units;
    }

    static int SumUnits(IReadOnlyList<Deed.QuantityUnit> units)
    {
        int sum = 0;
        for (int i = 0; i < units.Count; i++)
        {
            sum += Math.Max(0, units[i].Count);
        }

        return sum;
    }
}
