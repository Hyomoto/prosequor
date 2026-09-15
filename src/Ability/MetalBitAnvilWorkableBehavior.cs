using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;
using Vintagestory.GameContent;

namespace Prosequor.Ability;

/// <summary>
/// Makes <c>metalbit-*</c> anvil-workable for recipes/tier/temp. Placement itself is
/// aim-aware and lives in <see cref="AnvilBitsForgingOps"/> + <c>TryPut</c> aim scope —
/// no fixed center-box fill.
/// </summary>
public sealed class MetalBitAnvilWorkableBehavior : CollectibleBehavior, IAnvilWorkable
{
    public const string ClassName = "ProsequorMetalBitWorkable";

    ICoreAPI? api;

    public MetalBitAnvilWorkableBehavior(CollectibleObject collObj)
        : base(collObj)
    {
    }

    public override void OnLoaded(ICoreAPI api)
    {
        base.OnLoaded(api);
        this.api = api;
    }

    public static bool IsMetalBit(CollectibleObject? obj) => AnvilBitsForgingOps.IsMetalBit(obj);

    public static void TryAddTo(CollectibleObject obj, ICoreAPI api)
    {
        if (obj == null || api == null || !IsMetalBit(obj))
        {
            return;
        }

        CollectibleBehavior[] existing = obj.CollectibleBehaviors ?? Array.Empty<CollectibleBehavior>();
        for (int i = 0; i < existing.Length; i++)
        {
            if (existing[i] is MetalBitAnvilWorkableBehavior)
            {
                return;
            }
        }

        MetalBitAnvilWorkableBehavior behavior = new(obj);
        behavior.OnLoaded(api);
        obj.CollectibleBehaviors = existing.Append(behavior);
    }

    public static void AttachAll(ICoreAPI api)
    {
        if (api?.World?.Items == null)
        {
            return;
        }

        foreach (Item item in api.World.Items)
        {
            TryAddTo(item, api);
        }
    }

    public int GetRequiredAnvilTier(ItemStack stack)
    {
        string key = AnvilBitsForgingOps.ResolveBitMetal(stack);
        int tier = 0;
        if (api?.ModLoader.GetModSystem<SurvivalCoreSystem>(true)?.metalsByCode
                .TryGetValue(key, out MetalPropertyVariant? metal) == true
            && metal != null)
        {
            tier = metal.Tier - 1;
        }

        JsonObject? attributes = stack?.Collectible?.Attributes;
        if (attributes != null && attributes["requiresAnvilTier"].Exists)
        {
            tier = attributes["requiresAnvilTier"].AsInt(tier);
        }

        return tier;
    }

    public List<SmithingRecipe> GetMatchingRecipes(ItemStack stack)
    {
        if (api == null)
        {
            return new List<SmithingRecipe>();
        }

        ItemStack? ingotStack = GetBaseMaterial(stack);
        if (ingotStack == null)
        {
            return new List<SmithingRecipe>();
        }

        return ApiAdditions.GetSmithingRecipes(api)
            .Where(r => r.Ingredient != null && r.Ingredient.SatisfiesAsIngredient(ingotStack))
            .OrderBy(r => r.Output?.ResolvedItemstack?.Collectible?.Code?.ToString() ?? "")
            .ToList();
    }

    public bool CanWork(ItemStack stack)
    {
        if (api?.World == null || stack == null)
        {
            return false;
        }

        float temperature = stack.Collectible.GetTemperature(api.World, stack);
        float meltingPoint = stack.Collectible.GetMeltingPoint(
            api.World,
            null,
            new DummySlot(stack));
        JsonObject? attributes = stack.Collectible.Attributes;
        if (attributes != null && attributes["workableTemperature"].Exists)
        {
            return attributes["workableTemperature"].AsFloat(meltingPoint / 2f) <= temperature;
        }

        return temperature >= meltingPoint / 2f;
    }

    public ItemStack? TryPlaceOn(ItemStack stack, BlockEntityAnvil beAnvil)
    {
        if (api == null || stack == null || beAnvil == null || !CanWork(stack))
        {
            return null;
        }

        // Existing work only — Bits Forging never seeds a blank anvil.
        ItemStack? work = beAnvil.WorkItemStack;
        if (work == null)
        {
            AnvilBitsForgingOps.ClientError(
                api,
                this,
                "needworkitem",
                Lang.Get("prosequor:ingameerror-bitsforging-needwork"));
            return null;
        }

        if (work.Collectible is ItemWorkItem { isBlisterSteel: not false })
        {
            return null;
        }

        string bitMetal = AnvilBitsForgingOps.ResolveBitMetal(stack);
        string? workMetal = work.Collectible?.Variant?["metal"];
        if (!string.Equals(workMetal, bitMetal, StringComparison.OrdinalIgnoreCase))
        {
            AnvilBitsForgingOps.ClientError(
                api,
                this,
                "notequal",
                Lang.Get("Must be the same metal to add voxels"));
            return null;
        }

        Vec3i? aim = AnvilBitAimScope.Current;
        if (aim == null
            || !AnvilVoxelGrid.InBounds(aim.X, aim.Y, aim.Z)
            || beAnvil.Voxels == null
            || beAnvil.Voxels[aim.X, aim.Y, aim.Z] != AnvilVoxelGrid.Metal)
        {
            AnvilBitsForgingOps.ClientError(
                api,
                this,
                "aimmetal",
                Lang.Get("prosequor:ingameerror-bitsforging-aimmetal"));
            return null;
        }

        int need = AnvilBitsForgingOps.VoxelsPerBit;
        int placed = AnvilVoxelGrid.TryPlaceBitVoxels(
            beAnvil.Voxels,
            aim.X,
            aim.Y,
            aim.Z,
            need);
        if (placed < need)
        {
            AnvilBitsForgingOps.ClientError(
                api,
                this,
                "requireshammering",
                Lang.Get("Try hammering down before adding additional voxels"));
            return null;
        }

        // Vanilla TryPut expects a non-null stack when work already exists; return the
        // existing work reference so recipe/consume logic still runs.
        CollectibleObject? workColl = work.Collectible;
        if (api.World == null || workColl == null)
        {
            return null;
        }

        float temp = stack.Collectible.GetTemperature(api.World, stack);
        workColl.SetTemperature(api.World, work, temp, true);
        return work;
    }

    public ItemStack? GetBaseMaterial(ItemStack stack)
    {
        if (api?.World == null)
        {
            return null;
        }

        string metal = AnvilBitsForgingOps.ResolveBitMetal(stack);
        Item? ingot = api.World.GetItem(new AssetLocation("ingot-" + metal));
        return ingot == null ? null : new ItemStack(ingot, 1);
    }

    public EnumHelveWorkableMode GetHelveWorkableMode(ItemStack stack, BlockEntityAnvil beAnvil) =>
        EnumHelveWorkableMode.NotWorkable;

    public int VoxelCountForHandbook(ItemStack stack) => AnvilBitsForgingOps.VoxelsPerBit;
}
