# ClickDungeon — Implementation Checklist

## Gate 0 — Rules lock ✅
`docs/rules.md` covers visibility, sensing, movement, targeting, turn costs, first contact, traps,
enemy activation, chest semantics, death, floor completion, save boundary.
`docs/decisions.md` records D-001…D-014.

## Gate 1 — Ugly mechanical prototype

Evidence key: **T** = automated test (headless `dotnet test` + Unity EditMode), **B** = Windows build,
**S** = automation screenshot, **—** = not yet verified.

| Definition of Done item | Status | Evidence |
|---|---|---|
| Runs from a clean Unity checkout | ✅ | Batch-mode compile + scene setup, no compiler errors |
| Deterministic 5×5 floor from a known seed | ✅ | T `GenerationIsDeterministic`, `SameSeedAndInputsGiveIdenticalRuns` |
| Legal, predictable movement | ✅ | T `MovementTests` |
| Sense adjacent hidden information | ✅ | T `VisibilityTests` (reveal ≤1, sense =2, truthful clues) |
| Revealed enemies telegraph intent | ✅ | T `RevealedEnemyDeclaresButCannotActThatTurn` |
| First-contact rule | ✅ | T `FirstContactAndTurnOrderTests` |
| Slash / Shield / Dash / Potion | ✅ | T `ShieldTests`, `DashTests`, `PotionTests` |
| Deterministic enemy phase | ✅ | T `EnemiesActInAscendingIdOrder`, fuzz determinism |
| Traps work | ✅ | T `HazardTests` |
| Key unlock | ✅ | T `KeyUnlocksExitAndDescends` |
| Chest reward cannot duplicate | ✅ | T `ChestCannotBeOpenedTwice`, `RewardSurvivesSaveAndCannotDuplicateAfterLoad` |
| Exit advances floor | ✅ | T `KeyUnlocksExitAndDescends` |
| Five-floor run can complete | ✅ | T `FinalExitWinsTheRun`, boss death unlocks exit; `BalanceTests` assert novice AutoPlayer runs beat Lord Blobert |
| Death ends run cleanly | ✅ | T `DeathEndsTheRunAndRejectsFurtherCommands`, save cleared on death |
| Save and resume | ✅ | T `ResumedRunContinuesIdentically`, atomic store + backup recovery |
| Core rules have automated tests | ✅ | 137 headless tests (`dotnet test`); Unity EditMode 199 (196 passed, 3 explicit tuning tools skipped) |
| Generation validation rejects illegal boards | ✅ | T validator tests + 2,000-floor fuzz |
| Three enemy behaviours + boss | ✅ | Goblin, Crowned Slime, Fire Imp, Lord Blobert (Slam / Summon / Puff Up / Deflate) |
| Windows build | ✅ | B `ClickDungeon/Builds/Windows/ClickDungeon.exe` via **ClickDungeon → Build Windows** |
| Landscape layout per reference screens | ✅ | **S** title + gameplay screenshots from the Windows build. Found and fixed an off-board index crash on edge hover (regression test added) |
| Representative phone layout | — | Canvas uses Expand + safe area; not yet run on a device |

## Gate 3 prep — art pipeline (running alongside Gate 2)
- [x] Art brief from the reference images: `docs/art-brief.md`.
- [x] Art registry with placeholder fallback, import standards and coverage report (D-016).
- [x] D1 decided: default (make art for unbuilt systems, keep it hidden until each system exists).
- [x] D4 decided: slice the reference images into temporary placeholders (`tools/art-slicer/`).
- [ ] Save the reference images to `ClickDungeon/Art/Source/References/` and run the slicer.
- [ ] Review the contact sheet, calibrate crops, and check both screens in the build.
- [ ] Decide D2, D3, D5 (currently on their defaults: banner-styled panels, no mana bar, tile set B).
- [ ] Style frame: one finished 5×5 board, approved before mass production.

