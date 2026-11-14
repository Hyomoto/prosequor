using System.Runtime.CompilerServices;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace Prosequor.Ability;

/// <summary>
/// Sparse block-entity pedigree on chunk moddata. Only positions that were
/// stamped get an entry; overwrite replaces; remove clears. Same local pack
/// as <see cref="ForagePlayerPlaced"/>. Live entries stay in memory while the
/// chunk is loaded; the blob is the save record. Live blob is the unit; other
/// keys on the same entry are host process / farmland / plant / anti-replay state.
/// </summary>
public static class ProsequorChunkPedigree
{
    public const string ModDataKey = "prosequorBlockPedigree";

    const string AbsorbAttr = "prosequorFarmlandAbsorb";
    const string AbsorbMulKey = "mul";
    const string AbsorbRemainderKey = "remainder";
    const string WaterCreditAttr = "prosequorWaterCredit";
    const string DistillLockedAttr = "prosequorDistillLocked";
    const float AbsorbEpsilon = 0.0001f;
    const float MinGrowthMul = 0.01f;

    public sealed class Box
    {
        public ProsequorBlob Blob = ProsequorBlob.Empty;

        public Dictionary<string, int>? CareFlags;

        public float AbsorbMultiplier = ProsequorBlockPedigreeStation.DefaultAbsorbMultiplier;

        public float AbsorbRemainder;

        public float WaterCredit;

        public int ClayHighWater;

        public string? ClayRecipeKey;

        public float ClimateHalfDelta;

        public float GrowthTimeMultiplier;

        public float EstablishChance;

        public bool DistillLocked;

        public bool CementationPaid;

        public string? MoldToolPourerUid;

        public bool MoldToolPaid;

        public string? MoldLeftPourerUid;

        public string? MoldRightPourerUid;

        public bool MoldLeftPaid;

        public bool MoldRightPaid;

        public bool CollectXp;

        public string? LastInteractorUid;

        public string? KilnLastIgniterUid;

        public string? KilnSessionFirerUid;

        public bool HasPersistable =>
            Blob.HasPersistable
            || DistillLocked
            || (CareFlags != null && CareFlags.Count > 0)
            || WaterCredit > ProsequorBlockPedigreeStation.WaterCreditMinGain
            || AbsorbMultiplier > ProsequorBlockPedigreeStation.DefaultAbsorbMultiplier + AbsorbEpsilon
            || AbsorbRemainder > AbsorbEpsilon
            || ClayHighWater > 0
            || !string.IsNullOrEmpty(ClayRecipeKey)
            || ClimateHalfDelta > AbsorbEpsilon
            || GrowthTimeMultiplier >= MinGrowthMul
            || EstablishChance > AbsorbEpsilon
            || CementationPaid
            || !string.IsNullOrEmpty(MoldToolPourerUid)
            || MoldToolPaid
            || !string.IsNullOrEmpty(MoldLeftPourerUid)
            || !string.IsNullOrEmpty(MoldRightPourerUid)
            || MoldLeftPaid
            || MoldRightPaid
            || CollectXp
            || !string.IsNullOrEmpty(LastInteractorUid)
            || !string.IsNullOrEmpty(KilnLastIgniterUid)
            || !string.IsNullOrEmpty(KilnSessionFirerUid);

        public void ClearPedigree()
        {
            Blob = ProsequorBlob.Empty;
            CareFlags = null;
            WaterCredit = 0f;
            DistillLocked = false;
            ClimateHalfDelta = 0f;
            GrowthTimeMultiplier = 0f;
            EstablishChance = 0f;
            ClayHighWater = 0;
            ClayRecipeKey = null;
            CementationPaid = false;
            MoldToolPourerUid = null;
            MoldToolPaid = false;
            MoldLeftPourerUid = null;
            MoldRightPourerUid = null;
            MoldLeftPaid = false;
            MoldRightPaid = false;
            CollectXp = false;
            LastInteractorUid = null;
            KilnLastIgniterUid = null;
            KilnSessionFirerUid = null;
        }

