---
name: prosequor
description: >-
  Implement Prosequor skill trees, XP, collections, stats, affixes, pools,
  unlocks, and pedigree using the mod's data layout and code injection points.
  Use when adding or editing Prosequor content or C# hooks, or when the user
  mentions Prosequor, skill trees, unlocks, xpRules, deeds, effort, collections,
  affixes, pools, contributions, attribute stats, or pedigree.
---

# Prosequor

Vintage Story progression mod (`modid`: `prosequor`). JSON **declares** matches and effects; C# **emits** facts and runs stations. Do not scrape authored JSON at runtime, and do not invent a parallel XP or unlock path.

This skill is the **layout and injection map**. Field grammar (effects, tags, hooks, actions, XP rule fields, contributions) lives in [reference.md](../../../reference.md). Admin commands: [commands.md](../../../commands.md). Design targets and historical remaps are not runtime truth — [attrref.md](../../../attrref.md), [docs/surface-verb-phase-remap.md](../../../docs/surface-verb-phase-remap.md).

Paths below are relative to the **prosequor mod root**.

When a specific id, amount, collection member, or rename is needed, **open the asset or type** named here. Do not treat examples in chat or old docs as the current catalog.

## Mental model

```
Gameplay moment
  → Station or XP adapter (server)
  → AbilityPipeline.Run(hook, verb, phase, context, seed)
     and/or Deed.Emit / Effort.Emit
  → JSON rules match the fact (tags, activity)
  → FatherXp pays buckets, or the pipeline folds the seed value
```

