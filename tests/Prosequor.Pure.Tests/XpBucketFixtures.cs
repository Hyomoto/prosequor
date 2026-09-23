using Prosequor.Ability.Hooks;
using Prosequor.Data;
using Prosequor.Progress;
using Prosequor.Xp;
using Prosequor.Xp.Activity;
using Vintagestory.API.Common;
using Xunit;

namespace Prosequor.Ability;

/// <summary>Pure XP bucket formula / award fixtures (no world required).</summary>
public static class XpBucketFixtures
{
    public static void VerifyAll()
    {
        VerifyCapFormulas();
        VerifyMultiplier();
        VerifyAccruedFlush();
        VerifySatTierAccrual();
        VerifyGraceAndDrain();
        VerifyAwardSkillPipeline();
        VerifyGrantAndFillMeters();
        VerifyCraftGrantMath();
        VerifyCraftXpRuleCompile();
        VerifyIngredientsMeasureTable();
    }

    static void VerifyCraftGrantMath()
    {
        if (XpGrantFormulas.CraftCount(5, 1) != 5
            || XpGrantFormulas.CraftCount(20, 4) != 5
            || XpGrantFormulas.CraftCount(1, 1) != 1
            // Vanilla CraftMany returns only the last craft's MovedQuantity (e.g. 4), not 20.
            || XpGrantFormulas.ResolveCraftCount(consumeCount: 5, movedQuantity: 4, outputStackSizePerCraft: 4) != 5
            || XpGrantFormulas.ResolveCraftCount(consumeCount: 0, movedQuantity: 4, outputStackSizePerCraft: 4) != 1
            || XpGrantFormulas.ResolveCraftCount(consumeCount: 0, movedQuantity: 0, outputStackSizePerCraft: 4) != 0
            || XpGrantFormulas.UnitsPerCraft(15, 5) != 3
            || XpGrantFormulas.UnitsPerCraft(5, 5) != 1)
        {
            Assert.Fail("[prosequor] XP bucket fixture failed (craft grant math).");
        }
    }

    static void VerifyCraftXpRuleCompile()
    {
        CollectionIndex collections = new();
        collections.EnsureKey("clothing");
        collections.EnsureKey("armor");
        collections.EnsureKey("thread");
        collections.EnsureKey("cloth");

        if (!XpRuleCompiler.TryCompile(
                new XpRuleJson
                {
                    id = "prosequor:craft-clothing-tailoring",
                    amount = new Newtonsoft.Json.Linq.JArray(10, 30, 50),
                    pay = new Newtonsoft.Json.Linq.JValue("ingredients"),
                    when = new XpRuleWhenJson
                    {
                        activity = "prosequor:deed",
                        tags = ["crafted", "caller:@grid", "target:<clothing>"]
                    }
                },
                "tailoring",
                1,
                collections,
                out XpRule clothing,
                out string clothingError)
            || clothing.AmountTable == null
            || clothing.AmountTable.Count != 3
            || clothing.Pay != XpPayChannel.Ingredients)
        {
            Assert.Fail(string.Format(
                "[prosequor] XP bucket fixture failed (clothing compile): {0}",
                clothingError));
        }
    }

    static void VerifyIngredientsMeasureTable()
    {
        CollectionIndex collections = new();
        collections.EnsureKey("clothing");
        if (!XpRuleCompiler.TryCompile(
                new XpRuleJson
                {
                    id = "cloth-ing",
                    amount = new Newtonsoft.Json.Linq.JArray(10, 30, 50),
                    pay = new Newtonsoft.Json.Linq.JValue("ingredients"),
                    when = new XpRuleWhenJson
                    {
                        activity = Deed.Activity,
                        tags = ["crafted", "target:<clothing>"]
                    }
                },
                "tailoring",
                1,
                collections,
                out XpRule rule,
                out string error))
        {
            Assert.Fail($"[prosequor] ingredients measure compile failed: {error}");
            return;
        }

        float low = Deed.ResolveGrant(
            rule,
            new Deed.Channels(
                0, 0, 0, false, 0, 0, 0, false,
                Quantity: 1,
                Ingredients: 1,
                HasIngredients: true));
        float mid = Deed.ResolveGrant(
            rule,
            new Deed.Channels(
                0, 0, 0, false, 0, 0, 0, false,
                Quantity: 1,
                Ingredients: 20,
                HasIngredients: true));
        float high = Deed.ResolveGrant(
            rule,
            new Deed.Channels(
                0, 0, 0, false, 0, 0, 0, false,
                Quantity: 1,
                Ingredients: 40,
                HasIngredients: true));
        float clamped = Deed.ResolveGrant(
            rule,
            new Deed.Channels(
                0, 0, 0, false, 0, 0, 0, false,
                Quantity: 1,
                Ingredients: 99,
                HasIngredients: true));

        // 20 sits just shy of the middle knot on [1, 40]: 10 + (30-10) * (38/39).
        if (!Near(low, 10f) || !Near(mid, 10f + 20f * (38f / 39f)) || !Near(high, 50f) || !Near(clamped, 50f))
        {
            Assert.Fail(
                $"[prosequor] ingredients table lerp failed: low={low} mid={mid} high={high} clamped={clamped}");
        }
    }

