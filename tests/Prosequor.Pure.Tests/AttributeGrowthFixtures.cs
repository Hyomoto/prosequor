using Prosequor.Data;
using Prosequor.Progress;
using Vintagestory.API.Common;
using Xunit;

namespace Prosequor.Progress;

/// <summary>Attribute growth grant / soft-reset checks (no world required).</summary>
public static class AttributeGrowthFixtures
{
    /// <summary>Delta that yields <paramref name="effective"/> under the default baseline.</summary>
    static int DeltaForEffective(int effective) =>
        AttributeScoreMath.DeltaFromEffective(AttributeGrowth.DefaultScore, effective);

    /// <summary>Delta that sits at MaxScore under the default baseline.</summary>
    static int MaxDelta() => DeltaForEffective(AttributeGrowth.MaxScore);

    public static void VerifyAll()
    {
        VerifyUniqueMaxBucketWins();
        VerifyAllZeroBucketsUniqueHighScore();
        VerifyAllZeroBucketsJointHighScores();
        VerifyFiveWayScoreTie();
        VerifyOnlyWinnerBucketResets();
        VerifyMaxedBucketSkipped();
        VerifyAllMaxedGrantsNothing();
        VerifyAddScoresOneLevel();
        VerifyAddScoresMultiLevel();
        VerifyAddScoresBeforeGrantWins();
        VerifyAttributeScoreCompiler();
        VerifyTryGrowIncrementsDelta();
        VerifyAbsoluteToDeltaMigration();
        VerifyClassChangeKeepsDelta();
    }

    static PlayerProgressState FreshState()
    {
        PlayerProgressState state = new();
        PlayerProgressState.EnsureAttributeEntries(state);
        return state;
    }

    static void VerifyUniqueMaxBucketWins()
    {
        PlayerProgressState state = FreshState();
        state.AttributeBuckets[AttributeIds.Perception] = 12f;
        state.AttributeBuckets[AttributeIds.Strength] = 3f;
        string? winner = AttributeGrowth.TryGrow(state, new Random(1));
        if (winner != AttributeIds.Perception || state.Attributes[AttributeIds.Perception] != 1)
        {
            Assert.Fail("[prosequor] Attribute growth fixture failed (unique max bucket).");
        }
    }

    static void VerifyAllZeroBucketsUniqueHighScore()
    {
        PlayerProgressState state = FreshState();
        state.Attributes[AttributeIds.Strength] = DeltaForEffective(14);
        string? winner = AttributeGrowth.TryGrow(state, new Random(2));
        if (winner != AttributeIds.Strength
            || state.Attributes[AttributeIds.Strength] != DeltaForEffective(15))
        {
            Assert.Fail("[prosequor] Attribute growth fixture failed (all-zero buckets + unique high score).");
        }
    }

    static void VerifyAllZeroBucketsJointHighScores()
    {
        // Seeded RNG: only Strength and Perception are candidates.
        for (int seed = 0; seed < 40; seed++)
        {
            PlayerProgressState trial = FreshState();
            trial.Attributes[AttributeIds.Strength] = DeltaForEffective(14);
            trial.Attributes[AttributeIds.Perception] = DeltaForEffective(14);
            string? winner = AttributeGrowth.TryGrow(trial, new Random(seed));
            if (winner != AttributeIds.Strength && winner != AttributeIds.Perception)
            {
                Assert.Fail("[prosequor] Attribute growth fixture failed (joint high scores left the pair).");
                return;
            }
        }

        bool sawStrength = false;
        bool sawPerception = false;
        for (int seed = 0; seed < 80; seed++)
        {
            PlayerProgressState trial = FreshState();
            trial.Attributes[AttributeIds.Strength] = DeltaForEffective(14);
            trial.Attributes[AttributeIds.Perception] = DeltaForEffective(14);
            string? winner = AttributeGrowth.TryGrow(trial, new Random(seed));
            sawStrength |= winner == AttributeIds.Strength;
            sawPerception |= winner == AttributeIds.Perception;
            if (sawStrength && sawPerception)
            {
                break;
            }
        }

        if (!sawStrength || !sawPerception)
        {
            Assert.Fail("[prosequor] Attribute growth fixture failed (joint high scores never both won).");
        }
    }