## Deferred (by design)
Gold, gems, shop, talents, inventory, daily reward, mail, extra heroes, equipment, rarity,
production art, audio. Their space in the reference layout is left empty rather than faked.

## Gate 2 — Fun test: prep and watch list
In progress.
- [x] Playtest telemetry (§38): local JSONL logs with tile-choice context, damage sources, ability use and skipped rewards (`docs/telemetry.md`, D-015).
- [x] Report: **ClickDungeon → Telemetry → Summarize Logs**, or `dotnet run --project Sim/ClickDungeon.Telemetry.Report -- <folder>`.
- [x] Floor variety: 15 room templates → 51 distinct room layouts after rotation and mirroring, plus 3 Lord Blobert arenas, with seeded placement on top. Covered by `TemplateLibraryHasEnoughDistinctLayouts` and `EveryTemplateCanGenerateValidFloors`.
- [ ] Run sessions with 5+ players and interview them about their tile choices.
- [ ] Review the summary against the ~70% informed-choice target and decide: continue, or redesign sensing.

Design risks to watch in playtests (tunable without rule changes unless noted):
1. **Bomb and Slam can't be escaped by walking.** Every orthogonal step from a bomb stays in its 3×3,
   and every step out of a Slam plus stays inside it. Only Dash or Shield work. Intended pressure, but if
   it feels unfair, shrink the blast to a plus shape (content value) or telegraph Slam on a fixed area.
2. **Melee "step-away dance."** Attacks target cells, so a lone goblin can be dodged forever. Board size
   and the key objective should force engagement. Watch for stalling.
3. **Spikes as a flat HP tax.** Enemies avoid them, so there is no way to exploit them yet.
4. **Sensing radius 2** may reveal too much on a 5×5. Try radius 1 sensing, plus a Rogue-style class that sees further.
5. **Shield cooldown 3 vs. boss 6-turn cycle.** Shield is always ready for Slam; check that the boss still tests positioning.

## Gate 2 tuning — difficulty tiers
- [x] Three tiers chosen when starting a run: Squire's Stroll, Knight's Trial, Blobert's Wrath (D-017, rules §10).
- [x] AutoPlayer balance tool: `BalanceReport` (explicit) plus `BalanceTests` guards; kit bot demo uses it (`-cdBot`).
- [x] Floor 5 reachable: novice AutoPlayer reaches Blobert in 100% (easy) and 99% (medium) of runs.
- [x] Exit tile reads open once the key is held.
- [x] Refine Knight's Trial and Blobert's Wrath numbers (D-017 amendment, retuned for adjacent-only melee).
- [ ] Check tiers against real playtest telemetry.


## Production tile set (D-018, D-019, rules §11)
- [x] Every tile in both sheets has a rule or an explicit decoration role.
- [x] New mechanics: lava, teleport pads, healing fountains, pressure plates, vault doors, great chests.
- [x] Vault rooms behind doors: awake guards, one great chest or several ordinary ones, the way back is the door you came in by.
- [x] Decoration only: cracked and mossy floors, wall corners, torches, water (pits), stair up (entrance).
- [x] Simulation, generation, saves and telemetry for all of it, with tests.
- [ ] Production PNGs: drop the sheets into `ClickDungeon/Art/Source/References/` to slice placeholders, or the final tiles into `Art/Runtime/Tiles/`.
- [ ] Tune how often vaults appear once real playtest data exists.

## Covered tiles and falling pits (D-020, rules §2.1 and §4)
- [x] Unknown tiles are drawn as stone covers, terrain included; sensing still shows the clue on the cover.
- [x] Stepping into a pit falls to the next floor for HP, skipping that floor's key, chests and exit.
- [x] Pits stay solid on the last floor and inside vaults; a fatal fall never leaves a hero inside a pit.
- [x] Tests: fall damage and landing, fatal fall, pit blocking, occupied teleport pad.
- [ ] Re-tune once playtest data shows how often players take the fall.

