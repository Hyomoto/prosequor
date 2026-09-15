namespace Prosequor.Ability.Hooks;

public readonly record struct HookId(string Value)
{
    public override string ToString() => Value;

    public static HookId Normalize(string raw)
    {
        string trimmed = raw.Trim();
        return new HookId(trimmed.Contains(':') ? trimmed : "prosequor:" + trimmed);
    }
}

public readonly record struct PhaseId(string Value)
{
    public override string ToString() => Value;

    public static PhaseId Normalize(string raw) => new(raw.Trim());
}

public readonly record struct ActionId(string Value)
{
    public override string ToString() => Value;

    public static ActionId Normalize(string raw)
    {
        string trimmed = raw.Trim();
        return new ActionId(trimmed.Contains(':') ? trimmed : "prosequor:" + trimmed);
    }
}

public readonly record struct VerbId(string Value)
{
    public override string ToString() => Value;

    public static VerbId Normalize(string raw)
    {
        string trimmed = raw.Trim();
        return new VerbId(trimmed.Contains(':') ? trimmed : "prosequor:" + trimmed);
    }
}

/// <summary>Built-in hook and phase identifiers.</summary>
public static class HookIds
{
    public static readonly HookId BlockInteraction = new("prosequor:block-interaction");
    public static readonly HookId ItemInteraction = new("prosequor:item-interaction");
    public static readonly HookId CraftingInteraction = new("prosequor:crafting-interaction");
    public static readonly HookId EntityInteraction = new("prosequor:entity-interaction");
    public static readonly HookId PlayerInteraction = new("prosequor:player-interaction");
    public static readonly HookId Progress = new("prosequor:progress");

    public static readonly PhaseId Default = new("default");
    public static readonly PhaseId Quantity = new("quantity");
    public static readonly PhaseId Output = new("output");
    /// <summary>Legacy list-pre phase id; mutate-drops no longer registers it.</summary>
    public static readonly PhaseId DropList = new("drops");
    /// <summary>Per-stack ItemStack fold under mutate-drops (after quantity).</summary>
    public static readonly PhaseId Stack = new("stack");
    public static readonly PhaseId Stacks = new("stacks");
    public static readonly PhaseId Amount = new("amount");
    public static readonly PhaseId Cap = new("cap");
    public static readonly PhaseId Size = new("size");
    public static readonly PhaseId AssistRadius = new("assist-radius");
    public static readonly PhaseId AutoFinish = new("auto-finish");
    public static readonly PhaseId PlaceConservation = new("place-conservation");
    public static readonly PhaseId Growth = new("growth");
    public static readonly PhaseId MoveSpeed = new("move-speed");
    public static readonly PhaseId CanRide = new("can-ride");
    public static readonly PhaseId SaddleBreak = new("saddle-break");
    public static readonly PhaseId HungerRate = new("hunger-rate");
    public static readonly PhaseId FallDamage = new("fall-damage");
    public static readonly PhaseId MeleeDamage = new("melee-damage");
    public static readonly PhaseId TurnSpeed = new("turn-speed");
    public static readonly PhaseId RatlineStamina = new("ratline-stamina");
    /// <summary>
    /// Response-rate fold under animal-flee / animal-seek (seed = vanilla ExecutionChance).
    /// Distinct from <see cref="HookIds.Chance"/> so husbandry flee-reduction can keep seed 0.
    /// </summary>
    public static readonly PhaseId Response = new("response");
    public static readonly PhaseId Chance = new("chance");
    public static readonly PhaseId Multiplier = new("multiplier");
    public static readonly PhaseId AddDurability = new("add-durability");
    public static readonly PhaseId Attributes = new("attributes");
    public static readonly PhaseId Refund = new("refund");
    public static readonly PhaseId Restock = new("restock");
    public static readonly PhaseId LastStand = new("last-stand");
    public static readonly PhaseId AllowRightClickHarvest = new("allow-right-click-harvest");
    public static readonly PhaseId RightClickHarvestBreakChance = new("right-click-harvest-break-chance");
    /// <summary>Anvil heavy-hit slag clear radius (seed −1 = off).</summary>
    public static readonly PhaseId SlagRadius = new("slag-radius");
    /// <summary>Anvil heavy-hit mastery move quota (seed 0).</summary>
    public static readonly PhaseId MoveCount = new("move-count");
    /// <summary>Metal Recovery: splits per refunded metal bit (seed 0 = off).</summary>
    public static readonly PhaseId BitsRefund = new("bits-refund");
    /// <summary>Heated Strikes: fraction of cooling debt shrunk per strike (seed 0 = off).</summary>
    public static readonly PhaseId DecayShrink = new("decay-shrink");
    /// <summary>Craft quality fold: roll window floor (seed = −200 + skill level).</summary>
    public static readonly PhaseId QualityBase = new("quality-base");
    /// <summary>Craft quality fold: roll window width (seed 200).</summary>
    public static readonly PhaseId QualityWindow = new("quality-window");
    /// <summary>Craft quality fold: how many rolls to take max of (seed 1).</summary>
    public static readonly PhaseId QualityRolls = new("quality-rolls");
    /// <summary>Craft quality fold: global bonus added to every quality rule (seed 0).</summary>
    public static readonly PhaseId QualityBonus = new("quality-bonus");
}

