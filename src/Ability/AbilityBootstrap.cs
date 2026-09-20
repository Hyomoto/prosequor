using Prosequor.Ability.Actions;
using Prosequor.Ability.Hooks;
using Prosequor.Data;
using Vintagestory.API.Common;
using Vintagestory.GameContent;

namespace Prosequor.Ability;

/// <summary>Registers built-in hooks, tags, and actions during mod Start.</summary>
public static class AbilityBootstrap
{
    public const string ClayTag = "clay";
    public const string ClayItemTag = "prosequor:clay-item";
    public const string PeatTag = "peat";
    public const string SaltpeterTag = "saltpeter";
    public const string CharcoalTag = "charcoal";
    public const string NuggetTag = "nugget";
    public const string MaterialSoilTag = "material:soil";
    public const string MaterialWoodTag = "material:wood";
    public const string MaterialStoneTag = "material:stone";
    public const string MaterialOreTag = "material:ore";
    public const string DirtTag = "dirt";
    public const string SoilTag = "soil";
    public const string StoneTag = "stone";
    public const string OreTag = "ore";
    public const string GemstoneTag = "gemstone";
    public const string CrystallizedOreTag = "crystalizedore";
    public const string WoodTag = "wood";
    public const string LeavesTag = "leaves";
    public const string PineTag = "pine";
    public const string SaplingTag = "sapling";
    public const string SeedTag = "seed";
    public const string CropTag = "crop";
    public const string MatureCropTag = "mature-crop";
    public const string ImmatureCropTag = "immature-crop";
    public const string BerryBushTag = "berry-bush";
    public const string FruitTreeTag = "fruit-tree";
    public const string FarmlandTag = "farmland";
    public const string ResinTag = "prosequor:resin";
    public const string ToolShovelTag = "tool:shovel";
    public const string ToolPickaxeTag = "tool:pickaxe";
    public const string ToolAxeTag = "tool:axe";
    public const string ToolFishingPoleTag = "tool:fishingpole";
    public const string ToolPanTag = "tool:pan";
    public const string WateringCanTag = "watering-can";
    public const string BombsTag = "bombs";
    public const string ToolSawTag = "tool:saw";
    public const string GravelTag = "gravel";
    public const string SandTag = "sand";
    public const string BonySoilTag = "bonysoil";
    public const string GraftTag = "graft";
    public const string RootTag = "root";

    public const string VerbEstablishCutting = "prosequor:establish-cutting";
    public const string VerbPlantSapling = "prosequor:plant-sapling";
    public const string VerbPlantBushCutting = "prosequor:plant-bush-cutting";
    public const string VerbPlantCrop = "prosequor:plant-crop";
    public const string VerbFertilize = "prosequor:fertilize";
    public const string VerbSeekBobber = "prosequor:seek-bobber";

    /// <summary>Fact token when seek chance comes from baited bobber seek.</summary>
    public const string TokenUsedBait = "used-bait";
    public const string VerbFieldWork = "prosequor:field-work";
    public const string VerbScytheMultibreak = "prosequor:scythe-multibreak";
    public const string VerbTroughFill = "prosequor:trough-fill";
    public const string VerbTroughEaten = "prosequor:trough-eaten";
    public const string VerbSpawnBeesChance = "prosequor:spawn-bees-chance";
    public const string VerbHarvestSkep = "prosequor:harvest-skep";
    public const string VerbHarvestBloomery = "prosequor:harvest-bloomery";
    public const string VerbClayForm = "prosequor:clay-form";
    public const string TokenFirePottery = "fire-pottery";
    public const string TokenBarrel = "barrel";
    public const string TokenFruitPress = "fruit-press";

    public const string StorageVesselTag = "storagevessel";
    public const string LeatherTag = "leather";
    public const string HideTag = "hide";
    public const string FlowerpotTag = "flowerpot";
    public const string ClayPlanterTag = "clayplanter";

    public const string OpPlaceTag = "op:place";
    public const string OpRemoveTag = "op:remove";
    public const string OpFinishTag = "op:finish";

    public const string FishSmallTag = FishClassification.SmallTag;
    public const string FishMediumTag = FishClassification.MediumTag;
    public const string FishLargeTag = FishClassification.LargeTag;
    public const string FishTag = FishClassification.FishTag;
    public const string FishFreshwaterTag = FishClassification.FreshwaterTag;
    public const string FishSaltwaterTag = FishClassification.SaltwaterTag;
    public const string FishReefTag = FishClassification.ReefTag;
    public const string FishAdultTag = FishClassification.AdultTag;
    public const string FishJuvenileTag = FishClassification.JuvenileTag;

    /// <summary>Craft-time attribute mutators (durability, later warmth / protection / …).</summary>
    public static CraftAttributeMutatorRegistry AttributeMutators { get; } = CreateAttributeMutators();

    static CraftAttributeMutatorRegistry CreateAttributeMutators()
    {
        CraftAttributeMutatorRegistry registry = new();
        registry.Register(new DurabilityAttributeMutator());
        registry.Register(new WarmthAttributeMutator());
        registry.Register(new CoolingAttributeMutator());
        registry.Register(new ProtectionAttributeMutator());
        registry.Register(new FreshnessAttributeMutator());
        registry.Register(new SatietyAttributeMutator());
        registry.Register(new HungerDelayAttributeMutator());
        registry.Register(new IntoxicationAttributeMutator());
        registry.Register(new PriceAttributeMutator());
        return registry;
    }

    public static void RegisterBuiltIns(
        IHookRegistry hooks,
        IAbilityActionRegistry actions,
        AffixListRegistry? affixLists = null)
    {
        AffixListRegistry lists = affixLists ?? new AffixListRegistry();
        RegisterSurfaceHooks(hooks);
        RegisterSurfaceActions(actions, lists);
    }

