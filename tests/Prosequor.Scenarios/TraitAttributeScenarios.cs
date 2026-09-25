using Atlas.Api;
using Atlas.XUnit;
using Prosequor.Ability;
using Prosequor.Data;
using Prosequor.Player;
using Prosequor.Xp;
using Vintagestory.API.Common;
using Vintagestory.API.Server;
using Vintagestory.GameContent;
using Xunit;

namespace Prosequor.Scenarios;

/// <summary>
/// Mid-save / already-selected characters never send <c>DidSelect</c>. Class scores
/// must still apply from the cached starting profile, not leftover stripped traits.
/// </summary>
public class TraitAttributeScenarios : AtlasScenarioBase
{
    [AtlasScenario]
    [Trait("Layer", "Server")]
    [Trait("Kind", "Traits")]
    public async Task ClassProfile_Should_ApplyFromCache_When_SelectionAlreadyDone()
    {
        ITestPlayer joined = await World.JoinPlayer("MidSaveHunter");
        IServerPlayer player = RequireServerPlayer(joined.Player);
        IPlayerProgress progress = RequireProgress(player);

        player.Entity.WatchedAttributes.SetString("characterClass", "hunter");
        player.SetModData(TraitAttributeConverter.CreateCharacterModDataKey, true);
        player.SetModData(TraitAttributeConverter.AppliedModDataKey, false);

        ProsequorModSystem mod = ProsequorModSystem.For(World.Api)
            ?? throw new InvalidOperationException("Expected a live Prosequor mod system.");
        CharacterSystem characters = World.Api.ModLoader.GetModSystem<CharacterSystem>()
            ?? throw new InvalidOperationException("Expected CharacterSystem.");

        TraitAttributeConverter.TryApplyOnSelection(player, characters, mod.TraitAttributes, mod.Registry);

        Assert.True(player.GetModData(TraitAttributeConverter.AppliedModDataKey, false));
        Assert.Equal(9, progress.GetAttribute(AttributeIds.Strength));
        Assert.Equal(13, progress.GetAttribute(AttributeIds.Perception));
        Assert.Equal(11, progress.GetAttribute(AttributeIds.Constitution));
        Assert.Equal(10, progress.GetAttribute(AttributeIds.Inconspicuity));
        Assert.Equal(9, progress.GetAttribute(AttributeIds.Resilience));
    }

    [AtlasScenario]
    [Trait("Layer", "Server")]
    [Trait("Kind", "Traits")]
    public async Task ClassProfile_Should_Wait_Until_CharacterCreationConfirmed()
    {
        ITestPlayer joined = await World.JoinPlayer("UnconfCommoner");
        IServerPlayer player = RequireServerPlayer(joined.Player);
        IPlayerProgress progress = RequireProgress(player);

        player.Entity.WatchedAttributes.SetString("characterClass", "hunter");
        player.SetModData(TraitAttributeConverter.CreateCharacterModDataKey, false);
        player.SetModData(TraitAttributeConverter.AppliedModDataKey, false);

        ProsequorModSystem mod = ProsequorModSystem.For(World.Api)
            ?? throw new InvalidOperationException("Expected a live Prosequor mod system.");
        CharacterSystem characters = World.Api.ModLoader.GetModSystem<CharacterSystem>()
            ?? throw new InvalidOperationException("Expected CharacterSystem.");

        TraitAttributeConverter.TryApplyOnSelection(player, characters, mod.TraitAttributes, mod.Registry);

        Assert.False(player.GetModData(TraitAttributeConverter.AppliedModDataKey, false));
        Assert.Equal(AttributeGrowth.DefaultScore, progress.GetAttribute(AttributeIds.Perception));
    }

