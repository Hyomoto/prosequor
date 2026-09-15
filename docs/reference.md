This file is for user-facing JSON documentation,

# Skill JSON reference
Path: `assets/<moddomain>/config/prosequor/skills/*.json` (one skill object per file).

Address: **hook** → **verb** → **phase** (omit → `default`) → **contract**. Actions bind to a hook+verb+phase and must fulfill that phase’s contract (or be side-effect actions on a phase with **no** fold contract). `contract` is not a JSON field.

```
requires  →  who must be owned (logic)
layout    →  where it draws relative to parents (presentation)
excludes  →  who you cannot also own
```

---

## Skill file root


| Field               | Meaning                                                                                                              |
| ------------------- | -------------------------------------------------------------------------------------------------------------------- |
| `id`                | Stable skill id (required)                                                                                           |
| `nameLang`          | List title lang key (default `skill-{id}`)                                                                           |
| `descriptionLang`   | Optional C-menu list-hover blurb                                                                                     |
| `descriptionParams` | Optional `{0}`… args for `descriptionLang` (same grammar as nodes)                                                   |
| `icon`              | Texture path (bare paths resolve under `prosequor:`)                                                                 |
| `hobby`             | `true` = hobby skill (cap 20, local unlock points, own Skills-tab section; specialization flags stripped at compile) |
| `attributeScores`   | Optional bucket fill deposited on each skill level gained                                                            |
| `xpRules`           | XP amount/rate rules owned by this skill                                                                             |
| `effects`           | Always-on baseline rules                                                                                             |
| `tree`              | Unlock tree (`{ "nodes": [ … ] }`)                                                                                   |


Level caps are **derived**, not authored (`maxLevel` is ignored if present):


| Kind           | Cap | How classified                                    |
| -------------- | --- | ------------------------------------------------- |
| Hobby          | 20  | `"hobby": true` (wins over everything)            |
| Specialization | 100 | Tree has at least one `specialization: true` node |
| Minor          | 50  | Tree with no specialization nodes                 |
| Passive        | 50  | No tree / empty tree                              |

Shipped passives (no tree): Forager, Athletics, Swimming, Sneaking, Adaptation. Root `effects` and `xpRules` still apply; the Skills tab does not open a tree.




### Hobby unlock points

Hobby trees do **not** spend global unlock points. Entitlement is derived from the compiled tree:

```text
entitled  = ceil(totalTierCost × skillLevel / maxLevel)
spendable = max(0, entitled − sum of owned tier costs)
```

Cost-0 tiers do not contribute. Global points still come from skill milestones (+1 per 20 levels) and player level-up rules (`level-ups.json`) as usual; they simply cannot buy hobby nodes.

Root `effects` are always active. Tier `effects` are active only for the owned tier — lower tiers do not stack. Multi-tier nodes must `replicate` prior effects.

### Attribute scores

On each skill level gained (natural XP commit), each entry’s `value` is added to that attribute’s growth bucket, multiplied by levels gained. Fill runs before player XP.

Known `id` values: `strength`, `perception`, `constitution`, `inconspicuity`, `resilience`. `value` must be finite and `> 0`. Duplicate ids last-win. Omitted or empty = no fill.

```json
"attributeScores": [
  { "id": "strength", "value": 0.5 },
  { "id": "resilience", "value": 0.2 }
]
```

---



## Node identity / unlock


| Field                          | Meaning                                                                                                                                         |
| ------------------------------ | ----------------------------------------------------------------------------------------------------------------------------------------------- |
| `id`                           | Stable node id (unique within the skill)                                                                                                        |
| `nameLang` / `descriptionLang` | Lang keys                                                                                                                                       |
| `descriptionParams`            | `{0}`, `{1}`… from effect params (`sum` / `format`)                                                                                             |
| `icon`                         | Texture path                                                                                                                                    |
| `cost`                         | Unlock points (global for normal skills; hobby spendable for hobbies; tier can override; default `1`)                                           |
| `minSkillLevel`                | Floor for first tier (tier can override; default `0`)                                                                                           |
| `specialization`               | `true` = spends a shared specialization slot (from `level-ups.json` `earn-specialization-point` rules; default 1 per 10 player levels, cross-skill); must be exactly 1 tier. Ignored (stripped) on hobby skills. |
| `tiers`                        | Ranks; later tiers usually `replicate` earlier effects                                                                                          |
| `layout`                       | Visual grid hints                                                                                                                               |
| `requires` / `excludes`        | Prerequisites and mutual exclusion                                                                                                              |


---



## Prerequisites & exclusivity


| Field      | Meaning                                          |
| ---------- | ------------------------------------------------ |
| `requires` | Groups: **OR inside**, **AND across**            |
| `excludes` | Mutual lock; one-sided is symmetrized at compile |


```json
"requires": ["a", "b"]                       // a AND b
"requires": [["coastalmaster", "riverspecialist"], "fisher"]  // (coastal OR river) AND fisher
"excludes": ["riverspecialist"]
```

Any owned tier of a prereq counts.

---



## Layout


| Field         | Values                     | Effect                                                                                  |
| ------------- | -------------------------- | --------------------------------------------------------------------------------------- |
| `layerOffset` | `1` (default)              | Next row under deepest parent                                                           |
|               | `0`                        | Same row as parent                                                                      |
| `columnBias`  | `"left"` / `"right"`       | Soft nudge among legal columns; omit = barycenter (downward) or prefer right (same-row) |
| `compact`     | `true` (default) / `false` | `false` = don’t pull this node upward in shake-out                                      |


Roots (`requires` empty) ignore `layerOffset` / `columnBias`.

```json
"layout": { "layerOffset": 0, "columnBias": "left", "compact": false }
```

Hard rules:

- Downward edge = child on parent layer + 1 → child column must be parent−1, parent, or parent+1.
- Max 3 downward children per parent.
- Same-layer children (`layerOffset: 0`) do not use those 3 slots.
- Edges cannot skip a row.
- Root rows can be banded by `minSkillLevel` when there are no parents.

---



## Tiers & replicate


| Field                                   | Meaning                                    |
| --------------------------------------- | ------------------------------------------ |
| `descriptionLang` / `descriptionParams` | Optional per-tier overrides                |
| `cost` / `minSkillLevel`                | Optional per-tier overrides                |
| `effects`                               | Rules active only while this tier is owned |


If `tiers` is omitted, the compiler creates one implicit tier with no effects.

### `replicate`

On tier 2+: `"replicate": 0` copies previous-tier effect index 0, then overlays present fields. `params` deep-merge by key. Cannot appear in root `effects` or on tier 1.

```json
{
  "minSkillLevel": 20,
  "effects": [
    { "replicate": 0, "params": { "base": 30, "cap": 30 } }
  ]
}
```

---



## Description params

Array of positional args for `descriptionLang` (`{0}`, `{1}`, …).

**Unlock nodes:** requires a `tiers` array; args bake at compile from the tier being described.

**Skill root (list hover):** optional on the skill object. Specs evaluate at hover from authored coefficients + ownership. Unowned node operands in a `sum` contribute `0`.


| Form                                                        | Meaning                                                                           |
| ----------------------------------------------------------- | --------------------------------------------------------------------------------- |
| `"0.base"`                                                  | Effect index 0, param `base` (current tier on nodes; root effects on skill hover) |
| `"root.0.perSkillLevel"`                                    | Skill root `effects`                                                              |
| `"prev.0.base"`                                             | Previous tier (nodes only; missing treated as 0 in sums)                          |
| `"axeexpert.0.perLevel"`                                    | Skill hover only: owned tier of node `axeexpert` (0 if unowned)                   |
| `{ "sum": ["0.base", "prev.0.base"], "format": "percent" }` | Sum selectors; `format` optional (`percent` / `fractionPercent`). A negative value prints as its magnitude in red (`-0.10` → `10%`), so the sentence can say "reduced by {0}" |


Bare `"base"` resolves across all current-tier effects (ambiguous if several share the key) — prefer scoped `index.param`.

### `totalParams`

Optional live total for the **owned** rank tooltip. Omitted or empty means no bracket. Node-level list; a tier may override. Not a pipeline fold — each entry evaluates that effect's number operand (`base + perSkillLevel × skill level`, including `cap` and `skill`) and appends `[X/Y/Z]`.

```json
"totalParams": [
  { "target": 0, "format": "fractionPercent" }
]
```

| Field | Meaning |
| --- | --- |
| `target` | Effect index on that tier (post-`replicate`), same as `0.value` |
| `format` | `percent` or `fractionPercent` (a set multiplier of `1.02` needs `fractionPercent` to print `102%`) |

Compile rejects a missing / out-of-range target, or a target that is not a `prosequor:number` spec. Chance-nested numbers and `ofBase` adds are not totals — `Evaluate` ignores `ofBase`, and chance params are not the rule's number spec.

---



## Effects


| Field                                | Role                                                                                                   |
| ------------------------------------ | ------------------------------------------------------------------------------------------------------ |
| `hook` / `verb` / `phase` / `action` | Surface → moment → facet (omit phase → `default`) → mutation. Action must fulfill the phase’s contract |
| `when`                               | Filter — `tags` AND                                                                                    |
| `params`                             | Action-specific                                                                                        |
| `priority`                           | Lower runs first, then registration order (default `0`)                                                |
| `replicate`                          | Tier overlay only                                                                                      |


Bare `hook` / `verb` / `action` ids normalize to `prosequor:`. Legacy `type` and `when.verb` are compile errors. `contract` is not authored.

```json
{
  "hook": "prosequor:block-interaction",
  "verb": "prosequor:mutate-drops",
  "phase": "quantity",
  "action": "prosequor:number",
  "when": { "tags": ["target:<crop>"] },
  "params": { "op": "add", "base": 5, "perSkillLevel": 1, "cap": 15 },
  "priority": 0
}
```

Matching rules on a hook+verb+phase all run. Nested gate children (`chance`, `has-unlock`) must resolve on the same hook+verb+phase.

### Ability `when`


| Field  | Meaning                                 |
| ------ | --------------------------------------- |
| `tags` | Every listed criterion must match (AND) |


Omit tags = no filter. Ability and XP share the same tags language. No `held` / `heldTags` / `targetCode` / `targetTags` / `when.verb`.

```json
{
  "hook": "prosequor:block-interaction",
  "verb": "prosequor:interaction-speed",
  "action": "prosequor:number",
  "when": { "tags": ["caller:<axe>", "target:<wood>"] },
  "params": { "op": "scale", "base": 0, "perSkillLevel": 0.001 }
}
```

