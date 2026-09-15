This is the **design target** from our discussion. Attribute scores, buckets, level-up grants, and skill-root `attributeScores` → bucket fill are implemented. Effect tables below and the growth-door mapping are still content/design notes — shipped skills may not yet author `attributeScores`.

---

## Attribute effects (design)

| Stat | Super ability (poster, ~14+) | Steady effects (wallpaper) | Below neutral (penalty band) | Explicitly **not** on this stat |
|---|---|---|---|---|
| **Strength** | **Extra carry** — fewer inventory slots “taxed,” less backpack slowdown, or a small number of extra carry slots (not a second toggle backpack) | Melee damage; reduced armor movement penalty; bow draw strength (optional — overlaps Perception) | Weak melee; clumsy armor wear; slower draw | Mining speed, ore/wood drops (Mining/Forestry skills) |
| **Perception** | **Cat eyes** — modest always-on low-light vision; weaker than a night-vision device | Ranged accuracy; ranged damage; bow draw / charge feel | Nearsighted-style ranged penalties | Forage/loot yields (skills); “see everything” at 10 |
| **Constitution** | **Feast & fast** — bigger stomach (`MaxSaturation`) **and** meals count for more satiety (multiplier on intake only, not nutrition/food-HP) | Bonus max HP; climate comfort deadband; poultice / heal item potency | Low HP; hungry faster (`hungerrate`); cold/heat bites harder | Also slowing hunger drain on the high side (avoid triple-dip with stomach + meals) |
| **Inconspicuity** | **Quiet presence** — predators sense you later, commit from closer range less often, give up chases sooner; homesteading wildlife stays calmer (not “deer walk up to you”) | Optional: small **crit / opening hit** chance (hunters + knights envy it; not a full damage stat) | Noisy / threatening — animals notice and commit sooner; heavy-footed feel | Prey flee reduction (unless you explicitly want “quiet hunter”); stacking with Perception on the same life |
| **Resilience** | **Last stand** — once per long cooldown, a killing blow or **fatal fall** leaves you at 1 HP instead of dead | Reduced fall damage on “oops” drops; frost resistance | Fragile under stress — worse fall outcomes, maybe worse healing under strain | Temporal drain/recovery (Adaptation skill); “stand in rifts unharmed” as the main sell |

---

## Jealousy map (why each poster matters)

| If you stack this… | You get jealous of… | Because… |
|---|---|---|
| **Strength** (labor life) | CON lunch, PER dark, RES last stand | You haul more but still eat often, still blind at night, still die on a bad fall |
| **Constitution** (farmer/athlete) | STR pockets, PER cat eyes, INC quiet wilds | You last on one meal but pack tight and the forest still reacts to you |
| **Perception** (hunter) | STR carry, CON tank, RES cheat-death | You see and shoot but carry less lunch and aren’t hard to kill |
| **Inconspicuity** (homesteader/hunter) | STR/CON sustain, PER vision | The world ignores you less in combat than in the yard |
| **Resilience** (fighter/miner customer) | STR pockets, PER vision, CON meals | You survive the hit but live out of a small pack |

---

## Vanilla stat keys (likely hooks when you implement)

| Effect area | Likely `Entity.Stats` / behavior hook |
|---|---|
| Carry / encumbrance | Custom (not vanilla trait today); possibly bag slot rules |
| Melee | `meleeWeaponsDamage` |
| Ranged | `rangedWeaponsAcc`, `rangedWeaponsDamage`, `rangedWeaponsSpeed`, `bowDrawingStrength` |
| Armor move | `armorWalkSpeedAffectedness` |
| Max HP | `maxhealthExtraPoints` |
| Hunger burn (penalty only) | `hungerrate` |
| Satiety tank / meal size | `MaxSaturation` + intake multiplier on `OnEntityReceiveSaturation` |
| Healing items | `healingeffectivness` |
| Climate | `bodyTemperatureResistance` or custom deadband on body temp |
| Fall “oops” | `fallDamageFactor`, `fallDamageThreshold` |
| Last stand | Custom on lethal damage + `OnFallToGround` fatal path |
| Temporal | `EntityBehaviorTemporalStabilityAffected` drain/recover |
| Animal presence | Custom on `animalSeekingRange`, seek range, `maxFollowTime`, revenge bypass |
| Low light | Client cat-eyes post-process (`CatEyesController`); Perception score dial, not `Entity.Stats` |
| Crits | New combat verb (vanilla has no crit table) |

---

## Growth doors (skill → bucket fill)

The runtime fill path is wired: skill-root [`attributeScores`](reference.md#attribute-scores) deposit into attribute buckets on skill level-up (before player XP / grants). Shipped skill JSON may still omit the field until content is authored.

Suggested mapping from our last pass (weights are “doors,” not final balance):

| Skill / life | Feeds primarily | Also touches |
|---|---|---|
| Mining / Digging / Forestry | Strength | — |
| Farming / Athletics / Riding | Constitution | — |
| Hunting / Panning / Fishing | Perception | — |
| Homesteading (future) | Inconspicuity | Constitution? |
| Melee combat (future) | Resilience | Strength |
| Mining (dark stress, optional) | Resilience secondary via other activities, not primary on Mining alone | — |

---

## One-line pitch per stat

- **Strength** — “I carry the world.”
- **Perception** — “I see what others miss.”
- **Constitution** — “One meal lasts me all day.”
- **Inconspicuity** — “The wild doesn’t pick fights with me.”
- **Resilience** — “That should have killed me.”

If you want this turned into a config file (`attributes.json` with poster thresholds and per-point curves), switch to Agent mode and we can scaffold that next without wiring effects yet.