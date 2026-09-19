using Prosequor.Ability.Hooks;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace Prosequor.Ability;

/// <summary>Thread-local player for clay-form private voxel ops during OnUseOver.</summary>
public static class ClayFormScope
{
    [ThreadStatic]
    static IPlayer? currentPlayer;

    public static IPlayer? CurrentPlayer => currentPlayer;

    public static void Begin(IPlayer player) => currentPlayer = player;

    public static void End() => currentPlayer = null;
}

/// <summary>Clay-form voxel mutations with Prosequor voxel-work assists.</summary>
public static class ClayFormVoxelOps
{
    public static Cuboidi LayerBounds(ClayFormingRecipe recipe, int layer)
    {
        Cuboidi bounds = new(8, 8, 8, 8, 8, 8);
        if (recipe == null || layer < 0 || layer >= 16)
        {
            return bounds;
        }

        for (int x = 0; x < 16; x++)
        {
            for (int z = 0; z < 16; z++)
            {
                if (recipe.Voxels[x, layer, z])
                {
                    bounds.X1 = Math.Min(bounds.X1, x);
                    bounds.X2 = Math.Max(bounds.X2, x);
                    bounds.Z1 = Math.Min(bounds.Z1, z);
                    bounds.Z2 = Math.Max(bounds.Z2, z);
                }
            }
        }

        return bounds;
    }

    public static bool TryRefill(BlockEntityClayForm form, ItemSlot slot, EntityAgent entity)
    {
        if (form == null || slot?.Itemstack == null || form.AvailableVoxels > 0)
        {
            return false;
        }

        if (slot.Itemstack.StackSize <= 0)
        {
            return false;
        }

        slot.TakeOut(1);
        slot.MarkDirty();
        int refill = entity is EntityPlayer ep && ep.Player != null
            ? VoxelWorkStation.ResolveVoxelRefill(ep.Player)
            : VoxelWorkStation.VanillaVoxelRefill;
        form.AvailableVoxels += refill;

        return true;
    }

    public static bool OnAdd(BlockEntityClayForm form, int layer, Vec3i voxelPos, int radius, IPlayer player)
    {
        if (voxelPos.Y != layer || layer < 0 || layer >= 16 || form.SelectedRecipe == null)
        {
            return false;
        }

        int assistRadius = VoxelWorkStation.RunInt(
            player,
            AbilityBootstrap.VerbClayForm,
            [AbilityBootstrap.OpPlaceTag],
            HookIds.AssistRadius,
            VoxelWorkStation.AssistRadiusOff);
        bool guardNonRecipe = assistRadius >= 0 && radius <= assistRadius;

        float conserveChance = VoxelWorkStation.RunFloat(
            player,
            AbilityBootstrap.VerbClayForm,
            [AbilityBootstrap.OpPlaceTag],
            HookIds.PlaceConservation,
            0f);

        bool result = false;
        Cuboidi bounds = LayerBounds(form.SelectedRecipe, layer);
        for (int i = -(int)Math.Ceiling(radius / 2f); i <= radius / 2; i++)
        {
            for (int j = -(int)Math.Ceiling(radius / 2f); j <= radius / 2; j++)
            {
                Vec3i pos = voxelPos.AddCopy(i, 0, j);
                if (!form.InBounds(pos, bounds) || pos.Y != layer)
                {
                    continue;
                }

                if (!form.Voxels[pos.X, pos.Y, pos.Z])
                {
                    if (guardNonRecipe && !form.SelectedRecipe.Voxels[pos.X, pos.Y, pos.Z])
                    {
                        continue;
                    }

                    if (conserveChance <= 0f
                        || player.Entity.World.Rand.NextDouble() >= conserveChance)
                    {
                        form.AvailableVoxels--;
                        if (form.AvailableVoxels < 0)
                        {
                            TryRefill(
                                form,
                                player.InventoryManager.ActiveHotbarSlot,
                                player.Entity);
                        }
                    }

                    result = true;
                }

                form.Voxels[pos.X, pos.Y, pos.Z] = true;
            }
        }

        if (result)
        {
            TryAwardProgress(form, player);
        }

        return result;
    }

    public static bool OnRemove(
        BlockEntityClayForm form,
        int layer,
        Vec3i voxelPos,
        BlockFacing facing,
        int radius,
        IPlayer player)
    {
        if (voxelPos.Y != layer || layer < 0 || layer >= 16 || form.SelectedRecipe == null)
        {
            return false;
        }

        int assistRadius = VoxelWorkStation.RunInt(
            player,
            AbilityBootstrap.VerbClayForm,
            [AbilityBootstrap.OpRemoveTag],
            HookIds.AssistRadius,
            VoxelWorkStation.AssistRadiusOff);
        bool guardRecipe = assistRadius >= 0 && radius <= assistRadius;

        bool result = false;
        for (int i = -(int)Math.Ceiling(radius / 2f); i <= radius / 2; i++)
        {
            for (int j = -(int)Math.Ceiling(radius / 2f); j <= radius / 2; j++)
            {
                Vec3i pos = voxelPos.AddCopy(i, 0, j);
                if (pos.X < 0 || pos.X >= 16 || pos.Y < 0 || pos.Y >= 16 || pos.Z < 0 || pos.Z >= 16)
                {
                    continue;
                }

                if (guardRecipe && form.SelectedRecipe.Voxels[pos.X, pos.Y, pos.Z])
                {
                    continue;
                }

                bool had = form.Voxels[pos.X, pos.Y, pos.Z];
                result = result || had;
                form.Voxels[pos.X, pos.Y, pos.Z] = false;
                if (had)
                {
                    form.AvailableVoxels++;
                }
            }
        }

        return result;
    }

