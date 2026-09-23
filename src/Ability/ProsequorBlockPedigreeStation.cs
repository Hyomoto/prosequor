using System;
using Vintagestory.API.Common;
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
/// Persistence lives on <see cref="ProsequorChunkPedigree"/> (chunk moddata).
/// <see cref="BlockEntityBehaviorProsequorPedigree"/> is the client copy
/// <see cref="BlockEntity.MarkDirty"/> already sends.
/// </summary>
public static class ProsequorBlockPedigreeStation
{
    public const string CareCreditsAttr = "prosequorCareCredits";

    public const float DefaultAbsorbMultiplier = 1f;

    public const float WaterCreditCap = 1f;

    /// <summary>Pours at or below this moisture gain do not credit (full tile or a token splash).</summary>
    public const float WaterCreditMinGain = 0.01f;

    const float AbsorbEpsilon = 0.0001f;

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

        if (!TryWorld(be, out IWorldAccessor world))
        {
            return;
        }

        ProsequorChunkPedigree.Box box = ProsequorChunkPedigree.GetOrCreate(world, be.Pos);
        box.Blob = blob;
        Commit(world, be, box);
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
        if (be == null || !TryWorld(be, out IWorldAccessor world))
        {
            return;
        }

        string? uid = string.IsNullOrWhiteSpace(planterUid) ? null : planterUid.Trim();
        if (uid == null)
        {
            return;
        }

