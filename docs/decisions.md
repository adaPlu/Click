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

## D-015 Local JSONL playtest telemetry recorded from simulation results
- **DECISION**: `TelemetryRecorder` (Application layer) snapshots decision context before each command and
  maps the `CommandResult` afterwards. It writes JSON lines to local files, one per app launch. Tile choices
  record only player-visible information (telegraphs, revealed hazards and pickups, clues). The log is on by
  default in prototype builds, can be turned off in Settings, and uses no network. Details: `docs/telemetry.md`.
- **WHY**: Gate 2 must judge whether choices were informed, which is only possible against what was
  knowable. Local files avoid consent, back-end and privacy work before a product decision. Recording in
  Application keeps presentation dumb and makes telemetry testable headless.
- **DEPENDENCIES**: `GameEvent` vocabulary, `Threats`, `Commands.LegalTargets`.
- **REVERSIBILITY**: high. The schema is versioned (`schema` in `run_started`), and a network sink can
  replace the file sink later.

## D-016 Art registry keyed by file name, with placeholder fallback
- **DECISION**: Production art lives in `Assets/ClickDungeon/Art/Runtime/`. The file name is the key, and
  numbered files form animations. An editor tool builds `Art/Resources/ArtCatalog.asset`. Presentation
  looks art up by key through `Art` and draws the existing procedural placeholder for any missing key.
  Import standards apply to new files automatically. Brief and key list: `docs/art-brief.md`.
- **WHY**: art will arrive piece by piece from an undecided source (brief D4), so the game must never break
  or wait on a missing file. File-name keys need no scene wiring in a code-built UI and make coverage
  reportable.
- **DEPENDENCIES**: presentation layer only (`Icons`, `BoardView`, screens). Simulation is untouched.
- **REVERSIBILITY**: high. Keys are the contract; storage (Resources, Addressables) can change later.

## D-017 Difficulty tiers as tuned content catalogs
- **DECISION**: Three tiers: Squire's Stroll (easy), Knight's Trial (medium) and Blobert's Wrath (hardcore). A tier
  is a `DifficultyDefinition` of additive adjustments: hero HP and potions, enemy HP and damage, boss HP and slam,
  hazard damage, enemy count, and HP restored when arriving on a new floor. `ContentCatalog.CreateDefault(tier)`
  builds fresh content with those adjustments applied. The run stores its tier, and `GameSession` switches to the
  matching catalog whenever a run starts or resumes, so rules, threat previews, inspector text and telemetry all
  read the same numbers. `Difficulty.Medium` is enum value 0, so saves from before tiers load as Medium. Ruleset 2
  adds the arrival heal.
- **WHY**: bot measurement showed floor 5 was out of reach for most runs. Keeping tuning in content, rather than
  multipliers spread through the rules, keeps every displayed number truthful and every tier testable headless.
- **HOW IT IS TUNED**: `AutoPlayer` (Application layer) is a deterministic one-turn look-ahead bot. The explicit
  `BalanceReport` test plays 200 seeds per tier; `BalanceTests` guard the targets on every test run. The kit bot
  demo uses the same player (`-cdBot smart`, the default).
- **DEPENDENCIES**: `ContentCatalog`, `RunState.Difficulty`, `GameSession`, the title difficulty picker.
- **REVERSIBILITY**: high. The numbers live in one table, and another tier is one more dictionary entry.

## D-018 Vault rooms behind doors
- **DECISION**: Normal floors may hold a **locked door** and a **pressure plate**. Stepping on the plate opens every door on
  that floor (the plate stays pressed). Stepping into an open door enters a **vault room**: its own 5×5 floor generated from
  `hash(runSeed, floorIndex, doorCell)`, holding 2–3 awake enemies and either one **great chest** (three rewards at once) or
  two to three ordinary chests. The vault's stair leads back to the floor the hero came from, restored exactly as it was, with
  the hero back on the tile they stepped in from (nobody ever stands in a doorway, so every hero position is floor terrain). Vaults are optional: the key, the exit and the floor count are unaffected.
- **WHY**: the tile sheets add doors, plates and chests; a risk-for-loot side room is the smallest rule that makes all three
  meaningful, and it fits the "informed choice" pillar — the player sees the enemies and the reward before stepping in.
- **DEPENDENCIES**: `FloorState`, `RunState` gains the outer floor to return to (additive save field), `Chests`, generation.
- **REVERSIBILITY**: medium. The save gains a nested floor; a run without vaults is unchanged.

