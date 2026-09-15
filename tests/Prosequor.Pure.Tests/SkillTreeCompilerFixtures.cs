using Prosequor.Ability;
using Prosequor.Ability.Actions;
using Prosequor.Ability.Hooks;
using Prosequor.Data;
using Prosequor.Pure.Tests;
using Prosequor.Xp;
using Xunit;

namespace Prosequor.Data;

/// <summary>Skill-tree compiler layout / rejection fixtures (xUnit pure tests).</summary>
public static class SkillTreeCompilerFixtures
{
    /// <summary>Deterministic smoke checks for the layout compiler (no separate test project).</summary>
    public static void VerifyAll()
    {
        HookRegistry hooks = new();
        AbilityActionRegistry actions = new(hooks);
        AbilityBootstrap.RegisterBuiltIns(hooks, actions);
        CollectionIndex collections = new();
        collections.EnsureKey("clay");
        collections.EnsureKey("soil");
        collections.AddCode("clay", "game:clay-blue-raw");
        collections.AddCode("soil", "game:soil-low-none");
        int order = 0;
        SkillTreeJson diamond = new()
        {
            nodes =
            [
                new SkillTreeNodeJson { id = "root", requires = [] },
                new SkillTreeNodeJson { id = "a", requires = ["root"] },
                new SkillTreeNodeJson { id = "b", requires = ["root"] },
                new SkillTreeNodeJson { id = "merge", requires = ["a", "b"], cost = 2, minSkillLevel = 10 }
            ]
        };

        SkillTreeCompiler.CompileResult ok = SkillTreeCompiler.Compile(
            "fixture", 100, diamond, hooks, actions, collections, ref order);
        if (!ok.Success || ok.Tree == null
            || ok.Tree.ById["merge"].Layer != 2
            || ok.Tree.ById["a"].Layer != 1
            || ok.Tree.ById["root"].Layer != 0
            || ok.Tree.ById["root"].GridColumn != 0
            || ok.Tree.ById["a"].GridColumn != 0
            || ok.Tree.ById["b"].GridColumn != 1
            || ok.Tree.ById["merge"].GridColumn != 0
            || ok.Tree.ColumnSpan != 2
            || Math.Abs(ok.Tree.LayoutWidth - ((2 - 1) * SkillTreeCompiler.LayoutGridStep + SkillTreeCompiler.LayoutNodeSize)) > 0.01)
        {
            Assert.Fail("[prosequor] Skill-tree compiler fixture failed (constrained diamond layout).");
        }

        if (SkillTreeCompiler.DefaultUnlockNameLang("digging", "shovelexpert") != "unlock-digging-shovelexpert"
            || SkillTreeCompiler.DefaultUnlockNameLang("digging", "mymod:deepclay")
                != "mymod:unlock-digging-deepclay"
            || SkillTreeCompiler.DefaultUnlockNameLang("mymod:alchemy", "mymod:transmutation")
                != "mymod:unlock-alchemy-transmutation"
            || SkillTreeCompiler.DefaultUnlockNameLang("mymod:alchemy", "transmutation")
                != "mymod:unlock-alchemy-transmutation"
            || SkillTreeCompiler.DefaultSkillNameLang("digging") != "skill-digging"
            || SkillTreeCompiler.DefaultSkillNameLang("mymod:alchemy") != "mymod:skill-alchemy")
        {
            Assert.Fail("[prosequor] Skill-tree compiler fixture failed (namespaced default nameLang).");
        }

        SkillTreeJson namespacedName = new()
        {
            nodes =
            [
                new SkillTreeNodeJson { id = "mymod:deepclay", requires = [] },
                new SkillTreeNodeJson { id = "plain", nameLang = "custom-key", requires = [] }
            ]
        };
        SkillTreeCompiler.CompileResult named = SkillTreeCompiler.Compile(
            "digging", 100, namespacedName, hooks, actions, collections, ref order);
        if (!named.Success || named.Tree == null
            || named.Tree.ById["mymod:deepclay"].NameLang != "mymod:unlock-digging-deepclay"
            || named.Tree.ById["plain"].NameLang != "custom-key")
        {
            Assert.Fail("[prosequor] Skill-tree compiler fixture failed (omitted namespaced nameLang).");
        }

        SkillTreeJson wideRow = new()
        {
            nodes =
            [
                new SkillTreeNodeJson { id = "w0", requires = [] },
                new SkillTreeNodeJson { id = "w1", requires = [] },
                new SkillTreeNodeJson { id = "w2", requires = [] },
                new SkillTreeNodeJson { id = "w3", requires = [] },
                new SkillTreeNodeJson { id = "w4", requires = [] }
            ]
        };
        SkillTreeCompiler.CompileResult wide = SkillTreeCompiler.Compile(
            "fixture-wide", 100, wideRow, hooks, actions, collections, ref order);
        if (!wide.Success || wide.Tree == null
            || wide.Tree.ColumnSpan != 5
            || Math.Abs(wide.Tree.LayoutWidth - ((5 - 1) * SkillTreeCompiler.LayoutGridStep + SkillTreeCompiler.LayoutNodeSize)) > 0.01)
        {
            Assert.Fail("[prosequor] Skill-tree compiler fixture failed (column span / layout width).");
        }

        SkillTreeJson tiered = new()
        {
            nodes =
            [
                new SkillTreeNodeJson
                {
                    id = "root",
                    cost = 1,
                    minSkillLevel = 5,
                    descriptionLang = "base",
                    requires = [],
                    tiers =
                    [
                        new SkillTreeTierJson(),
                        new SkillTreeTierJson { cost = 3, descriptionLang = "second" }
                    ]
                }
            ]
        };

        SkillTreeCompiler.CompileResult tiers = SkillTreeCompiler.Compile(
            "fixture-tiers", 100, tiered, hooks, actions, collections, ref order);
        if (!tiers.Success || tiers.Tree == null
            || tiers.Tree.ById["root"].MaxTier != 2
            || tiers.Tree.ById["root"].TierAt(1).Cost != 1
            || tiers.Tree.ById["root"].TierAt(1).DescriptionLang != "base"
            || tiers.Tree.ById["root"].TierAt(2).Cost != 3
            || tiers.Tree.ById["root"].TierAt(2).MinSkillLevel != 5
            || tiers.Tree.ById["root"].TierAt(2).DescriptionLang != "second")
        {
            Assert.Fail("[prosequor] Skill-tree compiler fixture failed (tier inheritance).");
        }

        SkillTreeJson levelBands = new()
        {
            nodes =
            [
                new SkillTreeNodeJson { id = "level-1", minSkillLevel = 1, requires = [] },
                new SkillTreeNodeJson { id = "level-3", minSkillLevel = 3, requires = [] },
                new SkillTreeNodeJson { id = "profession", minSkillLevel = 5, requires = [] },
                new SkillTreeNodeJson { id = "specialist", minSkillLevel = 5, requires = ["profession"] }
            ]
        };
        SkillTreeCompiler.CompileResult bands = SkillTreeCompiler.Compile(
            "fixture-level-bands", 100, levelBands, hooks, actions, collections, ref order);
        if (!bands.Success || bands.Tree == null
            || bands.Tree.ById["level-1"].Layer != 0
            || bands.Tree.ById["level-3"].Layer != 0
            || bands.Tree.ById["profession"].Layer != 0
            || bands.Tree.ById["specialist"].Layer != 1)
        {
            Assert.Fail("[prosequor] Skill-tree compiler fixture failed (minimum-level bands / shake-out).");
        }

        // Capstone with a high minSkillLevel still sits on the row after its parents.
        SkillTreeJson parentPrecedence = new()
        {
            nodes =
            [
                new SkillTreeNodeJson { id = "a", minSkillLevel = 1, requires = [] },
                new SkillTreeNodeJson { id = "b", minSkillLevel = 1, requires = [] },
                new SkillTreeNodeJson { id = "c", minSkillLevel = 1, requires = [] },
                new SkillTreeNodeJson
                {
                    id = "mid",
                    minSkillLevel = 30,
                    requires = ["a"]
                },
                new SkillTreeNodeJson
                {
                    id = "capstone",
                    minSkillLevel = 50,
                    requires = ["a", "b", "c"]
                }
            ]
        };
        SkillTreeCompiler.CompileResult precedence = SkillTreeCompiler.Compile(
            "fixture-parent-precedence", 100, parentPrecedence, hooks, actions, collections, ref order);
        if (!precedence.Success || precedence.Tree == null
            || precedence.Tree.ById["a"].Layer != 0
            || precedence.Tree.ById["mid"].Layer != 1
            || precedence.Tree.ById["capstone"].Layer != 1)
        {
            Assert.Fail("[prosequor] Skill-tree compiler fixture failed (parentage over minSkillLevel).");
        }

        SkillTreeJson authoredMerge = new()
        {
            nodes =
            [
                new SkillTreeNodeJson { id = "careful", requires = [] },
                new SkillTreeNodeJson { id = "clay", requires = [] },
                new SkillTreeNodeJson { id = "peat", requires = [] },
                new SkillTreeNodeJson { id = "saltpeter", requires = [] },
                new SkillTreeNodeJson { id = "shovel", requires = ["careful"] },
                new SkillTreeNodeJson { id = "mixed", requires = ["clay"] },
                new SkillTreeNodeJson
                {
                    id = "digger",
                    requires = ["clay", "peat", "saltpeter"]
                }
            ]
        };
        SkillTreeCompiler.CompileResult mergeLayout = SkillTreeCompiler.Compile(
            "fixture-authored-merge", 100, authoredMerge, hooks, actions, collections, ref order);
        if (!mergeLayout.Success || mergeLayout.Tree == null
            || mergeLayout.Tree.ById["careful"].GridColumn != -1
            || mergeLayout.Tree.ById["clay"].GridColumn != 0
            || mergeLayout.Tree.ById["peat"].GridColumn != 1
            || mergeLayout.Tree.ById["saltpeter"].GridColumn != 2
            || mergeLayout.Tree.ById["shovel"].GridColumn != -1
            || mergeLayout.Tree.ById["mixed"].GridColumn != 0
            || mergeLayout.Tree.ById["digger"].GridColumn != 1)
        {
            Assert.Fail("[prosequor] Skill-tree compiler fixture failed (authored many-to-one layout).");
        }

        SkillTreeJson cycle = new()
        {
            nodes =
            [
                new SkillTreeNodeJson { id = "x", requires = ["y"] },
                new SkillTreeNodeJson { id = "y", requires = ["x"] }
            ]
        };
        if (SkillTreeCompiler.Compile("fixture-cycle", 100, cycle, hooks, actions, collections, ref order).Success)
        {
            Assert.Fail("[prosequor] Skill-tree compiler fixture failed (cycle should reject).");
        }

        SkillTreeJson unknown = new()
        {
            nodes =
            [
                new SkillTreeNodeJson { id = "x", requires = ["missing"] }
            ]
        };
        if (SkillTreeCompiler.Compile("fixture-unknown", 100, unknown, hooks, actions, collections, ref order).Success)
        {
            Assert.Fail("[prosequor] Skill-tree compiler fixture failed (unknown prereq should reject).");
        }

        SkillTreeJson fourWayFork = new()
        {
            nodes =
            [
                new SkillTreeNodeJson { id = "root", requires = [] },
                new SkillTreeNodeJson { id = "a", requires = ["root"] },
                new SkillTreeNodeJson { id = "b", requires = ["root"] },
                new SkillTreeNodeJson { id = "c", requires = ["root"] },
                new SkillTreeNodeJson { id = "d", requires = ["root"] }
            ]
        };
        if (SkillTreeCompiler.Compile("fixture-four-way", 100, fourWayFork, hooks, actions, collections, ref order).Success)
        {
            Assert.Fail("[prosequor] Skill-tree compiler fixture failed (four-way fork should reject).");
        }

        SkillTreeJson sameLayerSide = new()
        {
            nodes =
            [
                new SkillTreeNodeJson { id = "root", requires = [] },
                new SkillTreeNodeJson { id = "a", requires = ["root"] },
                new SkillTreeNodeJson { id = "b", requires = ["root"] },
                new SkillTreeNodeJson { id = "c", requires = ["root"] },
                new SkillTreeNodeJson
                {
                    id = "side",
                    requires = ["root"],
                    layout = new SkillTreeNodeLayoutJson { layerOffset = 0 }
                }
            ]
        };
        SkillTreeCompiler.CompileResult sameLayer = SkillTreeCompiler.Compile(
            "fixture-same-layer", 100, sameLayerSide, hooks, actions, collections, ref order);
        if (!sameLayer.Success || sameLayer.Tree == null
            || sameLayer.Tree.ById["side"].Layer != sameLayer.Tree.ById["root"].Layer
            || sameLayer.Tree.ById["side"].GridColumn == sameLayer.Tree.ById["root"].GridColumn
            || sameLayer.Tree.ById["a"].Layer != sameLayer.Tree.ById["root"].Layer + 1)
        {
            Assert.Fail("[prosequor] Skill-tree compiler fixture failed (same-layer horizontal child).");
        }

        SkillTreeJson sameLayerBias = new()
        {
            nodes =
            [
                new SkillTreeNodeJson { id = "root", requires = [] },
                new SkillTreeNodeJson
                {
                    id = "right-default",
                    requires = ["root"],
                    layout = new SkillTreeNodeLayoutJson { layerOffset = 0 }
                },
                new SkillTreeNodeJson
                {
                    id = "left-bias",
                    requires = ["root"],
                    layout = new SkillTreeNodeLayoutJson { layerOffset = 0, columnBias = "left" }
                }
            ]
        };
        SkillTreeCompiler.CompileResult sameBias = SkillTreeCompiler.Compile(
            "fixture-same-layer-bias", 100, sameLayerBias, hooks, actions, collections, ref order);
        if (!sameBias.Success || sameBias.Tree == null
            || sameBias.Tree.ById["right-default"].GridColumn
                <= sameBias.Tree.ById["root"].GridColumn
            || sameBias.Tree.ById["left-bias"].GridColumn
                >= sameBias.Tree.ById["root"].GridColumn)
        {
            Assert.Fail("[prosequor] Skill-tree compiler fixture failed (same-layer columnBias left/default).");
        }

        SkillTreeJson fourDownward = new()
        {
            nodes =
            [
                new SkillTreeNodeJson { id = "root", requires = [] },
                new SkillTreeNodeJson { id = "a", requires = ["root"] },
                new SkillTreeNodeJson { id = "b", requires = ["root"] },
                new SkillTreeNodeJson { id = "c", requires = ["root"] },
                new SkillTreeNodeJson { id = "d", requires = ["root"] }
            ]
        };
        if (SkillTreeCompiler.Compile("fixture-four-downward", 100, fourDownward, hooks, actions, collections, ref order).Success)
        {
            Assert.Fail("[prosequor] Skill-tree compiler fixture failed (four downward children should reject).");
        }

        SkillTreeJson columnBias = new()
        {
            nodes =
            [
                new SkillTreeNodeJson { id = "left", requires = [] },
                new SkillTreeNodeJson { id = "right", requires = [] },
                new SkillTreeNodeJson
                {
                    id = "merge-left",
                    requires = ["left", "right"],
                    layout = new SkillTreeNodeLayoutJson { columnBias = "left" }
                },
                new SkillTreeNodeJson
                {
                    id = "merge-right",
                    requires = ["left", "right"],
                    layout = new SkillTreeNodeLayoutJson { columnBias = "right" }
                }
            ]
        };
        SkillTreeCompiler.CompileResult biased = SkillTreeCompiler.Compile(
            "fixture-column-bias", 100, columnBias, hooks, actions, collections, ref order);
        if (!biased.Success || biased.Tree == null
            || biased.Tree.ById["merge-left"].GridColumn != biased.Tree.ById["left"].GridColumn
            || biased.Tree.ById["merge-right"].GridColumn != biased.Tree.ById["right"].GridColumn)
        {
            Assert.Fail("[prosequor] Skill-tree compiler fixture failed (columnBias left/right).");
        }

        // compact:false pins a higher-band root on its seeded row.
        SkillTreeJson pinned = new()
        {
            nodes =
            [
                new SkillTreeNodeJson { id = "early", minSkillLevel = 1, requires = [] },
                new SkillTreeNodeJson
                {
                    id = "late",
                    minSkillLevel = 10,
                    requires = [],
                    layout = new SkillTreeNodeLayoutJson { compact = false }
                }
            ]
        };
        SkillTreeCompiler.CompileResult pinResult = SkillTreeCompiler.Compile(
            "fixture-compact-false", 100, pinned, hooks, actions, collections, ref order);
        if (!pinResult.Success || pinResult.Tree == null
            || pinResult.Tree.ById["early"].Layer != 0
            || pinResult.Tree.ById["late"].Layer != 1)
        {
            Assert.Fail("[prosequor] Skill-tree compiler fixture failed (compact:false pin).");
        }

        // Mining-like multi-parent graph must compile after shake-out.
        SkillTreeJson miningLike = new()
        {
            nodes =
            [
                new SkillTreeNodeJson { id = "carefulminer", minSkillLevel = 1, requires = [] },
                new SkillTreeNodeJson { id = "oreminer", minSkillLevel = 1, requires = [] },
                new SkillTreeNodeJson { id = "pickaxeexpert", minSkillLevel = 1, requires = [] },
                new SkillTreeNodeJson { id = "stonebreaker", minSkillLevel = 10, requires = [] },
                new SkillTreeNodeJson
                {
                    id = "miner",
                    minSkillLevel = 30,
                    requires = ["pickaxeexpert", "oreminer", "stonebreaker"]
                },
                new SkillTreeNodeJson { id = "stonecutter", minSkillLevel = 10, requires = [] },
                new SkillTreeNodeJson
                {
                    id = "gemstoneminer",
                    minSkillLevel = 20,
                    requires = ["stonecutter"]
                },
                new SkillTreeNodeJson
                {
                    id = "crystalseeker",
                    minSkillLevel = 40,
                    requires = ["oreminer"]
                },
                new SkillTreeNodeJson
                {
                    id = "geologist",
                    minSkillLevel = 60,
                    requires = ["miner"]
                }
            ]
        };
        SkillTreeCompiler.CompileResult miningLayout = SkillTreeCompiler.Compile(
            "fixture-mining-shakeout", 100, miningLike, hooks, actions, collections, ref order);
        if (!miningLayout.Success || miningLayout.Tree == null
            || miningLayout.Tree.ById["carefulminer"].Layer != 0
            || miningLayout.Tree.ById["oreminer"].Layer != 0
            || miningLayout.Tree.ById["pickaxeexpert"].Layer != 0
            || miningLayout.Tree.ById["stonebreaker"].Layer != 0
            || miningLayout.Tree.ById["miner"].Layer != 1
            || miningLayout.Tree.ById["stonecutter"].Layer != 1
            || miningLayout.Tree.ById["crystalseeker"].Layer != 1
            || miningLayout.Tree.ById["gemstoneminer"].Layer != 2
            || miningLayout.Tree.ById["geologist"].Layer != 2)
        {
            Assert.Fail("[prosequor] Skill-tree compiler fixture failed (mining shake-out).");
        }

        // Cascade may push a row past the pull threshold of four when same-row children follow.
        SkillTreeJson cascade = new()
        {
            nodes =
            [
                new SkillTreeNodeJson { id = "a", minSkillLevel = 1, requires = [] },
                new SkillTreeNodeJson { id = "b", minSkillLevel = 1, requires = [] },
                new SkillTreeNodeJson { id = "c", minSkillLevel = 1, requires = [] },
                new SkillTreeNodeJson { id = "d", minSkillLevel = 10, requires = [] },
                new SkillTreeNodeJson
                {
                    id = "side-left",
                    minSkillLevel = 10,
                    requires = ["d"],
                    layout = new SkillTreeNodeLayoutJson { layerOffset = 0 }
                },
                new SkillTreeNodeJson
                {
                    id = "side-right",
                    minSkillLevel = 10,
                    requires = ["d"],
                    layout = new SkillTreeNodeLayoutJson { layerOffset = 0 }
                }
            ]
        };
        SkillTreeCompiler.CompileResult cascadeResult = SkillTreeCompiler.Compile(
            "fixture-shakeout-cascade", 100, cascade, hooks, actions, collections, ref order);
        if (!cascadeResult.Success || cascadeResult.Tree == null
            || cascadeResult.Tree.ById["d"].Layer != 0
            || cascadeResult.Tree.ById["side-left"].Layer != 0
            || cascadeResult.Tree.ById["side-right"].Layer != 0
            || cascadeResult.Tree.Nodes.Count(n => n.Layer == 0) != 6)
        {
            Assert.Fail("[prosequor] Skill-tree compiler fixture failed (shake-out cascade).");
        }

        SkillTreeJson abilityOk = new()
        {
            nodes =
            [
                new SkillTreeNodeJson
                {
                    id = "claydigger",
                    descriptionLang = "fixture-description",
                    descriptionParams = ["base", "perSkillLevel", "cap"],
                    requires = [],
                    tiers =
                    [
                        new SkillTreeTierJson
                        {
                            effects =
                            [
                                new AbilityEffectJson
                                {
                                    hook = "prosequor:block-interaction",
                                    verb = "prosequor:mutate-drops",
                                    phase = "quantity",
                                    action = "prosequor:number",
                                    when = new AbilityWhenJson
                                    {
                                        tags = ["target:<clay>"]
                                    },
                                    @params = new Newtonsoft.Json.Linq.JObject
                                    {
                                        ["op"] = "add",
                                        ["base"] = 0.10,
                                        ["perSkillLevel"] = 0.02,
                                        ["cap"] = 0.30
                                    }
                                }
                            ]
                        },
                        new SkillTreeTierJson
                        {
                            effects =
                            [
                                new AbilityEffectJson
                                {
                                    replicate = 0,
                                    @params = new Newtonsoft.Json.Linq.JObject
                                    {
                                        ["cap"] = 0.60
                                    }
                                }
                            ]
                        }
                    ]
                }
            ]
        };
        SkillTreeCompiler.CompileResult ability = SkillTreeCompiler.Compile(
            "fixture-ability", 100, abilityOk, hooks, actions, collections, ref order);
        if (!ability.Success || ability.Tree == null
            || ability.Tree.ById["claydigger"].TierAt(1).Rules.Count != 1
            || ability.Tree.ById["claydigger"].TierAt(1).Rules[0].Verb.Value != "prosequor:mutate-drops"
            || ability.Tree.ById["claydigger"].TierAt(2).Rules[0].Action != ActionIds.Number
            || ability.Tree.ById["claydigger"].TierAt(2).Rules[0].Parameters is not NumberSpec
            || ability.Tree.ById["claydigger"].TierAt(2).DescriptionArgs.Count != 3
            || Convert.ToSingle(
                ability.Tree.ById["claydigger"].TierAt(2).DescriptionArgs[2],
                System.Globalization.CultureInfo.InvariantCulture) != 0.60f
            || ability.Tree.ById["claydigger"].TierAt(1).DescriptionLang != "fixture-description"
            || ability.Tree.ById["claydigger"].TierAt(2).DescriptionLang != "fixture-description")
        {
            Assert.Fail("[prosequor] Skill-tree compiler fixture failed (effect replication and params overlay).");
        }

        // Overlay kept base/perSkillLevel and raised cap: level 25 → min(0.60, 0.10+0.50) = 0.60
        {
            NumberSpec inherited = (NumberSpec)ability.Tree.ById["claydigger"].TierAt(2).Rules[0].Parameters;
            ActionTestProgress progress = new();
            progress.SetSkillLevel("fixture-ability", 25);
            DropsContext drops = new()
            {
                World = null!,
                Tags = collections,
                Progress = progress
            };
            float scaled = inherited.Apply(
                0f,
                drops,
                ability.Tree.ById["claydigger"].TierAt(2).Rules[0].Source);
            if (Math.Abs(scaled - 0.60f) > 0.0001f)
            {
                Assert.Fail(string.Format(
                    "[prosequor] Skill-tree compiler fixture failed (number overlay evaluate). got={0}.",
                    scaled));
            }
        }

        SkillTreeJson descriptionOverride = new()
        {
            nodes =
            [
                new SkillTreeNodeJson
                {
                    id = "override",
                    descriptionLang = "shared-desc",
                    descriptionParams = ["base", "cap"],
                    requires = [],
                    tiers =
                    [
                        new SkillTreeTierJson
                        {
                            effects =
                            [
                                new AbilityEffectJson
                                {
                                    hook = "prosequor:block-interaction",
                                    verb = "prosequor:mutate-drops",
                                    phase = "quantity",
                                    action = "prosequor:number",
                                    when = new AbilityWhenJson { tags = ["target:<clay>"] },
                                    @params = new Newtonsoft.Json.Linq.JObject
                                    {
                                        ["op"] = "add",
                                        ["base"] = 0.10,
                                        ["perSkillLevel"] = 0.02,
                                        ["cap"] = 0.30
                                    }
                                }
                            ]
                        },
                        new SkillTreeTierJson
                        {
                            descriptionLang = "special-desc",
                            descriptionParams = ["cap"],
                            effects =
                            [
                                new AbilityEffectJson
                                {
                                    replicate = 0,
                                    @params = new Newtonsoft.Json.Linq.JObject { ["cap"] = 0.90 }
                                }
                            ]
                        }
                    ]
                }
            ]
        };
        SkillTreeCompiler.CompileResult descOverride = SkillTreeCompiler.Compile(
            "fixture-desc-override", 100, descriptionOverride, hooks, actions, collections, ref order);
        if (!descOverride.Success || descOverride.Tree == null
            || descOverride.Tree.ById["override"].TierAt(1).DescriptionLang != "shared-desc"
            || descOverride.Tree.ById["override"].TierAt(1).DescriptionArgs.Count != 2
            || descOverride.Tree.ById["override"].TierAt(2).DescriptionLang != "special-desc"
            || descOverride.Tree.ById["override"].TierAt(2).DescriptionArgs.Count != 1
            || Convert.ToSingle(
                descOverride.Tree.ById["override"].TierAt(2).DescriptionArgs[0],
                System.Globalization.CultureInfo.InvariantCulture) != 0.90f)
        {
            Assert.Fail("[prosequor] Skill-tree compiler fixture failed (per-tier description override).");
        }

        AbilityEffectJson[] descriptionRootEffects =
        [
            new AbilityEffectJson
            {
                hook = "prosequor:block-interaction",
                verb = "prosequor:interaction-speed",
                action = "prosequor:number",
                @params = new Newtonsoft.Json.Linq.JObject { ["op"] = "scale", ["base"] = 0, ["perSkillLevel"] = 0.001 }
            }
        ];
        SkillTreeJson descriptionExpressions = new()
        {
            nodes =
            [
                new SkillTreeNodeJson
                {
                    id = "expert",
                    descriptionLang = "expert-desc",
                    descriptionParams =
                    [
                        new Newtonsoft.Json.Linq.JObject
                        {
                            ["sum"] = new Newtonsoft.Json.Linq.JArray(
                                "root.0.perSkillLevel",
                                "prev.0.perSkillLevel"),
                            ["format"] = "fractionPercent"
                        },
                        new Newtonsoft.Json.Linq.JObject
                        {
                            ["sum"] = new Newtonsoft.Json.Linq.JArray(
                                "root.0.perSkillLevel",
                                "0.perSkillLevel"),
                            ["format"] = "fractionPercent"
                        }
                    ],
                    requires = [],
                    tiers =
                    [
                        new SkillTreeTierJson
                        {
                            effects =
                            [
                                new AbilityEffectJson
                                {
                                    hook = "prosequor:block-interaction",
                                    verb = "prosequor:interaction-speed",
                                    action = "prosequor:number",
                                    @params = new Newtonsoft.Json.Linq.JObject { ["op"] = "scale", ["base"] = 0, ["perSkillLevel"] = 0.001 }
                                }
                            ]
                        },
                        new SkillTreeTierJson
                        {
                            effects =
                            [
                                new AbilityEffectJson
                                {
                                    replicate = 0,
                                    @params = new Newtonsoft.Json.Linq.JObject { ["perSkillLevel"] = 0.0025 }
                                }
                            ]
                        }
                    ]
                }
            ]
        };
        SkillTreeCompiler.CompileResult expressions = SkillTreeCompiler.Compile(
            "fixture-description-expressions",
            100,
            descriptionExpressions,
            descriptionRootEffects,
            hooks,
            actions,
            collections,
            ref order);
        SkillTreeTierDef? expressionTier1 = expressions.Tree?.ById["expert"].TierAt(1);
        SkillTreeTierDef? expressionTier2 = expressions.Tree?.ById["expert"].TierAt(2);
        if (!expressions.Success
            || expressionTier1?.DescriptionArgs[0] is not FormattedDescriptionArg tier1Previous
            || expressionTier1.DescriptionArgs[1] is not FormattedDescriptionArg tier1Current
            || expressionTier2?.DescriptionArgs[0] is not FormattedDescriptionArg tier2Previous
            || expressionTier2.DescriptionArgs[1] is not FormattedDescriptionArg tier2Current
            || tier1Previous.Value != 0.001m
            || tier1Current.Value != 0.002m
            || tier2Previous.Value != 0.002m
            || tier2Current.Value != 0.0035m)
        {
            Assert.Fail("[prosequor] Skill-tree compiler fixture failed (startup description sums and formats).");
        }

        // Skill-list descriptionParams: progress-aware root + owned node coefficients.
        AbilityEffectJson[] listRootEffects =
        [
            new AbilityEffectJson
            {
                hook = "prosequor:block-interaction",
                verb = "prosequor:interaction-speed",
                action = "prosequor:number",
                @params = new Newtonsoft.Json.Linq.JObject { ["op"] = "scale", ["base"] = 0, ["perSkillLevel"] = 0.001 }
            }
        ];
        SkillTreeJson listHoverTree = new()
        {
            nodes =
            [
                new SkillTreeNodeJson
                {
                    id = "axeexpert",
                    requires = [],
                    tiers =
                    [
                        new SkillTreeTierJson
                        {
                            effects =
                            [
                                new AbilityEffectJson
                                {
                                    hook = "prosequor:block-interaction",
                                    verb = "prosequor:interaction-speed",
                                    action = "prosequor:number",
                                    @params = new Newtonsoft.Json.Linq.JObject { ["op"] = "scale", ["base"] = 0, ["perSkillLevel"] = 0.001 }
                                }
                            ]
                        },
                        new SkillTreeTierJson
                        {
                            effects =
                            [
                                new AbilityEffectJson
                                {
                                    replicate = 0,
                                    @params = new Newtonsoft.Json.Linq.JObject { ["perSkillLevel"] = 0.002 }
                                }
                            ]
                        }
                    ]
                }
            ]
        };
        SkillTreeCompiler.CompileResult listHoverCompile = SkillTreeCompiler.Compile(
            "fixture-skill-list-desc",
            100,
            listHoverTree,
            listRootEffects,
            hooks,
            actions,
            collections,
            ref order);
        if (!listHoverCompile.Success || listHoverCompile.Tree == null)
        {
            Assert.Fail("[prosequor] Skill-list description fixture failed (tree compile).");
        }

        SkillDef listHoverSkill = new()
        {
            Id = "fixture-skill-list-desc",
            DescriptionLang = "skilldesc-fixture",
            RootEffectSnapshots = SkillDescriptionResolver.SnapshotEffects(listRootEffects),
            Tree = listHoverCompile.Tree,
            DescriptionParamSpecs =
            [
                Newtonsoft.Json.Linq.JObject.Parse(
                    """{ "sum": [ "0.perSkillLevel", "axeexpert.0.perSkillLevel" ], "format": "fractionPercent" }""")
            ]
        };
        List<string> listHoverErrors = new();
        if (!SkillDescriptionResolver.TryValidate(
                listHoverSkill.Id,
                listHoverSkill.DescriptionParamSpecs,
                listHoverSkill.RootEffectSnapshots,
                listHoverSkill.Tree,
                listHoverErrors))
        {
            Assert.Fail(
                "[prosequor] Skill-list description fixture failed (validate): "
                + string.Join("; ", listHoverErrors));
        }

        IReadOnlyList<object> unownedArgs = SkillDescriptionResolver.Resolve(listHoverSkill, progress: null);
        if (unownedArgs.Count != 1
            || unownedArgs[0] is not FormattedDescriptionArg { Value: 0.001m, Format: "fractionPercent" })
        {
            Assert.Fail("[prosequor] Skill-list description fixture failed (unowned should be root only).");
        }

        Prosequor.Pure.Tests.ActionTestProgress ownedProgress = new();
        ownedProgress.SetUnlockTier(listHoverSkill.Id, "axeexpert", 2);
        IReadOnlyList<object> ownedArgs = SkillDescriptionResolver.Resolve(listHoverSkill, ownedProgress);
        if (ownedArgs.Count != 1
            || ownedArgs[0] is not FormattedDescriptionArg { Value: 0.003m, Format: "fractionPercent" })
        {
            Assert.Fail("[prosequor] Skill-list description fixture failed (owned tier-2 should sum 0.001+0.002).");
        }

        List<string> badNodeErrors = new();
        if (SkillDescriptionResolver.TryValidate(
                listHoverSkill.Id,
                [
                    Newtonsoft.Json.Linq.JObject.Parse(
                        """{ "sum": [ "missingnode.0.perSkillLevel" ], "format": "fractionPercent" }""")
                ],
                listHoverSkill.RootEffectSnapshots,
                listHoverSkill.Tree,
                badNodeErrors))
        {
            Assert.Fail("[prosequor] Skill-list description fixture failed (unknown node should reject).");
        }

        SkillTreeJson abilityBad = new()
        {
            nodes =
            [
                new SkillTreeNodeJson
                {
                    id = "bad",
                    requires = [],
                    tiers =
                    [
                        new SkillTreeTierJson
                        {
                            effects =
                            [
                                new AbilityEffectJson { type = "yieldMultiplier" }
                            ]
                        }
                    ]
                }
            ]
        };
        if (SkillTreeCompiler.Compile("fixture-ability-bad", 100, abilityBad, hooks, actions, collections, ref order).Success)
        {
            Assert.Fail("[prosequor] Skill-tree compiler fixture failed (legacy type should reject).");
        }

        SkillTreeJson badReplication = new()
        {
            nodes =
            [
                new SkillTreeNodeJson
                {
                    id = "bad-replication",
                    requires = [],
                    tiers =
                    [
                        new SkillTreeTierJson
                        {
                            effects = [new AbilityEffectJson { replicate = 0 }]
                        }
                    ]
                }
            ]
        };
        if (SkillTreeCompiler.Compile(
                "fixture-bad-replication",
                100,
                badReplication,
                hooks,
                actions,
                collections,
                ref order).Success)
        {
            Assert.Fail("[prosequor] Skill-tree compiler fixture failed (first-tier replicate should reject).");
        }

        VerifyOrphanRepair(hooks, actions, collections, ref order);

        if (Math.Abs(AbilityFormulas.YieldBonusFraction(10, 2, 30, 5) - 0.20f) > 0.0001f
            || Math.Abs(AbilityFormulas.YieldBonusFraction(10, 2, 30, 50) - 0.30f) > 0.0001f
            || Math.Abs(AbilityFormulas.ChanceFraction(50) - 0.5f) > 0.0001f)
        {
            Assert.Fail("[prosequor] Ability formula fixture failed.");
        }

        Assert.False(
            AbilityRuleCompiler.TryCompileWhen(
                "fixture",
                new AbilityWhenJson { verb = "dig", tags = ["target:<clay>"] },
                collections,
                out _,
                out string whenVerbError),
            "when.verb must fail compile");
        if (!whenVerbError.Contains("when.verb is removed", StringComparison.Ordinal))
        {
            Assert.Fail("[prosequor] Ability when.verb reject fixture failed.");
        }

        Assert.True(
            AbilityRuleCompiler.TryCompileWhen(
                "fixture",
                new AbilityWhenJson { tags = ["target:<clay>"] },
                collections,
                out AbilityWhenFilter? match,
                out string whenError),
            whenError);
        AbilityAction clayDig = new()
        {
            Verb = VerbIds.MutateDrops.Value,
            ActorUid = "fixture",
            Target = "game:clay-blue-raw"
        };
        AbilityAction dirtDig = new()
        {
            Verb = VerbIds.MutateDrops.Value,
            ActorUid = "fixture",
            Target = "game:soil-low-none"
        };
        if (!match!.Matches(clayDig) || match.Matches(dirtDig))
        {
            Assert.Fail("[prosequor] Ability matcher fixture failed.");
        }
    }

