using System.Reflection;
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace Prosequor.Ability;

/// <summary>
/// Fruit press: sole contributor on mash fill, Perfect Presser quantity on juice litres,
/// and MakerUid on minted juice before <c>TryPutLiquid</c> (existing liquid pedigree merges).
/// Each accepted transfer emits a <c>crafted</c> deed for the new units only.
/// </summary>
public static class FruitPressMutateProcessPatches
{
    static readonly AccessTools.FieldRef<BlockEntityFruitPress, int>? DryStackSizeField =
        AccessTools.FieldRefAccess<BlockEntityFruitPress, int>("dryStackSize");

    static readonly PropertyInfo? JuiceableLitresLeftProp =
        AccessTools.Property(typeof(BlockEntityFruitPress), "juiceableLitresLeft");

    static readonly PropertyInfo? JuiceableLitresTransferedProp =
        AccessTools.Property(typeof(BlockEntityFruitPress), "juiceableLitresTransfered");

    [ThreadStatic]
    static string? juiceMakerUid;

    [ThreadStatic]
    static IWorldAccessor? juiceWorld;

    public sealed class MashFillState
    {
        public double LitresBefore;
        public string? PlayerUid;
    }

    [HarmonyPatch(typeof(BlockEntityFruitPress), "InteractMashContainer")]
    public static class FruitPressMashFillQuantityPatch
    {
        [HarmonyPrefix]
        public static void Prefix(
            BlockEntityFruitPress __instance,
            IPlayer byPlayer,
            out MashFillState __state)
        {
            __state = new MashFillState
            {
                LitresBefore = ReadLitresLeft(__instance),
                PlayerUid = byPlayer?.PlayerUID
            };
        }

        [HarmonyPostfix]
        public static void Postfix(
            BlockEntityFruitPress __instance,
            bool __result,
            MashFillState __state)
        {
            if (!__result
                || __instance.Api?.World?.Side != EnumAppSide.Server
                || string.IsNullOrWhiteSpace(__state.PlayerUid))
            {
                return;
            }

            double after = ReadLitresLeft(__instance);
            double delta = after - __state.LitresBefore;
            if (delta <= 0.0001)
            {
                return;
            }

            ProsequorBlockPedigreeStation.StampSoleContributor(__instance, __state.PlayerUid);

            ItemStack? juice = TryResolveJuiceStack(__instance);
            if (juice == null)
            {
                return;
            }

            float scaled = MutateProcessStation.ResolveQuantity(
                __instance.Api.World,
                __state.PlayerUid,
                juice,
                AbilityBootstrap.TokenFruitPress,
                (float)delta);
            if (!float.IsFinite(scaled) || scaled <= 0f)
            {
                return;
            }

            double nextLeft = __state.LitresBefore + scaled;
            if (Math.Abs(nextLeft - after) < 0.0001)
            {
                return;
            }

            WriteLitresLeft(__instance, nextLeft);
            RecomputeDryStackSize(__instance);
            __instance.MarkDirty(redrawOnClient: true);
        }
    }

    [HarmonyPatch(typeof(BlockEntityFruitPress), "onTick100msServer")]
    public static class FruitPressSqueezeMakerScopePatch
    {
        [HarmonyPrefix]
        public static void Prefix(BlockEntityFruitPress __instance)
        {
            juiceMakerUid = null;
            juiceWorld = null;
            if (__instance.Api?.World?.Side != EnumAppSide.Server)
            {
                return;
            }

            juiceWorld = __instance.Api.World;
            if (ProsequorBlockPedigreeStation.TryGetSoleContributor(__instance, out string? uid)
                && !string.IsNullOrWhiteSpace(uid))
            {
                juiceMakerUid = uid;
            }
        }

        [HarmonyFinalizer]
        public static void Finalizer()
        {
            juiceMakerUid = null;
            juiceWorld = null;
        }
    }

    /// <summary>
    /// Runs before liquid pedigree capture so SourceBlob includes the press filler MakerUid.
    /// Postfix pays cooking XP for the accepted stack-size delta only (a later tick does not
    /// re-pay the bucket total).
    /// </summary>
    [HarmonyPatch(
        typeof(BlockLiquidContainerBase),
        nameof(BlockLiquidContainerBase.TryPutLiquid),
        typeof(ItemStack),
        typeof(ItemStack),
        typeof(float))]
    [HarmonyPriority(Priority.First)]
    public static class FruitPressJuiceMakerStampPatch
    {
        [HarmonyPrefix]
        public static void Prefix(ItemStack liquidStack)
        {
            if (string.IsNullOrWhiteSpace(juiceMakerUid) || liquidStack == null)
            {
                return;
            }

            CraftAttribution.StampMakerUid(liquidStack, juiceMakerUid);
        }

        [HarmonyPostfix]
        public static void Postfix(
            BlockLiquidContainerBase __instance,
            ItemStack containerStack,
            int __result)
        {
            if (__result <= 0
                || string.IsNullOrWhiteSpace(juiceMakerUid)
                || containerStack == null
                || juiceWorld?.Side != EnumAppSide.Server)
            {
                return;
            }

            ItemStack? content = __instance?.GetContent(containerStack);
            if (content == null)
            {
                return;
            }

            CraftedProductXp.Emit(juiceWorld, juiceMakerUid, content, __result);
        }
    }

    static double ReadLitresLeft(BlockEntityFruitPress press)
    {
        if (JuiceableLitresLeftProp == null)
        {
            return 0;
        }

        object? value = JuiceableLitresLeftProp.GetValue(press);
        return value is double d ? d : 0;
    }

    static void WriteLitresLeft(BlockEntityFruitPress press, double litres)
    {
        JuiceableLitresLeftProp?.SetValue(press, litres);
    }

    static double ReadLitresTransfered(BlockEntityFruitPress press)
    {
        if (JuiceableLitresTransferedProp == null)
        {
            return 0;
        }

        object? value = JuiceableLitresTransferedProp.GetValue(press);
        return value is double d ? d : 0;
    }

    static ItemStack? TryResolveJuiceStack(BlockEntityFruitPress press)
    {
        ItemStack? mash = press.MashSlot?.Itemstack;
        if (mash == null)
        {
            return null;
        }

        JuiceableProperties? props = press.getJuiceableProps(mash);
        ItemStack? liquid = props?.LiquidStack?.ResolvedItemstack;
        return liquid == null ? null : liquid.Clone();
    }

    static void RecomputeDryStackSize(BlockEntityFruitPress press)
    {
        if (DryStackSizeField == null || press.Api?.World == null)
        {
            return;
        }

        ItemStack? mash = press.MashSlot?.Itemstack;
        if (mash == null)
        {
            return;
        }

        JuiceableProperties? props = press.getJuiceableProps(mash);
        float ratio = props?.PressedDryRatio ?? 1f;
        float total = (float)(ReadLitresLeft(press) + ReadLitresTransfered(press));
        DryStackSizeField(press) = GameMath.RoundRandom(press.Api.World.Rand, total * ratio);
    }
}
