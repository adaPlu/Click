# ClickDungeon — Decision Log

High-reversal-cost decisions. Format: DECISION / WHY / DEPENDENCIES / REVERSIBILITY.
Rules referenced here live in `docs/rules.md`.

---

## D-001 Pure C# simulation shared by Unity and a headless test project
- **DECISION**: `Domain`, `Content`, `Simulation`, `Application` assemblies have
  `noEngineReferences: true`. The same source files are compiled by
  `Sim/ClickDungeon.Sim.Tests` (dotnet + NUnit 3) for fast headless tests, and
  by Unity asmdefs for the game. C# 9 language level (Unity 6 limit).
- **WHY**: rules are testable in seconds without the editor; fuzzing thousands
  of seeds is cheap; presentation physically cannot own gameplay truth.
- **DEPENDENCIES**: everything.
- **REVERSIBILITY**: very low. Don't add `UnityEngine` to those assemblies.

## D-002 Visibility semantics
- **DECISION**: terrain and exit always known; reveal Manhattan ≤ 1; sense
  Manhattan = 2; clue *set* per cell; "resolved" is per thing, not per cell.
- **WHY**: guarantees walking never lands on an unrevealed cell (fair traps),
  while range-2 clues create route decisions and "wake the enemy?" risk.
- **DEPENDENCIES**: first contact, dash, generation (no enemy within 2 of start).
- **REVERSIBILITY**: low. The sense/reveal radii are content values, but the
  state model is locked.

## D-003 Turn order: execute old intents, then declare new ones
- **DECISION**: enemies execute intents declared last turn, then declare.
- **WHY**: the prompt's original order (declare then execute same phase) hid
  every attack from the player and made Shield/telegraphs meaningless.
- **DEPENDENCIES**: all AI, Shield, boss script, tests.
- **REVERSIBILITY**: very low.

## D-004 Attacks target cells
- **DECISION**: melee/boss attacks lock onto a cell at declaration.
- **WHY**: readable telegraphs; movement and Dash become defensive decisions.
- **REVERSIBILITY**: low.

## D-005 Chest reward committed by the Interact command
- **DECISION**: reward chosen and granted inside the turn transaction; the
  3-tap animation only reveals it.
- **WHY**: no half-open chest state to save/resume; exactly-once for free.
- **REVERSIBILITY**: medium (presentation can change freely).

## D-006 RNG = hash-derived seeds, no shared mutable stream
- **DECISION**: generation uses a PRNG seeded by
  `hash(runSeed, generationVersion, floorIndex, attemptIndex)`; chest loot by
  `hash(runSeed, floorIndex, chestId)`. Enemy AI uses no randomness.
  `UnityEngine.Random` is never used by simulation.
- **WHY**: stream isolation by construction; nothing about a chest can shift a
  later roll; saves need no RNG state.
- **REVERSIBILITY**: medium. Changing hashing changes every seed → bump
  `GenerationVersion`.

## D-007 Save format
- **DECISION**: JSON (Newtonsoft — Unity package `com.unity.nuget.newtonsoft-json`,
  NuGet `Newtonsoft.Json` in tests) of the full `RunState` with
  `SaveSchemaVersion`, `RulesetVersion`, `ContentCatalogVersion`,
  `GenerationVersion`. Atomic temp → verify → replace with `.bak`.
- **WHY**: human-readable for playtest debugging; full state survives balance
  changes.
- **REVERSIBILITY**: medium; migrations keyed by `SaveSchemaVersion`.

## D-008 Generation = templates × transforms + seeded placement
- **DECISION**: hand-authored 5×5 topology templates, 8 dihedral transforms,
  validated placement, deterministic retry on `attemptIndex`.
- **REVERSIBILITY**: medium.

## D-009 Landscape layout follows reference gameplay screen
- **DECISION**: landscape on all platforms (user-confirmed). Gameplay screen
  layout follows the reference "main game screen": portrait + HP top-left,
  floor plaque left, 5×5 board centre, ability bar (Move, Slash, Shield, Dash,
  Potion) under the board, settings top-right. Title screen follows the
  reference title screen layout.
- Systems that don't exist yet (gold, gems, mana bar, inventory, talents, shop,
  mail, daily reward, hero select) are **not shown** until implemented; their
  screen space is reserved in the layout.
- **REVERSIBILITY**: low for orientation; high for individual widgets.

## D-010 Small integer numbers
- **DECISION**: hero HP 10, hits of 1–4.
- **WHY**: intent damage must be readable at a glance on a phone.
- **REVERSIBILITY**: high (content values).

## D-011 Contextual tap; MOVE is the default mode
- **DECISION**: in MOVE mode (default, highlighted) tapping a cell does the
  obvious thing: step / slash adjacent enemy / open adjacent chest. SLASH,
  SHIELD, DASH, POTION buttons select explicit actions. One command model for
  mouse, touch and keyboard.
- **REVERSIBILITY**: high.

## D-012 Gate 1 content defined in C#
- **DECISION**: `ContentCatalog` built in code with stable string ids.
  ScriptableObject authoring deferred until content volume justifies it.
- **REVERSIBILITY**: high (ids are the contract, not the storage).

## D-013 Locked exit instead of locked doors (Gate 1)
- **DECISION**: the key opens the floor exit. Doors deferred.
- **WHY**: one progression structure is enough to test key routing.
- **REVERSIBILITY**: high.

## D-014 Knight has no mana in Gate 1
- **DECISION**: Shield and Dash use cooldowns (3). No resource bar.
- **WHY**: fewer systems; cooldowns stop ability spam.
- **REVERSIBILITY**: high.
