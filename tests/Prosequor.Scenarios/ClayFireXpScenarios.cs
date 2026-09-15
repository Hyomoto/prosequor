using Atlas.Api;
using Atlas.XUnit;
using Prosequor;
using Prosequor.Ability;
using Prosequor.Player;
using Prosequor.Xp;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;
using Xunit;

namespace Prosequor.Scenarios;

/// <summary>
/// Pit-kiln contribution stamps and fire-complete XP settle (no burn-time wait).
/// </summary>
public class ClayFireXpScenarios : AtlasScenarioBase
{
    const string Skill = "clayforming";

    [AtlasScenario]
    [Trait("Layer", "Xp")]
    [Trait("Kind", "ClayFire")]
    public async Task TryIgnite_Should_AddContributorOnAllContentSlots()
    {
        ITestPlayer firerJoin = await World.JoinPlayer("KilnStampFirer");
        IPlayer firer = firerJoin.Player;
        _ = RequireBehavior(firer);

        BlockEntityPitKiln kiln = PlacePitKiln(firer);
        ItemStack a = NewStampableStack();
        ItemStack b = NewStampableStack();
        kiln.Inventory[0].Itemstack = a;
        kiln.Inventory[1].Itemstack = b;

        kiln.TryIgnite(firer);

        Assert.True(kiln.Lit, "Expected TryIgnite to light the pit kiln.");
        Assert.True(CraftAttribution.HasContributor(kiln.Inventory[0].Itemstack, firer.PlayerUID));
        Assert.True(CraftAttribution.HasContributor(kiln.Inventory[1].Itemstack, firer.PlayerUID));
        Assert.False(CraftAttribution.HasContributor(kiln.Inventory[4].Itemstack, firer.PlayerUID));

        Assert.True(ProsequorStackPedigree.TryGetPrimaryBlob(kiln.Inventory[0].Itemstack, out ProsequorBlob blob0));
        Assert.True(blob0.TryGetContributorWeight(firer.PlayerUID, out int w0) && w0 >= 1);
        Assert.True(ProsequorStackPedigree.TryGetPrimaryBlob(kiln.Inventory[1].Itemstack, out ProsequorBlob blob1));
        Assert.True(blob1.TryGetContributorWeight(firer.PlayerUID, out int w1) && w1 >= 1);
        Assert.False(kiln.Inventory[0].Itemstack!.Attributes.HasAttribute(CraftAttribution.FirerAttr));
    }

    [AtlasScenario]
    [Trait("Layer", "Xp")]
    [Trait("Kind", "ClayFire")]
    public async Task Settle_Should_SplitAmongContributors()
    {
        ITestPlayer aJoin = await World.JoinPlayer("KilnPayA");
        ITestPlayer bJoin = await World.JoinPlayer("KilnPayB");
        IPlayer playerA = aJoin.Player;
        IPlayer playerB = bJoin.Player;
        EntityBehaviorProgress progressA = RequireBehavior(playerA);
        EntityBehaviorProgress progressB = RequireBehavior(playerB);

        ItemStack stack = NewStampableStack();
        CraftAttribution.AddContribution(stack, playerA);
        CraftAttribution.AddContribution(stack, playerB);
        CraftAttribution.AddContribution(stack, playerA);

        float beforeA = TotalSkillXp(progressA);
        float beforeB = TotalSkillXp(progressB);

        ClayFireXp.Settle(
            World.Api.World,
            ClayFireXpMath.TokenPitKiln,
            targetCode: "game:bowl-fired",
            stack);

        float gainedA = TotalSkillXp(progressA) - beforeA;
        float gainedB = TotalSkillXp(progressB) - beforeB;
        // Weights 2:1 on table floor amount ~1 → ~0.666 / ~0.333
        Assert.True(gainedA > gainedB, $"Expected A (weight 2) > B (weight 1), got A={gainedA} B={gainedB}.");
        Assert.True(gainedA + gainedB >= 0.99f, $"Expected combined ~1 XP, got {gainedA + gainedB}.");
    }

    [AtlasScenario]
    [Trait("Layer", "Xp")]
    [Trait("Kind", "ClayFire")]
    public async Task Settle_Should_PaySoloContributorOnce()
    {
        ITestPlayer joined = await World.JoinPlayer("KilnPaySolo");
        IPlayer player = joined.Player;
        EntityBehaviorProgress progress = RequireBehavior(player);

        ItemStack stack = NewStampableStack();
        CraftAttribution.StampFirer(stack, player);

        float before = TotalSkillXp(progress);
        ClayFireXp.Settle(
            World.Api.World,
            ClayFireXpMath.TokenPitKiln,
            targetCode: "game:bowl-fired",
            stack);

        float gained = TotalSkillXp(progress) - before;
        Assert.True(gained >= 0.99f && gained < 1.5f, $"Expected solo ~1 XP (one emit), got {gained}.");
    }