    static void RegisterSurfaceHooks(IHookRegistry hooks)
    {
        hooks.RegisterHook(HookIds.BlockInteraction);
        hooks.RegisterPhase(
            HookIds.BlockInteraction,
            VerbIds.MutateDrops,
            HookIds.Quantity,
            typeof(DropsContext),
            typeof(float));
        hooks.RegisterPhase(
            HookIds.BlockInteraction,
            VerbIds.MutateDrops,
            HookIds.Stack,
            typeof(DropsContext),
            typeof(ItemStack));
        hooks.RegisterPhase(
            HookIds.BlockInteraction,
            VerbIds.MutateDrops,
            HookIds.Stacks,
            typeof(DropsContext),
            typeof(IReadOnlyList<ItemStack>));
        hooks.RegisterPhase(
            HookIds.BlockInteraction,
            VerbIds.MutateProcess,
            HookIds.Quantity,
            typeof(MutateProcessContext),
            typeof(float));
        hooks.RegisterPhase(
            HookIds.BlockInteraction,
            VerbIds.MutateProcess,
            HookIds.Stacks,
            typeof(MutateProcessContext),
            typeof(IReadOnlyList<ItemStack>));
        hooks.RegisterPhase(
            HookIds.BlockInteraction,
            VerbIds.InteractionSpeed,
            HookIds.Default,
            typeof(InteractionSpeedContext),
            typeof(float));
        foreach (VerbId chanceVerb in new[]
                 {
                     VerbIds.PlantSapling,
                     VerbIds.PlantBushCutting,
                     VerbIds.EstablishCutting,
                     VerbIds.SeekBobber
                 })
        {
            hooks.RegisterPhase(
                HookIds.BlockInteraction,
                chanceVerb,
                HookIds.Default,
                typeof(SuccessChanceContext),
                typeof(float));
        }

        // Growth duration shares plant verbs; GrowthDurationContext at default.
        hooks.RegisterPhase(
            HookIds.BlockInteraction,
            VerbIds.PlantSapling,
            HookIds.Growth,
            typeof(GrowthDurationContext),
            typeof(float));
        hooks.RegisterPhase(
            HookIds.BlockInteraction,
            VerbIds.PlantBushCutting,
            HookIds.Growth,
            typeof(GrowthDurationContext),
            typeof(float));
        hooks.RegisterPhase(
            HookIds.BlockInteraction,
            VerbIds.Fertilize,
            HookIds.Default,
            typeof(FertilizerAbsorbContext),
            typeof(float));
        hooks.RegisterPhase(
            HookIds.BlockInteraction,
            VerbIds.PlantCrop,
            HookIds.Default,
            typeof(PlantCropClimateContext),
            typeof(float));
        hooks.RegisterPhase(
            HookIds.BlockInteraction,
            VerbIds.FieldWork,
            HookIds.Size,
            typeof(FieldWorkContext),
            typeof(int));
        hooks.RegisterPhase(
            HookIds.BlockInteraction,
            VerbIds.ScytheMultibreak,
            HookIds.Quantity,
            typeof(ScytheMultiBreakContext),
            typeof(int));
        hooks.RegisterPhase(
            HookIds.BlockInteraction,
            VerbIds.TroughFill,
            HookIds.Quantity,
            typeof(TroughFillContext),
            typeof(int));
        hooks.RegisterPhase(
            HookIds.BlockInteraction,
            VerbIds.SpawnBeesChance,
            HookIds.Default,
            typeof(SkepBeeSpawnContext),
            typeof(float));
        hooks.RegisterPhase(
            HookIds.BlockInteraction,
            VerbIds.HarvestSkep,
            HookIds.AllowRightClickHarvest,
            typeof(SkepHarvestContext),
            typeof(int));
        hooks.RegisterPhase(
            HookIds.BlockInteraction,
            VerbIds.HarvestSkep,
            HookIds.RightClickHarvestBreakChance,
            typeof(SkepHarvestContext),
            typeof(float));
        hooks.RegisterPhase(
            HookIds.BlockInteraction,
            VerbIds.HarvestBloomery,
            HookIds.AllowRightClickHarvest,
            typeof(BloomeryHarvestContext),
            typeof(int));
        hooks.RegisterPhase(
            HookIds.BlockInteraction,
            VerbIds.HarvestBloomery,
            HookIds.RightClickHarvestBreakChance,
            typeof(BloomeryHarvestContext),
            typeof(float));

        hooks.RegisterHook(HookIds.ItemInteraction);
        hooks.RegisterPhase(
            HookIds.ItemInteraction,
            VerbIds.MutateDrops,
            HookIds.Quantity,
            typeof(DropsContext),
            typeof(float));
        hooks.RegisterPhase(
            HookIds.ItemInteraction,
            VerbIds.MutateDrops,
            HookIds.Stack,
            typeof(DropsContext),
            typeof(ItemStack));
        hooks.RegisterPhase(
            HookIds.ItemInteraction,
            VerbIds.MutateDrops,
            HookIds.Stacks,
            typeof(DropsContext),
            typeof(IReadOnlyList<ItemStack>));
        hooks.RegisterPhase(
            HookIds.ItemInteraction,
            VerbIds.InteractionSpeed,
            HookIds.Default,
            typeof(InteractionSpeedContext),
            typeof(float));
        foreach (VerbId durabilityVerb in new[]
                 {
                     VerbIds.BlockDamaged,
                     VerbIds.ItemDamage,
                     VerbIds.CraftDamaged
                 })
        {
            hooks.RegisterPhase(
                HookIds.ItemInteraction,
                durabilityVerb,
                HookIds.Amount,
                typeof(ItemDurabilityContext),
                typeof(int));
        }

        hooks.RegisterPhase(
            HookIds.ItemInteraction,
            VerbIds.ConsumeBait,
            HookIds.Restock,
            typeof(ConsumeBaitContext),
            typeof(int));
        hooks.RegisterPhase(
            HookIds.ItemInteraction,
            VerbIds.Repair,
            HookIds.AddDurability,
            typeof(RepairContext),
            typeof(float));
        hooks.RegisterPhase(
            HookIds.ItemInteraction,
            VerbIds.ClayForm,
            HookIds.AssistRadius,
            typeof(VoxelWorkContext),
            typeof(int));
        hooks.RegisterPhase(
            HookIds.ItemInteraction,
            VerbIds.ClayForm,
            HookIds.AutoFinish,
            typeof(VoxelWorkContext),
            typeof(float));
        hooks.RegisterPhase(
            HookIds.ItemInteraction,
            VerbIds.ClayForm,
            HookIds.PlaceConservation,
            typeof(VoxelWorkContext),
            typeof(float));
        foreach (PhaseId anvilPhase in new[]
                 {
                     HookIds.SlagRadius,
                     HookIds.AssistRadius,
                     HookIds.MoveCount
                 })
        {
            hooks.RegisterPhase(
                HookIds.ItemInteraction,
                VerbIds.AnvilHeavyHit,
                anvilPhase,
                typeof(VoxelWorkContext),
                typeof(int));
        }

        hooks.RegisterPhase(
            HookIds.ItemInteraction,
            VerbIds.AnvilSplit,
            HookIds.BitsRefund,
            typeof(VoxelWorkContext),
            typeof(int));

        hooks.RegisterPhase(
            HookIds.ItemInteraction,
            VerbIds.AnvilStrike,
            HookIds.DecayShrink,
            typeof(VoxelWorkContext),
            typeof(float));

        hooks.RegisterPhase(
            HookIds.ItemInteraction,
            VerbIds.VoxelCopy,
            HookIds.Default,
            typeof(VoxelWorkContext),
            typeof(int));
        hooks.RegisterPhase(
            HookIds.ItemInteraction,
            VerbIds.VoxelRefill,
            HookIds.Default,
            typeof(VoxelWorkContext),
            typeof(int));

        hooks.RegisterHook(HookIds.CraftingInteraction);
        hooks.RegisterPhase(
            HookIds.CraftingInteraction,
            VerbIds.MutateOutput,
            HookIds.Quantity,
            typeof(CraftMutateOutputContext),
            typeof(float));
        hooks.RegisterPhase(
            HookIds.CraftingInteraction,
            VerbIds.MutateOutput,
            HookIds.Output,
            typeof(CraftMutateOutputContext),
            typeof(ItemStack));
        hooks.RegisterPhase(
            HookIds.CraftingInteraction,
            VerbIds.MutateOutput,
            HookIds.Attributes,
            typeof(CraftMutateOutputContext),
            typeof(ItemStack));
        hooks.RegisterPhase(
            HookIds.CraftingInteraction,
            VerbIds.MutateOutput,
            HookIds.Refund,
            typeof(CraftMutateOutputContext),
            typeof(int));

        foreach (PhaseId qualityPhase in new[]
                 {
                     HookIds.QualityBase,
                     HookIds.QualityWindow,
                     HookIds.QualityRolls,
                     HookIds.QualityBonus
                 })
        {
            hooks.RegisterPhase(
                HookIds.CraftingInteraction,
                VerbIds.ApplyQuality,
                qualityPhase,
                typeof(CraftMutateOutputContext),
                typeof(float));
        }

        hooks.RegisterPhase(
            HookIds.CraftingInteraction,
            VerbIds.ApplyQuality,
            HookIds.Attributes,
            typeof(CraftMutateOutputContext),
            typeof(ItemStack));

        hooks.RegisterHook(HookIds.EntityInteraction);
        foreach (PhaseId mountedPhase in new[]
                 {
                     HookIds.MoveSpeed,
                     HookIds.CanRide,
                     HookIds.SaddleBreak,
                     HookIds.HungerRate,
                     HookIds.FallDamage,
                     HookIds.MeleeDamage,
                     HookIds.TurnSpeed,
                     HookIds.RatlineStamina
                 })
        {
            hooks.RegisterPhase(
                HookIds.EntityInteraction,
                VerbIds.Mounted,
                mountedPhase,
                typeof(MountedContext),
                typeof(float));
        }

        foreach (VerbId animalVerb in new[]
                 {
                     VerbIds.AnimalFlee,
                     VerbIds.AnimalSeek,
                     VerbIds.AnimalMelee,
                     VerbIds.AnimalBrood,
                     VerbIds.AnimalMilk
                 })
        {
            hooks.RegisterPhase(
                HookIds.EntityInteraction,
                animalVerb,
                HookIds.Chance,
                typeof(AnimalBehaviorContext),
                typeof(float));
            hooks.RegisterPhase(
                HookIds.EntityInteraction,
                animalVerb,
                HookIds.Multiplier,
                typeof(AnimalBehaviorContext),
                typeof(int));
        }

        // Inconspicuity response-rate: scales vanilla ExecutionChance (seed = chance).
        // Kept off chance so husbandry flee-reduction (seed 0) does not share the fold.
        foreach (VerbId responseVerb in new[] { VerbIds.AnimalFlee, VerbIds.AnimalSeek })
        {
            hooks.RegisterPhase(
                HookIds.EntityInteraction,
                responseVerb,
                new PhaseId("response"),
                typeof(AnimalBehaviorContext),
                typeof(float));
        }

        hooks.RegisterPhase(
            HookIds.EntityInteraction,
            VerbIds.AnimalPet,
            HookIds.Default,
            typeof(AnimalBehaviorContext),
            typeof(int));
        hooks.RegisterPhase(
            HookIds.EntityInteraction,
            VerbIds.TroughEaten,
            HookIds.Chance,
            typeof(AnimalBehaviorContext),
            typeof(float));
        hooks.RegisterPhase(
            HookIds.EntityInteraction,
            VerbIds.MutateDrops,
            HookIds.Quantity,
            typeof(DropsContext),
            typeof(float));
        hooks.RegisterPhase(
            HookIds.EntityInteraction,
            VerbIds.MutateDrops,
            HookIds.Stack,
            typeof(DropsContext),
            typeof(ItemStack));
        hooks.RegisterPhase(
            HookIds.EntityInteraction,
            VerbIds.MutateDrops,
            HookIds.Stacks,
            typeof(DropsContext),
            typeof(IReadOnlyList<ItemStack>));

        hooks.RegisterHook(HookIds.PlayerInteraction);
        foreach (VerbId playerVerb in new[]
                 {
                     VerbIds.Health,
                     VerbIds.Satiety,
                     VerbIds.HungerDelay,
                     VerbIds.ArmorWalk,
                     VerbIds.MeleeDamage,
                     VerbIds.BasicSlots,
                     VerbIds.RangedSpeed,
                     VerbIds.RangedAcc,
                     VerbIds.FallDamageFactor,
                     VerbIds.FallDamageThreshold,
                     VerbIds.TemporalRecoverRate,
                     VerbIds.TemporalDrainRate,
                     VerbIds.AnimalSeekingRange,
                     VerbIds.CritChance,
                     VerbIds.WholeVesselLootChance
                 })
        {
            hooks.RegisterPhase(
                HookIds.PlayerInteraction,
                playerVerb,
                HookIds.Default,
                typeof(PlayerInteractionContext),
                typeof(int));
        }

        foreach (VerbId speedVerb in new[]
                 {
                     VerbIds.SprintSpeed,
                     VerbIds.SwimSpeed,
                     VerbIds.SneakSpeed
                 })
        {
            hooks.RegisterPhase(
                HookIds.PlayerInteraction,
                speedVerb,
                HookIds.Default,
                typeof(PlayerInteractionContext),
                typeof(float));
        }

        hooks.RegisterPhase(
            HookIds.PlayerInteraction,
            VerbIds.CatEyes,
            HookIds.Default,
            typeof(CatEyesContext),
            typeof(float));
        hooks.RegisterPhase(
            HookIds.PlayerInteraction,
            VerbIds.OnDamage,
            HookIds.Amount,
            typeof(TakeDamageContext),
            typeof(float));
        hooks.RegisterPhase(
            HookIds.PlayerInteraction,
            VerbIds.OnDamage,
            HookIds.LastStand,
            typeof(TakeDamageContext),
            typeof(float));

        hooks.RegisterHook(HookIds.Progress);
        hooks.RegisterPhase(
            HookIds.Progress,
            VerbIds.SkillXp,
            HookIds.Amount,
            typeof(SkillXpContext),
            typeof(float));
        hooks.RegisterPhase(
            HookIds.Progress,
            VerbIds.SkillBucket,
            HookIds.Cap,
            typeof(SkillBucketCapContext),
            typeof(float));
    }