    static void VerifyCapFormulas()
    {
        if (!Near(XpBucketFormulas.SkillCap(0), 25f)
            || !Near(XpBucketFormulas.SkillCap(10), 45f)
            || !Near(XpBucketFormulas.SkillCap(99), 223f))
        {
            Assert.Fail("[prosequor] XP bucket fixture failed (cap formulas).");
            return;
        }

        PlayerProgressState state = new() { PlayerLevel = 10 };
        state.Skills["digging"] = new SkillProgressState { Level = 20 };
        XpBucketFormulas.RefreshAllCaps(state);
        if (!Near(state.Skills["digging"].CachedCap, 65f))
        {
            Assert.Fail("[prosequor] XP bucket fixture failed (cached skill cap).");
        }
    }

    static void VerifyMultiplier()
    {
        // fill in [cap, 2cap) => sat 1 => /2
        float skillCap = 25f;
        float mult = XpBucketFormulas.Multiplier(30f, skillCap);
        if (!Near(mult, 0.5f))
        {
            Assert.Fail(string.Format("[prosequor] XP bucket fixture failed (sat 1 mult), got {0}.", mult));
            return;
        }

        if (!Near(XpBucketFormulas.Multiplier(0), 1f)
            || !Near(XpBucketFormulas.Multiplier(1), 0.5f)
            || !Near(XpBucketFormulas.Multiplier(2), 0.25f))
        {
            Assert.Fail("[prosequor] XP bucket fixture failed (sat exponents).");
        }
    }

    static void VerifyAccruedFlush()
    {
        float accrued = 0f;
        float fill = 0f;
        double lastAccrual = 0.0;

        float g1 = XpBucketFormulas.AccrueAndMaybeFlush(
            ref accrued, ref fill, ref lastAccrual, 0.05f, 1f, nowHours: 10.0);
        if (g1 != 0f || !Near(accrued, 0.05f) || !Near(fill, 0.05f) || lastAccrual != 10.0)
        {
            Assert.Fail("[prosequor] XP bucket fixture failed (accrue hold under MinAward).");
            return;
        }

        float g2 = XpBucketFormulas.AccrueAndMaybeFlush(
            ref accrued, ref fill, ref lastAccrual, 0.05f, 1f, nowHours: 10.1);
        if (!Near(g2, 0.1f) || accrued != 0f || !Near(fill, 0.1f))
        {
            Assert.Fail(string.Format("[prosequor] XP bucket fixture failed (accrue flush at MinAward), granted={0} accrued={1}.",
                g2,
                accrued));
            return;
        }

        // Large raw flushes accrued remnants in one grant.
        accrued = 0.08f;
        float g3 = XpBucketFormulas.AccrueAndMaybeFlush(
            ref accrued, ref fill, ref lastAccrual, 5f, 1f, nowHours: 11.0);
        if (!Near(g3, 5.08f) || accrued != 0f)
        {
            Assert.Fail("[prosequor] XP bucket fixture failed (flush sweeps prior accrued).");
        }
    }

    static void VerifySatTierAccrual()
    {
        // fill 24/25: first 1 raw at ×1, next 4 at ×0.5 (sat 1) = 3.0 effective.
        float accrued = 0f;
        float skillFill = 24f;
        double lastAccrual = 10.0;
        float cap = 25f;
        float granted = XpBucketFormulas.AccrueAcrossSatTiers(
            ref accrued,
            ref skillFill,
            ref lastAccrual,
            cap,
            5f,
            nowHours: 11.0);
        if (!Near(granted, 3f) || !Near(skillFill, 29f) || accrued != 0f)
        {
            Assert.Fail(string.Format("[prosequor] XP bucket fixture failed (sat tier split), granted={0} fill={1}.",
                granted,
                skillFill));
            return;
        }

        if (XpCurves.PlayerXpForSkillLevelGain(0, 3) != 72
            || XpCurves.PlayerXpForSkillLevelGain(0, 1) != 21
            || XpCurves.PlayerXpForSkillLevelGain(5, 5) != 0)
        {
            Assert.Fail("[prosequor] XP bucket fixture failed (player XP for skill level gain).");
        }

        if (UnlockPointPolicy.PointsForSkillLevelGain(0, 19) != 0
            || UnlockPointPolicy.PointsForSkillLevelGain(19, 20) != 1
            || UnlockPointPolicy.PointsForSkillLevelGain(20, 21) != 0
            || UnlockPointPolicy.PointsForSkillLevelGain(0, 40) != 2
            || UnlockPointPolicy.PointsForSkillLevelGain(0, 0) != 0
            || UnlockPointPolicy.PointsForSkillLevelGain(40, 20) != 0)
        {
            Assert.Fail("[prosequor] XP bucket fixture failed (skill milestone unlock points).");
        }
    }