        ProsequorChunkPedigree.Box box = ProsequorChunkPedigree.GetOrCreate(world, be.Pos);
        box.Blob = box.Blob.WithMaker(uid);
        Commit(world, be, box);
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
    }

    /// <summary>Clear planter pedigree stored directly on a farmland BE.</summary>
    public static void ClearPlanter(BlockEntityFarmland? farmland) =>
        ClearPlanter((BlockEntity?)farmland);

    public static bool TryGetBlob(BlockEntity? be, out ProsequorBlob blob)
    {
        blob = ProsequorBlob.Empty;
        if (!TryGetBox(be, out ProsequorChunkPedigree.Box box) || !box.Blob.HasPersistable)
        {
            return false;
        }

        blob = box.Blob;
        return true;
    }

    /// <summary>Mutate the chunk pedigree box and commit (server).</summary>
    public static void Mutate(BlockEntity? be, Action<ProsequorChunkPedigree.Box> mutate)
    {
        if (be == null || mutate == null || !TryWorld(be, out IWorldAccessor world))
        {
            return;
        }

        ProsequorChunkPedigree.Box box = ProsequorChunkPedigree.GetOrCreate(world, be.Pos);
        mutate(box);
        Commit(world, be, box);
    }

    public static bool TryGetBox(BlockEntity? be, out ProsequorChunkPedigree.Box box)
    {
        box = new ProsequorChunkPedigree.Box();
        if (be == null)
        {
            return false;
        }

        if (be.Api?.Side == EnumAppSide.Client
            && be.GetBehavior<BlockEntityBehaviorProsequorPedigree>() is { HasMirror: true } mirror)
        {
            box = mirror.Box;
            return box.HasPersistable;
        }

        if (!TryWorld(be, out IWorldAccessor world))
        {
            return false;
        }

        return ProsequorChunkPedigree.TryGet(world, be.Pos, out box);
    }

    /// <summary>
    /// Attach the client mirror before <see cref="BlockEntity.FromTreeAttributes"/>.
    /// </summary>
    public static void EnsureAttached(BlockEntity? be)
    {
        if (be == null)
        {
            return;
        }

        RequireMirror(be);
    }

    /// <summary>Increments contributor weight on the BE Live blob (default +1).</summary>
    public static void AddContributor(BlockEntity? be, string? contributorUid, int amount = 1)
    {
        if (be == null
            || string.IsNullOrWhiteSpace(contributorUid)
            || amount <= 0
            || !TryWorld(be, out IWorldAccessor world))
        {
            return;
        }

        ProsequorChunkPedigree.Box box = ProsequorChunkPedigree.GetOrCreate(world, be.Pos);
        box.Blob = box.Blob.WithContributor(contributorUid.Trim(), amount);
        Commit(world, be, box);
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
        if (be == null
            || kind == FarmlandCareKind.None
            || string.IsNullOrWhiteSpace(contributorUid)
            || !TryWorld(be, out IWorldAccessor world))
        {
            return false;
        }

        string uid = contributorUid.Trim();
        int bit = (int)kind;
        ProsequorChunkPedigree.Box box = ProsequorChunkPedigree.GetOrCreate(world, be.Pos);
        box.CareFlags ??= new Dictionary<string, int>(StringComparer.Ordinal);
        box.CareFlags.TryGetValue(uid, out int flags);
        if ((flags & bit) != 0)
        {
            return false;
        }

        box.CareFlags[uid] = flags | bit;
        box.Blob = box.Blob.WithContributor(uid, 1);
        Commit(world, be, box);
        return true;
    }

    /// <summary>Soil Enrichment slow-release multiplier (1 = vanilla). Missing stamp → 1.</summary>
    public static float GetAbsorbMultiplier(BlockEntity? be)
    {
        if (be != null
            && TryWorld(be, out IWorldAccessor world)
            && ProsequorChunkPedigree.TryGet(world, be.Pos, out ProsequorChunkPedigree.Box box))
        {
            return Math.Max(box.AbsorbMultiplier, DefaultAbsorbMultiplier);
        }

        return DefaultAbsorbMultiplier;
    }

    /// <summary>Stamps the absorb multiplier. Does not touch maker, contributors, or care flags.</summary>
    public static void StampAbsorbMultiplier(BlockEntity? be, float multiplier)
    {
        if (be == null || !TryWorld(be, out IWorldAccessor world))
        {
            return;
        }

        ProsequorChunkPedigree.Box box = ProsequorChunkPedigree.GetOrCreate(world, be.Pos);
        box.AbsorbMultiplier = GameMath.Clamp(multiplier, DefaultAbsorbMultiplier, float.MaxValue);
        Commit(world, be, box);
    }

    public static float GetAbsorbRemainder(BlockEntity? be)
    {
        if (be != null
            && TryWorld(be, out IWorldAccessor world)
            && ProsequorChunkPedigree.TryGet(world, be.Pos, out ProsequorChunkPedigree.Box box))
        {
            return Math.Max(0f, box.AbsorbRemainder);
        }

        return 0f;
    }

    /// <summary>Adds drained nutrient percent to the unpaid remainder. Survives harvest.</summary>
    public static float AddAbsorbRemainder(BlockEntity? be, float transferred)
    {
        if (be == null || transferred <= AbsorbEpsilon || !TryWorld(be, out IWorldAccessor world))
        {
            return GetAbsorbRemainder(be);
        }

        ProsequorChunkPedigree.Box box = ProsequorChunkPedigree.GetOrCreate(world, be.Pos);
        box.AbsorbRemainder = Math.Max(0f, box.AbsorbRemainder) + transferred;
        Commit(world, be, box);
        return box.AbsorbRemainder;
    }

    /// <summary>
    /// Takes whole percents out of the remainder (at least 1 required). Returns 0 when the
    /// bank is still a fraction.
    /// </summary>
    public static int TakeAbsorbPercents(BlockEntity? be)
    {
        if (be == null
            || !TryWorld(be, out IWorldAccessor world)
            || !ProsequorChunkPedigree.TryGet(world, be.Pos, out ProsequorChunkPedigree.Box box))
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

        Commit(world, be, box);
        return whole;
    }

    public static float GetWaterCredit(BlockEntity? be)
    {
        if (be != null
            && TryWorld(be, out IWorldAccessor world)
            && ProsequorChunkPedigree.TryGet(world, be.Pos, out ProsequorChunkPedigree.Box box))
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
        if (be == null || gained <= WaterCreditMinGain || !TryWorld(be, out IWorldAccessor world))
        {
            return 0f;
        }

        ProsequorChunkPedigree.Box box = ProsequorChunkPedigree.GetOrCreate(world, be.Pos);
        float room = WaterCreditCap - Math.Clamp(box.WaterCredit, 0f, WaterCreditCap);
        if (room <= AbsorbEpsilon)
        {
            return 0f;
        }

        float credited = Math.Min(gained, room);
        box.WaterCredit = Math.Clamp(box.WaterCredit + credited, 0f, WaterCreditCap);
        Commit(world, be, box);
        return credited;
    }

    /// <summary>Growth spend: the next stage can earn another full pour.</summary>
    public static void ResetWaterCredit(BlockEntity? be)
    {
        if (be == null
            || !TryWorld(be, out IWorldAccessor world)
            || !ProsequorChunkPedigree.TryGet(world, be.Pos, out ProsequorChunkPedigree.Box box)
            || box.WaterCredit <= AbsorbEpsilon)
        {
            return;
        }

        box.WaterCredit = 0f;
        Commit(world, be, box);
    }

    /// <summary>
    /// Replaces the contributor bag with a single process-starter uid (weight 1).
    /// Preserves maker / recipe / friendliness-ready / anvil splits.
    /// </summary>
    public static void StampSoleContributor(BlockEntity? be, string? contributorUid)
    {
        if (be == null || !TryWorld(be, out IWorldAccessor world))
        {
            return;
        }

        string? uid = string.IsNullOrWhiteSpace(contributorUid) ? null : contributorUid.Trim();
        if (uid == null)
        {
            return;
        }

        ProsequorChunkPedigree.Box box = ProsequorChunkPedigree.GetOrCreate(world, be.Pos);
        box.Blob = box.Blob.WithSoleContributor(uid);
        Commit(world, be, box);
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
        if (be == null
            || !TryWorld(be, out IWorldAccessor world)
            || !ProsequorChunkPedigree.TryGet(world, be.Pos, out ProsequorChunkPedigree.Box box)
            || box.Blob.IsAnonymous)
        {
            return;
        }

        ProsequorBlob cleared = box.Blob.WithClearedContributors();
        box.Blob = cleared.IsAnonymous ? ProsequorBlob.Empty : cleared;
        Commit(world, be, box);
    }

    /// <summary>
    /// Write a known blob onto the BE (anonymous → clear). Used when replacing a host
    /// (e.g. empty skep dummy → populated Beehive) so Live survives SetBlock.
    /// </summary>
    public static void ApplyBlob(BlockEntity? be, ProsequorBlob blob)
    {
        if (be == null || !TryWorld(be, out IWorldAccessor world))
        {
            return;
        }

        if (blob.IsAnonymous)
        {
            Clear(be);
            return;
        }

        ProsequorChunkPedigree.Box box = ProsequorChunkPedigree.GetOrCreate(world, be.Pos);
        box.Blob = blob;
        Commit(world, be, box);
    }

    public static void Clear(BlockEntity? be)
    {
        if (be == null || !TryWorld(be, out IWorldAccessor world))
        {
            return;
        }

        if (!ProsequorChunkPedigree.TryGet(world, be.Pos, out ProsequorChunkPedigree.Box box))
        {
            ProsequorChunkPedigree.Clear(world, be.Pos);
            return;
        }

        box.ClearPedigree();
        Commit(world, be, box);
    }

    /// <summary>Drop the chunk pedigree entry when the block entity is removed.</summary>
    public static void ClearAtPos(IWorldAccessor? world, BlockPos? pos) =>
        ProsequorChunkPedigree.Clear(world, pos);

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

    static bool TryWorld(BlockEntity be, out IWorldAccessor world)
    {
        if (be.Api?.World is IWorldAccessor resolved)
        {
            world = resolved;
            return true;
        }

        world = null!;
        return false;
    }

    static void Commit(IWorldAccessor world, BlockEntity be, ProsequorChunkPedigree.Box box)
    {
        ProsequorChunkPedigree.Set(world, be.Pos, box);
        BlockEntityBehaviorProsequorPedigree mirror = RequireMirror(be);
        mirror.Box = box;
        mirror.HasMirror = true;
        be.MarkDirty(redrawOnClient: false);
    }

    static BlockEntityBehaviorProsequorPedigree RequireMirror(BlockEntity be)
    {
        BlockEntityBehaviorProsequorPedigree? existing =
            be.GetBehavior<BlockEntityBehaviorProsequorPedigree>();
        if (existing != null)
        {
            return existing;
        }

        BlockEntityBehaviorProsequorPedigree created = new(be);
        be.Behaviors.Add(created);
        return created;
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