    static void VerifyOrphanRepair(IHookRegistry hooks, IAbilityActionRegistry actions, CollectionIndex collections, ref int order)
    {
        AbilityEffectJson[] noEffects = Array.Empty<AbilityEffectJson>();

        // Four downward children: orphan the last-declared among the blame set (d).
        SkillTreeJson fourDownward = new()
        {
            nodes =
            [
                new SkillTreeNodeJson { id = "root", requires = [] },
                new SkillTreeNodeJson { id = "a", requires = ["root"] },
                new SkillTreeNodeJson { id = "b", requires = ["root"] },
                new SkillTreeNodeJson { id = "c", requires = ["root"] },
                new SkillTreeNodeJson { id = "d", requires = ["root"] }
            ]
        };
        SkillTreeCompileRepair.RepairResult fork = SkillTreeCompileRepair.CompileWithOrphanRepair(
            "fixture-orphan-four-downward",
            100,
            fourDownward,
            noEffects,
            hooks,
            actions,
            collections,
            ref order);
        if (!fork.Compile.Success
            || fork.Compile.Tree == null
            || fork.Orphans.Count != 1
            || !string.Equals(fork.Orphans[0].NodeId, "d", StringComparison.OrdinalIgnoreCase)
            || fork.Compile.Tree.ById.ContainsKey("d")
            || !fork.Compile.Tree.ById.ContainsKey("a")
            || !fork.Compile.Tree.ById.ContainsKey("b")
            || !fork.Compile.Tree.ById.ContainsKey("c"))
        {
            Assert.Fail(
                "[prosequor] Orphan-repair fixture failed (four-downward should orphan last child 'd').");
        }

        // Cycle: orphan LIFO among cycle members so the tree compiles.
        SkillTreeJson cycle = new()
        {
            nodes =
            [
                new SkillTreeNodeJson { id = "root", requires = [] },
                new SkillTreeNodeJson { id = "x", requires = ["y"] },
                new SkillTreeNodeJson { id = "y", requires = ["x"] }
            ]
        };
        SkillTreeCompileRepair.RepairResult cycleRepair = SkillTreeCompileRepair.CompileWithOrphanRepair(
            "fixture-orphan-cycle",
            100,
            cycle,
            noEffects,
            hooks,
            actions,
            collections,
            ref order);
        if (!cycleRepair.Compile.Success
            || cycleRepair.Compile.Tree == null
            || cycleRepair.Orphans.Count == 0
            || cycleRepair.Compile.Tree.ById.ContainsKey("x")
                && cycleRepair.Compile.Tree.ById.ContainsKey("y"))
        {
            Assert.Fail("[prosequor] Orphan-repair fixture failed (cycle should orphan a blamed node).");
        }

        // Unknown require on one node: orphan that node; keep the rest.
        SkillTreeJson unknown = new()
        {
            nodes =
            [
                new SkillTreeNodeJson { id = "root", requires = [] },
                new SkillTreeNodeJson { id = "ok", requires = ["root"] },
                new SkillTreeNodeJson { id = "bad", requires = ["missing"] }
            ]
        };
        SkillTreeCompileRepair.RepairResult unknownRepair = SkillTreeCompileRepair.CompileWithOrphanRepair(
            "fixture-orphan-unknown",
            100,
            unknown,
            noEffects,
            hooks,
            actions,
            collections,
            ref order);
        if (!unknownRepair.Compile.Success
            || unknownRepair.Compile.Tree == null
            || unknownRepair.Orphans.Count != 1
            || !string.Equals(unknownRepair.Orphans[0].NodeId, "bad", StringComparison.OrdinalIgnoreCase)
            || unknownRepair.Compile.Tree.ById.ContainsKey("bad")
            || !unknownRepair.Compile.Tree.ById.ContainsKey("ok"))
        {
            Assert.Fail(
                "[prosequor] Orphan-repair fixture failed (unknown require should orphan 'bad').");
        }

        // Strict compile still rejects four-downward.
        if (SkillTreeCompiler.Compile(
                "fixture-strict-four-downward",
                100,
                fourDownward,
                hooks,
                actions,
                collections,
                ref order).Success)
        {
            Assert.Fail(
                "[prosequor] Orphan-repair fixture failed (strict compile must still reject four-downward).");
        }
    }
}
