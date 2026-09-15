using System.Runtime.CompilerServices;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace Prosequor.Ability;

/// <summary>
/// Carries one unit of stack pedigree onto a placed block entity and back onto
/// pick/break drops. World blocks are size-1; the bag stays on ItemStacks.
/// Farmland stores crop <b>planter</b> MakerUid plus once-per-role care credits
/// (till / plant / fertilize / water → contributor weight 1 each). Soil Enrichment's
/// absorb multiplier and the unpaid absorb remainder live on the same box and
/// survive harvest. Watering XP uses a crop-scoped moisture budget that harvest
/// clears. Berry bushes, saplings, and fruit-tree cuttings store planter only.
/// </summary>
public static class ProsequorBlockPedigreeStation
{
    public const string CareCreditsAttr = "prosequorCareCredits";

    const string AbsorbAttr = "prosequorFarmlandAbsorb";
    const string AbsorbMulKey = "mul";
    const string AbsorbRemainderKey = "remainder";
    const string WaterCreditAttr = "prosequorWaterCredit";

    public const float DefaultAbsorbMultiplier = 1f;

    public const float WaterCreditCap = 1f;

    /// <summary>Pours at or below this moisture gain do not credit (full tile or a token splash).</summary>
    public const float WaterCreditMinGain = 0.01f;

    const float AbsorbEpsilon = 0.0001f;

    static readonly ConditionalWeakTable<BlockEntity, Box> boxes = new();

    sealed class Box
    {
        public ProsequorBlob Blob = ProsequorBlob.Empty;

        public Dictionary<string, int>? CareFlags;

        public float AbsorbMultiplier = DefaultAbsorbMultiplier;

        public float AbsorbRemainder;

        public float WaterCredit;
    }

    /// <summary>
    /// After place: stash the primary persistable unit blob from the placing stack onto the BE.
    /// </summary>
    public static void CaptureFromPlacedStack(BlockEntity? be, ItemStack? byItemStack)
    {
        if (be == null || byItemStack?.Attributes == null)
        {
            return;
        }

        // Ground storage keeps the ItemStack in inventory; pedigree rides on that stack.
        if (be is BlockEntityGroundStorage)
        {
            return;
        }

        ProsequorStackPedigree.AbsorbLegacySurface(byItemStack);
        if (!ProsequorStackPedigree.TryGetPrimaryBlob(byItemStack, out ProsequorBlob blob))
        {
            return;
        }

        Box box = boxes.GetOrCreateValue(be);
        box.Blob = blob;
        be.MarkDirty(redrawOnClient: false);
    }

    public static void CaptureAtPos(IWorldAccessor? world, BlockPos? pos, ItemStack? byItemStack)
    {
        if (world == null || pos == null)
        {
            return;
        }

        CaptureFromPlacedStack(world.BlockAccessor?.GetBlockEntity(pos), byItemStack);
    }

    /// <summary>
    /// Stamp a planter MakerUid onto any block entity (farmland crop planter, berry bush, …).
    /// Preserves existing contributor weights (same as entity/stack <c>StampMaker</c>).
    /// </summary>
    public static void StampPlanter(BlockEntity? be, string? planterUid)
    {
        if (be == null)
        {
            return;
        }

        string? uid = string.IsNullOrWhiteSpace(planterUid) ? null : planterUid.Trim();
        if (uid == null)
        {
            return;
        }

        Box box = boxes.GetOrCreateValue(be);
        box.Blob = box.Blob.WithMaker(uid);
        be.MarkDirty(redrawOnClient: false);
    }

    /// <summary>
    /// Stamp the planting player onto farmland. Survives crop stage SetBlock/ExchangeBlock.
    /// </summary>
    public static void StampPlanter(BlockEntityFarmland? farmland, string? planterUid) =>
        StampPlanter((BlockEntity?)farmland, planterUid);

    /// <summary>
    /// Resolve planter MakerUid from the farmland BE under a crop block position.
    /// </summary>
    public static bool TryGetCropPlanter(IWorldAccessor? world, BlockPos? cropPos, out string? planterUid)
    {
        planterUid = null;
        if (world?.BlockAccessor == null || cropPos == null)
        {
            return false;
        }

        BlockPos farmlandPos = cropPos.DownCopy();
        if (world.BlockAccessor.GetBlockEntity(farmlandPos) is not BlockEntityFarmland farmland)
        {
            return false;
        }

        return TryGetPlanter(farmland, out planterUid);
    }

