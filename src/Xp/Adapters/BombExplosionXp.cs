using HarmonyLib;
using Prosequor.Ability;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace Prosequor.Xp.Adapters;

/// <summary>
/// Bomb combust deletes the bomb before <c>CreateExplosion</c>, so
/// <see cref="Block.OnBlockExploded"/> never sees a bomb in hand or at the
/// center. Stash the bomb code for the duration of that explosion.
/// </summary>
public static class BombExplosionXp
{
    [ThreadStatic]
    static Stack<string?>? callers;

    public static string? CurrentCaller
    {
        get
        {
            Stack<string?>? stack = callers;
            return stack == null || stack.Count == 0 ? null : stack.Peek();
        }
    }

    public static void Push(string? caller)
    {
        callers ??= new Stack<string?>();
        callers.Push(caller);
    }

    public static void Pop()
    {
        if (callers != null && callers.Count > 0)
        {
            callers.Pop();
        }
    }
}

/// <summary>
/// Prefix runs before <see cref="BlockEntityBomb.Combust"/> airs the bomb, so
/// <c>Block.Code</c> is still the blasting charge.
/// </summary>
[HarmonyPatch(typeof(BlockEntityBomb), nameof(BlockEntityBomb.Combust))]
public static class BlockEntityBombCombustXpPatch
{
    [HarmonyPrefix]
    public static void Prefix(BlockEntityBomb __instance) =>
        BombExplosionXp.Push(EventFactBuilder.CodeOf(__instance?.Block));

    [HarmonyFinalizer]
    public static void Finalizer() => BombExplosionXp.Pop();
}

/// <summary>
/// Explosion does not call <see cref="Block.OnBlockBroken"/>. Mine-class blocks
/// still emit the existing <c>block-broken</c> deed, with caller = the bomb.
/// </summary>
[HarmonyPatch(
    typeof(Block),
    nameof(Block.OnBlockExploded),
    [typeof(IWorldAccessor), typeof(BlockPos), typeof(BlockPos), typeof(EnumBlastType), typeof(string)])]
public static class BlockOnBlockExplodedXpPatch
{
    [HarmonyPostfix]
    public static void Postfix(
        Block __instance,
        IWorldAccessor world,
        BlockPos pos,
        string ignitedByPlayerUid)
    {
        if (world?.Side != EnumAppSide.Server || __instance == null)
        {
            return;
        }

        string? caller = BombExplosionXp.CurrentCaller;
        if (string.IsNullOrWhiteSpace(caller) || string.IsNullOrWhiteSpace(ignitedByPlayerUid))
        {
            return;
        }

        ProsequorModSystem.For(world.Api)?.NotifyBlockExplodedXp(
            ignitedByPlayerUid,
            __instance,
            pos,
            caller);
    }
}
