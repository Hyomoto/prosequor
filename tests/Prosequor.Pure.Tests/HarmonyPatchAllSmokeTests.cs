using System.Reflection;
using System.Text;
using HarmonyLib;
using Prosequor;
using Prosequor.Ability;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;
using Xunit;

namespace Prosequor.Pure.Tests;

/// <summary>
/// Runs the same <see cref="Harmony.PatchAll(Assembly)"/> path mod startup uses.
/// Catches argument-name mismatches, missing targets, and other patch-time failures
/// before a game boot (e.g. postfix <c>allInputslots</c> vs <c>allInputSlots</c>).
/// Run this suite after any new <c>[HarmonyPatch]</c> — fixture/scenario filters alone will not.
/// </summary>
public class HarmonyPatchAllSmokeTests
{
    [Fact]
    [Trait("Layer", "Harmony")]
    [Trait("Kind", "PatchAll")]
    public void PatchAll_ModAssembly_Should_ApplyWithoutThrowing()
    {
        Assembly mod = typeof(ProsequorModSystem).Assembly;
        string harmonyId = $"{ProsequorModSystem.ModId}.test.patchall.{Guid.NewGuid():N}";
        Harmony harmony = new(harmonyId);

        try
        {
            try
            {
                harmony.PatchAll(mod);
                CraftMutateOutputAttributePatches.TryPatchOptionalReaders(harmony);
                KilnFireXpPatches.TryPatchOptionalIgniters(harmony);
                CementationAbilityPatches.TryPatchOptionalIgniters(harmony);
                PlayerModelLibCompat.TryPatch(harmony);
                KnapsterCompat.TryPatch(harmony);
            }
            catch (Exception ex)
            {
                Assert.Fail(FormatPatchFailure(ex));
            }

            Assert.NotEmpty(harmony.GetPatchedMethods());
        }
        finally
        {
            harmony.UnpatchAll(harmonyId);
        }
    }

    [Fact]
    [Trait("Layer", "Harmony")]
    [Trait("Kind", "PatchAll")]
    public void PatchAll_Should_ReplaceComposeStatsGuiWithOwnedPanel()
    {
        Assembly mod = typeof(ProsequorModSystem).Assembly;
        string harmonyId = $"{ProsequorModSystem.ModId}.test.playerstats.{Guid.NewGuid():N}";
        Harmony harmony = new(harmonyId);

        try
        {
            harmony.PatchAll(mod);
            MethodInfo? compose = AccessTools.Method(typeof(CharacterExtraDialogs), "ComposeStatsGui");
            Assert.NotNull(compose);
            Assert.Contains(compose, harmony.GetPatchedMethods());
            Patches? info = Harmony.GetPatchInfo(compose);
            Assert.NotNull(info);
            Assert.True(
                info!.Prefixes.Count > 0 && info.Postfixes.Count > 0,
                $"Expected ComposeStatsGui prefix+postfix; prefixes={info.Prefixes.Count}, postfixes={info.Postfixes.Count}");
        }
        finally
        {
            harmony.UnpatchAll(harmonyId);
        }
    }

    [Fact]
    [Trait("Layer", "Harmony")]
    [Trait("Kind", "PatchAll")]
    public void PatchAll_Should_IncludePitKilnOnFired()
    {
        Assembly mod = typeof(ProsequorModSystem).Assembly;
        string harmonyId = $"{ProsequorModSystem.ModId}.test.onfired.{Guid.NewGuid():N}";
        Harmony harmony = new(harmonyId);

        try
        {
            harmony.PatchAll(mod);
            MethodInfo? onFired = AccessTools.Method(typeof(BlockEntityPitKiln), "OnFired");
            Assert.NotNull(onFired);
            Assert.Contains(onFired, harmony.GetPatchedMethods());
            Patches? info = Harmony.GetPatchInfo(onFired);
            Assert.NotNull(info);
            Assert.True(
                info!.Prefixes.Count > 0 && info.Finalizers.Count > 0,
                $"Expected OnFired prefix+finalizer; prefixes={info.Prefixes.Count}, finalizers={info.Finalizers.Count}, postfixes={info.Postfixes.Count}");
        }
        finally
        {
            harmony.UnpatchAll(harmonyId);
        }
    }

    [Fact]
    [Trait("Layer", "Harmony")]
    [Trait("Kind", "PatchAll")]
    public void PatchAll_Should_IncludeStackPedigreeMergeAndTakeOut()
    {
        Assembly mod = typeof(ProsequorModSystem).Assembly;
        string harmonyId = $"{ProsequorModSystem.ModId}.test.pedigree.{Guid.NewGuid():N}";
        Harmony harmony = new(harmonyId);

        try
        {
            harmony.PatchAll(mod);

            MethodInfo? merge = AccessTools.Method(
                typeof(CollectibleObject),
                nameof(CollectibleObject.TryMergeStacks));
            MethodInfo? takeOut = AccessTools.Method(
                typeof(ItemSlot),
                nameof(ItemSlot.TakeOut),
                [typeof(int)]);
            Assert.NotNull(merge);
            Assert.NotNull(takeOut);
            Assert.Contains(merge, harmony.GetPatchedMethods());
            Assert.Contains(takeOut, harmony.GetPatchedMethods());

            Patches? mergeInfo = Harmony.GetPatchInfo(merge);
            Patches? takeInfo = Harmony.GetPatchInfo(takeOut);
            Assert.NotNull(mergeInfo);
            Assert.NotNull(takeInfo);
            Assert.True(
                mergeInfo!.Prefixes.Count > 0 && mergeInfo.Postfixes.Count > 0,
                "Expected TryMergeStacks pedigree prefix+postfix.");
            Assert.True(
                takeInfo!.Prefixes.Count > 0 && takeInfo.Postfixes.Count > 0,
                "Expected TakeOut pedigree prefix+postfix.");
        }
        finally
        {
            harmony.UnpatchAll(harmonyId);
        }
    }