| Concern | Who owns it |
|---------|-------------|
| What pays, how much, which tags | Skill `xpRules` (data) |
| When a fact exists | Adapter / station (C#) |
| What an unlock changes | Skill `effects` / tier `effects`, or `stats/*.json` |
| Whether a rule is active | `IPlayerProgress` unlock tier or attribute score |
| Player numbers | `IPlayerProgress` — never the JSON files |

Single entry point: `src/ProsequorModSystem.cs`. There are no extra `ModSystem` classes.

**Singleplayer has two instances** (client and server). Always `ProsequorModSystem.For(api)`. Never cache one static instance across sides. Harmony patches are process-wide (shared, refcounted).

## Asset discovery

Other mods inject content **without forking** by depending on `prosequor` and shipping the same relative path under **their** domain:

```
assets/<modDomain>/config/prosequor/<category>/...
```

Loaders use `api.Assets.GetMany` on `config/prosequor/...` with a null domain, so **every loaded mod** is scanned. Sort is domain A→Z, then path A→Z.

| Merge | Rule |
|-------|------|
| Skills, XP rule ids, affix lists, attribute stats, trait maps | Last-win by id (warning logged) |
| Output pool entries | Same pool id merges; same `code` → later weight wins |
| Contributions | Applied to skill drafts **before** compile |

Vanilla-style asset patches (`assets/<domain>/patches/*.json`) are separate from this JSON. The player behavior is attached that way (`prosequor:progress`), not via the config folder.

**Content fingerprint** is computed at the end of `AssetsFinalize` from compiled definitions (not GameReady membership). Client and server hashes must match or the player gets a content-mismatch warning. Adding or editing config JSON changes the hash — both sides need the same assets.

## Load lifecycle

`Start` (both sides) registers entity/block/collectible behaviors, `AbilityBootstrap.RegisterBuiltIns` (hooks + actions), and Harmony.

`AssetsFinalize` (both sides), in this order — do not reorder dependencies:

1. `Collections.LoadFromAssets` — keys, include patterns, union declarations
2. `OutputPools.LoadFromAssets` — pool defs; registers pool ids as collection keys
3. `AffixLists.LoadFromAssets` — so skill compile can resolve `add-affix` list refs
4. `SkillRegistry.LoadFromAssets` — skills, then contribution grafts, then compile
5. `AttributeStats.LoadFromAssets`
6. `TraitAttributes.LoadFromAssets`
7. `LevelUps.LoadFromAssets` (also reads contribution `levelUps`)
8. `AbilityPipeline` constructed; `XpRules.LoadFromSkills` flattens embedded `xpRules`
9. `ContentFingerprint.Compute`

`StartServerSide` → `GameReady`: collection **membership** (pattern expand, C# fillers, unions), pool index resolve, `TagCriterion.BindAll`, catalogs (clay, block-break hardness). Keys must exist at compile time; item/block codes inside collections are not required until GameReady.

Server also starts XP adapters, effort polls, `FatherXp`, commands, and one activity-watch tick (effort, drain, and ModData flush for players admitted from `AfterInitialized`). Client attaches C-menu UI, tooltip bands, and network handlers. **Server owns truth.**

## Where content lives

| System | Author at | Loader | New content |
|--------|-----------|--------|-------------|
| Skill | `assets/<domain>/config/prosequor/skills/<id>.json` (one object per file) | `src/Data/SkillRegistry.cs` | New file, or graft an existing skill |
| Contribution | `config/prosequor/contributions/*.json` (array) | `src/Data/SkillContributionMerger.cs` | Extend/disable nodes, merge `xpRules`, graft `levelUps`. Optional engine-style `dependsOn`. Node ids **must** be `<yourDomain>:localId` |
| Collection | `config/prosequor/collections.json` or `collections/*.json` | `src/Data/CollectionRegistry.cs` | Named code sets for tags. Optional engine-style `dependsOn` |
| Output pool | `config/prosequor/pools.json` or `pools/*.json` | `src/Data/OutputPoolRegistry.cs` | Weighted item codes; id is also a collection key |
| Affix list | `config/prosequor/affixes.json` or `affixes/*.json` | `src/Data/AffixListRegistry.cs` | Display stamps; list id must be `<assetDomain>:localId` |
| Attribute stat | `config/prosequor/stats/<id>.json` | `src/Data/AttributeStatRegistry.cs` | Same effect envelope as skills, gated by score |
| Trait map | `config/prosequor/trait-attributes.json` | `src/Data/TraitAttributeRegistry.cs` | Vanilla class-trait → attribute mapping |
| Level-ups | `config/prosequor/level-ups.json` | `src/Data/LevelUpRegistry.cs` | Player-level grants; also graftable via contribution `levelUps` |
| XP rule | `xpRules` on the **owning skill** (or a contribution) | `src/Xp/XpRuleCompiler.cs` then `XpRuleRegistry` | No `skill` field — ownership is the enclosing skill |
| Handbook | `assets/prosequor/config/handbook/*.json` | game handbook | Player prose only; not a code hook |
| Lang / icons | `assets/<domain>/lang/`, texture paths on nodes | — | Required for contributed nodes |

`src/Data/SkillRegistry.cs` last-wins skill ids across domains. A second file with the same `id` replaces the whole skill — use **contributions** to extend one.

## Schema shape (not values)

Full fields: [reference.md](../../../reference.md). Shapes only:

**Skill file** — `id`, presentation (`nameLang`, `descriptionLang`, `descriptionParams`, `icon`), optional `hobby`, `attributeScores`, `xpRules`, always-on `effects`, `tree.nodes[]`.

**Node** — `id` unique within the skill, `requires` / `excludes`, `layout`, `tiers[]`, optional `specialization`. `requires`: **OR inside** a nested array, **AND across** entries. One-sided `excludes` is symmetrized at compile.

**Tier effects** are active only while **that** tier is owned. Lower tiers do not stack. Multi-tier nodes must `replicate` prior effects (tier 2+ only; never on root `effects` or tier 1).

**Effect row** — `hook` / `verb` / `phase` (omit phase → `default`) / `action` / `params` / `when` / `priority`. `contract` is **not** a JSON field; the action must fulfill the phase contract. Bare ids normalize to `prosequor:`. Catalog of hooks, phases, and actions: [reference.md](../../../reference.md) (Hooks, Contracts, Actions). Do not invent a hook that `AbilityBootstrap` did not register.

**XP rule** — `id`, exactly one of `amount` or `rate`, `when.activity` + `when.tags`. Amount rules may set `pay` and `payee`. Skill is implied. Activities without `:` get a `game:` prefix (`XpRuleRegistry.NormalizeActivity`). Prefer `prosequor:deed` and `prosequor:effort` plus tokens; a custom activity only pays if some rule's `when.activity` matches it.

**Collection row** — `id`, `includes` (wildcard codes, expanded at GameReady), optional `unions` / `excludes`, optional engine-style `dependsOn`. In tags, `<collectionId>` is membership. Built-in keys also come from C# fillers (`AbilityBootstrap`) and pool registration — a key can exist with empty membership until GameReady.

**Pool row** — `id` (`domain:localId`), `entries[]` of `{ code, weight }`. Unknown item codes warn and skip at index time.

**Affix list** — `id` (`domain:localId`), `entries[]` of `{ code, lang, color? }`. Gameplay is a separate effect; the list is presentation metadata resolved at **skill compile** when an effect uses `{ "list", "item" }`.

**Stat file** — `id` must be a known attribute (`src/Data/AttributeIds.cs`). `rules[]` use the effect envelope plus score gates (`minScore` / `maxScore`). Unknown id → skipped.

**Level of caps and curves** — authored `maxLevel` is **ignored**. Kind and cap come from `src/Data/SkillKind.cs` (`SkillKindPolicy`) and `src/Data/XpCurves.cs`. Classification: `hobby: true` wins; else a tree with any `specialization` node; else a non-empty tree; else no/empty tree. Read those types; do not hardcode caps. Hobby specialization flags are stripped at compile. Hobbies spend **local** unlock points derived from skill level vs tree cost, not global points — formula in [reference.md](../../../reference.md).

## Ability address

Stations build a typed context (with `IPlayerProgress` and a fact) and call `AbilityPipeline.Run(hook, verb, phase, context, seed)`. Matching rules on that address all run, lower `priority` first, each action receiving the previous output. Nested gate children (`chance`, `has-unlock`) must stay on the same address.

`AbilityPipeline.IsActive`:

| Rule source | Active when |
|-------------|-------------|
| Skill root `effects` | Always |
| Tree tier | `GetUnlockTier(skillId, nodeId) == rule.Source.Tier` (and > 0) |
| Attribute stat | `GetAttribute(id)` within min/max score |

New hook or action: register both in `src/Ability/AbilityBootstrap.cs`, implement `IAbilityActionHandler`, and document the contract in `reference.md`. A hook without a matching action (or the reverse) will not fold.

Prefer JSON `effects` plus an existing station. Direct `HasUnlock` in C# is for hard gates the fold cannot express — copy `src/Ability/AnvilBitsForgingOps.cs`.

## XP: emit, don't award

Gameplay must not call `IPlayerProgress.AddSkillXp`. That method is the pay sink. Facts go through `src/Xp/Activity/Deed.cs` or `Effort.cs`; `src/Xp/FatherXp.cs` delivers to the online entity or an offline mailbox.

| Situation | Call | JSON activity |
|-----------|------|----------------|
| One-shot completion (break, craft take, growth stage) | `Deed.Emit` or `mod.EmitDeed` | `prosequor:deed` (`amount`) |
| Game already pulses (interact step, pour tick) | `Effort.Emit` or `mod.EmitEffort` | `prosequor:effort` (`rate`) |
| Continuous state, no pulse (mounted, fishing) | `mod.RegisterEffortPoll` | `prosequor:effort` |

`RegisterActivityWrapper` is deprecated for rate XP.

Publish roles the rule will read (`caller`, `target`, tokens, metric, quantity). Payees (`user`, `maker`, `contributor`, `contributors`) are chosen by the **rule**, not by merging maker into contributors. Emit `makerUid` and `contributors` separately. Channel and token catalogs: [reference.md](../../../reference.md) (XP rules). Prefer `DeedToken` / `EffortToken` for standard tags. A new token needs the enum, an emitter, and a doc line in that file — data-only rules can only match tokens something already emits.

Offline XP is held on the world save and flushed on join. Test plans without a world: `Deed.PlanPays`.

## Runtime reads and persistence

```csharp
ProsequorModSystem? mod = ProsequorModSystem.For(api);
IPlayerProgress? live = ProsequorModSystem.GetProgress(player);
IPlayerProgress? parked = ProsequorModSystem.GetProgress(api, playerUid); // online, else ProgressPark
```

Read levels, XP, unlocks, tiers, attributes, and bars from `src/Player/IPlayerProgress.cs`. Mutations (`TryPurchaseNode`, grants, debug sets) are server-only.

| Store | Key | Role |
|-------|-----|------|
| Player ModData | `ProgressStore.ModDataKey` (`prosequor-progress`) | Authoritative blob (`PlayerProgressState`) |
| Entity WatchedAttributes | `ProgressStore.AttrTree` (`prosequor`) | Client mirror |
| World save | `FatherXp.SaveDataKey` (`prosequor-father-xp`) | Offline XP mailbox |
| `ProgressPark` | player uid, RAM | Offline reads this uptime only |

Blob schema version is `PlayerProgressState.CurrentSchema`. XP is truth — load reconciles levels from lifetime XP. Client UI must wait for `HasSyncedMirror` before treating zeros as real. Unlock purchases: `src/Network/ProgressNetwork.cs` (client request, server `TryPurchaseNode`). There is no custom progress snapshot packet.

`src/Data/UnlockIdRemap.cs` is a **one-hop** rename table applied when stored schema is older than `UnlockIdRemap.Schema`. Do not list or extend it for new nodes. Touch it only when renaming an id that already exists in player saves, and bump schema with a migration in `ProgressStore.TryHydrateStored`. Lookups are not chained.

Affix stamps: `src/Ability/ItemAffixes.cs` tree `prosequorAffixes` on the **stack**. Pedigree blob is the unit of record; the tree is a display cache. Never write `ItemStack.ItemAttributes` (type-level). Reserved leading grade code: `ItemAffixes.QualityCode`.

Attribute ids and score clamps: `src/Data/AttributeIds.cs` and `src/Data/AttributeGrowth.cs`. Growth credit is separate from the integer score. Vanilla `Entity.Stats` modifiers (for example riding) are stations, not the attribute blob.

## Agent workflows

### New skill (data only)

1. Add `assets/<domain>/config/prosequor/skills/<id>.json` (one object).
2. Set `id`, presentation, `hobby` only if it should be a hobby, `tree` and/or root `effects` / `xpRules`.
3. Let `SkillKindPolicy` derive the cap. Do not author `maxLevel`.
4. Lang keys and icon. Open an existing skill file only to copy **shape**, then replace ids.
5. Compile/playtest. Confirm the skill is in the registry log and the fingerprint matches on client and server.

### Graft an existing skill (other mod or local extension)

1. Depend on `prosequor` in `modinfo.json`.
2. `assets/<yourDomain>/config/prosequor/contributions/<name>.json` — JSON **array**. Optional engine-style `dependsOn` skips the entry unless those mods are loaded.
3. Order per entry: `dependsOn` → `disable` → `xpRules` → `nodes` (`replaces` then append). `"disable": "all"` stops that entry.
4. Contributed node ids must be `<yourDomain>:localId`. Prereqs may use plain ids of nodes already on that skill.
5. Ship lang and icons. Grammar and examples: [reference.md](../../../reference.md) (Contributions).

### New XP source

1. Decide deed vs effort vs poll (table above).
2. Server-only adapter under `src/Xp/Adapters/` (static class + Harmony/station — no shared interface). Copy the closest file in the index below.
3. Emit tokens and roles; do not call `AddSkillXp`.
4. Add matching `xpRules` on the owning skill or a contribution. An emit with no rule pays nothing.
5. Pure test: `Deed.PlanPays` fixture. World interaction: Atlas scenario.

### New station or action

1. If an existing hook/verb/phase can fold the number, add JSON only.
2. Else: Harmony patch delegates to a Station; Station calls `pipeline.Run`. Copy `src/Ability/DropsStation.cs` or `src/Ability/CraftMutateOutputStation.cs`.
3. Register hook and action together in `AbilityBootstrap`.
4. Update the contract section of `reference.md`.

### Collection, pool, or affix

1. Add JSON under your domain (table above).
2. Collections: pattern or union is enough if codes exist at GameReady. Optional `dependsOn` skips that row unless those mods are loaded. C# membership (`CollectionIndex.AddCode` / `AddWeighted`) only when codes are not knowable from assets (see clay-formed catalog).
3. Pools are referenced as collection tags once indexed.
4. Affixes: list JSON, then `prosequor:add-affix` on an effect (inline or `{ list, item }`). Stamp at the mutate-output station, not onto the type.

## `src/` map

| Folder | Use |
|--------|-----|
| `Data/` | JSON loaders, compilers, progress blob, fingerprint, unlock remap |
| `Xp/` | Rules, matcher, FatherXp, deeds/effort, adapters |
| `Ability/` | Stations, patches, actions, affix/pedigree/quality, pipeline host |
| `Ability/Hooks/` | `AbilityPipeline`, hook context |
| `Player/` | `EntityBehaviorProgress`, `IPlayerProgress`, `ProgressPark` |
| `Progress/` | Unlock-point policy, eligibility, level-up rules at runtime |
| `Network/` | Unlock purchase, fingerprint, HUDs |
| `Client/` | Skills tab, stats panel, tooltips, HUDs |
| `Commands/` | `/prosequor` registrars |
| `Inventory/` | Carry-inventory extension |

## Copy-from index

| Task | Start here |
|------|------------|
| Discrete XP + Harmony | `src/Xp/Adapters/GrowthXp.cs`, `TillSoilXp.cs` |
| Rate XP on an existing pulse | `src/Xp/Adapters/WateringXp.cs` |
| Contributor payee | `src/Xp/Adapters/FertilizerAbsorbXp.cs` |
| Patch vs adapter split | `src/Ability/FertilizerAbsorbPatches.cs` + matching adapter |
| Station + pipeline | `src/Ability/DropsStation.cs`, `InteractionSpeedStation.cs` |
| Craft output mutate | `src/Ability/CraftMutateOutputStation.cs` |
| Hard unlock gate | `src/Ability/AnvilBitsForgingOps.cs` |
| Block / farmland pedigree | `src/Ability/ProsequorBlockPedigreeStation.cs` |
| Item pedigree | `src/Ability/CraftAttribution.cs` |
| Tooltip credit | `src/Ability/OwnerCredit.cs` |
| Meal cook vs potter | `src/Ability/MealHostCredit.cs` |
| Bootstrap | `src/Ability/AbilityBootstrap.cs` |
| Lifecycle | `src/ProsequorModSystem.cs` |
| Pure XP test | `tests/Prosequor.Pure.Tests/DeedFixtures.cs` (wire `VerifyAll` in `MigratedFixtureTests.cs`) |
| World scenario | `tests/Prosequor.Scenarios/FarmingTillSoilScenarios.cs` |

## Tests

| Project | When |
|---------|------|
| `tests/Prosequor.Pure.Tests` | Compile math, `Deed.PlanPays`, pipeline folds, blob transforms — no world |
| `tests/Prosequor.Scenarios` | Atlas headless server: place, join, emit, stamp |

New Harmony: smoke/transpile tests next to existing `HarmonyPatchAllSmokeTests` / `HarmonyTranspileBindTests`.

## Anti-patterns

| Don't | Do |
|-------|-----|
| `AddSkillXp` from gameplay | `Deed.Emit` / `Effort.Emit` + `xpRules` |
| Scrape skill JSON for unlocks | `HasUnlock` / `GetUnlockTier` |
| Award XP on the client | Server-only emit |
| One static `ProsequorModSystem` | `For(api)` |
| Second skill file with an existing `id` to "add a node" | Contribution graft |
| Custom activity string with no matching rule | `prosequor:deed` / `prosequor:effort` + tokens |
| Merge maker into contributors | Separate `makerUid` and `contributors`; rule chooses `payee` |
| Affixes on `ItemAttributes` | `ItemAffixes` stack tree; pedigree is record |
| Hook without a registered action | Pair them in `AbilityBootstrap` |
| Author `maxLevel` | `SkillKindPolicy` |
| Assume collection membership at `AssetsFinalize` | Keys then; codes at `GameReady` |
| `RegisterActivityWrapper` for new rate XP | `EmitEffort` or `RegisterEffortPoll` |
| Chain unlock renames in `UnlockIdRemap` | Single hop; bump schema |
| Treat `reference.md` as optional when adding a hook/action/token | Update that catalog in the same change |

## Packaging

Edit `assets/` and `src/`. Do not hand-edit `dist/`. Build/package with the repo VSpythonTK skill. JSON comments are not the config format here — skill and contribution files are strict JSON (handbook files may differ; don't copy that style into `config/prosequor/`).
