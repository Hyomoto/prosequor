# Prosequor follow-ups

Sourced from ModDB comments around 2026-09-15. Items already shipped or declined are listed at the bottom so this file stays a work list, not a comment archive.

## Bugs

- [x] **Hammer Care scales off Mining.** The metalworking node’s chance uses `"skill": "mining"`, and `unlockdesc-metalworking-hammer-care` says “per Mining level”. Fix the JSON skill id and the lang string so both use Metalworking.
- [x] **Breaking a skep for honey crashes with no useful log.** JesseSpot on rc.10, still after removing Trait Acquirer. Cause: `SkepBeeSpawnPatch` called `Block.OnBlockBroken` via reflection, which still callvirt’d into the patched `BlockSkep` override (stack overflow). Fixed with a non-virtual `DynamicMethod` base call; also patch `BlockSkep.GetDrops` like coating so Sticky Fingers / harvest XP run. Scenarios: break + plain RMB + Apiary Master sneak-RMB.



## Compatibility

- [x] ~~**Trait Acquirer (Modded Classes) GUI crash.** New Trait Acquirer + “all traits from all classes” crashes the C-menu / Skills tab; the older Trait Acquirer does not. Prosequor should not crash when another mod rewrites the traits tab or inflates the trait list. Same family as the old Gourmand nutrition crash.~~ **hard incompatibility, no fix**
- [ ] **Aldi’s Classes traits are not folded into attributes.** `trait-attributes.json` only maps vanilla codes. Aldi’s extra class traits stay as vanilla `Entity.Stats` traits. Decide whether to ship mappings (or a contribution pack) so those classes get attribute scores the same way vanilla classes do.
- [ ] **Yang’s Character Creator.** Class + extra-trait assignment happens outside vanilla `DidSelect`. `TryApplyOnSelection` already folds `extraTraits`, but only once (`prosequorTraitAttributesApplied`). Verify Yang’s apply order; re-run distribution when class/extra traits are assigned after first join.
- [ ] **Smithing Plus bit-smithing is gated by Bits Forging.** SP’s free bits path is overwritten by the metalworking unlock. Either detect SP and stand down, or document “disable bits in SP”. Metal Recovery also overlaps SP refunds (keep both only if that generosity is intended).
- [ ] **Toolsmith (mod) tools via toolheads.** Durability folds hook `CollectibleObject.DamageItem`. Confirm whether Toolsmith toolheads still go through that path (speed, care, quality). Add behavioral hooks if it co-opts the pipeline.
- [ ] **Stats Breakdown (TealSeer).** Traits tab now shows qualitative walk speed / hunger rate / etc. Stats Breakdown still needs a coexistence pass so both can show numbers without fighting the C-menu.
- [ ] **Hydrate or Diedrate satiety overlay.** HoD draws a second bar that assumes max satiety is a multiple of 100. Prosequor’s constitution satiety breaks the pip math. The calculation bug is in HoD; track until they patch, or document it as known.
- [ ] **Seraph Levelling.** Traits added after first class apply are not converted to attributes. Confirm the leftover traits still display and stack safely, or document the mismatch.
- [ ] **Wulks Smithing Addons log T0 anvil.** Anvil XP is wired for `BlockEntityAnvil`. Check whether that anvil uses a different BE and pays nothing.



## Wishlist

- [ ] **Respec.** Admins have `/prosequor revoke` (one node) and `/prosequor clear` (full wipe). No player- or admin-facing respec that refunds a tree or redistributes attributes.
- [ ] **Force class-attribute distribution.** Mid-save join already applies once. Servers that installed onto an existing world still want a command to re-run it (or to apply after Yang/Aldi changes) without wiping XP.
- [ ] **Public authoring docs (C# surface).** JSON schema and contributions are in `docs/reference.md`. Injury Expanded / Logging Expanded authors still need the C# station/hook primer that was described as in progress.
- [ ] **Combat skill tree.** Requested; currently out of vanilla scope (crafting + attributes cover the gap). Revisit only if we are willing to invent combat mechanics.
- [ ] **Builder profession.** Requested with CollapseStory in mind. Same stance: vanilla building is mining/digging/forestry; a builder tree belongs in a compatibility or third-party contribution.



## Already addressed (not work)

- Level-up / skill-up volume. Clip gain defaults to 50 in `ModConfig/prosequor/client.json`. Integrated Mod Manager exposes the slider from `config/imm.json`. 0 skips the stinger.
- XP awarding healed the player (cattails / any XP). Constitution health preservation used vanilla max instead of the pre-change pool. Fix + scenarios are in tree (`PlayerInteractionAbilityPatches`, `FatherXp` pay-only, `XpHealthSideEffectScenarios`). Swodobr confirmed.
- Existing-world join crash / “what it means to join.” `AdmitInitialized` on init, `PlayerJoin`, `PlayerNowPlaying`, and host catch-up. Class scores apply from the cached profile when `DidSelect` never fires.
- Gourmand C-menu crash when another mod injects nutrition bars. Stats panel adopts extra `ComposeExtraGuis` bars instead of assuming a fixed nutrition block.
- Metalworking XP for anvils, crucibles, and molds (`AnvilXpStation`, `MoldCastXpStation`).
- Walk speed / hunger rate (and a few other blended stats) on the Traits tab as qualitative lines. Precise numeric accounting is still Stats Breakdown, above.
- Attribute leveling is documented in the handbook: work fills buckets; player-level milestones raise the most-exercised attribute. Stats panel ticks show proximity. `/prosequor addbucket` / `setattr` exist for admins.
- Immersive Mining: Prosequor patches vanilla mining speed; IM turns that into block damage, not animation speed. “Fully compatible” means no conflict, not extra hits on every block.
- Magic-mod aux trees: no in-house plans; other authors can contribute.
- xSkills world migration: not a Prosequor feature.