    /// <summary>
    /// Cutting maturity uses private <c>OnMatureTick</c> (not
    /// <see cref="BEBehaviorFruitingBush.OnGrownFromCutting"/> on the cutting type).
    /// A wrong string target boots the game with PatchAll failure — lock the real hook here.
    /// </summary>
    [Fact]
    [Trait("Layer", "Harmony")]
    [Trait("Kind", "PatchAll")]
    public void PatchAll_Should_IncludeBushCuttingOnMatureTick()
    {
        MethodInfo? matureTick = AccessTools.DeclaredMethod(
            typeof(BEBehaviorFruitingBushCutting),
            "OnMatureTick");
        Assert.NotNull(matureTick);

        // Guard against regressing to the mature-bush API on the cutting type.
        Assert.Null(
            AccessTools.DeclaredMethod(typeof(BEBehaviorFruitingBushCutting), "OnGrownFromCutting"));
        Assert.NotNull(
            AccessTools.DeclaredMethod(
                typeof(BEBehaviorFruitingBush),
                nameof(BEBehaviorFruitingBush.OnGrownFromCutting)));

        Assembly mod = typeof(ProsequorModSystem).Assembly;
        string harmonyId = $"{ProsequorModSystem.ModId}.test.bushmature.{Guid.NewGuid():N}";
        Harmony harmony = new(harmonyId);

        try
        {
            harmony.PatchAll(mod);
            Assert.Contains(matureTick, harmony.GetPatchedMethods());
            Patches? info = Harmony.GetPatchInfo(matureTick);
            Assert.NotNull(info);
            Assert.True(
                info!.Prefixes.Count > 0 && info.Postfixes.Count > 0,
                "Expected OnMatureTick planter carry prefix+postfix.");
        }
        finally
        {
            harmony.UnpatchAll(harmonyId);
        }
    }

    [Fact]
    [Trait("Layer", "Harmony")]
    [Trait("Kind", "PatchAll")]
    public void PatchAll_Should_IncludeFarmlandTryGrowCrop()
    {
        MethodInfo? tryGrow = AccessTools.Method(
            typeof(BlockEntityFarmland),
            nameof(BlockEntityFarmland.TryGrowCrop));
        Assert.NotNull(tryGrow);

        Assembly mod = typeof(ProsequorModSystem).Assembly;
        string harmonyId = $"{ProsequorModSystem.ModId}.test.cropgrow.{Guid.NewGuid():N}";
        Harmony harmony = new(harmonyId);

        try
        {
            harmony.PatchAll(mod);
            Assert.Contains(tryGrow, harmony.GetPatchedMethods());
            Patches? info = Harmony.GetPatchInfo(tryGrow);
            Assert.NotNull(info);
            Assert.True(info!.Postfixes.Count > 0, "Expected TryGrowCrop growth XP postfix.");
        }
        finally
        {
            harmony.UnpatchAll(harmonyId);
        }
    }

    [Fact]
    [Trait("Layer", "Harmony")]
    [Trait("Kind", "PatchAll")]
    public void PatchAll_Should_IncludeGrowableGrowthXpHooks()
    {
        MethodInfo? setGrowth = AccessTools.DeclaredMethod(
            typeof(BEBehaviorFruitingBush),
            "setGrowthState");
        MethodInfo? doGrow = AccessTools.DeclaredMethod(
            typeof(BlockEntityBerryBush),
            "DoGrow");
        MethodInfo? checkGrow = AccessTools.DeclaredMethod(
            typeof(BlockEntitySapling),
            "CheckGrow");
        MethodInfo? tryGrowTo = AccessTools.DeclaredMethod(
            typeof(FruitTreeGrowingBranchBH),
            "TryGrowTo");
        MethodInfo? tryGrow = AccessTools.DeclaredMethod(
            typeof(FruitTreeGrowingBranchBH),
            "TryGrow");
        Assert.NotNull(setGrowth);
        Assert.NotNull(doGrow);
        Assert.NotNull(checkGrow);
        Assert.NotNull(tryGrowTo);
        Assert.NotNull(tryGrow);

        Assembly mod = typeof(ProsequorModSystem).Assembly;
        string harmonyId = $"{ProsequorModSystem.ModId}.test.growxp.{Guid.NewGuid():N}";
        Harmony harmony = new(harmonyId);

        try
        {
            harmony.PatchAll(mod);
            Assert.Contains(setGrowth, harmony.GetPatchedMethods());
            Assert.Contains(doGrow, harmony.GetPatchedMethods());
            Assert.Contains(checkGrow, harmony.GetPatchedMethods());
            Assert.Contains(tryGrowTo, harmony.GetPatchedMethods());
            Assert.Contains(tryGrow, harmony.GetPatchedMethods());
        }
        finally
        {
            harmony.UnpatchAll(harmonyId);
        }
    }

