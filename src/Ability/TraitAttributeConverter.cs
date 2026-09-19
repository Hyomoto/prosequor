using Prosequor.Data;
using Prosequor.Player;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace Prosequor.Ability;

/// <summary>
/// Infers starting attribute scores from trait codes; strips mapped traits from classes at load.
/// Class score cache lives on <see cref="ITraitAttributeRegistry"/> (per client/server mod system).
/// </summary>
public static class TraitAttributeConverter
{
    public const string AppliedModDataKey = "prosequorTraitAttributesApplied";
    public const string CreateCharacterModDataKey = "createCharacter";
    public const string FoldedExtrasModDataKey = "prosequorTraitExtrasFolded";

    /// <summary>
    /// Base 10 + summed deltas for known mappings; clamp 0..18. Unknown trait codes contribute nothing.
    /// </summary>
    public static Dictionary<string, int> ResolveScores(
        ITraitAttributeRegistry registry,
        IEnumerable<string>? traitCodes)
    {
        Dictionary<string, int> scores = NewBaseScores();
        ApplyDeltas(scores, registry, traitCodes);
        ClampScores(scores);
        return scores;
    }

    /// <summary>Copy class scores and fold extra-trait deltas (Player Model Lib extras, extraTraits).</summary>
    public static Dictionary<string, int> WithExtraTraits(
        IReadOnlyDictionary<string, int> classScores,
        ITraitAttributeRegistry registry,
        IEnumerable<string>? extraTraits)
    {
        Dictionary<string, int> scores = NewBaseScores();
        if (classScores != null)
        {
            foreach (KeyValuePair<string, int> pair in classScores)
            {
                scores[pair.Key] = pair.Value;
            }
        }

        ApplyDeltas(scores, registry, extraTraits);
        ClampScores(scores);
        return scores;
    }

    /// <summary>
    /// Cache starting scores from each class's original traits, strip mapped traits from
    /// <c>CharacterClass.Traits</c>, and clear Entity.Stats only on <c>retainTrait</c> mappings.
    /// </summary>
    public static (int ClassesMutated, int TraitsStripped, int RetainStatsCleared) MutateLoadedClasses(
        CharacterSystem characterSystem,
        ITraitAttributeRegistry registry)
    {
        if (characterSystem?.characterClasses == null || registry == null)
        {
            return (0, 0, 0);
        }

        registry.ClearClassStartingScores();

        int classesMutated = 0;
        int traitsStripped = 0;
        int retainStatsCleared = 0;

        foreach (CharacterClass characterClass in characterSystem.characterClasses)
        {
            if (characterClass == null || string.IsNullOrWhiteSpace(characterClass.Code))
            {
                continue;
            }

            string[] original = characterClass.Traits ?? Array.Empty<string>();
            registry.SetClassStartingScores(characterClass.Code, ResolveScores(registry, original));

            List<string> kept = new(original.Length);
            int strippedHere = 0;
            foreach (string raw in original)
            {
                if (string.IsNullOrWhiteSpace(raw))
                {
                    continue;
                }

                string code = raw.Trim();
                if (registry.TryGet(code, out TraitAttributeMapping mapping) && mapping.ShouldStripFromClass)
                {
                    strippedHere++;
                    continue;
                }

                kept.Add(code);
            }

            if (strippedHere > 0 || original.Length != kept.Count)
            {
                characterClass.Traits = kept.ToArray();
                classesMutated++;
                traitsStripped += strippedHere;
            }
        }

        if (characterSystem.TraitsByCode != null)
        {
            foreach (KeyValuePair<string, Trait> kv in characterSystem.TraitsByCode)
            {
                Trait trait = kv.Value;
                if (trait == null || string.IsNullOrWhiteSpace(trait.Code))
                {
                    continue;
                }

                if (!registry.TryGet(trait.Code, out TraitAttributeMapping mapping)
                    || !mapping.RetainTrait
                    || !mapping.HasScoreDeltas)
                {
                    continue;
                }

                if (trait.Attributes == null || trait.Attributes.Count == 0)
                {
                    continue;
                }

                trait.Attributes = new Dictionary<string, double>();
                retainStatsCleared++;
            }
        }

        return (classesMutated, traitsStripped, retainStatsCleared);
    }