    static void VerifyGraceAndDrain()
    {
        float fill = 5f;
        double lastAccrual = 100.0;
        double lastDrain = 100.0;

        // Still inside 1h grace — no drain.
        XpBucketFormulas.DrainMeter(ref fill, lastAccrual, ref lastDrain, nowHours: 100.5);
        if (!Near(fill, 5f) || lastDrain != 100.5)
        {
            Assert.Fail("[prosequor] XP bucket fixture failed (grace blocks drain).");
            return;
        }

        // At grace boundary + 10 game seconds: 0.01*10 = 0.1 drained.
        lastDrain = 101.0; // drainStart = 101.0
        XpBucketFormulas.DrainMeter(ref fill, lastAccrual, ref lastDrain, nowHours: 101.0 + (10.0 / 3600.0));
        if (!Near(fill, 4.9f))
        {
            Assert.Fail(string.Format("[prosequor] XP bucket fixture failed (drain 0.01/s), fill={0}.", fill));
            return;
        }

        // Long jump after grace empties the meter.
        fill = 1f;
        lastAccrual = 200.0;
        lastDrain = 201.0;
        XpBucketFormulas.DrainMeter(ref fill, lastAccrual, ref lastDrain, nowHours: 202.0);
        if (fill > 0.0001f)
        {
            Assert.Fail(string.Format("[prosequor] XP bucket fixture failed (drain to zero over hour), fill={0}.", fill));
        }
    }

    static void VerifyAwardSkillPipeline()
    {
        PlayerProgressState state = new() { PlayerLevel = 1 };
        state.Skills["digging"] = new SkillProgressState { Level = 0 };
        XpBucketFormulas.RefreshAllCaps(state);

        double t = 50.0;
        float g1 = XpAwardService.AwardSkill(state, "digging", 0.05f, t);
        if (g1 != 0f
            || !Near(state.Skills["digging"].Accrued, 0.05f)
            || state.Skills["digging"].LastAccrualTotalHours != t)
        {
            Assert.Fail("[prosequor] XP bucket fixture failed (AwardSkill hold).");
            return;
        }

        float g2 = XpAwardService.AwardSkill(state, "digging", 0.05f, t + 0.01);
        if (!Near(g2, 0.1f) || state.Skills["digging"].Accrued != 0f)
        {
            Assert.Fail("[prosequor] XP bucket fixture failed (AwardSkill flush).");
            return;
        }

        if (!Near(state.Skills["digging"].Fill, 0.1f))
        {
            Assert.Fail("[prosequor] XP bucket fixture failed (AwardSkill skill fill).");
        }
    }

    /// <summary>
    /// GrantAndFill fill-only: full raw onto the skill meter even when saturated; accrued untouched.
    /// Contrasts with Earn, which sat-scales the flush amount.
    /// </summary>
    static void VerifyGrantAndFillMeters()
    {
        PlayerProgressState fillState = new() { PlayerLevel = 1 };
        fillState.Skills["digging"] = new SkillProgressState { Level = 0 };
        XpBucketFormulas.RefreshAllCaps(fillState);

        float skillCap = fillState.Skills["digging"].CachedCap;
        const float raw = 100f;
        const double t = 100.0;
        fillState.Skills["digging"].Fill = skillCap * 5f;
        fillState.Skills["digging"].Accrued = 0.04f;
        // Keep meter inside drain grace so ApplyDrain is a no-op for this call.
        fillState.Skills["digging"].LastAccrualTotalHours = t;
        fillState.Skills["digging"].LastDrainTotalHours = t;
        float accruedBefore = fillState.Skills["digging"].Accrued;
        float skillFillBefore = fillState.Skills["digging"].Fill;

        XpAwardService.FillSkillMeters(fillState, "digging", raw, t);

        if (!Near(fillState.Skills["digging"].Fill, skillFillBefore + raw)
            || !Near(fillState.Skills["digging"].Accrued, accruedBefore)
            || fillState.Skills["digging"].LastAccrualTotalHours != t)
        {
            Assert.Fail(string.Format(
                "[prosequor] XP GrantAndFill fixture failed (fill-only). skillFill={0} accrued={1}.",
                fillState.Skills["digging"].Fill,
                fillState.Skills["digging"].Accrued));
            return;
        }

        // Earn with the same pre-sat would grant far less than raw.
        PlayerProgressState earnState = new() { PlayerLevel = 1 };
        earnState.Skills["digging"] = new SkillProgressState { Level = 0 };
        XpBucketFormulas.RefreshAllCaps(earnState);
        earnState.Skills["digging"].Fill = skillCap * 5f;
        earnState.Skills["digging"].LastAccrualTotalHours = t;
        earnState.Skills["digging"].LastDrainTotalHours = t;
        float granted = XpAwardService.AwardSkill(earnState, "digging", raw, t);
        if (granted >= raw - 0.001f)
        {
            Assert.Fail(string.Format("[prosequor] XP GrantAndFill contrast failed (Earn should sat-scale). granted={0} raw={1}.",
                granted,
                raw));
        }
    }

    static bool Near(float a, float b) => Math.Abs(a - b) < 0.0001f;
}
