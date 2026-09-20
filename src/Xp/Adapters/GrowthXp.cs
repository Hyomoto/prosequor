using HarmonyLib;
using Prosequor.Ability;
using Prosequor.Xp;
using Prosequor.Xp.Activity;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace Prosequor.Xp.Adapters;

/// <summary>
/// Domesticated growable growth-stage deeds: planter is emit <c>makerUid</c>;
/// crop farming pays with <c>payee: contributors</c> (till / plant / fertilize / water shares).
/// Forestry saplings and fruit-tree structural growth use <c>payee: maker</c>.
/// Fruit trees batch <c>TryGrowTo</c> placements into one emit (<c>craftCount</c> = blocks)
/// so flat and quantity rules both see a single deed.
/// </summary>
public static class GrowthXp
{
    /// <summary>Legacy alias for <see cref="DeedTokenTags.Grown"/> (crop-only rules).</summary>
    public const string TokenCropGrown = DeedTokenTags.CropGrown;

    [ThreadStatic]
    static int pendingFruitTreeBlocks;

    [ThreadStatic]
    static BlockEntity? pendingFruitTreeRoot;

    /// <summary>
    /// Emit <c>grown</c> + <c>domesticated</c> with blank actor, <c>makerUid</c> = planter,
    /// and farmland contributor shares (synthesized planter weight 1 when the bag is empty).
    /// </summary>
    public static void EmitIfDomesticated(
        ICoreAPI? api,
        BlockEntity? pedigreeBe,
        Block? targetBlock,
        BlockPos? position,
        int craftCount = 1)
    {
        if (api?.Side != EnumAppSide.Server
            || api.World == null
            || targetBlock == null
            || craftCount <= 0
            || !ProsequorBlockPedigreeStation.TryGetPlanter(pedigreeBe, out string? planterUid)
            || string.IsNullOrWhiteSpace(planterUid))
        {
            return;
        }

        IReadOnlyList<Deed.ContributorShare> shares = ResolveGrowthShares(pedigreeBe, planterUid!);
        EmitGrown(api, planterUid!, targetBlock, position, shares, craftCount);
    }

    /// <summary>
    /// Real contributor bag, or a single weight-1 planter share when empty
    /// (legacy planted crops with maker only).
    /// </summary>
    public static IReadOnlyList<Deed.ContributorShare> ResolveGrowthShares(
        BlockEntity? pedigreeBe,
        string planterUid)
    {
        if (ProsequorBlockPedigreeStation.TryGetBlob(pedigreeBe, out ProsequorBlob blob))
        {
            IReadOnlyList<Deed.ContributorShare> real = HusbandryContributorXp.RealContributorShares(blob);
            if (real.Count > 0)
            {
                return real;
            }
        }

        return [new Deed.ContributorShare(planterUid, 1f)];
    }

    /// <summary>
    /// Remember one successful fruit-tree <c>TryGrowTo</c> placement on the root BE.
    /// </summary>
    public static void NoteFruitTreeBlockGrown(BlockEntity? rootBe)
    {
        if (rootBe?.Api?.Side != EnumAppSide.Server)
        {
            return;
        }

        if (pendingFruitTreeRoot != null && !ReferenceEquals(pendingFruitTreeRoot, rootBe))
        {
            FlushFruitTreeStructuralGrowth();
        }

        pendingFruitTreeRoot = rootBe;
        pendingFruitTreeBlocks++;
    }

    /// <summary>
    /// Pay once for all structural placements since the last flush (<c>craftCount</c> = blocks).
    /// </summary>
    public static void FlushFruitTreeStructuralGrowth()
    {
        int blocks = pendingFruitTreeBlocks;
        BlockEntity? root = pendingFruitTreeRoot;
        pendingFruitTreeBlocks = 0;
        pendingFruitTreeRoot = null;

        if (blocks <= 0 || root?.Api == null)
        {
            return;
        }

        Block? block = root.Block
            ?? (root.Pos != null ? root.Api.World.BlockAccessor.GetBlock(root.Pos) : null);
        if (block == null || !AbilityBootstrap.IsFruitTreeBlock(block))
        {
            return;
        }

        EmitIfDomesticated(root.Api, root, block, root.Pos, blocks);
    }