    [Fact]
    [Trait("Layer", "Harmony")]
    [Trait("Kind", "PatchAll")]
    public void PatchAll_Should_IncludeItemHoeDoTillTillSoilHooks()
    {
        MethodInfo? doTill = AccessTools.Method(typeof(ItemHoe), nameof(ItemHoe.DoTill));
        Assert.NotNull(doTill);

        Assembly mod = typeof(ProsequorModSystem).Assembly;
        string harmonyId = $"{ProsequorModSystem.ModId}.test.tillsoil.{Guid.NewGuid():N}";
        Harmony harmony = new(harmonyId);

        try
        {
            harmony.PatchAll(mod);
            Assert.Contains(doTill, harmony.GetPatchedMethods());
            Patches? info = Harmony.GetPatchInfo(doTill);
            Assert.NotNull(info);
            Assert.True(
                info!.Prefixes.Count > 0 && info.Postfixes.Count > 0,
                "Expected DoTill area-till prefix and till-soil postfix.");
        }
        finally
        {
            harmony.UnpatchAll(harmonyId);
        }
    }

    [Fact]
    [Trait("Layer", "Harmony")]
    [Trait("Kind", "PatchAll")]
    public void PatchAll_Should_IncludeWateringCanInteractStep()
    {
        MethodInfo? step = AccessTools.Method(
            typeof(BlockWateringCan),
            nameof(BlockWateringCan.OnHeldInteractStep));
        Assert.NotNull(step);

        Assembly mod = typeof(ProsequorModSystem).Assembly;
        string harmonyId = $"{ProsequorModSystem.ModId}.test.watering.{Guid.NewGuid():N}";
        Harmony harmony = new(harmonyId);

        try
        {
            harmony.PatchAll(mod);
            Assert.Contains(step, harmony.GetPatchedMethods());
            Patches? info = Harmony.GetPatchInfo(step);
            Assert.NotNull(info);
            Assert.True(info!.Postfixes.Count > 0, "Expected watering-can interact-step postfix.");
        }
        finally
        {
            harmony.UnpatchAll(harmonyId);
        }
    }

    [Fact]
    [Trait("Layer", "Harmony")]
    [Trait("Kind", "PatchAll")]
    public void PatchAll_Should_IncludeSoilFertilityAbsorbHooks()
    {
        MethodInfo? update = AccessTools.Method(typeof(BlockEntitySoilNutrition), "updateSoilFertility");
        Assert.NotNull(update);

        Assembly mod = typeof(ProsequorModSystem).Assembly;
        string harmonyId = $"{ProsequorModSystem.ModId}.test.fertabsorb.{Guid.NewGuid():N}";
        Harmony harmony = new(harmonyId);

        try
        {
            harmony.PatchAll(mod);
            Assert.Contains(update, harmony.GetPatchedMethods());
            Patches? info = Harmony.GetPatchInfo(update);
            Assert.NotNull(info);
            Assert.True(
                info!.Prefixes.Count > 0 && info.Postfixes.Count > 0 && info.Transpilers.Count > 0,
                "Expected updateSoilFertility prefix, postfix, and slow-release transpiler.");
        }
        finally
        {
            harmony.UnpatchAll(harmonyId);
        }
    }

    [Fact]
    [Trait("Layer", "Harmony")]
    [Trait("Kind", "PatchAll")]
    public void PatchAll_Should_IncludeOreSmashContainedInteractStop()
    {
        MethodInfo? stop = AccessTools.Method(
            typeof(ItemOre),
            nameof(ItemOre.OnContainedInteractStop));
        Assert.NotNull(stop);

        Assembly mod = typeof(ProsequorModSystem).Assembly;
        string harmonyId = $"{ProsequorModSystem.ModId}.test.oresmash.{Guid.NewGuid():N}";
        Harmony harmony = new(harmonyId);

        try
        {
            harmony.PatchAll(mod);
            Assert.Contains(stop, harmony.GetPatchedMethods());
            Patches? info = Harmony.GetPatchInfo(stop);
            Assert.NotNull(info);
            Assert.True(
                info!.Prefixes.Count > 0 && info.Postfixes.Count > 0,
                "Expected OnContainedInteractStop smash XP prefix+postfix.");
        }
        finally
        {
            harmony.UnpatchAll(harmonyId);
        }
    }

    [Fact]
    [Trait("Layer", "Harmony")]
    [Trait("Kind", "PatchAll")]
    public void PatchAll_Should_IncludeOreBlockBroken()
    {
        MethodInfo? oreBroken = AccessTools.DeclaredMethod(
            typeof(BlockOre),
            nameof(BlockOre.OnBlockBroken));
        Assert.NotNull(oreBroken);

        Assembly mod = typeof(ProsequorModSystem).Assembly;
        string harmonyId = $"{ProsequorModSystem.ModId}.test.orebroken.{Guid.NewGuid():N}";
        Harmony harmony = new(harmonyId);

        try
        {
            harmony.PatchAll(mod);
            Assert.Contains(oreBroken, harmony.GetPatchedMethods());
            Patches? oreInfo = Harmony.GetPatchInfo(oreBroken);
            Assert.NotNull(oreInfo);
            Assert.True(
                oreInfo!.Postfixes.Count > 0,
                "Expected BlockOre.OnBlockBroken mining XP postfix.");
        }
        finally
        {
            harmony.UnpatchAll(harmonyId);
        }
    }