    /// <summary>
    /// Once per character: set attributes from cached class scores, then fold extraTraits deltas.
    /// Used on first confirmed selection and again for mid-save joins that already have a class
    /// (those players never send <c>DidSelect</c>).
    /// Vanilla and Player Model Lib both assign a default class before the dialog confirms, so
    /// skip until <c>createCharacter</c> is true.
    /// </summary>
    public static void TryApplyOnSelection(
        IServerPlayer player,
        CharacterSystem characterSystem,
        ITraitAttributeRegistry registry,
        ISkillRegistry skills)
    {
        if (player?.Entity == null || characterSystem == null || registry == null)
        {
            return;
        }

        if (player.GetModData(AppliedModDataKey, false))
        {
            return;
        }

        if (!player.GetModData(CreateCharacterModDataKey, false))
        {
            return;
        }

        EntityPlayer entityPlayer = player.Entity;
        // Entity.GetBehavior NREs when SidedProperties is null (Properties / Server unset).
        if (entityPlayer.SidedProperties?.Behaviors == null)
        {
            return;
        }

        string? classCode = entityPlayer.WatchedAttributes.GetString("characterClass");
        if (string.IsNullOrWhiteSpace(classCode))
        {
            return;
        }

        Dictionary<string, int> scores = NewBaseScores();
        if (registry.ClassStartingScores.TryGetValue(classCode, out Dictionary<string, int>? cached)
            && cached != null)
        {
            foreach (KeyValuePair<string, int> pair in cached)
            {
                scores[pair.Key] = pair.Value;
            }
        }
        else if (characterSystem.characterClassesByCode.TryGetValue(classCode, out CharacterClass? characterClass)
            && characterClass != null)
        {
            // Cache miss: leftover traits only (stripped list) — prefer warm cache from mutate.
            scores = ResolveScores(registry, characterClass.Traits);
        }

        string[]? extra = entityPlayer.WatchedAttributes.GetStringArray("extraTraits");
        if (extra != null && extra.Length > 0)
        {
            ApplyDeltas(scores, registry, extra);
            ClampScores(scores);
        }

        EntityBehaviorProgress? progress = entityPlayer.GetBehavior<EntityBehaviorProgress>();
        if (progress == null)
        {
            return;
        }

        progress.EnsureLoaded(player, skills);
        foreach (string id in AttributeIds.All)
        {
            progress.SetAttribute(id, scores[id]);
        }

        player.SetModData(AppliedModDataKey, true);
        RememberFoldedExtras(player, extra);
    }

    /// <summary>
    /// Player Model Lib writes model <c>ExtraTraits</c> onto <c>extraTraits</c> after the
    /// class-selection packet. Fold any codes that were not part of the first apply.
    /// Does not reset grown scores.
    /// </summary>
    public static void FoldNewExtraTraits(
        IServerPlayer player,
        ITraitAttributeRegistry registry,
        ISkillRegistry skills)
    {
        if (player?.Entity == null || registry == null || !player.GetModData(AppliedModDataKey, false))
        {
            return;
        }

        EntityPlayer entityPlayer = player.Entity;
        if (entityPlayer.SidedProperties?.Behaviors == null)
        {
            return;
        }

        string[] extra = entityPlayer.WatchedAttributes.GetStringArray("extraTraits") ?? [];
        HashSet<string> folded = ReadFoldedExtras(player);
        HashSet<string> fresh = NewExtraTraitCodes(extra, folded);
        if (fresh.Count == 0)
        {
            return;
        }

        EntityBehaviorProgress? progress = entityPlayer.GetBehavior<EntityBehaviorProgress>();
        if (progress == null)
        {
            return;
        }

        progress.EnsureLoaded(player, skills);
        Dictionary<string, int> scores = NewBaseScores();
        foreach (string id in AttributeIds.All)
        {
            scores[id] = progress.GetAttribute(id);
        }

        ApplyDeltas(scores, registry, fresh);
        ClampScores(scores);
        foreach (string id in AttributeIds.All)
        {
            progress.SetAttribute(id, scores[id]);
        }

        RememberFoldedExtras(player, folded.Concat(fresh));
    }