    /// <summary>
    /// Resolve planter MakerUid from the block entity at a berry-bush (or other planted) position.
    /// </summary>
    public static bool TryGetBushPlanter(IWorldAccessor? world, BlockPos? bushPos, out string? planterUid)
    {
        planterUid = null;
        if (world?.BlockAccessor == null || bushPos == null)
        {
            return false;
        }

        BlockEntity? be = world.BlockAccessor.GetBlockEntity(bushPos);
        return TryGetPlanter(be, out planterUid);
    }

    /// <summary>
    /// Resolve planter from the fruit-tree root BE (via <c>RootOff</c>). Foliage and
    /// grown branches are not stamped; the cutting BE at the root is.
    /// </summary>
    public static bool TryGetFruitTreePlanter(
        IWorldAccessor? world,
        BlockPos? partPos,
        out string? planterUid)
    {
        planterUid = null;
        if (world?.BlockAccessor == null || partPos == null)
        {
            return false;
        }

        BlockEntity? be = world.BlockAccessor.GetBlockEntity(partPos);
        if (be is not BlockEntityFruitTreePart part)
        {
            return false;
        }

        BlockPos rootPos = part.Pos;
        if (part.RootOff != null && !part.RootOff.IsZero)
        {
            rootPos = part.Pos.AddCopy(part.RootOff);
        }

        return TryGetPlanter(world.BlockAccessor.GetBlockEntity(rootPos), out planterUid);
    }

    public static bool TryGetPlanter(BlockEntity? be, out string? planterUid)
    {
        planterUid = null;
        if (!TryGetBlob(be, out ProsequorBlob blob) || string.IsNullOrWhiteSpace(blob.MakerUid))
        {
            return false;
        }

        planterUid = blob.MakerUid;
        return true;
    }

    /// <summary>Clear planter pedigree on the farmland under <paramref name="cropPos"/>.</summary>
    public static void ClearCropPlanterAt(IWorldAccessor? world, BlockPos? cropPos)
    {
        if (world?.BlockAccessor == null || cropPos == null)
        {
            return;
        }

        if (world.BlockAccessor.GetBlockEntity(cropPos.DownCopy()) is BlockEntityFarmland farmland)
        {
            ClearPlanter(farmland);
        }
    }

    /// <summary>Clear planter pedigree stored on a block entity.</summary>
    public static void ClearPlanter(BlockEntity? be)
    {
        if (be == null)
        {
            return;
        }

        Clear(be);
        be.MarkDirty(redrawOnClient: false);
    }

    /// <summary>Clear planter pedigree stored directly on a farmland BE.</summary>
    public static void ClearPlanter(BlockEntityFarmland? farmland) =>
        ClearPlanter((BlockEntity?)farmland);

    public static bool TryGetBlob(BlockEntity? be, out ProsequorBlob blob)
    {
        blob = ProsequorBlob.Empty;
        if (be == null || !boxes.TryGetValue(be, out Box? box) || box.Blob.IsAnonymous)
        {
            return false;
        }

        blob = box.Blob;
        return true;
    }

    /// <summary>Increments contributor weight on the BE Live blob (default +1).</summary>
    public static void AddContributor(BlockEntity? be, string? contributorUid, int amount = 1)
    {
        if (be == null || string.IsNullOrWhiteSpace(contributorUid) || amount <= 0)
        {
            return;
        }

        Box box = boxes.GetOrCreateValue(be);
        box.Blob = box.Blob.WithContributor(contributorUid.Trim(), amount);
        be.MarkDirty(redrawOnClient: false);
    }