## Movement modes (D-021, rules §12)
- [x] Free Roam (default): tap any tile, no hints about unrevealed ones.
- [x] Step by Step (option): the eight neighbours only, with sensing hints.
- [x] Enemies behave the same in both modes: melee from a neighbouring tile, ranged and boss attacks from a distance
      (the earlier Free Roam "strike from anywhere" rule was removed — see Enemy reach below).
- [x] 8-way adjacency for hero and enemies; dash covers one or two tiles; revealing follows melee reach.
- [x] Nothing blocks the hero but a locked vault door and an occupied tile; walls are no longer generated.
- [x] Saves record the mode and resume in it.
- [x] Mode picker in Settings (applies to the next new run), a `-cdMovement free|step` automation flag, and the mode on the pause screen.
- [ ] Re-measure the difficulty tiers under Free Roam: they no longer separate (rules §10.1).

## Open: Free Roam removes the death threat (rules §10.1, D-021)
Measured over 200 seeds per tier, both modes, with `BalanceReport`.
- [x] Measured: in Free Roam the bot **never dies** — zero deaths in all 15 tier/skill rows. Losses are
      stalls against the command cap, almost all on Blobert's floor.
- [x] Cause identified: attacks target a cell one turn ahead (§3.4); with the whole board one tap away,
      moving out of the telegraphed cell is always free, so enemies can never connect.
- [ ] **Decide the fix.** Leading candidate: in Free Roam a revealed enemy's attack resolves against the
      hero's *current* tile rather than the declared cell, which is what "monsters attack each turn you
      take any action" implies. Needs a decision entry: it changes the §3.4 pillar for one mode.
- [ ] Re-measure every tier afterwards and re-base the `BalanceTests` guards from the new data.
- [ ] Separately: the 0%-mistake bot deadlocks in the Step by Step step-away dance and understates that mode.

## Chest quality and tap-to-open (D-022, rules §7)
- [x] Common 2 / Rare 3 / Epic 4 taps; every tap is a full turn that the monsters answer.
- [x] Vault great chests are Epic and still grant three rewards.
- [x] Quality drawn from a hash at floor setup: floors from a seed are unchanged, saves carry it, ruleset bumped to 5.
- [ ] Board art: per-quality chest treatments and the 2/3/4-segment progress meter (`ui_tap_progress` is in the sheets).
- [ ] Telemetry: record taps, so we can see how often a player starts a chest and walks away from it.
- [ ] Special/Premium chests and the Mimic: blocked on a currency system and a new enemy (see the art brief).

## Free Roam gives no hints (D-021 amendment, rules §2.1 / §2.3 / §12)
- [x] Free Roam produces no `Sensed` cells: every unrevealed tile is a blank cover, whatever lies under it.
- [x] Sensing and the clue set are unchanged in Step by Step.
- [x] "Can't dash into the unknown" now applies only in Step by Step, since nothing distant is ever known in Free Roam.
- [x] Test: `FreeRoamGivesNoHintsAtAll`.
- [ ] Re-measure the tiers afterwards: the guards were all set against a sighted bot.

## Blind AutoPlayer (D-021 amendment)
- [x] A blind bot decides on a redacted board — unrevealed tiles blanked, hidden enemies removed — look-ahead included,
      so it cannot find a trap by simulating a step onto it.
- [x] Exploration drive: the nearest unrevealed tile becomes the goal when no objective is visible, and uncovering tiles
      counts as both progress and score.
- [x] `BalanceReport` measures every tier in both modes twice, sighted and blind.
- [x] `GuardBaseline` (explicit) prints the slice the guards assert, at the sample sizes they use.
- [ ] Set the three balance guards from `GuardBaseline` output rather than by nudging thresholds.

## Open: the floor banner covers the HUD (a trade, not a defect)
Measured at the 1920×1080 reference; centre-anchored y=0 is mid-screen, so the top edge is +540.

