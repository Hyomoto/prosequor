using Newtonsoft.Json.Linq;
using Prosequor.Ability;
using Prosequor.Ability.Hooks;
using Prosequor.Data;
using Prosequor.Pure.Tests;
using Xunit;

namespace Prosequor.Progress;

/// <summary>Live skill-tooltip totals: compile gates and number-operand evaluation (no world).</summary>
public static class SkillEffectTotalFixtures
{
    public static void VerifyAll()
    {
        VerifyAcceptsNumberSpecAndEvaluates();
        VerifyRejectsNonNumber();
        VerifyRejectsOutOfRange();
        VerifyMissingProgressOmitsBracket();
        VerifyNegativePercentPrintsMagnitude();
    }

    static void VerifyAcceptsNumberSpecAndEvaluates()
    {
        SkillTreeCompiler.CompileResult compiled = Compile(JockeyNode());
        if (!compiled.Success || compiled.Tree == null)
        {
            Assert.Fail("[prosequor] Jockey-shaped totalParams should compile. " +
                        string.Join(" | ", compiled.Errors));
        }

        SkillTreeTierDef tier = compiled.Tree.ById["jockey"].TierAt(1);
        if (tier.TotalParams.Count != 1
            || tier.TotalParams[0].Target != 0
            || tier.TotalParams[0].Format != "fractionPercent")
        {
            Assert.Fail("[prosequor] totalParams did not compile onto the owned tier.");
        }

        // 1 + 0.0025 * 8 = 1.02 → 102%.
        ActionTestProgress progress = new();
        progress.SetSkillLevel("riding", 8);
        string bracket = SkillEffectTotal.FormatBracket(tier, progress, vtml: false);
        if (bracket != " [102%]")
        {
            Assert.Fail(string.Format(
                "[prosequor] Skill-scaled set total failed. got '{0}' want ' [102%]'.",
                bracket));
        }
    }

    static void VerifyRejectsNonNumber()
    {
        SkillTreeNodeJson node = new()
        {
            id = "steppe",
            requires = [],
            totalParams = [new JObject { ["target"] = 0, ["format"] = "percent" }],
            tiers =
            [
                new SkillTreeTierJson
                {
                    effects =
                    [
                        new AbilityEffectJson
                        {
                            hook = "prosequor:entity-interaction",
                            verb = "prosequor:mounted",
                            phase = "can-ride",
                            action = "prosequor:allow-mounted-ride-without-saddle",
                            @params = new JObject()
                        }
                    ]
                }
            ]
        };

        SkillTreeCompiler.CompileResult compiled = Compile(node);
        if (compiled.Success || !compiled.Errors.Exists(err => err.Contains("not a number effect", StringComparison.Ordinal)))
        {
            Assert.Fail("[prosequor] totalParams must reject a non-number effect.");
        }
    }

    static void VerifyRejectsOutOfRange()
    {
        SkillTreeNodeJson node = JockeyNode();
        node.totalParams = [new JObject { ["target"] = 1, ["format"] = "fractionPercent" }];
        SkillTreeCompiler.CompileResult compiled = Compile(node);
        if (compiled.Success || !compiled.Errors.Exists(err => err.Contains("outside this tier's effects", StringComparison.Ordinal)))
        {
            Assert.Fail("[prosequor] totalParams must reject an out-of-range target.");
        }
    }

    static void VerifyMissingProgressOmitsBracket()
    {
        SkillTreeCompiler.CompileResult compiled = Compile(JockeyNode());
        if (!compiled.Success || compiled.Tree == null)
        {
            Assert.Fail("[prosequor] Jockey-shaped totalParams should compile for the empty-progress case.");
        }

        string bracket = SkillEffectTotal.FormatBracket(compiled.Tree.ById["jockey"].TierAt(1), null, vtml: false);
        if (bracket.Length != 0)
        {
            Assert.Fail("[prosequor] Missing progress must omit the total bracket.");
        }
    }

    static void VerifyNegativePercentPrintsMagnitude()
    {
        string reduced = SkillDescriptionRender.FormatPercentPlain(new FormattedDescriptionArg
        {
            Value = -0.10m,
            Format = "fractionPercent",
        });
        string third = SkillDescriptionRender.FormatPercentPlain(new FormattedDescriptionArg
        {
            Value = -0.33m,
            Format = "fractionPercent",
        });
        string boosted = SkillDescriptionRender.FormatPercentPlain(new FormattedDescriptionArg
        {
            Value = 150m,
            Format = "percent",
        });
        if (reduced != "10%" || third != "33%" || boosted != "150%")
        {
            Assert.Fail(string.Format(
                "[prosequor] Negative percents must print as a magnitude. got '{0}' / '{1}' / '{2}'.",
                reduced,
                third,
                boosted));
        }
    }

    static SkillTreeNodeJson JockeyNode() =>
        new()
        {
            id = "jockey",
            requires = [],
            totalParams = [new JObject { ["target"] = 0, ["format"] = "fractionPercent" }],
            tiers =
            [
                new SkillTreeTierJson
                {
                    effects =
                    [
                        new AbilityEffectJson
                        {
                            hook = "prosequor:entity-interaction",
                            verb = "prosequor:mounted",
                            phase = "move-speed",
                            action = "prosequor:number",
                            @params = new JObject
                            {
                                ["op"] = "set",
                                ["base"] = 1,
                                ["perSkillLevel"] = 0.0025,
                                ["cap"] = 1.2
                            }
                        }
                    ]
                }
            ]
        };

    static SkillTreeCompiler.CompileResult Compile(SkillTreeNodeJson node)
    {
        HookRegistry hooks = new();
        AbilityActionRegistry actions = new(hooks);
        AbilityBootstrap.RegisterBuiltIns(hooks, actions);
        CollectionIndex collections = new();
        int order = 0;
        return SkillTreeCompiler.Compile(
            "riding",
            100,
            new SkillTreeJson { nodes = [node] },
            hooks,
            actions,
            collections,
            ref order);
    }
}
