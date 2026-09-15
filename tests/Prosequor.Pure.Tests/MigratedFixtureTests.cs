using Prosequor.Ability;
using Prosequor.Client;
using Prosequor.Data;
using Prosequor.Progress;
using Prosequor.Xp;
using Xunit;

namespace Prosequor.Pure.Tests;

/// <summary>Entry points for migrated boot-time fixture suites.</summary>
public class MigratedFixtureTests
{
    [Fact]
    public void XpBucketFixtures_Should_Pass() => XpBucketFixtures.VerifyAll();

    [Fact]
    public void FatherXpFixtures_Should_Pass() => FatherXpFixtures.VerifyAll();

    [Fact]
    public void ProgressParkFixtures_Should_Pass() => ProgressParkFixtures.VerifyAll();

    [Fact]
    public void ClayFormXpFixtures_Should_Pass() => ClayFormXpFixtures.VerifyAll();

    [Fact]
    public void MoldCastXpFixtures_Should_Pass() => MoldCastXpFixtures.VerifyAll();

    [Fact]
    public void AnvilVoxelGridFixtures_Should_Pass() => AnvilVoxelGridFixtures.VerifyAll();

    [Fact]
    public void AnvilMetalRecoveryFixtures_Should_Pass() => AnvilMetalRecoveryFixtures.VerifyAll();

    [Fact]
    public void AnvilBitsForgingFixtures_Should_Pass() => AnvilBitsForgingFixtures.VerifyAll();

    [Fact]
    public void AnvilHeatedStrikesFixtures_Should_Pass() => AnvilHeatedStrikesFixtures.VerifyAll();

    [Fact]
    public void ClayFireXpFixtures_Should_Pass() => ClayFireXpFixtures.VerifyAll();

    [Fact]
    public void AmountTableFixtures_Should_Pass() => AmountTableFixtures.VerifyAll();

    [Fact]
    public void QualityMathFixtures_Should_Pass() => QualityMathFixtures.VerifyAll();

    [Fact]
    public void EffortFixtures_Should_Pass() => EffortFixtures.VerifyAll();

    [Fact]
    public void DeedFixtures_Should_Pass() => DeedFixtures.VerifyAll();

    [Fact]
    public void ProsequorStackPedigreeFixtures_Should_Pass() => ProsequorStackPedigreeFixtures.VerifyAll();

    [Fact]
    public void ProsequorLiquidPedigreeFixtures_Should_Pass() => ProsequorLiquidPedigreeFixtures.VerifyAll();

    [Fact]
    public void ProsequorEntityPedigreeFixtures_Should_Pass() => ProsequorEntityPedigreeFixtures.VerifyAll();

    [Fact]
    public void CropPlanterPedigreeFixtures_Should_Pass() => CropPlanterPedigreeFixtures.VerifyAll();

    [Fact]
    public void BushPlanterPedigreeFixtures_Should_Pass() => BushPlanterPedigreeFixtures.VerifyAll();

    [Fact]
    public void OwnerCreditFixtures_Should_Pass() => OwnerCreditFixtures.VerifyAll();

    [Fact]
    public void PedigreeInspectFixtures_Should_Pass() => PedigreeInspectFixtures.VerifyAll();

    [Fact]
    public void MealHostCreditFixtures_Should_Pass() => MealHostCreditFixtures.VerifyAll();

    [Fact]
    public void LiquidContentChromeFixtures_Should_Pass() => LiquidContentChromeFixtures.VerifyAll();

    [Fact]
    public void LiquorQualityStampFixtures_Should_Pass() => LiquorQualityStampFixtures.VerifyAll();

    [Fact]
    public void BoilerProcessStarterFixtures_Should_Pass() => BoilerProcessStarterFixtures.VerifyAll();

    [Fact]
    public void BoilerDistillBatchFixtures_Should_Pass() => BoilerDistillBatchFixtures.VerifyAll();

    [Fact]
    public void ActivityWatchFixtures_Should_Pass() => ActivityWatchFixtures.VerifyAll();

    [Fact]
    public void PlayerWorkBucketsFixtures_Should_Pass() => PlayerWorkBucketsFixtures.VerifyAll();

    [Fact]
    public void AttributeGrowthFixtures_Should_Pass() => AttributeGrowthFixtures.VerifyAll();

    [Fact]
    public void AttributeBucketAxisFixtures_Should_Pass() => AttributeBucketAxisFixtures.VerifyAll();

    [Fact]
    public void LevelUpRuleFixtures_Should_Pass() => LevelUpRuleFixtures.VerifyAll();

    [Fact]
    public void ContributionDependsOnFixtures_Should_Pass() => ContributionDependsOnFixtures.VerifyAll();

    [Fact]
    public void LevelUpHudFixtures_Should_Pass() => LevelUpHudFixtures.VerifyAll();

    [Fact]
    public void SkillWaitingHintFixtures_Should_Pass() => SkillWaitingHintFixtures.VerifyAll();

    [Fact]
    public void TraitAttributeFixtures_Should_Pass() => TraitAttributeFixtures.VerifyAll();

    [Fact]
    public void QualitativeStatScaleFixtures_Should_Pass() => QualitativeStatScaleFixtures.VerifyAll();

    [Fact]
    public void SkillEffectTotalFixtures_Should_Pass() => SkillEffectTotalFixtures.VerifyAll();

    [Fact]
    public void CreateCharacterClassFixtures_Should_Pass() => CreateCharacterClassFixtures.VerifyAll();

    [Fact]
    public void AttributeStatFixtures_Should_Pass() => AttributeStatFixtures.VerifyAll();

    [Fact]
    public void SkillTreeCompilerFixtures_Should_Pass() => SkillTreeCompilerFixtures.VerifyAll();

    [Fact]
    public void CollectionOrFixtures_Should_Pass() => CollectionOrFixtures.VerifyAll();

    [Fact]
    public void AbilityFixtures_Should_Pass() => AbilityFixtures.VerifyAll();

    [Fact]
    public void HobbySkillFixtures_Should_Pass() => HobbySkillFixtures.VerifyAll();
}
