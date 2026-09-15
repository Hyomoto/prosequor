using Prosequor.Ability.Hooks;
using Prosequor.Player;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;

namespace Prosequor.Ability;

/// <summary>
/// Resolves <c>prosequor:entity-interaction</c> animal verbs.
/// Fact verb equals the moment verb; <c>target</c> is the animal code for <c>when</c> matching.
/// </summary>
public static class AnimalBehaviorStation
{
    public const string VerbFlee = "prosequor:animal-flee";
    public const string VerbSeek = "prosequor:animal-seek";
    public const string VerbMelee = "prosequor:animal-melee";
    public const string VerbBrood = "prosequor:animal-brood";
    public const string VerbMilk = "prosequor:animal-milk";
    public const string VerbPet = "prosequor:animal-pet";

    /// <summary>
    /// Passive flee reduction fraction (seed 0, <c>BaseValue</c> 1). Digging-style skill scale.
    /// </summary>
    public static float ResolveFleeChanceReduction(IPlayer player, Entity? animal) =>
        RunFloat(player, animal, VerbIds.AnimalFlee, HookIds.Chance, seed: 0f, baseValue: 1f);

    /// <summary>
    /// Response-rate / seek chance reduction (same fold as flee chance; Inconspicuity).
    /// </summary>
    public static float ResolveSeekChanceReduction(IPlayer player, Entity? animal) =>
        RunFloat(player, animal, VerbIds.AnimalSeek, HookIds.Chance, seed: 0f, baseValue: 1f);

    /// <summary>Gentle Spirit friendliness multiplier percent (seed 100 = ×1).</summary>
    public static int ResolveFleeFearMultiplierPercent(IPlayer player, Entity? animal) =>
        RunInt(player, animal, VerbIds.AnimalFlee, HookIds.Multiplier, HusbandryFriendliness.MultSeedPercent);

    /// <summary>Calming Presence friendliness multiplier percent (seed 100 = ×1).</summary>
    public static int ResolveMeleeFearMultiplierPercent(IPlayer player, Entity? animal) =>
        RunInt(player, animal, VerbIds.AnimalMelee, HookIds.Multiplier, HusbandryFriendliness.MultSeedPercent);

    /// <summary>Hen Friend friendliness multiplier percent (seed 100 = ×1).</summary>
    public static int ResolveBroodMultiplierPercent(IPlayer player, Entity? animal) =>
        RunInt(player, animal, VerbIds.AnimalBrood, HookIds.Multiplier, HusbandryFriendliness.MultSeedPercent);

    /// <summary>Warm Hands friendliness multiplier percent (seed 100 = ×1).</summary>
    public static int ResolveMilkMultiplierPercent(IPlayer player, Entity? animal) =>
        RunInt(player, animal, VerbIds.AnimalMilk, HookIds.Multiplier, HusbandryFriendliness.MultSeedPercent);

    /// <summary>
    /// Animal Whisperer: whether pet calm is allowed for this animal (seed 0; ≥1 means allow).
    /// </summary>
    public static bool ResolvePetInteractionAllowed(IPlayer player, Entity? animal) =>
        RunPetDefault(player, animal, out _).Allowed;

    /// <summary>Friendliness points to add on a successful pet (from add-friendliness side channel).</summary>
    public static int ResolvePetFriendlinessGain(IPlayer player, Entity? animal) =>
        Math.Max(0, RunPetDefault(player, animal, out _).Gain);

    static (bool Allowed, int Gain) RunPetDefault(IPlayer player, Entity? animal, out AnimalBehaviorContext? context)
    {
        context = null;
        if (player?.Entity == null)
        {
            return (false, 0);
        }

        ProsequorModSystem? mod = ProsequorModSystem.For(player.Entity.Api);
        if (mod?.Pipeline == null)
        {
            return (false, 0);
        }

        IPlayerProgress? progress = ProsequorModSystem.GetProgress(player);
        if (progress == null)
        {
            return (false, 0);
        }

        AbilityAction fact = EventFactBuilder.Build(
            VerbIds.AnimalPet.Value,
            player.PlayerUID,
            held: null,
            target: EventFactBuilder.CodeOf(animal),
            lastCraft: null,
            ground: null,
            op: null,
            tokens: null,
            damage: null,
            inputs: null,
            position: null);

        context = new AnimalBehaviorContext
        {
            Player = player,
            Progress = progress,
            Fact = fact,
            Animal = animal,
            BaseValue = 0f
        };

        int allowed = mod.Pipeline.Run(
            HookIds.EntityInteraction,
            VerbIds.AnimalPet,
            HookIds.Default,
            context,
            0);
        return (allowed >= 1, context.FriendlinessGain);
    }
    /// <summary>Inconspicuity response-rate: scale vanilla ExecutionChance (seed = chance).</summary>
    public static float ResolveResponseChance(
        IPlayer player,
        Entity? animal,
        VerbId verb,
        float chance) =>
        RunFloat(player, animal, verb, HookIds.Response, seed: chance, baseValue: chance);

    static int RunInt(IPlayer player, Entity? animal, VerbId verb, PhaseId phase, int seed) =>
        Run(player, animal, verb, phase, seed, baseValue: 0f);

    static float RunFloat(
        IPlayer player,
        Entity? animal,
        VerbId verb,
        PhaseId phase,
        float seed,
        float baseValue) =>
        Run(player, animal, verb, phase, seed, baseValue);

    static TValue Run<TValue>(
        IPlayer player,
        Entity? animal,
        VerbId verb,
        PhaseId phase,
        TValue seed,
        float baseValue)
    {
        if (player?.Entity == null)
        {
            return seed;
        }

        ProsequorModSystem? mod = ProsequorModSystem.For(player.Entity.Api);
        if (mod?.Pipeline == null)
        {
            return seed;
        }

        IPlayerProgress? progress = ProsequorModSystem.GetProgress(player);
        if (progress == null)
        {
            return seed;
        }

        AbilityAction fact = EventFactBuilder.Build(
            verb.Value,
            player.PlayerUID,
            held: null,
            target: EventFactBuilder.CodeOf(animal),
            lastCraft: null,
            ground: null,
            op: null,
            tokens: null,
            damage: null,
            inputs: null,
            position: null);

        AnimalBehaviorContext context = new()
        {
            Player = player,
            Progress = progress,
            Fact = fact,
            Animal = animal,
            BaseValue = baseValue
        };

        return mod.Pipeline.Run(HookIds.EntityInteraction, verb, phase, context, seed);
    }
}