    [Fact]
    [Trait("Layer", "Harmony")]
    [Trait("Kind", "PatchAll")]
    public void PatchAll_Should_IncludeBombCombustAndBlockExploded()
    {
        MethodInfo? combust = AccessTools.Method(
            typeof(BlockEntityBomb),
            nameof(BlockEntityBomb.Combust));
        MethodInfo? exploded = AccessTools.Method(
            typeof(Block),
            nameof(Block.OnBlockExploded),
            [typeof(IWorldAccessor), typeof(BlockPos), typeof(BlockPos), typeof(EnumBlastType), typeof(string)]);
        Assert.NotNull(combust);
        Assert.NotNull(exploded);

        Assembly mod = typeof(ProsequorModSystem).Assembly;
        string harmonyId = $"{ProsequorModSystem.ModId}.test.bombxp.{Guid.NewGuid():N}";
        Harmony harmony = new(harmonyId);

        try
        {
            harmony.PatchAll(mod);
            Assert.Contains(combust, harmony.GetPatchedMethods());
            Assert.Contains(exploded, harmony.GetPatchedMethods());

            Patches? combustInfo = Harmony.GetPatchInfo(combust);
            Patches? explodedInfo = Harmony.GetPatchInfo(exploded);
            Assert.NotNull(combustInfo);
            Assert.NotNull(explodedInfo);
            Assert.True(
                combustInfo!.Prefixes.Count > 0 && combustInfo.Finalizers.Count > 0,
                "Expected BlockEntityBomb.Combust prefix+finalizer.");
            Assert.True(
                explodedInfo!.Postfixes.Count > 0,
                "Expected Block.OnBlockExploded postfix.");
        }
        finally
        {
            harmony.UnpatchAll(harmonyId);
        }
    }

    [Fact]
    [Trait("Layer", "Harmony")]
    [Trait("Kind", "PatchAll")]
    public void PatchAll_Should_IncludeGroundStoredProcessableStop()
    {
        MethodInfo? stop = AccessTools.Method(
            typeof(CollectibleBehaviorGroundStoredProcessable),
            nameof(CollectibleBehaviorGroundStoredProcessable.OnContainedInteractStop));
        Assert.NotNull(stop);

        Assembly mod = typeof(ProsequorModSystem).Assembly;
        string harmonyId = $"{ProsequorModSystem.ModId}.test.groundprocess.{Guid.NewGuid():N}";
        Harmony harmony = new(harmonyId);

        try
        {
            harmony.PatchAll(mod);
            Assert.Contains(stop, harmony.GetPatchedMethods());
            Patches? info = Harmony.GetPatchInfo(stop);
            Assert.NotNull(info);
            Assert.True(
                info!.Prefixes.Count > 0 && info.Postfixes.Count > 0,
                "Expected OnContainedInteractStop ground-process XP prefix+postfix.");
        }
        finally
        {
            harmony.UnpatchAll(harmonyId);
        }
    }

    [Fact]
    [Trait("Layer", "Harmony")]
    [Trait("Kind", "PatchAll")]
    public void PatchAll_Should_IncludeLooseItemConsumeOnePortion()
    {
        MethodInfo? consume = AccessTools.Method(
            typeof(LooseItemFoodSource),
            nameof(LooseItemFoodSource.ConsumeOnePortion));
        Assert.NotNull(consume);

        Assembly mod = typeof(ProsequorModSystem).Assembly;
        string harmonyId = $"{ProsequorModSystem.ModId}.test.looseitem.{Guid.NewGuid():N}";
        Harmony harmony = new(harmonyId);

        try
        {
            harmony.PatchAll(mod);
            Assert.Contains(consume, harmony.GetPatchedMethods());

            Patches? info = Harmony.GetPatchInfo(consume);
            Assert.NotNull(info);
            Assert.True(
                info!.Postfixes.Count > 0,
                "Expected LooseItemFoodSource.ConsumeOnePortion friendliness postfix.");
        }
        finally
        {
            harmony.UnpatchAll(harmonyId);
        }
    }

    [Fact]
    [Trait("Layer", "Harmony")]
    [Trait("Kind", "PatchAll")]
    public void PatchAll_Should_IncludeFarmlandConsumeOnePortion()
    {
        MethodInfo? consume = AccessTools.Method(
            typeof(BlockEntityFarmland),
            nameof(BlockEntityFarmland.ConsumeOnePortion));
        MethodInfo? receiveDamage = AccessTools.Method(
            typeof(Entity),
            nameof(Entity.ReceiveDamage));
        Assert.NotNull(consume);
        Assert.NotNull(receiveDamage);

        Assembly mod = typeof(ProsequorModSystem).Assembly;
        string harmonyId = $"{ProsequorModSystem.ModId}.test.cropeat.{Guid.NewGuid():N}";
        Harmony harmony = new(harmonyId);

        try
        {
            harmony.PatchAll(mod);
            Assert.Contains(consume, harmony.GetPatchedMethods());
            Assert.Contains(receiveDamage, harmony.GetPatchedMethods());

            Patches? consumeInfo = Harmony.GetPatchInfo(consume);
            Patches? damageInfo = Harmony.GetPatchInfo(receiveDamage);
            Assert.NotNull(consumeInfo);
            Assert.NotNull(damageInfo);
            Assert.True(
                consumeInfo!.Postfixes.Count > 0,
                "Expected Farmland.ConsumeOnePortion friendliness postfix.");
            Assert.True(
                damageInfo!.Postfixes.Count > 0,
                "Expected Entity.ReceiveDamage friendliness postfix.");
        }
        finally
        {
            harmony.UnpatchAll(harmonyId);
        }
    }