    static void EmitGrown(
        ICoreAPI api,
        string planterUid,
        Block? targetBlock,
        BlockPos? position,
        IReadOnlyList<Deed.ContributorShare>? contributors = null,
        int craftCount = 1)
    {
        if (targetBlock == null || craftCount <= 0)
        {
            return;
        }

        float metric = 0f;
        string? metricDomain = null;
        int growthStages = 0;
        BlockCropProperties? props = targetBlock.CropProps;
        if (props != null && AbilityBootstrap.IsCropBlock(targetBlock))
        {
            float daysPerMonth = (float)(api.World?.Calendar?.DaysPerMonth ?? 0);
            float days = CropLifetimeMath.TotalGrowthDays(props, daysPerMonth);
            if (days > 0f)
            {
                metric = days;
                metricDomain = Deed.MetricDomainCropLifetime;
                growthStages = CropLifetimeMath.GrowthStages(props);
            }
        }

        api.Logger.VerboseDebug(
            "[prosequor] deed grown+domesticated {0} maker={1} contributors={2} craftCount={3} lifetimeDays={4} stages={5}",
            targetBlock.Code,
            planterUid,
            contributors?.Count ?? 0,
            craftCount,
            metric,
            growthStages);

        Deed.Emit(
            api,
            playerUid: "",
            [DeedToken.Grown.ToTag(), HarvestXp.TokenDomesticated],
            caller: CallerIdentities.Hand,
            target: EventFactBuilder.CodeOf(targetBlock),
            metric: metric,
            metricDomain: metricDomain,
            craftCount: craftCount,
            position: position,
            contributors: contributors,
            makerUid: planterUid,
            growthStages: growthStages);
    }

    static bool TryResolveFruitTreeRoot(BlockEntityFruitTreePart? part, out BlockEntity? rootBe)
    {
        rootBe = null;
        if (part?.Api?.World?.BlockAccessor == null || part.Pos == null)
        {
            return false;
        }

        BlockPos rootPos = part.Pos;
        if (part.RootOff != null && !part.RootOff.IsZero)
        {
            rootPos = part.Pos.AddCopy(part.RootOff);
        }

        rootBe = part.Api.World.BlockAccessor.GetBlockEntity(rootPos);
        return rootBe != null;
    }

    [HarmonyPatch(typeof(BlockEntityFarmland), nameof(BlockEntityFarmland.TryGrowCrop))]
    public static class FarmlandTryGrowCropXpPatch
    {
        [HarmonyPostfix]
        public static void Postfix(BlockEntityFarmland __instance, bool __result)
        {
            if (!__result || __instance?.Api == null)
            {
                return;
            }

            ProsequorBlockPedigreeStation.ResetWaterCredit(__instance);

            BlockPos cropPos = __instance.Pos.UpCopy();
            Block crop = __instance.Api.World.BlockAccessor.GetBlock(cropPos);
            if (!AbilityBootstrap.IsCropBlock(crop))
            {
                return;
            }

            EmitIfDomesticated(__instance.Api, __instance, crop, cropPos);
        }
    }

    /// <summary>
    /// Cutting → mature bush. Runs after planter carry (lower priority postfix).
    /// </summary>
    [HarmonyPatch(typeof(BEBehaviorFruitingBushCutting), "OnMatureTick")]
    [HarmonyPriority(Priority.Low)]
    public static class FruitingBushCuttingMatureXpPatch
    {
        [HarmonyPostfix]
        public static void Postfix(BEBehaviorFruitingBushCutting __instance)
        {
            BlockEntity? be = __instance?.Blockentity;
            ICoreAPI? api = be?.Api ?? __instance?.Api;
            if (api?.World == null || be?.Pos == null)
            {
                return;
            }

            Block block = api.World.BlockAccessor.GetBlock(be.Pos);
            if (!AbilityBootstrap.IsBerryBushBlock(block))
            {
                return;
            }

            EmitIfDomesticated(api, be, block, be.Pos);
        }
    }

    [HarmonyPatch(typeof(BEBehaviorFruitingBush), "setGrowthState")]
    public static class FruitingBushSetGrowthStateXpPatch
    {
        [HarmonyPrefix]
        public static void Prefix(
            BEBehaviorFruitingBush __instance,
            out EnumFruitingBushGrowthState __state)
        {
            __state = __instance?.BState?.Growthstate
                ?? EnumFruitingBushGrowthState.Young;
        }

        [HarmonyPostfix]
        public static void Postfix(
            BEBehaviorFruitingBush __instance,
            EnumFruitingBushGrowthState state,
            EnumFruitingBushGrowthState __state)
        {
            if (state == __state)
            {
                return;
            }

            BlockEntity? be = __instance?.Blockentity;
            ICoreAPI? api = be?.Api ?? __instance?.Api;
            if (api?.World == null || be?.Pos == null)
            {
                return;
            }

            Block block = api.World.BlockAccessor.GetBlock(be.Pos);
            if (!AbilityBootstrap.IsBerryBushBlock(block))
            {
                return;
            }

            EmitIfDomesticated(api, be, block, be.Pos);
        }
    }