    static void RegisterSurfaceActions(IAbilityActionRegistry actions, AffixListRegistry lists)
    {
        actions.Register(new AllowMountedRideWithoutSaddleAction());
        actions.Register(new AllowAnimalPetAction());
        actions.Register(new AddFriendlinessAction());
        actions.Register(new SetTrueHarvestSkepAllowAction());
        actions.Register(new SetTrueHarvestBloomeryAllowAction());
        actions.Register(new ModifyAttributeMutateOutputAction());
        actions.Register(new AddAffixMutateOutputAction(lists));
        actions.Register(new ApplyQualityAction(lists));
        actions.Register(new ApplyQualityRankAction(lists));
        foreach (PhaseId qualityPhase in new[]
                 {
                     HookIds.QualityBase,
                     HookIds.QualityWindow,
                     HookIds.QualityRolls,
                     HookIds.QualityBonus
                 })
        {
            actions.Register(new NumberApplyQualityAction(qualityPhase));
        }
        foreach (VerbId animalVerb in new[]
                 {
                     VerbIds.AnimalFlee,
                     VerbIds.AnimalSeek,
                     VerbIds.AnimalMelee,
                     VerbIds.AnimalBrood,
                     VerbIds.AnimalMilk
                 })
        {
            actions.Register(new NumberAnimalBehaviorMultiplierAction(animalVerb));
            actions.Register(new NumberAnimalBehaviorChanceAction(animalVerb));
        }

        actions.Register(new RefundIngredientsOnCraftAction());
        actions.Register(new RestoreConsumedBaitOnBaitAction());
        actions.Register(new RestockLastBaitOnBaitAction());
        actions.Register(new NumberDropsQuantityAction());
        actions.Register(new NumberItemMutateDropsQuantityAction());
        actions.Register(new NumberEntityMutateDropsQuantityAction());
        actions.Register(new NumberCraftQuantityAction());
        actions.Register(new NumberProcessQuantityAction());
        actions.Register(new NumberInteractionSpeedAction());
        actions.Register(new NumberItemInteractionSpeedAction());
        actions.Register(new NumberRepairAddDurabilityAction());
        actions.Register(new NumberClayFormAction(HookIds.AutoFinish));
        actions.Register(new NumberClayFormAction(HookIds.PlaceConservation));
        actions.Register(new NumberClayFormAssistRadiusAction());
        foreach (PhaseId anvilPhase in new[]
                 {
                     HookIds.SlagRadius,
                     HookIds.AssistRadius,
                     HookIds.MoveCount
                 })
        {
            actions.Register(new NumberAnvilHeavyHitAction(anvilPhase));
        }

        actions.Register(new NumberAnvilSplitAction());
        actions.Register(new NumberAnvilStrikeDecayShrinkAction());

        actions.Register(new NumberVoxelWorkDefaultAction(VerbIds.VoxelCopy));
        actions.Register(new NumberVoxelWorkDefaultAction(VerbIds.VoxelRefill));
        actions.Register(new NumberFertilizeAction());
        actions.Register(new AdjustPlantClimateValueAction());
        actions.Register(new NumberFieldWorkSizeAction());
        actions.Register(new NumberScytheMultiBreakQuantityAction());
        actions.Register(new NumberTroughFillQuantityAction());
        actions.Register(new NumberAnimalBehaviorChanceAction(VerbIds.TroughEaten));
        actions.Register(new NumberSpawnBeesChanceAction());
        actions.Register(new NumberHarvestSkepBreakChanceAction());
        actions.Register(new NumberHarvestBloomeryBreakChanceAction());
        foreach (PhaseId mountedPhase in new[]
                 {
                     HookIds.MoveSpeed,
                     HookIds.TurnSpeed,
                     HookIds.SaddleBreak,
                     HookIds.HungerRate,
                     HookIds.FallDamage,
                     HookIds.MeleeDamage,
                     HookIds.RatlineStamina
                 })
        {
            actions.Register(new NumberMountedAction(mountedPhase));
        }

        foreach (VerbId chanceVerb in new[]
                 {
                     VerbIds.PlantSapling,
                     VerbIds.PlantBushCutting,
                     VerbIds.EstablishCutting,
                     VerbIds.SeekBobber
                 })
        {
            actions.Register(new NumberSuccessChanceAction(chanceVerb));
        }

        actions.Register(new NumberGrowthDurationAction(VerbIds.PlantSapling));
        actions.Register(new NumberGrowthDurationAction(VerbIds.PlantBushCutting));
        actions.Register(new NumberCraftDamagedAmountAction());
        actions.Register(new NumberItemDurabilityAmountAction(VerbIds.BlockDamaged));
        actions.Register(new NumberItemDurabilityAmountAction(VerbIds.ItemDamage));
        actions.Register(new HasUnlockDropsQuantityAction(actions));
        actions.Register(new HasUnlockDropsStackAction(actions));
        actions.Register(new HasUnlockDropsStacksAction(actions));
        actions.Register(new IncreaseFreshnessStackAction());
        actions.Register(new IncreaseFreshnessItemStackAction());
        actions.Register(new UpgradeOreGradeOnDropsStackAction());
        actions.Register(new ReplaceMatchingStackWithBlockStackAction());
        actions.Register(new AppendFromDropTableBlockAction());
        actions.Register(new AppendFromDropTableItemAction());
        actions.Register(new ReplaceFromDropTableBlockAction());
        actions.Register(new ReplaceFromDropTableItemAction());
        actions.Register(new EnrichSoilAction());
        actions.Register(new AddCropSeedAction());
        actions.Register(new NumberSkillXpAmountAction());
        actions.Register(new NumberSkillBucketCapAction());
        actions.Register(new ReplaceWithVariantAction());
        actions.Register(new ChanceDropsQuantityAction(actions));
        actions.Register(new ChanceItemMutateDropsQuantityAction(actions));
        actions.Register(new ChanceDropsStackAction(actions));
        foreach (VerbId durabilityVerb in new[]
                 {
                     VerbIds.BlockDamaged,
                     VerbIds.ItemDamage,
                     VerbIds.CraftDamaged
                 })
        {
            actions.Register(new ChanceItemUsageAmountAction(actions, durabilityVerb));
        }

        actions.Register(new ChanceOnBaitRestockAction(actions));
        actions.Register(new ChanceAnimalPetDefaultAction(actions));
        actions.Register(new ChanceDropsStacksAction(actions));
        actions.Register(new ChanceItemMutateDropsStacksAction(actions));
        actions.Register(new ChanceMutateProcessStacksAction(actions));
        foreach (VerbId mapped in new[]
                 {
                     VerbIds.Health,
                     VerbIds.Satiety,
                     VerbIds.HungerDelay,
                     VerbIds.ArmorWalk,
                     VerbIds.MeleeDamage,
                     VerbIds.BasicSlots,
                     VerbIds.RangedSpeed,
                     VerbIds.RangedAcc,
                     VerbIds.FallDamageFactor,
                     VerbIds.FallDamageThreshold,
                     VerbIds.TemporalRecoverRate,
                     VerbIds.TemporalDrainRate,
                     VerbIds.AnimalSeekingRange,
                     VerbIds.CritChance,
                     VerbIds.WholeVesselLootChance
                 })
        {
            actions.Register(new AddMappedNumberIntAction(mapped));
        }

        actions.Register(new NumberPlayerInteractionIntAction(VerbIds.TemporalRecoverRate));
        actions.Register(new NumberPlayerInteractionIntAction(VerbIds.TemporalDrainRate));
        foreach (VerbId speedVerb in new[]
                 {
                     VerbIds.SprintSpeed,
                     VerbIds.SwimSpeed,
                     VerbIds.SneakSpeed
                 })
        {
            actions.Register(new NumberPlayerInteractionFloatAction(speedVerb));
        }

        actions.Register(new AddMappedNumberFloatAction(VerbIds.CatEyes));
        actions.Register(new AddMappedNumberOnDamageAction(HookIds.Amount));
        actions.Register(new AddMappedNumberOnDamageAction(HookIds.LastStand));
        actions.Register(new AddMappedNumberAnimalResponseAction(VerbIds.AnimalFlee));
        actions.Register(new AddMappedNumberAnimalResponseAction(VerbIds.AnimalSeek));
    }

