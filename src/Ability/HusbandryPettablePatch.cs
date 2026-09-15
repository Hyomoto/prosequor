using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace Prosequor.Ability;

/// <summary>
/// Animal Whisperer: calm low-generation pettable animals when friendliness allows,
/// and grant friendliness on a successful pet (duration ≥ 0.6s).
/// </summary>
[HarmonyPatch(typeof(EntityBehaviorPettable))]
public static class HusbandryPettablePatch
{
    const float PetSuccessSeconds = 0.6f;

    static readonly FieldInfo PetDurationField =
        AccessTools.Field(typeof(EntityBehaviorPettable), "petDurationS")
        ?? throw new InvalidOperationException("[prosequor] EntityBehaviorPettable.petDurationS missing.");

    static readonly ConditionalWeakTable<EntityBehaviorPettable, PetState> States = new();

    [HarmonyPostfix]
    [HarmonyPatch(nameof(EntityBehaviorPettable.Initialize))]
    public static void InitializePostfix(
        EntityBehaviorPettable __instance,
        EntityProperties properties,
        JsonObject attributes)
    {
        if (__instance?.entity == null)
        {
            return;
        }

        PetState state = States.GetOrCreateValue(__instance);
        state.MinGeneration = attributes?["minGeneration"]?.AsInt(1) ?? 1;

        if (state.Subscribed || __instance.entity.World?.Side != EnumAppSide.Server)
        {
            return;
        }

        EntityBehaviorTaskAI? taskAi = __instance.entity.GetBehavior<EntityBehaviorTaskAI>();
        if (taskAi?.TaskManager == null)
        {
            return;
        }

        EntityBehaviorPettable self = __instance;
        taskAi.TaskManager.OnShouldExecuteTask += _ => ShouldAllowAiTask(self);
        state.Subscribed = true;
    }

    [HarmonyPostfix]
    [HarmonyPatch(nameof(EntityBehaviorPettable.OnInteract))]
    public static void OnInteractPostfix(
        EntityBehaviorPettable __instance,
        EntityAgent byEntity,
        ItemSlot itemslot,
        Vec3d hitPosition,
        EnumInteractMode mode,
        ref EnumHandling handled)
    {
        if (__instance?.entity == null || __instance.entity.World?.Side != EnumAppSide.Server)
        {
            return;
        }

        PetState state = States.GetOrCreateValue(__instance);
        float duration = ReadPetDuration(__instance);

        if (byEntity is not EntityPlayer entityPlayer
            || entityPlayer.Player == null
            || !byEntity.Controls.RightMouseDown
            || !byEntity.RightHandItemSlot.Empty
            || byEntity.Pos.DistanceTo(__instance.entity.Pos) >= 1.2)
        {
            state.WasPastThreshold = duration >= PetSuccessSeconds;
            return;
        }

        state.LastPettingPlayer = entityPlayer.Player;
        bool past = duration >= PetSuccessSeconds;
        if (past && !state.WasPastThreshold)
        {
            OnPetSucceeded(__instance, entityPlayer.Player);
        }

        state.WasPastThreshold = past;
    }

    static void OnPetSucceeded(EntityBehaviorPettable self, IPlayer player)
    {
        int gain = AnimalBehaviorStation.ResolvePetFriendlinessGain(player, self.entity);
        if (gain > 0)
        {
            HusbandryFriendliness.Add(self.entity, player.PlayerUID, gain);
        }
    }

    /// <summary>
    /// Returns false to block new AI tasks while whispering-pet is active on a low-gen animal.
    /// High-gen animals keep vanilla's own handler; we return true so we do not fight it.
    /// </summary>
    static bool ShouldAllowAiTask(EntityBehaviorPettable self)
    {
        if (self?.entity == null)
        {
            return true;
        }

        float duration = ReadPetDuration(self);
        if (duration < PetSuccessSeconds)
        {
            return true;
        }

        PetState state = States.GetOrCreateValue(self);
        int generation = self.entity.WatchedAttributes.GetInt("generation", 0);
        if (generation >= state.MinGeneration)
        {
            return true;
        }

        IPlayer? player = state.LastPettingPlayer;
        if (player == null)
        {
            return true;
        }

        return !AnimalBehaviorStation.ResolvePetInteractionAllowed(player, self.entity);
    }

    static float ReadPetDuration(EntityBehaviorPettable self) =>
        PetDurationField.GetValue(self) is float value ? value : 0f;

    sealed class PetState
    {
        public int MinGeneration = 1;
        public IPlayer? LastPettingPlayer;
        public bool WasPastThreshold;
        public bool Subscribed;
    }
}
