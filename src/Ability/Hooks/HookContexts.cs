using Prosequor.Player;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;

namespace Prosequor.Ability.Hooks;

/// <summary>
/// Shared context for any process that produces item drops. Block and position are
/// optional adapter metadata; actions must not require them unless explicitly block-only.
/// </summary>
public sealed class DropsContext : IHookContext
{
    /// <summary>
    /// Surface that owns this fold. Defaults to block-interaction (GetDrops);
    /// panning and other held-tool drop producers use item-interaction.
    /// </summary>
    public HookId Hook { get; init; } = HookIds.BlockInteraction;
    public required IWorldAccessor World { get; init; }
    public Block? Block { get; init; }
    public BlockPos? Pos { get; init; }
    public IPlayer? Player { get; init; }
    public IPlayerProgress? Progress { get; init; }
    public AbilityAction? Fact { get; set; }

    /// <summary>Resolved collection index for drop mutations / pool picks.</summary>
    public required CollectionIndex Tags { get; init; }

    /// <summary>
    /// Optional item ids to skip when picking from an output pool (e.g. mixed-clay
    /// excluding originals already in the haul).
    /// </summary>
    public HashSet<int>? PoolExcludeItemIds { get; set; }

    /// <summary>
    /// Vanilla drop list cloned before the per-stack quantity/stack loop.
    /// List-post (<c>stacks</c>) actions may inspect originals without seeing later appends.
    /// </summary>
    public IReadOnlyList<ItemStack> OriginalDrops { get; set; } = Array.Empty<ItemStack>();

    /// <summary>
    /// Ambient VS-backed drop table for re-rolls (GetDrops, pan material table, …).
    /// Null when the adapter cannot expose a re-rollable source.
    /// </summary>
    public IDropTable? DropTable { get; init; }

    /// <summary>
    /// Snapshot of farmland nutrients at break time (from <see cref="FarmlandNutrientScope"/>).
    /// </summary>
    public float[]? FarmlandNutrients { get; init; }

    /// <summary>
    /// Snapshot of farmland original fertility at break time.
    /// </summary>
    public int[]? FarmlandOriginalFertility { get; init; }
}

/// <summary>Rolls one additional stack from a VS-backed drop source attached by an adapter.</summary>
public interface IDropTable
{
    ItemStack? TryRollOne();
}

public sealed class InteractionSpeedContext : IHookContext, IHasBaseValue
{
    /// <summary>
    /// Surface that owns this fold. Mining / block-interact stay on block-interaction;
    /// held-tool interact (e.g. pan) uses item-interaction.
    /// </summary>
    public HookId Hook { get; init; } = HookIds.BlockInteraction;
    public IPlayer? Player { get; init; }
    public IPlayerProgress? Progress { get; init; }
    public AbilityAction? Fact { get; init; }

    /// <summary>Optional block material when the egress knows it (mining speed).</summary>
    public EnumBlockMaterial? Material { get; init; }

    /// <summary>Rate before any Prosequor rules are applied (mining speed or 1 for interact).</summary>
    public required float BaseValue { get; init; }
}

/// <summary>
/// Pending item usage (durability damage, bait consume, etc.). Rules transform
/// <c>amount</c>; the adapter applies the result.
/// </summary>
public sealed class ItemDurabilityContext : IHookContext
{
    public HookId Hook => HookIds.ItemInteraction;
    public required IWorldAccessor World { get; init; }
    public required Entity ByEntity { get; init; }
    public required ItemSlot ItemSlot { get; init; }
    public required CollectibleObject Collectible { get; init; }
    public IPlayer? Player { get; init; }
    public IPlayerProgress? Progress { get; init; }
    public AbilityAction? Fact { get; init; }

    /// <summary>The block whose break caused this usage, when known.</summary>
    public Block? BrokenBlock { get; init; }

    public BlockPos? BlockPos { get; init; }
}