/// <summary>Built-in verb identifiers (moments on a surface).</summary>
public static class VerbIds
{
    public static readonly VerbId MutateDrops = new("prosequor:mutate-drops");
    public static readonly VerbId MutateProcess = new("prosequor:mutate-process");
    public static readonly VerbId MutateOutput = new("prosequor:mutate-output");
    /// <summary>Craft-time quality roll → attribute lerp + affix band.</summary>
    public static readonly VerbId ApplyQuality = new("prosequor:apply-quality");
    public static readonly VerbId InteractionSpeed = new("prosequor:interaction-speed");
    public static readonly VerbId Interact = new("prosequor:interact");
    public static readonly VerbId EstablishCutting = new("prosequor:establish-cutting");
    public static readonly VerbId SeekBobber = new("prosequor:seek-bobber");
    public static readonly VerbId PlantSapling = new("prosequor:plant-sapling");
    public static readonly VerbId PlantBushCutting = new("prosequor:plant-bush-cutting");
    public static readonly VerbId PlantCrop = new("prosequor:plant-crop");
    public static readonly VerbId Fertilize = new("prosequor:fertilize");
    public static readonly VerbId FieldWork = new("prosequor:field-work");
    public static readonly VerbId ScytheMultibreak = new("prosequor:scythe-multibreak");
    /// <summary>Portions placed per trough fill action (seed 1).</summary>
    public static readonly VerbId TroughFill = new("prosequor:trough-fill");
    /// <summary>Friendliness gain chance when an animal eats a trough portion (seed 0.05).</summary>
    public static readonly VerbId TroughEaten = new("prosequor:trough-eaten");
    /// <summary>Angry-bee spawn probability when breaking a populated skep.</summary>
    public static readonly VerbId SpawnBeesChance = new("prosequor:spawn-bees-chance");
    /// <summary>Right-click honey extract from a harvestable skep.</summary>
    public static readonly VerbId HarvestSkep = new("prosequor:harvest-skep");
    /// <summary>Sneak + right-click bloom extract from a finished bloomery.</summary>
    public static readonly VerbId HarvestBloomery = new("prosequor:harvest-bloomery");
    /// <summary>Anvil tool-mode 0 (heavy hit) assists: slag clear + recipe nudges.</summary>
    public static readonly VerbId AnvilHeavyHit = new("prosequor:anvil-heavy-hit");
    /// <summary>Anvil tool-mode split: Metal Recovery bit refund threshold.</summary>
    public static readonly VerbId AnvilSplit = new("prosequor:anvil-split");
    /// <summary>Anvil hammer strike (hit/upset/split): Heated Strikes cooling-debt shrink.</summary>
    public static readonly VerbId AnvilStrike = new("prosequor:anvil-strike");
    /// <summary>Item durability loss while breaking a block (<c>amount</c>).</summary>
    public static readonly VerbId BlockDamaged = new("prosequor:block-damaged");
    public static readonly VerbId ItemDamage = new("prosequor:item-damage");
    /// <summary>
    /// Item durability loss while consuming a craft-grid tool ingredient (<c>amount</c>).
    /// Distinct from XP activity <c>prosequor:craft</c> and last-craft remember verb.
    /// </summary>
    public static readonly VerbId CraftDamaged = new("prosequor:craft-damaged");
    public static readonly VerbId ConsumeBait = new("prosequor:consume-bait");
    public static readonly VerbId Repair = new("prosequor:repair");
    public static readonly VerbId ClayForm = new("prosequor:clay-form");
    public static readonly VerbId Mounted = new("prosequor:mounted");
    public static readonly VerbId AnimalFlee = new("prosequor:animal-flee");
    public static readonly VerbId AnimalSeek = new("prosequor:animal-seek");
    public static readonly VerbId AnimalMelee = new("prosequor:animal-melee");
    public static readonly VerbId AnimalBrood = new("prosequor:animal-brood");
    public static readonly VerbId AnimalMilk = new("prosequor:animal-milk");
    public static readonly VerbId AnimalPet = new("prosequor:animal-pet");
    public static readonly VerbId OnDamage = new("prosequor:on-damage");
    public static readonly VerbId CatEyes = new("prosequor:cat-eyes");
    public static readonly VerbId SkillXp = new("prosequor:skill-xp");
    public static readonly VerbId SkillBucket = new("prosequor:skill-bucket");
    public static readonly VerbId Health = new("prosequor:health");
    public static readonly VerbId Satiety = new("prosequor:satiety");
    public static readonly VerbId HungerDelay = new("prosequor:hunger-delay");
    public static readonly VerbId ArmorWalk = new("prosequor:armor-walk");
    public static readonly VerbId MeleeDamage = new("prosequor:melee-damage");
    public static readonly VerbId BasicSlots = new("prosequor:basic-slots");
    public static readonly VerbId RangedSpeed = new("prosequor:ranged-speed");
    public static readonly VerbId RangedAcc = new("prosequor:ranged-acc");
    public static readonly VerbId FallDamageFactor = new("prosequor:fall-damage-factor");
    public static readonly VerbId FallDamageThreshold = new("prosequor:fall-damage-threshold");
    public static readonly VerbId TemporalRecoverRate = new("prosequor:temporal-recover-rate");
    public static readonly VerbId TemporalDrainRate = new("prosequor:temporal-drain-rate");
    /// <summary>On-foot sprint speed bonus fraction (seed 0; station multiplies walk speed by 1 + fold).</summary>
    public static readonly VerbId SprintSpeed = new("prosequor:sprint-speed");
    /// <summary>Swim / feet-in-liquid speed bonus fraction (seed 0).</summary>
    public static readonly VerbId SwimSpeed = new("prosequor:swim-speed");
    /// <summary>On-foot sneak speed bonus fraction (seed 0).</summary>
    public static readonly VerbId SneakSpeed = new("prosequor:sneak-speed");
    public static readonly VerbId AnimalSeekingRange = new("prosequor:animal-seeking-range");
    public static readonly VerbId CritChance = new("prosequor:crit-chance");
    /// <summary>Intact cracked-vessel chance percent (vanilla <c>wholeVesselLootChance</c>).</summary>
    public static readonly VerbId WholeVesselLootChance = new("prosequor:whole-vessel-loot-chance");
    public static readonly VerbId VoxelCopy = new("prosequor:voxel-copy");
    public static readonly VerbId VoxelRefill = new("prosequor:voxel-refill");
}