    static void VerifyFiveWayScoreTie()
    {
        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
        for (int seed = 0; seed < 120; seed++)
        {
            PlayerProgressState trial = FreshState();
            string? winner = AttributeGrowth.TryGrow(trial, new Random(seed));
            if (winner == null || !AttributeIds.IsKnown(winner))
            {
                Assert.Fail("[prosequor] Attribute growth fixture failed (five-way unknown winner).");
                return;
            }

            seen.Add(winner);
            if (seen.Count == AttributeIds.All.Length)
            {
                break;
            }
        }

        if (seen.Count != AttributeIds.All.Length)
        {
            Assert.Fail("[prosequor] Attribute growth fixture failed (five-way tie did not hit all attributes).");
        }
    }

    static void VerifyOnlyWinnerBucketResets()
    {
        PlayerProgressState state = FreshState();
        state.AttributeBuckets[AttributeIds.Strength] = 5f;
        state.AttributeBuckets[AttributeIds.Perception] = 9f;
        state.AttributeBuckets[AttributeIds.Constitution] = 2f;
        AttributeGrowth.TryGrow(state, new Random(7));
        if (state.AttributeBuckets[AttributeIds.Perception] != 0f
            || state.AttributeBuckets[AttributeIds.Strength] != 5f
            || state.AttributeBuckets[AttributeIds.Constitution] != 2f)
        {
            Assert.Fail("[prosequor] Attribute growth fixture failed (only winner bucket resets).");
        }
    }

    static void VerifyMaxedBucketSkipped()
    {
        PlayerProgressState state = FreshState();
        state.Attributes[AttributeIds.Perception] = MaxDelta();
        state.AttributeBuckets[AttributeIds.Perception] = 12f;
        state.AttributeBuckets[AttributeIds.Strength] = 3f;
        string? winner = AttributeGrowth.TryGrow(state, new Random(1));
        if (winner != AttributeIds.Strength
            || state.Attributes[AttributeIds.Strength] != 1
            || state.Attributes[AttributeIds.Perception] != MaxDelta()
            || state.AttributeBuckets[AttributeIds.Perception] != 12f)
        {
            Assert.Fail("[prosequor] Attribute growth fixture failed (maxed bucket skipped).");
        }
    }

    static void VerifyAllMaxedGrantsNothing()
    {
        PlayerProgressState state = FreshState();
        foreach (string id in AttributeIds.All)
        {
            state.Attributes[id] = MaxDelta();
            state.AttributeBuckets[id] = 5f;
        }

        string? winner = AttributeGrowth.TryGrow(state, new Random(3));
        if (winner != null)
        {
            Assert.Fail("[prosequor] Attribute growth fixture failed (all-maxed returned a winner).");
            return;
        }

        foreach (string id in AttributeIds.All)
        {
            if (state.Attributes[id] != MaxDelta() || state.AttributeBuckets[id] != 5f)
            {
                Assert.Fail("[prosequor] Attribute growth fixture failed (all-maxed mutated state).");
                return;
            }
        }
    }

    static void VerifyAddScoresOneLevel()
    {
        PlayerProgressState state = FreshState();
        AttributeScoreEntry[] scores = [new(AttributeIds.Strength, 0.5f)];
        AttributeGrowth.AddScores(state, scores, levelsGained: 1);
        if (state.AttributeBuckets[AttributeIds.Strength] != 0.5f)
        {
            Assert.Fail(
                $"[prosequor] Attribute growth fixture failed (one-level fill; got {state.AttributeBuckets[AttributeIds.Strength]}).");
        }
    }