public sealed class SkillXpContext : IHookContext
{
    public HookId Hook => HookIds.Progress;
    public IPlayer? Player { get; init; }
    public IPlayerProgress? Progress { get; init; }
    public AbilityAction? Fact { get; init; }

    /// <summary>Skill receiving the matched XP amount.</summary>
    public required string SkillId { get; init; }

    /// <summary>Base amount from the winning skill-embedded XP rule before modifiers.</summary>
    public required float BaseAmount { get; init; }
}

public sealed class SkillBucketCapContext : IHookContext
{
    public HookId Hook => HookIds.Progress;
    public IPlayer? Player { get; init; }
    public IPlayerProgress? Progress { get; init; }
    public AbilityAction? Fact { get; init; }

    /// <summary>Skill whose saturation bucket capacity is being resolved.</summary>
    public required string SkillId { get; init; }
}

/// <summary>
/// Pending success probability (0–1). Rules transform the value; the adapter
/// applies/stamps the result.
/// </summary>
public sealed class SuccessChanceContext : IHookContext
{
    public HookId Hook => HookIds.BlockInteraction;
    public required IWorldAccessor World { get; init; }
    public IPlayer? Player { get; init; }
    public IPlayerProgress? Progress { get; init; }
    public AbilityAction? Fact { get; init; }

    /// <summary>Chance before any Prosequor rules are applied.</summary>
    public required float BaseValue { get; init; }
}

/// <summary>
/// Pending growth-duration multiplier (1 = unchanged). Rules transform the value;
/// the adapter stamps and scales remaining hours.
/// </summary>
public sealed class GrowthDurationContext : IHookContext
{
    public HookId Hook => HookIds.BlockInteraction;
    public required IWorldAccessor World { get; init; }
    public IPlayer? Player { get; init; }
    public IPlayerProgress? Progress { get; init; }
    public AbilityAction? Fact { get; init; }

    /// <summary>Multiplier before any Prosequor rules are applied.</summary>
    public required float BaseValue { get; init; }
}

/// <summary>
/// Pending fertilizer absorption-rate multiplier (1 = unchanged). Rules transform
/// the value; the adapter stamps it onto farmland when fertilizing.
/// </summary>
public sealed class FertilizerAbsorbContext : IHookContext
{
    public HookId Hook => HookIds.BlockInteraction;
    public required IWorldAccessor World { get; init; }
    public IPlayer? Player { get; init; }
    public IPlayerProgress? Progress { get; init; }
    public AbilityAction? Fact { get; init; }

    /// <summary>Multiplier before any Prosequor rules are applied.</summary>
    public required float BaseValue { get; init; }
}

/// <summary>
/// Pending crop climate-window expand fraction (0 = unchanged). Rules transform
/// the value; the adapter stamps half-delta °C onto farmland when planting.
/// </summary>
public sealed class PlantCropClimateContext : IHookContext, IHasBaseValue
{
    public HookId Hook => HookIds.BlockInteraction;
    public required IWorldAccessor World { get; init; }
    public IPlayer? Player { get; init; }
    public IPlayerProgress? Progress { get; init; }
    public AbilityAction? Fact { get; init; }

    /// <summary>Expand fraction before any Prosequor rules are applied.</summary>
    public required float BaseValue { get; init; }
}

/// <summary>
/// Pending field-work area side length (1 = single cell). Rules transform the value;
/// hoe / watering-can adapters apply it at use time.
/// </summary>
public sealed class FieldWorkContext : IHookContext
{
    public HookId Hook => HookIds.BlockInteraction;
    public IPlayer? Player { get; init; }
    public IPlayerProgress? Progress { get; init; }
    public AbilityAction? Fact { get; init; }

    /// <summary>Side length before any Prosequor rules are applied.</summary>
    public required int BaseValue { get; init; }
}

/// <summary>
/// Pending multi-break block count. Rules transform the value; scythe adapters apply it.
/// </summary>
public sealed class ScytheMultiBreakContext : IHookContext
{
    public HookId Hook => HookIds.BlockInteraction;
    public IPlayer? Player { get; init; }
    public IPlayerProgress? Progress { get; init; }
    public AbilityAction? Fact { get; init; }

