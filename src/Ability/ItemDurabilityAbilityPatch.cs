using HarmonyLib;
using Prosequor.Ability.Hooks;
using Prosequor.Player;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;

namespace Prosequor.Ability;

/// <summary>
/// Captures block-break provenance while vanilla calls into DamageItem.
/// DamageItem itself does not expose a damage-source argument.
/// </summary>
[HarmonyPatch(typeof(CollectibleObject), nameof(CollectibleObject.OnBlockBrokenWith))]
public static class BlockBreakDamageScopePatch
{
    [ThreadStatic]
    static Stack<BlockBreakDamageScope>? scopes;

    internal static BlockBreakDamageScope? Current =>
        scopes is { Count: > 0 } ? scopes.Peek() : null;

    [HarmonyPrefix]
    public static void Prefix(
        IWorldAccessor world,
        BlockSelection blockSel,
        out BlockBreakDamageScope? __state)
    {
        Block? block = blockSel?.Block
            ?? (blockSel == null ? null : world.BlockAccessor.GetBlock(blockSel.Position));
        __state = blockSel == null
            ? null
            : new BlockBreakDamageScope(block, blockSel.Position.Copy());

        if (__state != null)
        {
            (scopes ??= new Stack<BlockBreakDamageScope>()).Push(__state);
        }
    }

    [HarmonyFinalizer]
    public static Exception? Finalizer(Exception? __exception, BlockBreakDamageScope? __state)
    {
        if (__state != null && scopes is { Count: > 0 })
        {
            scopes.Pop();
        }

        return __exception;
    }
}

public sealed record BlockBreakDamageScope(Block? Block, BlockPos Pos);

/// <summary>
/// Thin item-interaction durability adapter. Rules transform the pending amount before
/// vanilla collectible behaviors and the durability subtraction run.
/// </summary>
[HarmonyPatch(typeof(CollectibleObject), nameof(CollectibleObject.DamageItem))]
public static class ItemDurabilityAbilityPatch
{
    [HarmonyPrefix]
    public static void Prefix(
        CollectibleObject __instance,
        IWorldAccessor world,
        Entity byEntity,
        ItemSlot itemSlot,
        ref int amount)
    {
        if (amount <= 0
            || world?.Side != EnumAppSide.Server
            || byEntity is not EntityPlayer entityPlayer
            || itemSlot?.Itemstack == null)
        {
            return;
        }

        IPlayer? player = entityPlayer.Player;
        ProsequorModSystem? mod = ProsequorModSystem.For(world.Api);
        IPlayerProgress? progress = player == null ? null : ProsequorModSystem.GetProgress(player);
        if (player == null || progress == null || mod?.Pipeline == null)
        {
            return;
        }

        BlockBreakDamageScope? scope = BlockBreakDamageScopePatch.Current;
        VerbId verb = ItemDurabilityStation.ResolveVerb(
            CraftConsumeScope.IsActive,
            scope != null);

        AbilityAction fact = EventFactBuilder.ForPlayer(
            player,
            verb.Value,
            target: EventFactBuilder.CodeOf(scope?.Block),
            held: EventFactBuilder.CodeOf(__instance),
            position: scope?.Pos.Copy());

        ItemDurabilityContext context = new()
        {
            World = world,
            ByEntity = byEntity,
            ItemSlot = itemSlot,
            Collectible = __instance,
            Player = player,
            Progress = progress,
            Fact = fact,
            BrokenBlock = scope?.Block,
            BlockPos = scope?.Pos.Copy()
        };

        amount = ItemDurabilityStation.RunAmount(context, verb, amount);
    }
}
