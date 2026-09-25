# Skill JSON reference

User-facing field catalog for Prosequor JSON. Explains what each key does and, where it matters, what accepted values do. No engine internals or design rationale.

**Paths:** `assets/<moddomain>/config/prosequor/…`

**Effect address:** `hook` → `verb` → `phase` (omit → `default`) → **contract**. Actions bind to a hook+verb+phase and must fulfill that phase’s contract (or be side-effect actions on a phase with no fold contract). `contract` is not a JSON field.

```
requires  →  who must be owned (logic)
layout    →  where it draws relative to parents (presentation)
excludes  →  who you cannot also own
```

## Contents

- [Skill file root](#skill-file-root)
  - [Kind & level caps](#kind--level-caps)
  - [Hobby unlock points](#hobby-unlock-points)
  - [Attribute scores](#attribute-scores)
- [Node identity / unlock](#node-identity--unlock)
- [Prerequisites & exclusivity](#prerequisites--exclusivity)
- [Layout](#layout)
- [Tiers & replicate](#tiers--replicate)
- [Description params](#description-params)
  - [`totalParams`](#totalparams)
- [Effects](#effects)
  - [Ability `when`](#ability-when)
- [Tags](#tags)
  - [Criterion forms](#criterion-forms)
  - [Fact roles](#fact-roles)
  - [Reserved callers](#reserved-callers)
  - [Effort tokens](#effort-tokens)
  - [Deed tokens](#deed-tokens)
- [Contracts](#contracts)
- [Hooks](#hooks)
  - [block-interaction](#prosequorblock-interaction)
  - [item-interaction](#prosequoritem-interaction)
  - [crafting-interaction](#prosequorcrafting-interaction)
  - [entity-interaction](#prosequorentity-interaction)
  - [player-interaction](#prosequorplayer-interaction)
  - [progress](#prosequorprogress)
- [Actions](#actions)
  - [Int range](#int-range-params)
  - [NumberSpec](#numberspec)
  - [Contract: number](#contract-number)
  - [Contract: bool / none / refund](#contract-bool--none--refund)
  - [Contract: stack](#contract-stack)
  - [Contract: stacks](#contract-stacks)
  - [Gates](#gates)
- [XP rules](#xp-rules)
  - [XP `when`](#xp-when)
  - [Activities](#activities)
  - [`pay` / `payee` / `include` / `exclude`](#pay--payee--include--exclude)
- [Collections](#collections)
- [Pools](#pools)
- [Affix lists](#affix-lists)
- [Attribute stats](#attribute-stats)
- [Trait attributes](#trait-attributes)
- [Level-ups](#level-ups)
- [Contributions](#contributions)

---

## Skill file root

Path: `skills/<id>.json` (one object per file).

| Field | Meaning |
| --- | --- |
| `id` | Stable skill id (required) |
| `nameLang` | List title lang key (default `skill-{id}`) |
| `descriptionLang` | Optional C-menu list-hover blurb |
| `descriptionParams` | Optional `{0}`… args for `descriptionLang` (same grammar as [nodes](#description-params)) |
| `icon` | Texture path (bare paths resolve under `prosequor:`) |
| `hobby` | `true` = hobby skill (see [kind](#kind--level-caps)) |
| `maxLevel` | Ignored if present; caps are derived |
| `attributeScores` | Optional bucket fill on each skill level gained |
| `xpRules` | XP amount/rate rules owned by this skill |
| `effects` | Always-on baseline rules |
| `tree` | Unlock tree (`{ "nodes": [ … ] }`); omit/empty → passive |

Root `effects` are always active. Tier `effects` are active only for the owned tier — lower tiers do not stack. Multi-tier nodes must [`replicate`](#replicate) prior effects.

### Kind & level caps

| Kind | Cap | Classification |
| --- | --- | --- |
| Hobby | 20 | `"hobby": true` (wins over everything) |
| Specialization | 100 | Tree has at least one `specialization: true` node |
| Minor | 50 | Non-empty tree, no specialization nodes |
| Passive | 50 | No tree / empty tree |

Hobby skills strip `specialization` flags at compile. They use local unlock points ([below](#hobby-unlock-points)), not global points.

### Hobby unlock points

```text
entitled  = ceil(totalTierCost × skillLevel / maxLevel)
spendable = max(0, entitled − sum of owned tier costs)
```

Cost-0 tiers do not contribute. Global points cannot buy hobby nodes.

### Attribute scores

On each skill level gained (natural XP commit), each entry’s `value` is added to that attribute’s growth bucket, multiplied by levels gained. Fill runs before player XP.

| Field | Values |
| --- | --- |
| `id` | `strength` \| `perception` \| `constitution` \| `inconspicuity` \| `resilience` |
| `value` | Finite number `> 0` |

Duplicate ids last-win. Omitted or empty = no fill.

```json
"attributeScores": [
  { "id": "strength", "value": 0.5 },
  { "id": "resilience", "value": 0.2 }
]
```

---

## Node identity / unlock

| Field | Meaning |
| --- | --- |
| `id` | Stable node id (unique within the skill) |
| `nameLang` / `descriptionLang` | Lang keys |
| `descriptionParams` | `{0}`, `{1}`… from effect params |
| `totalParams` | Optional live totals for the owned-rank tooltip |
| `icon` | Texture path |
| `cost` | Unlock points (global for normal skills; hobby spendable for hobbies; tier can override; default `1`) |
| `minSkillLevel` | Floor for first tier (tier can override; default `0`) |
| `specialization` | `true` = spends a shared specialization slot; must be exactly 1 tier. Stripped on hobby skills. Slot capacity comes from [level-ups](#level-ups) `earn-specialization-point` rules |
| `tiers` | Ranks; later tiers usually `replicate` earlier effects |
| `layout` | Visual grid hints |
| `requires` / `excludes` | Prerequisites and mutual exclusion |
| `replaces` | Contribution-only: remove named node and rewrite deps |

---

## Prerequisites & exclusivity

| Field | Meaning |
| --- | --- |
| `requires` | Groups: **OR inside**, **AND across** |
| `excludes` | Mutual lock; one-sided is symmetrized at compile |

```json
"requires": ["a", "b"]
"requires": [["coastalmaster", "riverspecialist"], "fisher"]
"excludes": ["riverspecialist"]
```

Any owned tier of a prereq counts.

---

## Layout

| Field | Values | Effect |
| --- | --- | --- |
| `layerOffset` | `1` (default) | Next row under deepest parent |
| | `0` | Same row as parent |
| `columnBias` | `"left"` / `"right"` | Soft nudge among legal columns; omit = barycenter (downward) or prefer right (same-row) |
| `compact` | `true` (default) / `false` | `false` = don’t pull this node upward in shake-out |

Roots (`requires` empty) ignore `layerOffset` / `columnBias`.

```json
"layout": { "layerOffset": 0, "columnBias": "left", "compact": false }
```

Constraints:

- Downward edge = child on parent layer + 1 → child column must be parent−1, parent, or parent+1.
- Max 3 downward children per parent.
- Same-layer children (`layerOffset: 0`) do not use those 3 slots.
- Edges cannot skip a row.
- Root rows can be banded by `minSkillLevel` when there are no parents.

---

## Tiers & replicate

| Field | Meaning |
| --- | --- |
| `descriptionLang` / `descriptionParams` / `totalParams` | Optional per-tier overrides |
| `cost` / `minSkillLevel` | Optional per-tier overrides |
| `effects` | Rules active only while this tier is owned |

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

| Form | Meaning |
| --- | --- |
| `"0.base"` | Effect index 0, param `base` (current tier on nodes; root effects on skill hover) |
| `"root.0.perSkillLevel"` | Skill root `effects` |
| `"prev.0.base"` | Previous tier (nodes only; missing treated as 0 in sums) |
| `"axeexpert.0.perLevel"` | Skill hover only: owned tier of node `axeexpert` (0 if unowned) |
| `{ "sum": ["0.base", "prev.0.base"], "format": "percent" }` | Sum selectors; `format` optional: `percent` \| `fractionPercent`. A negative value prints as its magnitude in red |

Bare `"base"` resolves across all current-tier effects (ambiguous if several share the key) — prefer scoped `index.param`.

### `totalParams`

Optional live total for the **owned** rank tooltip. Omitted or empty = no bracket. Node-level list; a tier may override.

```json
"totalParams": [
  { "target": 0, "format": "fractionPercent" }
]
```

| Field | Meaning |
| --- | --- |
| `target` | Effect index on that tier (post-`replicate`) |
| `format` | `percent` or `fractionPercent` |

Compile rejects a missing / out-of-range target, or a target that is not a `prosequor:number` spec.

---

## Effects

| Field | Role |
| --- | --- |
| `hook` / `verb` / `phase` / `action` | Surface → moment → facet (omit phase → `default`) → mutation. Action must fulfill the phase’s contract |
| `when` | Filter — `tags` AND |
| `params` | Action-specific |
| `priority` | Lower runs first, then registration order (default `0`) |
| `replicate` | Tier overlay only |

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

| Field | Meaning |
| --- | --- |
| `tags` | Every listed criterion must match (AND) |

Omit tags = no filter. Ability and XP share the same tags language.

```json
{
  "hook": "prosequor:block-interaction",
  "verb": "prosequor:interaction-speed",
  "action": "prosequor:number",
  "when": { "tags": ["caller:<axe>", "target:<wood>"] },
  "params": { "op": "scale", "base": 0, "perSkillLevel": 0.001 }
}
```

---

## Tags

1. Every listed criterion must match (AND). Case-insensitive. No OR across the list. Omit = no filter.
2. `<key>` looks up a collection (builtins + `collections.json` + pools). Unknown `<key>` fails compile. Comma lists inside one ref (`<a, b>`) match if the role identity is in **any** listed collection (OR inside that criterion only).
3. First colon splits a known role from the rest; otherwise the whole string is a target identity.

### Criterion forms

| Form | Meaning | Example |
| --- | --- | --- |
| `role:<collection>` | Role identity is in collection | `caller:<axe>`, `target:<clay>`, `drop:<seed>` |
| `role:<a, b>` | Role identity is in any listed collection | `last-craft:<clothing, armor>` |
| `role:code` | Exact identity on that role | `caller:game:torch` |
| `role:none` | Role has no code | `caller:none`, `target:none` |
| `caller:@hand` | [Reserved caller](#reserved-callers) | `caller:@grid` |
| `mount:<collection>` | Mount / boat entity in collection | `mount:<watercraft>` |
| `<collection>` | Same as `target:<collection>` | `<crop>` |
| `<a, b>` | Same as `target:<a, b>` | `<clothing, armor>` |
| `code` | Same as `target:code` (use `target:` when the code contains `:`) | `target:game:torch` |
| `op:place` | Role `op` equals `place` | clay-form |
| `damage:frost` | Damage kind present | on-damage |
| bare token | Event token on the fact | `interacting`, `block-broken`, `crafted` |

Known roles: `caller` (`held` is a legacy alias), `target`, `drop`, `last-craft`, `ground`, `mount`, `op`, `damage`, `input`.

- `input` matches if any craft-grid ingredient matches.
- `drop` is present only during mutate-drops per-stack `quantity` / `stack`.
- Collections are not allowed on `op` / `damage`.
- Wildcards belong in `collections.json` `includes`, not in `when.tags`.

### Fact roles

| Role | Content |
| --- | --- |
| `caller` | Actor tool / reserved identity (`@hand`, `@grid`, …) |
| `target` | The thing this event is about |
| `drop` | Current stack code during mutate-drops quantity/stack |
| `last-craft` | Last craft-grid product code when remembered |
| `ground` | Underfoot / water when distinct from target |
| `mount` | Mount / boat entity |
| `op` | Voxel operation (`place`, `remove`, `finish`, …) |
| `damage` | Damage kinds (`frost`, `weather`) |
| `input` | Craft-grid / feed ingredient codes |
| tokens | Effort / deed / event tokens |

### Reserved callers

| Identity | Meaning |
| --- | --- |
| `@hand` | Empty hand / default deed caller |
| `@grid` | Craft-grid |
| `@trough` | Trough feed |
| `@crop` | Crop pilfer feed |
| `@loose` | Loose-item feed |
| `@mold` | Cast mold |
| `@cementation` | Cementation furnace |

### Effort tokens

| Token | Typical use |
| --- | --- |
| `interacting` | Held interact pulse (pan, watering, …) |
| `mounted` | On a mount |
| `riding` | Land mount |
| `boating` | Boat |
| `helmsman` | Boat captain |
| `fishing` | Pole out |
| `moving` | In motion |
| `sprinting` / `swimming` / `sneaking` | Locomotion |
| `temporal-drain` | Temporal stability drain |

Custom bare tokens (no `:`) are allowed on effort facts.

### Deed tokens

| Token | Meaning |
| --- | --- |
| `block-broken` | Block break (dig / mine / chop) |
| `harvested` | Plant / forage harvest |
| `butchered` | Dead-animal harvest |
| `hunted` | Animal killed by a player arrow or thrown spear |
| `trapped` | Animal caught in a basket / crate trap |
| `crafted` | Craft-grid take / similar discrete craft |
| `crafting` | Hand-shape progress (clay-form / smith); one flat deed per novel voxel |
| `grown` | Growth stage advanced (legacy alias `crop-grown`) |
| `till-soil` | Soil → farmland |
| `fertilizer-absorbed` | Fertilizer nutrient transfer |
| `cooking-pot` | Meal finished in a cooking pot |
| `fishing-catch` | Fish catch |
| `kiln-fired` | Kiln settle |
| `mold-cast` | Mold cast hardened |
| `bloomery-harvest` | Finished bloom taken |
| `cementation-fired` | Cementation complete |
| `reinforced` | Block reinforced with the plumb and square |
| `healed` | Healing item successfully applied (bandage / poultice) |
| `saddle-break` / `saddle-tame` | Riding progress |
| `fed-animal` | Animal ate (legacy alias `trough-eaten`) |
| `milked` | Successful milking |
| `friendly` | Friendliness score > 5 at emit |
| `aged-up` | Juvenile became adult |
| `gave-birth` | Birth completed |
| `skep-harvest` / `skep-propagate` | Skep honeycomb / swarm |
| `domesticated` / `undomesticated` | Plant source tags |
| `pit-kiln` / `beehive-kiln` | Kiln caller identities |
| `fire-pottery` / `barrel` / `fruit-press` | Process tokens |
| `used-bait` | Bait consumed |
| `block` | Craft-grid refund fact when the output is a block |

Custom bare tokens are allowed. Tagless `prosequor:deed` / `prosequor:effort` rules warn at compile.

---

## Contracts

A contract is the kind of value a phase folds. It is implied by the phase; it is not written in JSON.

| Contract | Meaning |
| --- | --- |
| `number` | Scalar fold |
| `bool` | 0/1 flag fold |
| `stack` | Mutate one drop/output stack |
| `stacks` | Append / replace / filter a list of stacks |
| `refund` | Ingredient refund fold |
| *(none)* | Side-effect phase (e.g. bait restock) |

Gates (`chance`, `has-unlock`) keep the outer phase’s contract.

---

## Hooks

Bare ids normalize to `prosequor:`. Phases are per `(hook, verb)`.

### `prosequor:block-interaction`

| Verb | Phase | Contract |
| --- | --- | --- |
| `mutate-drops` | `quantity` | number |
| | `stack` | stack |
| | `stacks` | stacks |
| `mutate-process` | `quantity` | number |
| | `stacks` | stacks |
| `heat-structure-damage` | `skip` | number |
| `interaction-speed` | `default` | number |
| `plant-sapling` | `default` | number |
| | `growth` | number |
| `plant-bush-cutting` | `default` | number |
| | `growth` | number |
| `establish-cutting` | `default` | number |
| `seek-bobber` | `default` | number |
| `fertilize` | `default` | number |
| `plant-crop` | `default` | number |
| `field-work` | `size` | number |
| `scythe-multibreak` | `quantity` | number |
| `trough-fill` | `quantity` | number |
| `spawn-bees-chance` | `default` | number |
| `harvest-skep` | `allow-right-click-harvest` | bool |
| | `right-click-harvest-break-chance` | number |
| `harvest-bloomery` | `allow-right-click-harvest` | bool |
| | `right-click-harvest-break-chance` | number |

`mutate-drops`: block break drops. Fact `target` = block; `caller` = tool; during per-stack loop `drop` = current stack.

`mutate-process`: kiln / barrel / similar process output. Tokens `fire-pottery` / `barrel`.

`heat-structure-damage`: beehive kiln / stone coffin structure heat damage. Seed 0; fold is skip probability. Fact `target` = damaged block code.

### `prosequor:item-interaction`

| Verb | Phase | Contract |
| --- | --- | --- |
| `mutate-drops` | `quantity` | number |
| | `stack` | stack |
| | `stacks` | stacks |
| `interaction-speed` | `default` | number |
| `block-damaged` | `amount` | number |
| `item-damage` | `amount` | number |
| `craft-damaged` | `amount` | number |
| `consume-bait` | `restock` | *(none)* |
| `repair` | `add-durability` | number |
| `clay-form` | `assist-radius` | number |
| | `auto-finish` | number |
| | `place-conservation` | number |
| `anvil-heavy-hit` | `slag-radius` | number |
| | `assist-radius` | number |
| | `move-count` | number |
| `anvil-split` | `bits-refund` | number |
| `anvil-strike` | `decay-shrink` | number |
| `voxel-copy` | `default` | number |
| `voxel-refill` | `default` | number |
| `reinforce` | `strength` | number |
| `tend` | `health` | number |
| | `application-rate` | number |
| `revive` | `health` | number |
| | `duration` | number |

Durability: `caller` = damaged collectible; `target` = broken block when known. `block-damaged` = while breaking; `item-damage` = other loss; `craft-damaged` = craft-grid tool ingredient.

`clay-form`: match with `op:place` / `op:remove` / `op:finish`.

`reinforce`: seed = material `reinforcementStrength`. Match with plumb-and-square apply.

`tend`: healing-item application. Match `other` when the patient is another player. `health` seed = 1 (heal total multiplier). `application-rate` seed = 1 (application seconds divisor).

`revive`: after a successful player revive with a healing item. `health` seed = 0 (fraction of max health held back then restored). `duration` seed = 0 (seconds for that restore).

### `prosequor:crafting-interaction`

| Verb | Phase | Contract |
| --- | --- | --- |
| `mutate-output` | `quantity` | number |
| | `output` | stack |
| | `attributes` | stack |
| | `refund` | refund |
| `apply-quality` | `quality-base` | number |
| | `quality-window` | number |
| | `quality-rolls` | number |
| | `quality-bonus` | number |
| | `attributes` | stack |
| `recipe-available` | `default` | bool |

Fact `target` = output code. `refund` runs after ingredient consume; gate with `input:<collection>`. When the output is a block, the refund fact also carries token `block`.

`mutate-output` / `output` is registered with no shipped actions.

`recipe-available`: craft-grid recipe unlock gate (seed false). Match recipes that declare `attributes.prosequorUnlock`.

### `prosequor:entity-interaction`

Fact `target` = animal / mount / boat code.

| Verb | Phase | Contract |
| --- | --- | --- |
| `mounted` | `move-speed` | number |
| | `turn-speed` | number |
| | `saddle-break` | number |
| | `hunger-rate` | number |
| | `fall-damage` | number |
| | `melee-damage` | number |
| | `ratline-stamina` | number |
| | `can-ride` | bool |
| `animal-flee` | `chance` | number |
| | `multiplier` | number |
| `animal-seek` | `chance` | number |
| | `multiplier` | number |
| `animal-melee` | `chance` | number |
| | `multiplier` | number |
| `animal-brood` | `chance` | number |
| | `multiplier` | number |
| `animal-milk` | `chance` | number |
| | `multiplier` | number |
| `animal-pet` | `default` | bool |
| `trough-eaten` | `chance` | number |
| `mutate-drops` | `quantity` | number |
| | `stack` | stack |
| | `stacks` | stacks |

`mounted`: filter with `mount:<raft>` / `sailboat`; boats also use `ground` water and tokens `moving` / `helmsman`. Entity `mutate-drops` / `stack` supports `chance` and `upgrade-hide-size`.

`animal-flee` `chance`: ordinary alert threat reduction fraction (seed 0, base 1). `animal-flee` / `animal-brood` `multiplier`: friendliness calm percent (seed 100 = ×1) on ordinary alert threat. `animal-melee` / `animal-milk` `multiplier` still scale melee fear-reduction and milking aggro.

### `prosequor:player-interaction`

| Verb | Phase | Contract |
| --- | --- | --- |
| `health` / `satiety` / `hunger-delay` / `armor-walk` / `melee-damage` / `basic-slots` / `ranged-speed` / `ranged-acc` / `fall-damage-factor` / `fall-damage-threshold` / `temporal-recover-rate` / `temporal-drain-rate` / `animal-threat` / `crit-chance` / `whole-vessel-loot-chance` | `default` | number |
| `sprint-speed` / `swim-speed` / `sneak-speed` / `animal-sense-range` / `animal-threat-sneak` / `arrow-break` | `default` | number |
| `unaware-damage` | `amount` | number |
| | `threshold` | number |
| `cat-eyes` | `default` | number |
| `on-damage` | `amount` | number |
| | `last-stand` | number |
| `bleed-out` | `rate` | number |

`animal-threat`: player threat-emission percent for the animal alert meter (pipeline percent ÷ 100; inconspicuity maps score 0→18 to 180→80). Does not write the vanilla `animalSeekingRange` entity stat. The meter owns player-flee eligibility (`CanSensePlayer` false until panic, then true for the alert target); creature TaskAI still runs `fleeentity` / seek / melee.

`animal-sense-range`: sneak-only sense-range multiplier (seed 1). `animal-threat-sneak`: sneak-only threat-emission multiplier (seed 1). `arrow-break`: arrow break chance 0–1 (seed = current break chance).

`unaware-damage` / `amount`: outgoing damage multiplier (seed 1) when the victim's alert-meter threat is below `unaware-damage` / `threshold` (seed 0, percent).

`on-damage`: match `damage:frost` / `damage:weather`.

`bleed-out`: mortally-wounded revive-window multiplier (seed 1).

### `prosequor:progress`

| Verb | Phase | Contract |
| --- | --- | --- |
| `skill-xp` | `amount` | number |
| `skill-bucket` | `cap` | number |

`skill-xp`: after an xpRule wins, before commit. Own-skill only.

---

## Actions

Same action id can bind to several hook+verb+phases. Nested `chance` / `has-unlock` children must be valid for the same hook+verb+phase.

### Int range params

`rolls` / `quantity`: omit → default (usually `1`); number / `"2"` → fixed; `"2-5"` → inclusive `min..=max` (both ends ≥ 1).

### NumberSpec

Used by `prosequor:number` and nested chance/freshness/attribute operands.

| Param | |
| --- | --- |
| `op` | `add` \| `scale` \| `set` |
| Operand | literal `value` **or** skill-scaled `base` / `perSkillLevel` / `cap` (optional `skill`) |
| `ofBase` | add only: multiply addend by frozen context base |

Skill-scaled operand: `min(cap, base + perSkillLevel × skillLevel)`. Same units as literal `value`. `cap: 0` = uncapped. Scale: `value * (1 + amount)`.

### Contract: number

#### `prosequor:number`

NumberSpec params.

Surfaces include: mutate-drops/`quantity` (block, item, entity); mutate-process/`quantity`; mutate-output/`quantity`; reinforce/`strength`; heat-structure-damage/`skip`; tend/`health` and `application-rate`; revive/`health` and `duration`; bleed-out/`rate`; interaction-speed/`default` (block + item); mounted number phases (not `can-ride`); repair/`add-durability`; clay-form phases; anvil phases; fertilize/`default`; plant/seek `default` and plant/`growth`; field-work/`size`; scythe-multibreak/`quantity`; trough-fill/`quantity`; spawn-bees/`default`; harvest-skep|bloomery/`right-click-harvest-break-chance`; animal `chance`/`multiplier`; trough-eaten/`chance`; apply-quality number phases; durability `amount`; voxel-copy|refill/`default`; progress skill-xp/`amount` and skill-bucket/`cap`; player sprint|swim|sneak and temporal rates.

#### `prosequor:adjust-plant-climate-value`

NumberSpec. Surface: block / plant-crop / `default`.

#### `prosequor:add-mapped-number`

| Param | |
| --- | --- |
| `fromScore` / `fromValue` / `toScore` / `toValue` | Linear map endpoints |
| `midScore` / `midValue` | Optional hinge |
| `op` | `add` (default) or `scale` (`value * mapped`) |
| `round` | Optional `ceil` \| `floor` \| `round`; omit for fractional |

Surfaces: player-interaction mapped verbs/`default`; cat-eyes/`default`; on-damage `amount` / `last-stand`.

### Contract: bool / none / refund

#### `prosequor:allow-mounted-ride-without-saddle`

No params. Sets mounted/`can-ride` to true.

#### `prosequor:allow-animal-pet`

Optional `minFriendliness` (≥0; default 5). Sets animal-pet/`default` allow when friendliness > threshold.

#### `prosequor:set-true`

No params. Surfaces: harvest-skep|harvest-bloomery / `allow-right-click-harvest`; recipe-available / `default`.

#### `prosequor:add-friendliness`

`base` (≥0). Adds to friendliness gain on animal-pet/`default` (fold unchanged).

#### `prosequor:restore-consumed-bait`

No params. Surface: consume-bait/`restock`.

#### `prosequor:restock-last-bait`

Optional `amount` (≥1; default 1). Surface: consume-bait/`restock`.

#### `prosequor:refund-ingredients`

| Param | |
| --- | --- |
| `match` | Collection id |
| `amount` | ≥ 0 |
| `retain` | ≥ 0 |

Surface: mutate-output/`refund`.

### Contract: stack

#### `prosequor:increase-freshness`

NumberSpec. Surfaces: mutate-drops/`stack` (block + item).

#### `prosequor:upgrade-ore-grade`

No params. Surface: mutate-drops/`stack` (block).

#### `prosequor:upgrade-hide-size`

No params. Surface: mutate-drops/`stack` (entity). Advances `hide-{process}-{size}` one step on the ladder small → medium → large → huge. Species pelts and already-huge hides are unchanged.

#### `prosequor:replace-matching-stack-with-block`

No params. Surface: mutate-drops/`stack` (block).

#### `prosequor:modify-attribute`

`key` + NumberSpec. Surface: mutate-output/`attributes`.

Known craft attribute keys: `durability`, `warmth`, `cooling`, `protection`, `freshness`, `satiety`, `hungerDelay`, `intoxication`, `price`, `regen`, `flight`, `rangedAcc`.

#### `prosequor:add-affix`

`{ "code", "lang", "color"? }` or `{ "list", "item" }`. Surface: mutate-output/`attributes`.

#### `prosequor:quality`

| Param | |
| --- | --- |
| `key` | Attribute key |
| `op` | Quality op |
| `table` | Value table |
| `affixes` / `bonus` / `affixRange` | Optional |

Surface: apply-quality/`attributes`.

#### `prosequor:quality-rank`

| Param | |
| --- | --- |
| `key` | `qualityRank` or `rank` |
| `table` | Rank table |
| `bonus` | Optional |

Surface: apply-quality/`attributes`.

### Contract: stacks

#### `prosequor:append-from-drop-table`

Optional `table` (pool id); optional `rolls` / `quantity` int-ranges. Surfaces: mutate-drops/`stacks` (block + item). Omit `table` → ambient drop table.

#### `prosequor:replace-from-drop-table`

Same as append + optional `rule` (`first` \| `last` \| `random`, default `first`).

#### `prosequor:replace-with-variant`

`variant`. Surface: mutate-process/`stacks`.

#### `prosequor:enrich-soil`

`maxFertility` (≥0). Surface: mutate-drops/`stacks` (block).

#### `prosequor:add-crop-seed`

No params. Surface: mutate-drops/`stacks` (block).

### Gates

Preserve the outer phase’s contract. Nested `onSuccess` / `onFailure` must bind on the same surface.

#### `prosequor:chance`

| Param | |
| --- | --- |
| `chance` | NumberSpec add-only (unit probability), or `{ "percent": N }` / bare 0–100 |
| `onSuccess` / `onFailure` | Nested `{ "action", "params" }` |

Surfaces: mutate-drops quantity/stack/stacks (block + item); durability `amount`; consume-bait/`restock`; mutate-process/`stacks`; mutate-output/`refund`; animal-pet/`default`.

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

| Param | Surfaces |
| --- | --- |
| `unlock` (node id), optional `skill`, `onSuccess` / `onFailure` | block mutate-drops quantity/stack/stacks |

---

## XP rules

Set exactly one of `amount` or `rate`. No `skill` field — ownership is the enclosing skill (or contribution `skill`).

| Field | Meaning |
| --- | --- |
| `id` | Rule id (merge key) |
| `amount` | Discrete grant: number, or number array (measure table) |
| `rate` | XP per game-second while matched |
| `pay` | Amount channel only (omit → `flat`) |
| `include` | Codes / `<collections>` kept for quantity (outputs) or ingredients (inputs); requires `pay: quantity` or `pay: ingredients` |
| `exclude` | Codes / `<collections>` omitted from quantity; requires `pay: quantity` |
| `payee` | Who is paid (amount only; omit → `user`) |
| `when` | Match filter |
| `priority` | Tie-break after specificity (rule root, not under `when`) |

```json
{
  "id": "prosequor:dig-digging",
  "amount": 1,
  "pay": "resistance",
  "when": { "activity": "prosequor:deed", "tags": ["block-broken", "caller:<shovel>"] }
}
```

```json
{
  "id": "prosequor:pan-effort",
  "rate": 0.01,
  "when": { "activity": "prosequor:effort", "tags": ["interacting", "caller:<pan>"] }
}
```

`amount` arrays require a measure channel (`resistance` / `effort`, `voxels`, `ingredients`, or `lifetime`) and piecewise-lerp across that channel’s domain. Ingredient lerp domain is always 1–40. Lifetime lerps against the live crop-growth-days catalog, then divides by the crop’s `GrowthStages`. For `hunted` / `trapped` deeds, `pay: effort` lerps against the GameReady animal-weight catalog (authored entity `weight` / `weightByType` min–max).

### XP `when`

| Field | Required | Meaning |
| --- | --- | --- |
| `activity` | yes | Activity id (bare → `game:`) |
| `tags` | no | Same criteria language as ability `when.tags` |

All set conditions AND. Among matching rules for a skill, one winner: identity criteria beat collections; more criteria beat fewer; then `priority`; then source order.

### Activities

| Activity | Kind | Meaning |
| --- | --- | --- |
| `prosequor:effort` | rate | Continuous effort samples |
| `prosequor:deed` | amount | Discrete completion |
| `prosequor:collect-xp-item` | amount | Stamped collectible flush (`pay: quantity`) |
| `game:…` / `<modid>:…` | either | Other registered activities |

### `pay` / `payee` / `include` / `exclude`

| `pay` | Effect |
| --- | --- |
| `flat` | Pay `amount` once (default) |
| `resistance` | Lerp `amount` table against a float measure (block resistance, animal weight, or an explicit 0–1 emit range) |
| `effort` | Alias for `resistance` (same channel) |
| `voxels` | Lerp `amount` table against clay voxels-per-unit on the fired piece |
| `quantity` | Multiply by produced units on the emit (drops, crafts, contents, …). Not used for clay-form / smith voxel pulses — those are flat per emit |
| `ingredients` | Lerp `amount` table against recipe ingredient units (1–40) |
| `lifetime` | Lerp `amount` table against crop growth days (catalog min–max), then divide by `GrowthStages` |

| `payee` | Who receives the grant |
| --- | --- |
| `user` | Emitting / acting player (default) |
| `maker` | `makerUid` on the emit |
| `contributor` | `selectedContributorUid` on the emit |
| `contributors` | Weighted `contributors` bag on the emit |

`include` keeps only matching codes / `<collections>` when summing quantity (output units) or ingredients (input units). `exclude` then omits matching codes from quantity. `when.tags` still match the deed subject and are not used for these list filters.

`pay`, `payee`, `include`, and `exclude` are invalid on rate rules. `include` requires `pay: quantity` or `pay: ingredients`. `exclude` requires `pay: quantity`.

---

## Collections

Path: `collections.json` or `collections/*.json` (JSON array).

```json
[
  {
    "id": "clothing",
    "includes": ["game:clothes-*"]
  },
  {
    "id": "wearable",
    "unions": ["clothing", "armor"]
  }
]
```

| Field | Meaning |
| --- | --- |
| `id` | Collection key (used as `<id>` in `when.tags`) |
| `dependsOn` | Mod gates: `{ "modid", "invert"? }[]`. Unmet rows are skipped |
| `includes` | Patterns / codes expanded against all blocks and items |
| `excludes` | Other collection ids subtracted after includes, before unions |
| `unions` | Other collection ids copied into this key (multi-pass, after excludes) |

Pool ids share the same key space. Authored rows and pool ids are the membership for code-pattern collections. After patterns expand, classifiers still add to declared ids: `soil`, `dirt`, `wood`, `leaves`, `stone`, `crop`, `mature-crop`, `immature-crop`, `small-fish`, `medium-fish`, `large-fish`, `metal-crafts`, `clay-formed`, and `smithing-formed`. `excludes` run after those adds. `unions` run after excludes, and again after `clay-formed` / `smithing-formed` recipe output is copied in.

---

## Pools

Path: `pools.json` or `pools/*.json` (JSON array). Domain of `id` must match the asset domain. Last-win by id.

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

| Field | Meaning |
| --- | --- |
| `id` | Namespaced pool id (also a collection key) |
| `entries[].code` | Item code |
| `entries[].weight` | Pick weight (default `1`) |

Used as `params.table` for `append-from-drop-table` / `replace-from-drop-table`. Omit `table` to use the ambient drop table.

---

## Affix lists

Path: `affixes.json` or `affixes/*.json` (JSON array). Domain of `id` must match the asset domain. Last-win by id.

```json
[
  {
    "id": "prosequor:durability",
    "entries": [
      { "code": "sturdy", "lang": "prosequor:affix-sturdy", "color": "#84ff84" }
    ]
  }
]
```

| Field | Meaning |
| --- | --- |
| `id` | Namespaced list id |
| `entries[].code` | Stamp dedupe key |
| `entries[].lang` | Lang key |
| `entries[].color` | Optional richtext color |

`prosequor:add-affix` references with `{ "list": "prosequor:durability", "item": 0 }` or `{ "list": "…", "item": "sturdy" }`.

---

## Attribute stats

Path: `stats/<id>.json` (one object per file). Same effect envelope as skills, gated by attribute score. No `replicate`.

```json
{
  "id": "constitution",
  "rules": [
    {
      "id": "prosequor:con-health",
      "minScore": 0,
      "hook": "prosequor:player-interaction",
      "verb": "prosequor:health",
      "action": "prosequor:add-mapped-number",
      "params": {
        "fromScore": 0,
        "fromValue": -5,
        "toScore": 18,
        "toValue": 5,
        "round": "ceil"
      }
    }
  ]
}
```

| Field | Meaning |
| --- | --- |
| `id` | Known attribute id (`strength`, `perception`, `constitution`, `inconspicuity`, `resilience`) |
| `rules[].id` | Optional rule id |
| `rules[].minScore` | Inactive below this score (default `0`) |
| `rules[].maxScore` | Optional inclusive upper gate |
| `rules[]` effect fields | Same as skill effects (`hook` / `verb` / `phase` / `action` / `when` / `params` / `priority`) |

---

## Trait attributes

Path: `trait-attributes.json` (JSON array). Maps vanilla class traits to attribute score deltas.

```json
[
  { "code": "soldier", "attributes": { "strength": 2 } },
  { "code": "bowyer", "attributes": {} },
  { "code": "technical", "attributes": { "resilience": 1 }, "retainTrait": true }
]
```

| Field | Meaning |
| --- | --- |
| `code` | Vanilla trait code |
| `attributes` | Map of attribute id → score delta. Empty `{}` = flavor / crafting gate only |
| `retainTrait` | If `true`, keep the trait on the class and clear only its vanilla `Entity.Stats` bag |

Valid attribute keys: `strength`, `perception`, `constitution`, `inconspicuity`, `resilience`. Last-win by `code`.

---

## Level-ups

Path: `level-ups.json` or `level-ups/*.json`.

```json
{
  "rules": [
    {
      "id": "prosequor:earn-skill-point",
      "every": 1,
      "action": "prosequor:earn-skill-point"
    }
  ]
}
```

| Field | Meaning |
| --- | --- |
| `rules[].id` | Rule id (merge / disable key) |
| `rules[].every` | Fire when player level `L % N == 0`. XOR with `levels` |
| `rules[].levels` | Exact player levels. XOR with `every` |
| `rules[].action` | Grant action |
| `rules[].params` | Action params |
| `rules[].priority` | Order |

| Action | Params |
| --- | --- |
| `prosequor:earn-skill-point` | Optional `value` (default 1) — add unlock points |
| `prosequor:earn-specialization-point` | Optional `value` (default 1) — specialization slot capacity |
| `prosequor:earn-attribute` | `key`: attribute id → add `value` to score (cap 18); or `key: "buckets"` → soft-reset growth `value` times. Optional `value` (default 1) |

---

## Contributions

Path: `contributions/<anything>.json` (JSON **array**). Merged in asset order (domain, then path).

1. Depend on `prosequor` in `modinfo.json`.
2. Ship lang and icons for contributed nodes.

Contributed node ids must be `<yourmodid>:localId`. Prerequisites may reference existing nodes by plain id.

| Field | Required | Meaning |
| --- | --- | --- |
| `skill` | yes* | Target skill id (*optional if the entry is only `levelUps`) |
| `dependsOn` | no | `{ "modid", "invert"? }[]` — skip whole entry if unmet |
| `disable` | no | `"all"` to remove the entire skill, or an array of node ids to strip first |
| `xpRules` | no | XP rules to merge (last-win by rule `id`) |
| `nodes` | no | Nodes to graft (same shape as skill nodes + optional `replaces`) |
| `levelUps` | no | `{ "disable"?, "rules"? }` grafts onto level-up rules |

Processing order per entry: `dependsOn` → `disable` → `xpRules` → each `nodes` entry (`replaces` then append). `"disable": "all"` stops further skill ops for that entry.

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
        "amount": 1,
        "when": { "activity": "prosequor:deed", "tags": ["block-broken"] }
      }
    ]
  }
]
```

Omit `disable` and `replaces` to append nodes via `requires`. Unmet prerequisites are deferred across contribution files; still missing after all files → skipped with a warning.
