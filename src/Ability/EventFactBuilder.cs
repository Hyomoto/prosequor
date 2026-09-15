using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;

namespace Prosequor.Ability;

/// <summary>
/// Thin standard tag load: copies identities in hand. Does not walk collections.
/// </summary>
public static class EventFactBuilder
{
    public static string? CodeOf(CollectibleObject? collectible) =>
        collectible?.Code?.ToString();

    public static string? CodeOf(ItemStack? stack) =>
        CodeOf(stack?.Collectible);

    public static string? CodeOf(Block? block) =>
        block?.Code?.ToString();

    public static string? CodeOf(Entity? entity) =>
        entity?.Code?.ToString();

    public static string? HeldCode(IPlayer? player) =>
        CodeOf(player?.InventoryManager?.ActiveHotbarSlot?.Itemstack);

    /// <summary>
    /// Deed / harvest caller: a held <see cref="EnumTool"/> (knife, scythe, …)
    /// keeps its asset code; empty hands or a non-tool stack become
    /// <see cref="CallerIdentities.Hand"/>.
    /// </summary>
    public static string CallerOrHand(IPlayer? player) =>
        CallerOrHand(player?.InventoryManager?.ActiveHotbarSlot?.Itemstack);

    public static string CallerOrHand(ItemStack? stack)
    {
        if (stack?.Collectible == null || stack.Collectible.Id == 0)
        {
            return CallerIdentities.Hand;
        }

        if (stack.Collectible.Tool == null)
        {
            return CallerIdentities.Hand;
        }

        return CodeOf(stack) ?? CallerIdentities.Hand;
    }

    public static string? LastCraftCode(IPlayer? player)
    {
        if (LastCraftStation.TryGet(player, out AbilityAction last)
            && !string.IsNullOrWhiteSpace(last.Target))
        {
            return last.Target;
        }

        return null;
    }

    public static string? LastCraftCode(string? playerUid)
    {
        if (LastCraftStation.TryGet(playerUid, out AbilityAction last)
            && !string.IsNullOrWhiteSpace(last.Target))
        {
            return last.Target;
        }

        return null;
    }

    public static string? GroundUnder(Entity? entity)
    {
        if (entity?.World == null || entity.Pos == null)
        {
            return null;
        }

        BlockPos feet = entity.Pos.AsBlockPos.DownCopy();
        Block? below = entity.World.BlockAccessor.GetBlock(feet);
        return below != null && below.Id != 0 ? CodeOf(below) : null;
    }

    public static string? GroundUnder(IWorldAccessor world, BlockPos pos)
    {
        Block? below = world.BlockAccessor.GetBlock(pos.DownCopy());
        return below != null && below.Id != 0 ? CodeOf(below) : null;
    }

    public static AbilityAction Build(
        string verb,
        string actorUid,
        string? held = null,
        string? target = null,
        string? lastCraft = null,
        string? ground = null,
        string? op = null,
        string? drop = null,
        IEnumerable<string>? tokens = null,
        IEnumerable<string>? damage = null,
        IEnumerable<string>? inputs = null,
        BlockPos? position = null,
        string? mount = null)
    {
        HashSet<string> tokenSet = new(StringComparer.OrdinalIgnoreCase);
        if (tokens != null)
        {
            foreach (string t in tokens)
            {
                if (!string.IsNullOrWhiteSpace(t))
                {
                    tokenSet.Add(t.Trim());
                }
            }
        }

        HashSet<string> damageSet = new(StringComparer.OrdinalIgnoreCase);
        if (damage != null)
        {
            foreach (string d in damage)
            {
                if (!string.IsNullOrWhiteSpace(d))
                {
                    damageSet.Add(d.Trim());
                }
            }
        }

        List<string> inputList = new();
        if (inputs != null)
        {
            foreach (string input in inputs)
            {
                if (!string.IsNullOrWhiteSpace(input))
                {
                    inputList.Add(input.Trim());
                }
            }
        }

        return new AbilityAction
        {
            Verb = verb,
            ActorUid = actorUid,
            Held = held,
            Target = target,
            Drop = drop,
            LastCraft = lastCraft,
            Ground = ground,
            Mount = mount,
            Op = op,
            Inputs = inputList,
            Tokens = tokenSet,
            Damage = damageSet,
            Position = position
        };
    }

    /// <summary>Copies <paramref name="fact"/> with an extra event token (e.g. <c>undomesticated</c>).</summary>
    public static AbilityAction WithToken(AbilityAction fact, string token)
    {
        HashSet<string> tokens = new(fact.Tokens, StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(token))
        {
            tokens.Add(token.Trim());
        }

        return new AbilityAction
        {
            Verb = fact.Verb,
            ActorUid = fact.ActorUid,
            Held = fact.Held,
            Target = fact.Target,
            Drop = fact.Drop,
            LastCraft = fact.LastCraft,
            Ground = fact.Ground,
            Mount = fact.Mount,
            Op = fact.Op,
            Inputs = fact.Inputs,
            Tokens = tokens,
            Damage = fact.Damage,
            Position = fact.Position
        };
    }

    /// <summary>Copies <paramref name="fact"/> with a per-stack <see cref="AbilityAction.Drop"/> identity.</summary>
    public static AbilityAction WithDrop(AbilityAction fact, string? dropCode) =>
        new()
        {
            Verb = fact.Verb,
            ActorUid = fact.ActorUid,
            Held = fact.Held,
            Target = fact.Target,
            Drop = dropCode,
            LastCraft = fact.LastCraft,
            Ground = fact.Ground,
            Mount = fact.Mount,
            Op = fact.Op,
            Inputs = fact.Inputs,
            Tokens = fact.Tokens,
            Damage = fact.Damage,
            Position = fact.Position
        };

    public static AbilityAction ForPlayer(
        IPlayer player,
        string verb,
        string? target = null,
        string? held = null,
        string? ground = null,
        string? op = null,
        IEnumerable<string>? tokens = null,
        IEnumerable<string>? damage = null,
        IEnumerable<string>? inputs = null,
        BlockPos? position = null,
        bool includeLastCraft = true)
    {
        string? heldCode = held ?? HeldCode(player);
        string? lastCraft = includeLastCraft ? LastCraftCode(player) : null;
        return Build(
            verb,
            player.PlayerUID,
            heldCode,
            target,
            lastCraft,
            ground,
            op,
            drop: null,
            tokens,
            damage,
            inputs,
            position);
    }

    public static IReadOnlyList<string> CodesOf(IReadOnlyList<ItemStack> stacks)
    {
        List<string> codes = new(stacks.Count);
        for (int i = 0; i < stacks.Count; i++)
        {
            string? code = CodeOf(stacks[i]);
            if (!string.IsNullOrWhiteSpace(code))
            {
                codes.Add(code);
            }
        }

        return codes;
    }
}