| Element | Placed | Occupies |
|---|---|---|
| Floor banner | centre `(0, 482)`, 760×116 | y **424→540**, x −380→+380 |
| HP bar | top-left `(592, −28)`, 440×54 | y **458→512**, x −368→+72 |
| Boss/slash/turn chips | top-left `(1254, −32)` and `(1438, −32)`, 170×46 | y **462→508** |
| Floor plaque | top-left `(100, −126)`, 380×104 | y 310→414 (clear) |
| Board top | — | ≈426 |

The HP bar sits entirely inside the banner, and the first chip is clipped. There is no free gap to move into:
424→540 *is* the HUD band, and the banner is wider than the space between the plaque and the chips. The call site
chose this deliberately — "above the board, over the top HUD band, so it never hides board tiles" — so the real
question is which one it may cover for ~1.3 s.

- [ ] **Decide.** Recommended: move it down (e.g. `(0, 300)` → y 242→358) so it briefly overlaps the top of the board
      instead of the health bar. It is transient, never blocks input (`blocksRaycasts = false`), and the floor plaque
      carries the same floor number and name permanently — whereas HP is hidden exactly when arriving on a new floor.
- [ ] Alternative if board tiles must stay clear: shrink the plate and drop the subtitle, or slide it under the chips.
- [x] `-cdBlind 1` automation flag: demo and screenshot runs can use a blind bot (README, `ClickDungeonApp`).

## Enemy reach (D-021 amendment, rules §12)
- [x] Decided: melee monsters must stand next to the hero to attack, in both modes; the fire imp and Lord Blobert's slam
      and summon reach from a distance. "Strike from anywhere" is gone, so enemy behaviour no longer depends on the mode.
- [x] Tests: `FreeRoamMeleeMustStandNextToTheHeroToAttack`, `FreeRoamRangedEnemiesAndTheBossReachFromADistance`.
- [x] Re-measured (rules §10.2) and guards re-based.
- [ ] **Retune the difficulty tiers**: every tier got much easier, and Knight's Trial is now only four novice wins
      behind Squire's Stroll.
- [x] Retuned (D-017 amendment): Knight's Trial +1 enemy / −1 potion; Blobert's Wrath +2 enemies / −2 hearts.
      Blind novice wins 39 / 31 / 9 of 40 (rules §10.2).
- [ ] **AutoPlayer stall bug**: the casual blind bot runs out of commands in ~1 run in 10 at every tier, even where it
      never dies. Its balance numbers are unusable until that is fixed.

## Chest rewards (D-022 amendment)
- [x] One reward per tap: Common 2, Rare 3, Epic 4; great chest 5. Draws give +2 potions or +3 max HP or +1 Slash.
- [x] AutoPlayer opens chests when safe; `ChestWorth` compares skipping, old rewards and shipped rewards.
- [x] Measured: 4–5× the loot at the same win rate; looting beats skipping on Blobert's Wrath (25 vs 19 of 60).
- [ ] Looting is still roughly neutral on the easier tiers. Consider *when* chests can be opened (a lid that keeps
      progress, or tapping that does not provoke) rather than raising rewards further.
- [x] Chest reveal overlay opens straight to the reveal (no second tap ritual) and lists all rewards.
- [x] Tap pips on the board are readable (they were drawn at 12×8 on gold art).
- [x] End screen and telemetry summary count chests, not rewards (`Chests.ChestsOpened`).
- [x] Potion draws back to +1; weight moved to +3 max HP.

## Tiles reveal only when clicked (D-023)
- [x] Only the hero's own tile is revealed; every other tile needs a click. Step by Step keeps its sensed clues.
- [x] Clicking a covered tile you cannot enter is a bump: sleeping monster (wakes), shut door, bottomless pit. Dashes too.
- [x] AutoPlayer no longer circles vaults; the casual bot's stall bug is gone (0 stalls in every sweep).
- [x] Tiers retuned: blind novice wins 36 / 25 / 8 of 40 (rules §10.2).
- [x] Help, hint and log text: "Something was lurking there!" / "Something blocks the way."

