using Prosequor.Ability;
using Prosequor.Ability.Hooks;
using Prosequor.Xp;
using Prosequor.Xp.Activity;
using Xunit;

namespace Prosequor.Pure.Tests;

/// <summary>Pure fixtures for effort Emit, open tokens, and materialization.</summary>
public static class EffortFixtures
{
    public static void VerifyAll()
    {
        VerifyEffortTokenTags();
        VerifyOpenBareTokenParse();
        VerifyStampChannelsAndTarget();
        VerifyMaterializeAddsMovingOnMount();
        VerifyTaglessEffortWarns();
        VerifyPollRegistryNullReplaceUnregister();
    }

    static void VerifyEffortTokenTags()
    {
        if (EffortToken.Interacting.ToTag() != EffortTokenTags.Interacting
            || !EffortTokenTags.TryParse("boating", out EffortToken t)
            || t != EffortToken.Boating)
        {
            Assert.Fail("[prosequor] EffortToken tag map failed.");
        }
    }

    static void VerifyOpenBareTokenParse()
    {
        CollectionIndex collections = new();
        collections.EnsureKey("pan");
        if (!TagCriterionParser.TryParse("interacting", collections, out TagCriterion? token, out _)
            || token is not TokenCriterion tc
            || !string.Equals(tc.Token, "interacting", StringComparison.OrdinalIgnoreCase))
        {
            Assert.Fail("[prosequor] Bare interacting should parse as TokenCriterion.");
        }

        if (!TagCriterionParser.TryParse("custom-boost", collections, out TagCriterion? custom, out _)
            || custom is not TokenCriterion)
        {
            Assert.Fail("[prosequor] Open custom bare token should parse as TokenCriterion.");
        }

        if (!TagCriterionParser.TryParse("held:<pan>", collections, out TagCriterion? held, out _)
            || held is not RoleCollectionCriterion heldCol
            || heldCol.Role != FactRole.Caller)
        {
            Assert.Fail("[prosequor] held:<pan> should still be a collection criterion on caller.");
        }

        if (!TagCriterionParser.TryParse("caller:<pan>", collections, out TagCriterion? caller, out _)
            || caller is not RoleCollectionCriterion)
        {
            Assert.Fail("[prosequor] caller:<pan> should parse as a collection criterion.");
        }
    }

    static void VerifyStampChannelsAndTarget()
    {
        Effort.Forget("fixture-player");
        Effort.Store.Put(
            "fixture-player",
            EffortTokenTags.Interacting,
            [EffortTokenTags.Interacting],
            target: "game:bonysoil",
            mount: null,
            ground: null,
            totalHours: 10.0);
        Effort.Store.Put(
            "fixture-player",
            EffortTokenTags.Boating,
            [EffortTokenTags.Mounted, EffortTokenTags.Boating],
            target: null,
            mount: "game:boat-raft",
            ground: "game:water-still-7",
            totalHours: 10.0);

        IReadOnlyList<EffortStamp> fresh = Effort.Store.GetFresh("fixture-player", 10.0);
        if (fresh.Count != 2)
        {
            Assert.Fail("[prosequor] Expected two effort stamp channels.");
        }

        EffortStamp? pan = null;
        EffortStamp? boat = null;
        foreach (EffortStamp s in fresh)
        {
            if (s.Channel == EffortTokenTags.Interacting)
            {
                pan = s;
            }

            if (s.Channel == EffortTokenTags.Boating)
            {
                boat = s;
            }
        }

        if (pan == null
            || pan.Value.Target != "game:bonysoil"
            || boat == null
            || boat.Value.Mount != "game:boat-raft")
        {
            Assert.Fail("[prosequor] Emitter-supplied target/mount missing on stamps.");
        }

        Effort.Forget("fixture-player");
    }

