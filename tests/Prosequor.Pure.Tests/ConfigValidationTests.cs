using Newtonsoft.Json.Linq;
using Prosequor.Pure.Tests.ConfigValidation;
using Xunit;

namespace Prosequor.Pure.Tests;

/// <summary>
/// Pre-compile config checks. The first rule validates skill-file keys against the
/// JSON DTOs; other config families can join the same runner later.
/// </summary>
public class ConfigValidationTests
{
    [Fact]
    [Trait("Layer", "Content")]
    [Trait("Kind", "ConfigValidation")]
    public void SkillShapeRule_Should_FlagUnknownKeyOnEffect()
    {
        JObject skill = JObject.Parse(
            """
            {
              "id": "fixture",
              "tree": {
                "nodes": [
                  {
                    "id": "n",
                    "tiers": [
                      {
                        "effects": [
                          {
                            "hook": "prosequor:block-interaction",
                            "verb": "prosequor:mutate-drops",
                            "action": "prosequor:number",
                            "params": { "op": "add", "base": 0.1 },
                            "minSkillLevel": 25
                          }
                        ]
                      }
                    ]
                  }
                ]
              }
            }
            """);

        List<ConfigFinding> findings = new();
        new SkillShapeRule().Check("skills/fixture.json", skill, findings);

        Assert.Single(findings);
        ConfigFinding finding = findings[0];
        Assert.Equal("skill-shape", finding.RuleName);
        Assert.Equal("/tree/nodes/0/tiers/0/effects/0/minSkillLevel", finding.Pointer);
        Assert.Contains("minSkillLevel", finding.Message);
        Assert.Contains("effect", finding.Message);
    }

    [Fact]
    [Trait("Layer", "Content")]
    [Trait("Kind", "ConfigValidation")]
    public void SkillShapeRule_Should_AllowMinSkillLevelOnTier()
    {
        JObject skill = JObject.Parse(
            """
            {
              "id": "fixture",
              "tree": {
                "nodes": [
                  {
                    "id": "n",
                    "minSkillLevel": 10,
                    "tiers": [
                      {
                        "minSkillLevel": 25,
                        "effects": [
                          {
                            "hook": "prosequor:block-interaction",
                            "verb": "prosequor:mutate-drops",
                            "action": "prosequor:number",
                            "params": { "op": "add", "base": 0.1 }
                          }
                        ]
                      }
                    ]
                  }
                ]
              }
            }
            """);

        List<ConfigFinding> findings = new();
        new SkillShapeRule().Check("skills/fixture.json", skill, findings);

        Assert.Empty(findings);
    }

    [Fact]
    [Trait("Layer", "Content")]
    [Trait("Kind", "ConfigValidation")]
    public void SkillShapeRule_Should_AcceptKnownKeysOnlySkill()
    {
        JObject skill = JObject.Parse(
            """
            {
              "id": "fixture",
              "nameLang": "skill-fixture",
              "hobby": false,
              "attributeScores": [ { "id": "strength", "value": 1.0 } ],
              "xpRules": [
                {
                  "id": "prosequor:fixture",
                  "amount": 0.5,
                  "when": { "activity": "prosequor:deed", "tags": ["block-broken"] }
                }
              ],
              "effects": [
                {
                  "hook": "prosequor:block-interaction",
                  "verb": "prosequor:interaction-speed",
                  "action": "prosequor:number",
                  "when": { "tags": ["caller:<shovel>"] },
                  "params": { "op": "scale", "base": 0 }
                }
              ],
              "tree": {
                "nodes": [
                  {
                    "id": "n",
                    "cost": 1,
                    "layout": { "layerOffset": 0 },
                    "requires": [],
                    "tiers": [ { "effects": [] } ]
                  }
                ]
              }
            }
            """);

        List<ConfigFinding> findings = new();
        new SkillShapeRule().Check("skills/fixture.json", skill, findings);

        Assert.Empty(findings);
    }

    [Fact]
    [Trait("Layer", "Content")]
    [Trait("Kind", "ConfigValidation")]
    public void ShippedConfig_Should_HaveNoUnknownSkillKeys()
    {
        string repoRoot = ConfigValidationRunner.FindRepoRoot();
        ConfigValidationRunner runner = new([new SkillShapeRule()]);
        IReadOnlyList<ConfigFinding> findings = runner.Scan(repoRoot);

        if (findings.Count > 0)
        {
            Assert.Fail(ConfigValidationRunner.FormatReport(findings));
        }
    }
}