    /// <summary>Quantity before any Prosequor rules are applied.</summary>
    public required int BaseValue { get; init; }
}

/// <summary>
/// Portions to place per trough fill. Rules transform the value; fill adapters apply extras.
/// </summary>
public sealed class TroughFillContext : IHookContext
{
    public HookId Hook => HookIds.BlockInteraction;
    public IPlayer? Player { get; init; }
    public IPlayerProgress? Progress { get; init; }
    public AbilityAction? Fact { get; init; }

    /// <summary>Quantity before any Prosequor rules are applied (seed 1).</summary>
    public required int BaseValue { get; init; }
}

/// <summary>Angry-bee spawn chance fold when breaking a populated skep.</summary>
public sealed class SkepBeeSpawnContext : IHookContext, IHasBaseValue
{
    public HookId Hook => HookIds.BlockInteraction;
    public IPlayer? Player { get; init; }
    public IPlayerProgress? Progress { get; init; }
    public AbilityAction? Fact { get; init; }
    public Block? Block { get; init; }

    /// <summary>Vanilla <c>beemobSpawnChance</c> before Prosequor rules.</summary>
    public float BaseValue { get; init; }
}

/// <summary>Right-click skep harvest allow + break-chance folds.</summary>
public sealed class SkepHarvestContext : IHookContext, IHasBaseValue
{
    public HookId Hook => HookIds.BlockInteraction;
    public IPlayer? Player { get; init; }
    public IPlayerProgress? Progress { get; init; }
    public AbilityAction? Fact { get; init; }
    public Block? Block { get; init; }

    /// <summary>Baseline for number phases (break-chance seed 0).</summary>
    public float BaseValue { get; init; }
}

/// <summary>Sneak + right-click bloomery harvest allow + break-chance folds.</summary>
public sealed class BloomeryHarvestContext : IHookContext, IHasBaseValue
{
    public HookId Hook => HookIds.BlockInteraction;
    public IPlayer? Player { get; init; }
    public IPlayerProgress? Progress { get; init; }
    public AbilityAction? Fact { get; init; }
    public Block? Block { get; init; }

    /// <summary>Baseline for number phases (break-chance seed 0).</summary>
    public float BaseValue { get; init; }
}

/// <summary>
/// Pending voxel-crafting values (clay form / anvil heavy-hit assists). Rules transform by phase;
/// adapters apply at form/refill/finish or anvil OnHit time.
/// </summary>
public sealed class VoxelWorkContext : IHookContext
{
    public HookId Hook => HookIds.ItemInteraction;
    public required IWorldAccessor World { get; init; }
    public IPlayer? Player { get; init; }
    public IPlayerProgress? Progress { get; init; }
    public AbilityAction? Fact { get; init; }
}

/// <summary>
/// Process-completion stacks (kiln fire, barrel seal, firepit cook/smelt). Adapters
/// resolve the process starter from BE/item pedigree (sole contributor or maker) and
/// look up progress by uid (online or parked). Chance belongs in nested
/// <c>prosequor:chance</c> actions, not a hook phase.
/// </summary>
public sealed class MutateProcessContext : IHookContext
{
    public HookId Hook => HookIds.BlockInteraction;
    public required IWorldAccessor World { get; init; }
    public required ItemSlot OutputSlot { get; init; }
    public IPlayer? Player { get; init; }
    public IPlayerProgress? Progress { get; init; }
    public AbilityAction? Fact { get; init; }
    public required CollectionIndex Tags { get; init; }
    public required CollectibleVariantTable Variants { get; init; }
}

/// <summary>
/// Mounted vehicle effects (land rideables and boats).
/// <see cref="Mount"/> is the controlled entity when known.
/// </summary>
public sealed class MountedContext : IHookContext
{
    public HookId Hook => HookIds.EntityInteraction;
    public IPlayer? Player { get; init; }
    public IPlayerProgress? Progress { get; init; }
    public AbilityAction? Fact { get; init; }
    public Entity? Mount { get; init; }
}