    [Fact]
    [Trait("Layer", "Harmony")]
    [Trait("Kind", "PatchAll")]
    public void PatchAll_Should_IncludeFarmlandUpdateCropDamage()
    {
        MethodInfo? updateDamage = AccessTools.DeclaredMethod(
            typeof(BlockEntityFarmland),
            "updateCropDamage");
        Assert.NotNull(updateDamage);

        Assembly mod = typeof(ProsequorModSystem).Assembly;
        string harmonyId = $"{ProsequorModSystem.ModId}.test.cropclimate.{Guid.NewGuid():N}";
        Harmony harmony = new(harmonyId);

        try
        {
            harmony.PatchAll(mod);
            Assert.Contains(updateDamage, harmony.GetPatchedMethods());
            Patches? info = Harmony.GetPatchInfo(updateDamage);
            Assert.NotNull(info);
            Assert.True(
                info!.Transpilers.Count > 0,
                "Expected updateCropDamage climate-window transpiler.");
        }
        finally
        {
            harmony.UnpatchAll(harmonyId);
        }
    }

    [Fact]
    [Trait("Layer", "Harmony")]
    [Trait("Kind", "PatchAll")]
    public void PatchAll_Should_IncludeEntityPedigreeTransitions()
    {
        MethodInfo? becomeAdult = AccessTools.DeclaredMethod(
            typeof(EntityBehaviorGrow),
            "BecomeAdult");
        MethodInfo? creaturePlace = AccessTools.Method(
            typeof(ItemCreature),
            nameof(ItemCreature.OnHeldInteractStart));
        MethodInfo? rightClickPickup = AccessTools.Method(
            typeof(EntityBehaviorRightClickPickup),
            nameof(EntityBehaviorRightClickPickup.OnInteract));
        Assert.NotNull(becomeAdult);
        Assert.NotNull(creaturePlace);
        Assert.NotNull(rightClickPickup);

        Assembly mod = typeof(ProsequorModSystem).Assembly;
        string harmonyId = $"{ProsequorModSystem.ModId}.test.entitypedigree.{Guid.NewGuid():N}";
        Harmony harmony = new(harmonyId);

        try
        {
            harmony.PatchAll(mod);
            Assert.Contains(becomeAdult, harmony.GetPatchedMethods());
            Assert.Contains(creaturePlace, harmony.GetPatchedMethods());
            Assert.Contains(rightClickPickup, harmony.GetPatchedMethods());

            Patches? growInfo = Harmony.GetPatchInfo(becomeAdult);
            Patches? placeInfo = Harmony.GetPatchInfo(creaturePlace);
            Patches? pickupInfo = Harmony.GetPatchInfo(rightClickPickup);
            Assert.NotNull(growInfo);
            Assert.NotNull(placeInfo);
            Assert.NotNull(pickupInfo);
            Assert.True(growInfo!.Prefixes.Count > 0, "Expected BecomeAdult pedigree prefix.");
            Assert.True(growInfo.Postfixes.Count > 0, "Expected BecomeAdult age-up XP postfix.");
            Assert.True(
                placeInfo!.Prefixes.Count > 0 && placeInfo.Postfixes.Count > 0,
                "Expected ItemCreature place pedigree prefix+postfix.");
            Assert.True(
                pickupInfo!.Prefixes.Count > 0,
                "Expected RightClickPickup pedigree prefix.");
        }
        finally
        {
            harmony.UnpatchAll(harmonyId);
        }
    }

    [Fact]
    [Trait("Layer", "Harmony")]
    [Trait("Kind", "PatchAll")]
    public void PatchAll_Should_IncludeHusbandryMilestoneXp()
    {
        MethodInfo? becomeAdult = AccessTools.DeclaredMethod(
            typeof(EntityBehaviorGrow),
            "BecomeAdult");
        MethodInfo? giveBirth = AccessTools.DeclaredMethod(
            typeof(EntityBehaviorMultiply),
            "GiveBirth");
        Assert.NotNull(becomeAdult);
        Assert.NotNull(giveBirth);

        Assembly mod = typeof(ProsequorModSystem).Assembly;
        string harmonyId = $"{ProsequorModSystem.ModId}.test.husbandrymilestone.{Guid.NewGuid():N}";
        Harmony harmony = new(harmonyId);

        try
        {
            harmony.PatchAll(mod);
            Assert.Contains(becomeAdult, harmony.GetPatchedMethods());
            Assert.Contains(giveBirth, harmony.GetPatchedMethods());

            Patches? birthInfo = Harmony.GetPatchInfo(giveBirth);
            Assert.NotNull(birthInfo);
            Assert.True(birthInfo!.Postfixes.Count > 0, "Expected GiveBirth XP postfix.");
        }
        finally
        {
            harmony.UnpatchAll(harmonyId);
        }
    }

