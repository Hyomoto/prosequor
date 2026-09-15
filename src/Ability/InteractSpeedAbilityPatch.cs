using System.Reflection;
using HarmonyLib;
using Prosequor.Ability.Hooks;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;

namespace Prosequor.Ability;

/// <summary>
/// Held-interact egress into interaction-speed via OnHeldUse* (non-overridden entry points
/// that virtually dispatch to OnHeldInteractStep/Stop — covers BlockPan and other overrides).
/// </summary>
[HarmonyPatch(typeof(CollectibleObject), nameof(CollectibleObject.OnHeldUseStep))]
public static class CollectibleHeldUseStepSpeedPatch
{
    [HarmonyPrefix]
    public static void Prefix(
        CollectibleObject __instance,
        ref float secondsPassed,
        ItemSlot slot,
        EntityAgent byEntity,
        BlockSelection blockSel)
    {
        if (byEntity.Controls.HandUse != EnumHandInteract.HeldItemInteract)
        {
            return;
        }

        Apply(__instance, ref secondsPassed, byEntity, blockSel);
    }

    internal static void Apply(
        CollectibleObject collectible,
        ref float seconds,
        EntityAgent byEntity,
        BlockSelection? blockSel)
    {
        if (byEntity is not EntityPlayer entityPlayer || entityPlayer.Player == null)
        {
            return;
        }

        Block? target = blockSel?.Position == null
            ? null
            : byEntity.World.BlockAccessor.GetBlock(blockSel.Position);

        float factor = InteractionSpeedStation.Run(
            entityPlayer.Player,
            EventFactBuilder.CodeOf(target),
            EventFactBuilder.CodeOf(collectible),
            1f,
            target?.BlockMaterial,
            HookIds.ItemInteraction);
        if (Math.Abs(factor - 1f) > 0.0001f)
        {
            seconds *= factor;
        }
    }
}

[HarmonyPatch(typeof(CollectibleObject), nameof(CollectibleObject.OnHeldUseStop))]
public static class CollectibleHeldUseStopSpeedPatch
{
    [HarmonyPrefix]
    public static void Prefix(
        CollectibleObject __instance,
        ref float secondsPassed,
        ItemSlot slot,
        EntityAgent byEntity,
        BlockSelection blockSel,
        EntitySelection entitySel,
        EnumHandInteract useType)
    {
        if (useType != EnumHandInteract.HeldItemInteract)
        {
            return;
        }

        CollectibleHeldUseStepSpeedPatch.Apply(__instance, ref secondsPassed, byEntity, blockSel);
    }
}

[HarmonyPatch(typeof(CollectibleObject), nameof(CollectibleObject.OnHeldUseCancel))]
public static class CollectibleHeldUseCancelSpeedPatch
{
    [HarmonyPrefix]
    public static void Prefix(
        CollectibleObject __instance,
        ref float secondsPassed,
        ItemSlot slot,
        EntityAgent byEntity,
        BlockSelection blockSel)
    {
        if (byEntity.Controls.HandUse != EnumHandInteract.HeldItemInteract)
        {
            return;
        }

        CollectibleHeldUseStepSpeedPatch.Apply(__instance, ref secondsPassed, byEntity, blockSel);
    }
}

/// <summary>
/// Block-interact egress into interaction-speed. Base Block methods cover non-overrides;
/// <see cref="PatchDeclaredOverrides"/> also patches declaring subtypes (e.g. IW chopping/sawhorse)
/// and the server callOnUsingBlock funnel when available.
/// </summary>
[HarmonyPatch(typeof(Block), nameof(Block.OnBlockInteractStep))]
public static class BlockInteractSpeedPatch
{
    static int overridePatchGen;

    internal static void ResetOverridePatchGate() => Interlocked.Exchange(ref overridePatchGen, 0);

    [HarmonyPrefix]
    public static void Prefix(
        Block __instance,
        ref float secondsUsed,
        IWorldAccessor world,
        IPlayer byPlayer,
        BlockSelection blockSel)
    {
        Apply(__instance, ref secondsUsed, byPlayer);
    }

    internal static void Apply(Block block, ref float secondsUsed, IPlayer? byPlayer)
    {
        if (byPlayer == null || block == null)
        {
            return;
        }

        CollectibleObject? tool = byPlayer.InventoryManager?.ActiveHotbarSlot?.Itemstack?.Collectible;
        float factor = InteractionSpeedStation.Run(
            byPlayer,
            EventFactBuilder.CodeOf(block),
            EventFactBuilder.CodeOf(tool),
            1f,
            block.BlockMaterial);
        if (Math.Abs(factor - 1f) > 0.0001f)
        {
            secondsUsed *= factor;
        }
    }

    /// <summary>
    /// Late-bind Block subtype overrides after other mods have loaded types
    /// (e.g. Immersive Woodworking chopping block / sawhorse).
    /// </summary>
    public static void PatchDeclaredOverrides(Harmony harmony)
    {
        // AssetsFinalize runs per side in SP; only scan once per process.
        if (Interlocked.Exchange(ref overridePatchGen, 1) != 0)
        {
            return;
        }

        foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            Type[] types;
            try
            {
                types = assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException ex)
            {
                types = ex.Types.Where(t => t != null).Cast<Type>().ToArray();
            }
            catch
            {
                continue;
            }

            foreach (Type type in types)
            {
                if (type == null || type == typeof(Block) || !typeof(Block).IsAssignableFrom(type))
                {
                    continue;
                }

                TryPatchDeclared(
                    harmony,
                    type,
                    nameof(Block.OnBlockInteractStep),
                    new HarmonyMethod(typeof(BlockInteractSpeedPatch), nameof(Prefix)));
                TryPatchDeclared(
                    harmony,
                    type,
                    nameof(Block.OnBlockInteractStop),
                    new HarmonyMethod(typeof(BlockInteractStopSpeedPatch), nameof(BlockInteractStopSpeedPatch.Prefix)));
            }
        }
    }

    static void TryPatchDeclared(Harmony harmony, Type type, string methodName, HarmonyMethod prefix)
    {
        MethodInfo? method = AccessTools.DeclaredMethod(type, methodName);
        if (method == null || method.IsAbstract)
        {
            return;
        }

        try
        {
            harmony.Patch(method, prefix: prefix);
        }
        catch
        {
            // Already patched or incompatible signature — leave base/other patches alone.
        }
    }
}

[HarmonyPatch(typeof(Block), nameof(Block.OnBlockInteractStop))]
public static class BlockInteractStopSpeedPatch
{
    [HarmonyPrefix]
    public static void Prefix(
        Block __instance,
        ref float secondsUsed,
        IWorldAccessor world,
        IPlayer byPlayer,
        BlockSelection blockSel)
    {
        BlockInteractSpeedPatch.Apply(__instance, ref secondsUsed, byPlayer);
    }
}
