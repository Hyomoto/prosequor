using Prosequor.Ability;
using Prosequor.Xp.Activity;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Server;

namespace Prosequor.Xp.Adapters;

/// <summary>
/// A mob's harvestable inventory became drops. Always <c>mob-harvested</c>.
/// <c>cleaver-certain</c> is added at the cleaver line. Rules keep meat and fat via <c>include</c>.
/// </summary>
public static class ButcherXp
{
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

        List<Deed.QuantityUnit> units = ToUnits(drops);
        if (units.Count == 0)
        {
            return;
        }

        string? tool = EventFactBuilder.HeldCode(serverPlayer);
        string caller = string.IsNullOrWhiteSpace(tool) ? CallerIdentities.Hand : tool;

        bool cleaverCertain = AnimalWeightCatalog.CanCleaverSlaughter(entity);
        List<string> tokens = [DeedTokenTags.MobHarvested];
        if (cleaverCertain)
        {
            tokens.Add(DeedTokenTags.CleaverCertain);
        }

        api.Logger.VerboseDebug(
            "[prosequor] deed mob-harvested{0} {1} units={2} by {3}",
            cleaverCertain ? "+cleaver-certain" : "",
            entity.Code,
            SumUnits(units),
            serverPlayer.PlayerName);

        Deed.Emit(
            api,
            serverPlayer.PlayerUID,
            tokens,
            caller: caller,
            target: EventFactBuilder.CodeOf(entity),
            lastCraft: EventFactBuilder.LastCraftCode(serverPlayer),
            position: entity.Pos?.AsBlockPos?.Copy(),
            outputs: units);
    }

    public static List<Deed.QuantityUnit> ToUnits(ItemStack[]? drops)
    {
        List<Deed.QuantityUnit> units = new();
        if (drops == null)
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