    [Fact]
    [Trait("Layer", "Harmony")]
    [Trait("Kind", "PatchAll")]
    public void PatchAll_Should_IncludeSkepHarvestXpHooks()
    {
        MethodInfo? onBroken = AccessTools.Method(
            typeof(BlockSkep),
            nameof(BlockSkep.OnBlockBroken));
        MethodInfo? interact = AccessTools.Method(
            typeof(BlockSkep),
            nameof(BlockSkep.OnBlockInteractStart));
        MethodInfo? getDrops = AccessTools.Method(
            typeof(BlockSkep),
            nameof(BlockSkep.GetDrops));
        MethodInfo? doPlace = AccessTools.Method(
            typeof(Block),
            nameof(Block.DoPlaceBlock));
        MethodInfo? onPlaced = AccessTools.Method(
            typeof(Block),
            nameof(Block.OnBlockPlaced));
        Assert.NotNull(onBroken);
        Assert.NotNull(interact);
        Assert.NotNull(getDrops);
        Assert.NotNull(doPlace);
        Assert.NotNull(onPlaced);

        Assembly mod = typeof(ProsequorModSystem).Assembly;
        string harmonyId = $"{ProsequorModSystem.ModId}.test.skepharvest.{Guid.NewGuid():N}";
        Harmony harmony = new(harmonyId);

        try
        {
            harmony.PatchAll(mod);
            Assert.Contains(onBroken, harmony.GetPatchedMethods());
            Assert.Contains(interact, harmony.GetPatchedMethods());
            Assert.Contains(getDrops, harmony.GetPatchedMethods());
            Assert.Contains(doPlace, harmony.GetPatchedMethods());
            Assert.Contains(onPlaced, harmony.GetPatchedMethods());
        }
        finally
        {
            harmony.UnpatchAll(harmonyId);
        }
    }

    [Fact]
    [Trait("Layer", "Harmony")]
    [Trait("Kind", "PatchAll")]
    public void PatchAll_Should_IncludeForageHarvestHooks()
    {
        MethodInfo? blockBroken = AccessTools.DeclaredMethod(
            typeof(Block),
            nameof(Block.OnBlockBroken));
        MethodInfo? doPlace = AccessTools.DeclaredMethod(
            typeof(Block),
            nameof(Block.DoPlaceBlock));
        MethodInfo? reedBroken = AccessTools.DeclaredMethod(
            typeof(BlockReeds),
            nameof(BlockReeds.OnBlockBroken));
        MethodInfo? fruitingInteract = AccessTools.DeclaredMethod(
            typeof(BEBehaviorFruitingBush),
            nameof(BEBehaviorFruitingBush.OnBlockInteractStop));
        MethodInfo? harvestableInteract = AccessTools.DeclaredMethod(
            typeof(BlockBehaviorHarvestable),
            nameof(BlockBehaviorHarvestable.OnBlockInteractStop));
        MethodInfo? ripeDrops = AccessTools.DeclaredMethod(
            typeof(BEBehaviorFruitingBush),
            nameof(BEBehaviorFruitingBush.GetRipeDrops));
        MethodInfo? rightClickPickup = AccessTools.DeclaredMethod(
            typeof(BlockBehaviorRightClickPickup),
            nameof(BlockBehaviorRightClickPickup.OnBlockInteractStart));
        Assert.NotNull(blockBroken);
        Assert.NotNull(doPlace);
        Assert.NotNull(reedBroken);
        Assert.NotNull(fruitingInteract);
        Assert.NotNull(harvestableInteract);
        Assert.NotNull(ripeDrops);
        Assert.NotNull(rightClickPickup);

        Assembly mod = typeof(ProsequorModSystem).Assembly;
        string harmonyId = $"{ProsequorModSystem.ModId}.test.forageharvest.{Guid.NewGuid():N}";
        Harmony harmony = new(harmonyId);

        try
        {
            harmony.PatchAll(mod);
            Assert.Contains(blockBroken, harmony.GetPatchedMethods());
            Assert.Contains(doPlace, harmony.GetPatchedMethods());
            Assert.Contains(reedBroken, harmony.GetPatchedMethods());
            Assert.Contains(fruitingInteract, harmony.GetPatchedMethods());
            Assert.Contains(harvestableInteract, harmony.GetPatchedMethods());
            Assert.Contains(ripeDrops, harmony.GetPatchedMethods());
            Assert.Contains(rightClickPickup, harmony.GetPatchedMethods());
        }
        finally
        {
            harmony.UnpatchAll(harmonyId);
        }
    }

    [Fact]
    [Trait("Layer", "Harmony")]
    [Trait("Kind", "PatchAll")]
    public void PatchAll_Should_IncludeHarvestableGenerateDrops()
    {
        MethodInfo? generate = AccessTools.Method(
            typeof(EntityBehaviorHarvestable),
            nameof(EntityBehaviorHarvestable.GenerateDrops));
        Assert.NotNull(generate);

        Assembly mod = typeof(ProsequorModSystem).Assembly;
        string harmonyId = $"{ProsequorModSystem.ModId}.test.butcher.{Guid.NewGuid():N}";
        Harmony harmony = new(harmonyId);

        try
        {
            harmony.PatchAll(mod);
            Assert.Contains(generate, harmony.GetPatchedMethods());
            Patches? info = Harmony.GetPatchInfo(generate);
            Assert.NotNull(info);
            Assert.True(
                info!.Prefixes.Count > 0 && info.Postfixes.Count > 0,
                $"Expected GenerateDrops prefix+postfix; prefixes={info.Prefixes.Count}, postfixes={info.Postfixes.Count}");
        }
        finally
        {
            harmony.UnpatchAll(harmonyId);
        }
    }

