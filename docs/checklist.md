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
| Core rules have automated tests | ✅ | 135 headless tests (`dotnet test`); Unity EditMode 196 (193 passed, 3 explicit tuning tools skipped) |
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
- [ ] Refine Knight's Trial and Blobert's Wrath numbers.
- [ ] Check tiers against real playtest telemetry.