    /// <summary>
    /// After TryIgnite stamps contributors, invoke OnFired — Harmony settle pays weighted shares.
    /// Does not wait on burn time.
    /// </summary>
    [AtlasScenario]
    [Trait("Layer", "Xp")]
    [Trait("Kind", "ClayFire")]
    public async Task OnFired_Should_PayContributors_PerPiece()
    {
        ITestPlayer makerJoin = await World.JoinPlayer("KilnOnFiredMaker");
        ITestPlayer firerJoin = await World.JoinPlayer("KilnOnFiredFirer");
        IPlayer maker = makerJoin.Player;
        IPlayer firer = firerJoin.Player;
        EntityBehaviorProgress makerProgress = RequireBehavior(maker);
        EntityBehaviorProgress firerProgress = RequireBehavior(firer);

        BlockEntityPitKiln kiln = PlacePitKiln(firer);
        for (int i = 0; i < 2; i++)
        {
            ItemStack stack = NewStampableStack();
            CraftAttribution.StampMaker(stack, maker);
            CraftAttribution.AddContribution(stack, maker);
            kiln.Inventory[i].Itemstack = stack;
        }

        kiln.TryIgnite(firer);
        Assert.True(CraftAttribution.HasContributor(kiln.Inventory[0].Itemstack, firer.PlayerUID));
        Assert.True(CraftAttribution.HasContributor(kiln.Inventory[0].Itemstack, maker.PlayerUID));
        Assert.Equal(maker.PlayerUID, CraftAttribution.TryGetMakerUid(kiln.Inventory[0].Itemstack));

        Assert.True(ProsequorStackPedigree.TryGetPrimaryBlob(kiln.Inventory[0].Itemstack, out ProsequorBlob piece));
        Assert.Equal(maker.PlayerUID, piece.MakerUid);
        Assert.True(piece.TryGetContributorWeight(firer.PlayerUID, out _));
        Assert.True(piece.TryGetContributorWeight(maker.PlayerUID, out _));
        Assert.False(kiln.Inventory[0].Itemstack!.Attributes.HasAttribute(CraftAttribution.MakerAttr));
        Assert.False(kiln.Inventory[0].Itemstack!.Attributes.HasAttribute(CraftAttribution.FirerAttr));

        float makerBefore = TotalSkillXp(makerProgress);
        float firerBefore = TotalSkillXp(firerProgress);

        try
        {
            kiln.OnFired();
        }
        catch
        {
            // KillFire / IsValidPitKiln often fail without a full pit structure in Atlas;
            // Harmony finalizer still settles from the Prefix stamp snapshot.
        }

        float makerGained = TotalSkillXp(makerProgress) - makerBefore;
        float firerGained = TotalSkillXp(firerProgress) - firerBefore;
        // 2 pieces × ~0.5 each when maker+firer share equally
        Assert.True(makerGained >= 0.99f, $"Expected maker ~1 XP across 2 pieces, got {makerGained}.");
        Assert.True(firerGained >= 0.99f, $"Expected firer ~1 XP across 2 pieces, got {firerGained}.");
    }

    static float TotalSkillXp(EntityBehaviorProgress progress)
    {
        var skill = progress.State.GetOrCreateSkill(Skill);
        return progress.GetSkillXp(Skill) + skill.Accrued;
    }

    static EntityBehaviorProgress RequireBehavior(IPlayer player)
    {
        EntityBehaviorProgress? progress = player.Entity?.GetBehavior<EntityBehaviorProgress>();
        Assert.NotNull(progress);
        return progress!;
    }

    BlockEntityPitKiln PlacePitKiln(IPlayer nearPlayer)
    {
        IWorldAccessor world = World.Api.World;
        Block? block = world.GetBlock(new AssetLocation("game:pitkiln"))
            ?? world.GetBlock(new AssetLocation("pitkiln"));
        Assert.NotNull(block);

        BlockPos pos = nearPlayer.Entity.Pos.AsBlockPos.AddCopy(2, 0, 0);
        // Solid floor under the kiln so OnFired's neighbor checks are less likely to explode.
        Block? dirt = world.GetBlock(new AssetLocation("game:soil-low-none"))
            ?? world.GetBlock(new AssetLocation("game:dirt"));
        if (dirt != null)
        {
            world.BlockAccessor.SetBlock(dirt.BlockId, pos.DownCopy());
            foreach (BlockFacing face in BlockFacing.HORIZONTALS)
            {
                world.BlockAccessor.SetBlock(dirt.BlockId, pos.AddCopy(face));
            }
        }

        world.BlockAccessor.SetBlock(0, pos);
        world.BlockAccessor.SetBlock(block.BlockId, pos);
        BlockEntityPitKiln? kiln = world.BlockAccessor.GetBlockEntity(pos) as BlockEntityPitKiln;
        Assert.NotNull(kiln);
        return kiln!;
    }

    ItemStack NewStampableStack()
    {
        IWorldAccessor world = World.Api.World;
        Item? item = world.GetItem(new AssetLocation("game:clay-blue"))
            ?? world.GetItem(new AssetLocation("game:clay-fire"))
            ?? world.GetItem(new AssetLocation("game:stick"));
        Block? block = world.GetBlock(new AssetLocation("game:bowl-raw-blue"))
            ?? world.GetBlock(new AssetLocation("game:bowl-raw-fire"));

        if (block != null)
        {
            return new ItemStack(block, 1);
        }

        Assert.NotNull(item);
        return new ItemStack(item, 1);
    }
}