Attribute-stat rules (`config/prosequor/stats/*.json`) use the same effect envelope plus optional `id`, `minScore`, `maxScore` (no `replicate`).

---



## Tags

1. Every listed criterion must match (AND). Case-insensitive. No OR across the list. Omit = no filter.
2. `<key>` looks up a collection (builtins + `collections.json` + pools). Unknown `<key>` fails compile. Comma lists inside one ref (`<a, b>`) match if the role identity is in any listed collection (OR inside that criterion only).
3. First colon splits a known role from the rest; otherwise the whole string is a target identity.



### Criterion forms


| Form                            | Meaning                                                          | Example                                                                                   |
| ------------------------------- | ---------------------------------------------------------------- | ----------------------------------------------------------------------------------------- |
| `role:<collection>`             | Role identity is in collection                                   | `caller:<axe>`, `target:<clay>`, `drop:<seed>`, `last-craft:<clothing>`, `input:<thread>` |
| `role:<a, b>`                   | Role identity is in any listed collection                        | `last-craft:<clothing, armor>`                                                            |
| `role:code`                     | Exact identity on that role                                      | `caller:game:torch`                                                                       |
| `caller:none` / `held:none`     | Role has no code (empty / missing value)                         | also `target:none`, `input:none`, …                                                       |
| `caller:@hand` / `caller:@grid` | Reserved callers (not asset codes; `@` = Prosequor reserved)     | deeds omit → `@hand`; craft grid uses `@grid`                                             |
| `mount:<collection>`            | Mount / boat entity in collection                                | `mount:<watercraft>`                                                                      |
| `<collection>`                  | Same as `target:<collection>`                                    | `<crop>`                                                                                  |
| `<a, b>`                        | Same as `target:<a, b>`                                          | `<clothing, armor>`                                                                       |
| `code`                          | Same as `target:code` (use `target:` when the code contains `:`) | `target:game:torch`                                                                       |
| `op:place`                      | Role `op` equals `place`                                         | clay-form                                                                                 |
| `damage:frost`                  | Damage kind present                                              | on-damage                                                                                 |
| bare token                      | Event token on the fact                                          | `interacting`, `moving`, `block-broken`, `crafted`, `crafting`                            |


Known roles: `caller` (authoring; `held` is a legacy alias → same field), `target`, `drop`, `last-craft`, `ground`, `mount`, `op`, `damage`, `input`.

- Bare tags with no `:` (and not `<collection>`) are **tokens** (open vocabulary).
- Namespaced codes (`game:torch`) without a role still parse as `target:game:torch`.
- `input` matches if any craft-grid ingredient matches; `input:none` matches an empty input list.
- `caller:none` / `held:none` matches when the role has no code (`Caller` null/blank). Deeds that mean empty hands store `@hand`, not blank.
- `caller:@hand` / `caller:@grid` / `caller:@trough` / `caller:@crop` / `caller:@loose` are identity-only (not collections). Unknown `@…` fails compile. `@` never means an asset code. Feed deeds use the last three so a blank caller does not collapse to `@hand`.
- `drop` is present only during mutate-drops per-stack `quantity` / `stack`.
- `none` is reserved (not a collectible code and not a collection id). Not valid on `op` / `damage`.
- Collections are not allowed on `op` / `damage`.
- Wildcards belong in `collections.json` `includes`, not in `when.tags`.



### Fact roles


| Role         | Content                                                                                                                                                                                                                                                                                                                                                                                                             |
| ------------ | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `caller`     | Instrument / source of the event (tool, kiln, mount, `@hand`, `@grid`, `@trough`, `@crop`, `@loose`, …). Effort ambient default = active hotbar. Legacy JSON `held:` still parses here                                                                                                                                                                                                                                                            |
| `target`     | The thing this event is about                                                                                                                                                                                                                                                                                                                                                                                       |
| `mount`      | Mount / boat entity when distinct from target                                                                                                                                                                                                                                                                                                                                                                       |
| `drop`       | Current stack code during mutate-drops quantity/stack                                                                                                                                                                                                                                                                                                                                                               |
| `last-craft` | Last craft-grid product code when remembered                                                                                                                                                                                                                                                                                                                                                                        |
| `ground`     | Underfoot / water when distinct from target                                                                                                                                                                                                                                                                                                                                                                         |
| `op`         | Voxel operation (`place`, `remove`, `finish`, …)                                                                                                                                                                                                                                                                                                                                                                    |
| `damage`     | Damage kinds (`frost`, `weather`)                                                                                                                                                                                                                                                                                                                                                                                   |
| `input`      | Craft-grid ingredient codes before consume. On `fed-animal`, the collectible that was eaten |
| tokens       | Effort: `interacting`, `mounted`, `riding`, `boating`, `helmsman`, `fishing`, `moving`, `sprinting`, `swimming`, `sneaking`, `temporal-drain`. Deed: `block-broken`, `harvested`, `grown`, `crafted`, `crafting` (clay-form), `fishing-catch`, `kiln-fired`, `saddle-break`, `saddle-tame`, `fed-animal`, `milked`, `aged-up`, `gave-birth`, `skep-harvest`, `skep-propagate`, `till-soil`, `butchered`, `cooking-pot` (meal-host firepit output only). Also `fire-pottery`, `barrel`, `used-bait`, `domesticated`, `undomesticated`, `friendly` (animal friendliness > 5) where emitted. Legacy aliases: `crop-grown` → `grown`, `trough-eaten` → `fed-animal` |


Builtin collection keys (also filled from world classifiers) share the same index as authored collections and pools: materials (`clay`, `peat`, `dirt`, `soil`, `wood`, `ore`, …), tools (`axe`, `shovel`, `pickaxe`, `saw`, `hammer`, `hoe`, `pan`, `fishingpole`, `bombs` = `BlockBomb`, plus aggregate `tool` / `weapon` / `metal-crafts` = tool∪weapon with a known metal path token), crops/plants, fish size/habitat/age, wearables (`clothing`, `armor`), craft goods (`thread`, `cloth`, `leather`, `hide`, `firewood`, `planks`), clay / smithing recipe outputs (`clay-formed`, `smithing-formed`), watercraft (`raft`, `sailboat`), bloomeries (`bloomery`), named unions (`wearable`, `watercraft`), and pool ids.

Crop **planter** pedigree (farming): `MakerUid` is the current crop's planter (Planted By / `domesticated` / Grown By). Care credits are once-per-role contributor weights on the same farmland BE: till `1`, plant `1`, fertilize `1`, water `1`. Soil Enrichment's absorb multiplier and the unpaid absorb remainder live on that BE box and **survive harvest**. Watering XP uses a crop-scoped moisture budget (`waterCredit`, cap `1` = one empty-to-full pour) that harvest clears with the care bag and a growth stage resets to `0`. `OnCropBlockBroken` clears maker, contributors, care-role flags, and that budget only. World-spawned / never-planted crops stay anonymous. Block info uses `OwnerCredit` with `prosequor:planted-by` (same muted italic style as item `Created By`).

**Fruit-tree harvest XP** (farming): interact harvest and ripe-part break emit `harvested` + `domesticated` when the **root** cutting BE has a planter (`RootOff` resolve — foliage is not stamped). Actor is the harvester (full XP); grafters are not paid. Quantity from fruit stacks. Wild / unstamped trees emit no `domesticated` and do not match the farming rule. Non-ripe fruit-tree breaks pay nothing.

**Crop growth XP** (farming): emit `grown` + `domesticated` with blank actor, `makerUid` = planter, and real contributor shares (`payee: contributors` on `crop-grown-farming`). Empty bag (legacy maker-only) synthesizes planter weight `1`. Shares are **not** wiped after growth — each stage pays the same bag. Forestry sapling growth still uses `payee: maker`.

**Fruit-tree structural growth XP** (farming): each successful `TryGrowTo` (new stem/branch/foliage block) is counted on the root; when `TryGrow` finishes, emit **one** `grown` + `domesticated` deed with `craftCount` = blocks in that batch (`payee: maker`). Flat rules pay once per batch; `pay: quantity` scales by block count. Amount `2` per block. Fruiting-calendar state changes (flowering/ripe/dormancy) do **not** emit growth XP.

**Fertilizer absorb XP** (farming): each slow-release fertility tick banks the percent that actually transferred (`updateSoilFertility`). When the bank reaches a whole percent and the care bag is non-empty, emit `fertilizer-absorbed` with `pay: quantity` = whole percents and `payee: contributors` (no planter synthesis). Farmland emits `target:<farmland>`. A planted bush does not keep absorbing through the soil it was planted in — the cutting inherits starting N/P/K once (`OnCreatedFromSoil`, pays nothing); later fertilizer drains on that bush's `BerryBushFarmland` store and emits `target:<berry-bush>` (the bush above). If that bush is gone, leftover drain has no target and pays nobody. Empty bag still drains, and still uses the surviving absorb multiplier, but pays nobody until the next care credit. Amount `0.01` per percent (~0.56 per compost). Faster Soil Enrichment absorb does not change the total.

**Watering XP** (farming): holding a watering can on farmland emits `interacting` (`caller:<watering-can>`, `target:<farmland>`, rate `0.01`) only when that pour raises `waterCredit`. Gains at or below `0.01` moisture (full tile, rain-filled, token splash) and pours past the cap pay nothing and do not stamp the water care credit. Neighbor splash and rain are not measured. Field Expertise extras update those tiles' budgets and care credits only — no extra effort stamp. A successful crop stage sets `waterCredit` back to `0`.

**Till-soil XP** (farming): each soil→farmland conversion emits `till-soil` (flat `0.1`) to the tiller. Field Expertise area till emits once per converted tile (skipped cells pay nothing).

Crop **climate window** (farming skill root effect): at `TryPlant`, run **block-interaction** / `plant-crop` / `adjust-plant-climate-value` (NumberSpec expand fraction, seed 0). Stamp `prosequorClimateHalfDelta` = `δ = (Heat−Cold) × fraction / 2`. A transpiler on private `updateCropDamage` adjusts threshold reads to `Cold−δ` / `Heat+δ` (symmetric expand; plant-time snapshot). Does **not** change the global growth-pause curve or greenhouse `Temperature += 5`. Cleared with the crop.

