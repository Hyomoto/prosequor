using HarmonyLib;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace Prosequor.Ability;

/// <summary>
/// Opens <see cref="TreeFellScope"/> for the break that consumes <c>FindTree</c>.
/// Chop-time <c>FindTree</c> calls stay outside the scope.
/// </summary>
[HarmonyPatch(typeof(ItemAxe), nameof(ItemAxe.OnBlockBrokenWith))]
public static class ItemAxeTreeFellScopePatch
{
    [HarmonyPrefix]
    public static void Prefix() => TreeFellScope.Begin();

    [HarmonyFinalizer]
    public static void Finalizer() => TreeFellScope.End();
}

/// <summary>Copies the fell walk into the open scope. The stack itself is left intact.</summary>
[HarmonyPatch(typeof(ItemAxe), nameof(ItemAxe.FindTree))]
public static class ItemAxeFindTreeFellPatch
{
    [HarmonyPostfix]
    public static void Postfix(Stack<BlockPos>? __result) => TreeFellScope.Note(__result);
}
