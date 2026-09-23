using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace Prosequor.Ability;

/// <summary>
/// Sparse block-entity pedigree on chunk moddata. Only positions that were
/// stamped get an entry; overwrite replaces; remove clears. Same local pack
/// as <see cref="ForagePlayerPlaced"/>. Live blob is the unit; other keys on
/// the same entry are host process / farmland / plant / anti-replay state.
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

        public int AnvilHighWater;

        public string? AnvilRecipeKey;

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
            || AnvilHighWater > 0
            || !string.IsNullOrEmpty(AnvilRecipeKey)
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
            AnvilHighWater = 0;
            AnvilRecipeKey = null;
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

            if (AnvilHighWater > 0)
            {
                tree.SetInt(AnvilXpStation.HighWaterAttr, AnvilHighWater);
            }

            if (!string.IsNullOrEmpty(AnvilRecipeKey))
            {
                tree.SetString(AnvilXpStation.RecipeKeyAttr, AnvilRecipeKey);
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

            if (tree.HasAttribute(AnvilXpStation.HighWaterAttr))
            {
                AnvilHighWater = Math.Max(0, tree.GetInt(AnvilXpStation.HighWaterAttr));
            }

            AnvilRecipeKey = NormalizeOptional(tree.GetString(AnvilXpStation.RecipeKeyAttr));

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

    public static bool TryGet(IWorldAccessor? world, BlockPos? pos, out Box box)
    {
        box = new Box();
        if (world?.BlockAccessor == null || pos == null)
        {
            return false;
        }

        IWorldChunk? chunk = world.BlockAccessor.GetChunkAtBlockPos(pos);
        return chunk != null && TryGetFromChunk(chunk, pos, out box);
    }

    /// <summary>Read an entry from a chunk that already holds moddata (e.g. after FromBytes).</summary>
    public static bool TryGetFromChunk(IWorldChunk? chunk, BlockPos? pos, out Box box)
    {
        box = new Box();
        if (chunk == null || pos == null)
        {
            return false;
        }

        byte[]? raw = chunk.GetModdata(ModDataKey);
        if (raw == null || raw.Length == 0)
        {
            return false;
        }

        TreeAttribute root = new();
        root.FromBytes(raw);
        ITreeAttribute? entry = root.GetTreeAttribute(PackKey(Pack(pos)));
        if (entry == null)
        {
            return false;
        }

        box.ReadFrom(entry);
        return box.HasPersistable;
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

        if (!TryGetRoot(world, pos, out TreeAttribute? root, out IWorldChunk? chunk) || chunk == null)
        {
            return;
        }

        root ??= new TreeAttribute();
        TreeAttribute entry = new();
        box.WriteTo(entry);
        root[PackKey(Pack(pos))] = entry;
        WriteRoot(chunk, root);
    }

    public static void Clear(IWorldAccessor? world, BlockPos? pos)
    {
        if (world?.Side != EnumAppSide.Server || world.BlockAccessor == null || pos == null)
        {
            return;
        }

        if (!TryGetRoot(world, pos, out TreeAttribute? root, out IWorldChunk? chunk)
            || chunk == null
            || root == null)
        {
            return;
        }

        string key = PackKey(Pack(pos));
        if (!root.HasAttribute(key))
        {
            return;
        }

        root.RemoveAttribute(key);
        WriteRoot(chunk, root);
    }

    static string PackKey(int packed) => packed.ToString("X");

    static bool TryGetRoot(
        IWorldAccessor world,
        BlockPos pos,
        out TreeAttribute? root,
        out IWorldChunk? chunk)
    {
        root = null;
        chunk = world.BlockAccessor.GetChunkAtBlockPos(pos);
        if (chunk == null)
        {
            return false;
        }

        byte[]? raw = chunk.GetModdata(ModDataKey);
        if (raw == null || raw.Length == 0)
        {
            root = null;
            return true;
        }

        root = new TreeAttribute();
        root.FromBytes(raw);
        return true;
    }

    static void WriteRoot(IWorldChunk chunk, TreeAttribute root)
    {
        if (root.Count == 0)
        {
            chunk.RemoveModdata(ModDataKey);
        }
        else
        {
            chunk.SetModdata(ModDataKey, root.ToBytes());
        }

        chunk.MarkModified();
    }
}
