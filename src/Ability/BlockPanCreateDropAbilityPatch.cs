using System.Reflection;
using HarmonyLib;
using Prosequor.Ability.Hooks;
using Prosequor.Player;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;
using Vintagestory.GameContent;

namespace Prosequor.Ability;

/// <summary>
/// Thin BlockPan.CreateDrop adapter into the shared drops station (no rate XP — panning uses Effort.Emit).
/// </summary>
[HarmonyPatch(typeof(BlockPan), "CreateDrop")]
public static class BlockPanCreateDropAbilityPatch
{
    static readonly FieldInfo? DropsBySourceMatField =
        AccessTools.Field(typeof(BlockPan), "dropsBySourceMat");

    static readonly MethodInfo? ResolveMethod =
        AccessTools.Method(typeof(BlockPan), "Resolve");

    [HarmonyPrefix]
    public static bool Prefix(BlockPan __instance, EntityAgent byEntity, string fromBlockCode)
    {
        if (byEntity is not EntityPlayer entityPlayer || entityPlayer.Player == null)
        {
            return true;
        }

        IPlayer player = entityPlayer.Player;
        IWorldAccessor world = byEntity.World;
        if (world.Side != EnumAppSide.Server)
        {
            return false;
        }

        ProsequorModSystem? mod = ProsequorModSystem.For(world.Api);
        if (mod?.Pipeline == null || mod.Collections?.Index == null || DropsBySourceMatField == null)
        {
            return true;
        }

        IPlayerProgress? progress = ProsequorModSystem.GetProgress(player);
        if (progress == null)
        {
            return true;
        }

        if (DropsBySourceMatField.GetValue(__instance) is not Dictionary<string, PanningDrop[]> table)
        {
            return true;
        }

        PanningDrop[]? drops = null;
        foreach (string key in table.Keys)
        {
            if (WildcardUtil.Match(key, fromBlockCode))
            {
                drops = table[key];
                break;
            }
        }

        if (drops == null || drops.Length == 0)
        {
            return true;
        }

        Block? materialBlock = world.GetBlock(new AssetLocation(fromBlockCode));
        BlockPos pos = byEntity.Pos.AsBlockPos.Copy();
        AbilityAction fact = EventFactBuilder.ForPlayer(
            player,
            VerbIds.MutateDrops.Value,
            target: fromBlockCode,
            held: EventFactBuilder.CodeOf(__instance),
            position: pos);

        PanDropTable dropTable = new(__instance, byEntity, fromBlockCode, drops);
        ItemStack? first = dropTable.TryRollOne();
        List<ItemStack> stacks = new();
        if (first != null)
        {
            stacks.Add(first);
        }

        Block blockForContext = materialBlock ?? __instance;
        stacks = DropsStation.Run(
            world,
            player,
            stacks.ToArray(),
            fact,
            blockForContext,
            pos,
            dropTable,
            HookIds.ItemInteraction).ToList();

        foreach (ItemStack stack in stacks)
        {
            if (!player.InventoryManager.TryGiveItemstack(stack, slotNotifyEffect: true))
            {
                world.SpawnItemEntity(stack, byEntity.Pos.XYZ);
            }
        }

        return false;
    }

    sealed class PanDropTable : IDropTable
    {
        readonly BlockPan pan;
        readonly EntityAgent byEntity;
        readonly string fromBlockCode;
        readonly PanningDrop[] template;

        public PanDropTable(
            BlockPan pan,
            EntityAgent byEntity,
            string fromBlockCode,
            PanningDrop[] template)
        {
            this.pan = pan;
            this.byEntity = byEntity;
            this.fromBlockCode = fromBlockCode;
            this.template = template;
        }

        public ItemStack? TryRollOne()
        {
            PanningDrop[] array = (PanningDrop[])template.Clone();
            array.Shuffle(byEntity.World.Rand);
            string? rock = byEntity.World.GetBlock(new AssetLocation(fromBlockCode))?.Variant["rock"];

            foreach (PanningDrop drop in array)
            {
                double roll = byEntity.World.Rand.NextDouble();
                float statMul = 1f;
                if (drop.DropModbyStat != null)
                {
                    statMul = byEntity.Stats.GetBlended(drop.DropModbyStat);
                }

                float chance = drop.Chance.nextFloat() * statMul;
                ItemStack? stack = drop.ResolvedItemstack;
                if (drop.Code?.Path.Contains("{rocktype}") == true && rock != null && ResolveMethod != null)
                {
                    stack = ResolveMethod.Invoke(
                        pan,
                        new object[]
                        {
                            drop.Type,
                            drop.Code.Path.Replace("{rocktype}", rock)
                        }) as ItemStack;
                }

                if (roll < chance && stack != null)
                {
                    return stack.Clone();
                }
            }

            return null;
        }
    }
}
