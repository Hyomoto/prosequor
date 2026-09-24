using System.Collections.Generic;
using Prosequor.Xp.Activity;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;

namespace Prosequor.Ability;

/// <summary>
/// Shared <c>fed-animal</c> deed for trough, crop, and loose-item meals.
/// Call before <see cref="HusbandryFriendliness.Add"/> so <c>friendly</c> is the pre-gain score.
/// </summary>
public static class AnimalFeedXp
{
    /// <summary>
    /// Pays <paramref name="payerUid"/> when it is a real player. Blank and <c>@</c> sentinels
    /// emit nothing. <paramref name="caller"/> must be a reserved source (<c>@trough</c>,
    /// <c>@crop</c>, <c>@loose</c>). <paramref name="foodCode"/> is the eaten collectible.
    /// </summary>
    public static void Emit(
        ICoreAPI? api,
        Entity? animal,
        string? payerUid,
        string caller,
        string? foodCode,
        BlockPos? position)
    {
        if (api == null || animal == null || !IsRealPlayer(payerUid))
        {
            return;
        }

        IReadOnlyList<Deed.QuantityUnit>? inputUnits = string.IsNullOrWhiteSpace(foodCode)
            ? null
            : [new Deed.QuantityUnit(foodCode.Trim(), 1)];

        Deed.Emit(
            api,
            playerUid: "",
            tokens: HusbandryFriendliness.WithFriendlyToken(animal, DeedTokenTags.FedAnimal),
            caller: caller,
            target: EventFactBuilder.CodeOf(animal),
            position: position,
            inputs: inputUnits,
            selectedContributorUid: payerUid!.Trim());
    }

    /// <summary>True when <paramref name="uid"/> can be a feed payee (not blank, not a sentinel).</summary>
    public static bool IsRealPlayer(string? uid) =>
        !string.IsNullOrWhiteSpace(uid) && uid.Trim()[0] != '@';
}