    [HarmonyPatch(typeof(BlockEntityBerryBush), "DoGrow")]
    public static class LegacyBerryBushDoGrowXpPatch
    {
        [HarmonyPostfix]
        public static void Postfix(BlockEntityBerryBush __instance, bool __result)
        {
            if (!__result || __instance?.Api == null || __instance.Pos == null)
            {
                return;
            }

            Block block = __instance.Block
                ?? __instance.Api.World.BlockAccessor.GetBlock(__instance.Pos);
            if (!AbilityBootstrap.IsBerryBushBlock(block))
            {
                return;
            }

            EmitIfDomesticated(__instance.Api, __instance, block, __instance.Pos);
        }
    }

    public sealed class SaplingGrowCapture
    {
        public EnumTreeGrowthStage Stage;
        public string? Planter;
        public AssetLocation? Code;
        public BlockPos? Pos;
    }

    [HarmonyPatch(typeof(BlockEntitySapling), "CheckGrow")]
    [HarmonyPriority(Priority.Low)]
    public static class SaplingCheckGrowXpPatch
    {
        [HarmonyPrefix]
        public static void Prefix(
            BlockEntitySapling __instance,
            EnumTreeGrowthStage ___stage,
            out SaplingGrowCapture __state)
        {
            ProsequorBlockPedigreeStation.TryGetPlanter(__instance, out string? planter);
            __state = new SaplingGrowCapture
            {
                Stage = ___stage,
                Planter = planter,
                Code = __instance?.Block?.Code,
                Pos = __instance?.Pos?.Copy()
            };
        }

        [HarmonyPostfix]
        public static void Postfix(
            BlockEntitySapling __instance,
            EnumTreeGrowthStage ___stage,
            SaplingGrowCapture __state)
        {
            if (__state == null
                || string.IsNullOrWhiteSpace(__state.Planter)
                || __state.Pos == null
                || __instance?.Api?.Side != EnumAppSide.Server
                || __instance.Api.World == null)
            {
                return;
            }

            bool stageChanged = ___stage != __state.Stage;
            BlockEntity? stillSapling = __instance.Api.World.BlockAccessor.GetBlockEntity(__state.Pos);
            bool becameTree = stillSapling is not BlockEntitySapling;
            if (!stageChanged && !becameTree)
            {
                return;
            }

            Block? block = __instance.Block;
            if (block == null && __state.Code != null)
            {
                block = __instance.Api.World.GetBlock(__state.Code);
            }

            if (block == null)
            {
                block = __instance.Api.World.BlockAccessor.GetBlock(__state.Pos);
            }

            BlockEntity? pedigreeBe = stillSapling as BlockEntitySapling ?? __instance;
            if (!ProsequorBlockPedigreeStation.TryGetPlanter(pedigreeBe, out _) && becameTree)
            {
                EmitCaptured(__instance.Api, __state.Planter!, block, __state.Pos);
                return;
            }

            EmitIfDomesticated(__instance.Api, pedigreeBe, block, __state.Pos);
        }

        static void EmitCaptured(
            ICoreAPI api,
            string planterUid,
            Block? targetBlock,
            BlockPos position) =>
            EmitGrown(
                api,
                planterUid,
                targetBlock,
                position,
                ResolveGrowthShares(null, planterUid));
    }

    [HarmonyPatch(typeof(FruitTreeGrowingBranchBH), "TryGrowTo")]
    public static class FruitTreeTryGrowToXpPatch
    {
        [HarmonyPostfix]
        public static void Postfix(FruitTreeGrowingBranchBH __instance, bool __result)
        {
            if (!__result
                || __instance?.Blockentity is not BlockEntityFruitTreePart part
                || !TryResolveFruitTreeRoot(part, out BlockEntity? rootBe))
            {
                return;
            }

            NoteFruitTreeBlockGrown(rootBe);
        }
    }

    [HarmonyPatch(typeof(FruitTreeGrowingBranchBH), "TryGrow")]
    public static class FruitTreeTryGrowXpPatch
    {
        [HarmonyPostfix]
        [HarmonyPriority(Priority.Last)]
        public static void Postfix() => FlushFruitTreeStructuralGrowth();
    }
}
