# Surface → verb → phase remap

Status: **complete** (historical). The hard cut is done; current authoring truth is [reference.md](../reference.md). Keep this file for decision rationale and the old→new address table.

Audience: authors and implementers reviewing how the address model was chosen.

## Locked decisions

| Decision | Choice |
|---|---|
| Surfaces (hooks) | `block-interaction`, `item-interaction`, `crafting-interaction`, `entity-interaction`, `player-interaction`, `progress` |
| Drops | Verb `prosequor:mutate-drops`; match with `held:` / `target:` / `drop:` (former dig/chop/mine/harvest/fish/catch/pan tokens removed) |
| Process outputs | Verb `prosequor:mutate-process`; tokens `fire-pottery` / `barrel` / `fruit-press` |
| Craft grid | Verb `prosequor:mutate-output`; phases `quantity`, `output`, `attributes`, `refund` |
| Animal pet | Verb `animal-pet`, **no phase** (sentinel `default`); action `prosequor:add-friendliness` |
| Player-stats | Each former phase **hoists to verb**; seeking-range stays player-interaction |
| Response-rate | `entity-interaction` + `animal-flee` / `animal-seek` + phase `chance` |
| Omit phase | Content may omit `phase` → runtime `PhaseId` `"default"` |
| Actions | Existing action ids kept except `add-friendliness`; further thickening is a follow-on ADR |

## Layers

| Layer | Role | VS intuition |
|---|---|---|
| **Hook** | Interaction **surface** | Block / item / crafting / entity / player (+ **progress**) |
| **Verb** | **Moment** on that surface | Former hooks or former fact verbs |
| **Phase** | **Facet** of that moment | Optional; typed fold. Sentinel `default` when omitted |
| **Action** | **Mutation** | Registration key: `(hook, verb, phase)` |

```text
WAS:          Run(hook, phase)           + fact.verb for when-filter
NOW:          Run(hook, verb, phase)     + fact.Verb == verb; classification via tags/tokens
```

Phase still selects where an action behaves — it just lives under the verb.

## Author cheat sheet

```json
{
  "hook": "prosequor:entity-interaction",
  "verb": "prosequor:mounted",
  "phase": "move-speed",
  "action": "prosequor:add-skill-scaled-percent",
  "params": { "base": 0, "perSkillLevel": 0.2, "cap": 10 }
}
```

```json
{
  "hook": "prosequor:block-interaction",
  "verb": "prosequor:mutate-drops",
  "phase": "quantity",
  "action": "prosequor:number",
  "when": { "tags": ["target:<crop>"] },
  "params": { "op": "add", "base": 0.10, "perSkillLevel": 0.005, "cap": 0.50 }
}
```

```json
{
  "hook": "prosequor:entity-interaction",
  "verb": "prosequor:animal-pet",
  "action": "prosequor:add-friendliness",
  "params": { "base": 1 }
}
```

`when.verb` is **removed**. Do not use it.

### Classification (mutate-drops)

Prefer `held:` / `target:` / `drop:`. Former dig/chop/mine/harvest/fish/catch/pan fact tokens are gone.

### Classification tokens (mutate-process)

Tokens: `fire-pottery`, `barrel`, `fruit-press`.

---

## Catalog: hooks today → target

See mapping tables below (same as the design sketch, with drops option 2 locked).

### Surfaces

| New hook | Meaning |
|---|---|
| `prosequor:block-interaction` | Break/harvest/fertilize/field/speed/process |
| `prosequor:item-interaction` | Durability, bait, repair, voxel tool ops |
| `prosequor:crafting-interaction` | Grid craft mutate-output |
| `prosequor:entity-interaction` | Mounted (land + boats), animal AI/pet |
| `prosequor:player-interaction` | Body stats, damage, cat-eyes, seeking-range |
| `prosequor:progress` | Skill XP + bucket cap |

### Block interaction

| Today | New hook | New verb | New phase |
|---|---|---|---|
| `drops` / quantity\|drops\|stacks + dig/… | `block-interaction` | `mutate-drops` | keep pipeline phases; class → tags |
| `interaction-speed` / value | `block-interaction` | `interaction-speed` | `default` |
| `success-chance` / value + verbs | `block-interaction` | *today’s verb* | `default` |
| `growth-duration` / value | `block-interaction` | *plant-* verb | `default` |
| `fertilizer-absorb` / value | `block-interaction` | `fertilize` | `default` |
| `field-area` / size | `block-interaction` | `field-work` | `size` |
| `multi-break` / quantity | `block-interaction` | `scythe-multibreak` | `default` (or keep `quantity`) |
| `on-processed` | `block-interaction` | `mutate-process` | `quantity` → `stacks`; class → tokens |

### Item interaction

| Today | New verb | Phase |
|---|---|---|
| `item-usage` | block-damaged / item-damage / craft-damaged | `default` or `amount` |
| `on-bait` | `consume-bait` | `restock` |
| `on-repair` | `repair` | `add-durability` |
| `voxel-work` | `clay-form` | keep three facets |

### Crafting

| Today | New verb | Phase |
|---|---|---|
| `on-craft` | `mutate-output` | `quantity`, `output`, `attributes`, `refund` |

### Entity

| Today | New verb | Phase |
|---|---|---|
| `mounted` / facets | `mounted` | keep land facets; shared `move-speed` |
| `boating` / facets | `mounted` | `forward-speed` → `move-speed`; keep `turn-speed`, `ratline-stamina`; filter mount kind with `when` |
| animal-behavior chance/multiplier | animal-flee/melee/brood/milk/seek | `chance` / `multiplier` |
| animal-pet interaction/amount | `animal-pet` | `default` + `add-friendliness` / allow-animal-pet |

### Player + progress

| Today | New |
|---|---|
| player-stats / *phase* | player-interaction / verb=*phase* / phase=`default` |
| animal-response-rate | entity-interaction / animal-flee+animal-seek / chance |
| animal-seeking-range | player-interaction / animal-seeking-range / default |
| take-damage, cat-eyes | player-interaction / same verbs |
| skill-xp, skill-bucket | progress / skill-xp, skill-bucket |

- Inconspicuity response-rate uses phase `response` under animal-flee/seek (not `chance`) so it can share the verb with husbandry flee-reduction without fighting seed semantics (chance seed 0 vs ExecutionChance seed).

## Out of scope (this migration)

- Action vocabulary overhaul (`mutate-attribute`, etc.)
- Renaming collections / tag language beyond drops classification tokens
- Long-lived dual runtime
