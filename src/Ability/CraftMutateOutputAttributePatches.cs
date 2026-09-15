using System.Reflection;
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.Common;
using Vintagestory.GameContent;

namespace Prosequor.Ability;

/// <summary>
/// Craft-time quantity floor preview, attribute stamps, and mutator read-back.
/// </summary>
public static class CraftMutateOutputAttributePatches
{
    /// <summary>
    /// After vanilla fills the output slot from a match — apply floor(qty) on client + server.
    /// </summary>
    [HarmonyPatch(typeof(InventoryCraftingGrid), "FoundMatch")]
    public static class InventoryCraftingGridFoundMatchQuantityPatch
    {
        [HarmonyPostfix]
        public static void Postfix(InventoryCraftingGrid __instance)
        {
            if (__instance == null || __instance.Count <= 0)
            {
                return;
            }

            ItemSlot outputSlot = __instance[__instance.Count - 1];
            if (outputSlot is DummySlot || outputSlot?.Itemstack?.Collectible == null)
            {
                return;
            }

            if (__instance is not InventoryBasePlayer playerInv || playerInv.Player?.Entity == null)
            {
                return;
            }

            ItemSlot[] inputs = new ItemSlot[Math.Max(0, __instance.Count - 1)];
            for (int i = 0; i < inputs.Length; i++)
            {
                inputs[i] = __instance[i];
            }

            CraftMutateOutputStation.ApplyQuantityPreview(playerInv.Player, outputSlot, inputs);

            if (__instance.Api?.Side == EnumAppSide.Server
                || __instance.Api?.World?.Side == EnumAppSide.Server)
            {
                CraftMutateOutputStation.StampCraftMaker(playerInv.Player, outputSlot.Itemstack);
            }
        }
    }

    [HarmonyPatch(typeof(CollectibleObject), nameof(CollectibleObject.OnCreatedByCrafting))]
    public static class CollectibleOnCreatedByCraftingAttributesPatch
    {
        [HarmonyPostfix]
        public static void Postfix(ItemSlot[] allInputSlots, ItemSlot outputSlot, IRecipeBase byRecipe)
        {
            if (outputSlot is DummySlot || outputSlot?.Itemstack?.Collectible == null)
            {
                return;
            }

            if (byRecipe?.Name?.Path != null
                && byRecipe.Name.Path.Contains("repair", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (outputSlot.Inventory is not InventoryBasePlayer playerInv
                || playerInv.Player?.Entity == null)
            {
                return;
            }

            IPlayer player = playerInv.Player;

            // Also apply here so non-grid create paths stay covered; FoundMatch covers the grid UI.
            CraftMutateOutputStation.ApplyQuantityPreview(player, outputSlot, allInputSlots);

            if (player.Entity.World?.Side != EnumAppSide.Server
                && player.Entity.Api?.Side != EnumAppSide.Server)
            {
                return;
            }

            CraftMutateOutputStation.ApplyAttributes(player, outputSlot.Itemstack, allInputSlots);
            outputSlot.MarkDirty();
        }
    }

    [HarmonyPatch(typeof(CollectibleObject), nameof(CollectibleObject.GetMaxDurability))]
    public static class CollectibleGetMaxDurabilityCraftModPatch
    {
        [HarmonyPostfix]
        public static void Postfix(ItemStack itemstack, ref int __result)
        {
            if (itemstack == null || __result <= 0)
            {
                return;
            }

            float factor = CraftAttributeMods.GetFactor(itemstack, DurabilityAttributeMutator.KeyName);
            if (Math.Abs(factor - 1f) < 0.0001f)
            {
                return;
            }

            int scaled = (int)Math.Round(__result * (double)factor);
            __result = Math.Max(1, scaled);
        }
    }

    [HarmonyPatch(typeof(CollectibleBehaviorWearable), nameof(CollectibleBehaviorWearable.GetMaxWarmth))]
    public static class WearableGetMaxWarmthCraftModPatch
    {
        [HarmonyPostfix]
        public static void Postfix(ItemSlot inslot, ref float __result)
        {
            if (inslot?.Itemstack == null || Math.Abs(__result) < 0.0001f)
            {
                return;
            }

            float factor = CraftAttributeMods.GetFactor(inslot.Itemstack, WarmthAttributeMutator.KeyName);
            if (Math.Abs(factor - 1f) < 0.0001f)
            {
                return;
            }

            __result *= factor;
        }
    }

    [HarmonyPatch(
        typeof(CollectibleBehaviorWearable),
        nameof(CollectibleBehaviorWearable.GetProtectionModifiers))]
    public static class WearableGetProtectionModifiersCraftModPatch
    {
        [HarmonyPostfix]
        public static void Postfix(ItemSlot slot, ref ProtectionModifiers __result)
        {
            if (__result == null || slot?.Itemstack == null)
            {
                return;
            }

            float factor = CraftAttributeMods.GetFactor(slot.Itemstack, ProtectionAttributeMutator.KeyName);
            if (Math.Abs(factor - 1f) < 0.0001f)
            {
                return;
            }

            __result = ScaleProtection(__result, factor);
        }

        static ProtectionModifiers ScaleProtection(ProtectionModifiers src, float factor)
        {
            return new ProtectionModifiers
            {
                RelativeProtection = GameMath.Clamp(src.RelativeProtection * factor, 0f, 1f),
                FlatDamageReduction = src.FlatDamageReduction * factor,
                ProtectionTier = src.ProtectionTier,
                HighDamageTierResistant = src.HighDamageTierResistant,
                PerTierFlatDamageReductionLoss = src.PerTierFlatDamageReductionLoss,
                PerTierRelativeProtectionLoss = src.PerTierRelativeProtectionLoss
            };
        }
    }

    /// <summary>
    /// HoD <c>CoolingManager.GetMaxCooling</c> when that mod is loaded; no-op otherwise.
    /// </summary>
    public static void TryPatchOptionalReaders(Harmony harmony)
    {
        if (harmony == null)
        {
            return;
        }

        Type? coolingManager = AccessTools.TypeByName("HydrateOrDiedrate.Hot_Weather.CoolingManager");
        MethodInfo? getMaxCooling = coolingManager == null
            ? null
            : AccessTools.Method(coolingManager, "GetMaxCooling", [typeof(ItemStack)]);
        if (getMaxCooling == null)
        {
            return;
        }

        harmony.Patch(
            getMaxCooling,
            postfix: new HarmonyMethod(
                typeof(CoolingManagerGetMaxCoolingCraftModPatch),
                nameof(CoolingManagerGetMaxCoolingCraftModPatch.Postfix)));
    }

    public static class CoolingManagerGetMaxCoolingCraftModPatch
    {
        public static void Postfix(ItemStack itemStack, ref float __result)
        {
            if (itemStack == null || Math.Abs(__result) < 0.0001f)
            {
                return;
            }

            float factor = CraftAttributeMods.GetFactor(itemStack, CoolingAttributeMutator.KeyName);
            if (Math.Abs(factor - 1f) < 0.0001f)
            {
                return;
            }

            __result *= factor;
        }
    }
}
