# Commands (`/prosequor`)

Server console / in-game chat. Requires `controlserver`. Online players only (name match is case-insensitive). Omit the player name on `show` / `clear` / `emptybuckets` to target yourself.

| Command | Args | Effect |
|---|---|---|
| `/prosequor show [player]` | optional player | Dump player level/XP/points, attribute scores + buckets, and per-skill level/unlocks |
| `/prosequor setlevel <player> <skill\|player\|self> <level>` | player + target + int | Set overall player level (`player` / `self`) or a skill level (skill id or localized display name) |
| `/prosequor addxp <player> <skill\|player\|self> <amount> [nb\|fb]` | player + target + float + optional flag | Add player XP or skill XP. Skill default Earn is skill-bucketed; player-track awards are unmetered (`nb`/`fb` no-ops there). Skill `nb` = Grant (full commit, no meters). Skill `fb` = GrantAndFill (full commit + fill skill meter) |
| `/prosequor points <player> <amount>` | player + int | Add or subtract unspent unlock points (negative allowed) |
| `/prosequor addbucket <player> <attribute> <amount>` | player + attribute + float | Add growth credit to an attribute bucket (`strength`, `perception`, `constitution`, `inconspicuity`, `resilience`) |
| `/prosequor setattr <player> <attribute> <value>` | player + attribute + int | Set attribute score (clamped 0–18); refreshes that attribute’s player-interaction effects |
| `/prosequor unlock <player> <skill> <code>` | player + skill + node id | Grant a tree unlock (costs points / respects purchase rules when a tree exists) |
| `/prosequor revoke <player> <skill> <code>` | player + skill + node id | Revoke an unlock and refund 1 point |
| `/prosequor clear [player]` | optional player | Full reset: levels, XP, unlocks, attribute scores/buckets |
| `/prosequor emptybuckets [player]` | optional player | Zero XP saturation buckets and pending accrued XP only |
| `/prosequor setfriendliness <value> [entityId]` | looked-at entity or id | Replace friendliness score (admin Set; sentinel contributor) |
| `/prosequor addfriendliness [amount] [entityId]` | looked-at entity or id | Earn friendliness via the real gain path (your UID, cooldown stamp, favorite roll). Default amount 1; skips the wait so you can fire it again |
| `/prosequor finishbarrel` | looked-at block | Finish the targeted sealed barrel immediately (vanilla complete + pedigree / XP) |
| `/prosequor pedigree` | held item, else looked-at entity/block | Dump maker / contributors (player names). Occupied hand inspects that stack; empty hand interrogates the target. `none` if no pedigree, `invalid` if nothing to inspect |

Skill arguments accept the skill id or the caller’s localized display name. Attribute ids are the five stable ids above (case-insensitive).