## D-019 Floor features from the production tile set
- **DECISION**: The tile sheets map to rules as follows. Real mechanics: **lava** (permanent hazard, entering costs 3 HP,
  never expires), **teleport pads** (a pair per floor; entering one places the hero on the other, no extra turn),
  **healing fountain** (entering heals 3 once, then it is spent), **pressure plate** and **doors** (D-018), plus the tiles
  already in the game (stone floor, pit, bomb, spikes, key, chests, wall, locked and open stairs). Decoration only, chosen
  deterministically from the cell so it never lies about state: **cracked** and **mossy** floor variants, **wall corner**,
  **torch** walls, **water** (drawn for pits) and **stair up** (the floor entrance). Both swirl tiles (`tile_teleport`, `tile_shadow`) are teleport pads.
- **WHY**: every tile in the sheet gets a use, but only where it can carry a truthful rule. Anything without a rule is drawn
  as a variant of a tile that already has one, so the board never shows a feature the simulation does not implement.
- **DEPENDENCIES**: `Terrain`/`HazardKind`/`ContentKind`, `FloorGenerator`, art registry keys, `docs/rules.md` §11.
- **REVERSIBILITY**: high per feature; each is a separate generator entry and can be dropped from the floor profiles.

## D-020 Covered tiles and falling pits
- **DECISION**: Two changes to what the board shows and what a pit does.
  (a) **Covers**: every tile the hero has not revealed is drawn as a plain stone cover, terrain included. Sensing still
  shows the tile's clue on the cover; walls and pits are discovered the same way as hazards and content. Rules §2.1.
  (b) **Falling**: stepping into a pit costs `FallDamage` HP and lands the hero on the next floor, skipping that floor's
  key, chests and exit. On the last floor and inside a vault there is nothing below, so pits stay solid. Rules §4.
- **WHY**: covers make exploration the point of every click rather than reading a pre-drawn map, and a pit that drops you
  turns dead space into a real choice: pay HP and lose the floor's loot to save turns.
- **DEPENDENCIES**: `Board.HeroCanEnter`/`CanFallThrough`, `TurnResolver.TryFall`, `BoardView` cover rendering, generation
  (pits already exist in templates), telemetry `fell_through_pit`.
- **REVERSIBILITY**: high for (a), presentation only. Medium for (b): it changes pacing and the value of every floor's loot,
  and the balance guards measure it.

## D-021 Movement modes
- **DECISION**: A run picks one of two movement modes, stored in the save. **Free Roam** (default): the hero moves to any
  tile on the 5×5, nothing blocks the way, and a revealed enemy strikes the hero's tile from anywhere each time the player
  acts. **Step by Step** (option): the hero moves to one of the eight neighbouring tiles, and enemies chase, declare fire
  lanes and strike only from adjacent tiles. Adjacency is 8-way for both actors in both modes, dash covers one or two tiles
  in a straight line, and the only things that stop the hero are a locked vault door and an occupied tile. Walls are no
  longer generated; the wall sprite is the cover over an unrevealed tile.
- **WHY**: the 5×5 board is a click surface first. Free Roam makes every tile a legal choice and puts the pressure on
  reading the board rather than walking across it; Step by Step keeps the tactical positioning game for players who want it.
- **DEPENDENCIES**: `MovementMode`, `Commands.Validate`/`TryContextual`, `EnemyAi` declare/execute, `Board.HeroCanEnter`,
  `Visibility` (revealing follows 8-way reach), save validation, `AutoPlayer`, the balance guards.
- **REVERSIBILITY**: low for the default. Free Roam changes what the game is to play, and it makes the game markedly
  easier: the difficulty tiers were measured under the old movement and no longer separate, so they must be re-measured
  against Free Roam before the tiers mean anything.

## D-022 Chest quality and tap-to-open
- **DECISION**: Regular chests carry a quality that sets how many taps they take to open: **Common 2, Rare 3, Epic 4**
  (weights 60 / 30 / 10). Every tap is a full player action costing a turn, so each one gives every revealed monster its
  response. A vault's great chest is always Epic and still grants three rewards. Quality is drawn from a hash of the run
  seed, floor and cell at floor setup rather than from the generator's stream, so floors from a given seed are unchanged;
  the reward is still committed by the tap that finally opens the chest. Ruleset version 5.
- **WHY**: looting stops being free. A chest in a room with woken monsters becomes a real decision — two to four turns of
  exposure for one reward — instead of a one-click pickup grabbed on the way past.
