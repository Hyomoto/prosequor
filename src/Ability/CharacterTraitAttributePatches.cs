using System.Reflection;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using Prosequor.Data;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace Prosequor.Ability;

/// <summary>Startup class trait strip and class-profile apply (selection or mid-save join).</summary>
[HarmonyPatch]
public static class CharacterTraitAttributePatches
{
    static readonly FieldInfo ApiField = AccessTools.Field(typeof(CharacterSystem), "api");
    static readonly FieldInfo CreateCharDlgField =
        AccessTools.Field(typeof(CharacterSystem), "createCharDlg");
    static readonly MethodInfo ChangeClassMethod =
        AccessTools.Method(typeof(GuiDialogCreateCharacter), "changeClass", [typeof(int)]);

    // Per side: singleplayer runs two CharacterSystems in one process.
    static bool preparedServer;
    static bool preparedClient;

    /// <summary>
    /// True after the current side's <c>characterClasses</c> list was collapsed and mutated.
    /// Cleared when vanilla reloads that side's list from assets.
    /// </summary>
    public static bool IsPrepared(ICoreAPI? api) =>
        api != null && (api.Side == EnumAppSide.Server ? preparedServer : preparedClient);

    static void SetPrepared(ICoreAPI api, bool value)
    {
        if (api.Side == EnumAppSide.Server)
        {
            preparedServer = value;
        }
        else
        {
            preparedClient = value;
        }
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(CharacterSystem), "loadCharacterClasses")]
    public static void LoadCharacterClassesPrefix(CharacterSystem __instance)
    {
        // Vanilla replaces characterClasses; a prior strip must not block the new prepare.
        if (ApiField?.GetValue(__instance) is ICoreAPI api)
        {
            SetPrepared(api, false);
        }
    }

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

        PrepareCharacterClasses(api, mod, __instance);
        TryRepaintOpenClassTab(__instance);
    }

    /// <summary>
    /// Collapse duplicate codes, reload profiles when needed, then strip and score.
    /// Idempotent for the current list while <see cref="IsPrepared"/> stays true on that side.
    /// </summary>
    public static void PrepareCharacterClasses(
        ICoreAPI api,
        ProsequorModSystem mod,
        CharacterSystem? characterSystem = null)
    {
        if (IsPrepared(api))
        {
            return;
        }

        characterSystem ??= api.ModLoader.GetModSystem<CharacterSystem>();
        if (characterSystem == null || mod == null)
        {
            return;
        }

        if (characterSystem.characterClasses == null || characterSystem.characterClasses.Count == 0)
        {
            return;
        }

        if (mod.TraitAttributes.ByCode.Count == 0)
        {
            mod.TraitAttributes.LoadFromAssets(api, mod.AttributeStats);
        }

        // Client AssetsFinalize can run before server assets exist; class load is the real list.
        mod.TraitAttributes.LoadClassProfiles(api, mod.AttributeStats, mod.Registry);

        CollapseDuplicateClasses(characterSystem, api, api.Logger);
        MutateAndRebuildSkillSets(api, mod, characterSystem);
        SetPrepared(api, true);
    }

    /// <summary>
    /// Keep one <see cref="CharacterClass"/> per code. Winner is the last enabled asset
    /// (domain, then path). Falls back to the last list entry when assets cannot be lined up.
    /// </summary>
    public static int CollapseDuplicateClasses(
        CharacterSystem characterSystem,
        ICoreAPI? api = null,
        ILogger? logger = null)
    {
        if (characterSystem?.characterClasses == null || characterSystem.characterClasses.Count == 0)
        {
            return 0;
        }

        List<CharacterClass> list = characterSystem.characterClasses;
        Dictionary<string, int>? winnerIndexByCode = TryResolveWinnersFromAssets(
            api,
            list.Count,
            logger,
            out Dictionary<string, AssetLocation>? winnerSources);
        Dictionary<string, int> counts = new(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, int> lastIndexByCode = new(StringComparer.OrdinalIgnoreCase);

        for (int i = 0; i < list.Count; i++)
        {
            CharacterClass? cls = list[i];
            if (cls == null || string.IsNullOrWhiteSpace(cls.Code))
            {
                continue;
            }

            string code = cls.Code.Trim();
            counts.TryGetValue(code, out int count);
            counts[code] = count + 1;
            lastIndexByCode[code] = i;
        }

        Dictionary<string, int> keepIndexByCode = new(StringComparer.OrdinalIgnoreCase);
        foreach (KeyValuePair<string, int> pair in lastIndexByCode)
        {
            if (winnerIndexByCode != null
                && winnerIndexByCode.TryGetValue(pair.Key, out int assetWinner)
                && assetWinner >= 0
                && assetWinner < list.Count
                && list[assetWinner] != null
                && string.Equals(list[assetWinner].Code, pair.Key, StringComparison.OrdinalIgnoreCase))
            {
                keepIndexByCode[pair.Key] = assetWinner;
            }
            else
            {
                keepIndexByCode[pair.Key] = pair.Value;
            }
        }

        List<CharacterClass> kept = new(list.Count);
        HashSet<int> keepIndices = new(keepIndexByCode.Values);
        for (int i = 0; i < list.Count; i++)
        {
            if (keepIndices.Contains(i))
            {
                kept.Add(list[i]);
            }
        }

        int removed = list.Count - kept.Count;
        if (removed > 0)
        {
            characterSystem.characterClasses = kept;
        }

        if (characterSystem.characterClassesByCode != null)
        {
            foreach (KeyValuePair<string, int> pair in keepIndexByCode)
            {
                CharacterClass winner = list[pair.Value];
                characterSystem.characterClassesByCode[pair.Key] = winner;
            }
        }

        foreach (KeyValuePair<string, int> pair in counts)
        {
            if (pair.Value < 2)
            {
                continue;
            }

            string source = winnerSources != null
                && winnerSources.TryGetValue(pair.Key, out AssetLocation? asset)
                && asset != null
                    ? asset.ToString()
                    : "last list entry";
            logger?.Warning(
                "[prosequor] Class '{0}' defined {1} times; keeping the last ({2}).",
                pair.Key,
                pair.Value,
                source);
        }

        return removed;
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
                mod.AttributeStats,
                api.Logger);
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

    static void TryRepaintOpenClassTab(CharacterSystem characterSystem)
    {
        if (CreateCharDlgField?.GetValue(characterSystem) is not GuiDialogCreateCharacter dialog
            || ChangeClassMethod == null
            || !dialog.IsOpened())
        {
            return;
        }

        Traverse t = Traverse.Create(dialog);
        if (t.Field<int>("curTab").Value != 1)
        {
            return;
        }

        if (!dialog.Composers.ContainsKey("createcharacter"))
        {
            return;
        }

        ChangeClassMethod.Invoke(dialog, [0]);
    }

    /// <summary>
    /// Map class code → live list index of the last enabled asset (domain, then path).
    /// Null when the asset walk does not line up with the live list length.
    /// </summary>
    static Dictionary<string, int>? TryResolveWinnersFromAssets(
        ICoreAPI? api,
        int liveCount,
        ILogger? logger,
        out Dictionary<string, AssetLocation>? winnerSources)
    {
        winnerSources = null;
        if (api?.Assets == null || liveCount <= 0)
        {
            return null;
        }

        List<(AssetLocation Source, string Code, int ListIndex)> appearances = new();
        int listIndex = 0;
        foreach (KeyValuePair<AssetLocation, JToken> kv in api.Assets
            .GetMany<JToken>(api.Logger, "config/characterclasses", null))
        {
            if (!TryEnumerateEnabledCodes(kv.Value, kv.Key, logger, out List<string> codes))
            {
                continue;
            }

            foreach (string code in codes)
            {
                appearances.Add((kv.Key, code, listIndex));
                listIndex++;
            }
        }

        if (appearances.Count != liveCount)
        {
            logger?.Warning(
                "[prosequor] Character class asset walk ({0}) does not match live list ({1}); using last list entry per code.",
                appearances.Count,
                liveCount);
            return null;
        }

        List<(AssetLocation Source, string Code, int ListIndex)> ordered = appearances
            .OrderBy(a => a.Source.Domain, StringComparer.OrdinalIgnoreCase)
            .ThenBy(a => a.Source.Path, StringComparer.OrdinalIgnoreCase)
            .ThenBy(a => a.ListIndex)
            .ToList();

        Dictionary<string, int> winners = new(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, AssetLocation> sources = new(StringComparer.OrdinalIgnoreCase);
        foreach ((AssetLocation source, string code, int index) in ordered)
        {
            winners[code] = index;
            sources[code] = source;
        }

        winnerSources = sources;
        return winners;
    }

    static bool TryEnumerateEnabledCodes(
        JToken? token,
        AssetLocation source,
        ILogger? logger,
        out List<string> codes)
    {
        codes = new List<string>();
        if (token is JObject single)
        {
            if (TryReadEnabledCode(single, out string? code) && code != null)
            {
                codes.Add(code);
            }

            return true;
        }

        if (token is not JArray list)
        {
            logger?.Warning(
                "[prosequor] Skipping character classes in {0}: expected an object or array.",
                source);
            return false;
        }

        foreach (JToken entry in list)
        {
            if (entry is not JObject classObject)
            {
                continue;
            }

            if (TryReadEnabledCode(classObject, out string? code) && code != null)
            {
                codes.Add(code);
            }
        }

        return true;
    }

    static bool TryReadEnabledCode(JObject classObject, out string? code)
    {
        code = null;
        if (classObject.Value<bool?>("enabled") == false)
        {
            return false;
        }

        string? raw = classObject.Value<string>("code");
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        code = raw.Trim();
        return true;
    }
}
