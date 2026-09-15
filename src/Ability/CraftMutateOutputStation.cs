using Prosequor.Ability.Hooks;
using Prosequor.Player;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.Server;
using Vintagestory.Common;

namespace Prosequor.Ability;

/// <summary>
/// Resolves crafting-interaction <c>mutate-output</c>: quantity floor preview, pull-time
/// stochastic remainder onto the taken stack, create-time attributes, and refund.
/// </summary>
public static class CraftMutateOutputStation
{
    public const string VerbCraft = "prosequor:craft";
    public const string TagThread = "thread";
    public const string TagCloth = "cloth";

    /// <summary>Recipe base stack size stamped on the output before floor preview.</summary>
    public const string CraftBaseAttr = "prosequorCraftBase";

    static readonly PhaseId[] OutputAffectPhases = [HookIds.Quantity, HookIds.Attributes];

    public static void RegisterServer(ICoreServerAPI api)
    {
        // Quantity yield moved to TryPutInto postfix; kept for startup symmetry with other stations.
        _ = api;
    }

    /// <summary>
    /// When an unlock that can mutate the current craft output changes, rematch the open
    /// crafting grid the same way a material change does. Skips when the grid is empty or
    /// the node has no matching <c>mutate-output</c> quantity/attributes rule.
    /// </summary>
    public static bool TryRefreshOpenGrid(IPlayer player, string skillId, string nodeId)
    {
        if (player?.Entity == null
            || string.IsNullOrWhiteSpace(skillId)
            || string.IsNullOrWhiteSpace(nodeId))
        {
            return false;
        }

        IInventory? raw = player.InventoryManager?.GetOwnInventory(GlobalConstants.craftingInvClassName);
        if (raw is not InventoryCraftingGrid craft || craft.Count <= 1)
        {
            return false;
        }

        ItemSlot outputSlot = craft[craft.Count - 1];
        if (outputSlot is DummySlot || outputSlot?.Itemstack?.Collectible == null)
        {
            return false;
        }

        if (!NodeAffectsCraftOutput(player, craft, outputSlot.Itemstack, skillId, nodeId))
        {
            return false;
        }

        ItemSlot? trigger = null;
        int ingredientCount = craft.Count - 1;
        for (int i = 0; i < ingredientCount; i++)
        {
            ItemSlot slot = craft[i];
            if (slot?.Itemstack?.Collectible != null)
            {
                trigger = slot;
                break;
            }
        }

        if (trigger == null)
        {
            return false;
        }

        outputSlot.Itemstack = null;
        outputSlot.MarkDirty();
        craft.OnItemSlotModified(trigger);
        return true;
    }

