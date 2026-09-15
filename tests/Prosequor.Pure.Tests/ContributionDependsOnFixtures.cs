using Newtonsoft.Json;
using Prosequor.Data;
using Xunit;

namespace Prosequor.Pure.Tests;

/// <summary>Engine-style contribution <c>dependsOn</c> evaluation.</summary>
public static class ContributionDependsOnFixtures
{
    public static void VerifyAll()
    {
        VerifyAbsentOrEmptyIsSatisfied();
        VerifyRequiredModPresentAndAbsent();
        VerifyInvert();
        VerifyAndAcrossClauses();
        VerifyNullClauseFails();
        VerifyFormat();
        VerifyJsonShape();
        VerifyCollectionJsonShape();
    }

    static HashSet<string> Loaded(params string[] ids) => new(ids);

    static void VerifyAbsentOrEmptyIsSatisfied()
    {
        HashSet<string> loaded = Loaded("game", "prosequor");
        if (!ContributionDependsOn.IsSatisfied(null, loaded)
            || !ContributionDependsOn.IsSatisfied([], loaded)
            || !ContributionDependsOn.IsSatisfied(Array.Empty<ContributionModDependence>(), loaded))
        {
            Assert.Fail("[prosequor] dependsOn fixture failed (null/empty should apply).");
        }
    }

    static void VerifyRequiredModPresentAndAbsent()
    {
        ContributionModDependence[] required = [new() { modid = "wildcraft" }];
        if (!ContributionDependsOn.IsSatisfied(required, Loaded("game", "wildcraft"))
            || ContributionDependsOn.IsSatisfied(required, Loaded("game", "prosequor")))
        {
            Assert.Fail("[prosequor] dependsOn fixture failed (required mod present/absent).");
        }
    }

    static void VerifyInvert()
    {
        ContributionModDependence[] inverted = [new() { modid = "wildcraft", invert = true }];
        if (ContributionDependsOn.IsSatisfied(inverted, Loaded("game", "wildcraft"))
            || !ContributionDependsOn.IsSatisfied(inverted, Loaded("game", "prosequor")))
        {
            Assert.Fail("[prosequor] dependsOn fixture failed (invert).");
        }
    }

    static void VerifyAndAcrossClauses()
    {
        ContributionModDependence[] both =
        [
            new() { modid = "wildcraft" },
            new() { modid = "expandedfoods", invert = true }
        ];
        if (!ContributionDependsOn.IsSatisfied(both, Loaded("game", "wildcraft"))
            || ContributionDependsOn.IsSatisfied(both, Loaded("game", "wildcraft", "expandedfoods"))
            || ContributionDependsOn.IsSatisfied(both, Loaded("game")))
        {
            Assert.Fail("[prosequor] dependsOn fixture failed (AND across clauses).");
        }
    }

    static void VerifyNullClauseFails()
    {
        ContributionModDependence[] clauses = [null!];
        if (ContributionDependsOn.IsSatisfied(clauses, Loaded("game")))
        {
            Assert.Fail("[prosequor] dependsOn fixture failed (null clause should fail).");
        }
    }

    static void VerifyFormat()
    {
        string formatted = ContributionDependsOn.Format(
        [
            new() { modid = "wildcraft" },
            new() { modid = "expandedfoods", invert = true }
        ]);
        if (formatted != "wildcraft,!expandedfoods"
            || ContributionDependsOn.Format(null) != ""
            || ContributionDependsOn.Format([]) != "")
        {
            Assert.Fail($"[prosequor] dependsOn fixture failed (format got '{formatted}').");
        }
    }

    static void VerifyJsonShape()
    {
        const string json = """
            [
              {
                "skill": "husbandry",
                "dependsOn": [{ "modid": "wildcraft" }],
                "xpRules": [{ "id": "mymod:wild", "amount": 1 }]
              },
              {
                "levelUps": { "disable": ["prosequor:earn-specialization-point"] },
                "dependsOn": [{ "modid": "wildcraft", "invert": true }]
              }
            ]
            """;

        SkillContributionJson[]? rows = JsonConvert.DeserializeObject<SkillContributionJson[]>(json);
        if (rows == null
            || rows.Length != 2
            || rows[0].dependsOn is not { Length: 1 } required
            || required[0].modid != "wildcraft"
            || required[0].invert
            || rows[1].dependsOn is not { Length: 1 } inverted
            || inverted[0].modid != "wildcraft"
            || !inverted[0].invert
            || !ContributionDependsOn.IsSatisfied(rows[0].dependsOn, Loaded("wildcraft"))
            || ContributionDependsOn.IsSatisfied(rows[1].dependsOn, Loaded("wildcraft")))
        {
            Assert.Fail("[prosequor] dependsOn fixture failed (JSON patch shape).");
        }
    }

    static void VerifyCollectionJsonShape()
    {
        const string json = """
            [
              {
                "id": "berry-bush",
                "includes": ["game:smallberrybush-*"]
              },
              {
                "id": "berry-bush",
                "dependsOn": [{ "modid": "wildcraft" }],
                "includes": ["wildcraft:berrybush-*"]
              }
            ]
            """;

        CollectionJson[]? rows = JsonConvert.DeserializeObject<CollectionJson[]>(json);
        if (rows == null
            || rows.Length != 2
            || rows[0].dependsOn != null
            || !ContributionDependsOn.IsSatisfied(rows[0].dependsOn, Loaded("game"))
            || rows[1].dependsOn is not { Length: 1 } required
            || required[0].modid != "wildcraft"
            || required[0].invert
            || !ContributionDependsOn.IsSatisfied(rows[1].dependsOn, Loaded("wildcraft"))
            || ContributionDependsOn.IsSatisfied(rows[1].dependsOn, Loaded("game")))
        {
            Assert.Fail("[prosequor] dependsOn fixture failed (collection JSON shape).");
        }
    }
}
