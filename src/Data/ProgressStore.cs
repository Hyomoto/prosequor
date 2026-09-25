using System.Text;
using Newtonsoft.Json;
using Prosequor.Xp;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.Server;

namespace Prosequor.Data;

/// <summary>
/// ModData persistence and sparse public WatchedAttributes mirror.
/// Private numbers (XP, points, attribute buckets) ride the owner channel.
/// </summary>
public static class ProgressStore
{
    public const string ModDataKey = "prosequor-progress";

    /// <summary>Legacy full-blob root. Removed on write so join does not keep sending it.</summary>
    public const string AttrTree = "prosequor";

    /// <summary>Public unlock tiers (omit empty skills).</summary>
    public const string AttrUnlocks = "prosequorU";

    /// <summary>Public attribute scores only.</summary>
    public const string AttrScores = "prosequorA";

    public static PlayerProgressState Load(
        IServerPlayer player,
        ISkillRegistry registry,
        IAttributeStatRegistry? stats = null)
    {
        if (TryHydrateStored(ReadModData(player), registry, out PlayerProgressState stored, stats))
        {
            return stored;
        }

        PlayerProgressState created = PlayerProgressState.CreateNew(registry, stats);
        XpBucketFormulas.RefreshAllCaps(created);
        return created;
    }

    /// <summary>ModData bytes from an online or offline world player. Null when unavailable.</summary>
    public static byte[]? ReadModData(IPlayer? player)
    {
        if (player is IServerPlayer server)
        {
            return server.GetModdata(ModDataKey);
        }

        return player?.WorldData?.GetModdata(ModDataKey);
    }

    /// <summary>
    /// Hydrate a previously stored blob. False when missing or schema-less (do not invent a row).
    /// </summary>
    public static bool TryHydrateStored(
        byte[]? bytes,
        ISkillRegistry registry,
        out PlayerProgressState state,
        IAttributeStatRegistry? stats = null)
    {
        state = null!;
        if (bytes == null || bytes.Length == 0)
        {
            return false;
        }

        PlayerProgressState? stored = JsonConvert.DeserializeObject<PlayerProgressState>(
            Encoding.UTF8.GetString(bytes));
        if (stored == null || stored.Schema <= 0)
        {
            return false;
        }

        bool remapUnlocks = stored.Schema < UnlockIdRemap.Schema;
        foreach (KeyValuePair<string, SkillProgressState> kv in stored.Skills)
        {
            if (remapUnlocks)
            {
                kv.Value.RemapUnlockIds(kv.Key);
            }

            kv.Value.MigrateLegacyUnlocks();
        }

        stored.Schema = PlayerProgressState.CurrentSchema;
        PlayerProgressState.EnsureSkillEntries(stored, registry);
        IReadOnlyList<string> catalog = stats != null
            ? AttributeIds.CatalogIds(stats)
            : AttributeIds.All;
        PlayerProgressState.EnsureAttributeEntries(stored, catalog);
        stored.ReconcileLevelsFromXp();
        XpBucketFormulas.RefreshAllCaps(stored);
        state = stored;
        return true;
    }