Berry / fruiting-bush **planter** pedigree: `Block.DoPlaceBlock` stamps the planting player's `MakerUid` onto the **bush** BE at the plant position. Harvest `ExchangeBlock` keeps that BE (multi-harvest). Cutting maturity (`BEBehaviorFruitingBushCutting.OnMatureTick`) `SetBlock`s a new bush BE then calls `OnGrownFromCutting` on it — MakerUid is carried across that replace. `BlockEntity.OnBlockRemoved` clears it. World-spawned bushes stay anonymous. Block info uses the same `OwnerCredit` / `planted-by` path. Fruit-tree cuttings stamp planter on the root cutting BE only; harvest resolves it via `RootOff` (foliage is not stamped; grafters are not paid).

Ownership credit chrome (`OwnerCredit`): resolve owner UID → display name, localize with a caller-supplied lang key (`created-by`, `planted-by`, `grown-by`, …), wrap in muted italic VTML. Item tooltips use `AppendForStack` (reads `prosequorCreditLang` when set); block info uses `Append`.

Liquid **vessels** (jugs, bowls, buckets, barrels): vanilla only prints `{0} litres of {1}` from the collectible code. `LiquidContentChrome` appends the **portion** affixes, `Quality:` footer, and Created By under that litres line on held `GetContentInfo` and placed `GetPlacedBlockInfo`. The vessel’s own pottery chrome stays on the vessel stack.

Craft-grid maker stamp: `FoundMatch` / `OnCreatedByCrafting` (`ApplyAttributes`) call `StampCraftMaker` so synthesis outputs carry `MakerUid` for `Created By` (skips stacks that already have a maker, e.g. repairs). Clay-form already stamps via `ClayFormCraftAttribution`.

Harvest **Grown By**: crop/berry drops inherit the **planter** (farmland / bush pedigree), not the harvester — `OwnerCredit.StampGrownBy` on `GetDrops`, interact rolls, and fruiting-bush `GetRipeDrops`. Tooltip lang is `prosequor:grown-by`.

---



## Contracts

A contract is the kind of value a phase folds. It is implied by the phase; it is not written in JSON.


| Contract | Meaning                                                                                                                               |
| -------- | ------------------------------------------------------------------------------------------------------------------------------------- |
| `number` | Scalar fold (multiplier, chance, rate, count, size, flag, …)                                                                          |
| `bool`   | Allow / deny fold (true when the pipeline resolves ≥ 1 / truthy)                                                                      |
| `stack`  | Mutate one drop/output stack                                                                                                          |
| `stacks` | Append / replace / filter a list of stacks                                                                                            |
| `refund` | Post-consume craft-grid restock — needs the **pre-consume ingredient snapshot** plus the live grid; not interchangeable with `stacks` |
| *(none)* | Side-effect only — no fold value authors care about; rules fire for effects on the context                                            |


An action may bind only to phases whose contract it fulfills (or none ↔ none). Gates (`chance`, `has-unlock`) keep the outer phase’s contract. `bool` phases are not number surfaces — `prosequor:number` does not bind there.

---



## Hooks

Bare ids normalize to `prosequor:`. Six surfaces. Phases are per `(hook, verb)`.

### `prosequor:block-interaction`


| Verb                 | Phase      | Contract |
| -------------------- | ---------- | -------- |
| `mutate-drops`       | `quantity` | number   |
|                      | `stack`    | stack    |
|                      | `stacks`   | stacks   |
| `mutate-process`     | `quantity` | number   |
|                      | `stacks`   | stacks   |
| `interaction-speed`  | `default`  | number   |
| `plant-sapling`      | `default`  | number   |
|                      | `growth`   | number   |
| `plant-bush-cutting` | `default`  | number   |
|                      | `growth`   | number   |
| `plant-crop`         | `default`  | number   |
| `establish-cutting`  | `default`  | number   |
| `seek-bobber`        | `default`  | number   |
| `fertilize`          | `default`  | number   |
| `field-work`         | `size`     | number   |
| `scythe-multibreak`  | `quantity` | number   |
| `trough-fill`        | `quantity` | number   |
| `spawn-bees-chance`  | `default`  | number   |
| `harvest-skep`       | `allow-right-click-harvest` | bool |
| `harvest-skep`       | `right-click-harvest-break-chance` | number |
| `harvest-bloomery`   | `allow-right-click-harvest` | bool |
| `harvest-bloomery`   | `right-click-harvest-break-chance` | number |


`mutate-drops`: block break drops. Fact `target` = block; `caller` = hotbar; during per-stack loop `drop` = current stack.

`mutate-process`: kiln / barrel / fruit-press output. Tokens `fire-pottery` / `barrel` / `fruit-press`. Process skills resolve from a **process starter** uid (online or parked progress via `GetProgress(api, uid)`), not only a live `IPlayer`.

**Process starter** pedigree (firepit cook/smelt, barrel seal, fruit press fill, **boiler pour**): firepit, fruit press, and still stamp exactly one **contributor** on the host BE when the process begins — never increment the bag. Firepit: note last interactor, stamp on `canSmeltInput && IsBurning` rising edge. **Barrel** uses a contributor bag instead of a sole share. A successful pour into an unsealed barrel (right-click or GUI insert) adds that player at weight 1 and does not increment on a top-up. Seal (packet 1337) adds the closer the same way and stamps them as **MakerUid** on the liquid already in the barrel. At craft completion (sealed→unsealed) vanilla mints a fresh stack; that stack does **not** inherit the juice pedigree. If a sealer was recorded, `ApplyAttributes` stamps them as MakerUid and rolls fermented quality (quality-rank / grade, only if the roll writes one). If nobody sealed, the new liquid is anonymous. The `crafted` deed publishes the bag and `craft-fermented` pays `payee: contributors` (`0.5` per fermented unit, split by weight). A later tick after unseal does not pay again. The bag is cleared after the emit. Fruit press: stamp on mash fill; minted juice gets that uid as **MakerUid** before `TryPutLiquid` (liquid pedigree merge handles mixed makers). Each accepted transfer emits `crafted` for the new units only, and `craft-juice` pays `0.02` per unit. **Still / boiler:** stamp on successful liquid pour (content stack size increases); top-ups restamp the pourer. Distill quality at the condenser prefers the adjacent boiler’s sole contributor, then falls back to mash `MakerUid`. That uid is the spirit’s maker. Each distill tick emits `crafted` for the **new** spirit units only (not the bucket total) so `craft-distilled` pays quantity once per drop. **Batch quality:** the first distill tick after a pour rolls apply-quality once (mash rank spent as quality-base) and stores the result on the boiler (`prosequorDistillBatch`). Later ticks copy that blob onto each spirit portion — they do not reroll. Pouring more liquid dilutes the mash as a normal liquid merge and clears the batch, so the next distill rolls again. Relighting without adding liquid does not reroll. Maker (if any) on the block stays who crafted/placed it; sole contributor is who started the firepit / filled the press / loaded the still. Mid-process pokes do not restamp; the next rising edge / fill / pour replaces that sole share.

**Cook quality:** at firepit `smeltItems` / oven bake completion, the process starter’s progress runs `crafting-interaction` apply-quality onto the finished stack (cooked pot, `SmeltedStack`, `CooksInto` product, oven result) via `CraftMutateOutputStation.ApplyAttributes(world, uid, stack)`, then emits a `crafted` deed for that product so cooking XP rules pay. A meal-host output also carries `cooking-pot` (`<claypot>` is excluded from `<meal>`, so the pot code alone does not match the meal rule). Leftover dirty pots are emitted without that token and pay nothing; they still roll quality via `target:<claypot>`. The meal deed publishes both channels: `quantity` is meals produced (`quantityServings`, else output stack size) and `ingredients` is cooking-slot units snapshotted before `smeltItems` (stack sizes summed; the pot itself is not an ingredient). A raw smelt with no cooking slots publishes the input stack size as `ingredients` and the output stack size as `quantity`. The meal rule and the `cooking-pot` rule both pay `ingredients` as an amount table lerped across 1–40. Oven bakes publish output stack size as `quantity` only. On a meal vessel (`BlockCookedContainer`, `BlockMeal`, `BlockCrock`) the maker is the potter and the cook is a contributor (weight 1, not incremented if already present). The same person can be both, and both tooltip lines still show. Other cook products still take the cooker as maker immediately. Adding a meal (`ServeInto` / `ServeIntoStack` / `ServeIntoBowl`, including a crock) copies quality and that cook onto the destination and keeps the destination potter. Removing the meal — serve until empty, last bite, rot, or a wash in water — keeps the potter and drops the cook, quality, and meal mods. Held meal tooltips put `Prepared By` under the serving line and before Nutrition Facts. Created By stays the footer.

**Meal eat mods:** quality keys `satiety` (Very Filling) and `hungerDelay` (Nothing Wasted) are storage-only on the meal. During `BlockMeal.Consume`, `MealEatScope` exposes the stack; `OnEntityReceiveSaturation` multiplies saturation by `satiety` and divides `nutritionGainMultiplier` by the same factor (nutrition unchanged), and multiplies `saturationLossDelay` by `hungerDelay`. Player Constitution `prosequor:hunger-delay` still applies afterward on delay.

`interaction-speed`: mining / block interact speed. Fact `caller` + `target`.

Plant / seek verbs: `default` = success chance (0–1) for sapling / bush-cutting / establish-cutting / seek-bobber. `growth` = growth-duration multiplier (`plant-sapling` / `plant-bush-cutting` only). `plant-crop` / `default` = climate-window expand fraction (0 = unchanged). Seek-bobber facts may include token `used-bait` when the baited seek rate is queried (absent for baitless).

`spawn-bees-chance`: seed = vanilla skep `beemobSpawnChance` (default 0.4). Calm Hives scales it down. Skep break only (not wild beehives).

`harvest-skep`: Apiary Master — `allow-right-click-harvest` enables **sneak + right-click** honey extract on a harvestable skep (plain right-click stays vanilla pickup); `right-click-harvest-break-chance` is the chance that extract breaks the skep instead (seed 0; content sets absolute).

`harvest-bloomery`: Bloom Brigand — `allow-right-click-harvest` enables **sneak + empty-hand right-click** extract on a finished bloomery (`OutSlot` filled, not burning). `right-click-harvest-break-chance` is the chance the bloom shatters instead of being taken (seed 0). Successful extract and breaking a finished bloomery both emit `bloomery-harvest` XP.

### `prosequor:item-interaction`