    /// <summary>
    /// Populates built-in collection membership from world classifiers.
    /// Explicit output pools are loaded via <see cref="Data.OutputPoolRegistry"/>.
    /// Target collections and output-pool collections stay separate: e.g. ores hosted
    /// in claystone must never become Mixed Clay outputs.
    /// </summary>
    public static void FillBuiltinCollections(ICoreAPI api, CollectionIndex index)
    {
        FillFromWorld(
            api,
            addBlock: (tag, block) =>
            {
                index.EnsureKey(tag);
                index.AddCode(tag, block.Code?.ToString());
            },
            addItem: (tag, item) =>
            {
                index.EnsureKey(tag);
                index.AddCode(tag, item.Code?.ToString());
            });

        FillMetalCraftsCollection(api, index);

        // Membership filled at GameReady from clayforming / smithing recipe outputs.
        index.EnsureKey("clay-formed");
        index.EnsureKey("smithing-formed");
    }

    /// <summary>
    /// <c>tool</c> ∪ <c>weapon</c> whose path contains a known metal code
    /// (<see cref="SurvivalCoreSystem.metalsByCode"/>). Excludes flint/chert/bows/clubs.
    /// </summary>
    static void FillMetalCraftsCollection(ICoreAPI api, CollectionIndex index)
    {
        const string id = "metal-crafts";
        index.EnsureKey(id);

        HashSet<string>? metals = api?.ModLoader
            .GetModSystem<SurvivalCoreSystem>(true)
            ?.metalsByCode
            ?.Keys
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (metals == null || metals.Count == 0)
        {
            return;
        }

        foreach (string code in index.Codes("tool").Concat(index.Codes("weapon")))
        {
            if (string.IsNullOrWhiteSpace(code) || !PathHasMetalToken(code, metals))
            {
                continue;
            }

            index.AddCode(id, code);
        }
    }

