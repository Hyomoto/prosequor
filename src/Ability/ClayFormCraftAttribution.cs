using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace Prosequor.Ability;

/// <summary>Stamps the forming player and clayforming recipe onto unfired pottery.</summary>
public static class ClayFormCraftAttribution
{
    static ICoreServerAPI? serverApi;

    public static void RegisterServer(ICoreServerAPI api)
    {
        serverApi = api;
        api.Event.RegisterEventBusListener(OnItemClayFormed, 0.5, "onitemclayformed");
    }

    static void OnItemClayFormed(string eventName, ref EnumHandling handling, IAttribute data)
    {
        if (serverApi == null || data is not ITreeAttribute tree)
        {
            return;
        }

        ItemStack? stack = tree.GetItemstack("itemstack");
        if (stack == null)
        {
            return;
        }

        long entityId = tree.GetLong("byentityid");
        if (entityId == 0)
        {
            return;
        }

        Entity? entity = serverApi.World.GetEntityById(entityId);
        if (entity is not EntityPlayer playerEntity || playerEntity.Player == null)
        {
            return;
        }

        CraftAttribution.StampMaker(stack, playerEntity.Player);
        string? recipeKey = VoxelFinishScope.RecipeKey;
        if (string.IsNullOrWhiteSpace(recipeKey))
        {
            TryStampRecipeFromPlayerForm(playerEntity.Player, stack);
        }
        else
        {
            CraftAttribution.StampRecipe(stack, recipeKey);
        }

        if (ProsequorStackPedigree.TryGetRecipeKey(stack, out _))
        {
            string? caller = VoxelFinishScope.Caller ?? ClayFormCaller(playerEntity.Player);
            ProsequorModSystem.For(serverApi)?.VoxelWorkXp?.NotifyFinished(
                playerEntity.Player,
                stack,
                caller);
        }
    }

    /// <summary>Ground-storage and multi-slot clay-form outputs bypass the event bus.</summary>
    public static void StampGroundStorageOutputs(BlockEntityClayForm form, IPlayer? player)
    {
        if (player == null || form?.Api?.Side != EnumAppSide.Server)
        {
            return;
        }

        if (form.Api.World.BlockAccessor.GetBlockEntity(form.Pos) is not BlockEntityGroundStorage storage)
        {
            return;
        }

        string? recipeKey = VoxelFinishScope.RecipeKey;
        if (string.IsNullOrWhiteSpace(recipeKey))
        {
            recipeKey = ClayFormXpStation.RecipeKeyOf(form.SelectedRecipe);
        }

        bool stampRecipe = !string.IsNullOrWhiteSpace(recipeKey);
        foreach (ItemSlot slot in storage.Inventory)
        {
            if (slot.Itemstack == null)
            {
                continue;
            }

            CraftAttribution.StampMaker(slot.Itemstack, player);
            if (stampRecipe)
            {
                CraftAttribution.StampRecipe(slot.Itemstack, recipeKey);
                ProsequorModSystem.For(form.Api)?.VoxelWorkXp?.NotifyFinished(
                    player,
                    slot.Itemstack,
                    EventFactBuilder.CodeOf(form.Block));
            }

            slot.MarkDirty();
        }
    }

    static void TryStampRecipeFromPlayerForm(IPlayer player, ItemStack stack)
    {
        BlockSelection? sel = player.CurrentBlockSelection;
        if (sel == null || serverApi == null)
        {
            return;
        }

        if (serverApi.World.BlockAccessor.GetBlockEntity(sel.Position) is not BlockEntityClayForm form)
        {
            return;
        }

        CraftAttribution.StampRecipe(stack, ClayFormXpStation.RecipeKeyOf(form.SelectedRecipe));
    }

    static string? ClayFormCaller(IPlayer player)
    {
        BlockSelection? sel = player.CurrentBlockSelection;
        if (sel == null || serverApi == null)
        {
            return null;
        }

        if (serverApi.World.BlockAccessor.GetBlockEntity(sel.Position) is not BlockEntityClayForm form)
        {
            return null;
        }

        return EventFactBuilder.CodeOf(form.Block);
    }
}
