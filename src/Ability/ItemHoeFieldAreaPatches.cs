using HarmonyLib;
using Prosequor.Xp.Adapters;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;
using Vintagestory.GameContent;

namespace Prosequor.Ability;

/// <summary>Field Expertise hoe tool modes and NxN tilling.</summary>
public static class ItemHoeFieldArea
{
    const string ToolModesCacheKey = "prosequorHoeToolModes";
    const string ToolModeAttr = "toolMode";

    public static SkillItem[]? GetOrCreateToolModes(ICoreAPI api)
    {
        ICoreClientAPI? capi = api as ICoreClientAPI;
        if (capi == null)
        {
            return null;
        }

        return ObjectCacheUtil.GetOrCreate(api, ToolModesCacheKey, () =>
        {
            SkillItem[] modes =
            [
                new SkillItem
                {
                    Code = new AssetLocation("1size"),
                    Name = Lang.Get("1x1")
                }.WithIcon(capi, ItemClay.Drawcreate1_svg),
                new SkillItem
                {
                    Code = new AssetLocation("2size"),
                    Name = Lang.Get("2x2")
                }.WithIcon(capi, ItemClay.Drawcreate4_svg),
                new SkillItem
                {
                    Code = new AssetLocation("3size"),
                    Name = Lang.Get("3x3")
                }.WithIcon(capi, new ItemClay().Drawcreate9_svg)
            ];
            return modes;
        });
    }

    public static SkillItem[]? FilterModes(SkillItem[]? allModes, int unlockedSize)
    {
        if (allModes == null || unlockedSize <= 1)
        {
            return null;
        }

        int count = Math.Min(unlockedSize, allModes.Length);
        if (count <= 0)
        {
            return null;
        }

        SkillItem[] slice = new SkillItem[count];
        Array.Copy(allModes, slice, count);
        return slice;
    }

    public static int GetClampedToolMode(ItemSlot slot, int unlockedSize)
    {
        if (slot?.Itemstack == null || unlockedSize <= 1)
        {
            return 0;
        }

        int mode = slot.Itemstack.Attributes.GetInt(ToolModeAttr, 0);
        return GameMath.Clamp(mode, 0, unlockedSize - 1);
    }

    public static void SetToolMode(ItemSlot slot, int toolMode)
    {
        slot.Itemstack?.Attributes.SetInt(ToolModeAttr, toolMode);
    }

    /// <summary>
    /// Area-tills when an area tool mode is selected. Returns true to continue with vanilla
    /// single-block till, false when area till already handled the action.
    /// </summary>
    public static bool TryAreaTill(
        ICoreAPI api,
        ItemSlot slot,
        EntityAgent byEntity,
        BlockSelection blockSel)
    {
        if (blockSel == null || api.Side != EnumAppSide.Server)
        {
            return true;
        }

        if (byEntity is not EntityPlayer entityPlayer || entityPlayer.Player == null)
        {
            return true;
        }

        IPlayer player = entityPlayer.Player;
        int areaSize = FieldWorkStation.Run(player);
        if (areaSize <= 1)
        {
            return true;
        }

        int toolMode = GetClampedToolMode(slot, areaSize);
        if (toolMode <= 0)
        {
            return true;
        }

        // Mirror XSkills: any area mode uses the unlocked ability size (not mode+1).
        int value = areaSize;
        int tillCount = 0;
        int x = blockSel.Position.X;
        int y = blockSel.Position.Y;
        int z = blockSel.Position.Z;
        int xOff = 0;
        int zOff = 0;
        if (value % 2 == 0)
        {
            if (x - byEntity.Pos.X >= 0.0)
            {
                xOff = 1;
            }

            if (z - byEntity.Pos.Z >= 0.0)
            {
                zOff = 1;
            }
        }

        x = x - value / 2 + xOff;
        z = z - value / 2 + zOff;
        Block? soundSource = null;

        for (int ix = x; ix < x + value; ix++)
        {
            for (int iz = z; iz < z + value; iz++)
            {
                BlockPos above = new(ix, y + 1, iz, blockSel.Position.dimension);
                Block aboveBlock = api.World.BlockAccessor.GetBlock(above);
                if (aboveBlock == null || aboveBlock.Id != 0)
                {
                    continue;
                }

                BlockPos pos = new(ix, y, iz, blockSel.Position.dimension);
                Block block = api.World.BlockAccessor.GetBlock(pos);
                if (block?.Code == null || !block.Code.Path.StartsWith("soil", StringComparison.Ordinal))
                {
                    continue;
                }

                string fertility = block.LastCodePart(1);
                Block? farmland = byEntity.World.GetBlock(new AssetLocation("farmland-dry-" + fertility));
                if (farmland == null)
                {
                    continue;
                }

                TreeAttribute? fertilityTree = null;
                if (api.World.BlockAccessor.GetBlockEntity(pos) is BlockEntitySoilNutrition soilNutrition)
                {
                    fertilityTree = new TreeAttribute();
                    soilNutrition.ToTreeAttributes(fertilityTree);
                }

                api.World.BlockAccessor.SetBlock(farmland.BlockId, pos);
                tillCount++;

                if (api.World.BlockAccessor.GetBlockEntity(pos) is BlockEntityFarmland farmlandBe)
                {
                    farmlandBe.OnCreatedFromSoil(block, fertilityTree);
                    TillSoilXp.OnSoilConverted(api, player, pos, block);
                }

                api.World.BlockAccessor.MarkBlockDirty(pos);
                soundSource ??= block;
            }
        }

        int damage = (int)(tillCount * 0.5f + 0.6f);
        if (damage > 0 && slot.Itemstack != null)
        {
            ItemSlot hotbar = player.InventoryManager.ActiveHotbarSlot;
            for (int d = 0; d < damage; d++)
            {
                if (slot.Empty || hotbar.Itemstack == null)
                {
                    break;
                }

                slot.Itemstack.Collectible.DamageItem(byEntity.World, byEntity, hotbar);
            }
        }

        if (slot.Empty)
        {
            byEntity.World.PlaySoundAt(
                new AssetLocation("sounds/effect/toolbreak"),
                byEntity.Pos.X,
                byEntity.Pos.Y,
                byEntity.Pos.Z);
        }

        if (soundSource?.Sounds != null)
        {
            byEntity.World.PlaySoundAt(soundSource.Sounds.Place, blockSel.Position, 0.4);
        }

        return false;
    }
}