## No hints in Free Roam (D-021, D-023)
- [x] Board: Free Roam draws no clue markers (no "K", danger, treasure or safe marks); they come only from Step by Step sensing.
- [x] Hover: the Inspect panel checked terrain before knowledge, so hovering a covered tile named a pit, vault door or wall.
      Covered tiles now read UNKNOWN whatever is under them ("Covered. Click it to find out what is here." in Free Roam);
      Step by Step keeps its sensed clues. Test: `InspectTileTests` (`GameScreen.InspectTile`).
- [x] Step by Step clues split: "K" is only the key, a green "E" is the exit, a purple "+" is a door, plate or teleport pad.

## Art conversion from the newer sheets (D4 placeholders, D1 holds)
- [x] Action poses from the character sheets, played as one-frame one-shots: Sir Clickington slash / hit / victory / defeat;
      Crowned Slime and Fire Imp wake (spawn) / attack / hit / defeat; Lord Blobert boast / summon / puffup / defeat.
- [x] Common encounter monster pack: Goblin Raider is the goblin and Slime Minion the slimelet (idle, wake from the alert
      frame, attack, hit, defeat); `enemy_alert` is the wake effect.
- [x] Core UI pack: the framed small icons are the ability icons; heart, lock (locked exit badge) and alert (danger corner icon).
- [x] Slicer: `min_island` drops panel dividers and specks around cut-outs, and `local_tolerance` keeps dark bodies whole
      (Lord Blobert's idle, puffed and deflated art had holes). Coverage is 80 reference slices, up from 54.
- [ ] Not converted, with reasons: `tile_highlight` is a 20 px glow (highlights must stay in the outer 8 px); the large
      `btn_*` buttons have baked labels (the game draws live labels); `ui_tap_progress` has the chest and hand baked in;
      `enemy_health_bar` has no enemy HP bar to wire to. Intent badges, danger telegraphs, HUD frames and most FX have no
      source art on disk and stay procedural.

## Every cover is identical (D-023 amendment)
- [x] Free Roam: nothing drawn, said or answered for a covered tile depends on what is under it; the exit stairs above all.
- [x] Leaks fixed: exit-unlock popup and effect on a covered exit, blast news about sleeping monsters, plates uncovering
      doors, tapping a covered chest, the cover placeholder colour following sensing.
- [x] Regression tests: `HiddenTileArtTests` (Unity), `HiddenTileTests`, `InspectTileTests`.
- [ ] Step by Step still shows sensing markers, and monsters that start awake stay visible (user decision to keep both).

## Art conversion, second pass
- [x] Step by Step clue icons, underfoot badges, the chest tap meter, the reward burst and the hover highlight all have
      art; coverage is 95 reference slices and 98 procedural placeholders (was 80 / 113).
- [x] Slicer: a `hollow` setting clears the middle of a frame, so a tile highlight keeps only its glow.
- [ ] Still procedural, with no source art on the sheets: intent badges, danger telegraphs, HUD frames and panels,
      modal panels, title-screen panels and buttons (their labels are baked in), backgrounds, and most FX.
- [x] Third pass: reward icons, guard/rest/spawn/fire poses, armed bomb, alias tiles, chest reactions. Coverage is 112
      reference slices and 81 procedural placeholders.
- [ ] Remaining 81 are mostly things the sheets cannot supply as-is: intent badges, danger telegraphs, HUD and modal
      frames and title panels (their text is painted in, so they need frames synthesised from the art plus 9-slice
      borders), backgrounds, most FX, and the walk/attack poses the character sheets never drew.
- [x] Frames: slicer `frame` mode plus importer-applied 9-slice borders, ready for frame art that is drawn to stretch.
      The reference sheets' own frames were tried and reverted: flattening their baked-in middles read worse in the game
      than the procedural panels.

