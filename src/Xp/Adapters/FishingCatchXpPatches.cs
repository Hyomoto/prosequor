using HarmonyLib;
using Prosequor.Ability;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.GameContent;

namespace Prosequor.Xp.Adapters;

/// <summary>
/// Scopes catch XP to an active <see cref="EntityBobber.TryCatchFish"/> call so junk/other gives are ignored.
/// </summary>
public static class FishingCatchScope
{
    [ThreadStatic]
    static IPlayer? activeCatcher;

    [ThreadStatic]
    static bool hadCatchGive;

    public static IPlayer? CurrentCatcher => activeCatcher;

    /// <summary>True when at least one catch stack was given during the active <see cref="EntityBobber.TryCatchFish"/> call.</summary>
    public static bool HadCatchGive => hadCatchGive;

    public static void Begin(IPlayer? catcher)
    {
        activeCatcher = catcher;
        hadCatchGive = false;
    }

    public static void NoteCatchGive() => hadCatchGive = true;

    public static void End()
    {
        activeCatcher = null;
        hadCatchGive = false;
    }
}

[HarmonyPatch(typeof(EntityBobber), nameof(EntityBobber.TryCatchFish))]
public static class EntityBobberTryCatchFishXpPatch
{
    [HarmonyPrefix]
    public static void Prefix(EntityAgent entityCatcher)
    {
        IPlayer? player = (entityCatcher as EntityPlayer)?.Player;
        if (entityCatcher?.World?.Side == EnumAppSide.Server && player != null)
        {
            FishingCatchScope.Begin(player);
            FishingCatchDrops.BeginCatch();
        }
    }

    [HarmonyPostfix]
    public static void Postfix() => FishingCatchScope.End();

    [HarmonyFinalizer]
    public static void Finalizer() => FishingCatchScope.End();
}

/// <summary>
/// Players override <see cref="EntityPlayer.TryGiveItemStack"/> and never call
/// <see cref="EntityAgent.TryGiveItemStack"/>, so the catch hook must target <see cref="EntityPlayer"/>.
/// </summary>
[HarmonyPatch(typeof(EntityPlayer), nameof(EntityPlayer.TryGiveItemStack))]
public static class EntityPlayerTryGiveItemStackFishXpPatch
{
    [HarmonyPrefix]
    public static void Prefix(EntityPlayer __instance, ItemStack itemstack)
    {
        IPlayer? catcher = FishingCatchScope.CurrentCatcher;
        if (catcher == null || itemstack == null)
        {
            return;
        }

        if (__instance?.World?.Side != EnumAppSide.Server)
        {
            return;
        }

        FishingCatchScope.NoteCatchGive();
        ProsequorModSystem.For(__instance.World.Api)?.NotifyFishCatchDrops(catcher, itemstack);
    }

    [HarmonyPostfix]
    public static void Postfix(EntityPlayer __instance, ItemStack itemstack)
    {
        IPlayer? catcher = FishingCatchScope.CurrentCatcher;
        if (catcher == null || itemstack == null)
        {
            return;
        }

        if (__instance?.World?.Side != EnumAppSide.Server)
        {
            return;
        }

        FishingCatchDrops.FlushExtraStacks(__instance, catcher);
        ProsequorModSystem.For(__instance.World.Api)?.NotifyFishCatchXp(catcher, itemstack);
    }
}
