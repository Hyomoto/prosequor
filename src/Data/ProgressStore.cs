using System.Text;
using Newtonsoft.Json;
using Prosequor.Xp;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.Server;

namespace Prosequor.Data;

/// <summary>ModData persistence and WatchedAttributes mirror.</summary>
public static class ProgressStore
{
    public const string ModDataKey = "prosequor-progress";
    public const string AttrTree = "prosequor";

    public static PlayerProgressState Load(IServerPlayer player, ISkillRegistry registry)
    {
        if (TryHydrateStored(ReadModData(player), registry, out PlayerProgressState stored))
        {
            return stored;
        }

        PlayerProgressState created = PlayerProgressState.CreateNew(registry);
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
        out PlayerProgressState state)
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
        PlayerProgressState.EnsureAttributeEntries(stored);
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

    public static void Save(IServerPlayer player, PlayerProgressState state)
    {
        WriteModData(player, state);
        MirrorToEntity(player.Entity, state);
    }

    public static void MirrorToEntity(Entity? entity, PlayerProgressState state)
    {
        if (entity == null)
        {
            return;
        }

        ITreeAttribute tree = EnsureRootTree(entity);
        tree.SetInt("schema", state.Schema);
        MirrorPlayerTrack(entity, state.PlayerLevel, state.PlayerXp, state.UnlockPoints);
        MirrorAttributes(entity, state);

        ITreeAttribute skillsTree = tree.GetTreeAttribute("skills") as TreeAttribute ?? new TreeAttribute();
        foreach (KeyValuePair<string, SkillProgressState> kv in state.Skills)
        {
            MirrorSkillIntoTree(skillsTree, kv.Key, kv.Value);
        }

        tree["skills"] = skillsTree;
        entity.WatchedAttributes.MarkPathDirty(AttrTree);
    }

    public static void MirrorPlayerTrack(Entity entity, int playerLevel, float playerXp, int unlockPoints)
    {
        ITreeAttribute tree = EnsureRootTree(entity);
        tree.SetInt("playerLevel", playerLevel);
        tree.SetFloat("playerXp", playerXp);
        tree.SetInt("unlockPoints", unlockPoints);
        entity.WatchedAttributes.MarkPathDirty(AttrTree);
    }

    /// <summary>Mirror attribute scores and growth-bucket fills for the stats panel.</summary>
    public static void MirrorAttributes(Entity entity, PlayerProgressState state)
    {
        PlayerProgressState.EnsureAttributeEntries(state);
        ITreeAttribute tree = EnsureRootTree(entity);
        TreeAttribute attributesTree = new();
        TreeAttribute bucketsTree = new();
        foreach (string id in AttributeIds.All)
        {
            attributesTree.SetInt(id, state.Attributes[id]);
            bucketsTree.SetFloat(id, state.AttributeBuckets[id]);
        }

        tree["attributes"] = attributesTree;
        tree["attributeBuckets"] = bucketsTree;
        entity.WatchedAttributes.MarkPathDirty(AttrTree);
    }

    public static void MirrorSkillTrack(Entity entity, string skillId, int level, float xp)
    {
        if (string.IsNullOrWhiteSpace(skillId))
        {
            return;
        }

        ITreeAttribute skillsTree = EnsureSkillsTree(entity);
        ITreeAttribute skillTree = skillsTree.GetTreeAttribute(skillId) as TreeAttribute ?? new TreeAttribute();
        skillTree.SetInt("level", level);
        skillTree.SetFloat("xp", xp);
        if (skillTree.GetTreeAttribute("unlockTiers") == null)
        {
            skillTree["unlockTiers"] = new TreeAttribute();
        }

        skillsTree[skillId] = skillTree;
        entity.WatchedAttributes.MarkPathDirty(AttrTree);
    }

    public static void MirrorUnlockTier(Entity entity, string skillId, string nodeId, int tier)
    {
        if (string.IsNullOrWhiteSpace(skillId) || string.IsNullOrWhiteSpace(nodeId))
        {
            return;
        }

        ITreeAttribute skillsTree = EnsureSkillsTree(entity);
        ITreeAttribute skillTree = skillsTree.GetTreeAttribute(skillId) as TreeAttribute ?? new TreeAttribute();
        ITreeAttribute tiersTree = skillTree.GetTreeAttribute("unlockTiers") as TreeAttribute ?? new TreeAttribute();
        if (tier <= 0)
        {
            tiersTree.RemoveAttribute(nodeId);
        }
        else
        {
            tiersTree.SetInt(nodeId, tier);
        }

        skillTree["unlockTiers"] = tiersTree;
        skillsTree[skillId] = skillTree;
        entity.WatchedAttributes.MarkPathDirty(AttrTree);
    }

