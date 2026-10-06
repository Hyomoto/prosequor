::: lead
Prosequor adds skills, attributes, and features to the things you already do in Vintage Story.  It is built to feel like a natural extension of the vanilla game: you farm, forge, explore, hunt, and build as usual, only now your character also develops from that work.  It is designed for long-running multiplayer worlds but works in singleplayer too.
:::

::: notice
![Tested with Atlas](https://raw.githubusercontent.com/Pixnop/Atlas/main/docs/assets/badges/tested-with-atlas-seal-sepia.png)

**This is a release candidate.** It is feature complete and should be stable, but balance and compatibility issues are still possible. It can be added to or removed from an in-progress game; see [Removing Prosequor](#removing-prosequor) for caveats.
:::

## What changes when I play?

You have skills which gain experience from myriad activities, and provide passive benefits as they grow.  These levels feed into your player level which grants you skill points that let you buy new abilities from their trees.  These include improvements to familiar work, while others open up new features like tracking an animal, crafting quality, or improved tool use.  Your character also has five base attributes that largely take the place of vanilla's trait buffs and debuffs, they provide passive effects and develop depending on how you level your skills.

Prosequor is not a mod about becoming a demigod, it's about making labor distinctions by providing a satisfying progression path to differentiate yourself from other characters.  Power does extend past vanilla, but you won't be running 200 km/h or duplicating magical artifacts.  It is firmly rooted in the vanilla experience.

## What's included?

- **21 skills:** Eight specializations (Digging, Farming, Forestry, Husbandry, Hunting, Metalworking, Mining, and Tailoring), eight smaller skill trees (Clayforming, Construction, Cooking, Fishing, Medicine, Panning, Riding, and Seamanship), and five passive skills (Adaptation, Athletics, Forager, Sneaking, and Swimming).
- **Five attributes:** Strength affects melee, breaking blocks, and carrying space; Perception affects ranged weapons and low-light vision; Constitution affects hunger and weather tolerance; Inconspicuity affects how animals notice you; Resilience affects health and falls.
- **Crafting quality:** Supported crafts can receive a quality grade and useful properties, such as warmer clothing, more durable tools, or stronger spirits. Skill and unlocks influence the results. Item tooltips show these qualities alongside practical stats.
- **Animals that react to you:** Wildlife builds alert from your presence and movement. Animals you care for can grow friendlier and become less fearful and may even favor a particular player.
- **A clearer character and item interface:** Skill trees, attribute growth, level-up notices, and redesigned tooltips make progression and crafted items easier to inspect.

## How does progression work?

Doing things gains xp which causes your skills to level, when your skills level up you gain player XP.  Player levels grant skill points, and sustained work in a skill can grant additional points. Specialization skills can reach level 100; minor and passive skills can reach level 50.

Skill levels also contribute toward attributes. At certain player levels, the attribute with the most accumulated growth gains a point. You can see that growth in the character screen, so your stats reflect what you have spent time doing.

## Playing together

Prosequor keeps track of who created, grew, or prepared things, including items that can be stacked together when their credit matches. Shared work can credit contributors for XP: tending crops and animals, using kilns, and other activities do not depend only on who makes the final click. XP from some work that finishes while you are away is awarded when you return, so you are not penalized for logging out.

The progression pace is aimed at servers where people play together over time and develop different strengths. You can play alone with all the same skills and systems.  It is not expected that a player will ever 'max out' a character, and by default it isn't even possible.

## The XP Bucket System

Each skill fills a 'bucket' whenever it gains XP, when the bucket gets full a penalty is applied to XP gain and it starts over again.  If you keep grinding out a skill, this value will eventually just reach zero.  To recover from this, you simply need to take time doing something else.  This is tied to the in-game clock, so sleeping will refresh your buckets.  On a server, however, logging off, roleplaying or just doing other tasks is the intended solution.

While this may seem like it's designed to prevent you from levelling up, the design is intended to prevent players from feeling grinding is necessary.  The goal of Prosequor is not to be your primary task, I do not want to incentivize performing activities solely for the sake of gaining XP.  I want you to play Vintage Story and enjoy watching your skills and attributes grow over time, so that when your server has been going for a few weeks you still have goals to work towards and the other players naturally grow in different ways.  This also helps, somewhat, to bridge the gap between someone who is always on vice someone who may have more limited time.  The first person is always going to advance more quickly, Prosequor is not designed to stop grinding, but if you want to maximize mining you will need to do more than wear out your pickaxe.

## Modding and Prosequor

Compatibility was a primary concern, but it touches many vanilla interactions to award XP and apply skills.  It's impossible to know how it will interact with other mods, so Prosequor is tested specifically against vanilla.  It can, in some cases, work with other mods depending on how vanilla-aligned they are (for example, using vanilla-styled item codes), but more complex mods are unlikely to just work out of the box.  To that end I have tried to make it very easy for modders and the full spec can be seen [here](https://github.com/Hyomoto/prosequor/blob/main/docs/modding.md)

::: details Collections
A lot of what Prosequor does relies on collections.  These are just groups of items codes it matches against to figure out if rules should apply (see below).  For content mods, if your items aren't automatically registered to a collection, a simple VS patch to add them to relevant collections is usually all that's needed.  collections.json contains them all, and of course mods are free to make their own collections.
:::

::: details Writing rules
Prosequor uses rules that listen for game events, match the relevant item, block, or action, and change the result. Skill effects, unlocks, and attribute effects use this system. Many are defined in JSON, so other mods can patch or extend them without changing Prosequor's code.
:::

::: details Removing Prosequor #removing-prosequor
Most changes can be rolled back, with a few caveats:

- Class traits are converted to character attributes. After removal, reselect your class to restore your vanilla traits.
- Empty any inventory slots granted by Strength before removal; items left there will be lost.
- Changes such as health or satiety may persist until vanilla recalculates them.
:::

## So About the Name
**Proh-seh-kwor.** As in *pro sequor*.  We don't know how the Romans would have said it, but they aren't around to ask.  It is a Latin root behind words such as *pursue* and *prosecute* and combines my two favorite things: naming my mod something people hate, and not calling it Hyomoto's Vintage Story RPG Levelling System.

---

## Compatibility

I wrote a companion mod, [Prosequor+](https://mods.vintagestory.at/show/mod/68666), which provides some baseline compatibility with a handful of popular mods.

It does not add new skills or skill trees — that would be up to the authors if they want to — but it hooks their behaviors into Prosequor's existing pipeline.

::: mods Fully Compatible - Built-in support or no patching needed
Immersive Mining
https://mods.vintagestory.at/immersivemining
by SaltyWater and GamingToast

Barbershop Plus
https://mods.vintagestory.at/show/mod/60672
by Me

Diverse Diets
https://mods.vintagestory.at/diversediets
by Tentharchitect

Real Grapes
https://mods.vintagestory.at/realgrapes
by xXx_Ape_xXx

Knapster
https://mods.vintagestory.at/knapster
by Apache

PlayerModelLib
https://mods.vintagestory.at/playermodellib
by VSCustodian, Maltiez and Caliber

Esoteric
https://mods.vintagestory.at/show/mod/35893
by Stoon

Rift Traveler
https://mods.vintagestory.at/rifttraveler
by T0xx
:::

::: mods Incompatible - Mods with known issues that can't be resolved
Traits Acquirer
https://mods.vintagestory.at/show/mod/36975
— unable to determine a fix; possible UI crashes and skills-menu corruption (critical), replacement of Prosequor's traits screen (annoying).
:::

::: mods Known Issues - Minor issues with workarounds
Floral Zones (various)
— many plants trigger skills and earn xp, but some do not. Partial compatibility.

Gourmand
https://mods.vintagestory.at/show/mod/14390
— nutrition bar has no icon. Otherwise should be fully compatible.

Eco Machina
https://mods.vintagestory.at/ecomachina
— experimental tree growth can skip XP hooks. Leaving it off is fully compatible.

Falling Trees
https://mods.vintagestory.at/fallingtrees
— bypasses several Prosequor felling hooks; partially compatible.

Toolsmith
https://mods.vintagestory.at/toolsmith
— needs investigation, but it is likely tools will skip some or all XP/quality paths.

SmithingPlusPlus
https://mods.vintagestory.at/smithingplusplus
— bit smithing, bit recovery (turn off in config), other conflicts under investigation; both target forging at the anvil.

Hydrate or Diedrate
https://mods.vintagestory.at/hydrateordiedrate
— draws a satiety overlay, but is disjoint with satiety values that aren't multiples of 100. Visual bug, but annoying.

Seraph Levelling
https://mods.vintagestory.at/show/mod/65408
— replaces Prosequor's traits screen; otherwise should be compatible.
:::

## AI Disclosure

While I hope this is not the part that turns you away, AI tools were heavily used. I know that this is important for many people, and so I am disclosing it. I have spent months on this project, and without AI it would not be as polished, tested, performant or complete. AI was used extensively throughout development, including writing code, design mockups and generating test cases.
<br><br>
No AI-generated artwork or audio was used; everything was either authored by me or sourced and credited.