    static bool PathHasMetalToken(string code, HashSet<string> metals)
    {
        int colon = code.IndexOf(':');
        string path = colon >= 0 && colon + 1 < code.Length ? code[(colon + 1)..] : code;
        string[] parts = path.Split('-', StringSplitOptions.RemoveEmptyEntries);
        for (int i = 0; i < parts.Length; i++)
        {
            if (metals.Contains(parts[i]))
            {
                return true;
            }
        }

        return false;
    }

    static void FillFromWorld(
        ICoreAPI api,
        Action<string, Block> addBlock,
        Action<string, Item> addItem)
    {
        foreach (Item item in api.World.Items)
        {
            if (item?.Code == null || item.Id == 0)
            {
                continue;
            }

            if (item is ItemTreeSeed)
            {
                addItem(SaplingTag, item);
            }

            if (IsSeedItem(item))
            {
                addItem(SeedTag, item);
            }

            if (IsCrystallizedOreItemPath(item.Code.Path))
            {
                addItem(CrystallizedOreTag, item);
            }

            if (IsNuggetItem(item))
            {
                addItem(NuggetTag, item);
            }

            if (IsStoneItemPath(item.Code.Path))
            {
                addItem(StoneTag, item);
            }

            AddToolCollections(item, addItem);
            AddThreadClothCollections(item, addItem);
            AddLeatherCollections(item, addItem);
            AddHideCollections(item, addItem);
            AddWearableCollections(item, addItem);
            AddFishCollections(item, addItem);
        }

        int pineBlocks = 0;
        int leavesBlocks = 0;
        int woodBlocks = 0;
        int saplingBlocks = 0;
        int cropBlocks = 0;
        int matureCropBlocks = 0;
        int immatureCropBlocks = 0;
        int berryBushBlocks = 0;
        int fruitTreeBlocks = 0;
        int stoneBlocks = 0;
        int oreBlocks = 0;
        int gemstoneBlocks = 0;

        foreach (Block block in api.World.Blocks)
        {
            if (block?.Code == null || block.Id == 0)
            {
                continue;
            }

            string path = block.Code.Path;
            if (IsRawClayBlockPath(path))
            {
                addBlock(ClayTag, block);
            }

            if (IsPeatBlockPath(path))
            {
                addBlock(PeatTag, block);
            }

            if (IsSaltpeterBlockPath(path))
            {
                addBlock(SaltpeterTag, block);
            }

            if (IsCharcoalBlockPath(path))
            {
                addBlock(CharcoalTag, block);
            }

            if (block is BlockSapling)
            {
                addBlock(SaplingTag, block);
                saplingBlocks++;
            }

            if (IsCropBlock(block))
            {
                addBlock(CropTag, block);
                cropBlocks++;
                if (IsMatureCrop(block))
                {
                    addBlock(MatureCropTag, block);
                    matureCropBlocks++;
                }
                else
                {
                    addBlock(ImmatureCropTag, block);
                    immatureCropBlocks++;
                }
            }

            if (IsBerryBushBlock(block))
            {
                addBlock(BerryBushTag, block);
                berryBushBlocks++;
            }

            if (ForageBlocks.IsSap(block))
            {
                addBlock("sap", block);
            }

            if (IsFruitTreeBlock(block))
            {
                addBlock(FruitTreeTag, block);
                fruitTreeBlocks++;
            }

            if (IsFarmlandBlock(block))
            {
                addBlock(FarmlandTag, block);
            }

            if (block.BlockMaterial == EnumBlockMaterial.Leaves)
            {
                addBlock(LeavesTag, block);
                leavesBlocks++;
            }

            if ((block.BlockMaterial == EnumBlockMaterial.Soil
                || BlockBreakClassification.IsDiggableTerrain(block))
                && !IsPeatBlockPath(path)
                && !IsSandPath(path)
                && !IsGravelPath(path)
                && block.BlockMaterial is not (EnumBlockMaterial.Sand or EnumBlockMaterial.Gravel))
            {
                addBlock(SoilTag, block);
                addBlock(DirtTag, block);
            }

            if (block.BlockMaterial == EnumBlockMaterial.Wood
                || BlockBreakClassification.IsFellingWood(block))
            {
                addBlock(WoodTag, block);
                if (BlockBreakClassification.IsFellingWood(block))
                {
                    woodBlocks++;
                    if (BlockBreakClassification.IsPineWood(block))
                    {
                        addBlock(PineTag, block);
                        pineBlocks++;
                    }
                }
            }

            if (IsGravelPath(path))
            {
                addBlock(GravelTag, block);
            }

            if (IsSandPath(path))
            {
                addBlock(SandTag, block);
            }

            if (IsBonySoilPath(path))
            {
                addBlock(BonySoilTag, block);
            }

            if (block.BlockMaterial == EnumBlockMaterial.Stone)
            {
                addBlock(StoneTag, block);
                stoneBlocks++;
            }
            else if (block.BlockMaterial == EnumBlockMaterial.Ore)
            {
                if (IsGemstoneOre(block))
                {
                    addBlock(GemstoneTag, block);
                    gemstoneBlocks++;
                }
                else
                {
                    addBlock(OreTag, block);
                    oreBlocks++;
                }
            }

            AddToolCollections(block, (tag, collectible) =>
            {
                if (collectible is Block b)
                {
                    addBlock(tag, b);
                }
            });

            // Linen (and cloth) bolts are blocks; the item classifier never sees them.
            if (path.StartsWith("cloth-", StringComparison.OrdinalIgnoreCase)
                || path.StartsWith("linen-", StringComparison.OrdinalIgnoreCase))
            {
                addBlock(CraftMutateOutputStation.TagCloth, block);
            }
        }

        api.Logger.Notification(
            "[prosequor] Builtin collections: wood {0}, pine {1}, leaves {2}, sapling {3}, crop {4} ({5} mature / {6} immature), berry-bush {7}, fruit-tree {8}, stone {9}, ore {10}, gemstone {11}.",
            woodBlocks,
            pineBlocks,
            leavesBlocks,
            saplingBlocks,
            cropBlocks,
            matureCropBlocks,
            immatureCropBlocks,
            berryBushBlocks,
            fruitTreeBlocks,
            stoneBlocks,
            oreBlocks,
            gemstoneBlocks);
    }

