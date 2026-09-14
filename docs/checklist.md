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
| Five-floor run can complete | ✅ | T `FinalExitWinsTheRun`, boss death unlocks exit; full bot clear not yet asserted |
| Death ends run cleanly | ✅ | T `DeathEndsTheRunAndRejectsFurtherCommands`, save cleared on death |
| Save and resume | ✅ | T `ResumedRunContinuesIdentically`, atomic store + backup recovery |
| Core rules have automated tests | ✅ | 77 tests, passing headless (`dotnet test`) and in Unity EditMode (77/77) |
| Generation validation rejects illegal boards | ✅ | T validator tests + 2,000-floor fuzz |
| Three enemy behaviours + boss | ✅ | Goblin, Crowned Slime, Fire Imp, Lord Blobert (Slam / Summon / Puff Up / Deflate) |
| Windows build | ✅ | B `ClickDungeon/Builds/Windows/ClickDungeon.exe` via **ClickDungeon → Build Windows** |
| Landscape layout per reference screens | ✅ | **S** title + gameplay screenshots from the Windows build. Found and fixed an off-board index crash on edge hover (regression test added) |
| Representative phone layout | — | Canvas uses Expand + safe area; not yet run on a device |

## Deferred (by design)
Gold, gems, shop, talents, inventory, daily reward, mail, extra heroes, equipment, rarity,
production art, audio. Their space in the reference layout is left empty rather than faked.

## Gate 2 — Fun test: prep and watch list
Not started. Before playtesting:
- [ ] Minimal telemetry (§38): tile choices with sensed options, damage source, ability use.
- [ ] 30–50 floor configurations (currently 6 templates × 8 transforms × seeded placement).

Design risks to watch in playtests (tunable without rule changes unless noted):
1. **Bomb and Slam can't be escaped by walking.** Every orthogonal step from a bomb stays in its 3×3,
   and every step out of a Slam plus stays inside it. Only Dash or Shield work. Intended pressure, but if
   it feels unfair, shrink the blast to a plus shape (content value) or telegraph Slam on a fixed area.
2. **Melee "step-away dance."** Attacks target cells, so a lone goblin can be dodged forever. Board size
   and the key objective should force engagement. Watch for stalling.
3. **Spikes as a flat HP tax.** Enemies avoid them, so there is no way to exploit them yet.
4. **Sensing radius 2** may reveal too much on a 5×5. Try radius 1 sensing, plus a Rogue-style class that sees further.
5. **Shield cooldown 3 vs. boss 6-turn cycle.** Shield is always ready for Slam; check that the boss still tests positioning.