| Verb                | Phase                | Contract |
| ------------------- | -------------------- | -------- |
| `mutate-drops`      | `quantity`           | number   |
|                     | `stack`              | stack    |
|                     | `stacks`             | stacks   |
| `interaction-speed` | `default`            | number   |
| `block-damaged`     | `amount`             | number   |
| `item-damage`       | `amount`             | number   |
| `craft-damaged`     | `amount`             | number   |
| `consume-bait`      | `restock`            | *(none)* |
| `repair`            | `add-durability`     | number   |
| `clay-form`         | `assist-radius`      | number   |
|                     | `auto-finish`        | number   |
|                     | `place-conservation` | number   |
| `anvil-heavy-hit`   | `slag-radius`        | number   |
|                     | `assist-radius`      | number   |
|                     | `move-count`         | number   |
| `anvil-split`       | `bits-refund`        | number   |
| `anvil-strike`      | `decay-shrink`       | number   |
| `voxel-copy`        | `default`            | number   |
| `voxel-refill`      | `default`            | number   |


Durability verbs: `caller` = damaged collectible; `target` = broken block when known; `last-craft` when remembered. `block-damaged` = while breaking a block; `item-damage` = other loss; `craft-damaged` = craft-grid tool ingredient.

`consume-bait` / `restock`: after a catch clears bobber bait. Side-effect actions queue bait onto the context; no number fold for authors.

`mutate-drops`: tool process drops (panning, fishing catch). `caller` = tool; `target` = material or catch.

`clay-form`: match with `op:place` / `op:remove` / `op:finish`.

`anvil-heavy-hit`: tool-mode 0 (heavy hit) on an anvil work item. `assist-radius` + `move-count` run before vanilla flatten: of misplaced metal in the fixed 3×3 hit brush (strike Y), place up to N into empty recipe cells within Chebyshev `assist-radius` of the strike, only when `dest.Y < source.Y` (prefer nearer dests). Vanilla `OnHit` then moves leftovers. `slag-radius` (seed −1) clears slag in a Chebyshev ball after the hit.

`anvil-split`: tool-mode split on metal voxels. `bits-refund` (seed 0 = off) is the Metal Recovery interval: each metal split increments pedigree `anvilSplits` on the work item; when the count reaches the threshold, grant one matching `game:metalbit-*` and reset the counter to 0.

`anvil-strike`: hammer hit / upset / split on anvil work. `decay-shrink` (seed 0 = off) shrinks pending cooling debt by advancing `temperatureLastUpdate` by `pct × (now − lastUpdate)` (clamped to now). Ranks 2% / 5%.

**Bits Forging** (`bits-forging` unlock): heated `metalbit-*` become anvil-workable. Placement is aim-aware onto **existing** work only — click a solid metal voxel; new metal is placed in empty cells with metal directly below (`Y ≥ 1`, never floating / never Y=0). Unlock is gated in C# (not a number fold). ~2 voxels per bit (`ItemIngot.VoxelCount / 21`).

### `prosequor:crafting-interaction`


| Verb            | Phase             | Contract |
| --------------- | ----------------- | -------- |
| `mutate-output` | `quantity`        | number   |
|                 | `attributes`      | stack    |
|                 | `output`          | stack    |
|                 | `refund`          | refund   |
| `apply-quality` | `quality-base`    | number   |
|                 | `quality-window`  | number   |
|                 | `quality-rolls`   | number   |
|                 | `quality-bonus`   | number   |
|                 | `attributes`      | stack    |


Fact `target` = output code. Typical flow: `quantity` (count) → `attributes` (create-time stack mods) → take; then `refund` after ingredient consume using the pre-craft grid snapshot (`input:<collection>`). `output` is reserved for take-time stack extras (no shipped actions yet). `refund` is **not** `stacks` — actions must see both the before-list and the live grid.

`apply-quality` runs after `mutate-output` / `attributes` on **synthesis** crafts (server create-time only; no client preview), and on **cook-complete** via `CraftMutateOutputStation.ApplyAttributes(world, cookerUid, stack)`. Number phases are knobs folded with `prosequor:number` (compose-memo eligible). Seeds: `quality-base` = −200 + owning skill level; `quality-window` = 200; `quality-rolls` = 1; `quality-bonus` = 0. The stack action `prosequor:quality` rolls per matching rule (not memoized) so crafts stay variable. Craft attribute key `freshness` scales perish `freshHours` (Clean Cook); `satiety` / `hungerDelay` store eat-time factors (Very Filling / Nothing Wasted). Factors may live on cooked pots until serve rematerializes hours on the bowl.

### `prosequor:entity-interaction`

Fact `target` = animal / mount / boat code.


| Verb           | Phase             | Contract |
| -------------- | ----------------- | -------- |
| `mounted`      | `move-speed`      | number   |
|                | `turn-speed`      | number   |
|                | `saddle-break`    | number   |
|                | `hunger-rate`     | number   |
|                | `fall-damage`     | number   |
|                | `melee-damage`    | number   |
|                | `ratline-stamina` | number   |
|                | `can-ride`        | bool     |
| `animal-flee`  | `chance`          | number   |
|                | `multiplier`      | number   |
|                | `response`        | number   |
| `animal-seek`  | `chance`          | number   |
|                | `multiplier`      | number   |
|                | `response`        | number   |
| `animal-melee` | `chance`          | number   |
|                | `multiplier`      | number   |
| `animal-brood` | `chance`          | number   |
|                | `multiplier`      | number   |
| `animal-milk`  | `chance`          | number   |
|                | `multiplier`      | number   |
| `animal-pet`   | `default`         | bool     |
| `trough-eaten` | `chance`          | number   |


`mounted`: land rideables + boats. Filter with `target:<raft>` / `sailboat`; boats also use `ground` water and tokens `moving` / `helmsman`. `turn-speed` and `ratline-stamina` are boat-only. `can-ride` is allow/deny (bridle-without-saddle).

`animal-pet` / `default`: allow/deny pet. `allow-animal-pet` sets allow when score &gt; `minFriendliness`. `chance` + nested `add-friendliness` may raise the friendliness side channel without changing the allow fold.

`trough-fill` / `quantity`: portions placed per successful player fill (seed 1). Feedhand sets 2 / 3.

`trough-eaten` / `chance`: probability a meal raises friendliness (seed 0.05). Evaluated with the **contributor** uid’s progress (online or parked), not the animal. Feedhand adds +5% (total 10% when unlocked).

### `prosequor:player-interaction`


| Verb                                                                                                                                                                                                                                                              | Phase        | Contract |
| ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | ------------ | -------- |
| `health` / `satiety` / `hunger-delay` / `armor-walk` / `melee-damage` / `basic-slots` / `ranged-speed` / `ranged-acc` / `fall-damage-factor` / `fall-damage-threshold` / `temporal-recover-rate` / `temporal-drain-rate` / `animal-seeking-range` / `crit-chance` / `whole-vessel-loot-chance` | `default`    | number   |
| `sprint-speed` / `swim-speed` / `sneak-speed`                                                                                                                                                                                                                     | `default`    | number   |
| `cat-eyes`                                                                                                                                                                                                                                                        | `default`    | number   |
| `on-damage`                                                                                                                                                                                                                                                       | `amount`     | number   |
|                                                                                                                                                                                                                                                                   | `last-stand` | number   |


`on-damage`: match `damage:frost` / `damage:weather` on `amount`. `last-stand` folds cooldown hours (seed 0); the station applies fatal-blow survival when hours > 0 and ready.

`sprint-speed` / `swim-speed` / `sneak-speed`: float folds, seed 0 (bonus fraction). Station multiplies on-foot `GetWalkSpeedMultiplier` by `1 + bonus`. Liquid (`Swimming` or feet-in-liquid) uses swim only. Sprint and sneak are exclusive; mounted players are unchanged. `prosequor:number` is also registered on `temporal-recover-rate` / `temporal-drain-rate` (int percent, seed 0). Adaptation authors those; Resilience no longer maps them.

`whole-vessel-loot-chance`: int percent written onto vanilla `wholeVesselLootChance` (FlatSum additive; vanilla rolls `GetBlended - 1`). Inconspicuity is 0% through 10, then 3% per point (14 → 12%, 18 → 24%).

### `prosequor:progress`


| Verb           | Phase    | Contract |
| -------------- | -------- | -------- |
| `skill-xp`     | `amount` | number   |
| `skill-bucket` | `cap`    | number   |


`skill-xp`: after an xpRule wins, before commit. Own-skill only (number binders skip other skills).
`skill-bucket`: saturation bucket capacity multiplier (seed `1`). Own-skill only.

---



## Actions

Same action id can bind to several hook+verb+phases. Nested `chance` / `has-unlock` children must be valid for the same hook+verb+phase.

### Int range params

`rolls` / `quantity`: omit → default (usually `1`); number / `"2"` → fixed; `"2-5"` → inclusive `min..=max` (both ends ≥ 1).

### NumberSpec

Used by `prosequor:number` and nested chance/freshness operands.


| Param    |                                                                                         |
| -------- | --------------------------------------------------------------------------------------- |
| `op`     | `add` | `scale` | `set`                                                                 |
| Operand  | literal `value` **or** skill-scaled `base` / `perSkillLevel` / `cap` (optional `skill`) |
| `ofBase` | add only: multiply addend by frozen context base                                        |


Skill-scaled operand: `min(cap, base + perSkillLevel × skillLevel)`. Same units as literal `value`. `cap: 0` = uncapped. Scale: `value * (1 + amount)`.

### Contract: number



#### `prosequor:number`

NumberSpec params. Surfaces: mutate-drops/`quantity` (block + item + entity); mutate-process/`quantity`; mutate-output/`quantity`; apply-quality/`quality-base`|`quality-window`|`quality-rolls`|`quality-bonus`; interaction-speed/`default` (block + item); mounted phases except `can-ride`; repair/`add-durability`; clay-form `auto-finish` / `place-conservation` / `assist-radius`; anvil-heavy-hit `slag-radius` / `assist-radius` / `move-count`; anvil-split `bits-refund`; anvil-strike `decay-shrink`; fertilize/`default`; plant/seek `default` (success chance); plant/`growth`; animal `chance` / `multiplier`; trough-eaten/`chance`; trough-fill/`quantity`; spawn-bees-chance/`default`; harvest-skep/`right-click-harvest-break-chance`; field-work/`size`; scythe-multibreak/`quantity`; voxel-copy|refill/`default`; block-damaged / item-damage / craft-damaged / `amount`; progress / skill-xp/`amount`; progress / skill-bucket/`cap`; player-interaction `temporal-recover-rate` / `temporal-drain-rate` (int, rounded) and `sprint-speed` / `swim-speed` / `sneak-speed` (float).