- **NOT BUILT**: Special/Premium chests and the Mimic. The art sheet itself states special keys are a premium currency item
  "not found in regular dungeon gameplay", so a premium chest would be a dead mechanic until a shop and currency exist (D1).
  A Mimic is an enemy wearing a chest: it needs an enemy definition, a rules entry and tests before any art is wired. Both
  sets of art stay on disk, unwired (see `docs/art-brief.md`).
- **DEPENDENCIES**: `ChestQuality`, `CellState.Quality`/`ChestTaps`, `Chests.Tap`/`TapsToOpen`/`RollQuality`,
  `RunFactory.SetupFloor`, `TurnResolver` Interact, save validation, `AutoPlayer.Copy`, `GameEventKind.ChestTapped`.
- **REVERSIBILITY**: medium. Forcing every quality to Common and one tap restores the old behaviour, but the balance moves
  with it: chests now cost turns, and the bot already skips them entirely in Free Roam.

### D-021 amendment: Free Roam gives no hints
- **DECISION**: Free Roam produces no `Sensed` cells. Every tile the hero has not revealed is drawn as a blank cover with
  no clue of any kind, whatever lies under it; knowledge runs `Unseen → Revealed` only. The exit stays known from the start
  (§2.1). Step by Step keeps sensing and its clue set unchanged. Because nothing distant is ever known in Free Roam, the
  "can't dash into the unknown" restriction applies only in Step by Step — a blind dash there is no worse than a blind step.
- **WHY**: with hints, a Free Roam floor could be read from a distance and solved in two clicks — tap the key, tap the exit.
  Removing them makes clicking a tile the way you learn what is on it, which is the point of the mode.
- **NOTE**: this does not change `AutoPlayer`. The bot never consults `cell.Knowledge`, so its runs still take the optimal
  route and cannot measure what hiding information does to a human player. Any balance number taken from the bot after this
  change describes an omniscient player, not a real one.
- **DEPENDENCIES**: `Visibility.Update`, `Commands.ValidateDash`, rules §2.1/§2.3/§5/§12.
- **REVERSIBILITY**: high — one condition in `Visibility.Update`.

### D-021 amendment: a blind AutoPlayer for half the measurements
- **DECISION**: `AutoPlayer` can be constructed **blind**. A blind bot decides on a *redacted* copy of the run: every tile
  it has not revealed is blanked (terrain, hazard and content cleared) and enemies standing on unrevealed tiles are removed.
  Its one-turn look-ahead runs on that redacted board, and a command it believes is legal is still checked against the real
  board before it is played. Because a blind player has no key to walk to, two things give it a reason to explore: the
  nearest unrevealed tile becomes the goal when no objective is visible, and uncovering tiles counts as progress and score.
  `BalanceReport` now measures every tier in both modes **twice, once sighted and once blind**.
- **WHY**: the sighted bot walks straight to a key it should not be able to see, so its numbers describe an omniscient
  player and say nothing about how the game plays once hints are gone. Redaction has to cover the look-ahead as well as the
  scoring, or the bot would still find a trap by simulating a step onto it.
- **DEPENDENCIES**: `AutoPlayer.Redact`/`Blind`/`RevealedCells`, `GoalDistance` exploration fallback, `PlayRun`,
  `BalanceTests.Measure` and the balance guards.
- **REVERSIBILITY**: high — blind defaults to false, so every existing sighted measurement is unchanged.

### D-021 amendment: enemy reach is the same in both modes
- **DECISION**: Melee monsters (goblin, crowned slime, slimelet, and Lord Blobert while puffed up) attack only from a tile
  next to the hero and otherwise step closer — in Free Roam as well as Step by Step. Ranged monsters (the fire imp, down a
  clear lane up to 3 tiles) and the boss's slam and summon act from a distance. The Free Roam "strike from anywhere" rule
  is removed, so enemy behaviour no longer depends on the movement mode; the modes now differ only in how far the hero
  moves and whether nearby tiles are hinted.
- **WHY**: distance should matter. With strikes from anywhere, every monster threatened the whole board equally. Now a
  melee monster is a threat you choose to engage — slashing it means standing next to it — while ranged monsters and the
  boss are what pressure you across the board.
- **DEPENDENCIES**: `EnemyAi.ChaseIntent`/`LaneIntent`/`Execute`, rules §12, the balance guards (re-measured).
- **REVERSIBILITY**: high.

### D-017 amendment: tiers retuned for adjacent-only melee
- **DECISION**: Knight's Trial is no longer the unmodified base content: it adds one enemy to every normal floor
  (`ExtraEnemies = 1`) and starts with one fewer potion. Blobert's Wrath adds two enemies instead of one and takes two
  hearts from the hero (8 max HP), on top of its existing enemy, hazard and boss changes. Squire's Stroll is unchanged.