    static void AddToolCollections(CollectibleObject collectible, Action<string, Item> addItem)
    {
        if (collectible is not Item item)
        {
            return;
        }

        EnumTool? tool = item.Tool;
        switch (tool)
        {
            case EnumTool.Shovel:
                addItem("shovel", item);
                addItem("tool", item);
                break;
            case EnumTool.Axe:
                addItem("axe", item);
                addItem("tool", item);
                break;
            case EnumTool.Pickaxe:
                addItem("pickaxe", item);
                addItem("tool", item);
                break;
            case EnumTool.Saw:
                addItem("saw", item);
                addItem("tool", item);
                break;
            case EnumTool.Hammer:
                addItem("hammer", item);
                addItem("tool", item);
                break;
            case EnumTool.Hoe:
                addItem("hoe", item);
                addItem("tool", item);
                break;
            case EnumTool.Chisel:
            case EnumTool.Shears:
            case EnumTool.Wrench:
            case EnumTool.Scythe:
            case EnumTool.Sickle:
            case EnumTool.Probe:
                addItem("tool", item);
                break;
            case EnumTool.Sword:
            case EnumTool.Spear:
            case EnumTool.Bow:
            case EnumTool.Sling:
            case EnumTool.Club:
                addItem("weapon", item);
                break;
            case EnumTool.Knife:
                addItem("knife", item);
                addItem("weapon", item);
                break;
        }

        if (item is ItemFishingPole)
        {
            addItem("fishingpole", item);
        }
    }