    [Fact]
    [Trait("Layer", "Harmony")]
    [Trait("Kind", "PatchAll")]
    public void PatchAll_Should_IncludeSkepPropagateTryPop()
    {
        MethodInfo? tryPop = AccessTools.DeclaredMethod(
            typeof(BlockEntityBeehive),
            "TryPopCurrentSkep");
        Assert.NotNull(tryPop);

        Assembly mod = typeof(ProsequorModSystem).Assembly;
        string harmonyId = $"{ProsequorModSystem.ModId}.test.skeppropagate.{Guid.NewGuid():N}";
        Harmony harmony = new(harmonyId);

        try
        {
            harmony.PatchAll(mod);
            Assert.Contains(tryPop, harmony.GetPatchedMethods());
            Patches? info = Harmony.GetPatchInfo(tryPop);
            Assert.NotNull(info);
            Assert.True(info!.Prefixes.Count > 0, "Expected TryPopCurrentSkep prefix.");
            Assert.True(info.Postfixes.Count > 0, "Expected TryPopCurrentSkep postfix.");
        }
        finally
        {
            harmony.UnpatchAll(harmonyId);
        }
    }

    [Fact]
    [Trait("Layer", "Harmony")]
    [Trait("Kind", "PatchAll")]
    public void PatchAll_Should_IncludeFirepitAndBarrelProcessStarter()
    {
        MethodInfo? burnTick = AccessTools.Method(typeof(BlockEntityFirepit), "OnBurnTick");
        MethodInfo? firepitInteract = AccessTools.Method(
            typeof(BlockFirepit),
            nameof(BlockFirepit.OnBlockInteractStart));
        MethodInfo? barrelPacket = AccessTools.Method(
            typeof(BlockEntityBarrel),
            nameof(BlockEntityBarrel.OnReceivedClientPacket));
        MethodInfo? barrelTick = AccessTools.Method(typeof(BlockEntityBarrel), "OnEvery3Second");
        Assert.NotNull(burnTick);
        Assert.NotNull(firepitInteract);
        Assert.NotNull(barrelPacket);
        Assert.NotNull(barrelTick);

        Assembly mod = typeof(ProsequorModSystem).Assembly;
        string harmonyId = $"{ProsequorModSystem.ModId}.test.processstarter.{Guid.NewGuid():N}";
        Harmony harmony = new(harmonyId);

        try
        {
            harmony.PatchAll(mod);
            Assert.Contains(burnTick, harmony.GetPatchedMethods());
            Assert.Contains(firepitInteract, harmony.GetPatchedMethods());
            Assert.Contains(barrelPacket, harmony.GetPatchedMethods());
            Assert.Contains(barrelTick, harmony.GetPatchedMethods());

            Patches? burnInfo = Harmony.GetPatchInfo(burnTick);
            Assert.NotNull(burnInfo);
            Assert.True(burnInfo!.Postfixes.Count > 0, "Expected OnBurnTick postfix.");

            Patches? barrelTickInfo = Harmony.GetPatchInfo(barrelTick);
            Assert.NotNull(barrelTickInfo);
            Assert.True(barrelTickInfo!.Prefixes.Count > 0, "Expected OnEvery3Second prefix.");
            Assert.True(barrelTickInfo.Postfixes.Count > 0, "Expected OnEvery3Second postfix.");
        }
        finally
        {
            harmony.UnpatchAll(harmonyId);
        }
    }

    [Fact]
    [Trait("Layer", "Harmony")]
    [Trait("Kind", "PatchAll")]
    public void PatchAll_Should_IncludeCementationFurnaceXpHooks()
    {
        MethodInfo? interact = AccessTools.Method(
            typeof(BlockEntityStoneCoffin),
            nameof(BlockEntityStoneCoffin.Interact));
        MethodInfo? addIngot = AccessTools.Method(typeof(BlockEntityStoneCoffin), "AddIngot");
        MethodInfo? addCoal = AccessTools.Method(typeof(BlockEntityStoneCoffin), "AddCoal");
        MethodInfo? tick3s = AccessTools.Method(typeof(BlockEntityStoneCoffin), "onServerTick3s");
        Assert.NotNull(interact);
        Assert.NotNull(addIngot);
        Assert.NotNull(addCoal);
        Assert.NotNull(tick3s);

        Assembly mod = typeof(ProsequorModSystem).Assembly;
        string harmonyId = $"{ProsequorModSystem.ModId}.test.cementation.{Guid.NewGuid():N}";
        Harmony harmony = new(harmonyId);

        try
        {
            harmony.PatchAll(mod);

            Assert.Contains(interact, harmony.GetPatchedMethods());
            Assert.Contains(addIngot, harmony.GetPatchedMethods());
            Assert.Contains(addCoal, harmony.GetPatchedMethods());
            Assert.Contains(tick3s, harmony.GetPatchedMethods());
        }
        finally
        {
            harmony.UnpatchAll(harmonyId);
        }
    }

