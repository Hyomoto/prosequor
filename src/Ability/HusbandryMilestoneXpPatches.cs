using HarmonyLib;
using Prosequor.Xp.Activity;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.GameContent;

namespace Prosequor.Ability;

/// <summary>
/// Friendly animal age-up / birth → contributors XP, then wipe care weights.
/// Pedigree copy on grow stays in <see cref="ProsequorEntityPedigreePatches"/>.
/// </summary>
public static class HusbandryMilestoneXpPatches
{
    /// <summary>
    /// After child→adult pedigree copy, settle on the adult when friendly.
    /// </summary>
    [HarmonyPatch(typeof(EntityBehaviorGrow), "BecomeAdult")]
    public static class GrowBecomeAdultXpPatch
    {
        [HarmonyPostfix]
        public static void Postfix(Entity adult)
        {
            HusbandryContributorXp.TrySettleAndWipe(adult, DeedTokenTags.AgedUp);
        }
    }

    /// <summary>One settle per birth event on the mother (not per offspring).</summary>
    [HarmonyPatch(typeof(EntityBehaviorMultiply), "GiveBirth")]
    public static class MultiplyGiveBirthXpPatch
    {
        [HarmonyPostfix]
        public static void Postfix(EntityBehaviorMultiply __instance)
        {
            HusbandryContributorXp.TrySettleAndWipe(__instance?.entity, DeedTokenTags.GaveBirth);
        }
    }
}