        public void WriteTo(ITreeAttribute tree)
        {
            if (Blob.HasPersistable || DistillLocked)
            {
                Blob.WriteTo(tree.GetOrAddTreeAttribute(ProsequorStackPedigree.LiveAttr));
            }

            if (DistillLocked)
            {
                tree.SetBool(DistillLockedAttr, true);
            }

            if (CareFlags != null && CareFlags.Count > 0)
            {
                ITreeAttribute care = tree.GetOrAddTreeAttribute(ProsequorBlockPedigreeStation.CareCreditsAttr);
                foreach (KeyValuePair<string, int> kv in CareFlags)
                {
                    care.SetInt(kv.Key, kv.Value);
                }
            }

            if (WaterCredit > ProsequorBlockPedigreeStation.WaterCreditMinGain)
            {
                tree.SetFloat(WaterCreditAttr, WaterCredit);
            }
            else
            {
                tree.RemoveAttribute(WaterCreditAttr);
            }

            if (AbsorbMultiplier > ProsequorBlockPedigreeStation.DefaultAbsorbMultiplier + AbsorbEpsilon
                || AbsorbRemainder > AbsorbEpsilon)
            {
                ITreeAttribute absorb = tree.GetOrAddTreeAttribute(AbsorbAttr);
                if (AbsorbMultiplier > ProsequorBlockPedigreeStation.DefaultAbsorbMultiplier + AbsorbEpsilon)
                {
                    absorb.SetFloat(AbsorbMulKey, AbsorbMultiplier);
                }

                if (AbsorbRemainder > AbsorbEpsilon)
                {
                    absorb.SetFloat(AbsorbRemainderKey, AbsorbRemainder);
                }
            }

            if (ClayHighWater > 0)
            {
                tree.SetInt(ClayFormXpStation.HighWaterAttr, ClayHighWater);
            }

            if (!string.IsNullOrEmpty(ClayRecipeKey))
            {
                tree.SetString(ClayFormXpStation.RecipeKeyAttr, ClayRecipeKey);
            }

            if (ClimateHalfDelta > AbsorbEpsilon)
            {
                tree.SetFloat(CropClimateWindow.HalfDeltaAttr, ClimateHalfDelta);
            }

            if (GrowthTimeMultiplier >= MinGrowthMul)
            {
                tree.SetFloat(SaplingGrowthDuration.GrowthTimeMultiplierAttr, GrowthTimeMultiplier);
            }

            if (EstablishChance > AbsorbEpsilon)
            {
                tree.SetFloat(FruitTreeCuttingSuccess.EstablishChanceAttr, EstablishChance);
            }

            if (CementationPaid)
            {
                tree.SetBool(CementationXpStation.PaidAttr, true);
            }

            if (!string.IsNullOrEmpty(MoldToolPourerUid))
            {
                tree.SetString(MoldCastXpStation.ToolPourerAttr, MoldToolPourerUid);
            }

            if (MoldToolPaid)
            {
                tree.SetBool(MoldCastXpStation.ToolPaidAttr, true);
            }

            if (!string.IsNullOrEmpty(MoldLeftPourerUid))
            {
                tree.SetString(MoldCastXpStation.LeftPourerAttr, MoldLeftPourerUid);
            }

            if (!string.IsNullOrEmpty(MoldRightPourerUid))
            {
                tree.SetString(MoldCastXpStation.RightPourerAttr, MoldRightPourerUid);
            }

            if (MoldLeftPaid)
            {
                tree.SetBool(MoldCastXpStation.LeftPaidAttr, true);
            }

            if (MoldRightPaid)
            {
                tree.SetBool(MoldCastXpStation.RightPaidAttr, true);
            }

            if (CollectXp)
            {
                tree.SetBool(Prosequor.Xp.CollectXpStamp.AttrKey, true);
            }

            if (!string.IsNullOrEmpty(LastInteractorUid))
            {
                tree.SetString(FirepitProcessStarterStation.LastInteractorAttr, LastInteractorUid);
                tree.SetString(OvenCookStarterStation.LastInteractorAttr, LastInteractorUid);
            }

            if (!string.IsNullOrEmpty(KilnLastIgniterUid))
            {
                tree.SetString(BeeHiveKilnFirerStation.LastIgniterAttr, KilnLastIgniterUid);
            }

            if (!string.IsNullOrEmpty(KilnSessionFirerUid))
            {
                tree.SetString(BeeHiveKilnFirerStation.SessionFirerAttr, KilnSessionFirerUid);
            }
        }