/// <summary>Built-in action identifiers.</summary>
public static class ActionIds
{
    public static readonly ActionId Number = new("prosequor:number");
    public static readonly ActionId HasUnlock = new("prosequor:has-unlock");
    public static readonly ActionId IncreaseFreshness = new("prosequor:increase-freshness");
    public static readonly ActionId Chance = new("prosequor:chance");
    public static readonly ActionId AppendFromDropTable =
        new("prosequor:append-from-drop-table");
    public static readonly ActionId ReplaceFromDropTable =
        new("prosequor:replace-from-drop-table");
    public static readonly ActionId ReplaceMatchingStackWithBlock =
        new("prosequor:replace-matching-stack-with-block");
    public static readonly ActionId EnrichSoil = new("prosequor:enrich-soil");
    public static readonly ActionId AdjustPlantClimateValue =
        new("prosequor:adjust-plant-climate-value");
    public static readonly ActionId AddCropSeed = new("prosequor:add-crop-seed");
    public static readonly ActionId AllowMountedRideWithoutSaddle =
        new("prosequor:allow-mounted-ride-without-saddle");
    public static readonly ActionId AllowAnimalPet = new("prosequor:allow-animal-pet");
    public static readonly ActionId AddFriendliness = new("prosequor:add-friendliness");
    public static readonly ActionId SetTrue = new("prosequor:set-true");
    public static readonly ActionId ReplaceWithVariant =
        new("prosequor:replace-with-variant");
    public static readonly ActionId AddMappedNumber =
        new("prosequor:add-mapped-number");
    public static readonly ActionId ModifyAttribute =
        new("prosequor:modify-attribute");
    public static readonly ActionId AddAffix = new("prosequor:add-affix");
    public static readonly ActionId Quality = new("prosequor:quality");
    public static readonly ActionId QualityRank = new("prosequor:quality-rank");
    public static readonly ActionId RefundIngredients =
        new("prosequor:refund-ingredients");
    public static readonly ActionId RestockLastBait =
        new("prosequor:restock-last-bait");
    public static readonly ActionId RestoreConsumedBait =
        new("prosequor:restore-consumed-bait");
    public static readonly ActionId UpgradeOreGrade =
        new("prosequor:upgrade-ore-grade");
}