    static void VerifyAddScoresMultiLevel()
    {
        PlayerProgressState state = FreshState();
        AttributeScoreEntry[] scores =
        [
            new(AttributeIds.Strength, 0.5f),
            new(AttributeIds.Resilience, 0.2f)
        ];
        AttributeGrowth.AddScores(state, scores, levelsGained: 3);
        if (state.AttributeBuckets[AttributeIds.Strength] != 1.5f
            || state.AttributeBuckets[AttributeIds.Resilience] != 0.6f)
        {
            Assert.Fail(
                $"[prosequor] Attribute growth fixture failed (multi-level fill; strength={state.AttributeBuckets[AttributeIds.Strength]} resilience={state.AttributeBuckets[AttributeIds.Resilience]}).");
        }
    }

    static void VerifyAddScoresBeforeGrantWins()
    {
        PlayerProgressState state = FreshState();
        // Perception already has a small lead; skill fill for Strength must land first so Strength wins.
        state.AttributeBuckets[AttributeIds.Perception] = 0.3f;
        AttributeScoreEntry[] scores = [new(AttributeIds.Strength, 0.5f)];
        AttributeGrowth.AddScores(state, scores, levelsGained: 1);
        IReadOnlyList<string> winners = LevelUpRules.Apply(
            state,
            LevelUpRuleFixtures.DefaultRules(),
            beforeLevel: 9,
            afterLevel: 10,
            new Random(1));
        if (winners.Count != 1
            || winners[0] != AttributeIds.Strength
            || state.Attributes[AttributeIds.Strength] != 1
            || state.AttributeBuckets[AttributeIds.Strength] != 0f
            || state.AttributeBuckets[AttributeIds.Perception] != 0.3f)
        {
            Assert.Fail(
                $"[prosequor] Attribute growth fixture failed (fill-before-grant; winners=[{string.Join(',', winners)}] strengthDelta={state.Attributes[AttributeIds.Strength]} strengthBucket={state.AttributeBuckets[AttributeIds.Strength]}).");
        }
    }

    static void VerifyTryGrowIncrementsDelta()
    {
        PlayerProgressState state = FreshState();
        state.Attributes[AttributeIds.Strength] = 2;
        state.AttributeBuckets[AttributeIds.Strength] = 1f;
        string? winner = AttributeGrowth.TryGrow(state, new Random(1));
        if (winner != AttributeIds.Strength || state.Attributes[AttributeIds.Strength] != 3)
        {
            Assert.Fail(
                $"[prosequor] Attribute growth fixture failed (TryGrow delta; winner={winner} delta={state.Attributes[AttributeIds.Strength]}).");
        }
    }

    static void VerifyAbsoluteToDeltaMigration()
    {
        PlayerProgressState state = new() { Schema = 5 };
        foreach (string id in AttributeIds.All)
        {
            state.Attributes[id] = AttributeGrowth.DefaultScore;
        }

        state.Attributes[AttributeIds.Strength] = 14;
        Dictionary<string, int> baselines = new(StringComparer.OrdinalIgnoreCase);
        foreach (string id in AttributeIds.All)
        {
            baselines[id] = AttributeGrowth.DefaultScore;
        }

        baselines[AttributeIds.Strength] = 12;
        AttributeScoreMath.ConvertAbsoluteToDeltas(state, baselines);
        if (state.Schema != PlayerProgressState.AttributeDeltaSchema
            || state.Attributes[AttributeIds.Strength] != 2
            || state.Attributes[AttributeIds.Perception] != 0)
        {
            Assert.Fail(
                $"[prosequor] Attribute growth fixture failed (absolute→delta; schema={state.Schema} str={state.Attributes[AttributeIds.Strength]} per={state.Attributes[AttributeIds.Perception]}).");
        }
    }