    /// <summary>
    /// Adds contributor weight 1 the first time <paramref name="contributorUid"/> performs
    /// <paramref name="kind"/> on this farmland since the last <see cref="Clear"/>.
    /// </summary>
    public static bool TryAddCareCredit(
        BlockEntity? be,
        string? contributorUid,
        FarmlandCareKind kind)
    {
        if (be == null || kind == FarmlandCareKind.None || string.IsNullOrWhiteSpace(contributorUid))
        {
            return false;
        }

        string uid = contributorUid.Trim();
        int bit = (int)kind;
        Box box = boxes.GetOrCreateValue(be);
        box.CareFlags ??= new Dictionary<string, int>(StringComparer.Ordinal);
        box.CareFlags.TryGetValue(uid, out int flags);
        if ((flags & bit) != 0)
        {
            return false;
        }

        box.CareFlags[uid] = flags | bit;
        box.Blob = box.Blob.WithContributor(uid, 1);
        be.MarkDirty(redrawOnClient: false);
        return true;
    }

    /// <summary>Soil Enrichment slow-release multiplier (1 = vanilla). Missing stamp → 1.</summary>
    public static float GetAbsorbMultiplier(BlockEntity? be)
    {
        if (be != null && boxes.TryGetValue(be, out Box? box))
        {
            return Math.Max(box.AbsorbMultiplier, DefaultAbsorbMultiplier);
        }

        return DefaultAbsorbMultiplier;
    }

    /// <summary>Stamps the absorb multiplier. Does not touch maker, contributors, or care flags.</summary>
    public static void StampAbsorbMultiplier(BlockEntity? be, float multiplier)
    {
        if (be == null)
        {
            return;
        }

        Box box = boxes.GetOrCreateValue(be);
        box.AbsorbMultiplier = GameMath.Clamp(multiplier, DefaultAbsorbMultiplier, float.MaxValue);
        be.MarkDirty(redrawOnClient: false);
    }

    public static float GetAbsorbRemainder(BlockEntity? be)
    {
        if (be != null && boxes.TryGetValue(be, out Box? box))
        {
            return Math.Max(0f, box.AbsorbRemainder);
        }

        return 0f;
    }

    /// <summary>Adds drained nutrient percent to the unpaid remainder. Survives harvest.</summary>
    public static float AddAbsorbRemainder(BlockEntity? be, float transferred)
    {
        if (be == null || transferred <= AbsorbEpsilon)
        {
            return GetAbsorbRemainder(be);
        }

        Box box = boxes.GetOrCreateValue(be);
        box.AbsorbRemainder = Math.Max(0f, box.AbsorbRemainder) + transferred;
        return box.AbsorbRemainder;
    }

    /// <summary>
    /// Takes whole percents out of the remainder (at least 1 required). Returns 0 when the
    /// bank is still a fraction.
    /// </summary>
    public static int TakeAbsorbPercents(BlockEntity? be)
    {
        if (be == null || !boxes.TryGetValue(be, out Box? box))
        {
            return 0;
        }

        int whole = (int)Math.Floor(box.AbsorbRemainder);
        if (whole <= 0)
        {
            return 0;
        }

        box.AbsorbRemainder -= whole;
        if (box.AbsorbRemainder < AbsorbEpsilon)
        {
            box.AbsorbRemainder = 0f;
        }

        return whole;
    }

    public static float GetWaterCredit(BlockEntity? be)
    {
        if (be != null && boxes.TryGetValue(be, out Box? box))
        {
            return Math.Clamp(box.WaterCredit, 0f, WaterCreditCap);
        }

        return 0f;
    }

    /// <summary>
    /// Adds moisture the can actually added, up to one full pour (<see cref="WaterCreditCap"/>).
    /// Gains at or below <see cref="WaterCreditMinGain"/> credit nothing.
    /// </summary>
    public static float TryAddWaterCredit(BlockEntity? be, float gained)
    {
        if (be == null || gained <= WaterCreditMinGain)
        {
            return 0f;
        }

        Box box = boxes.GetOrCreateValue(be);
        float room = WaterCreditCap - Math.Clamp(box.WaterCredit, 0f, WaterCreditCap);
        if (room <= AbsorbEpsilon)
        {
            return 0f;
        }

        float credited = Math.Min(gained, room);
        box.WaterCredit = Math.Clamp(box.WaterCredit + credited, 0f, WaterCreditCap);
        be.MarkDirty(redrawOnClient: false);
        return credited;
    }

    /// <summary>Growth spend: the next stage can earn another full pour.</summary>
    public static void ResetWaterCredit(BlockEntity? be)
    {
        if (be == null || !boxes.TryGetValue(be, out Box? box) || box.WaterCredit <= AbsorbEpsilon)
        {
            return;
        }

        box.WaterCredit = 0f;
        be.MarkDirty(redrawOnClient: false);
    }