        public void ReadFrom(ITreeAttribute tree)
        {
            ITreeAttribute? live = tree.GetTreeAttribute(ProsequorStackPedigree.LiveAttr);
            if (live != null)
            {
                ProsequorBlob blob = ProsequorBlob.ReadFrom(live);
                if (blob.HasPersistable)
                {
                    Blob = blob;
                }
            }

            DistillLocked = tree.GetBool(DistillLockedAttr)
                || tree.GetTreeAttribute(BoilerDistillBatch.Attr)?.GetBool(BoilerDistillBatch.LockedKey) == true;

            // Legacy distill side-table nested blob under prosequorDistillBatch.
            ITreeAttribute? distill = tree.GetTreeAttribute(BoilerDistillBatch.Attr);
            if (distill != null && DistillLocked && Blob.IsAnonymous)
            {
                ProsequorBlob distillBlob = ProsequorBlob.ReadFrom(distill);
                if (distillBlob.HasPersistable)
                {
                    Blob = distillBlob;
                }
            }

            ITreeAttribute? care = tree.GetTreeAttribute(ProsequorBlockPedigreeStation.CareCreditsAttr);
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
                    CareFlags = flags;
                }
            }

            float waterCredit = tree.GetFloat(WaterCreditAttr, 0f);
            if (waterCredit > AbsorbEpsilon)
            {
                WaterCredit = Math.Clamp(waterCredit, 0f, ProsequorBlockPedigreeStation.WaterCreditCap);
            }

            ITreeAttribute? absorb = tree.GetTreeAttribute(AbsorbAttr);
            float multiplier = absorb?.GetFloat(AbsorbMulKey, ProsequorBlockPedigreeStation.DefaultAbsorbMultiplier)
                ?? ProsequorBlockPedigreeStation.DefaultAbsorbMultiplier;
            float remainder = absorb?.GetFloat(AbsorbRemainderKey, 0f) ?? 0f;
            if (multiplier > ProsequorBlockPedigreeStation.DefaultAbsorbMultiplier + AbsorbEpsilon)
            {
                AbsorbMultiplier = GameMath.Clamp(
                    multiplier,
                    ProsequorBlockPedigreeStation.DefaultAbsorbMultiplier,
                    float.MaxValue);
            }

            if (remainder > AbsorbEpsilon)
            {
                AbsorbRemainder = remainder;
            }

            if (tree.HasAttribute(ClayFormXpStation.HighWaterAttr))
            {
                ClayHighWater = Math.Max(0, tree.GetInt(ClayFormXpStation.HighWaterAttr));
            }

            ClayRecipeKey = NormalizeOptional(tree.GetString(ClayFormXpStation.RecipeKeyAttr));

            if (tree.HasAttribute(CropClimateWindow.HalfDeltaAttr))
            {
                ClimateHalfDelta = Math.Max(0f, tree.GetFloat(CropClimateWindow.HalfDeltaAttr));
            }

            if (tree.HasAttribute(SaplingGrowthDuration.GrowthTimeMultiplierAttr))
            {
                GrowthTimeMultiplier = GameMath.Clamp(
                    tree.GetFloat(SaplingGrowthDuration.GrowthTimeMultiplierAttr),
                    MinGrowthMul,
                    float.MaxValue);
            }
            else if (tree.HasAttribute(BushCuttingGrowthDuration.GrowthTimeMultiplierAttr))
            {
                GrowthTimeMultiplier = GameMath.Clamp(
                    tree.GetFloat(BushCuttingGrowthDuration.GrowthTimeMultiplierAttr),
                    MinGrowthMul,
                    float.MaxValue);
            }