    [AtlasScenario]
    [Trait("Layer", "Server")]
    [Trait("Kind", "Traits")]
    public async Task ExtraTraits_Should_Fold_After_FirstApply()
    {
        ITestPlayer joined = await World.JoinPlayer("ModelExtraHunter");
        IServerPlayer player = RequireServerPlayer(joined.Player);
        IPlayerProgress progress = RequireProgress(player);

        player.Entity.WatchedAttributes.SetString("characterClass", "hunter");
        player.SetModData(TraitAttributeConverter.CreateCharacterModDataKey, true);
        player.SetModData(TraitAttributeConverter.AppliedModDataKey, false);

        ProsequorModSystem mod = ProsequorModSystem.For(World.Api)
            ?? throw new InvalidOperationException("Expected a live Prosequor mod system.");
        CharacterSystem characters = World.Api.ModLoader.GetModSystem<CharacterSystem>()
            ?? throw new InvalidOperationException("Expected CharacterSystem.");

        TraitAttributeConverter.TryApplyOnSelection(player, characters, mod.TraitAttributes, mod.Registry);
        Assert.Equal(9, progress.GetAttribute(AttributeIds.Strength));
        Assert.Equal(13, progress.GetAttribute(AttributeIds.Perception));

        player.Entity.WatchedAttributes.SetStringArray("extraTraits", ["soldier"]);
        TraitAttributeConverter.FoldNewExtraTraits(player, mod.TraitAttributes, mod.Registry);

        Assert.Equal(13, progress.GetAttribute(AttributeIds.Perception));
        Assert.Equal(11, progress.GetAttribute(AttributeIds.Strength));
    }

    [AtlasScenario]
    [Trait("Layer", "Server")]
    [Trait("Kind", "Traits")]
    public async Task GetProgress_Should_ReturnLive_When_EntityIsReady()
    {
        ITestPlayer joined = await World.JoinPlayer("LiveProgress");
        Assert.NotNull(ProsequorModSystem.TryGetLiveProgress(joined.Player));
        Assert.NotNull(ProsequorModSystem.GetProgress(joined.Player));
    }

    [AtlasScenario]
    [Trait("Layer", "Server")]
    [Trait("Kind", "Traits")]
    public async Task ClassSkillSet_Should_GrantOptionalSkill_FromTrait_And_SkipUnknown()
    {
        const string optionalId = "prosequor:access-optional";
        const string unknownId = "prosequor:no-such-skill";

        ITestPlayer joined = await World.JoinPlayer("AccessOptional");
        IServerPlayer player = RequireServerPlayer(joined.Player);
        EntityBehaviorProgress progress = ProsequorModSystem.TryGetLiveProgress(player)
            ?? throw new InvalidOperationException("Expected live progress.");

        ProsequorModSystem mod = ProsequorModSystem.For(World.Api)
            ?? throw new InvalidOperationException("Expected a live Prosequor mod system.");

        Assert.True(
            mod.TraitAttributes.TryGet("soldier", out TraitAttributeMapping soldier),
            "Expected shipped soldier trait mapping.");

        mod.Registry.Register(new SkillDef
        {
            Id = optionalId,
            IsOptional = true,
            Kind = SkillKind.Minor,
            MaxLevel = XpCurves.MinorMaxLevel
        });

        mod.TraitAttributes.Register(new TraitAttributeMapping
        {
            Code = soldier.Code,
            Attributes = soldier.Attributes,
            RetainTrait = soldier.RetainTrait,
            Skills = [optionalId, unknownId]
        });
        mod.TraitAttributes.RebuildClassSkillSets(mod.Registry, World.Api);

        Assert.DoesNotContain(optionalId, mod.TraitAttributes.BaseSkillSet);
        Assert.DoesNotContain(
            optionalId,
            mod.TraitAttributes.SkillSetForClass("commoner"));
        Assert.Contains(
            optionalId,
            mod.TraitAttributes.SkillSetForClass("blackguard"));
        Assert.DoesNotContain(
            unknownId,
            mod.TraitAttributes.SkillSetForClass("blackguard"));

        player.Entity.WatchedAttributes.SetString("characterClass", "blackguard");
        progress.BindClassSkillAccess(player, mod.Registry);
        Assert.True(progress.HasSkillAccess(optionalId));

        player.Entity.WatchedAttributes.SetString("characterClass", "commoner");
        progress.BindClassSkillAccess(player, mod.Registry);
        Assert.False(progress.HasSkillAccess(optionalId));
    }

    static IServerPlayer RequireServerPlayer(IPlayer player)
    {
        Assert.True(player is IServerPlayer, $"Expected IServerPlayer, got {player.GetType().Name}");
        return (IServerPlayer)player;
    }

    static IPlayerProgress RequireProgress(IPlayer player)
    {
        IPlayerProgress? progress = ProsequorModSystem.GetProgress(player);
        Assert.NotNull(progress);
        return progress;
    }
}