- **WHY**: once melee monsters had to stand next to the hero (D-021 amendment), a blind novice AutoPlayer won 39 / 35 / 26
  of 40 and the tiers barely differed. `DifficultySweep` showed enemy and boss HP barely matter, because a melee monster
  can be walked away from; crowding and unavoidable damage do. After retuning, the same player wins 39 / 31 / 9.
- **DEPENDENCIES**: `ContentCatalog` difficulty definitions, rules §10 / §10.2, `BalanceTests` guards.
- **REVERSIBILITY**: high — content numbers only. Real playtest telemetry should replace the bot as the judge.

### D-022 amendment: chests pay one reward per tap
- **DECISION**: A chest grants one reward per tap it costs — Common 2, Rare 3, Epic 4 (`ChestRewardsByQuality`) — and a
  vault's great chest grants 5. Each reward draw is stronger: +2 potions (was 1) and +3 max HP (was 2); +1 Slash damage is
  unchanged. `AutoPlayer` now opens chests by default: it counts taps already spent as progress, values standing in reach
  of a known chest, and will not leave a floor with known loot while healthy and with nothing awake on it.
- **WHY**: every chest used to grant one reward however many turns it cost, and the bot — looking one turn ahead — could
  never see the value of a first tap, so no run opened a chest. `ChestWorth` (60 seeds per tier, blind novice) showed
  looting at the old rewards cost wins; at the shipped rewards looting gives 4–5× the loot at the same win rate, and on
  Blobert's Wrath beats skipping chests (25 wins vs 19).
- **OPEN**: on the two easier tiers looting is still roughly neutral. Safe windows to spend 2–4 turns are rare once melee
  monsters chase, so the next lever is *when* a chest can be opened, not how much it pays.
- **DEPENDENCIES**: `Chests.RewardDraws`, `ContentCatalog.ChestRewardsByQuality` and reward table, `VaultTuning`,
  `AutoPlayer` looting policy, `ChestWorth`, rules §7 / §10.2, balance guards.
- **REVERSIBILITY**: high — content numbers, plus `loots: false` on the bot.
- **REVISED (same day)**: potion draws went back to +1 (weight 2) and that weight moved to +3 max HP (weight 3): at +2
  potions per draw a looting run ended with ~11 unused. The chest reveal overlay no longer asks for its own 3 taps — the
  turns were already spent on the board — and lists every reward granted. The end screen and the telemetry summary count
  chests (first draws), not rewards. Blind novice now wins 36 / 29 / 15; looting still beats skipping on Blobert's Wrath
  (24 vs 19 of 60).