    public static byte[] Serialize(PlayerProgressState state) =>
        Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(state));

    public static void WriteModData(IServerPlayer player, PlayerProgressState state)
    {
        player.SetModdata(ModDataKey, Serialize(state));
    }

    public static void Save(
        IServerPlayer player,
        PlayerProgressState state,
        IReadOnlyList<string>? catalog = null)
    {
        WriteModData(player, state);
        MirrorToEntity(player.Entity, state, catalog);
    }

    /// <summary>
    /// Full public mirror: unlock tiers + attribute scores. Removes the legacy blob root.
    /// </summary>
    public static void MirrorToEntity(
        Entity? entity,
        PlayerProgressState state,
        IReadOnlyList<string>? catalog = null)
    {
        if (entity == null)
        {
            return;
        }

        RemoveLegacyTree(entity);
        MirrorUnlocks(entity, state);
        MirrorAttributeScores(entity, state, catalog);
    }

    public static void MirrorAttributeScores(
        Entity entity,
        PlayerProgressState state,
        IReadOnlyList<string>? catalog = null)
    {
        IReadOnlyList<string> ids = catalog ?? AttributeIds.All;
        PlayerProgressState.EnsureAttributeEntries(state, ids);
        TreeAttribute scores = new();
        foreach (string id in ids)
        {
            scores.SetInt(id, state.Attributes[id]);
        }

        entity.WatchedAttributes.SetAttribute(AttrScores, scores);
        entity.WatchedAttributes.MarkPathDirty(AttrScores);
    }

    /// <summary>Rewrite the full public unlock tree (omit skills with no owned tiers).</summary>
    public static void MirrorUnlocks(Entity entity, PlayerProgressState state)
    {
        TreeAttribute root = new();
        int skillCount = 0;
        foreach (KeyValuePair<string, SkillProgressState> kv in state.Skills)
        {
            TreeAttribute? tiers = BuildTiersTree(kv.Value);
            if (tiers == null)
            {
                continue;
            }

            TreeAttribute skillTree = new();
            skillTree["unlockTiers"] = tiers;
            root[kv.Key] = skillTree;
            skillCount++;
        }

        if (skillCount == 0)
        {
            if (entity.WatchedAttributes.HasAttribute(AttrUnlocks))
            {
                entity.WatchedAttributes.RemoveAttribute(AttrUnlocks);
                entity.WatchedAttributes.MarkPathDirty(AttrUnlocks);
            }

            return;
        }

        entity.WatchedAttributes.SetAttribute(AttrUnlocks, root);
        entity.WatchedAttributes.MarkPathDirty(AttrUnlocks);
    }

    public static void MirrorUnlockTier(Entity entity, string skillId, string nodeId, int tier)
    {
        if (string.IsNullOrWhiteSpace(skillId) || string.IsNullOrWhiteSpace(nodeId))
        {
            return;
        }

        ITreeAttribute root = EnsureUnlocksRoot(entity);
        ITreeAttribute skillTree = root.GetTreeAttribute(skillId) as TreeAttribute ?? new TreeAttribute();
        ITreeAttribute tiersTree = skillTree.GetTreeAttribute("unlockTiers") as TreeAttribute ?? new TreeAttribute();
        if (tier <= 0)
        {
            tiersTree.RemoveAttribute(nodeId);
        }
        else
        {
            tiersTree.SetInt(nodeId, tier);
        }

        if (CountEntries(tiersTree) == 0)
        {
            root.RemoveAttribute(skillId);
        }
        else
        {
            skillTree["unlockTiers"] = tiersTree;
            root[skillId] = skillTree;
        }

        if (CountEntries(root) == 0)
        {
            entity.WatchedAttributes.RemoveAttribute(AttrUnlocks);
        }
        else
        {
            entity.WatchedAttributes.SetAttribute(AttrUnlocks, root);
        }

        entity.WatchedAttributes.MarkPathDirty(AttrUnlocks);
    }

    public static void ApplyVisibleMirror(
        Entity entity,
        PlayerProgressState state,
        VisibleProgressChange change,
        IReadOnlyList<string>? catalog = null)
    {
        RemoveLegacyTree(entity);

        if (change.MirrorAttributeScores)
        {
            MirrorAttributeScores(entity, state, catalog);
        }

        if (change.MirrorUnlockTiers)
        {
            if (!string.IsNullOrWhiteSpace(change.SkillId)
                && !string.IsNullOrWhiteSpace(change.UnlockNodeId)
                && state.Skills.TryGetValue(change.SkillId, out SkillProgressState? unlockSkill))
            {
                MirrorUnlockTier(
                    entity,
                    change.SkillId,
                    change.UnlockNodeId,
                    unlockSkill.GetTier(change.UnlockNodeId));
            }
            else
            {
                MirrorUnlocks(entity, state);
            }
        }
    }

    /// <summary>
    /// Merge public unlock tiers and attribute scores into <paramref name="into"/>.
    /// Does not touch XP, points, or attribute buckets.
    /// </summary>
    public static bool MergePublicFromEntity(
        Entity entity,
        PlayerProgressState into,
        IReadOnlyList<string>? catalog = null)
    {
        IReadOnlyList<string> ids = catalog ?? AttributeIds.All;
        bool any = false;
        ITreeAttribute? scores = entity.WatchedAttributes.GetTreeAttribute(AttrScores);
        if (scores != null)
        {
            foreach (string id in ids)
            {
                if (scores.HasAttribute(id))
                {
                    into.Attributes[id] = scores.GetInt(id, AttributeGrowth.DefaultScore);
                    any = true;
                }
            }
        }

        ITreeAttribute? unlocks = entity.WatchedAttributes.GetTreeAttribute(AttrUnlocks);
        if (unlocks != null)
        {
            // Clear owned tiers then re-apply so revokes on the wire stick.
            foreach (SkillProgressState skill in into.Skills.Values)
            {
                skill.UnlockTiers.Clear();
            }

            foreach (KeyValuePair<string, IAttribute> kv in unlocks)
            {
                if (kv.Value is not ITreeAttribute skillTree)
                {
                    continue;
                }

                SkillProgressState skill = into.GetOrCreateSkill(kv.Key);
                skill.UnlockTiers.Clear();
                ITreeAttribute? tiersTree = skillTree.GetTreeAttribute("unlockTiers");
                if (tiersTree == null)
                {
                    continue;
                }

                foreach (KeyValuePair<string, IAttribute> tier in tiersTree)
                {
                    skill.SetTier(tier.Key, tiersTree.GetInt(tier.Key));
                    any = true;
                }
            }
        }
        else if (entity.WatchedAttributes.HasAttribute(AttrUnlocks) == false
                 && into.Skills.Values.Any(s => s.UnlockTiers.Count > 0))
        {
            // Explicit empty public tree: wipe mirrored unlocks.
            foreach (SkillProgressState skill in into.Skills.Values)
            {
                if (skill.UnlockTiers.Count > 0)
                {
                    skill.UnlockTiers.Clear();
                    any = true;
                }
            }
        }

        // Legacy full blob (pre-sparse): hydrate once for clients mid-upgrade.
        if (!any && entity.WatchedAttributes.GetTreeAttribute(AttrTree) is ITreeAttribute legacy)
        {
            MergeLegacyTree(legacy, into, ids);
            any = true;
        }

        PlayerProgressState.EnsureAttributeEntries(into, ids);
        return any || scores != null || unlocks != null;
    }

    /// <summary>
    /// Snapshot public fields only (unlocks + scores). Missing XP stays at defaults.
    /// Prefer <see cref="MergePublicFromEntity"/> when an existing state must keep private fields.
    /// </summary>
    public static PlayerProgressState? ReadFromEntity(Entity entity)
    {
        bool hasPublic =
            entity.WatchedAttributes.HasAttribute(AttrScores)
            || entity.WatchedAttributes.HasAttribute(AttrUnlocks)
            || entity.WatchedAttributes.HasAttribute(AttrTree);
        if (!hasPublic)
        {
            return null;
        }

        PlayerProgressState state = new() { Schema = PlayerProgressState.CurrentSchema };
        MergePublicFromEntity(entity, state);
        return state;
    }

    public static bool HasLegacyTree(Entity entity) =>
        entity.WatchedAttributes.HasAttribute(AttrTree);

    public static int CountUnlockSkillKeys(Entity entity)
    {
        ITreeAttribute? unlocks = entity.WatchedAttributes.GetTreeAttribute(AttrUnlocks);
        return unlocks == null ? 0 : CountEntries(unlocks);
    }

    public static int CountUnlockNodeKeys(Entity entity)
    {
        ITreeAttribute? unlocks = entity.WatchedAttributes.GetTreeAttribute(AttrUnlocks);
        if (unlocks == null)
        {
            return 0;
        }

        int total = 0;
        foreach (KeyValuePair<string, IAttribute> skill in unlocks)
        {
            if (skill.Value is not ITreeAttribute skillTree)
            {
                continue;
            }

            ITreeAttribute? tiers = skillTree.GetTreeAttribute("unlockTiers");
            if (tiers != null)
            {
                total += CountEntries(tiers);
            }
        }

        return total;
    }

    public static int PublicTreeBytes(Entity entity)
    {
        int total = 0;
        if (entity.WatchedAttributes.GetTreeAttribute(AttrUnlocks) is ITreeAttribute unlocks)
        {
            total += TreeBytes(unlocks);
        }

        if (entity.WatchedAttributes.GetTreeAttribute(AttrScores) is ITreeAttribute scores)
        {
            total += TreeBytes(scores);
        }

        if (entity.WatchedAttributes.GetTreeAttribute(AttrTree) is ITreeAttribute legacy)
        {
            total += TreeBytes(legacy);
        }

        return total;
    }

    static void RemoveLegacyTree(Entity entity)
    {
        if (!entity.WatchedAttributes.HasAttribute(AttrTree))
        {
            return;
        }

        entity.WatchedAttributes.RemoveAttribute(AttrTree);
        entity.WatchedAttributes.MarkPathDirty(AttrTree);
    }

    static ITreeAttribute EnsureUnlocksRoot(Entity entity)
    {
        if (entity.WatchedAttributes.GetTreeAttribute(AttrUnlocks) is TreeAttribute existing)
        {
            return existing;
        }

        TreeAttribute root = new();
        entity.WatchedAttributes.SetAttribute(AttrUnlocks, root);
        return root;
    }

    static TreeAttribute? BuildTiersTree(SkillProgressState skill)
    {
        TreeAttribute tiers = new();
        int count = 0;
        foreach (KeyValuePair<string, int> tier in skill.UnlockTiers)
        {
            if (tier.Value <= 0)
            {
                continue;
            }

            tiers.SetInt(tier.Key, tier.Value);
            count++;
        }

        return count == 0 ? null : tiers;
    }

    static void MergeLegacyTree(
        ITreeAttribute tree,
        PlayerProgressState into,
        IReadOnlyList<string>? catalog = null)
    {
        IReadOnlyList<string> ids = catalog ?? AttributeIds.All;
        ITreeAttribute? attributesTree = tree.GetTreeAttribute("attributes");
        if (attributesTree != null)
        {
            foreach (string id in ids)
            {
                if (attributesTree.HasAttribute(id))
                {
                    into.Attributes[id] = attributesTree.GetInt(id, AttributeGrowth.DefaultScore);
                }
            }
        }

        ITreeAttribute? skillsTree = tree.GetTreeAttribute("skills");
        if (skillsTree == null)
        {
            return;
        }

        foreach (KeyValuePair<string, IAttribute> kv in skillsTree)
        {
            if (kv.Value is not ITreeAttribute skillTree)
            {
                continue;
            }

            SkillProgressState skill = into.GetOrCreateSkill(kv.Key);
            ITreeAttribute? tiersTree = skillTree.GetTreeAttribute("unlockTiers");
            if (tiersTree == null)
            {
                continue;
            }

            foreach (KeyValuePair<string, IAttribute> tier in tiersTree)
            {
                skill.SetTier(tier.Key, tiersTree.GetInt(tier.Key));
            }
        }
    }

    static int CountEntries(ITreeAttribute tree)
    {
        int count = 0;
        foreach (KeyValuePair<string, IAttribute> _ in tree)
        {
            count++;
        }

        return count;
    }

    static int TreeBytes(ITreeAttribute tree)
    {
        using MemoryStream stream = new();
        using BinaryWriter writer = new(stream);
        tree.ToBytes(writer);
        writer.Flush();
        return (int)stream.Length;
    }
}