            if (tree.HasAttribute(FruitTreeCuttingSuccess.EstablishChanceAttr))
            {
                EstablishChance = GameMath.Clamp(
                    tree.GetFloat(FruitTreeCuttingSuccess.EstablishChanceAttr),
                    0f,
                    1f);
            }

            CementationPaid = tree.GetBool(CementationXpStation.PaidAttr);

            MoldToolPourerUid = NormalizeOptional(tree.GetString(MoldCastXpStation.ToolPourerAttr));
            MoldToolPaid = tree.GetBool(MoldCastXpStation.ToolPaidAttr);
            MoldLeftPourerUid = NormalizeOptional(tree.GetString(MoldCastXpStation.LeftPourerAttr));
            MoldRightPourerUid = NormalizeOptional(tree.GetString(MoldCastXpStation.RightPourerAttr));
            MoldLeftPaid = tree.GetBool(MoldCastXpStation.LeftPaidAttr);
            MoldRightPaid = tree.GetBool(MoldCastXpStation.RightPaidAttr);

            CollectXp = tree.GetBool(Prosequor.Xp.CollectXpStamp.AttrKey);

            LastInteractorUid = NormalizeOptional(tree.GetString(FirepitProcessStarterStation.LastInteractorAttr))
                ?? NormalizeOptional(tree.GetString(OvenCookStarterStation.LastInteractorAttr));