## D-023 Tiles reveal only when clicked
- **DECISION**: A tile is revealed only by clicking it, never by the hero's sight range. The hero's own tile is revealed;
  every other tile stays covered, including its neighbours. Clicking a covered tile the hero cannot enter is a **bump**:
  the hero stays put, the tile is uncovered and the turn is spent. That covers a sleeping monster (which wakes, and under
  first contact only acts after the player's next command), a shut vault door, and a pit with nothing below. A dash is
  stopped the same way by the first covered tile on its path that would block it. Step by Step still senses clues within
  two steps; sensing is not revealing. Monsters wake only when their tile is uncovered.
- **WHY**: design direction — clicking is how you learn the board. A click that was refused would have leaked what was
  under the cover, so every blocked click becomes an uncovering instead.
- **BOT**: two AutoPlayer bugs surfaced. Leaving a vault returns to the same floor number, so it scored as no progress and
  the bot circled vaults until the command cap (26 of 30 Squire's Stroll runs); being in a vault now costs score unless
  the bot is safely looting. This also fixed the casual bot's long-standing stall bug.
- **TUNING** (D-017 amendment): every click is now a blind step, so traps and bumped monsters do the hurting. Knight's
  Trial drops the extra monster and missing potion and blunts traps by 1. Blobert's Wrath drops its extra monsters,
  missing hearts and extra monster/trap damage; its pressure is Lord Blobert (+4 HP, +1 slam), tougher monsters and one
  potion. Blind novice AutoPlayer wins 36 / 25 / 8 of 40; sighted bots now win every run at every tier, so hidden
  information is what makes the tiers bite.
- **DEPENDENCIES**: `Visibility.Update`/`Reveal`, `Board.ClickUncovers`, `Commands.DashBumpsAt`, `TurnResolver.Bump`,
  `GameEventKind.HeroBumped`, telemetry `tile_bumped`, `AutoPlayer` vault scoring, difficulty definitions, rules §2 / §3 /
  §10 / §12, help and hint text.
- **REVERSIBILITY**: medium. The reveal radius is one condition, but every tier was retuned around blind clicking.

### D-023 amendment: the exit is covered too
- **DECISION**: The exit is no longer revealed when a floor starts or a vault is entered or left. It is covered like every
  other tile and found by clicking it; stepping onto it without the key only uncovers it. The blind AutoPlayer no longer
  sees a covered exit (it explores for it once it holds the key), and tile-choice telemetry records exit distance only
  once the exit has been uncovered.
- **WHY**: design direction — nothing on the board is known in advance.
- **MEASURED**: blind novice wins 39 / 21 / 10 of 40 (was 36 / 25 / 8); Knight's Trial runs ~24 turns longer. Tiers stay
  ordered, so no retune; the Knight's Trial reach guard was re-based from 24 to 21 of 30.

### D-023 amendment: every cover is identical
- **DECISION**: In Free Roam, every unrevealed tile looks and behaves exactly alike until it is clicked, the exit stairs
  above all. Presentation is gated by `Simulation.Discovery`: popups and board effects only mark uncovered tiles (or an awake
  monster's own tile), and nothing is said about a monster still hidden under its cover. The cover placeholder no longer
  varies with sensing. A pressure plate opens its doors without uncovering them. Tapping a covered chest uncovers it
  instead of opening it, so the tap command no longer reveals that a chest is there. The blind AutoPlayer now also sees
  awake monsters standing on covers (the player does) and no longer sees whether a covered door was opened.
- **KEPT** (user decision): Step by Step keeps its sensing markers on top of the cover. Monsters that start awake (vault
  guards, Lord Blobert) stay visible from the start.
- **LEAKS FIXED**: when Blobert fell, "OPEN!" and the exit-unlock effect played on the covered exit tile, showing where it
  was. A bomb blast popped damage numbers and log lines for sleeping monsters under covers. A pressure plate uncovered
  every vault door on the floor.
- **TESTS**: `HiddenTileArtTests` (Unity) compares everything drawn for each covered tile against the exit's cover, both
  with catalog art and with placeholders, with and without the key, once the exit is unlocked, and while hovering. It
  fails on a 1% tint difference. `HiddenTileTests` checks that every command and tap on a cover gets the same answer
  whatever is under it, that the bot's view of every cover is alike, that the exit is uncovered only by clicking it, the
  `Discovery` rules, plates, and that no pits are generated on the last floor. `InspectTileTests` checks hover text.
- **REVERSIBILITY**: easy; presentation filters and two small rule changes.

## D-024 Hero select
- **DECISION**: A run is taken by a hero identity chosen on the title screen, and the choice is remembered between runs
  (`cd.hero`). A run in progress keeps its own hero, and the save carries it. The catalog gains a second class, the
  Paladin, and its first identity, Dawnward: 12 hearts, potions that heal 6, a shield every 2 turns, against a dash of
  one tile every 4. Board token, portrait and pose art follow the hero, falling back to the first hero's art so a new
  identity can be added before its art exists.
- **WHY**: the reference title screen has a HERO SELECT button, and the sheets ship a second hero. This is the smallest
  system that makes that part of the screen real (the alternative was drawing a button that does nothing, which D1 rules out).
- **TUNING**: the Paladin first had slash 1 to pay for its hearts. That made boss fights drag: the sighted bot won 24 of
  40 on Blobert's Wrath against the Knight's 40, and runs hit the command cap at ~307 turns. With slash 2 and one heart
  fewer it wins every tier sighted, and blind it wins 39 / 38 / 33 of 40 against the Knight's 40 / 36 / 29.
- **DEPENDENCIES**: `ContentCatalog` hero classes and identities, `GameSession.StartNewRun(..., heroId)`,
  `UserPrefs.Hero`, `LaunchOptions.ParseHero` (`-cdHero` for automation), `Menus.OpenHeroSelect`, the title hero card and
  its button, `Icons.Hero`, portrait lookup, art keys per identity, rules §5.1.
- **TESTS**: `HeroSelectTests` (identities are complete, the chosen hero is the one who plays, the Paladin plays to its
  own numbers, an unknown name falls back, the hero survives a save, and every hero can finish every tier),
  `SettingsMenuTests.HeroSelectListsEveryHeroWithItsNumbersAndMarksTheChosenOne`, and the `HeroSweep` balance aid.
- **REVERSIBILITY**: easy. Removing the second identity leaves the Knight and one button to delete.