    /// <summary>
    /// Smoke: fixed growth delta under two class baselines → two effectives; delta unchanged.
    /// </summary>
    static void VerifyClassChangeKeepsDelta()
    {
        const int growthDelta = 2;
        Dictionary<string, int> classA = new(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, int> classB = new(StringComparer.OrdinalIgnoreCase);
        foreach (string id in AttributeIds.All)
        {
            classA[id] = AttributeGrowth.DefaultScore;
            classB[id] = AttributeGrowth.DefaultScore;
        }

        classA[AttributeIds.Strength] = 10;
        classB[AttributeIds.Strength] = 12;

        PlayerProgressState state = FreshState();
        state.Attributes[AttributeIds.Strength] = growthDelta;

        int effectiveA = AttributeScoreMath.Effective(classA[AttributeIds.Strength], growthDelta);
        int effectiveB = AttributeScoreMath.Effective(classB[AttributeIds.Strength], growthDelta);
        if (effectiveA != 12
            || effectiveB != 14
            || state.Attributes[AttributeIds.Strength] != growthDelta)
        {
            Assert.Fail(
                $"[prosequor] Attribute growth fixture failed (class-change smoke; a={effectiveA} b={effectiveB} delta={state.Attributes[AttributeIds.Strength]}).");
        }

        // Simulate reevaluation after "class change": delta must stay put.
        int afterSwitch = AttributeScoreMath.Effective(classB[AttributeIds.Strength], state.Attributes[AttributeIds.Strength]);
        if (afterSwitch != 14 || state.Attributes[AttributeIds.Strength] != growthDelta)
        {
            Assert.Fail("[prosequor] Attribute growth fixture failed (class-change smoke; delta mutated).");
        }
    }

    static void VerifyAttributeScoreCompiler()
    {
        List<string> errors = new();
        IReadOnlyList<AttributeScoreEntry> ok = AttributeScoreCompiler.Compile(
            "fixture",
            [
                new AttributeScoreJson { id = "Strength", value = 0.5f },
                new AttributeScoreJson { id = "resilience", value = 0.2f }
            ],
            errors);
        if (errors.Count != 0
            || ok.Count != 2
            || ok[0].Id != AttributeIds.Strength
            || ok[0].Value != 0.5f
            || ok[1].Id != AttributeIds.Resilience
            || ok[1].Value != 0.2f)
        {
            Assert.Fail(
                $"[prosequor] Attribute score compiler fixture failed (valid list; errors={errors.Count} count={ok.Count}).");
            return;
        }

        errors.Clear();
        IReadOnlyList<AttributeScoreEntry> unknown = AttributeScoreCompiler.Compile(
            "fixture",
            [new AttributeScoreJson { id = "luck", value = 1f }],
            errors);
        if (unknown.Count != 0 || errors.Count != 1)
        {
            Assert.Fail(
                $"[prosequor] Attribute score compiler fixture failed (unknown id; count={unknown.Count} errors={errors.Count}).");
            return;
        }

        errors.Clear();
        IReadOnlyList<AttributeScoreEntry> nonPositive = AttributeScoreCompiler.Compile(
            "fixture",
            [
                new AttributeScoreJson { id = "strength", value = 0f },
                new AttributeScoreJson { id = "perception", value = -1f }
            ],
            errors);
        if (nonPositive.Count != 0 || errors.Count != 2)
        {
            Assert.Fail(
                $"[prosequor] Attribute score compiler fixture failed (value<=0; count={nonPositive.Count} errors={errors.Count}).");
            return;
        }

        errors.Clear();
        IReadOnlyList<AttributeScoreEntry> dup = AttributeScoreCompiler.Compile(
            "fixture",
            [
                new AttributeScoreJson { id = "strength", value = 0.5f },
                new AttributeScoreJson { id = "STRENGTH", value = 0.8f }
            ],
            errors);
        if (dup.Count != 1
            || dup[0].Id != AttributeIds.Strength
            || dup[0].Value != 0.8f
            || errors.Count != 1)
        {
            Assert.Fail(
                $"[prosequor] Attribute score compiler fixture failed (duplicate last-win; count={dup.Count} value={(dup.Count > 0 ? dup[0].Value : -1)} errors={errors.Count}).");
        }
    }
}