    /// <summary>Whether a trait code should remain on a class after mutation.</summary>
    public static bool ShouldKeepOnClass(ITraitAttributeRegistry registry, string traitCode)
    {
        if (string.IsNullOrWhiteSpace(traitCode) || registry == null)
        {
            return true;
        }

        if (!registry.TryGet(traitCode.Trim(), out TraitAttributeMapping mapping))
        {
            return true;
        }

        return !mapping.ShouldStripFromClass;
    }

    public static HashSet<string> NewExtraTraitCodes(
        IEnumerable<string>? extraTraits,
        IEnumerable<string>? alreadyFolded)
    {
        HashSet<string> folded = new(StringComparer.OrdinalIgnoreCase);
        if (alreadyFolded != null)
        {
            foreach (string raw in alreadyFolded)
            {
                if (!string.IsNullOrWhiteSpace(raw))
                {
                    folded.Add(raw.Trim());
                }
            }
        }

        HashSet<string> fresh = new(StringComparer.OrdinalIgnoreCase);
        if (extraTraits == null)
        {
            return fresh;
        }

        foreach (string raw in extraTraits)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                continue;
            }

            string code = raw.Trim();
            if (folded.Add(code))
            {
                fresh.Add(code);
            }
        }

        return fresh;
    }

    static void RememberFoldedExtras(IServerPlayer player, IEnumerable<string>? extras)
    {
        HashSet<string> codes = new(StringComparer.OrdinalIgnoreCase);
        if (extras != null)
        {
            foreach (string raw in extras)
            {
                if (!string.IsNullOrWhiteSpace(raw))
                {
                    codes.Add(raw.Trim());
                }
            }
        }

        player.SetModData(FoldedExtrasModDataKey, string.Join(",", codes));
    }

    static HashSet<string> ReadFoldedExtras(IServerPlayer player)
    {
        HashSet<string> folded = new(StringComparer.OrdinalIgnoreCase);
        string stored = player.GetModData(FoldedExtrasModDataKey, "");
        if (string.IsNullOrWhiteSpace(stored))
        {
            return folded;
        }

        foreach (string raw in stored.Split(','))
        {
            if (!string.IsNullOrWhiteSpace(raw))
            {
                folded.Add(raw.Trim());
            }
        }

        return folded;
    }

    static Dictionary<string, int> NewBaseScores()
    {
        Dictionary<string, int> scores = new(StringComparer.OrdinalIgnoreCase);
        foreach (string id in AttributeIds.All)
        {
            scores[id] = AttributeGrowth.DefaultScore;
        }

        return scores;
    }

    static void ApplyDeltas(
        Dictionary<string, int> scores,
        ITraitAttributeRegistry registry,
        IEnumerable<string>? traitCodes)
    {
        if (traitCodes == null || registry == null)
        {
            return;
        }

        foreach (string raw in traitCodes)
        {
            if (string.IsNullOrWhiteSpace(raw) || !registry.TryGet(raw.Trim(), out TraitAttributeMapping mapping))
            {
                continue;
            }

            foreach (KeyValuePair<string, int> delta in mapping.Attributes)
            {
                scores[delta.Key] = scores[delta.Key] + delta.Value;
            }
        }
    }

    static void ClampScores(Dictionary<string, int> scores)
    {
        foreach (string id in AttributeIds.All)
        {
            scores[id] = Math.Clamp(scores[id], 0, AttributeGrowth.MaxScore);
        }
    }
}