Skill-xp / skill-bucket binders apply only when the recipient skill matches the rule’s owning skill.

#### `prosequor:adjust-plant-climate-value`

NumberSpec params (same shape as `prosequor:number`). **block-interaction** / `plant-crop` / `default`. Fold seed 0; result = climate-window expand fraction. Station stamps `δ = span × fraction / 2` on farmland at plant time.

#### `prosequor:add-mapped-number`


| Param                                             |                                                          |
| ------------------------------------------------- | -------------------------------------------------------- |
| `fromScore` / `fromValue` / `toScore` / `toValue` | Linear map endpoints                                     |
| `midScore` / `midValue`                           | Optional hinge                                           |
| `op`                                              | `add` (default) or `scale` (`value * mapped`)            |
| `round`                                           | Optional `ceil` | `floor` | `round`; omit for fractional |


Surfaces: player-interaction stat verbs/`default`; cat-eyes/`default`; on-damage/`amount` and `last-stand`; animal-flee|seek/`response`.

### Contract: bool

Allow/deny folds. Stations treat resolved value ≥ 1 as true. Not a number surface.

#### `prosequor:allow-mounted-ride-without-saddle`

No params. **entity-interaction** / `mounted` / `can-ride`. Sets allow when the mount has a bridle but no saddle (does not grant riding in general).

#### `prosequor:allow-animal-pet`

Optional `minFriendliness` (≥0; default 5). **entity-interaction** / `animal-pet` / `default`. Sets allow when friendliness > threshold.

#### `prosequor:set-true`

No params. **block-interaction** / `harvest-skep` or `harvest-bloomery` / `allow-right-click-harvest`. Sets allow to true (1).

#### `prosequor:add-friendliness`

`base` (≥0). Adds to friendliness gain on animal-pet/`default` (side channel; allow fold unchanged).

### No contract (side effects)

These do not fold a number/stack for authors. They run on a specific verb/phase and write side channels (or inventory).

#### `prosequor:restore-consumed-bait`

No params. **item-interaction** / `consume-bait` / `restock`. Clones the just-consumed bait onto the restock side channel (free restore).

#### `prosequor:restock-last-bait`

Optional `amount` (≥1; default 1). **item-interaction** / `consume-bait` / `restock`. Takes matching bait from inventory onto the restock side channel.

### Contract: refund

Post-consume craft restock. Distinct from `stacks`: the phase carries a **pre-consume ingredient snapshot** plus the live grid. `refund-ingredients` cannot bind on mutate-drops/`stacks` (or any other stacks surface).

#### `prosequor:refund-ingredients`


| Param    |                                         |
| -------- | --------------------------------------- |
| `match`  | Collection id of ingredients to restock |
| `amount` | Max units to refund (≥ 0)               |
| `retain` | Min units that must stay consumed (≥ 0) |


`toReturn = min(amount, max(0, consumed - retain))`. Surface: **crafting-interaction** / `mutate-output` / `refund`. Gate with `input:<collection>` when needed.

### Contract: stack



#### `prosequor:increase-freshness`

NumberSpec. Surfaces: mutate-drops/`stack` (block + item). Prefer craft attribute key `freshness` via `prosequor:quality` for cook meals (Clean Cook).

#### `prosequor:upgrade-ore-grade`

No params. Surface: mutate-drops/`stack` (block).

#### `prosequor:replace-matching-stack-with-block`

No params. Surface: mutate-drops/`stack` (block).

#### `prosequor:modify-attribute`

`key` + NumberSpec. Surface: mutate-output/`attributes`.

#### `prosequor:add-affix`

`{ "code", "lang", "color"? }` or `{ "list", "item" }`. Surface: mutate-output/`attributes`.

#### `prosequor:quality`

Craft quality roll on **apply-quality** / `attributes`. Required: `key`, `op` (`add` / `scale` / `set`, same units as NumberSpec), `table` (non-empty float array lerped over ranks 0–30). Optional: `affixes` (affix list id; index chosen at roll time — omit to skip the bonus affix; the `Quality:` grade still stamps from the roll); `bonus` (per-rule addend, may be negative; default 0); `affixRange` (`[from, to]` inclusive indices into that list — omit for the full list; requires `affixes`).

Per matching rule:

1. Fold knobs (`quality-base` / `quality-window` / `quality-rolls` / `quality-bonus`) via nested pipeline runs (memoized).
2. Sample `max(1, round(quality-rolls))` times in `[base, base + window]`; take the max. Degenerate window ≤ 0 → always `base`.
3. `points = floor(clamp(raw + ruleBonus + globalBonus, 0, 300) / 10)`. If `points ≤ 0`, skip (no attribute, no affix).
4. Piecewise-lerp `table` at `points` on `[0, 30]` (any knot count ≥ 1; rank 30 = last knot); apply with `op` through craft attribute mutators.
5. If `affixes` is set: band `points` onto the rule affix list slice (`affixRange`, or the full list) with nearest index (affix lists stay discrete; attribute tables lerp); stamp via `ItemAffixes.Add`. Example: five liquors House→Crown, R1 `"affixRange": [0, 3]`, R2 `[1, 4]` so Crown is R2-only. Omit `affixes` when the roll should only apply the attribute and the leading quality grade.
6. Also band **mean** (`clamp(raw + bonuses, 0, 300)`) onto `prosequor:quality` via `mean / 300` → nearest index. Stamp with `ItemAffixes.SetFront` under code `quality` (one leading grade; upgraded to the best mean across matching rules this craft). Grade names use rarity colors baked into the lang strings. Collection `<distilled>` never receives this grade. At the condenser, mash `qualityRank` is added to distilled `quality-base`, then both the grade and rank are stripped from the spirit (intoxication / value affixes stay).

Independent rolls per quality rule. Knob folds memo; the roll itself does not. Rank is **not** written here — use `prosequor:quality-rank`.

```json
{
  "hook": "prosequor:crafting-interaction",
  "verb": "prosequor:apply-quality",
  "phase": "attributes",
  "action": "prosequor:quality",
  "when": { "tags": ["target:<armor>"] },
  "params": {
    "key": "protection",
    "op": "scale",
    "table": [0, 0.1],
    "affixes": "prosequor:durability",
    "affixRange": [0, 2]
  }
}
```

#### `prosequor:quality-rank`

Same roll and grade footer as `prosequor:quality`, but the table value is the readable rank — no `op`, no bonus affix list. Required: `key` (`qualityRank` or `rank`), `table` (lerped over ranks 0–30). Optional: `bonus`.

`points` as above; lerp `table` and stamp the rounded value onto pedigree `qualityRank` (best mean this craft). Rank joins `ContentHash`; liquid merges average it by litres. 0 / omitted = unset. Read via `ProsequorStackPedigree.TryGetQualityRank`. Distilling adds the mash rank to `quality-base` (simple add), then strips rank from the spirit.

```json
{
  "hook": "prosequor:crafting-interaction",
  "verb": "prosequor:apply-quality",
  "phase": "attributes",
  "action": "prosequor:quality-rank",
  "when": { "tags": ["target:<meal, fermented>"] },
  "params": {
    "key": "qualityRank",
    "table": [0, 5]
  }
}
```

### Contract: stacks



#### `prosequor:append-from-drop-table`

Optional `table` (pool id); optional `rolls` / `quantity` int-ranges. Surfaces: mutate-drops/`stacks` (block + item). Omit `table` → ambient drop table.

#### `prosequor:replace-from-drop-table`

Same as append + optional `rule` (`first`  `last`  `random`, default `first`).

#### `prosequor:replace-with-variant`

`variant`. Surface: mutate-process/`stacks`.

#### `prosequor:enrich-soil`

`maxFertility` (≥0). Surface: mutate-drops/`stacks` (block).

#### `prosequor:add-crop-seed`

No params. Surface: mutate-drops/`stacks` (block).

### Gates

Preserve the outer phase’s contract. Nested `onSuccess` / `onFailure` must bind on the same surface.

#### `prosequor:chance`


| Param                     |                                                                            |
| ------------------------- | -------------------------------------------------------------------------- |
| `chance`                  | NumberSpec add-only (unit probability), or `{ "percent": N }` / bare 0–100 |
| `onSuccess` / `onFailure` | Nested `{ "action", "params" }`                                            |


Surfaces: mutate-drops quantity/stack/stacks (block + item); durability `amount`; mutate-process/`stacks`; consume-bait/`restock` (gate side-effect restock actions).

```json
{
  "action": "prosequor:chance",
  "params": {
    "chance": { "base": 0.05, "perSkillLevel": 0.01, "cap": 0.15 },
    "onSuccess": { "action": "prosequor:number", "params": { "op": "set", "value": 0 } }
  }
}
```



#### `prosequor:has-unlock`


| Param                                                           | Surfaces                                   |
| --------------------------------------------------------------- | ------------------------------------------ |
| `unlock` (node id), optional `skill`, `onSuccess` / `onFailure` | mutate-drops quantity/stack/stacks (block) |


---



## XP rules

Set exactly one of `amount` (discrete) or `rate` (per game-second). No `skill` field — ownership is the enclosing skill (or contribution `skill`).

```json
{
  "id": "prosequor:mine-mining",
  "amount": [1, 5, 10],
  "pay": "resistance",
  "when": {
    "activity": "prosequor:deed",
    "tags": ["block-broken", "target:<stone,ore,gemstone>", "caller:<pickaxe,bombs>"]
  }
}
```

```json
{
  "id": "prosequor:craft-clothing-tailoring",
  "amount": [10, 30, 50],
  "pay": "ingredients",
  "when": {
    "activity": "prosequor:deed",
    "tags": ["crafted", "caller:@grid", "target:<clothing>"]
  }
}
```

`amount` is the magnitude (scalar or band table). `pay` names which deed emit **channel** the grant reads. Omitted `pay` is `flat`: when the deed fires, pay `amount` once.


| `pay`            | Meaning                                                                                                              | Missing channel |
| ---------------- | -------------------------------------------------------------------------------------------------------------------- | --------------- |
| `flat` (default) | Pay `amount` once.                                                                                                   | —               |
| `resistance`     | Lerped amount table against block Resistance (dig / mine / chop catalog)                                             | `amount[0]`     |
| `voxels`         | Lerped amount table against clay voxels-per-unit (`clay-voxels` catalog)                                             | `amount[0]`     |
| `ingredients`    | Lerped amount table against recipe ingredient units (fixed range **1–40**, clamp above)                              | `amount[0]`     |
| `quantity`       | Multiply by countable output / drops (per-unit list when emitted; else craftCount fallback). `exclude` filters units | `× 1`           |


