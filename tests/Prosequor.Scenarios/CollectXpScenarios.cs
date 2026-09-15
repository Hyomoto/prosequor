using Atlas.Api;
using Atlas.XUnit;
using Prosequor;
using Prosequor.Ability;
using Prosequor.Player;
using Prosequor.Xp;
using Prosequor.Xp.Activity;
using Vintagestory.API.Common;
using Xunit;

namespace Prosequor.Scenarios;

/// <summary>
/// Collect-XP stamp → buffer enqueue → watch flush (no live hen required).
/// </summary>
public class CollectXpScenarios : AtlasScenarioBase
{
    const string Skill = "husbandry";

    [AtlasScenario]
    [Trait("Layer", "Xp")]
    [Trait("Kind", "CollectXp")]
    public async Task TryGive_Should_EnqueueStampedEgg_AndFlushPays()
    {
        ITestPlayer joined = await World.JoinPlayer("CollectXpEgg");
        IPlayer player = joined.Player;
        EntityBehaviorProgress progress = RequireBehavior(player);

        ItemStack egg = NewRawEggStack(3);
        CollectXpStamp.Set(egg);
        Assert.True(CollectXpStamp.Has(egg));

        CollectXpBuffer buffer = RequireBuffer();
        buffer.Discard(player.PlayerUID);

        bool given = player.InventoryManager.TryGiveItemstack(egg);
        Assert.True(given, "Expected TryGiveItemstack to accept stamped eggs.");
        Assert.True(
            buffer.Peek(player.PlayerUID, "game:egg-chicken-raw") >= 1
            || buffer.Peek(player.PlayerUID, EventFactBuilder.CodeOf(egg) ?? "") >= 1,
            "Expected stamped give to enqueue collect-XP.");

        // Stamp should be cleared from fully consumed source, or leftovers keep it.
        if (egg.StackSize > 0)
        {
            Assert.True(CollectXpStamp.Has(egg), "Leftover units should keep the stamp.");
        }

        float before = TotalSkillXp(progress);
        buffer.Flush(World.Api, player.PlayerUID);
        float gained = TotalSkillXp(progress) - before;
        Assert.True(gained >= 0.09f, $"Expected ~0.1+ husbandry XP from collected eggs, got {gained}.");
        Assert.False(buffer.HasPending(player.PlayerUID), "Flush should clear the bag.");
    }

    [AtlasScenario]
    [Trait("Layer", "Xp")]
    [Trait("Kind", "CollectXp")]
    public async Task TryGive_Should_IgnoreUnstampedEgg()
    {
        ITestPlayer joined = await World.JoinPlayer("CollectXpNoStamp");
        IPlayer player = joined.Player;
        _ = RequireBehavior(player);

        ItemStack egg = NewRawEggStack(2);
        Assert.False(CollectXpStamp.Has(egg));

        CollectXpBuffer buffer = RequireBuffer();
        buffer.Discard(player.PlayerUID);

        bool given = player.InventoryManager.TryGiveItemstack(egg);
        Assert.True(given);
        Assert.False(buffer.HasPending(player.PlayerUID), "Unstamped eggs must not enqueue.");
    }

    [AtlasScenario]
    [Trait("Layer", "Xp")]
    [Trait("Kind", "CollectXp")]
    public async Task Enqueue_Should_MergeSameCode()
    {
        ITestPlayer joined = await World.JoinPlayer("CollectXpMerge");
        IPlayer player = joined.Player;
        _ = RequireBehavior(player);

        CollectXpBuffer buffer = RequireBuffer();
        buffer.Discard(player.PlayerUID);

        ItemStack a = NewRawEggStack(1);
        ItemStack b = NewRawEggStack(2);
        CollectXpStamp.Set(a);
        CollectXpStamp.Set(b);
        Assert.True(player.InventoryManager.TryGiveItemstack(a));
        Assert.True(player.InventoryManager.TryGiveItemstack(b));

        string? code = EventFactBuilder.CodeOf(a) ?? "game:egg-chicken-raw";
        Assert.True(buffer.Peek(player.PlayerUID, code) >= 3, "Same-code pickups should merge.");
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

    CollectXpBuffer RequireBuffer()
    {
        CollectXpBuffer? buffer = ProsequorModSystem.For(World.Api)?.ActivityWatch?.CollectXp;
        Assert.NotNull(buffer);
        return buffer!;
    }

    ItemStack NewRawEggStack(int size)
    {
        IWorldAccessor world = World.Api.World;
        Item? item = world.GetItem(new AssetLocation("game:egg-chicken-raw"))
            ?? world.GetItem(new AssetLocation("egg-chicken-raw"));
        Assert.NotNull(item);
        return new ItemStack(item, size);
    }
}