            KilnLastIgniterUid = NormalizeOptional(tree.GetString(BeeHiveKilnFirerStation.LastIgniterAttr));
            KilnSessionFirerUid = NormalizeOptional(tree.GetString(BeeHiveKilnFirerStation.SessionFirerAttr));
        }

        static string? NormalizeOptional(string? value) =>
            string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    /// <summary>Local X/Z (5 bits) plus Y in the high 16 bits. Chunk-relative.</summary>
    public static int Pack(BlockPos pos) =>
        ((pos.Y & 0xFFFF) << 16) | ((pos.Z & 31) << 8) | (pos.X & 31);

    /// <summary>
    /// Live entries for a loaded chunk. The blob under <see cref="ModDataKey"/> is the
    /// save record only: parsed once on first touch, encoded once on snapshot.
    /// Not stored on <see cref="IWorldChunk.LiveModData"/> — the game protobuf-writes
    /// that dictionary back over moddata on save.
    /// </summary>
    sealed class ChunkEntries
    {
        public readonly Dictionary<int, Box> Boxes = new();

        public byte[]? Raw;

        public bool Loaded;

        public bool Dirty;
    }

    static readonly ConditionalWeakTable<IWorldChunk, ChunkEntries> Live = new();

    static readonly object DirtyGate = new();

    static readonly List<WeakReference<IWorldChunk>> DirtyChunks = new();

    static ChunkEntries CacheOf(IWorldChunk chunk) =>
        Live.GetValue(chunk, static _ => new ChunkEntries());

    public static bool TryGet(IWorldAccessor? world, BlockPos? pos, out Box box)
    {
        if (world?.BlockAccessor != null && pos != null)
        {
            IWorldChunk? chunk = world.BlockAccessor.GetChunkAtBlockPos(pos);
            if (chunk != null)
            {
                return TryGetFromChunk(chunk, pos, out box);
            }
        }

        box = new Box();
        return false;
    }

    /// <summary>Read an entry from a chunk that already holds moddata (e.g. after FromBytes).</summary>
    public static bool TryGetFromChunk(IWorldChunk? chunk, BlockPos? pos, out Box box)
    {
        if (chunk != null && pos != null)
        {
            ChunkEntries entries = Ensure(chunk);
            if (entries.Boxes.TryGetValue(Pack(pos), out Box? stored) && stored != null)
            {
                box = stored;
                return true;
            }
        }

        box = new Box();
        return false;
    }

    public static Box GetOrCreate(IWorldAccessor world, BlockPos pos)
    {
        if (TryGet(world, pos, out Box existing))
        {
            return existing;
        }

        return new Box();
    }

    public static void Set(IWorldAccessor? world, BlockPos? pos, Box box)
    {
        if (world?.Side != EnumAppSide.Server || world.BlockAccessor == null || pos == null)
        {
            return;
        }

        if (box == null || !box.HasPersistable)
        {
            Clear(world, pos);
            return;
        }

        IWorldChunk? chunk = world.BlockAccessor.GetChunkAtBlockPos(pos);
        if (chunk == null)
        {
            return;
        }

        ChunkEntries entries = Ensure(chunk);
        entries.Boxes[Pack(pos)] = box;
        MarkDirty(chunk, entries);
    }

    public static void Clear(IWorldAccessor? world, BlockPos? pos)
    {
        if (world?.Side != EnumAppSide.Server || world.BlockAccessor == null || pos == null)
        {
            return;
        }

        IWorldChunk? chunk = world.BlockAccessor.GetChunkAtBlockPos(pos);
        if (chunk == null)
        {
            return;
        }

        ChunkEntries entries = Ensure(chunk);
        if (!entries.Boxes.Remove(Pack(pos)))
        {
            return;
        }

        MarkDirty(chunk, entries);
    }

    /// <summary>
    /// Encode every dirty live map into chunk moddata. Runs on world save (before the
    /// engine copies moddata) and again from dispose so an unpatch cannot skip the write.
    /// </summary>
    public static void FlushAllDirty()
    {
        lock (DirtyGate)
        {
            for (int i = 0; i < DirtyChunks.Count; i++)
            {
                if (DirtyChunks[i].TryGetTarget(out IWorldChunk? chunk) && chunk != null)
                {
                    FlushIfDirty(chunk);
                }
            }

            DirtyChunks.Clear();
        }
    }

    /// <summary>
    /// Encode dirty live entries into <see cref="ModDataKey"/> before the game copies
    /// chunk moddata (save, pack, or client packet). No-op when clean or untouched.
    /// </summary>
    public static void FlushIfDirty(IWorldChunk? chunk)
    {
        if (chunk == null || !Live.TryGetValue(chunk, out ChunkEntries? entries) || entries == null || !entries.Dirty)
        {
            return;
        }

        if (entries.Boxes.Count == 0)
        {
            chunk.RemoveModdata(ModDataKey);
            entries.Raw = null;
        }
        else
        {
            TreeAttribute root = new();
            foreach (KeyValuePair<int, Box> kv in entries.Boxes)
            {
                TreeAttribute entry = new();
                kv.Value.WriteTo(entry);
                root[PackKey(kv.Key)] = entry;
            }

            byte[] raw = root.ToBytes();
            chunk.SetModdata(ModDataKey, raw);
            entries.Raw = raw;
        }

        entries.Loaded = true;
        entries.Dirty = false;
    }

    static void MarkDirty(IWorldChunk chunk, ChunkEntries entries)
    {
        if (!entries.Dirty)
        {
            lock (DirtyGate)
            {
                DirtyChunks.Add(new WeakReference<IWorldChunk>(chunk));
            }
        }

        entries.Dirty = true;
        chunk.MarkModified();
    }

    static ChunkEntries Ensure(IWorldChunk chunk)
    {
        ChunkEntries entries = CacheOf(chunk);
        if (entries.Dirty)
        {
            return entries;
        }

        byte[]? raw = chunk.GetModdata(ModDataKey);
        if (entries.Loaded && ReferenceEquals(entries.Raw, raw))
        {
            return entries;
        }

        entries.Boxes.Clear();
        entries.Raw = raw;
        entries.Loaded = true;
        entries.Dirty = false;

        if (raw == null || raw.Length == 0)
        {
            return entries;
        }

        TreeAttribute root = new();
        root.FromBytes(raw);
        foreach (KeyValuePair<string, IAttribute> kv in root)
        {
            if (!int.TryParse(
                    kv.Key,
                    System.Globalization.NumberStyles.HexNumber,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out int packed))
            {
                continue;
            }

            ITreeAttribute? entry = root.GetTreeAttribute(kv.Key);
            if (entry == null)
            {
                continue;
            }

            Box box = new();
            box.ReadFrom(entry);
            if (box.HasPersistable)
            {
                entries.Boxes[packed] = box;
            }
        }

        return entries;
    }

    static string PackKey(int packed) => packed.ToString("X");
}
