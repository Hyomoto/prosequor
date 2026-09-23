using System.Text;
using System.Reflection;
using HarmonyLib;
using Prosequor.Ability.Hooks;
using Prosequor.Player;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace Prosequor.Ability;

/// <summary>
/// Place-time success-chance pipeline for fruit-tree cuttings, plus establish/tooltip
/// apply when an absolute chance was stamped on the pedigree host.
/// </summary>
public static class FruitTreeCuttingSuccess
{
    public const string EstablishChanceAttr = "prosequorEstablishChance";

    static readonly MethodInfo? TryGrowToMethod =
        AccessTools.Method(
            typeof(FruitTreeGrowingBranchBH),
            "TryGrowTo",
            new[] { typeof(EnumTreePartType), typeof(BlockFacing), typeof(int), typeof(float?) });

    static readonly FieldInfo? BranchBlockField =
        AccessTools.Field(typeof(FruitTreeGrowingBranchBH), "branchBlock");

    public static bool TryGetEstablishChance(BlockEntity? be, out float chance)
    {
        chance = 0f;
        if (be == null
            || !ProsequorBlockPedigreeStation.TryGetBox(be, out ProsequorChunkPedigree.Box box)
            || box.EstablishChance <= 0.0001f)
        {
            return false;
        }

        chance = box.EstablishChance;
        return true;
    }

    public static void StampEstablishChance(BlockEntity be, float chance)
    {
        if (be == null)
        {
            return;
        }

        ProsequorBlockPedigreeStation.Mutate(
            be,
            box => box.EstablishChance = GameMath.Clamp(chance, 0f, 1f));
    }

    /// <summary>
    /// Runs success-chance from the orientation base; stamps only when rules change it.
    /// </summary>
    public static void TryStampOnPlace(
        IWorldAccessor world,
        IPlayer player,
        BlockEntityFruitTreeBranch be,
        BlockFruitTreeBranch branchBlock)
    {
        if (world?.Side != EnumAppSide.Server
            || player == null
            || be == null
            || be.PartType != EnumTreePartType.Cutting
            || branchBlock?.TypeProps == null
            || string.IsNullOrEmpty(be.TreeType)
            || !branchBlock.TypeProps.TryGetValue(be.TreeType, out FruitTreeTypeProperties? props)
            || props == null)
        {
            return;
        }

        IPlayerProgress? progress = ProsequorModSystem.GetProgress(player);
        ProsequorModSystem? mod = ProsequorModSystem.For(world.Api);
        if (progress == null || mod?.Pipeline == null)
        {
            return;
        }

        bool graft = be.GrowthDir?.IsHorizontal == true;
        float baseChance = graft ? props.CuttingGraftChance : props.CuttingRootingChance;

        AbilityAction fact = EventFactBuilder.ForPlayer(
            player,
            AbilityBootstrap.VerbEstablishCutting,
            target: be.TreeType,
            held: EventFactBuilder.CodeOf(branchBlock),
            tokens: [graft ? AbilityBootstrap.GraftTag : AbilityBootstrap.RootTag],
            position: be.Pos.Copy());

        SuccessChanceContext context = new()
        {
            World = world,
            Player = player,
            Progress = progress,
            Fact = fact,
            BaseValue = baseChance
        };

        float result = mod.Pipeline.Run(
            HookIds.BlockInteraction,
            VerbIds.EstablishCutting,
            HookIds.Default,
            context,
            baseChance);
        result = GameMath.Clamp(result, 0f, 1f);
        if (Math.Abs(result - baseChance) <= 0.0001f)
        {
            return;
        }

        StampEstablishChance(be, result);
    }

    /// <summary>
    /// Mirrors vanilla Cutting establish using a stamped absolute chance.
    /// </summary>
    public static void EstablishCutting(FruitTreeGrowingBranchBH growing)
    {
        BlockEntityFruitTreeBranch? ownBe = growing.Blockentity as BlockEntityFruitTreeBranch;
        if (ownBe?.Api?.World == null || !TryGetEstablishChance(ownBe, out float chance))
        {
            return;
        }

        if (ownBe.FoliageState == EnumFoliageState.Dead || ownBe.GrowTries < 1)
        {
            return;
        }

        if (ownBe.RootOff == null)
        {
            return;
        }

        FruitTreeRootBH? behavior = (ownBe.Api.World.BlockAccessor.GetBlockEntity(
                ownBe.Pos.AddCopy(ownBe.RootOff)) as BlockEntityFruitTreeBranch)
            ?.GetBehavior<FruitTreeRootBH>();
        if (behavior == null)
        {
            return;
        }

        BlockFruitTreeBranch? branchBlock = BranchBlockField?.GetValue(growing) as BlockFruitTreeBranch;
        if (branchBlock == null)
        {
            return;
        }

        double roll = ownBe.Api.World.Rand.NextDouble();
        if (chance >= roll)
        {
            ownBe.Api.World.BlockAccessor.ExchangeBlock(branchBlock.Id, ownBe.Pos);
            ownBe.GrowTries += 4;
            ownBe.PartType = EnumTreePartType.Branch;
            if (ownBe.TreeType != null
                && behavior.propsByType.TryGetValue(ownBe.TreeType, out FruitTreeProperties? treeProps)
                && treeProps != null)
            {
                treeProps.State = EnumFruitTreeState.Young;
            }

            TryGrowToLeaves(growing, ownBe.GrowthDir);
            ownBe.MarkDirty(redrawOnClient: true);
        }
        else
        {
            if (ownBe.TreeType != null
                && behavior.propsByType.TryGetValue(ownBe.TreeType, out FruitTreeProperties? treeProps)
                && treeProps != null)
            {
                treeProps.State = EnumFruitTreeState.Dead;
            }

            ownBe.FoliageState = EnumFoliageState.Dead;
            ownBe.MarkDirty(redrawOnClient: true);
        }
    }

    public static void AppendCuttingInfo(BlockEntityFruitTreeBranch ownBe, StringBuilder dsc)
    {
        dsc.AppendLine(
            ownBe.FoliageState == EnumFoliageState.Dead
                ? "<font color=\"#ff8080\">" + Lang.Get("Dead tree cutting") + "</font>"
                : Lang.Get("Establishing tree cutting"));

        if (ownBe.FoliageState != EnumFoliageState.Dead
            && TryGetEstablishChance(ownBe, out float chance))
        {
            dsc.AppendLine(Lang.Get("{0}% survival chance", 100f * chance));
        }
    }

    static void TryGrowToLeaves(FruitTreeGrowingBranchBH growing, BlockFacing? dir)
    {
        if (TryGrowToMethod == null || dir == null)
        {
            return;
        }

        TryGrowToMethod.Invoke(
            growing,
            new object?[] { EnumTreePartType.Leaves, dir, 1, null });
    }
}