    [Fact]
    [Trait("Layer", "Harmony")]
    [Trait("Kind", "PatchAll")]
    public void PatchAll_Should_ScopeClayAndAnvilUseOverFirst()
    {
        MethodInfo? clayUseOver = AccessTools.Method(
            typeof(BlockEntityClayForm),
            nameof(BlockEntityClayForm.OnUseOver),
            [typeof(IPlayer), typeof(Vec3i), typeof(BlockFacing), typeof(bool)]);
        MethodInfo? anvilUseOver = AccessTools.Method(
            typeof(BlockEntityAnvil),
            "OnUseOver",
            [typeof(IPlayer), typeof(Vec3i), typeof(BlockSelection)]);
        MethodInfo? anvilFinished = AccessTools.Method(
            typeof(BlockEntityAnvil),
            nameof(BlockEntityAnvil.CheckIfFinished));
        Assert.NotNull(clayUseOver);
        Assert.NotNull(anvilUseOver);
        Assert.NotNull(anvilFinished);

        Assembly mod = typeof(ProsequorModSystem).Assembly;
        string harmonyId = $"{ProsequorModSystem.ModId}.test.knapsterscope.{Guid.NewGuid():N}";
        Harmony harmony = new(harmonyId);

        try
        {
            harmony.PatchAll(mod);

            Patch? clayPrefix = Harmony.GetPatchInfo(clayUseOver)
                ?.Prefixes
                .FirstOrDefault(p => p.PatchMethod.DeclaringType == typeof(ClayFormOnUseOverScopePatch));
            Assert.NotNull(clayPrefix);
            Assert.Equal(Priority.First, clayPrefix!.priority);

            Patch? anvilPrefix = Harmony.GetPatchInfo(anvilUseOver)
                ?.Prefixes
                .FirstOrDefault(p => p.PatchMethod.DeclaringType == typeof(AnvilOnUseOverScopePatch));
            Assert.NotNull(anvilPrefix);
            Assert.Equal(Priority.First, anvilPrefix!.priority);

            Assert.Contains(anvilFinished, harmony.GetPatchedMethods());
            Patches? finishedInfo = Harmony.GetPatchInfo(anvilFinished);
            Assert.NotNull(finishedInfo);
            Assert.True(finishedInfo!.Postfixes.Count > 0, "Expected anvil CheckIfFinished postfix.");
        }
        finally
        {
            harmony.UnpatchAll(harmonyId);
        }
    }

    [Fact]
    [Trait("Layer", "Harmony")]
    [Trait("Kind", "PatchAll")]
    public void PatchAll_Should_IncludeReinforceAndHeatStructureDamage()
    {
        Assembly mod = typeof(ProsequorModSystem).Assembly;
        string harmonyId = $"{ProsequorModSystem.ModId}.test.construction.{Guid.NewGuid():N}";
        Harmony harmony = new(harmonyId);

        try
        {
            harmony.PatchAll(mod);

            MethodInfo? strengthen = AccessTools.Method(
                typeof(ModSystemBlockReinforcement),
                nameof(ModSystemBlockReinforcement.StrengthenBlock),
                [typeof(BlockPos), typeof(IPlayer), typeof(int), typeof(int)]);
            MethodInfo? walk = AccessTools.Method(
                typeof(MultiblockStructure),
                nameof(MultiblockStructure.WalkMatchingBlocks));
            Assert.NotNull(strengthen);
            Assert.NotNull(walk);
            Assert.Contains(strengthen, harmony.GetPatchedMethods());
            Assert.Contains(walk, harmony.GetPatchedMethods());

            Patches? strengthenInfo = Harmony.GetPatchInfo(strengthen);
            Patches? walkInfo = Harmony.GetPatchInfo(walk);
            Assert.NotNull(strengthenInfo);
            Assert.NotNull(walkInfo);
            Assert.True(
                strengthenInfo!.Prefixes.Count > 0 && strengthenInfo.Postfixes.Count > 0,
                $"Expected StrengthenBlock prefix+postfix; prefixes={strengthenInfo.Prefixes.Count}, postfixes={strengthenInfo.Postfixes.Count}");
            Assert.True(
                walkInfo!.Prefixes.Count > 0,
                $"Expected WalkMatchingBlocks prefix; prefixes={walkInfo.Prefixes.Count}");
        }
        finally
        {
            harmony.UnpatchAll(harmonyId);
        }
    }

    [Fact]
    [Trait("Layer", "Harmony")]
    [Trait("Kind", "PatchAll")]
    public void TryPatch_WhenKnapsterMissing_ShouldNotThrow()
    {
        string harmonyId = $"{ProsequorModSystem.ModId}.test.knapsteroptional.{Guid.NewGuid():N}";
        Harmony harmony = new(harmonyId);

        try
        {
            KnapsterCompat.TryPatch(harmony);
            Assert.Null(AccessTools.TypeByName(KnapsterCompat.ClayExtensionsTypeName));
            Assert.Null(AccessTools.TypeByName(KnapsterCompat.SmithingPatchesTypeName));
        }
        finally
        {
            harmony.UnpatchAll(harmonyId);
        }
    }

    static string FormatPatchFailure(Exception ex)
    {
        StringBuilder sb = new();
        sb.AppendLine("[prosequor] Harmony PatchAll smoke failed (same path as mod Start / AcquirePatches):");
        for (Exception? cur = ex; cur != null; cur = cur.InnerException)
        {
            sb.Append("  ").Append(cur.GetType().Name).Append(": ").AppendLine(cur.Message);
        }

        return sb.ToString();
    }
}
