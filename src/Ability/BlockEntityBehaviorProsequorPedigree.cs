using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace Prosequor.Ability;

/// <summary>
/// Holds block-entity pedigree on the entity itself so
/// <see cref="BlockEntity.ToTreeAttributes"/> writes it from the behavior loop
/// before the method returns. The chunk save stores that tree.
/// </summary>
public sealed class BlockEntityBehaviorProsequorPedigree : BlockEntityBehavior
{
    public const string ClassName = "ProsequorPedigree";

    public const string CareCreditsAttr = ProsequorBlockPedigreeStation.CareCreditsAttr;

    const string AbsorbAttr = "prosequorFarmlandAbsorb";
    const string AbsorbMulKey = "mul";
    const string AbsorbRemainderKey = "remainder";
    const string WaterCreditAttr = "prosequorWaterCredit";
    const float AbsorbEpsilon = 0.0001f;

    public ProsequorBlob Blob = ProsequorBlob.Empty;

    public Dictionary<string, int>? CareFlags;

    public float AbsorbMultiplier = ProsequorBlockPedigreeStation.DefaultAbsorbMultiplier;

    public float AbsorbRemainder;

    public float WaterCredit;

    public BlockEntityBehaviorProsequorPedigree(BlockEntity blockentity)
        : base(blockentity)
    {
    }

    public bool HasPersistable =>
        !Blob.IsAnonymous
        || (CareFlags != null && CareFlags.Count > 0)
        || WaterCredit > ProsequorBlockPedigreeStation.WaterCreditMinGain
        || AbsorbMultiplier > ProsequorBlockPedigreeStation.DefaultAbsorbMultiplier + AbsorbEpsilon
        || AbsorbRemainder > AbsorbEpsilon;

    public override void ToTreeAttributes(ITreeAttribute tree)
    {
        if (!Blob.IsAnonymous)
        {
            Blob.WriteTo(tree.GetOrAddTreeAttribute(ProsequorStackPedigree.LiveAttr));
        }

        if (CareFlags != null && CareFlags.Count > 0)
        {
            ITreeAttribute care = tree.GetOrAddTreeAttribute(CareCreditsAttr);
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

        if (AbsorbMultiplier <= ProsequorBlockPedigreeStation.DefaultAbsorbMultiplier + AbsorbEpsilon
            && AbsorbRemainder <= AbsorbEpsilon)
        {
            return;
        }

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

    public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldAccessForResolve)
    {
        ITreeAttribute? live = tree.GetTreeAttribute(ProsequorStackPedigree.LiveAttr);
        if (live != null)
        {
            ProsequorBlob blob = ProsequorBlob.ReadFrom(live);
            if (!blob.IsAnonymous)
            {
                Blob = blob;
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
    }

    public void ClearPedigree()
    {
        Blob = ProsequorBlob.Empty;
        CareFlags = null;
        WaterCredit = 0f;
    }
}
