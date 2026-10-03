# Modding Prosequor

JSON says what matches and what changes. Code only reports that a moment happened, or reads the result. Every field is listed in [reference.md](reference.md). This page is the order of work.

Put files under your own domain. Prosequor scans every loaded mod:

```
assets/<yourmodid>/config/prosequor/…
```

Files are strict JSON. Ship the same files on client and server. The two sides hash the compiled content and warn the player if they differ.

In `modinfo.json`:

```json
"dependencies": {
  "prosequor": "1.0.1"
}
```

Bare `hook`, `verb`, `action`, and lang keys are prefixed with `prosequor:`. Icon paths are not: a path without a domain is loaded from Prosequor. Use `yourmodid:textures/…` for your own icons.

## Contents

- [Build a skill](#build-a-skill)
- [Fit existing content](#fit-existing-content)
  - [Make your codes match a tag](#make-your-codes-match-a-tag)
  - [Add XP or a node to someone else's skill](#add-xp-or-a-node-to-someone-elses-skill)
  - [Only when another mod is loaded](#only-when-another-mod-is-loaded)
  - [A skill only some classes should see](#a-skill-only-some-classes-should-see)
- [Leave the traits tab up](#leave-the-traits-tab-up)
- [Call it from a code mod](#call-it-from-a-code-mod)
  - [Report a moment](#report-a-moment)
  - [Read progress](#read-progress)
  - [Fold a number at a moment you own](#fold-a-number-at-a-moment-you-own)
  - [Register an action](#register-an-action)

---

## Build a skill

1. Add `skills/<id>.json`. One object. The file’s `id` is the skill id.
2. Give it a name, a short description, and an icon.
3. Add `xpRules` for what pays it. Match a token Prosequor already emits (`block-broken`, `harvested`, `interacting`, …). A token nobody emits never pays. The list is in [reference.md](reference.md) (Deed tokens, Effort tokens).
4. Add root `effects` for a bonus that is always on, and `tree.nodes` for bonuses the player buys.
5. Add lang entries and icons for every node.

Leave `maxLevel` out. The cap follows the shape of the file: `"hobby": true` is a hobby; a tree with any `"specialization": true` node is a specialization; any other tree is a minor skill; no tree is a passive.

`requires` is AND across entries and OR inside a nested array. `"requires": ["a", "b"]` needs both. `"requires": [["a", "b"], "c"]` needs (a or b) and c. One-sided `excludes` is made mutual when the skill compiles.

Only the tier the player owns is active. Later tiers do not stack on earlier ones. On tier 2 and up, `"replicate": 0` copies effect 0 from the previous tier, then overlays the fields you set.

```json
{
  "id": "fieldcraft",
  "nameLang": "mymod:skill-fieldcraft",
  "descriptionLang": "mymod:skilldesc-fieldcraft",
  "icon": "mymod:textures/icons/fieldcraft.svg",
  "xpRules": [
    {
      "id": "mymod:dig",
      "amount": 0.02,
      "when": {
        "activity": "prosequor:deed",
        "tags": ["block-broken", "caller:<shovel>"]
      }
    }
  ],
  "effects": [
    {
      "hook": "block-interaction",
      "verb": "interaction-speed",
      "action": "number",
      "when": { "tags": ["caller:<shovel>"] },
      "params": { "op": "add", "base": 0, "perSkillLevel": 0.002, "cap": 0.1 }
    }
  ],
  "tree": {
    "nodes": [
      {
        "id": "keen-eye",
        "nameLang": "mymod:unlock-fieldcraft-keen-eye",
        "descriptionLang": "mymod:unlockdesc-fieldcraft-keen-eye",
        "icon": "mymod:textures/icons/fieldcraft/keen-eye.svg",
        "requires": [],
        "tiers": [
          {
            "effects": [
              {
                "hook": "block-interaction",
                "verb": "mutate-drops",
                "phase": "quantity",
                "action": "number",
                "when": { "tags": ["caller:<shovel>"] },
                "params": { "op": "add", "base": 0.05, "perSkillLevel": 0.01, "cap": 0.2 }
              }
            ]
          }
        ]
      },
      {
        "id": "surveyor",
        "nameLang": "mymod:unlock-fieldcraft-surveyor",
        "descriptionLang": "mymod:unlockdesc-fieldcraft-surveyor",
        "icon": "mymod:textures/icons/fieldcraft/surveyor.svg",
        "requires": ["keen-eye"],
        "minSkillLevel": 10,
        "tiers": [
          {
            "effects": [
              {
                "hook": "block-interaction",
                "verb": "mutate-drops",
                "phase": "quantity",
                "action": "number",
                "when": { "tags": ["caller:<shovel>", "target:<ore>"] },
                "params": { "op": "add", "base": 0.1, "cap": 0.1 }
              }
            ]
          },
          {
            "minSkillLevel": 25,
            "effects": [
              { "replicate": 0, "params": { "base": 0.2, "cap": 0.2 } }
            ]
          }
        ]
      }
    ]
  }
}
```

`assets/mymod/lang/en.json`:

```json
{
  "skill-fieldcraft": "Fieldcraft",
  "skilldesc-fieldcraft": "Read the ground.",
  "unlock-fieldcraft-keen-eye": "Keen eye",
  "unlockdesc-fieldcraft-keen-eye": "Loose soil gives up a little more.",
  "unlock-fieldcraft-surveyor": "Surveyor",
  "unlockdesc-fieldcraft-surveyor": "Ore seams give up more."
}
```

Lang keys in JSON may be `mymod:skill-fieldcraft` or, if you omit the domain, `prosequor:skill-fieldcraft`. Prefer the domain form so the string lives in your lang file.

An effect runs only when some existing moment already calls that hook and verb. Pick the address from [reference.md](reference.md) (Hooks, Actions). `when.tags` are AND. `<shovel>` means “the caller is in the shovel collection.”

Leave `layout` off until a node sits in the wrong cell. The grid rules are in [reference.md](reference.md) (Layout).

---

## Fit existing content

Do this when your blocks, items, or activities should count for a skill that already exists. Do not ship a second `skills/<id>.json` with that skill’s id. A second file replaces the whole skill.

### Make your codes match a tag

Rules talk about collections (`caller:<shovel>`, `target:<crop>`), not individual codes. Add your codes to a collection. Rows that share an id are combined: your `includes` are appended.

`assets/mymod/config/prosequor/collections/field.json`:

```json
[
  { "id": "shovel", "includes": ["mymod:spade-*"] }
]
```

Patterns expand when the world is ready, so the codes only need to exist by then. Several built-in collections (`crop`, `wood`, `stone`, `ore`, and others) are also filled by classifiers. A block that is already a crop is already in `<crop>`.

A drop table is a pool, and the pool id is also a collection key. Entries that share a pool id are merged; the same item code keeps the later weight.

```json
[
  {
    "id": "mymod:prospect-finds",
    "entries": [
      { "code": "game:ore-poor-nativecopper-quartz", "weight": 2 }
    ]
  }
]
```

The id’s domain must be your mod id.

### Add XP or a node to someone else’s skill

Use a contribution. The file is a JSON array.

`assets/mymod/config/prosequor/contributions/field.json`:

```json
[
  {
    "skill": "farming",
    "xpRules": [
      {
        "id": "mymod:harvest-reed",
        "amount": 0.02,
        "when": {
          "activity": "prosequor:deed",
          "tags": ["harvested", "target:mymod:reedplot"]
        }
      }
    ],
    "nodes": [
      {
        "id": "mymod:reed-keeper",
        "nameLang": "mymod:unlock-reed-keeper",
        "descriptionLang": "mymod:unlockdesc-reed-keeper",
        "icon": "mymod:textures/icons/reed-keeper.svg",
        "requires": ["greenthumb"],
        "tiers": [
          {
            "effects": [
              {
                "hook": "block-interaction",
                "verb": "mutate-drops",
                "phase": "quantity",
                "action": "number",
                "when": { "tags": ["target:mymod:reedplot"] },
                "params": { "op": "add", "base": 0.1, "cap": 0.1 }
              }
            ]
          }
        ]
      }
    ]
  }
]
```

Contributed node ids must be `<yourmodid>:<localId>`. `requires` may name a node already on that skill by its plain id. Ship the lang lines and the icon. A node whose requirements are still missing after every contribution file is skipped.

`"disable": ["nodename"]` strips those nodes first. `"disable": "all"` removes the skill. `"replaces": "oldnode"` removes that node, retargets other nodes’ `requires` and `excludes` onto yours, and then appends. It does not copy layout.

### Only when another mod is loaded

`dependsOn` skips that collection row or contribution entry unless every clause is met. `"invert": true` skips it when the mod is loaded.

```json
{ "dependsOn": [{ "modid": "someothermod" }] }
```

### A skill only some classes should see

`"optional": true` on the skill keeps it out of the set every class shares. Grant it from a trait row in your own `trait-attributes.json`. The same `code` replaces the whole row, so copy the attribute deltas you still want:

```json
[
  { "code": "forager", "attributes": { "perception": 1 }, "skills": ["fieldcraft"] }
]
```

---

## Leave the traits tab up

Prosequor hides the character screen's traits tab. The tab's compose method stays in place, so another mod can still patch it. To keep the button visible, ship an options file.

`assets/mymod/config/prosequor/options.json`:

```json
{ "traitsTab": true }
```

Any file that sets `traitsTab` to true leaves the tab up. `false`, or leaving the key out, does not take it down. `dependsOn` skips that file unless those mods are loaded.

The player can also turn the tab on in `ModConfig/prosequor/client.json`:

```json
{ "ShowVanillaTraitsTab": true }
```

The tab stays up when that setting is true or any applied options file sets `traitsTab` to true. Prosequor's own tab is Status. It lists traits only while the vanilla traits tab is hidden.

---

## Call it from a code mod

Reference `prosequor.dll` with `Private` set to false, the same way you reference the game API. Look the mod up on the API you are running. Singleplayer has a client instance and a server instance.

```csharp
ProsequorModSystem? mod = ProsequorModSystem.For(api);
```

Emits and purchases are server-only. If `mod` is null, Prosequor is not loaded on that side.

### Report a moment

Do not call `AddSkillXp`. Emit a fact. A rule you wrote (on your skill, or in a contribution) decides whether anyone is paid. An emit with no matching rule pays nothing.

One-shot completion (break, craft take, harvest):

```csharp
if (api.Side != EnumAppSide.Server || player.PlayerUID == null)
{
    return;
}

ProsequorModSystem? mod = ProsequorModSystem.For(api);
mod?.EmitDeed(
    player.PlayerUID,
    new[] { "marked" },
    caller: player.InventoryManager.ActiveHotbarSlot?.Itemstack?.Collectible?.Code?.ToString(),
    target: "mymod:survey-stake");
```

Match it from the skill that should be paid:

```json
{
  "id": "mymod:mark-stake",
  "amount": 1,
  "when": { "activity": "prosequor:deed", "tags": ["marked", "target:mymod:survey-stake"] }
}
```

Prefer a `DeedToken` when the moment is one Prosequor already names (`BlockBroken`, `Harvested`, `Crafted`, …). Use a string token for a moment that is yours. The activity stays `prosequor:deed`.

The game already pulses (a held interact, a pour tick): `mod.EmitEffort(player, new[] { "sounding" }, target: code)`. Match `activity` `prosequor:effort` and use `rate` instead of `amount`.

Nothing pulses (the player is simply in a state): register a poll once, and return null while it is inactive.

```csharp
mod.RegisterEffortPoll("mymod:sounding", (player, entity) =>
    IsSounding(player)
        ? new EffortPollResult(new[] { "sounding" })
        : null);
```

Put `caller`, `target`, and the token on the emit. The rule chooses who is paid. Do not fold the maker into the contributor list.

### Read progress

```csharp
IPlayerProgress? progress = ProsequorModSystem.GetProgress(player);
if (progress == null)
{
    return;
}

int level = progress.GetSkillLevel("fieldcraft");
bool owned = progress.HasUnlock("fieldcraft", "keen-eye");
int tier = progress.GetUnlockTier("fieldcraft", "surveyor");
int strength = progress.GetAttribute("strength");
```

`GetProgress(api, playerUid)` falls back to the parked copy when that player is offline this session.

Use `HasUnlock` for a gate an effect cannot express (a recipe that must not exist, a block that must not place). Bonuses that change a number, a stack, or a chance belong in JSON on a hook the game already runs. You do not call those effects yourself.

`AddSkillXp`, `TryPurchaseNode`, and the other writes are server-only. Gameplay code should not use them to pay the player.

### Fold a number at a moment you own

If the moment is one Prosequor already runs (breaks, crafts, interacts, damage), stop at the JSON effect. The fold happens without you.

If the moment is new and a skill bonus must change a number there, build the context for that hook and call the pipeline. The seed is the vanilla value; the return value replaces it.

```csharp
ProsequorModSystem? mod = ProsequorModSystem.For(player.Entity.Api);
IPlayerProgress? progress = ProsequorModSystem.GetProgress(player);
if (mod?.Pipeline == null || progress == null)
{
    return baseSpeed;
}

InteractionSpeedContext context = new()
{
    Hook = HookIds.BlockInteraction,
    Player = player,
    Progress = progress,
    Fact = EventFactBuilder.ForPlayer(
        player,
        VerbIds.InteractionSpeed.Value,
        target: blockCode,
        held: heldCode),
    BaseValue = baseSpeed
};

return mod.Pipeline.Run(
    HookIds.BlockInteraction,
    VerbIds.InteractionSpeed,
    HookIds.Default,
    context,
    baseSpeed);
```

The hook, verb, and phase have to be a triple that is already registered, and the context type has to be the one that phase uses. Prosequor's triples are in [reference.md](reference.md) (Hooks).

### Register an action

Do this when a bonus needs behavior Prosequor does not ship, and other mods should name it from JSON. Register in `Start`, on the instance `For` returns. `Start` runs on the client and on the server. Skills compile after every mod's `Start`, so an action registered there is visible to every skill file.

The handler is one hook, one verb, and one phase. Its context type and value type have to be the ones that phase was registered with. Registering the same action on that same triple again is an error.

```csharp
public sealed class SurveyBonusParams
{
    public float Amount { get; init; }
}

public sealed class SurveyBonusAction
    : AbilityActionHandler<InteractionSpeedContext, float, SurveyBonusParams>
{
    public override ActionId Id => new("mymod:survey-bonus");
    public override HookId Hook => HookIds.BlockInteraction;
    public override VerbId Verb => VerbIds.InteractionSpeed;
    public override PhaseId Phase => HookIds.Default;

    protected override bool TryParse(
        JObject? raw,
        out SurveyBonusParams? parameters,
        out string error)
    {
        parameters = new SurveyBonusParams
        {
            Amount = raw?.Value<float?>("amount") ?? 0f
        };
        error = "";
        return true;
    }

    protected override float Apply(
        InteractionSpeedContext context,
        float value,
        SurveyBonusParams parameters,
        AbilityRuleSource source) =>
        value + parameters.Amount;
}
```

```csharp
public override void Start(ICoreAPI api)
{
    ProsequorModSystem? mod = ProsequorModSystem.For(api);
    mod?.Actions.Register(new SurveyBonusAction());
}
```

Another mod names the id as written. A bare action id is read as `prosequor:`, so keep your domain on it.

```json
{
  "hook": "block-interaction",
  "verb": "interaction-speed",
  "action": "mymod:survey-bonus",
  "params": { "amount": 0.05 }
}
```

The effect runs when that hook and verb already run. You do not call the action yourself.

A hook or phase Prosequor has not registered has to be declared on `mod.Hooks` before the action. `RegisterPhase` takes the context type and the value type. Call `Pipeline.Run` at that moment, the same way as the fold above. Until something does, JSON that names the hook never runs.

If the mod that registered the action is not loaded, that effect list fails to compile. A failed root list drops the skill's root effects. A failed tree is dropped, and the skill stays without it.
