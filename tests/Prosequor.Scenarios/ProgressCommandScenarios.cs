using Atlas.Api;
using Atlas.XUnit;
using Prosequor.Ability;
using Prosequor.Commands;
using Prosequor.Data;
using Prosequor.Player;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Xunit;

namespace Prosequor.Scenarios;

/// <summary>
/// Player-caller <c>/prosequor</c> paths. Console <see cref="IWorldSession.ExecuteCommand"/>
/// has no player; <see cref="ITestPlayer.ExecuteCommand"/> does.
/// </summary>
public class ProgressCommandScenarios : AtlasScenarioBase
{
    const string Farming = ProgressSyncInspect.Farming;

    static readonly string[] HareCodes =
    [
        "game:hare-european-adult-male",
        "game:hare-arctic-adult-male",
        "game:hare-european-adult-female"
    ];

    [AtlasScenario]
    [Trait("Layer", "Server")]
    [Trait("Kind", "Command")]
    public async Task Show_Should_TargetCaller_When_PlayerNameOmitted()
    {
        CommandResult console = await World.ExecuteCommand("/prosequor show");
        Assert.False(console.Ok);
        Assert.Contains("Specify a player name.", console.Message);

        ITestPlayer joined = await World.JoinPlayer("CmdShow");
        IPlayerProgress progress = RequireProgress(joined.Player);
        int before = progress.UnlockPoints;
        progress.AddUnlockPoints(4);
        Assert.Equal(before + 4, progress.UnlockPoints);

        CommandResult self = await joined.ExecuteCommand("/prosequor show");
        Assert.True(self.Ok, self.Message);
        Assert.StartsWith($"Player {joined.Player.PlayerName}:", self.Message);
        Assert.Contains($"points {progress.UnlockPoints}", self.Message);
    }

    [AtlasScenario]
    [Trait("Layer", "Server")]
    [Trait("Kind", "Command")]
    public async Task Pedigree_Should_DumpHeldStack_When_HandOccupied()
    {
        ITestPlayer joined = await World.JoinPlayer("CmdPedigree");
        IPlayer player = joined.Player;

        CommandResult empty = await joined.ExecuteCommand("/prosequor pedigree");
        Assert.True(empty.Ok, empty.Message);
        Assert.Equal(PedigreeInspect.Invalid, empty.Message);

        await joined.GiveItem("game:stick");
        ItemSlot? hand = player.InventoryManager?.ActiveHotbarSlot;
        Assert.NotNull(hand);
        Assert.False(hand.Empty);
        CraftAttribution.StampMakerUid(hand.Itemstack, player.PlayerUID);

        CommandResult held = await joined.ExecuteCommand("/prosequor pedigree");
        Assert.True(held.Ok, held.Message);
        Assert.Contains("held", held.Message);
        Assert.Contains(player.PlayerName, held.Message);
    }

    [AtlasScenario]
    [Trait("Layer", "Server")]
    [Trait("Kind", "Command")]
    public async Task AddFriendliness_Should_CreditCaller_When_EntityIdPassed()
    {
        ITestPlayer joined = await World.JoinPlayer("CmdFriend");
        IPlayer player = joined.Player;
        Entity animal = SpawnHareNear(joined);
        int before = HusbandryFriendliness.Get(animal);

        CommandResult result = await joined.ExecuteCommand(
            $"/prosequor addfriendliness 1 {animal.EntityId}");
        Assert.True(result.Ok, result.Message);

        int after = HusbandryFriendliness.Get(animal);
        Assert.Equal(before + 1, after);
        Assert.Contains($"{before} → {after}", result.Message);
        Assert.True(ProsequorEntityPedigreeStation.TryGetBlob(animal, out ProsequorBlob blob));
        Assert.True(blob.TryGetContributorWeight(player.PlayerUID, out int weight));
        Assert.Equal(1, weight);
    }

    [AtlasScenario]
    [Trait("Layer", "Server")]
    [Trait("Kind", "LevelUp")]
    public async Task LevelUp_Should_SendSkillNotification_When_SkillLevelRises()
    {
        ITestPlayer joined = await World.JoinPlayer("CmdLevel");
        IServerPlayer player = joined.Player;
        IPlayerProgress progress = RequireProgress(player);
        ProsequorModSystem? mod = ProsequorModSystem.For(World.Api);
        Assert.NotNull(mod);
        Assert.True(mod.Registry.TryGet(Farming, out SkillDef skill), "farming skill missing");

        string skillName = SkillRegistry.DisplayName(skill, player.LanguageCode);
        const int level = 1;
        string expected = Lang.GetL(
            player.LanguageCode,
            "prosequor:levelup-skill",
            skillName,
            level);

        joined.Client.Clear();
        progress.SetSkillLevel(Farming, level);

        List<ReceivedChatLine> matches = new();
        foreach (ReceivedChatLine line in joined.Client.Chat())
        {
            if (line.Type == EnumChatType.Notification
                && line.GroupId == GlobalConstants.InfoLogChatGroup
                && line.Message == expected)
            {
                matches.Add(line);
            }
        }

        Assert.True(
            matches.Count == 1,
            $"Expected one level-up notification '{expected}'. Chat:" + Environment.NewLine
            + string.Join(Environment.NewLine, joined.Client.Chat().Select(line =>
                $"{line.Type} group={line.GroupId} {line.Message}")));
    }

    Entity SpawnHareNear(ITestPlayer joined)
    {
        BlockPos animalPos = joined.Position.AddCopy(2, 0, 0);
        Exception? last = null;
        foreach (string code in HareCodes)
        {
            try
            {
                return World.SpawnEntity(code, animalPos);
            }
            catch (Exception ex)
            {
                last = ex;
            }
        }

        throw new InvalidOperationException(
            "Could not spawn a hare for addfriendliness: " + string.Join(", ", HareCodes),
            last);
    }

    static IPlayerProgress RequireProgress(IPlayer player)
    {
        IPlayerProgress? progress = ProsequorModSystem.GetProgress(player);
        Assert.NotNull(progress);
        return progress;
    }
}
