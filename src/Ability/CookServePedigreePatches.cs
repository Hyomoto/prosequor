using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace Prosequor.Ability;

/// <summary>
/// Copies cooked-pot quality and cook credit onto served meals. Vessel Created By is never replaced.
/// </summary>
public static class CookServePedigreePatches
{
    sealed class ServeScope
    {
        public ProsequorBlob SourceBlob = ProsequorBlob.Empty;
        public IWorldAccessor? World;
        public string? VesselMaker;
        public string? PotMaker;
        public BlockEntity? PotBe;
        public BlockPos? PotPos;
        public int PotBlockId;
        public int Depth;
    }

    [ThreadStatic]
    static ServeScope? active;

    public static void Begin(
        ProsequorBlob source,
        IWorldAccessor? world,
        string? vesselMaker,
        string? potMaker,
        BlockEntity? potBe)
    {
        if (!source.HasPersistable && string.IsNullOrEmpty(vesselMaker) && string.IsNullOrEmpty(potMaker))
        {
            return;
        }

        ServeScope scope = active ??= new ServeScope();
        if (scope.Depth == 0)
        {
            scope.SourceBlob = source;
            scope.World = world;
            scope.VesselMaker = vesselMaker;
            scope.PotMaker = potMaker;
            scope.PotBe = potBe;
            scope.PotPos = potBe?.Pos?.Copy();
            scope.PotBlockId = potBe?.Block?.BlockId ?? 0;
        }

        scope.Depth++;
    }

    public static void End()
    {
        ServeScope? scope = active;
        if (scope == null)
        {
            return;
        }

        scope.Depth--;
        if (scope.Depth > 0)
        {
            return;
        }

        scope.SourceBlob = ProsequorBlob.Empty;
        scope.World = null;
        scope.VesselMaker = null;
        scope.PotMaker = null;
        scope.PotBe = null;
        scope.PotPos = null;
        scope.PotBlockId = 0;
        active = null;
    }

    static bool TryGetActive(out ProsequorBlob blob, out IWorldAccessor? world)
    {
        blob = ProsequorBlob.Empty;
        world = null;
        ServeScope? scope = active;
        if (scope == null || scope.Depth <= 0)
        {
            return false;
        }

        blob = scope.SourceBlob;
        world = scope.World;
        return true;
    }

    static void ApplyToMealStack(ItemStack? meal, IWorldAccessor? world)
    {
        if (meal?.Collectible == null
            || !TryGetActive(out ProsequorBlob source, out IWorldAccessor? scopeWorld))
        {
            return;
        }

        MealHostCredit.ApplyServeToStack(meal, source, active?.VesselMaker, world ?? scopeWorld);
    }

    static void ApplyToMealBe(BlockEntity? mealBe, IWorldAccessor? world)
    {
        if (mealBe == null
            || !TryGetActive(out ProsequorBlob source, out IWorldAccessor? scopeWorld))
        {
            return;
        }

        MealHostCredit.ApplyServeToBe(mealBe, source, active?.VesselMaker, world ?? scopeWorld);
    }

    static void RestoreOuterPot(ItemStack? potStack)
    {
        ServeScope? scope = active;
        if (scope == null || scope.Depth != 1)
        {
            return;
        }

        MealHostCredit.RestoreMaker(potStack, scope.PotMaker);
        if (scope.PotBe != null)
        {
            MealHostCredit.RestoreBeMaker(scope.PotBe, scope.PotMaker, scope.SourceBlob);
        }

        RetainReplacedPot(scope);
    }

    static void RetainReplacedPot(ServeScope scope)
    {
        if (scope.PotPos == null || scope.World == null || scope.PotBlockId == 0)
        {
            return;
        }

        Block block = scope.World.BlockAccessor.GetBlock(scope.PotPos);
        if (block == null || block.BlockId == scope.PotBlockId)
        {
            return;
        }

        MealHostCredit.RetainBeMaker(scope.World.BlockAccessor.GetBlockEntity(scope.PotPos), scope.PotMaker);
    }

    static bool TryReadPotBlob(ItemStack? potStack, out ProsequorBlob blob) =>
        ProsequorStackPedigree.TryGetPrimaryBlob(potStack, out blob) && blob.HasPersistable;

    [HarmonyPatch(typeof(BlockEntityCookedContainer), nameof(BlockEntityCookedContainer.ServeInto))]
    public static class CookedContainerServeIntoPedigreePatch
    {
        [HarmonyPrefix]
        public static void Prefix(BlockEntityCookedContainer __instance, ItemSlot slot)
        {
            if (__instance?.Api?.World?.Side != EnumAppSide.Server)
            {
                return;
            }

            ProsequorBlockPedigreeStation.TryGetBlob(__instance, out ProsequorBlob blob);
            string? vesselMaker = CraftAttribution.TryGetMakerUid(slot?.Itemstack);
            if (!blob.HasPersistable && string.IsNullOrEmpty(vesselMaker) && string.IsNullOrEmpty(blob.MakerUid))
            {
                return;
            }

            Begin(blob, __instance.Api.World, vesselMaker, blob.MakerUid, __instance);
        }