    /// <summary>
    /// Replaces the contributor bag with a single process-starter uid (weight 1).
    /// Preserves maker / recipe / friendliness-ready / anvil splits.
    /// </summary>
    public static void StampSoleContributor(BlockEntity? be, string? contributorUid)
    {
        if (be == null)
        {
            return;
        }

        string? uid = string.IsNullOrWhiteSpace(contributorUid) ? null : contributorUid.Trim();
        if (uid == null)
        {
            return;
        }

        Box box = boxes.GetOrCreateValue(be);
        box.Blob = box.Blob.WithSoleContributor(uid);
        be.MarkDirty(redrawOnClient: false);
    }

    /// <summary>True only when the BE Live blob has exactly one contributor share.</summary>
    public static bool TryGetSoleContributor(BlockEntity? be, out string? uid)
    {
        uid = null;
        return TryGetBlob(be, out ProsequorBlob blob) && blob.TryGetSoleContributor(out uid);
    }

    /// <summary>
    /// Clears contributor weights while preserving maker / recipe / friendliness-ready.
    /// Anonymous after clear → empty Live.
    /// </summary>
    public static void ClearContributors(BlockEntity? be)
    {
        if (be == null || !boxes.TryGetValue(be, out Box? box) || box.Blob.IsAnonymous)
        {
            return;
        }

        ProsequorBlob cleared = box.Blob.WithClearedContributors();
        box.Blob = cleared.IsAnonymous ? ProsequorBlob.Empty : cleared;
        be.MarkDirty(redrawOnClient: false);
    }

    /// <summary>
    /// Write a known blob onto the BE (anonymous → clear). Used when replacing a host
    /// (e.g. empty skep dummy → populated Beehive) so Live survives SetBlock.
    /// </summary>
    public static void ApplyBlob(BlockEntity? be, ProsequorBlob blob)
    {
        if (be == null)
        {
            return;
        }

        if (blob.IsAnonymous)
        {
            Clear(be);
            be.MarkDirty(redrawOnClient: false);
            return;
        }

        boxes.GetOrCreateValue(be).Blob = blob;
        be.MarkDirty(redrawOnClient: false);
    }

    public static void Clear(BlockEntity? be)
    {
        if (be == null || !boxes.TryGetValue(be, out Box? box))
        {
            return;
        }

        box.Blob = ProsequorBlob.Empty;
        box.CareFlags = null;
        box.WaterCredit = 0f;
    }

    public static void WriteToTree(BlockEntity? be, ITreeAttribute? tree)
    {
        if (be == null || tree == null || !boxes.TryGetValue(be, out Box? box))
        {
            return;
        }

        if (!box.Blob.IsAnonymous)
        {
            box.Blob.WriteTo(tree.GetOrAddTreeAttribute(ProsequorStackPedigree.LiveAttr));
        }

        if (box.CareFlags != null && box.CareFlags.Count > 0)
        {
            ITreeAttribute care = tree.GetOrAddTreeAttribute(CareCreditsAttr);
            foreach (KeyValuePair<string, int> kv in box.CareFlags)
            {
                care.SetInt(kv.Key, kv.Value);
            }
        }

        if (box.WaterCredit > WaterCreditMinGain)
        {
            tree.SetFloat(WaterCreditAttr, box.WaterCredit);
        }
        else
        {
            tree.RemoveAttribute(WaterCreditAttr);
        }

        if (box.AbsorbMultiplier <= DefaultAbsorbMultiplier + AbsorbEpsilon
            && box.AbsorbRemainder <= AbsorbEpsilon)
        {
            return;
        }

        ITreeAttribute absorb = tree.GetOrAddTreeAttribute(AbsorbAttr);
        if (box.AbsorbMultiplier > DefaultAbsorbMultiplier + AbsorbEpsilon)
        {
            absorb.SetFloat(AbsorbMulKey, box.AbsorbMultiplier);
        }

        if (box.AbsorbRemainder > AbsorbEpsilon)
        {
            absorb.SetFloat(AbsorbRemainderKey, box.AbsorbRemainder);
        }
    }