    public static bool OnCopyLayer(BlockEntityClayForm form, int layer, IPlayer player)
    {
        if (layer <= 0 || layer > 15)
        {
            return false;
        }

        int quota = VoxelWorkStation.ResolveVoxelCopy(player);

        bool result = false;
        for (int x = 0; x < 16; x++)
        {
            for (int z = 0; z < 16; z++)
            {
                if (form.Voxels[x, layer - 1, z] && !form.Voxels[x, layer, z])
                {
                    quota--;
                    form.Voxels[x, layer, z] = true;
                    form.AvailableVoxels--;
                    result = true;
                    if (form.AvailableVoxels < 0)
                    {
                        TryRefill(
                            form,
                            player.InventoryManager.ActiveHotbarSlot,
                            player.Entity);
                    }
                }

                if (quota == 0)
                {
                    break;
                }
            }
        }

        if (result)
        {
            TryAwardProgress(form, player);
        }

        return result;
    }

    public static float FinishedProportion(BlockEntityClayForm form)
    {
        if (form?.SelectedRecipe == null)
        {
            return 0f;
        }

        int recipeVoxels = 0;
        int placedRecipe = 0;
        int totalPlaced = 0;
        int layers = Math.Min(16, form.SelectedRecipe.QuantityLayers);
        for (int x = 0; x < 16; x++)
        {
            for (int y = 0; y < layers; y++)
            {
                for (int z = 0; z < 16; z++)
                {
                    bool recipe = form.SelectedRecipe.Voxels[x, y, z];
                    bool have = form.Voxels[x, y, z];
                    if (recipe)
                    {
                        recipeVoxels++;
                        if (have)
                        {
                            placedRecipe++;
                            totalPlaced++;
                        }
                    }
                    else if (have)
                    {
                        totalPlaced++;
                    }
                }
            }
        }

        if (recipeVoxels == 0 || totalPlaced == 0)
        {
            return 0f;
        }

        float coverage = totalPlaced < recipeVoxels
            ? (float)totalPlaced / recipeVoxels
            : (float)recipeVoxels / totalPlaced;
        float accuracy = (float)placedRecipe / totalPlaced;
        return coverage * accuracy;
    }

    public static void FinishRecipe(BlockEntityClayForm form, ItemSlot slot, EntityAgent entity)
    {
        if (form?.SelectedRecipe == null)
        {
            return;
        }

        int layers = Math.Min(16, form.SelectedRecipe.QuantityLayers);
        for (int y = 0; y < layers; y++)
        {
            for (int x = 0; x < 16; x++)
            {
                for (int z = 0; z < 16; z++)
                {
                    bool want = form.SelectedRecipe.Voxels[x, y, z];
                    bool have = form.Voxels[x, y, z];
                    if (want != have)
                    {
                        if (want)
                        {
                            if (form.AvailableVoxels <= 0 && !TryRefill(form, slot, entity))
                            {
                                return;
                            }

                            form.AvailableVoxels--;
                        }
                        else
                        {
                            form.AvailableVoxels++;
                        }
                    }

                    form.Voxels[x, y, z] = want;
                }
            }
        }
    }

    public static bool TryAutoFinish(BlockEntityClayForm form, IPlayer player)
    {
        float chance = VoxelWorkStation.RunFloat(
            player,
            AbilityBootstrap.VerbClayForm,
            [AbilityBootstrap.OpFinishTag],
            HookIds.AutoFinish,
            0f);
        if (chance <= 0f)
        {
            return false;
        }

        float progress = FinishedProportion(form);
        float roll = chance * progress * progress;
        if (player.Entity.World.Rand.NextDouble() >= roll)
        {
            return false;
        }

        FinishRecipe(form, player.InventoryManager.ActiveHotbarSlot, player.Entity);
        TryAwardProgress(form, player);
        return true;
    }

    public static void TryAwardProgress(BlockEntityClayForm form, IPlayer player)
    {
        if (form?.Api?.Side != EnumAppSide.Server || player == null)
        {
            return;
        }

        int paid = ClayFormXpStation.TakeProgress(form);
        if (paid <= 0)
        {
            return;
        }

        string? target = form.SelectedRecipe?.Output?.Code?.ToString();
        ProsequorModSystem.For(form.Api)?.ClayFormXp?.NotifyProgress(player, paid, target);
    }
}