/// <summary>
/// Wearable repair (clothing condition merge or armor craft-grid durability).
/// </summary>
public sealed class RepairContext : IHookContext
{
    public HookId Hook => HookIds.ItemInteraction;
    public IPlayer? Player { get; init; }
    public IPlayerProgress? Progress { get; init; }
    public AbilityAction? Fact { get; init; }
    public ItemStack? Repaired { get; init; }
}

/// <summary>
/// Bait lifecycle after a catch clears bobber bait. Prefix adapters snapshot bait;
/// <c>restock</c> may restore it for free and/or refill from inventory.
/// </summary>
public sealed class ConsumeBaitContext : IHookContext
{
    public HookId Hook => HookIds.ItemInteraction;
    public required IWorldAccessor World { get; init; }
    public IPlayer? Player { get; init; }
    public IPlayerProgress? Progress { get; init; }
    public AbilityAction? Fact { get; init; }

    /// <summary>Bait that was on the bobber before vanilla cleared it.</summary>
    public required ItemStack ConsumedBait { get; init; }

    /// <summary>
    /// Pending bait for the hook after <c>restock</c>. Null leaves the bobber empty.
    /// </summary>
    public ItemStack? RestockedBait { get; set; }
}

/// <summary>
/// Craft-grid take yield (quantity extras) and optional output mutation.
/// </summary>
public sealed class CraftMutateOutputContext : IHookContext
{
    public HookId Hook => HookIds.CraftingInteraction;
    public IPlayer? Player { get; init; }
    public IPlayerProgress? Progress { get; init; }
    public AbilityAction? Fact { get; init; }
    public ItemStack? Crafted { get; init; }

    /// <summary>World for attribute mutators that need it (e.g. freshness).</summary>
    public IWorldAccessor? World { get; init; }

    /// <summary>
    /// Nested knob folds for <c>apply-quality</c>. Stations set from <c>ProsequorModSystem.Pipeline</c>;
    /// fixtures may inject a local pipeline. Stamps read pre-warmed knobs, not this, at apply time.
    /// </summary>
    public AbilityPipeline? Pipeline { get; init; }

    /// <summary>
    /// Optional RNG for <c>apply-quality</c>. Stations leave null (use world rand);
    /// fixtures inject a seeded <see cref="Random"/>.
    /// </summary>
    public Random? Rand { get; init; }

    /// <summary>Crafting inventory for <c>refund</c> restock (ingredient slots).</summary>
    public InventoryBase? CraftInventory { get; init; }

    /// <summary>Pre-consume ingredient snapshot for refund accounting.</summary>
    public IReadOnlyList<ItemStack>? IngredientSnapshot { get; init; }

    public CollectionIndex? Collections { get; init; }

    /// <summary>
    /// Highest quality mean seen this craft (for upgrading the leading grade affix).
    /// Mutable across matching <c>apply-quality</c> rules in one pipeline run.
    /// </summary>
    public float BestQualityMean { get; set; } = float.NegativeInfinity;

    /// <summary>
    /// Pre-warmed apply-quality knobs for this attributes pass. Station/fixtures set via WarmUp;
    /// stamps require <see cref="QualityKnobsReady"/>.
    /// </summary>
    public bool QualityKnobsReady { get; set; }

    public float QualityBase { get; set; }
    public float QualityWindow { get; set; }
    public float QualityRolls { get; set; }
    public float QualityBonus { get; set; }
}

/// <summary>
/// Always-on player body stats (max health, later melee / armor walk / etc.).
/// </summary>
public sealed class PlayerInteractionContext : IHookContext
{
    public HookId Hook => HookIds.PlayerInteraction;
    public IPlayer? Player { get; init; }
    public IPlayerProgress? Progress { get; init; }
    public AbilityAction? Fact { get; init; }

