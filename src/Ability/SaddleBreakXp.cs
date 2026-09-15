using Prosequor.Xp;
using Prosequor.Xp.Activity;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace Prosequor.Ability;

/// <summary>Emits riding amount XP for countable saddle breaks and final tame.</summary>
public static class SaddleBreakXp
{
    public const string VerbSaddleBreak = DeedTokenTags.SaddleBreak;
    public const string VerbSaddleTame = DeedTokenTags.SaddleTame;

    public static void AwardBreakIfProgressed(
        EntityBehaviorRideable rideable,
        int remainingBefore)
    {
        if (rideable.RemainingSaddleBreaks >= remainingBefore)
        {
            return;
        }

        Emit(rideable, DeedToken.SaddleBreak);
    }

    public static void AwardTame(EntityBehaviorRideable rideable) =>
        Emit(rideable, DeedToken.SaddleTame);

    static void Emit(EntityBehaviorRideable rideable, DeedToken token)
    {
        if (!MountedStation.TryGetDriver(rideable, out EntityAgent? driver)
            || driver is not EntityPlayer entityPlayer
            || entityPlayer.Player?.PlayerUID == null)
        {
            return;
        }

        ICoreAPI? api = entityPlayer.Api;
        if (api is not ICoreServerAPI sapi || sapi.Side != EnumAppSide.Server)
        {
            return;
        }

        Entity? mount = rideable.entity;
        string? mountCode = EventFactBuilder.CodeOf(mount);
        Deed.Emit(
            sapi,
            entityPlayer.Player.PlayerUID,
            token,
            caller: mountCode,
            target: mountCode,
            mount: mountCode,
            lastCraft: EventFactBuilder.LastCraftCode(entityPlayer.Player));
    }
}
