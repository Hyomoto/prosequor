using Vintagestory.API.Common;

namespace Prosequor.Ability;

/// <summary>
/// Shared block-break classification for XP activities and mutate-drops fact tokens.
/// </summary>
public static class BlockBreakClassification
{
    public const string TokenDig = "dig";
    public const string TokenChop = "chop";
    public const string TokenMine = "mine";
    public const string TokenHarvest = "harvest";

    /// <summary>
    /// harvest: crop, berry bush, or fruit-tree blocks.
    /// dig: Soil, Sand, or Gravel (shovel-class terrain).
    /// chop: non-Leaves with a non-empty treeFellingGroupCode.
    /// Leaves and other blocks return null (no XP activity; drops may still tag leaves).
    /// </summary>
    public static string? ClassifyToken(Block broken)
    {
        if (broken == null || broken.Id == 0)
        {
            return null;
        }

        if (AbilityBootstrap.IsCropBlock(broken)
            || AbilityBootstrap.IsBerryBushBlock(broken)
            || AbilityBootstrap.IsFruitTreeBlock(broken)
            || ForageBlocks.IsMushroom(broken)
            || ForageBlocks.IsReed(broken)
            || ForageBlocks.IsLooseStick(broken))
        {
            return TokenHarvest;
        }

        if (broken.BlockMaterial == EnumBlockMaterial.Leaves)
        {
            return null;
        }

        string? fellingGroup = broken.Attributes?["treeFellingGroupCode"].AsString();
        if (!string.IsNullOrWhiteSpace(fellingGroup))
        {
            return TokenChop;
        }

        if (IsDiggableTerrain(broken) || IsSpecialtyDigBlock(broken))
        {
            return TokenDig;
        }

        if (broken.BlockMaterial is EnumBlockMaterial.Stone or EnumBlockMaterial.Ore)
        {
            return TokenMine;
        }

        return null;
    }

    /// <summary>
    /// Clay / peat / charcoal piles / saltpeter coatings — dig XP and shovel-class drops,
    /// even when block material is not Soil/Sand/Gravel.
    /// </summary>
    public static bool IsSpecialtyDigBlock(Block block)
    {
        if (block?.Code?.Path is not string path || block.Id == 0)
        {
            return false;
        }

        return AbilityBootstrap.IsRawClayBlockPath(path)
            || AbilityBootstrap.IsPeatBlockPath(path)
            || AbilityBootstrap.IsCharcoalBlockPath(path)
            || AbilityBootstrap.IsSaltpeterBlockPath(path);
    }

    /// <summary>Soil / sand / gravel (and path-based sand/gravel fallbacks).</summary>
    public static bool IsDiggableTerrain(Block block)
    {
        if (block == null || block.Id == 0)
        {
            return false;
        }

        if (block.BlockMaterial is EnumBlockMaterial.Soil
            or EnumBlockMaterial.Sand
            or EnumBlockMaterial.Gravel)
        {
            return true;
        }

        string? path = block.Code?.Path;
        return path != null
            && (AbilityBootstrap.IsSandPath(path) || AbilityBootstrap.IsGravelPath(path));
    }

    /// <summary>
    /// Adds contextual ability tags for the block. Does not clear existing catalog tags.
    /// </summary>
    public static void AddContextualTags(Block block, HashSet<string> tags)
    {
        if (block == null || block.Id == 0)
        {
            return;
        }

        if (block.BlockMaterial == EnumBlockMaterial.Leaves)
        {
            tags.Add(AbilityBootstrap.LeavesTag);
            return;
        }

        string? token = ClassifyToken(block);
        if (token == TokenHarvest)
        {
            if (AbilityBootstrap.IsCropBlock(block))
            {
                tags.Add(AbilityBootstrap.CropTag);
                tags.Add(
                    AbilityBootstrap.IsMatureCrop(block)
                        ? AbilityBootstrap.MatureCropTag
                        : AbilityBootstrap.ImmatureCropTag);
            }

            if (AbilityBootstrap.IsBerryBushBlock(block))
            {
                tags.Add(AbilityBootstrap.BerryBushTag);
            }

            if (AbilityBootstrap.IsFruitTreeBlock(block))
            {
                tags.Add(AbilityBootstrap.FruitTreeTag);
            }

            return;
        }

        if (token == TokenDig)
        {
            AddDigTerrainTags(block, tags);
            if (AbilityBootstrap.IsFarmlandBlock(block))
            {
                tags.Add(AbilityBootstrap.FarmlandTag);
            }

            return;
        }

        if (token == TokenChop)
        {
            tags.Add(AbilityBootstrap.WoodTag);
            if (IsPineWood(block))
            {
                tags.Add(AbilityBootstrap.PineTag);
            }

            return;
        }

        if (token == TokenMine)
        {
            if (block.BlockMaterial != EnumBlockMaterial.Ore)
            {
                tags.Add(AbilityBootstrap.StoneTag);
            }
            else
            {
                tags.Add(
                    AbilityBootstrap.IsGemstoneOre(block)
                        ? AbilityBootstrap.GemstoneTag
                        : AbilityBootstrap.OreTag);
            }
        }
    }

    static void AddDigTerrainTags(Block block, HashSet<string> tags)
    {
        string? path = block.Code?.Path;
        if (block.BlockMaterial == EnumBlockMaterial.Sand
            || (path != null && AbilityBootstrap.IsSandPath(path)))
        {
            tags.Add(AbilityBootstrap.SandTag);
            return;
        }

        if (block.BlockMaterial == EnumBlockMaterial.Gravel
            || (path != null && AbilityBootstrap.IsGravelPath(path)))
        {
            tags.Add(AbilityBootstrap.GravelTag);
            return;
        }

        if (path != null && AbilityBootstrap.IsPeatBlockPath(path))
        {
            tags.Add(AbilityBootstrap.PeatTag);
            return;
        }

        tags.Add(AbilityBootstrap.DirtTag);
        tags.Add(AbilityBootstrap.SoilTag);
        if (path != null && AbilityBootstrap.IsBonySoilPath(path))
        {
            tags.Add(AbilityBootstrap.BonySoilTag);
        }
    }

    /// <summary>
    /// Grown log codes use the wood type as the third path segment (e.g. log-grown-pine-ud).
    /// </summary>
    public static bool IsPineWood(Block block)
    {
        if (block?.Code == null)
        {
            return false;
        }

        return string.Equals(block.FirstCodePart(2), "pine", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsFellingWood(Block block)
    {
        if (block == null || block.Id == 0 || block.BlockMaterial == EnumBlockMaterial.Leaves)
        {
            return false;
        }

        string? fellingGroup = block.Attributes?["treeFellingGroupCode"].AsString();
        return !string.IsNullOrWhiteSpace(fellingGroup);
    }
}