    /// <summary>
    /// True when the unlocked node has a <c>mutate-output</c> quantity or attributes rule
    /// whose <c>when</c> matches the current craft output (and inputs).
    /// </summary>
    public static bool NodeAffectsCraftOutput(
        IPlayer player,
        InventoryCraftingGrid craft,
        ItemStack output,
        string skillId,
        string nodeId)
    {
        if (player?.Entity == null
            || craft == null
            || output?.Collectible == null
            || string.IsNullOrWhiteSpace(skillId)
            || string.IsNullOrWhiteSpace(nodeId))
        {
            return false;
        }

        ProsequorModSystem? mod = ProsequorModSystem.For(player.Entity.Api);
        AbilityRuleIndex? index = mod?.Pipeline?.RuleIndex;
        if (index == null)
        {
            return false;
        }

        List<ItemStack> inputs = new(Math.Max(0, craft.Count - 1));
        int ingredientCount = craft.Count - 1;
        for (int i = 0; i < ingredientCount; i++)
        {
            ItemStack? stack = craft[i]?.Itemstack;
            if (stack?.Collectible != null)
            {
                inputs.Add(stack);
            }
        }

        AbilityAction fact = EventFactBuilder.ForPlayer(
            player,
            VerbIds.MutateOutput.Value,
            target: EventFactBuilder.CodeOf(output),
            held: CallerIdentities.Grid,
            inputs: EventFactBuilder.CodesOf(inputs),
            includeLastCraft: false);

        for (int p = 0; p < OutputAffectPhases.Length; p++)
        {
            IReadOnlyList<AbilityRule> rules = index.Get(
                HookIds.CraftingInteraction,
                VerbIds.MutateOutput,
                OutputAffectPhases[p]);
            for (int i = 0; i < rules.Count; i++)
            {
                AbilityRule rule = rules[i];
                if (!string.Equals(rule.Source.SkillId, skillId, StringComparison.OrdinalIgnoreCase)
                    || !string.Equals(rule.Source.NodeId, nodeId, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (rule.When.Matches(fact))
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// True when no ingredient shares a family tag with the output (synthesis, not transform).
    /// </summary>
    public static bool IsSynthesis(IReadOnlyList<ItemStack> inputs, ItemStack? output)
    {
        string?[] paths = new string?[inputs.Count];
        for (int i = 0; i < inputs.Count; i++)
        {
            paths[i] = inputs[i]?.Collectible?.Code?.Path;
        }

        return IsSynthesis(paths, output?.Collectible?.Code?.Path);
    }

    public static bool IsSynthesis(ItemSlot[]? inputSlots, ItemStack? output)
    {
        if (inputSlots == null || inputSlots.Length == 0)
        {
            return IsSynthesis(Array.Empty<string?>(), output?.Collectible?.Code?.Path);
        }

        string?[] paths = new string?[inputSlots.Length];
        for (int i = 0; i < inputSlots.Length; i++)
        {
            paths[i] = inputSlots[i]?.Itemstack?.Collectible?.Code?.Path;
        }

        return IsSynthesis(paths, output?.Collectible?.Code?.Path);
    }

    /// <summary>Path-based synthesis check for fixtures and adapters.</summary>
    public static bool IsSynthesis(IReadOnlyList<string?> inputPaths, string? outputPath)
    {
        HashSet<string> outTags = ClassifyTags(outputPath);
        if (outTags.Count == 0)
        {
            return true;
        }

        for (int i = 0; i < inputPaths.Count; i++)
        {
            HashSet<string> inTags = ClassifyTags(inputPaths[i]);
            foreach (string tag in outTags)
            {
                if (inTags.Contains(tag))
                {
                    return false;
                }
            }
        }

        return true;
    }

    /// <summary>
    /// Pipeline quantity fold seeded with <paramref name="recipeBase"/> (count, not multiplier).
    /// Returns <paramref name="recipeBase"/> when the pipeline is unavailable.
    /// </summary>
    public static float ResolveQuantity(IPlayer player, ItemStack? output, int recipeBase)
    {
        if (recipeBase <= 0)
        {
            return 0f;
        }

        if (player?.Entity == null || output?.Collectible == null)
        {
            return recipeBase;
        }

        ProsequorModSystem? mod = ProsequorModSystem.For(player.Entity.Api);
        if (mod?.Pipeline == null)
        {
            return recipeBase;
        }

        IPlayerProgress? progress = ProsequorModSystem.GetProgress(player);
        if (progress == null)
        {
            return recipeBase;
        }

        AbilityAction fact = EventFactBuilder.ForPlayer(
            player,
            VerbIds.MutateOutput.Value,
            target: EventFactBuilder.CodeOf(output),
            held: CallerIdentities.Grid,
            includeLastCraft: false);

        CraftMutateOutputContext context = new()
        {
            Player = player,
            Progress = progress,
            Fact = fact,
            Crafted = output,
            Pipeline = mod.Pipeline
        };

        return Math.Max(0f, mod.Pipeline.Run(
            HookIds.CraftingInteraction,
            VerbIds.MutateOutput,
            HookIds.Quantity,
            context,
            (float)recipeBase));
    }

    /// <summary>
    /// Quantity fold seeded from <paramref name="output"/>.<see cref="ItemStack.StackSize"/>
    /// (or 1 when empty). Prefer <see cref="ResolveQuantity"/> when the recipe base is known.
    /// </summary>
    public static float ResolveQuantityMultiplier(IPlayer player, ItemStack? output)
    {
        int seed = output?.StackSize > 0 ? output.StackSize : 1;
        return ResolveQuantity(player, output, seed);
    }

    /// <summary>Whole units shown in the output slot: <c>floor(qty)</c> after the count fold.</summary>
    public static int FloorQuantity(float qty)
    {
        if (qty <= 0f)
        {
            return 0;
        }

        return (int)Math.Floor(qty);
    }

    /// <summary>
    /// Whole units for a known recipe base. <paramref name="qty"/> is the folded count
    /// (already includes the base); <paramref name="recipeBase"/> must be &gt; 0.
    /// </summary>
    public static int FloorQuantity(int recipeBase, float qty) =>
        recipeBase <= 0 ? 0 : FloorQuantity(qty);

    public static bool TryGetRecipeBase(ItemStack? stack, out int recipeBase)
    {
        recipeBase = 0;
        if (stack?.Attributes == null || !stack.Attributes.HasAttribute(CraftBaseAttr))
        {
            return false;
        }

        recipeBase = stack.Attributes.GetInt(CraftBaseAttr, 0);
        return recipeBase > 0;
    }

    public static void ClearRecipeBase(ItemStack? stack)
    {
        stack?.Attributes?.RemoveAttribute(CraftBaseAttr);
    }

    /// <summary>
    /// Create-time: stamp recipe base and set <see cref="ItemStack.StackSize"/> to floor(qty).
    /// Runs on client and server so the UI shows guaranteed whole units (no RNG).
    /// </summary>
    public static void ApplyQuantityPreview(IPlayer player, ItemSlot outputSlot, ItemSlot[]? inputSlots)
    {
        ItemStack? crafted = outputSlot?.Itemstack;
        if (player?.Entity == null || crafted?.Collectible == null)
        {
            return;
        }

        if (!IsSynthesis(inputSlots, crafted))
        {
            return;
        }

        // Already previewed (e.g. rematch after a prior stamp).
        if (TryGetRecipeBase(crafted, out _))
        {
            return;
        }

        int recipeBase = crafted.StackSize;
        if (recipeBase <= 0)
        {
            return;
        }

        float qty = ResolveQuantity(player, crafted, recipeBase);
        if (Math.Abs(qty - recipeBase) < 0.0001f)
        {
            return;
        }

        crafted.Attributes ??= new TreeAttribute();
        crafted.Attributes.SetInt(CraftBaseAttr, recipeBase);
        crafted.StackSize = FloorQuantity(qty);
        outputSlot!.MarkDirty();
    }

    /// <summary>
    /// Stamp the crafting player as maker when the output has no maker yet
    /// (fresh synthesis). Skips stacks that already carry a maker (e.g. repairs).
    /// </summary>
    public static void StampCraftMaker(IPlayer? player, ItemStack? crafted)
    {
        if (player?.Entity == null || crafted?.Collectible == null)
        {
            return;
        }

        if (player.Entity.Api?.Side != EnumAppSide.Server
            && player.Entity.World?.Side != EnumAppSide.Server)
        {
            return;
        }

        if (!string.IsNullOrEmpty(CraftAttribution.TryGetMakerUid(crafted)))
        {
            return;
        }

        CraftAttribution.StampMaker(crafted, player);
    }

    /// <summary>
    /// At item create: run <c>mutate-output</c> / <c>attributes</c>, then
    /// <c>apply-quality</c> / <c>attributes</c> on synthesis crafts.
    /// </summary>
    public static void ApplyAttributes(IPlayer player, ItemStack crafted) =>
        ApplyAttributes(player, crafted, inputSlots: null);

    /// <summary>
    /// Cook / process-starter path: stamp maker from uid, run mutate-output attributes,
    /// then apply-quality (always treated as synthesis).
    /// Meal hosts pass <paramref name="stampMaker"/> false so the cooker is not Created By.
    /// </summary>
    public static void ApplyAttributes(
        IWorldAccessor world,
        string? playerUid,
        ItemStack crafted,
        bool stampMaker = true)
    {
        if (world?.Side != EnumAppSide.Server
            || string.IsNullOrWhiteSpace(playerUid)
            || crafted?.Collectible == null)
        {
            return;
        }

        string uid = playerUid.Trim();
        if (stampMaker && string.IsNullOrEmpty(CraftAttribution.TryGetMakerUid(crafted)))
        {
            CraftAttribution.StampMakerUid(crafted, uid);
        }

        ProsequorModSystem? mod = ProsequorModSystem.For(world.Api);
        if (mod?.Pipeline == null)
        {
            return;
        }

        IPlayerProgress? progress = ProsequorModSystem.GetProgress(world.Api, uid);
        if (progress == null)
        {
            return;
        }

        IPlayer? player = world.PlayerByUid(uid);
        AbilityAction fact = player != null
            ? EventFactBuilder.ForPlayer(
                player,
                VerbIds.MutateOutput.Value,
                target: EventFactBuilder.CodeOf(crafted),
                held: CallerIdentities.Grid,
                includeLastCraft: false)
            : EventFactBuilder.Build(
                VerbIds.MutateOutput.Value,
                uid,
                held: CallerIdentities.Grid,
                target: EventFactBuilder.CodeOf(crafted));

        CraftMutateOutputContext context = new()
        {
            Player = player,
            Progress = progress,
            Fact = fact,
            Crafted = crafted,
            Pipeline = mod.Pipeline,
            World = world
        };

        mod.Pipeline.Run(HookIds.CraftingInteraction, VerbIds.MutateOutput, HookIds.Attributes, context, crafted);
        TryApplyQuality(world, uid, crafted);
    }

    /// <summary>
    /// At item create: run <c>mutate-output</c> / <c>attributes</c>, then
    /// <c>apply-quality</c> / <c>attributes</c> when the craft is synthesis.
    /// </summary>
    public static void ApplyAttributes(IPlayer player, ItemStack crafted, ItemSlot[]? inputSlots)
    {
        if (player?.Entity == null || crafted?.Collectible == null)
        {
            return;
        }

        if (player.Entity.Api?.Side != EnumAppSide.Server
            && player.Entity.World?.Side != EnumAppSide.Server)
        {
            return;
        }

        StampCraftMaker(player, crafted);

        ProsequorModSystem? mod = ProsequorModSystem.For(player.Entity.Api);
        if (mod?.Pipeline == null)
        {
            return;
        }

        IPlayerProgress? progress = ProsequorModSystem.GetProgress(player);
        if (progress == null)
        {
            return;
        }

        AbilityAction fact = EventFactBuilder.ForPlayer(
            player,
            VerbIds.MutateOutput.Value,
            target: EventFactBuilder.CodeOf(crafted),
            held: CallerIdentities.Grid,
            includeLastCraft: false);

        IWorldAccessor? world = player.Entity.World;
        CraftMutateOutputContext context = new()
        {
            Player = player,
            Progress = progress,
            Fact = fact,
            Crafted = crafted,
            Pipeline = mod.Pipeline,
            World = world
        };

        mod.Pipeline.Run(HookIds.CraftingInteraction, VerbIds.MutateOutput, HookIds.Attributes, context, crafted);

        bool synthesis;
        if (inputSlots != null)
        {
            synthesis = IsSynthesis(inputSlots, crafted);
        }
        else if (CraftGridScope.TryGet(out IReadOnlyList<ItemStack> inputs))
        {
            synthesis = IsSynthesis(inputs, crafted);
        }
        else
        {
            // No ingredient snapshot — treat as synthesis so standalone create paths still quality.
            synthesis = true;
        }

        if (!synthesis)
        {
            return;
        }

        if (world != null)
        {
            TryApplyQuality(world, player.PlayerUID, crafted);
        }
    }

    /// <summary>
    /// Warm apply-quality knobs and fold <c>attributes</c> onto <paramref name="crafted"/>.
    /// <paramref name="extraQualityBase"/> is added after warmup (mash rank → distilled).
    /// </summary>
    public static bool TryApplyQuality(
        IWorldAccessor world,
        string? playerUid,
        ItemStack crafted,
        float extraQualityBase = 0f)
    {
        if (world?.Side != EnumAppSide.Server || crafted?.Collectible == null)
        {
            return false;
        }

        ProsequorModSystem? mod = ProsequorModSystem.For(world.Api);
        if (mod?.Pipeline == null)
        {
            return false;
        }

        IPlayerProgress? progress = ProsequorModSystem.GetProgress(world.Api, playerUid);
        if (progress == null)
        {
            return false;
        }

        IPlayer? player = !string.IsNullOrWhiteSpace(playerUid)
            ? world.PlayerByUid(playerUid)
            : null;

        AbilityAction qualityFact = player != null
            ? EventFactBuilder.ForPlayer(
                player,
                VerbIds.ApplyQuality.Value,
                target: EventFactBuilder.CodeOf(crafted),
                held: CallerIdentities.Grid,
                includeLastCraft: false)
            : EventFactBuilder.Build(
                VerbIds.ApplyQuality.Value,
                playerUid ?? "",
                held: CallerIdentities.Grid,
                target: EventFactBuilder.CodeOf(crafted));

        CraftMutateOutputContext qualityContext = new()
        {
            Player = player,
            Progress = progress,
            Fact = qualityFact,
            Crafted = crafted,
            Pipeline = mod.Pipeline,
            Collections = mod.Collections?.Index,
            Rand = world.Rand,
            World = world
        };

        if (!TryWarmQualityKnobs(qualityContext, mod.Pipeline, progress))
        {
            return false;
        }

        qualityContext.QualityBase += extraQualityBase;
        mod.Pipeline.Run(
            HookIds.CraftingInteraction,
            VerbIds.ApplyQuality,
            HookIds.Attributes,
            qualityContext,
            crafted);
        return true;
    }

    /// <summary>
    /// Resolves apply-quality knob phases onto <paramref name="context"/> once.
    /// Returns false when no active matching quality stamp will run (skip the attributes fold).
    /// </summary>
    public static bool TryWarmQualityKnobs(
        CraftMutateOutputContext context,
        AbilityPipeline pipeline,
        IPlayerProgress progress)
    {
        if (context == null || pipeline == null || progress == null)
        {
            return false;
        }

        if (!TryFindQualityStampSeedSkill(context, pipeline, progress, out string skillId))
        {
            return false;
        }

        int level = progress.GetSkillLevel(skillId);
        context.QualityBase = pipeline.Run(
            HookIds.CraftingInteraction,
            VerbIds.ApplyQuality,
            HookIds.QualityBase,
            context,
            QualityMath.BaseSeed(level));
        context.QualityWindow = pipeline.Run(
            HookIds.CraftingInteraction,
            VerbIds.ApplyQuality,
            HookIds.QualityWindow,
            context,
            QualityMath.WindowSeed);
        context.QualityRolls = pipeline.Run(
            HookIds.CraftingInteraction,
            VerbIds.ApplyQuality,
            HookIds.QualityRolls,
            context,
            QualityMath.RollsSeed);
        context.QualityBonus = pipeline.Run(
            HookIds.CraftingInteraction,
            VerbIds.ApplyQuality,
            HookIds.QualityBonus,
            context,
            QualityMath.BonusSeed);
        context.QualityKnobsReady = true;
        return true;
    }

    /// <summary>
    /// First active <c>apply-quality</c>/<c>attributes</c> rule whose <c>when</c> matches the fact.
    /// Seed skill is that rule's source (all matching stamps should share one skill).
    /// </summary>
    static bool TryFindQualityStampSeedSkill(
        CraftMutateOutputContext context,
        AbilityPipeline pipeline,
        IPlayerProgress progress,
        out string skillId)
    {
        skillId = "";
        IReadOnlyList<AbilityRule> candidates;
        bool filterActiveInline;
        if (progress is IAbilityComposeCache cache
            && cache.TryGetActiveRules(
                HookIds.CraftingInteraction,
                VerbIds.ApplyQuality,
                HookIds.Attributes,
                out IReadOnlyList<AbilityRule> active))
        {
            candidates = active;
            filterActiveInline = false;
        }
        else
        {
            candidates = pipeline.RuleIndex.Get(
                HookIds.CraftingInteraction,
                VerbIds.ApplyQuality,
                HookIds.Attributes);
            filterActiveInline = true;
        }

        AbilityAction? fact = context.Fact;
        for (int i = 0; i < candidates.Count; i++)
        {
            AbilityRule rule = candidates[i];
            if (filterActiveInline && !AbilityPipeline.IsActive(rule, progress))
            {
                continue;
            }

            if (!rule.When.Matches(fact))
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(rule.Source.SkillId))
            {
                continue;
            }

            skillId = rule.Source.SkillId;
            return true;
        }

        return false;
    }

    /// <summary>
    /// After a craft pull: add fractional StochasticRound remainders onto the sink stack.
    /// Overflow past <see cref="CollectibleObject.MaxStackSize"/> spawns a world drop.
    /// </summary>
    public static void ApplyTakeYield(
        IPlayer player,
        ItemSlot sink,
        ItemStack? outputSnapshot,
        int craftCount)
    {
        if (player?.Entity == null || sink == null || craftCount <= 0)
        {
            return;
        }

        if (player.Entity.Api?.Side != EnumAppSide.Server
            && player.Entity.World?.Side != EnumAppSide.Server)
        {
            return;
        }

        ItemStack? template = sink.Itemstack ?? outputSnapshot;
        if (template?.Collectible == null)
        {
            return;
        }

        if (CraftGridScope.TryGet(out IReadOnlyList<ItemStack> inputs)
            && !IsSynthesis(inputs, template))
        {
            ClearRecipeBase(sink.Itemstack);
            return;
        }

        if (!TryGetRecipeBase(outputSnapshot, out int recipeBase)
            && !TryGetRecipeBase(sink.Itemstack, out recipeBase))
        {
            ClearRecipeBase(sink.Itemstack);
            return;
        }

        float qty = ResolveQuantity(player, template, recipeBase);
        if (Math.Abs(qty - recipeBase) < 0.0001f)
        {
            ClearRecipeBase(sink.Itemstack);
            return;
        }

        IWorldAccessor world = player.Entity.World;
        int floorPer = FloorQuantity(qty);
        int desired = 0;
        for (int i = 0; i < craftCount; i++)
        {
            desired += AbilityFormulas.StochasticRound(qty, world.Rand);
        }

        int already = craftCount * floorPer;
        int extras = desired - already;
        ClearRecipeBase(sink.Itemstack);

        if (extras <= 0)
        {
            return;
        }

        AddToSinkOrDrop(player, sink, template, extras);
    }

    static void AddToSinkOrDrop(IPlayer player, ItemSlot sink, ItemStack template, int amount)
    {
        int remaining = amount;
        IWorldAccessor world = player.Entity.World;
        int max = Math.Max(1, template.Collectible.MaxStackSize);

        if (sink.Itemstack != null
            && sink.Itemstack.Collectible != null
            && sink.Itemstack.Collectible.Class == template.Collectible.Class
            && sink.Itemstack.Collectible.Id == template.Collectible.Id)
        {
            int room = Math.Max(0, max - sink.Itemstack.StackSize);
            int add = Math.Min(room, remaining);
            if (add > 0)
            {
                sink.Itemstack.StackSize += add;
                ProsequorStackPedigree.DuplicatePrimaryToMatchStackSize(sink.Itemstack);
                remaining -= add;
                sink.MarkDirty();
            }
        }
        else if (sink.Empty)
        {
            int add = Math.Min(max, remaining);
            ItemStack give = template.Clone();
            ClearRecipeBase(give);
            give.StackSize = add;
            ProsequorStackPedigree.DuplicatePrimaryToMatchStackSize(give);
            sink.Itemstack = give;
            remaining -= add;
            sink.MarkDirty();
        }

        while (remaining > 0)
        {
            int dropSize = Math.Min(max, remaining);
            ItemStack drop = template.Clone();
            ClearRecipeBase(drop);
            drop.StackSize = dropSize;
            ProsequorStackPedigree.DuplicatePrimaryToMatchStackSize(drop);
            world.SpawnItemEntity(drop, player.Entity.Pos.XYZ);
            remaining -= dropSize;
        }
    }

    /// <summary>
    /// After ingredient consume: run <c>refund</c> phase. Matching
    /// <c>refund-ingredients</c> rules restock from the pre-consume snapshot.
    /// </summary>
    public static void TryRefundIngredients(IPlayer player, InventoryBase inventory)
    {
        if (player?.Entity == null || inventory == null || inventory.Count <= 1)
        {
            return;
        }

        if (player.Entity.Api?.Side != EnumAppSide.Server
            && player.Entity.World?.Side != EnumAppSide.Server)
        {
            return;
        }

        ProsequorModSystem? mod = ProsequorModSystem.For(player.Entity.Api);
        if (mod?.Pipeline == null || mod.Collections?.Index == null)
        {
            return;
        }

        IPlayerProgress? progress = ProsequorModSystem.GetProgress(player);
        if (progress == null)
        {
            return;
        }

        if (!CraftGridScope.TryGet(out IReadOnlyList<ItemStack> before)
            || before.Count == 0)
        {
            return;
        }

        AbilityAction fact = EventFactBuilder.ForPlayer(
            player,
            VerbIds.MutateOutput.Value,
            inputs: EventFactBuilder.CodesOf(before),
            held: CallerIdentities.Grid,
            includeLastCraft: false);

        CraftMutateOutputContext context = new()
        {
            Player = player,
            Progress = progress,
            Fact = fact,
            CraftInventory = inventory,
            IngredientSnapshot = before,
            Collections = mod.Collections.Index,
            Pipeline = mod.Pipeline
        };

        mod.Pipeline.Run(HookIds.CraftingInteraction, VerbIds.MutateOutput, HookIds.Refund, context, 0);
    }

    /// <summary>
    /// Refund units: <c>min(amount, max(0, consumed - retain))</c>.
    /// </summary>
    public static int ComputeRefund(int amount, int retain, int consumed) =>
        Math.Min(Math.Max(0, amount), Math.Max(0, consumed - Math.Max(0, retain)));

    public static int CountMatchingUnits(
        IReadOnlyList<ItemStack> stacks,
        CollectionIndex collections,
        string matchId)
    {
        int total = 0;
        for (int i = 0; i < stacks.Count; i++)
        {
            ItemStack? stack = stacks[i];
            if (stack != null && collections.StackMatches(matchId, stack))
            {
                total += Math.Max(0, stack.StackSize);
            }
        }

        return total;
    }

    public static int CountMatchingUnitsInGrid(
        InventoryBase inventory,
        CollectionIndex collections,
        string matchId)
    {
        int total = 0;
        int ingredientCount = inventory.Count - 1;
        for (int i = 0; i < ingredientCount; i++)
        {
            ItemStack? stack = inventory[i]?.Itemstack;
            if (stack != null && collections.StackMatches(matchId, stack))
            {
                total += Math.Max(0, stack.StackSize);
            }
        }

        return total;
    }

    public static ItemStack? FindFirstMatching(
        IReadOnlyList<ItemStack> stacks,
        CollectionIndex collections,
        string matchId)
    {
        for (int i = 0; i < stacks.Count; i++)
        {
            ItemStack? stack = stacks[i];
            if (stack != null && collections.StackMatches(matchId, stack))
            {
                return stack;
            }
        }

        return null;
    }

    static bool SameCollectible(ItemStack a, ItemStack b) =>
        a.Collectible != null
        && b.Collectible != null
        && a.Collectible.Class == b.Collectible.Class
        && a.Collectible.Id == b.Collectible.Id;

    public static void RestockMatching(
        IPlayer player,
        InventoryBase inventory,
        ItemStack template,
        int amount)
    {
        int remaining = amount;
        int ingredientCount = inventory.Count - 1;

        for (int i = 0; i < ingredientCount && remaining > 0; i++)
        {
            ItemSlot slot = inventory[i];
            ItemStack? stack = slot.Itemstack;
            if (stack == null || !SameCollectible(stack, template))
            {
                continue;
            }

            int max = stack.Collectible.MaxStackSize;
            int room = Math.Max(0, max - stack.StackSize);
            int add = Math.Min(room, remaining);
            if (add <= 0)
            {
                continue;
            }

            stack.StackSize += add;
            remaining -= add;
            slot.MarkDirty();
        }

        for (int i = 0; i < ingredientCount && remaining > 0; i++)
        {
            ItemSlot slot = inventory[i];
            if (slot.Itemstack != null)
            {
                continue;
            }

            ItemStack give = template.Clone();
            int add = Math.Min(give.Collectible.MaxStackSize, remaining);
            give.StackSize = add;
            slot.Itemstack = give;
            remaining -= add;
            slot.MarkDirty();
        }

        if (remaining <= 0)
        {
            return;
        }

        ItemStack leftover = template.Clone();
        leftover.StackSize = remaining;
        if (!player.InventoryManager.TryGiveItemstack(leftover, slotNotifyEffect: true))
        {
            player.Entity.World.SpawnItemEntity(leftover, player.Entity.Pos.XYZ);
        }
    }

    public static HashSet<string> ClassifyTags(ItemStack? stack) =>
        ClassifyTags(stack?.Collectible?.Code?.Path);

    public static HashSet<string> ClassifyTags(string? path)
    {
        HashSet<string> tags = new(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrEmpty(path))
        {
            return tags;
        }

        if (path.Equals("flaxtwine", StringComparison.OrdinalIgnoreCase)
            || path.Contains("twine", StringComparison.OrdinalIgnoreCase)
            || path.Contains("thread", StringComparison.OrdinalIgnoreCase))
        {
            tags.Add(TagThread);
        }

        if (path.StartsWith("cloth-", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("linen-", StringComparison.OrdinalIgnoreCase))
        {
            tags.Add(TagCloth);
        }

        return tags;
    }
}