        [HarmonyPostfix]
        public static void Postfix(
            BlockEntityCookedContainer __instance,
            IPlayer player,
            ItemSlot slot,
            bool __result)
        {
            try
            {
                if (!__result || __instance?.Api?.World?.Side != EnumAppSide.Server)
                {
                    return;
                }

                ApplyToMealStack(slot?.Itemstack, __instance.Api.World);
                RestoreOuterPot(null);
            }
            finally
            {
                End();
            }
        }
    }

    /// <summary>
    /// Catch meal stacks minted via SetContents during an active serve scope
    /// (covers multi-bowl GiveItemstack paths).
    /// </summary>
    [HarmonyPatch(
        typeof(BlockCookedContainerBase),
        nameof(BlockCookedContainerBase.SetContents),
        new[] { typeof(string), typeof(ItemStack), typeof(ItemStack[]), typeof(float) })]
    public static class CookedContainerSetContentsPedigreePatch
    {
        [HarmonyPostfix]
        public static void Postfix(ItemStack containerStack) =>
            ApplyToMealStack(containerStack, active?.World);
    }

    [HarmonyPatch(
        typeof(BlockMeal),
        nameof(BlockMeal.SetContents),
        new[] { typeof(string), typeof(ItemStack), typeof(ItemStack[]), typeof(float) })]
    public static class BlockMealSetContentsPedigreePatch
    {
        [HarmonyPostfix]
        public static void Postfix(ItemStack containerStack) =>
            ApplyToMealStack(containerStack, active?.World);
    }

    [HarmonyPatch(typeof(BlockCookedContainerBase), nameof(BlockCookedContainerBase.ServeIntoStack))]
    public static class ServeIntoStackPedigreePatch
    {
        [HarmonyPrefix]
        public static void Prefix(ItemSlot bowlSlot, ItemSlot potslot, IWorldAccessor world)
        {
            if (world?.Side != EnumAppSide.Server)
            {
                return;
            }

            if (!TryReadPotBlob(potslot?.Itemstack, out ProsequorBlob blob))
            {
                return;
            }

            Begin(
                blob,
                world,
                CraftAttribution.TryGetMakerUid(bowlSlot?.Itemstack),
                CraftAttribution.TryGetMakerUid(potslot?.Itemstack),
                potBe: null);
        }

        [HarmonyPostfix]
        public static void Postfix(ItemSlot bowlSlot, ItemSlot potslot, IWorldAccessor world, bool __result)
        {
            try
            {
                if (!__result || world?.Side != EnumAppSide.Server)
                {
                    return;
                }

                ApplyToMealStack(bowlSlot?.Itemstack, world);
                RestoreOuterPot(potslot?.Itemstack);
            }
            finally
            {
                End();
            }
        }
    }

    [HarmonyPatch(typeof(BlockCookedContainerBase), nameof(BlockCookedContainerBase.ServeIntoBowl))]
    public static class ServeIntoBowlPedigreePatch
    {
        [HarmonyPrefix]
        public static void Prefix(BlockPos pos, ItemSlot potslot, IWorldAccessor world)
        {
            if (world?.Side != EnumAppSide.Server)
            {
                return;
            }

            if (!TryReadPotBlob(potslot?.Itemstack, out ProsequorBlob blob))
            {
                return;
            }

            string? vesselMaker = null;
            if (pos != null
                && ProsequorBlockPedigreeStation.TryGetBlob(
                    world.BlockAccessor.GetBlockEntity(pos),
                    out ProsequorBlob bowl)
                && !string.IsNullOrEmpty(bowl.MakerUid))
            {
                vesselMaker = bowl.MakerUid;
            }

            Begin(
                blob,
                world,
                vesselMaker,
                CraftAttribution.TryGetMakerUid(potslot?.Itemstack),
                potBe: null);
        }

        [HarmonyPostfix]
        public static void Postfix(BlockPos pos, ItemSlot potslot, IWorldAccessor world)
        {
            try
            {
                if (world?.Side != EnumAppSide.Server || pos == null)
                {
                    return;
                }

                BlockEntity? be = world.BlockAccessor.GetBlockEntity(pos);
                ApplyToMealBe(be, world);
                RestoreOuterPot(potslot?.Itemstack);
            }
            finally
            {
                End();
            }
        }
    }
}