    public static void ApplyVisibleMirror(Entity entity, PlayerProgressState state, VisibleProgressChange change)
    {
        if (change.MirrorPlayerTrack)
        {
            MirrorPlayerTrack(entity, state.PlayerLevel, state.PlayerXp, state.UnlockPoints);
        }

        if (change.MirrorAttributes)
        {
            MirrorAttributes(entity, state);
        }

        if (change.MirrorSkillTrack
            && !string.IsNullOrWhiteSpace(change.SkillId)
            && state.Skills.TryGetValue(change.SkillId, out SkillProgressState? skill))
        {
            MirrorSkillTrack(entity, change.SkillId, skill.Level, skill.Xp);
        }

        if (change.MirrorUnlockTiers
            && !string.IsNullOrWhiteSpace(change.SkillId)
            && !string.IsNullOrWhiteSpace(change.UnlockNodeId)
            && state.Skills.TryGetValue(change.SkillId, out SkillProgressState? unlockSkill))
        {
            MirrorUnlockTier(
                entity,
                change.SkillId,
                change.UnlockNodeId,
                unlockSkill.GetTier(change.UnlockNodeId));
        }
    }

    public static PlayerProgressState? ReadFromEntity(Entity entity)
    {
        ITreeAttribute? tree = entity.WatchedAttributes.GetTreeAttribute(AttrTree);
        if (tree == null)
        {
            return null;
        }

        int storedSchema = tree.HasAttribute("schema") ? tree.GetInt("schema") : 0;
        PlayerProgressState state = new()
        {
            Schema = storedSchema > 0 ? storedSchema : PlayerProgressState.CurrentSchema,
            PlayerLevel = tree.GetInt("playerLevel", XpCurves.PlayerMinLevel),
            PlayerXp = tree.GetFloat("playerXp"),
            UnlockPoints = tree.GetInt("unlockPoints")
        };

        ITreeAttribute? attributesTree = tree.GetTreeAttribute("attributes");
        if (attributesTree != null)
        {
            foreach (string id in AttributeIds.All)
            {
                if (attributesTree.HasAttribute(id))
                {
                    state.Attributes[id] = attributesTree.GetInt(id, AttributeGrowth.DefaultScore);
                }
            }
        }

        ITreeAttribute? bucketsTree = tree.GetTreeAttribute("attributeBuckets");
        if (bucketsTree != null)
        {
            foreach (string id in AttributeIds.All)
            {
                if (bucketsTree.HasAttribute(id))
                {
                    state.AttributeBuckets[id] = bucketsTree.GetFloat(id);
                }
            }
        }

        PlayerProgressState.EnsureAttributeEntries(state);

        ITreeAttribute? skillsTree = tree.GetTreeAttribute("skills");
        if (skillsTree != null)
        {
            foreach (KeyValuePair<string, IAttribute> kv in skillsTree)
            {
                if (kv.Value is not ITreeAttribute skillTree)
                {
                    continue;
                }

                SkillProgressState skill = new()
                {
                    Level = skillTree.GetInt("level"),
                    Xp = skillTree.GetFloat("xp")
                };

                ITreeAttribute? tiersTree = skillTree.GetTreeAttribute("unlockTiers");
                if (tiersTree != null)
                {
                    foreach (KeyValuePair<string, IAttribute> tier in tiersTree)
                    {
                        skill.SetTier(tier.Key, tiersTree.GetInt(tier.Key));
                    }
                }

                if (storedSchema < UnlockIdRemap.Schema)
                {
                    skill.RemapUnlockIds(kv.Key);
                }

                state.Skills[kv.Key] = skill;
            }
        }

        if (state.Schema < PlayerProgressState.CurrentSchema)
        {
            state.Schema = PlayerProgressState.CurrentSchema;
        }

        return state;
    }

    static ITreeAttribute EnsureRootTree(Entity entity)
    {
        ITreeAttribute? tree = entity.WatchedAttributes.GetTreeAttribute(AttrTree);
        if (tree == null)
        {
            tree = new TreeAttribute();
            entity.WatchedAttributes.SetAttribute(AttrTree, tree);
        }

        return tree;
    }

    static ITreeAttribute EnsureSkillsTree(Entity entity)
    {
        ITreeAttribute tree = EnsureRootTree(entity);
        if (tree.GetTreeAttribute("skills") is not TreeAttribute skillsTree)
        {
            skillsTree = new TreeAttribute();
            tree["skills"] = skillsTree;
        }

        return skillsTree;
    }

    static void MirrorSkillIntoTree(ITreeAttribute skillsTree, string skillId, SkillProgressState skill)
    {
        TreeAttribute skillTree = new();
        skillTree.SetInt("level", skill.Level);
        skillTree.SetFloat("xp", skill.Xp);

        TreeAttribute tiersTree = new();
        foreach (KeyValuePair<string, int> tier in skill.UnlockTiers)
        {
            tiersTree.SetInt(tier.Key, tier.Value);
        }

        skillTree["unlockTiers"] = tiersTree;
        skillsTree[skillId] = skillTree;
    }
}