`pay` is a single channel string. Closed set — unknown keys and arrays reject. Rate rules reject `pay`. Amount table with only `flat` (or no measure channel) compiles with a warn (always uses `amount[0]`).

`exclude` (string array, default `[]`) omits matching item codes / `<collections>` from the **quantity** channel. Requires `pay` `quantity`. Exact codes or `<id>` / `<a, b>` refs (same collection language as tags). When the emit publishes per-unit quantity parts, matching units are dropped from the count; otherwise the whole quantity is gated on the deed `target` (excluded target → quantity `0` → no grant).

```json
{
  "id": "prosequor:harvest-crop",
  "amount": 1,
  "pay": "quantity",
  "exclude": ["<stick>", "game:drygrass"],
  "when": {
    "activity": "prosequor:deed",
    "tags": ["harvested", "target:<crop>"]
  }
}
```

Only the named channel applies. Selected-but-empty is a content bug, not an engine crash.

`payee` names **who** receives the grant (amount rules only; rate rejects it). Omitted `payee` is `user`. Missing role data never falls back to another role.


| `payee`          | Who gets paid                                                                                   |
| ---------------- | ----------------------------------------------------------------------------------------------- |
| `user` (default) | Emit actor. Blank actor → no pay.                                                               |
| `maker`          | Emit `makerUid`. Missing → no pay.                                                              |
| `contributor`    | Emit **selected** contributor uid (`selectedContributorUid`). Missing → no pay.                 |
| `contributors`   | Weighted shares: each gets `grant × (ownWeight / sumWeights)`. Empty / degenerate sum → no pay. |


```json
{ "amount": 5 }
{ "amount": 0.001, "payee": "contributors", "when": { "tags": ["grown", "domesticated", "target:<crop>"] } }
{ "amount": 0.1, "payee": "contributor", "when": { "tags": ["fed-animal", "friendly"] } }
{ "amount": 1, "when": { "tags": ["milked", "friendly"] } }
{ "amount": 5, "payee": "contributors", "when": { "tags": ["aged-up", "friendly"] } }
{ "amount": 5, "payee": "contributors", "when": { "tags": ["gave-birth", "friendly"] } }
{ "amount": 0.2, "pay": "quantity", "payee": "contributors", "when": { "tags": ["skep-harvest"] } }
{ "amount": 1, "payee": "contributors", "when": { "tags": ["skep-propagate"] } }
{ "amount": [1, 5, 10], "pay": "voxels", "payee": "contributors", "when": { "tags": ["kiln-fired"] } }
```

Maker is **not** auto-merged into contributor resolution (pedigree keeps maker separate). Emit bag fields: `actor` / `playerUid`, `makerUid`, `selectedContributorUid`, `contributors` `(uid, weight)`. Weights must be `> 0`. Passive triggers may omit the actor when paying `maker` / `contributor` / `contributors`.

**Pedigree contributors** (`ProsequorBlob`) are a `uid → weight` share map. `AddContributor(uid, amount=1)` increments weight. Order / `LastContributorUid` are not features. Legacy ordered UID lists migrate to weight `1` each. ContentHash includes maker + sorted `(uid, weight)`. Optional payload fields (`recipe`, `friendlinessReadyAt`, `anvilSplits`) are excluded from ContentHash.

Trough **feed contributions** (husbandry): player fill increments a BE-side weighted bag (`uid → portions`). Feedhand may deposit extra fill levels in the same action (still costing feed) via `trough-fill` / `quantity`. `ConsumeOnePortion` takes one share (weighted random), rolls friendliness gain with `trough-eaten` / `chance` (base 5%; contributor progress via online or parked `GetProgress`), and on success stamps that uid onto the eater via `HusbandryFriendliness.Add` (entity Live pedigree; score = sum of contributor weights). A credited meal emits `fed-animal` before that roll (`caller:@trough`, `target` = animal, `input` = trough content). When pre-meal friendliness > 5, the emit also includes token `friendly`. Empty bag / hopper meals still use the 5% base chance under sentinel `@friendliness` but do not emit XP. Husbandry rule `prosequor:feed-animal` requires `fed-animal` + `friendly` (amount `0.1`, `payee: contributor`). Legacy tag `trough-eaten` compiles as `fed-animal`. The Feedhand ability verb stays `trough-eaten` and does not apply to crop or loose meals.

**Friendliness gain gate** (husbandry): after a successful `HusbandryFriendliness.Add`, the Live blob stamps `friendlinessReadyAt` = current calendar `TotalHours` + `4 × (1..6)` (4–24 hours in 4-hour steps). Further gains before that time are ignored. Missing stamp is treated as `0` (always eligible).

**Crop pilfer** (husbandry): `BlockEntityFarmland.ConsumeOnePortion` (animal eats a live crop) rolls a fixed 5% (`TroughEatStation.BaseFriendlinessChance`, not the trough / Feedhand pipeline). On success, `HusbandryFriendliness.Add` credits the farmland planter (`MakerUid`); missing planter → sentinel `@friendliness`. Same gain cooldown and favorite-refresh rules as trough meals. A real planter also emits `fed-animal` (`caller:@crop`, `input` = crop block) before the roll. Missing planter emits nothing. Berry-bush grazing is not a feed.

**Loose-item eat** (husbandry): `LooseItemFoodSource.ConsumeOnePortion` (both seek-and-eat tasks) reads vanilla `EntityItem.byPlayerUid` and, when set, uses the same 5% roll and `HusbandryFriendliness.Add` as crop pilfer. Missing uid is a miss — it does **not** credit `@friendliness` and does not emit XP. A set uid emits `fed-animal` (`caller:@loose`, `input` = dropped item) before the roll. Death-drop meals and spilled container stacks count because vanilla already stamps those entities. Block breaks, chutes, hoppers, and loot do not. The uid lives on the entity, not the stack, so pickup / hopper suck / despawn drop it without a clear step and without touching pedigree.

**Attack penalty** (husbandry): a player hit that lands (`Entity.ReceiveDamage` returns true; cause entity is the attacking player) calls `HusbandryFriendliness.ApplyAttackPenalty`. Score &gt; 5 drops to 5 (contributor bag collapses to `@friendliness`; Favorite Seraph kept). Score ≤ 5 drops to 0. If the result is 0 and the attacker is the Favorite Seraph, `MakerUid` is cleared. Wolves, fall, fire, and other non-player sources do not change friendliness.

**Favorite Seraph** (husbandry): animal Live `MakerUid` is the favorite. When friendliness is already &gt; 5 and a care point is gained (`HusbandryFriendliness.Add`): roll 5% → on pass, if there is no pickable contributor leave the current favorite unchanged → else refresh to the highest-weight real contributor (ties broken at random; `@friendliness` and other `@` sentinels are not pickable). Entity info uses `OwnerCredit` with `prosequor:favorite-seraph` — same muted italic chrome as Planted By / Created By. Cleared when an attack zeroes friendliness and the attacker was that favorite.

**Age-up / birth XP** (husbandry): when a friendly animal becomes adult (`aged-up`) or gives birth (`gave-birth`), emit with blank actor + real contributor shares (`payee: contributors`, amount `5`). Sentinel `@…` weights are omitted from the payout. After the emit, contributor weights are wiped to zero (favorite `MakerUid` / recipe / friendliness-ready kept). Non-friendly animals neither pay nor wipe.

**Skep harvest XP** (husbandry): placing a skep adds the placer as BE contributor weight `1` (stack Live capture first; no MakerUid stamp). Break or Apiary Master sneak-extract of a harvestable skep adds the harvester (`+1`), then emits `skep-harvest` with `payee: contributors` and `pay: quantity` = honeycomb count after Sticky Fingers (amount `0.2` per comb). Break relies on BE removal to clear the bag; non-breaking extract wipes contributors then re-adds the harvester at `1`.

**Empty skep pedigree:** empty variants use dummy BE `ProsequorPedigree` (`BlockEntityProsequorPedigree` — no ticks/POI) via asset patch so place can stamp. Populated stays vanilla `Beehive`. Swarm `TryPopCurrentSkep` snapshots dest Live before `SetBlock` and restamps onto the new Beehive BE (empty placer lineage). Source hive emits `skep-propagate` (`payee: contributors`, amount `1`); no contributors → no pay.

Milking: `MilkingComplete` emits `milked` (± `friendly` when score > 5); actor = milker; `payee` defaults to `user`. Husbandry rule `prosequor:milk-friendly` requires both tags (amount `1`).

`amount` may be a scalar or a number array. Arrays need a measure channel (`resistance`, `voxels`, or `ingredients`) and are piecewise-lerped across that channel's domain (same curve as quality attribute tables). Affix lists still snap to the nearest index. `voxels` is the catalog measure (voxels per unit); countable clay-form voxels are `quantity`. Ingredient lerp always uses min **1** / max **40** (not a world catalog).

`totalUnitFlat` / `totalUnitPercent` are removed — use an `ingredients` amount table instead.

### XP `when`


| Field      | Required | Meaning                                       |
| ---------- | -------- | --------------------------------------------- |
| `activity` | yes      | Activity id (bare → `game:`)                  |
| `tags`     | no       | Same criteria language as ability `when.tags` |
| `priority` | no       | Tie-break after specificity                   |


All set conditions AND. Among matching rules for a skill, one winner: identity criteria beat collections; more criteria beat fewer; then `priority`; then source order.

### Activity id convention


| Domain                      | Use                                                                                 |
| --------------------------- | ----------------------------------------------------------------------------------- |
| `prosequor:effort`          | Rate / continuous effort (Emit stamps or RegisterPoll → watcher)                    |
| `prosequor:deed`            | Amount / discrete completion (`Deed.Emit` → FatherXp)                               |
| `prosequor:collect-xp-item` | Amount for stamped collectible flush (same payment engine as deed; own rule bucket) |
| `game:…`                    | Legacy / other rate activities if registered                                        |
| `<modid>:…`                 | Mod-invented activities (or emit into `prosequor:deed` with custom tokens)          |




### Built-in activities

