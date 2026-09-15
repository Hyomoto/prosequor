using Newtonsoft.Json.Linq;
using Prosequor.Ability.Hooks;
using Vintagestory.API.Common;

namespace Prosequor.Ability.Actions;

/// <summary>
/// Appends one seed stack for the broken crop block. Runs on the stacks phase so the
/// bonus seed is never seen by quantity or drop-list yield rules (e.g. Demeter's Bless).
/// </summary>
public sealed class AddCropSeedAction
    : AbilityActionHandler<DropsContext, IReadOnlyList<ItemStack>, object>
{
    public override ActionId Id => ActionIds.AddCropSeed;
    public override HookId Hook => HookIds.BlockInteraction;
    public override VerbId Verb => VerbIds.MutateDrops;
    public override PhaseId Phase => HookIds.Stacks;

    protected override bool TryParse(JObject? raw, out object? parameters, out string error)
    {
        parameters = new object();
        error = "";
        return true;
    }

    protected override IReadOnlyList<ItemStack> Apply(
        DropsContext context,
        IReadOnlyList<ItemStack> value,
        object parameters,
        AbilityRuleSource source)
    {
        Item? seed = AbilityBootstrap.TryResolveCropSeed(context.World, context.Block);
        if (seed == null)
        {
            return value;
        }

        List<ItemStack> next = new(value.Count + 1);
        next.AddRange(value);
        next.Add(new ItemStack(seed, 1));
        return next;
    }
}