    /// <summary>
    /// Baseline for <c>number</c> with <c>ofBase: true</c> (Digging shovel-expert pattern).
    /// </summary>
    public float BaseValue { get; init; }
}

/// <summary>
/// Animal AI behavior checks (flee / melee / brood / milk / pet). Fact verb selects the check;
/// <c>target</c> is the animal entity code for <c>when.tags</c>.
/// </summary>
public sealed class AnimalBehaviorContext : IHookContext, IHasBaseValue
{
    public HookId Hook => HookIds.EntityInteraction;
    public IPlayer? Player { get; init; }
    public IPlayerProgress? Progress { get; init; }
    public AbilityAction? Fact { get; init; }
    public Entity? Animal { get; init; }

    /// <summary>
    /// Baseline for <c>ofBase</c> number adds on <c>chance</c>
    /// (flee uses 1 so root <c>perSkillLevel</c> is an absolute fraction).
    /// </summary>
    public float BaseValue { get; init; }

    /// <summary>
    /// Side channel for <c>add-friendliness</c> on animal-pet / default
    /// (allow fold stays on the int seed).
    /// </summary>
    public int FriendlinessGain { get; set; }
}

/// <summary>Low-light vision capacity (client post-process blend weight ceiling).</summary>
public sealed class CatEyesContext : IHookContext
{
    public HookId Hook => HookIds.PlayerInteraction;
    public IPlayer? Player { get; init; }
    public IPlayerProgress? Progress { get; init; }
    public AbilityAction? Fact { get; init; }
}

/// <summary>Incoming HP loss after vanilla <c>onDamaged</c> delegates; rules fold <c>amount</c>.</summary>
public sealed class TakeDamageContext : IHookContext
{
    public HookId Hook => HookIds.PlayerInteraction;
    public IPlayer? Player { get; init; }
    public IPlayerProgress? Progress { get; init; }
    public AbilityAction? Fact { get; init; }
    public required Entity Entity { get; init; }
    public required float CurrentHealth { get; init; }
    public required DamageSource DamageSource { get; init; }
}

/// <summary>Plumb-and-square reinforcement strength fold.</summary>
public sealed class ReinforceContext : IHookContext
{
    public HookId Hook => HookIds.ItemInteraction;
    public IPlayer? Player { get; init; }
    public IPlayerProgress? Progress { get; init; }
    public AbilityAction? Fact { get; init; }
    public IWorldAccessor? World { get; init; }
}

/// <summary>Beehive kiln / stone coffin heat-structure damage skip fold.</summary>
public sealed class HeatStructureDamageContext : IHookContext
{
    public HookId Hook => HookIds.BlockInteraction;
    public IPlayer? Player { get; init; }
    public IPlayerProgress? Progress { get; init; }
    public AbilityAction? Fact { get; init; }
    public IWorldAccessor? World { get; init; }
}

/// <summary>Healing-item tend folds (First Aid health / application-rate).</summary>
public sealed class TendContext : IHookContext
{
    public HookId Hook => HookIds.ItemInteraction;
    public IPlayer? Player { get; init; }
    public IPlayerProgress? Progress { get; init; }
    public AbilityAction? Fact { get; init; }
    public IWorldAccessor? World { get; init; }
}

/// <summary>Post-revive triage folds (health fraction / duration seconds).</summary>
public sealed class ReviveContext : IHookContext
{
    public HookId Hook => HookIds.ItemInteraction;
    public IPlayer? Player { get; init; }
    public IPlayerProgress? Progress { get; init; }
    public AbilityAction? Fact { get; init; }
    public IWorldAccessor? World { get; init; }
}

/// <summary>Craft-grid recipe unlock gate (<c>recipe-available</c> / default).</summary>
public sealed class RecipeAvailableContext : IHookContext
{
    public HookId Hook => HookIds.CraftingInteraction;
    public IPlayer? Player { get; init; }
    public IPlayerProgress? Progress { get; init; }
    public AbilityAction? Fact { get; init; }
}
