using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Prosequor.Xp.Activity;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.GameContent;

namespace Prosequor.Ability;

/// <summary>
/// Scales milking rejection (<c>aggroChance</c>) by friendliness × Warm Hands mult,
/// and emits <c>milked</c> (± <c>friendly</c>) on successful complete.
/// </summary>
[HarmonyPatch(typeof(EntityBehaviorMilkable))]
public static class HusbandryMilkingFriendlinessPatch
{
    static readonly FieldInfo AggroChanceField =
        AccessTools.Field(typeof(EntityBehaviorMilkable), "aggroChance");
    static readonly FieldInfo AggroTestedField =
        AccessTools.Field(typeof(EntityBehaviorMilkable), "aggroTested");

    static readonly ConditionalWeakTable<EntityBehaviorMilkable, StrongBox<float>> VanillaAggro = new();

    [HarmonyPostfix]
    [HarmonyPatch(nameof(EntityBehaviorMilkable.TryBeginMilking))]
    public static void TryBeginMilkingPostfix(EntityBehaviorMilkable __instance, bool __result)
    {
        if (!__result || __instance == null || AggroChanceField.GetValue(__instance) is not float aggro)
        {
            return;
        }

        VanillaAggro.GetOrCreateValue(__instance).Value = aggro;
    }

    [HarmonyPrefix]
    [HarmonyPatch(nameof(EntityBehaviorMilkable.CanContinueMilking))]
    public static void CanContinueMilkingPrefix(
        EntityBehaviorMilkable __instance,
        IPlayer milkingPlayer,
        float secondsUsed)
    {
        if (milkingPlayer == null || __instance?.entity == null)
        {
            return;
        }

        if (secondsUsed <= 1f)
        {
            return;
        }

        if (AggroTestedField.GetValue(__instance) is true)
        {
            return;
        }

        if (!VanillaAggro.TryGetValue(__instance, out StrongBox<float>? box))
        {
            return;
        }

        float mult = HusbandryFriendliness.MultFromPercent(
            AnimalBehaviorStation.ResolveMilkMultiplierPercent(milkingPlayer, __instance.entity));
        float scaled = HusbandryFriendliness.ScaleMilkingAggroChance(
            box.Value,
            HusbandryFriendliness.Get(__instance.entity),
            mult);
        AggroChanceField.SetValue(__instance, scaled);
    }

    [HarmonyPostfix]
    [HarmonyPatch(nameof(EntityBehaviorMilkable.MilkingComplete))]
    public static void MilkingCompletePostfix(
        EntityBehaviorMilkable __instance,
        EntityAgent byEntity)
    {
        Entity? animal = __instance?.entity;
        if (animal?.World == null || animal.World.Side != EnumAppSide.Server)
        {
            return;
        }

        if (byEntity is not EntityPlayer eplr || eplr.Player == null
            || string.IsNullOrEmpty(eplr.Player.PlayerUID))
        {
            return;
        }

        string[] tokens = HusbandryFriendliness.WithFriendlyToken(
            animal,
            DeedTokenTags.Milked);

        Deed.Emit(
            animal.Api,
            playerUid: eplr.Player.PlayerUID,
            tokens: tokens,
            caller: CallerIdentities.Hand,
            target: EventFactBuilder.CodeOf(animal),
            position: animal.Pos.AsBlockPos.Copy());
    }
}