    static void VerifyMaterializeAddsMovingOnMount()
    {
        Effort.Forget("fixture-move");
        Effort.Store.Put(
            "fixture-move",
            EffortTokenTags.Riding,
            [EffortTokenTags.Mounted, EffortTokenTags.Riding],
            target: null,
            mount: "game:horse",
            ground: null,
            totalHours: 5.0);

        // Mirror watcher materialize (no IPlayer): build facts from stamps.
        List<ActivityFact> facts = new();
        foreach (EffortStamp stamp in Effort.Store.GetFresh("fixture-move", 5.0))
        {
            HashSet<string> tokens = new(stamp.Tokens, StringComparer.OrdinalIgnoreCase);
            tokens.Add(EffortTokenTags.Moving);
            facts.Add(new ActivityFact
            {
                Activity = Effort.Activity,
                Held = "game:stick",
                Mount = stamp.Mount,
                Tokens = tokens
            });
        }

        if (facts.Count != 1
            || !facts[0].Tokens.Contains(EffortTokenTags.Moving)
            || facts[0].Mount != "game:horse"
            || facts[0].Target != null)
        {
            Assert.Fail("[prosequor] Materialize should keep emitter mount and add moving.");
        }

        Effort.Forget("fixture-move");
    }

    static void VerifyTaglessEffortWarns()
    {
        CollectionIndex collections = new();
        collections.EnsureKey("pan");
        int order = 0;
        List<string> warnings = new();
        _ = XpRuleCompiler.CompileAll(
            [
                new XpRuleJson
                {
                    id = "warn-me",
                    rate = 0.01f,
                    when = new XpRuleWhenJson { activity = Effort.Activity }
                }
            ],
            "fixture",
            collections,
            ref order,
            warnings.Add);

        if (warnings.Count == 0
            || warnings.TrueForAll(w => !w.Contains("no when.tags", StringComparison.Ordinal)))
        {
            Assert.Fail("[prosequor] Tagless effort rate rule should warn.");
        }
    }

    static void VerifyPollRegistryNullReplaceUnregister()
    {
        const string id = "fixture:effort-poll";
        Effort.UnregisterPoll(id);

        int hits = 0;
        Effort.RegisterPoll(id, (_, _) =>
        {
            hits++;
            return null;
        });
        Effort.RegisterPoll(
            id,
            (_, _) => new EffortPollResult(
                [EffortTokenTags.Interacting],
                Target: "game:bonysoil",
                Channel: EffortTokenTags.Interacting));

        int countForId = 0;
        EffortPoll? winner = null;
        foreach (KeyValuePair<string, EffortPoll> kv in Effort.Polls)
        {
            if (string.Equals(kv.Key, id, StringComparison.OrdinalIgnoreCase))
            {
                countForId++;
                winner = kv.Value;
            }
        }

        if (countForId != 1 || winner == null)
        {
            Effort.UnregisterPoll(id);
            Assert.Fail("[prosequor] Effort poll same id should last-win to one entry.");
        }

        // Pure: invoke the registered poll without a world player.
        EffortPollResult? sample = winner(null!, null!);
        if (sample == null
            || sample.Value.Tokens.Count != 1
            || sample.Value.Target != "game:bonysoil"
            || hits != 0)
        {
            Effort.UnregisterPoll(id);
            Assert.Fail("[prosequor] Replaced poll should return sample; null poll must be gone.");
        }

        Effort.Store.Put(
            "fixture-poll-player",
            sample.Value.Channel ?? EffortTokenTags.Interacting,
            sample.Value.Tokens,
            sample.Value.Target,
            sample.Value.Mount,
            sample.Value.Ground,
            totalHours: 3.0);
        IReadOnlyList<EffortStamp> fresh = Effort.Store.GetFresh("fixture-poll-player", 3.0);
        if (fresh.Count != 1 || fresh[0].Target != "game:bonysoil")
        {
            Effort.UnregisterPoll(id);
            Effort.Forget("fixture-poll-player");
            Assert.Fail("[prosequor] Poll sample should stamp like Emit.");
        }

        Effort.UnregisterPoll(id);
        Effort.Forget("fixture-poll-player");
        foreach (KeyValuePair<string, EffortPoll> kv in Effort.Polls)
        {
            if (string.Equals(kv.Key, id, StringComparison.OrdinalIgnoreCase))
            {
                Assert.Fail("[prosequor] UnregisterPoll should remove the poll.");
            }
        }
    }
}