    public static void ReadFromTree(BlockEntity? be, ITreeAttribute? tree)
    {
        if (be == null || tree == null)
        {
            return;
        }

        ITreeAttribute? live = tree.GetTreeAttribute(ProsequorStackPedigree.LiveAttr);
        if (live != null)
        {
            ProsequorBlob blob = ProsequorBlob.ReadFrom(live);
            if (!blob.IsAnonymous)
            {
                boxes.GetOrCreateValue(be).Blob = blob;
            }
        }

        ITreeAttribute? care = tree.GetTreeAttribute(CareCreditsAttr);
        if (care != null)
        {
            Dictionary<string, int> flags = new(StringComparer.Ordinal);
            foreach (KeyValuePair<string, IAttribute> kv in care)
            {
                if (string.IsNullOrWhiteSpace(kv.Key))
                {
                    continue;
                }

                int value = care.GetInt(kv.Key);
                if (value != 0)
                {
                    flags[kv.Key] = value;
                }
            }

            if (flags.Count > 0)
            {
                boxes.GetOrCreateValue(be).CareFlags = flags;
            }
        }

        float waterCredit = tree.GetFloat(WaterCreditAttr, 0f);
        if (waterCredit > AbsorbEpsilon)
        {
            boxes.GetOrCreateValue(be).WaterCredit = Math.Clamp(waterCredit, 0f, WaterCreditCap);
        }

        ITreeAttribute? absorb = tree.GetTreeAttribute(AbsorbAttr);
        float multiplier = absorb?.GetFloat(AbsorbMulKey, DefaultAbsorbMultiplier) ?? DefaultAbsorbMultiplier;
        float remainder = absorb?.GetFloat(AbsorbRemainderKey, 0f) ?? 0f;
        if (multiplier > DefaultAbsorbMultiplier + AbsorbEpsilon || remainder > AbsorbEpsilon)
        {
            Box box = boxes.GetOrCreateValue(be);
            if (multiplier > DefaultAbsorbMultiplier + AbsorbEpsilon)
            {
                box.AbsorbMultiplier = GameMath.Clamp(multiplier, DefaultAbsorbMultiplier, float.MaxValue);
            }

            if (remainder > AbsorbEpsilon)
            {
                box.AbsorbRemainder = remainder;
            }
        }
    }

    /// <summary>
    /// Restore pedigree onto pick/break stacks that represent this block.
    /// Skips stacks that already carry Live/Frozen.
    /// </summary>
    public static void ApplyToStacks(BlockEntity? be, Block? block, ItemStack[]? stacks)
    {
        if (stacks == null || stacks.Length == 0 || !TryGetBlob(be, out ProsequorBlob blob))
        {
            return;
        }

        for (int i = 0; i < stacks.Length; i++)
        {
            ApplyToStack(block, stacks[i], blob);
        }
    }

    public static void ApplyToStack(BlockEntity? be, Block? block, ItemStack? stack)
    {
        if (stack == null || !TryGetBlob(be, out ProsequorBlob blob))
        {
            return;
        }

        ApplyToStack(block, stack, blob);
    }

    public static void ApplyAtPos(IWorldAccessor? world, BlockPos? pos, Block? block, ItemStack[]? stacks)
    {
        if (world == null || pos == null)
        {
            return;
        }

        ApplyToStacks(world.BlockAccessor?.GetBlockEntity(pos), block, stacks);
    }

    public static void ApplyAtPos(IWorldAccessor? world, BlockPos? pos, Block? block, ItemStack? stack)
    {
        if (world == null || pos == null || stack == null)
        {
            return;
        }

        ApplyToStack(world.BlockAccessor?.GetBlockEntity(pos), block, stack);
    }

    static void ApplyToStack(Block? block, ItemStack stack, ProsequorBlob blob)
    {
        if (stack.Attributes == null
            || stack.StackSize <= 0
            || ProsequorStackPedigree.HasLive(stack)
            || ProsequorStackPedigree.HasFrozen(stack))
        {
            return;
        }

        // Pick/drop results are already scoped to this block. Do not require Id/code
        // equality — OnPickBlock often rebuilds a variant (e.g. emptied crock).
        _ = block;
        ProsequorStackPedigree.ApplyUnitBlob(stack, blob);
    }
}
