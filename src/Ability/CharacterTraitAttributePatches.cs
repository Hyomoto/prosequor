using System.Reflection;
using HarmonyLib;
using Prosequor.Data;
using Vintagestory.API.Common;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace Prosequor.Ability;

/// <summary>Startup class trait strip and class-profile apply (selection or mid-save join).</summary>
[HarmonyPatch]
public static class CharacterTraitAttributePatches
{
    static readonly FieldInfo ApiField = AccessTools.Field(typeof(CharacterSystem), "api");

    [HarmonyPostfix]
    [HarmonyPatch(typeof(CharacterSystem), "loadCharacterClasses")]
    public static void LoadCharacterClassesPostfix(CharacterSystem __instance)
    {
        if (ApiField?.GetValue(__instance) is not ICoreAPI api)
        {
            return;
        }

        ProsequorModSystem? mod = ProsequorModSystem.For(api);
        if (mod == null)
        {
            return;
        }

        if (mod.TraitAttributes.ByCode.Count == 0)
        {
            mod.TraitAttributes.LoadFromAssets(api, mod.AttributeStats);
        }

        MutateAndRebuildSkillSets(api, mod, __instance);
    }

    /// <summary>
    /// Strip mapped traits and rebuild class skill sets. Safe to call again after
    /// <c>AssetsFinalize</c> when character classes loaded before attribute stats.
    /// </summary>
    public static void MutateAndRebuildSkillSets(
        ICoreAPI api,
        ProsequorModSystem mod,
        CharacterSystem? characterSystem = null)
    {
        characterSystem ??= api.ModLoader.GetModSystem<CharacterSystem>();
        if (characterSystem == null || mod == null)
        {
            return;
        }

        (int classesMutated, int traitsStripped, int retainCleared) =
            TraitAttributeConverter.MutateLoadedClasses(
                characterSystem,
                mod.TraitAttributes,
                mod.AttributeStats);
        if (mod.Registry.All.Count > 0)
        {
            mod.TraitAttributes.RebuildClassSkillSets(mod.Registry, api);
        }

        api.Logger.Notification(
            "[prosequor] Stripped {0} mapped trait(s) from {1} class(es); cleared Entity.Stats on {2} retainTrait mapping(s).",
            traitsStripped,
            classesMutated,
            retainCleared);
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(CharacterSystem), "onCharacterSelection")]
    public static void OnCharacterSelectionPostfix(
        CharacterSystem __instance,
        IServerPlayer fromPlayer,
        CharacterSelectionPacket p)
    {
        if (p == null || fromPlayer?.Entity?.Api == null)
        {
            return;
        }

        // Existing / mid-save characters skip the create-character packet (DidSelect=false)
        // but already have a class. Still apply the cached starting profile.
        if (!p.DidSelect)
        {
            string? existing = fromPlayer.Entity.WatchedAttributes.GetString("characterClass");
            if (string.IsNullOrWhiteSpace(existing))
            {
                return;
            }
        }

        ProsequorModSystem? mod = ProsequorModSystem.For(fromPlayer.Entity.Api);
        if (mod?.TraitAttributes == null)
        {
            return;
        }

        TraitAttributeConverter.TryApplyOnSelection(
            fromPlayer,
            __instance,
            mod.TraitAttributes,
            mod.Registry);
    }
}
