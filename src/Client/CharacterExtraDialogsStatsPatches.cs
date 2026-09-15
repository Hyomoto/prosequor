using HarmonyLib;
using Vintagestory.GameContent;

namespace Prosequor.Client;

/// <summary>
/// Stats panel is owned by Prosequor. Skip Essentials compose/update so its listeners
/// do not NRE on removed Physical rows or overwrite current-only vitals.
/// Compose from a postfix so later <c>ComposeExtraGuis</c> injectors still see
/// <c>playerstats</c> after Essentials' handler. Extra nutrition bars are adopted
/// after that event, not reserved per mod.
/// </summary>
[HarmonyPatch(typeof(CharacterExtraDialogs))]
static class CharacterExtraDialogsStatsPatches
{
    [HarmonyPrefix]
    [HarmonyPatch("ComposeStatsGui")]
    static bool ComposeStatsGuiPrefix() => false;

    [HarmonyPostfix]
    [HarmonyPatch("ComposeStatsGui")]
    static void ComposeStatsGuiPostfix() => CharacterStatsPanel.ComposeOwned();

    [HarmonyPrefix]
    [HarmonyPatch("UpdateStats")]
    static bool UpdateStatsPrefix() => false;

    [HarmonyPrefix]
    [HarmonyPatch("UpdateStatBars")]
    static bool UpdateStatBarsPrefix() => false;
}