Rate and amount rules use the **same** `when.tags` language (see [Tags](#tags)).

#### Rate (`prosequor:effort` — work-bucket samples, pay `rate × dt`)

Two entry points, one stamp store:


| Path                                         | When to use                                                       |
| -------------------------------------------- | ----------------------------------------------------------------- |
| `Effort.Emit` / `EmitEffort`                 | Game already pulses (interact step, Harmony, your mod’s tick)     |
| `Effort.RegisterPoll` / `RegisterEffortPoll` | Continuous state with no natural pulse — watcher asks each sample |


Polls return **null** when inactive, or an `EffortPollResult` (tokens + optional `target` / `mount` / `ground` / `channel`). The watcher runs polls → Emit → materializes fresh stamps (fills ambient `caller` from hotbar / `last-craft`, derives `moving` onto mount stamps). Cross-skill matches may all pay; one winner per skill per sample.


| Source                             | Tokens                                               | Roles                          |
| ---------------------------------- | ---------------------------------------------------- | ------------------------------ |
| Pan (Emit on interact)             | `interacting`                                        | `target` = sifted material     |
| Watering can (Emit on pour)        | `interacting`                                        | `target` = farmland; only while the pour raises `waterCredit` |
| Fishing poll (`prosequor:fishing`) | `fishing`                                            | `target` = liquid              |
| Mount poll (`prosequor:mount`)     | `mounted`, `riding` / `boating`, optional `helmsman` | `mount`; boat `ground` = water |
| Watcher                            | `moving` (derived onto mount stamps)                 | —                              |


```csharp
// Natural pulse
Effort.Emit(player, EffortToken.Interacting, target: materialCode);

// No natural pulse — watcher polls
mod.RegisterEffortPoll("mymod:spinning", (player, entity) =>
{
    if (!IsSpinning(player)) return null;
    return new EffortPollResult(["spinning"], Target: wheelCode);
});
```

Standard tokens: C# `EffortToken` enum. JSON uses lowercase strings. Custom bare tokens (no `:`) are allowed. Tagless `prosequor:effort` rate rules warn at compile. Legacy `RegisterActivityWrapper` remains for non-effort activities; prefer Emit / RegisterPoll for rate XP.

```json
{
  "rate": 0.01,
  "when": {
    "activity": "prosequor:effort",
    "tags": ["interacting", "caller:<pan>"]
  }
}
```



#### Amount (`prosequor:deed` — discrete emit, pay via FatherXp)

Emitters call `Deed.Emit` / `ProsequorModSystem.EmitDeed` with an optional **actor** (player uid — never a tag), optional **makerUid**, optional **selectedContributorUid**, optional weighted **contributors**, tokens, and roles/metrics. The winning rule’s `payee` selects who receives the grant (`user` / `maker` / `contributor` / `contributors`). Blank **caller** on deed emit normalizes to `@hand` (effort ambient blank stays empty / `caller:none`). Rules resolve **at the call** (no watcher). Online players get buckets immediately; offline recipients go to FatherXp's mailbox until join. Mods that emit into this pipe get match, amount tables, multi-skill winners, and offline delivery for free.

Emit args fill a channel bag; the winning rule’s `pay` selects which channel to read:


| Emit input                          | Channel                        |
| ----------------------------------- | ------------------------------ |
| `metric` + domain dig / mine / chop | `resistance`                   |
| `metric` + domain `clay-voxels`     | `voxels`                       |
| `craftCount` / `quantityUnits`      | `quantity`                     |
| `totalUnits`                        | `ingredients`                  |
| `makerUid`                          | used by `payee` `maker`        |
| `selectedContributorUid`            | used by `payee` `contributor`  |
| `contributors` `(uid, weight)`      | used by `payee` `contributors` |



| Shape               | Tokens                         | Typical caller / target / `pay` / `payee`                                                                                                                           |
| ------------------- | ------------------------------ | ------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Dig / mine / chop   | `block-broken`                 | `caller` = tool, bomb (`<bombs>`), or `@hand`; `target` = broken block; `pay`: `resistance` (soil dig also has quantity specialty routes for clay / charcoal / saltpeter). Pickaxe mining and bomb blasts share mining's `prosequor:mine-mining` rule (`caller:<pickaxe,bombs>`). Bomb XP is emitted from `OnBlockExploded` (not `OnBlockBroken`) and only for mine-class stone / ore / gemstone. |
| Harvest             | `harvested`                    | `caller` = held tool (`caller:<knife>` / scythe / …) or `@hand` when empty or holding a non-tool; `target` = crop/berry/fruit-tree/mushroom/reed/stick/sap/…; optional token `domesticated` when the plant has a planter (fruit trees: root cutting via `RootOff`), or `undomesticated` when the source is wild (no planter, and not a player-placed forage mark / ground-storage pile); fruit-tree farming XP pays the **harvester** in full; sap is the dripping `log-resin-*` scoop (not axe-fell); `pay`: `quantity` from drops (`exclude` for seeds) or flat |
| Butcher             | `butchered`                    | Actor = harvester; `caller` = knife / rip weapon or `@hand`; `target` = animal; `pay`: `quantity` = meat + fat after cooking yield (`<meat, fat>`) |
| Growth stage        | `grown`                        | Blank actor + `makerUid` = planter + contributor shares; crop farming uses `payee: contributors` (synthesize planter if bag empty; no wipe on grow); bush/sapling rules `payee: maker`; fruit-tree structural growth batches `TryGrowTo` into one emit (`craftCount` = blocks, `payee: maker`, amount `2` with `pay: quantity`); `target` = crop / bush / sapling / fruit-tree; token `domesticated` |
| Till soil           | `till-soil`                    | Actor = tiller; one emit per converted tile (Field Expertise multiplies by tile count); stamps till care credit (contributor weight `1`, once per crop cycle); `pay` omitted (`flat`)                                                                                     |
| Fertilizer absorb   | `fertilizer-absorbed`          | Blank actor; `target` = farmland, or the berry bush above a `BerryBushFarmland` store (starting soil inherit pays nothing); `pay`: `quantity` = whole nutrient percents absorbed; `payee`: `contributors` (empty bag or missing bush → no pay); absorb multiplier is store-scoped and survives harvest |
| Craft grid          | `crafted`                      | `caller:@grid`; `target` = output; `pay`: `quantity` = output stack × completions; `ingredients` = lerped amount table on recipe units (1–40)                       |
| Ore smash           | `crafted`                      | Ground hammer smash (`ItemOre.OnContainedInteractStop`); `caller` = hammer; `target` = nugget; `pay`: `quantity` = ores processed (≤4)                              |
| Clay form voxels    | `crafting`                     | `caller:@hand`; `target` in `<clay-formed>`; `pay`: `quantity` (novel good voxels)                                                                                  |
| Anvil smith voxels  | `crafting`                     | `caller:@hand`; `target` in `<smithing-formed>`; `pay`: `quantity` (novel good Metal voxels)                                                                      |
| Metal tool craft    | `crafted`                      | `caller:@grid`; `target` in `<metal-crafts>`; `pay`: `quantity`                                                                                                     |
| Mold cast           | `mold-cast`                    | `caller:@mold`; blank actor + pourer contributor shares; `pay`: `ingredients` (`FillLevel/10`, domain 1–40); `payee`: `contributors`                               |
| Bloomery harvest    | `bloomery-harvest`             | Actor = breaker / Bloom Brigand extractor; flat amount; pays when finished bloom is taken (break with `OutSlot`, or successful sneak+RMB extract)                  |
| Fish catch          | `fishing-catch`                | `caller` = pole; `target` = fish; `pay` omitted (`flat`)                                                                                                            |
| Kiln settle         | `kiln-fired`                   | One emit; `caller` = `pit-kiln` / `beehive-kiln`; pedigree contributor shares; `pay`: `voxels`; `payee`: `contributors`                                             |
| Animal feed         | `fed-animal`                   | Blank actor + `selectedContributorUid` = trough feeder, crop planter, or dropper; `caller` = `@trough` / `@crop` / `@loose`; `target` = animal; `input` = eaten collectible; optional `friendly` (score > 5 before meal bump); friendliness +1 is chance-gated (trough uses `trough-eaten`/`chance`; crop and loose are fixed 5%); emit still fires on a real payer even when the roll misses; `payee`: `contributor`. Legacy tag `trough-eaten` compiles as `fed-animal` |
| Milking             | `milked`                       | Actor = milker; `caller:@hand`; `target` = animal; optional `friendly` (score > 5); `payee`: `user`                                                                 |
| Animal age-up       | `aged-up`                      | Blank actor; `target` = adult; requires `friendly`; real contributor shares; `payee`: `contributors`; wipe shares after emit (keep maker)                            |
| Animal birth        | `gave-birth`                   | Blank actor; `target` = mother; requires `friendly`; real contributor shares; `payee`: `contributors`; wipe shares after emit (keep maker)                           |
| Skep harvest        | `skep-harvest`                 | Actor = harvester; honeycomb `pay: quantity` (post Sticky Fingers); BE contributors (placer + harvester); `payee`: `contributors`; RMB wipe+re-add harvester         |
| Skep propagate      | `skep-propagate`               | Blank actor; source hive contributors; amount `1`; `payee`: `contributors`; dest empty Live carried onto new Beehive BE                                              |
| Collect (stamped)   | —                              | Activity `prosequor:collect-xp-item`; actor = picker; `target` = item code; `pay`: `quantity` from buffer flush; only stamped stacks enqueue; `payee`: `user`       |
| Saddle break / tame | `saddle-break` / `saddle-tame` | `caller` / `target` / `mount` = mount; `pay` omitted (`flat`)                                                                                                       |


**Collect-XP pipeline:** producers set a bool stamp (`prosequorCollectXp`) on item units (or on a ground-egg BE, copied onto drops). Pickup (`TryGiveItemstack` / `TryGiveItemStack`) enqueues `code → qty` into a per-player buffer and leaves leftovers stamped. The activity-watch bucket tick flushes the buffer via `Deed.Emit` with activity `prosequor:collect-xp-item` (amount rules with `pay: quantity`). That activity has its own amount-rule bucket — it does not scan `prosequor:deed` rules. Logout discards the buffer (no FatherXp). Friendliness for eggs is a **lay-time stamping policy** (friendly hen → stamp).

Standard tokens: C# `DeedToken` enum. `crafting` is clay-form (hand-shape); craft-grid uses `crafted`. Custom bare tokens allowed. Tagless `prosequor:deed` amount rules warn at compile.

```json
{
  "amount": 5,
  "when": {
    "activity": "prosequor:deed",
    "tags": ["fishing-catch", "caller:<fishingpole>", "target:<small-fish>"]
  }
}
```

---



## Collections

Path: `assets/<moddomain>/config/prosequor/collections.json` or `collections/*.json` (JSON array).

```json
[
  {
    "id": "clothing",
    "includes": ["game:clothes-*"]
  },
  {
    "id": "wearable",
    "unions": ["clothing", "armor"]
  },
  {
    "id": "berry-bush",
    "dependsOn": [{ "modid": "wildcraft" }],
    "includes": ["wildcraft:berrybush-*"]
  }
]
```


| Field       | Meaning                                                                                                |
| ----------- | ------------------------------------------------------------------------------------------------------ |
| `id`        | Collection key (used as `<id>` in `when.tags`)                                                         |
| `dependsOn` | Engine-style mod gates (same shape as contribution / JSON-patch `dependsOn`). Unmet rows are skipped   |
| `includes`  | Patterns / codes expanded against all blocks and items                                                 |
| `excludes`  | Other collection ids whose positive membership (includes and C# fills) is subtracted after every include is built, before unions copy. Reads a frozen snapshot, so mutual excludes are order-independent. Union copies are not subtracted; a union-only target warns and is skipped. Unknown ids warn and are skipped |
| `unions`    | Other collection ids whose membership is copied into this key (multi-pass, after excludes). May appear with `includes` |


Same `modid` / `invert` AND rules as contributions. Omit the field, or use an empty array, to always apply. Unmet gates skip the whole row quietly (verbose debug): that row's `includes`, `excludes`, and `unions` are not registered. Rows with the same `id` still merge, so a second row can graft extra patterns only when a mod is loaded. A key that exists only on a gated row is absent when the gate fails — tags that name it will fail compile unless those rules are gated too.

Pool ids share the same key space.

---



## Pools

Path: `assets/<moddomain>/config/prosequor/pools.json` or `pools/*.json` (JSON array). Domain of `id` must match the asset domain. Last-win by id.

```json
[
  {
    "id": "prosequor:clay-item",
    "entries": [
      { "code": "game:clay-blue" },
      { "code": "game:clay-red", "weight": 2 }
    ]
  }
]
```


| Field              | Meaning                                    |
| ------------------ | ------------------------------------------ |
| `id`               | Namespaced pool id (also a collection key) |
| `entries[].code`   | Item code                                  |
| `entries[].weight` | Pick weight (default `1`)                  |


Used as `params.table` for `append-from-drop-table` / `replace-from-drop-table`. Omit `table` to use the ambient drop table.

---



## Affix lists

Path: `assets/<moddomain>/config/prosequor/affixes.json` or `affixes/*.json` (JSON array). Domain of `id` must match the asset domain. Last-win by id.

```json
[
  {
    "id": "prosequor:durability",
    "entries": [
      { "code": "sturdy", "lang": "prosequor:affix-sturdy", "color": "#84ff84" },
      { "code": "reinforced", "lang": "prosequor:affix-reinforced", "color": "#c9c9c9" }
    ]
  }
]
```


| Field             | Meaning                 |
| ----------------- | ----------------------- |
| `id`              | Namespaced list id      |
| `entries[].code`  | Stamp dedupe key        |
| `entries[].lang`  | Lang key                |
| `entries[].color` | Optional richtext color |


`prosequor:add-affix` references with `{ "list": "prosequor:durability", "item": 0 }` or `{ "list": "…", "item": "sturdy" }`. Quality banding needs unique entry codes (same code dedupes on stamp).

`prosequor:quality` is the craft-grade list (Nice → Masterful). `apply-quality` stamps one leading entry under code `quality` via `SetFront`; rarity colors live in the lang strings, not `entries[].color`. Tooltip: quality is omitted from the header affix tags and shown as a muted italic footer (`Quality:` + colored grade) above `Created By`. `prosequor:quality-rank` writes the same footer plus an authored pedigree `qualityRank` (table value, not the 0–30 point index). Items in `<distilled>` never get the grade affix or leftover rank: the condenser adds mash `qualityRank` to `quality-base`, then strips both (`QualityGrade` also skips the collection).

---



## Level-ups

Path: `assets/<moddomain>/config/prosequor/level-ups.json` (also `level-ups/*.json`). Rules merge last-win by `id` across domains, then contribution `levelUps` grafts apply.

Player level-up grants are **not** ability-pipeline folds. For each newly reached player level `L` in `(before, after]`:

- `every: N` fires when `L % N == 0` (`N = 1` → every level)
- `levels: […]` fires when `L` is in the list

Exactly one of `every` or `levels` is required. Skipping levels applies every crossed match.

### Rule fields


| Field      | Meaning                                                                 |
| ---------- | ----------------------------------------------------------------------- |
| `id`       | Stable rule id (contribution merge/disable key)                         |
| `every`    | Fire on multiples of N                                                  |
| `levels`   | Fire on exact player levels                                             |
| `action`   | Grant action (see below)                                                |
| `params`   | Action-specific (`value` default 1; attribute `key` for earn-attribute) |
| `priority` | Sort key (default 0; then registration order)                           |


### Actions


| Action                                  | Effect                                                                 |
| --------------------------------------- | ---------------------------------------------------------------------- |
| `prosequor:earn-skill-point`            | Add `value` unlock points                                              |
| `prosequor:earn-specialization-point`   | Derived capacity only (slots = matches from level 1..playerLevel)      |
| `prosequor:earn-attribute`              | Requires `params.key`: `buckets` runs soft-reset growth `value` times; a known attribute id adds `value` to that score (capped at 18, no bucket drain) |


Shipped defaults:

```json
{
  "rules": [
    {
      "id": "prosequor:earn-skill-point",
      "every": 1,
      "action": "prosequor:earn-skill-point"
    },
    {
      "id": "prosequor:earn-specialization-point",
      "every": 10,
      "action": "prosequor:earn-specialization-point"
    },
    {
      "id": "prosequor:earn-attribute",
      "levels": [10, 20, 28, 35, 40, 44, 47, 50],
      "action": "prosequor:earn-attribute",
      "params": { "key": "buckets", "value": 1 }
    }
  ]
}
```

Skill-level milestone unlock points (+1 per 20 skill levels) and skill `attributeScores` bucket fill are separate and unchanged.

---



## Contributions

Mods extend an existing skill without editing the base JSON: add nodes, remove nodes, replace nodes, or merge XP. They may also graft player level-up rules via `levelUps`.

1. Depend on `prosequor` in `modinfo.json`.
2. Add files under `assets/<yourmodid>/config/prosequor/contributions/<anything>.json` (JSON array). Merged in asset order (domain, then path).
3. Ship lang and icons for contributed nodes.

Contributed node ids must be `<yourmodid>:localId`. Prerequisites may reference existing nodes by plain id.

### Contribution object


| Field       | Required | Meaning                                                                    |
| ----------- | -------- | -------------------------------------------------------------------------- |
| `skill`     | no*      | Target skill id (*required unless the entry only sets `levelUps`)          |
| `dependsOn` | no       | Engine-style mod gates; unmet entries are skipped (skill grafts and `levelUps`) |
| `disable`   | no       | `"all"` to remove the entire skill, or an array of node ids to strip first |
| `xpRules`   | no       | XP rules to merge (last-win by rule `id`)                                  |
| `nodes`     | no       | Nodes to graft (same shape as skill nodes + optional `replaces`)           |
| `levelUps`  | no       | Player level-up grafts: `disable` (`"all"` or rule-id array) then `rules`  |


`dependsOn` uses the same shape and AND/`invert` rules as Vintage Story JSON patches. Each clause is `{ "modid": "<id>", "invert": false }`. Every clause must pass (`loaded XOR invert`). Omit the field, or use an empty array, to always apply. Unmet gates skip the whole entry quietly (verbose debug), including `levelUps`.

Processing order per entry: `dependsOn` → `disable` → `xpRules` → each `nodes` entry (`replaces` then append). `"disable": "all"` stops further ops for that entry. `levelUps` is applied by the level-up loader (disable then merge rules last-win by id), independent of skill grafts.

### Level-up contribution example

```json
[
  {
    "levelUps": {
      "disable": ["prosequor:earn-specialization-point"],
      "rules": [
        {
          "id": "mymod:strength-at-5",
          "levels": [5],
          "action": "prosequor:earn-attribute",
          "params": { "key": "strength", "value": 1 }
        }
      ]
    }
  }
]
```

### `dependsOn`

```json
[
  {
    "skill": "husbandry",
    "dependsOn": [{ "modid": "wildcraft" }],
    "xpRules": [
      {
        "id": "mymod:wildcraft-feed",
        "amount": 0.1,
        "when": {
          "activity": "prosequor:deed",
          "tags": ["fed-animal"]
        }
      }
    ]
  },
  {
    "skill": "husbandry",
    "dependsOn": [{ "modid": "wildcraft", "invert": true }],
    "nodes": [
      {
        "id": "mymod:vanilla-only-node",
        "requires": ["keeper"],
        "nameLang": "mymod:vanilla-only-name",
        "descriptionLang": "mymod:vanilla-only-desc",
        "icon": "mymod:icons/vanilla-only.svg"
      }
    ]
  }
]
```

### `replaces`

Removes the named node, rewrites every other node’s `requires` / `excludes` from the old id to the new id, then appends. Does not copy `layout`.

### Examples

```json
[{ "skill": "fishing", "disable": ["coastalmaster"] }]
```

```json
[{ "skill": "digging", "disable": "all" }]
```

```json
[
  {
    "skill": "fishing",
    "nodes": [
      {
        "id": "mymod:shorefisher",
        "replaces": "coastalmaster",
        "requires": ["goodbait"],
        "nameLang": "mymod:shorefisher-name",
        "descriptionLang": "mymod:shorefisher-desc",
        "icon": "mymod:icons/shorefisher.svg"
      }
    ]
  }
]
```

```json
[
  {
    "skill": "digging",
    "xpRules": [
      {
        "id": "mymod:bonus-dig",
        "amount": [1, 5, 10],
        "when": {
          "activity": "prosequor:deed",
          "tags": ["block-broken", "target:<soil>", "caller:<shovel>"]
        }
      }
    ]
  }
]
```

Omit `disable` and `replaces` to append nodes via `requires`. Unmet prerequisites are deferred across contribution files; still missing after all files → skipped with a warning.