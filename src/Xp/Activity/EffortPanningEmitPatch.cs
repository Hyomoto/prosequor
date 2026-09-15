using System.Collections.Concurrent;
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.GameContent;

namespace Prosequor.Xp.Activity;

/// <summary>
/// Harmony probe on server panning ticks — emits interacting + sifted material target.
/// </summary>
[HarmonyPatch(typeof(BlockPan), nameof(BlockPan.OnHeldInteractStep))]
public static class EffortPanningEmitPatch
{
    [HarmonyPostfix]
    public static void Postfix(BlockPan __instance, ItemSlot slot, EntityAgent byEntity)
    {
        if (byEntity.World.Side != EnumAppSide.Server)
        {
            return;
        }

        if (byEntity is not EntityPlayer entityPlayer || entityPlayer.Player == null)
        {
            return;
        }

        ItemStack? stack = slot?.Itemstack;
        string? materialCode = stack != null ? __instance.GetBlockMaterialCode(stack) : null;
        if (materialCode == null || stack == null || !stack.TempAttributes.GetBool("canpan"))
        {
            return;
        }

        Effort.Emit(
            entityPlayer.Player,
            EffortToken.Interacting,
            target: materialCode,
            channel: EffortTokenTags.Interacting);
    }
}