    static void AddToolCollections(Block block, Action<string, CollectibleObject> add)
    {
        if (block is BlockPan)
        {
            add("pan", block);
        }

        if (block is BlockWateringCan)
        {
            add(WateringCanTag, block);
        }

        if (block is BlockBomb)
        {
            add(BombsTag, block);
        }
    }

    static void AddThreadClothCollections(Item item, Action<string, Item> addItem)
    {
        string? path = item.Code?.Path;
        if (string.IsNullOrEmpty(path))
        {
            return;
        }

        if (path.Equals("flaxtwine", StringComparison.OrdinalIgnoreCase)
            || path.Contains("twine", StringComparison.OrdinalIgnoreCase)
            || path.Contains("thread", StringComparison.OrdinalIgnoreCase))
        {
            addItem(CraftMutateOutputStation.TagThread, item);
        }

        if (path.StartsWith("cloth-", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("linen-", StringComparison.OrdinalIgnoreCase))
        {
            addItem(CraftMutateOutputStation.TagCloth, item);
        }
    }

    static void AddLeatherCollections(Item item, Action<string, Item> addItem)
    {
        string? path = item.Code?.Path;
        if (string.IsNullOrEmpty(path))
        {
            return;
        }

        if (path.StartsWith("leather", StringComparison.OrdinalIgnoreCase))
        {
            addItem(LeatherTag, item);
        }
    }

    static void AddHideCollections(Item item, Action<string, Item> addItem)
    {
        string? path = item.Code?.Path;
        if (string.IsNullOrEmpty(path))
        {
            return;
        }

        if (path.StartsWith("hide-", StringComparison.OrdinalIgnoreCase))
        {
            addItem(HideTag, item);
        }
    }

    static void AddWearableCollections(Item item, Action<string, Item> addItem)
    {
        HashSet<string> tags = LastCraftStation.ClassifyProductTags(item.Code?.Path);
        foreach (string tag in tags)
        {
            addItem(tag, item);
        }
    }

    static void AddFishCollections(Item item, Action<string, Item> addItem)
    {
        if (!FishClassification.IsFishItem(item))
        {
            return;
        }

        addItem(FishTag, item);
        ItemStack probe = new(item);
        HashSet<string> tags = new(StringComparer.OrdinalIgnoreCase);
        FishClassification.AddContextualTags(probe, tags);
        foreach (string tag in tags)
        {
            addItem(tag, item);
        }
    }

    /// <summary>Crop plant blocks (farming harvest targets).</summary>
    internal static bool IsCropBlock(Block block) =>
        block != null && (block.CropProps != null || block is BlockCrop);

    /// <summary>Legacy <see cref="BlockBerryBush"/> or modern fruiting-bush blocks.</summary>
    internal static bool IsBerryBushBlock(Block block)
    {
        if (block == null || block.Id == 0)
        {
            return false;
        }

        if (block is BlockBerryBush || block.GetBehavior<BlockBehaviorFruitingBush>() != null)
        {
            return true;
        }

        string? path = block.Code?.Path;
        return path != null
            && path.StartsWith("fruitingbush", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Fruit-tree branch / foliage blocks.</summary>
    internal static bool IsFruitTreeBlock(Block block)
    {
        if (block == null || block.Id == 0)
        {
            return false;
        }

        if (block is BlockFruitTreeBranch || block is BlockFruitTreeFoliage)
        {
            return true;
        }

        string? path = block.Code?.Path;
        return path != null
            && (path.StartsWith("fruittree", StringComparison.OrdinalIgnoreCase)
                || path.StartsWith("fruit-tree", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Tilled farmland blocks.</summary>
    internal static bool IsFarmlandBlock(Block block)
    {
        if (block == null || block.Id == 0)
        {
            return false;
        }

        if (block is BlockFarmland)
        {
            return true;
        }

        string? path = block.Code?.Path;
        return path != null
            && path.StartsWith("farmland", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>True when the crop is at its final growth stage.</summary>
    internal static bool IsMatureCrop(Block block)
    {
        if (!IsCropBlock(block) || block.CropProps == null)
        {
            return false;
        }

        int stage = GetCropStage(block);
        return stage >= 0 && stage >= block.CropProps.GrowthStages;
    }

    /// <summary>
    /// Resolves <c>seeds-{type}</c> for a crop block from its <c>type</c> variant
    /// (fallback: code path between the first and last hyphen, matching vanilla naming).
    /// </summary>
    internal static Item? TryResolveCropSeed(IWorldAccessor world, Block? block)
    {
        if (world == null || block?.Code == null)
        {
            return null;
        }

        string? cropType = null;
        if (block.Variant != null
            && block.Variant.TryGetValue("type", out string? type)
            && !string.IsNullOrWhiteSpace(type))
        {
            cropType = type;
        }
        else
        {
            string path = block.Code.Path;
            int first = path.IndexOf('-');
            int last = path.LastIndexOf('-');
            if (first >= 0 && last > first)
            {
                cropType = path.Substring(first + 1, last - first - 1);
            }
        }

        if (string.IsNullOrWhiteSpace(cropType))
        {
            return null;
        }

        string seedPath = "seeds-" + cropType;
        Item? seed = world.GetItem(new AssetLocation(block.Code.Domain, seedPath));
        if (seed != null)
        {
            return seed;
        }

        return world.GetItem(new AssetLocation("game", seedPath));
    }

    static int GetCropStage(Block block)
    {
        if (block is BlockCrop crop)
        {
            return crop.CurrentCropStage;
        }

        if (block.Variant != null
            && block.Variant.TryGetValue("stage", out string? stageText)
            && int.TryParse(stageText, out int fromVariant))
        {
            return fromVariant;
        }

        if (block.Code != null && int.TryParse(block.Code.EndVariant(), out int fromCode))
        {
            return fromCode;
        }

        return -1;
    }

    /// <summary>
    /// Plantable crop seeds: <see cref="ItemPlantableSeed"/> or items with attributes.isCrop.
    /// </summary>
    internal static bool IsSeedItem(Item item)
    {
        if (item == null || item.Id == 0)
        {
            return false;
        }

        if (item is ItemPlantableSeed)
        {
            return true;
        }

        return item.Attributes?["isCrop"].AsBool(false) == true;
    }

    internal static bool IsRawClayBlockPath(string path) =>
        path.StartsWith("rawclay-", StringComparison.OrdinalIgnoreCase);

    internal static bool IsPeatBlockPath(string path) =>
        path.StartsWith("peat-", StringComparison.OrdinalIgnoreCase);

    internal static bool IsStoneItemPath(string path) =>
        path.StartsWith("stone-", StringComparison.OrdinalIgnoreCase);

    internal static bool IsSaltpeterBlockPath(string path) =>
        path.StartsWith("saltpeter-", StringComparison.OrdinalIgnoreCase);

    internal static bool IsCharcoalBlockPath(string path) =>
        path.StartsWith("charcoalpile-", StringComparison.OrdinalIgnoreCase);

    internal static bool IsNuggetItem(Item? item)
    {
        if (item == null || item.Id == 0)
        {
            return false;
        }

        if (item is ItemNugget)
        {
            return true;
        }

        string? path = item.Code?.Path;
        return path != null && IsNuggetItemPath(path);
    }

    internal static bool IsNuggetItemPath(string path) =>
        path.StartsWith("nugget-", StringComparison.OrdinalIgnoreCase);

    internal static bool IsGravelPath(string path) =>
        path.StartsWith("gravel-", StringComparison.OrdinalIgnoreCase)
        || string.Equals(path, "gravel", StringComparison.OrdinalIgnoreCase);

    internal static bool IsSandPath(string path) =>
        path.StartsWith("sand-", StringComparison.OrdinalIgnoreCase)
        || string.Equals(path, "sand", StringComparison.OrdinalIgnoreCase);

    internal static bool IsBonySoilPath(string path) =>
        path.Contains("bonysoil", StringComparison.OrdinalIgnoreCase);

    internal static bool IsCrystallizedOreItemPath(string path) =>
        path.StartsWith("crystalizedore-", StringComparison.OrdinalIgnoreCase);

    internal static bool IsGemstoneOre(Block block)
    {
        if (block?.BlockMaterial != EnumBlockMaterial.Ore)
        {
            return false;
        }

        // Resolved drop codes support modded gem ores; the vanilla path fallback covers
        // catalog timing where drop stacks have not yet been resolved.
        if (block.Drops?.Any(drop =>
                (drop.ResolvedItemstack?.Collectible?.Code?.Path
                    ?? drop.Code?.Path
                    ?? "")
                .StartsWith("gem-", StringComparison.OrdinalIgnoreCase)) == true)
        {
            return true;
        }

        string path = block.Code?.Path ?? "";
        string[] parts = path.Split('-');
        if (parts.Length < 4 || !string.Equals(parts[0], "ore", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return parts[2] is "emerald" or "diamond" or "olivine_peridot";
    }
}