[HarmonyPatch(typeof(CollectibleObject), nameof(CollectibleObject.GetToolModes))]
public static class ItemHoeGetToolModesPatch
{
    [HarmonyPostfix]
    public static void Postfix(
        CollectibleObject __instance,
        ItemSlot slot,
        IClientPlayer forPlayer,
        ref SkillItem[] __result)
    {
        if (__instance is not ItemHoe)
        {
            return;
        }

        int unlocked = FieldWorkStation.Run(forPlayer);
        SkillItem[]? modes = ItemHoeFieldArea.FilterModes(
            ItemHoeFieldArea.GetOrCreateToolModes(forPlayer.Entity.Api),
            unlocked);
        if (modes != null)
        {
            __result = modes;
        }
    }
}

[HarmonyPatch(typeof(CollectibleObject), nameof(CollectibleObject.GetToolMode))]
public static class ItemHoeGetToolModePatch
{
    [HarmonyPostfix]
    public static void Postfix(
        CollectibleObject __instance,
        ItemSlot slot,
        IPlayer byPlayer,
        ref int __result)
    {
        if (__instance is not ItemHoe)
        {
            return;
        }

        int unlocked = FieldWorkStation.Run(byPlayer);
        __result = ItemHoeFieldArea.GetClampedToolMode(slot, unlocked);
    }
}

[HarmonyPatch(typeof(CollectibleObject), nameof(CollectibleObject.SetToolMode))]
public static class ItemHoeSetToolModePatch
{
    [HarmonyPostfix]
    public static void Postfix(
        CollectibleObject __instance,
        ItemSlot slot,
        IPlayer byPlayer,
        int toolMode)
    {
        if (__instance is not ItemHoe)
        {
            return;
        }

        ItemHoeFieldArea.SetToolMode(slot, toolMode);
    }
}

[HarmonyPatch(typeof(ItemHoe), nameof(ItemHoe.DoTill))]
public static class ItemHoeDoTillPatch
{
    /// <summary>
    /// Area till when Field Expertise mode is active; otherwise capture soil for the
    /// vanilla 1×1 postfix (DoTill is void — no __result).
    /// </summary>
    [HarmonyPrefix]
    public static bool Prefix(
        ItemHoe __instance,
        float secondsUsed,
        ItemSlot slot,
        EntityAgent byEntity,
        BlockSelection blockSel,
        EntitySelection entitySel,
        out Block? __state)
    {
        __state = null;
        if (!ItemHoeFieldArea.TryAreaTill(byEntity.Api, slot, byEntity, blockSel))
        {
            return false;
        }

        if (blockSel == null || byEntity?.Api?.Side != EnumAppSide.Server)
        {
            return true;
        }

        Block soil = byEntity.World.BlockAccessor.GetBlock(blockSel.Position);
        if (soil?.Code != null && soil.Code.Path.StartsWith("soil", StringComparison.Ordinal))
        {
            __state = soil;
        }

        return true;
    }

    [HarmonyPostfix]
    public static void Postfix(
        EntityAgent byEntity,
        BlockSelection blockSel,
        Block? __state)
    {
        if (__state == null
            || blockSel == null
            || byEntity is not EntityPlayer entityPlayer
            || entityPlayer.Player == null
            || byEntity.Api?.Side != EnumAppSide.Server)
        {
            return;
        }

        if (byEntity.World.BlockAccessor.GetBlockEntity(blockSel.Position) is not BlockEntityFarmland)
        {
            return;
        }

        TillSoilXp.OnSoilConverted(byEntity.Api, entityPlayer.Player, blockSel.Position, __state);
    }
}

[HarmonyPatch(typeof(ItemHoe), nameof(ItemHoe.GetHeldInteractionHelp))]
public static class ItemHoeHeldHelpPatch
{
    [HarmonyPostfix]
    public static void Postfix(ItemHoe __instance, ItemSlot inSlot, ref WorldInteraction[] __result)
    {
        InventoryBase? inventory = inSlot.Inventory;
        if (inventory is not InventoryBasePlayer playerInv || playerInv.Player == null)
        {
            return;
        }

        if (FieldWorkStation.Run(playerInv.Player) <= 1)
        {
            return;
        }

        __result = __result.Append(new WorldInteraction
        {
            ActionLangCode = "blockhelp-selecttoolmode",
            HotKeyCode = "toolmodeselect"
        });
    }
}
