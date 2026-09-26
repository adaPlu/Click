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
- **SUPERSEDED** by D-032 (mana).

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

## D-025 Coins, gems and the shop
- **DECISION**: Runs carry coins (chests, stairs) and gems (Lord Blobert) out into a between-runs profile
  (`profile.json`, kept apart from the run save so a broken run never costs coins). The title screen shows the purse and
  a SHOP that sells provisions for the next run (potion ration, heart token). Banking happens once, when a run ends;
  abandoning banks too. Automation keeps its profile in memory.
- **WHY**: the reference title and game screens show coin and gem counters and a SHOP; a counter with nothing to spend on
  would be the fake chrome D1 rules out, so currency ships with its shop.
- **BALANCE**: the tier guards and bots play with an empty profile, so every measured number describes a first run.
  Provisions make later runs easier by design.
- **TESTS**: `TreasureAndShopTests`, `SettingsMenuTests.TheShopShowsThePurseAndOnlyOffersWhatTheCoinsCover`.

## D-026 Special keys and premium chests
- **DECISION**: The shop sells a special key for 150 gems (the store card's price). Gems now come from Lord Blobert (50)
  and vault great chests (10). Each key carried into a run places one premium chest on floors 2-4 at a hash-chosen empty
  tile, so the rest of the floor is unchanged. Only a key opens it: 4 taps, 5 rewards, key consumed. Unused keys return.
- **WHY**: the chest-and-key pack; gives gems a use, which D-025 left pending.
- **TESTS**: `PremiumChestTests` (priced in gems, carried one per floor, placed once and hidden, nothing else on the
  floor changes, key required and consumed, unused key returns, great chests hold gems, keyed runs still winnable).
- **NOT YET**: the premium key and mega chest from the same sheet.
- **AMENDED (D-031)**: the shop also opens during a run, from the game screen; what is bought waits for the next run.

## D-027 Levels, experience and talents
- **DECISION**: Runs earn XP (monsters 3, Blobert 30, floors walked down 10, a win 20), banked at run end. Levels at
  50 × the triangle numbers; one talent point per level; five talents (Tough, Stocked, Quick Shield, Fleet, Lucky) applied
  when a run starts and never spent; free reset. Level badge on both portraits; TALENTS in its reference slot, with the
  sample's red "!" only while a point is free (a second copy of the art has the "!" painted out).
- **WHY**: the reference's level badge and TALENTS button.
- **BALANCE**: talents make later runs easier by design; the balance guards play an empty profile.
- **TESTS**: `LevelAndTalentTests`, `SettingsMenuTests.TalentsShowTheLevelThePointsAndWhatCanBeLearned`.

## D-028 Inventory and equipment
- **DECISION**: Nine items in five slots (weapon, shield, armor, boots, trinket), each a set of starting numbers. Drops:
  Lord Blobert, vault great chests and premium chests, one item each, chosen by hash. Banked at run end; duplicates become
  25 coins; a first find fills an empty slot. Worn items apply at run start like talents. INVENTORY screen with the
  sheets' item art; INVENTORY in its reference slot on the title, which now carries all seven reference buttons.
- **WHY**: the reference's INVENTORY button, and the item sheets.
- **BALANCE**: the balance guards play an empty profile; gear makes later runs easier by design.
- **TESTS**: `InventoryTests`, `InventoryOverlayTests`. Automation's `-cdDemoProfile 1` fills its in-memory profile so
  screenshots of these screens show something.

## D-029 Daily reward
- **DECISION**: One claim per calendar day from the title's DAILY REWARD panel, in the reference's place and art (frame,
  glowing chest, CLAIM with its red "!"). A seven-day week of rewards the game already has (coins, a potion ration, a
  heart token, gems, a special key); a missed day restarts the week. The date comes from the device clock and is passed
  in, so the rules are testable; setting the clock back cannot repeat a day. HOW TO PLAY moves into the top-right menu,
  which joins settings in the reference's top-right slots.
- **WHY**: the reference title's DAILY REWARD panel and menu button.
- **BALANCE**: a week is worth 160 coins, 15 gems and three provisions; the balance guards play an empty profile.
- **TESTS**: `DailyRewardTests`.
- **LATER**: the crown and mail buttons came with D-030.

## D-030 Achievements (the crown) and mail
- **DECISION**: The crown opens eleven achievements counted over every banked run (runs finished and won, monsters
  slain, chests opened, deepest floor, coins carried out, level, gear owned). Runs now count their monsters and chests;
  the profile keeps the totals. Each achievement is earned once and mails its gift. Mail holds letters from real events
  (welcome, each level gained, each achievement); gifts wait in the letter until collected, once. The mail button carries
  the reference's red "!" only while something is unread or uncollected (the "!" is cut out on its own and the button's
  copy is painted clean). Crown and mail sit in the reference's top-right slots.
- **WHY**: the reference title's crown and mail buttons.
- **BALANCE**: gifts only reach the profile; the balance guards play an empty profile.
- **TESTS**: `CrownAndMailTests`.

## D-031 The game screen's final layout
- **DECISION**: The game screen takes the reference's slots on a 1920 × 1080 canvas: logo, portrait with level badge, HP
  bar, coin and gem counters with "+" (opens the shop), settings and menu top-right, the floor plaque, the board frame
  from 132 to 792 (scaled to 0.887), the five abilities under it, and the INVENTORY / TALENTS / SHOP bar along the bottom
  (cut from the reference with its "!" painted out; the game draws the "!" only while a talent point is free). Both sides
  of the board are clear as in the reference: Sir Clickington's speech and the floor's goal (with turn, slash and key)
  sit under the floor plaque, INSPECT shows on the right only while a tile is hovered or an ability is aimed, and WHAT
  HAPPENED moves into the menu (also offered on the defeat panel). The counters show banked coins and gems plus what
  the run has found. INVENTORY, TALENTS and SHOP open during a run and change the profile, so they outfit the next run;
  each says so. The reference's mana bar slot stays empty until its design is decided (art brief D3).
- **WHY**: "title and game page should match exactly 1:1".
- **TESTS**: the existing screen and menu tests; screenshots with `-cdOverlay hud|menu|log|inventory|talents|shop`.

## D-032 Mana replaces cooldowns
- **DECISION**: SHIELD and DASH cost mana from a pool shown under HP, as in the reference. Knight: 6 mana, shield 2,
  dash 3. Paladin: 8 mana, shield 2, dash (1 tile) 4. +1 mana at the end of every turn, a full pool on every new floor;
  move, slash, potions and chests are free. Quick Shield becomes FOCUS (+1 max mana, two ranks, same id so learned
  points stay); Fleet makes the dash 1 cheaper (never below 1). Gilded Shield gives +1 max mana; Swift Boots a cheaper
  dash. Ruleset 6; a run saved before mana continues with its class's full pool.
- **WHY**: the reference's mana bar; the player chooses when to spend instead of waiting out timers (chosen by the
  owner from three options).
- **BALANCE**: every guard passes. Hero sweep, blind novice, 40 seeds Easy / Medium / Hardcore: Knight 40 / 36 / 26
  (was 29 on Hardcore), Paladin 37 / 38 / 30 (was 39 / 38 / 33); sighted still wins every tier. Step-by-step Hardcore
  with the weakest bots is a little harder, Free Roam unchanged.
- **TESTS**: `ManaTests`, `ShieldCostsManaAndManaComesBackEachTurn`, `TheDashNeverCostsLessThanOneMana`, and the hero,
  talent and inventory tests.

## D-033 The title matches the reference; the purse's "+"
- **DECISION**: The title background is the reference title itself, blurred only over its logo (whose "2" is painted
  out first, then covered by our logo). The hero card, CONTINUE panel and crown / mail / settings / menu buttons are
  the background's own pixels: cleaned patches cut from the same places (sample name, level, amounts and floor
  inpainted out, the mail "!" mirrored away) carry live text, and invisible buttons take the taps. The CONTINUE panel
  is always shown, reading NEW RUN (floor 1) when there is nothing to continue, as in the reference. The "+" beside
  coins and gems opens an exchange: 10 gems for 150 coins, 300 coins for 10 gems (a round trip loses half), with the
  shop one tap away. No real-money store: that needs a platform store account and payment provider.
- **WHY**: "correct and remove the blurred out areas so the screen match"; "+ purchasable option for gold and gems".
- **TOOLS**: the slicer gained `inpaint_text` / `inpaint_all` (OpenCV) and, for scenes, `erase_color`.
- **AMENDED (D-034), then REVERTED at the owner's request**: the title logo went back to the blurred box with our
  logo over it. `remove_logo_two.py` is kept for the gameplay screen.
- **TESTS**: `TheExchangeTradesOneCurrencyForTheOtherAtALoss`; screenshots with `-cdOverlay coins|gems`.

## D-034 The game screen matches the reference
- **DECISION**: The gameplay background is the reference gameplay screen itself, its logo's "2" removed by
  `remove_logo_two.py`; only the sample tiles inside its stone board frame are covered, by the board's own dark floor.
  The board sits inside that frame, with cells wider than tall (184 × 136, the reference's tile proportions) and
  everything standing on them square. The reference board is 5 × 4; ours stays 5 × 5, so its cells are shorter. The
  logo, Sir Clickington's portrait, settings and menu buttons and bottom bar are the background's; the level shield,
  floor plaque and purse fields are cleaned patches with live text; HP and mana are drawn over the sample bars; the
  five ability buttons are the reference's own (the potion's sample "2" painted out and the live count in its badge;
  MOVE keeps its selected glow and dims while another ability is aimed). Another hero's face is drawn over the
  portrait. The banners beside the board stay clear: Sir Clickington's line shows for five seconds in a bubble with his
  face and fades; the goal, turn and key moved to his INSPECT (hover him) and the menu.
- **WHY**: "change the main game screen as needed to match the reference main".
- **TESTS**: the existing screen, board and effect tests; screenshots with `-cdOverlay hud`.

## D-035 Gems are rare
- **DECISION**: Lord Blobert gives 5 gems (was 50), a vault great chest 1 (was 10). Everything priced or paid in gems
  scales with it: the special key costs 25 gems (was 150), the exchange trades 1 gem for 15 coins or 30 coins for 1
  gem, the daily reward's day 6 gives 3 gems, and the achievements' gem gifts are 5 / 3 / 4 / 3.
- **WHY**: the ten-playthrough bot sat on 580 unspent gems after ten wins; the owner asked for about 5 per boss win.
- **EFFECT**: the same ten runs now end with 62 gems, about two special keys; a key takes roughly five wins, or about
  750 coins through the exchange.

## D-036 A richer shop, rarity and more gear
- **DECISION**: 38 pieces of gear (was 9) in six slots (helmet added) with five rarities, cut from the clean item sheets;
  rarer is stronger. Drops, and two new shop chests, pick by rarity weight (6 / 4 / 3 / 2 / 1). The SHOP becomes a
  screen of cards in four tabs: BOOSTS for the next run (the old ration, token and key, plus mana tonic, strength
  elixir, fortune and wisdom scrolls), GEAR (four pieces a day, priced by rarity, epic and legendary in gems), CHESTS
  (gear chest in coins, royal chest in gems, rare or better) and EXCHANGE. The purse's "+" opens the exchange tab.
  Shop chests roll on a counter kept in the profile, so reopening the game cannot re-roll one. Talent and boost bonuses
  now add together instead of the later one replacing the earlier.
- **WHY**: "a much more rich shop experience" with the supplied item, potion, scroll and chest art.
- **BALANCE**: the balance guards play an empty profile; all shop effects reach only runs started afterwards.
- **TESTS**: `ShopTests`, `SettingsMenuTests.TheShopShowsThePurseAndOnlyOffersWhatTheCoinsCover`, `InventoryOverlayTests`.

## D-037 Class talent trees and an art-led Hero Select
- **DECISION**: The five shared talents give way to a tree per class, defined as data (`TalentDefinition` in the
  catalog: path, tier, ranks, prerequisite, effect and amount; `TalentBranch` for paths; class role, playstyle,
  difficulty and colour on `HeroClassDefinition`). Knight: Blade / Bulwark / Adventurer; Paladin: Hammer / Aegis /
  Devotion; 12 talents each, tiers opening at 0 / 2 / 4 / 7 points spent, one capstone per class. Every level's point
  goes to every class. Stat talents become starting numbers; the rest become run perks (`RunState.Perks`, by
  `TalentEffect`) that the rules read in `Simulation/Talents.cs`, `Combat`, `EnemyAi`, `Chests`, `Mana`, `Hazards` and
  `RunFactory`. Old generic talents are refunded.
- **UI**: the TALENTS screen is a constellation: three paths rise from tier 1 to the capstone, every talent an icon
  node (lit / gold-ringed when learnable / dark and padlocked), with links lit as the path fills, rank pills, class tabs
  and a detail panel that shows the effect, what learning changes, why it is locked and LEARN to confirm. Icons come
  from the hero sheets' affinity panels and the core UI pack, baked round by the slicer (`"circle"`), since runtime UI
  masks crashed the player on exit. HERO SELECT is rebuilt around the full-body hero art: name, title, class and role,
  difficulty, playstyle, starting numbers, the three paths with their capstone icons and VIEW TALENTS, a roster strip,
  and the roster's other heroes shown locked as COMING SOON (art and words only; no class exists for them).
- **WHY**: "make each of the two existing classes feel mechanically unique through a visually rich class-specific
  talent system, and redesign Hero Selection around the supplied full-character artwork".
- **BALANCE**: the balance guards play an empty profile; talents reach only the class's own runs.
- **TESTS**: `ClassTalentTests`, `SettingsMenuTests.TalentsShowTheTreeLockWhatIsNotOpenAndLearnOnConfirm`,
  `SettingsMenuTests.HeroSelectShowsEveryHeroLocksTheComingOnesAndChoosesAPlayableOne`.

## D-038 A harder Knight's Trial
- **DECISION**: Knight's Trial drops its trap softening (spikes, bombs and lava hit at full strength) and puts one more
  monster on every normal floor. Blobert's Wrath's traps hit one harder than that, to keep the tiers apart.
- **WHY**: "make medium harder". The blind casual bot had drifted to winning ~88% of Knight's Trial (novice ~90%, where
  it was tuned to ~65%), and a ten-run playthrough with a growing profile won all ten.
- **MEASURED**: `DifficultySweep`, 60 blind seeds, Free Roam, won: casual 95% / 70% / 53%, novice 100% / 67% / 33%
  (easy / medium / hardcore). Lord Blobert still kills nobody in these runs; the deaths are on floors 3 and 4.
- **TESTS**: `BalanceTests.KnightsTrialLetsANovicePlayerReachBlobert` (baseline 19/30 reach, guard 16), `TiersKeepTheirOrder`.

## D-039 A harder Lord Blobert
- **DECISION**: Blobert has 18 HP (was 12), his slam also shakes the target's whole row and column, each summon brings two
  Slimelets, and his court hides 2 spike traps and a bomb. Data on `EnemyDefinition` (`SummonCount`, `SlamShakesLines`)
  and the floor-5 profile; `Board.SlamCells` takes the lines flag, so the telegraph shows every shaken tile. Ruleset 7.
- **WHY**: "make blobert harder". No bot ever died on his floor: in Free Roam a telegraphed slam is always dodged, so
  HP and damage alone changed nothing (`BalanceTests.BlobertSweep`). A wider slam on a trapped floor makes the dodge a
  blind step, and the extra minions crowd it.
- **MEASURED**: Knight's Trial, 60 blind seeds: novice wins 67% -> 45% with 12 deaths on floor 5 (was 0); casual 70% (1
  death on floor 5); the sharp bot is unchanged.
- **TESTS**: `LordBlobertTests.SlamShakesItsRowAndColumn`, `SummonBringsTwoMinions`, `DashEscapesTheSlam`.

## D-040 A profile that grows meets a dungeon that answers
- **DECISION**: three changes together. (1) Lord Blobert slams twice a cycle: slam, summon, slam, puff up
  (`EnemyDefinition.DoubleSlam`). (2) **Renown** = levels past the first + items worn; every 2 renown is a point of
  threat (max 3), set on `RunState.Threat` when the run starts. From floor 3, monsters get +1 heart per threat
  (Blobert +2) and +1 damage per 2 threat (`Simulation/Renown.cs`, `RunFactory.ApplyThreat`); telegraphs and the
  inspect text show the raised numbers. (3) Gear is rarer: Blobert drops an item 50% of the time, a great chest 35%, a
  premium chest (bought key) always (`TreasureTuning.ItemChance*`). Ruleset 8.
- **WHY**: "do all three" after a ten-run playthrough still won 9 of 10: the profile snowballed (12 items and level 7)
  and a careful player never gave Blobert an opening.
- **MEASURED**: fresh profile, Knight's Trial, 60 blind seeds: casual wins 70% -> 65% (4 deaths to Blobert), novice
  45% -> 37% (18). The ten-run casual playthrough still wins 9 of 10, now with 7 items instead of 12 and closer runs
  (one won on 2 hearts): that bot dodges every telegraph, so its hearts go to hidden traps, which renown does not touch.
- **TESTS**: `RenownTests`, `InventoryTests.GearDropsOnlySometimesExceptFromAPremiumChest`,
  `LordBlobertTests.ScriptRunsSlamSummonPuffUp`.

## D-041 Traps answer renown too
- **DECISION**: from the renown floors (3 on), spikes, lava and bombs deal +1 per 2 threat (`Renown.Trap`,
  `RenownTuning.ThreatPerExtraTrapDamage`). Pits are a choice, not a trap, and keep their fall damage. The inspect text and
  the bomb telegraph show the raised number.
- **WHY**: "scale trap damage with renown too". After D-040 the careful playthrough bot still won 9 of 10: it dodges every
  telegraph, and its hearts go to hidden traps, which renown did not touch.
- **MEASURED**: the ten-run casual playthrough wins 8 of 10 (was 9), with four wins on 1-8 hearts and a loss to Blobert
  in run 10. +1 per threat instead was tried and won 5 of 10, with four losses in a row mid-profile: too punishing.
- **TESTS**: `RenownTests.SpikesHurtMoreOnTheDeepFloorsForARenownedHero`.

## D-042 Phones: portrait as well as landscape, an app icon, Android and iOS builds
- **DECISION**: phones turn freely between landscape and upright portrait. Landscape keeps the reference-matched screens
  (D-033, D-034). Portrait builds both screens on a 1080 x 1920 stage from their unpainted layout (every HUD piece its
  own art) over portrait backgrounds cut from the scene between the landscape art's painted HUD rows
  (`tools/art-slicer/make_portrait_backgrounds.py`: the gameplay room's board frame, the title's knight at the gate). The
  board sits in the portrait room's frame; the abilities are a size up. Menus keep their landscape design on a 16:9 stage
  of their own, scaled so each panel fills the screen's width (up to 1.3x): the pause menu, shop, mail and inventory read
  well. Talents and hero select have portrait layouts of their own on the screen's stage: the talent tree stacks header,
  class tabs, tree (names under the nodes) and detail; hero select stacks the art, who they are, stats, paths, roster
  and CHOOSE. The app rebuilds both screens when the device turns
  (`ClickDungeonApp.Relayout`); on a PC a window taller than wide gets the portrait layout too.
- App icon from the wordmark (`tools/art-slicer/make_app_icon.py`), set for Windows and every Android icon kind.
- Builds: `ProjectSetup.BuildAndroid` (APK, IL2CPP ARM64, debug-signed for sideloading) and `ProjectSetup.BuildIos`
  (an Xcode project; building and signing it needs a Mac, Xcode and an Apple developer account).
- **WHY**: "build an android apk", "add an app icon using the clickdungeon logo", "rebuild it so it can do landscape and
  portrait mode", "we need to build an iOS version ... same as android version".
- **LATER**: a release keystore and an App Bundle
  for the Play Store.

## D-043 The profile is protected like the run save; vault chests get their quality
- **DECISION**: `FileProfileStore` now writes the way `FileSaveStore` does: temp file → read back to prove it parses →
  `File.Replace`, keeping the copy it replaced as `profile.json.bak` (with a copy/move fallback for file systems without an
  atomic replace). `Load` falls back to that backup, and a profile that cannot be read at all is **kept** as
  `profile.json.broken` rather than overwritten; if it cannot even be set aside, `Save` refuses instead of destroying it. A
  profile from a newer build is treated as unreadable (kept), one from an older schema loads and is written back at the
  current schema. `IProfileStore.LoadNotice` carries what happened to `GameSession.ProfileNotice`, which the title screen
  shows once — and a failed profile write now sets it too, so a purchase that did not stick is no longer silent.
- **DECISION**: chest quality is assigned by `RunFactory.AssignChestQuality`, called from `SetupFloor` **and** from
  `EnterVault`. Vaults arrive through `EnterVault`, so their chests previously kept the default Common: the great chest
  opened in 2 taps instead of the 4 that rules §13 promises, and ordinary vault chests were never rolled.
- **WHY**: audit 2 (LLMHandOff.md) — DATA-07/DATA-08 were the only findings that could permanently destroy a player's
  profile, and REL-19 was a rule the code did not keep.
- **TESTS**: `ProfileStoreTests` (backup kept and used, damaged file kept and never overwritten, newer schema kept, older
  schema loads, no delete before the replacement exists), `TileFeatureTests.AVaultsChestsGetTheirQualityLikeEveryOtherFloor`.
  Both were checked load-bearing: reverting each fix turns its tests red.
- **STILL OPEN from the audit**: REL-20 (vault guards take renown damage but get no renown hearts), REL-13/REL-14 (the
  run-end panel is lost on rotation or when a chest reveal is destroyed), DATA-11 (abandon + failed delete banks twice).

## D-044 A turn of the device keeps the run's end and its log
- **DECISION**: rebuilding the screens for a new orientation no longer loses run state that only the screen held.
  `GameScreen.Reopen` re-runs `CheckRunEnd`, so a finished run gets its VICTORY/DEFEATED panel back; `PrepareForRebuild`
  closes a chest reveal through the new `ChestOverlay.CloseNow`, which runs whatever was waiting on it (the run-end check
  rides that callback, and a destroyed overlay used to drop it); and `AdoptFrom` hands the new screen the WHAT HAPPENED
  log and the last damage source, which live on the screen rather than in the session.
- **WHY**: audit 2, REL-13/REL-14/REL-15. Turning the phone after a run ended hid the result screen, NEW RUN and WHAT
  HAPPENED; the run was already banked, and the ☰ menu still worked, so it was recoverable but wrong.
- **TESTS**: `ChestRewardSequenceArtTests.ClosingTheRevealWithoutATapStillRunsWhatWasWaitingOnIt` and
  `TappingTheRevealClosedStillRunsItOnceOnly` (load-bearing: reverting the callback turns one red). The rebuild itself is
  proved by screenshot — `-cdRelayout 1` rebuilds both screens mid-run in a shot run — because no test harness constructs
  `ClickDungeonApp` yet (audit TEST-02, still open).

## D-045 Fewer sparkle stars on the board
- **DECISION**: the four-pointed sparkle star no longer plays for picking up a key, drinking a potion, or stepping on
  spikes. Those happen every few turns and already announce themselves with a popup and a line in WHAT HAPPENED. What
  still draws an effect is the bomb blast, the exit opening and a monster waking — each of which happens once.
- The art (`fx_key_collect`, `fx_potion_collect`, `fx_spikes_trigger`) stays in the project, out of the wired list, so
  putting any of it back is a one-line change.
- **WHY**: "remove some of those star effects on main game screen".
- **TESTS**: `BoardFxTests.EventsMapToBoardEffects` now pins which events draw an effect and which do not.

## D-046 Renown reaches a vault's guards whole
- **DECISION**: `EnterVault` applies renown's threat as well as chest quality, so a vault's guards carry their extra
  hearts. A vault keeps the index of the floor it hangs off, so `Renown.Hit` was already raising their blows; only the
  hearts were missing, because `ApplyThreat` ran solely in `SetupFloor` and a vault never goes through it.
- **WHY**: audit 2, REL-20 — rules §14.1 says every monster from floor 3 on has one extra heart per threat, and the vault
  was the one room where that was not true, which made it the cheapest place for a built-up hero to farm.
- **TESTS**: `RenownTests.VaultGuardsGetRenownHeartsLikeTheFloorTheVaultHangsOff` (load-bearing: removing the call turns
  it red).

## D-047 The Knight hits for 3; the Paladin keeps 11 hearts
- **DECISION**: the Knight's slash goes from 2 to 3, and the Paladin's hearts from 12 to 11. Nothing else moves: the
  Paladin keeps the stronger potion (6), the larger mana pool (8) and the one-tile dash; the Knight keeps the two-tile
  dash for 3 mana.
- **WHY**: "make the paladin and knight more even". `HeroSweep` and a new `ClassSweep` showed the classes were not close:
  blind novice, 40 seeds, Knight's Trial — Knight 11 wins, Paladin 22, on the same dungeons, and 3 against 8 on Blobert's
  Wrath. The Paladin's hearts and potions beat the Knight's reach for a player who cannot see what is coming.
- **WHY THIS LEVER**: four sweep rounds. Giving the Knight hearts or potions (the Paladin's own strengths) evened the
  numbers but blurred the classes; matching both made the Paladin strictly worse. Damage keeps each class in its lane —
  the Knight ends fights sooner and so takes fewer hits.
- **MEASURED** (40 seeds, blind, Knight's Trial): casual 26 → 33 Knight against 35 → 34 Paladin; novice 11 → 22 against
  22 → 18. Blobert's Wrath, novice: 3 → 7 against 8 → 8. Overall win rate rises about 8%: the classes were evened by
  lifting the weaker one, not by cutting the stronger one down. Tightening the tier to compensate was swept (every enemy
  +1 heart, traps +1, boss +4) and left for a separate decision — each of those re-opened the class gap or overshot.
- **TESTS**: `BalanceTests.TheClassesWinAboutAsOftenAsEachOther` (a real guard: 40 seeds per class at two skill levels,
  fails if one class wins more than 8 runs more than the other), `ClassSweep` as the tuning aid.

## D-048 The balance bot stops walking into the same wall
- **DECISION**: `AutoPlayer` remembers travel that got it nowhere. When a Move or Dash leaves the hero on the tile it
  started from and uncovers nothing, that (tile, command) pair is not tried again on that floor. A bump that reveals what
  blocked it counts as learning something, so it is not remembered; waiting, shielding and slashing in place never were
  travel and are untouched. The memory is cleared on every new floor and on entering or leaving a vault.
- **WHY**: MAINT-15. Watch-mode run 5 (seed 20300515) was traced turn by turn: the bot stood on a teleport pad, walked
  onto its pair, was sent straight back, and repeated for ~200 turns until it died — with the key three steps away. That
  same run now wins. The bot is the instrument behind every balance decision, so its stalls were being read as difficulty.
- **RE-BASELINED** (60 blind seeds, `DifficultySweep`, won): casual 97% / **92%** / 48% and novice 95% / **55%** / 15%
  for Easy / Knight's Trial / Blobert's Wrath — against 95% / 67% / 45% and 100% / 30% / 5% before the fix. **The game is
  considerably easier than the old numbers said**: much of what D-038 and D-040 were tuning against was the bot failing,
  not the dungeon winning. `HeroSweep` (40 blind novice seeds) keeps D-047's parity: Knight 37 / 20 / 9, Paladin 39 / 17 / 10.
- **NOT DONE**: retuning the tiers to the honest numbers. That is a separate decision, and it should come with a view on
  what win rate Knight's Trial is meant to have for a careful player.
- **KNOWN REMAINDER**: the bot still wanders a floor it cannot solve (one traced run spends ~300 turns circling floor 2
  without finding the key). That is weak exploration, not a loop, and it still inflates turn counts.
- **TESTS**: guard thresholds and the rules §10.2 table re-measured; `TheClassesWinAboutAsOftenAsEachOther` re-checked
  against the new numbers; `PlaythroughTests.TraceWatchRun` is the aid that found it.

## D-049 The bot visits a vault once, and takes a pit when a floor has nothing left to give
- **DECISION**: two more fixes to `AutoPlayer`, both found by tracing watch-mode run 5 (seed 20300515). It now remembers
  the doorway of a vault it has already been inside, so it stops walking back in every time it feels healthy — that run
  crossed the same doorway three times and spent 430 turns on one floor. And when every tile on a floor is uncovered and
  the key is still not in hand (it is under an actor, or across lava), a pit becomes the goal: the only way down without
  a key, instead of circling. The vault memory is cleared on a real floor change, not on stepping in and out of a vault.
- **MEASURED**: that run falls from 587 turns to 317. Tier numbers barely move (Knight's Trial casual 92% → 90%), so the
  fix removes wasted turns rather than changing how hard the game is.
- **REJECTED**: a general "uncover urgency" that paid the bot for revealing tiles. At every weight that stopped the
  wandering it also made the bot walk onto unknown tiles heedlessly — Knight's Trial casual wins fell 90% → 47% at 120,
  and to 25% at 200. A bot that dies exploring is a worse stand-in for a careful player than one that dawdles.
- **KNOWN REMAINDER**: on a floor whose key it cannot find, the bot still shuffles between tiles it has already seen. It
  survives, but it burns turns; fixing it needs a real exploration plan, not a score tweak.

## D-050 Knight's Trial: traps bite
- **DECISION**: Knight's Trial's traps hit one harder (spikes 3, bombs 5, lava 3) on top of D-038's extra monster a
  floor. Blobert's Wrath is unchanged: its gap is now the tougher monsters, the mightier boss and the missing potion
  rather than the traps, and its tagline says so.
- **WHY**: "make medium harder", now that D-048/D-049 made the bot an honest instrument. A careful player was winning
  90% of Knight's Trial, which is not a trial.
- **MEASURED** (60 blind seeds, `DifficultySweep`, won): Easy 97% casual / 100% novice · **Knight's Trial 78% / 35%**
  (was 90% / 52%) · Blobert's Wrath 45% / 15%. Deaths spread across floors 1-5 rather than piling on the boss, which is
  why traps were chosen over more monster hearts (`M1`) or damage (`M2`) in the sweep.
- **CLASS PARITY HOLDS** (`HeroSweep`, 40 blind novice seeds): Knight 12 wins on Knight's Trial, Paladin 14.
- **TESTS**: `KnightsTrialLetsANovicePlayerReachBlobert` re-baselined to the 52% reach measured now;
  `TiersKeepTheirOrder` and `TheClassesWinAboutAsOftenAsEachOther` re-checked.


## D-051 A watched or screenshotted run never writes the device's preferences
- **DECISION**: `UserPrefs.ReadOnly` is set once in `Awake` whenever the app runs in automation (`-cdShot` or
  `-cdWatch`), and every preference setter refuses while it is set. In the game screen, automation also ignores the
  keyboard, refuses board and ability taps, and refuses the two actions that tear the run down (QUIT TO TITLE,
  ABANDON). The menu and settings UI are still built and still open, so an automated shot shows what a player sees.
- **WHY**: audit 3 (REL-28, REL-29, DATA-16). Automation already kept its saves and profile to itself, but the
  settings panel stayed live and `UserPrefs` had no automation branch, so toggling PLAYTEST LOG during a demo turned a
  tester's telemetry off for good — silently, because automation does not read the setting it had just written. A
  single Escape tap opened the pause menu instead of stopping the batch, which blocked the bot for the rest of the run;
  QUIT TO TITLE nulled the screen the batch was still submitting into and quit the process with no summary.
- **REJECTED**: hiding the menu and settings UI in automation. The first cut did, and review caught that it silently
  broke `-cdOverlay pause` and `-cdOverlay menu` and stripped the gear from every portrait screenshot. Guarding the
  destructive *actions* keeps the screenshots honest and still leaves a stray click nothing to break.
- **VERIFIED** by real key presses into a running batch (scan-code `SendInput`; Unity's Input System ignores
  virtual-key injection): a 60 ms Escape tap is missed by the once-a-move poll and the run plays out untouched; a held
  Escape quits cleanly. At the shipped 0.5 s pace, hold the key briefly rather than tapping it.

## D-052 Riposte answers every blocked blow
- **DECISION**: Riposte moved inside `Combat.DamageHero` beside Holy Bulwark, so a blocked fire-imp shot and a
  blocked Blobert slam are answered as well as a blocked sword. A hazard passes no attacker, so a blocked bomb still
  answers nobody. A riposte that kills leaves no "dazed" corpse behind.
- **WHY**: audit 3 (REL-31). Riposte and Holy Bulwark were written with the same promise — "a blocked attack" — but
  Holy Bulwark paid on every block and Riposte only on melee, so the Knight's BULWARK path did nothing against the
  whole slam cycle of the fight it is built for. Asked and answered: the talent keeps its promise rather than having its
  text narrowed to "a blocked melee attack".
- **NOT MEASURED BY THE SWEEPS**: the balance bot plays with no talents, so no sweep number can move (see D-056).

## D-053 The balance guards assert what they measure
- **DECISION**: `GuardBaselineTests` is renamed `GuardBaselineReports`, with a note that nothing in it asserts
  anything. The class-parity guard takes whichever of its two fences is tighter at the measured scale — within 8 wins,
  and within 5 : 8 — and the tier guards now also cap how many runs ran out of commands.
- **WHY**: audit 3 (TEST-06, TEST-07). All fourteen `[Explicit]` "tests" were printers, so un-skipping them would have
  added unconditional passes, not guards. The parity band was an absolute ±8 against counts D-050 had halved, so a
  2× split (8 against 16) passed the check written to refuse it; a ratio alone would have been looser than ±8 above
  13 wins, which is why both fences are kept.
- **HONEST LIMIT**: the stall ceiling is a cheap tripwire, not the regression test for the D-048 teleport loop.
  Reverting D-048's fix leaves the guards green, because they run 30 seeds and that loop was found on a seed they
  never touch. It is a ceiling (`1 + runs/30`), not zero, because a healthy bot still stalls once or twice in 60 seeds.

## D-054 Levels stop at 500, and banked experience stops with them
- **DECISION**: `Progression.MaxLevel = 500`. The level search is bounded by it, the store clamps a loaded profile's
  experience to the cost of that level, and `BankXp` clamps to the same ceiling so the value held in memory can never
  be one the curve cannot express.
- **WHY**: audit 3 (DATA-17, DATA-20). The curve's intermediate product overflowed at level 6555, after which it
  could never exceed 2^30 — so for any experience total above that, the level search never ended, on the main thread,
  inside the session's constructor, before any screen existed. The store's clamp left `BankXp` still able to wrap a
  capped profile negative for the rest of a session. Both need a hand-edited profile; neither is reachable by play.

## D-055 Epic gear costs 21 gems
- **DECISION**: Epic gear rose from 15 to 21 gems. The price ladder now reads, in coins at the shop's own exchange:
  Common 150 · Uncommon 300 · Rare 600 · Epic 630 · Legendary 900.
- **WHY**: audit 3 (MAINT-16). The shop sells gems at 30 coins each, so at 15 gems an Epic piece cost 450 coins —
  cheaper than a 600-coin Rare one from the same screen on the same day. The ladder inverted exactly where it changed
  currency. Asked and answered: the smallest change that keeps coins-then-gems, rather than moving all gear to coins.
- **TEST**: `GearPricesRiseWithRarityInASingleCurrency` reads every rarity through the exchange and fails if any rung
  is not dearer than the one below it.

## D-056 Difficulty re-measured after audit 3
- **MEASURED** at `3535907` (60 blind seeds, `DifficultySweep`, Free Roam, reach F5 / won):

  | Player  | Squire's Stroll | Knight's Trial | Blobert's Wrath |
  |---------|-----------------|----------------|-----------------|
  | casual  | 97% / 97%       | 80% / **75%**  | 63% / **43%**   |
  | novice  | 100% / 100%     | 52% / **32%**  | 35% / **18%**   |

  Class parity (`ClassSweep`, 40 blind seeds, Knight's Trial, won): casual Knight 30 · Paladin 30; novice Knight 11 ·
  Paladin 16.
- **WHAT MOVED IT**: the floor validator now knows a teleport pad carries the hero to its partner (REL-27), which
  changes which generated floors are accepted for some seeds. It rescues more floors than it rejects. Net effect is
  about three points a tier against D-050 (Knight's Trial was 78% / 35%, Blobert's Wrath 45% / 15%). **No tier was
  retuned**: the tiers still sit where D-050 put them.
- **WHAT CANNOT MOVE IT**: `AutoPlayer.PlayRun` starts every run with an empty `Perks` dictionary, so no talent fix
  can move a sweep number — `ClassSweep` is digit-for-digit identical with and without the Judgement fix (REL-23).
  That is also why Judgement could be dead through four difficulty decisions without any guard noticing. Where talents
  do show, on the growing profile (`TenPlaythroughs`, 30 seeds, a talented Paladin), Judgement alone moved wins from
  18 to 23.
- **AND A WATCH BATCH AGREES**: ten watched runs at `3535907` came out byte-for-byte identical to the same seeds before
  the repairs, except the first run in which the Paladin held Judgement. The simulation stayed deterministic through a
  batch that changed the validator, chest rewards, a talent and combat.

## D-057 Ironheart is the Knight; Sir Clickington is the mascot
- **DECISION**: Ironheart is the first hero on the roster and the default Knight. Sir Clickington is no longer a playable
  hero: he is the game's mascot — painted into the title and HUD backgrounds, standing under the title arch, and the voice
  of its letters — and belongs to a comedy campaign still to be built. Ironheart comes off the "coming soon" roster.
- **WHY**: the user's call ("Ironheart is number 1 — Sir Clickington is meant as a game mascot and additional comedic game
  campaign"). It also fixes a live bug: hero-voice lines named Sir Clickington outright, so a Dawnward run ended "Sir
  Clickington has fallen". Those lines — the death log, the "1. Dungeon: 0." quip, the victory and defeat quips — now use
  the playing hero's name, and the Step-by-Step refusal and HOW TO PLAY no longer name anyone.
- **TWO CONSTANTS, NOT ONE**: `ArtKeys.HeroId` had meant both "the default hero" and "the hero painted into the
  backgrounds". Repointing it alone would have hidden Ironheart's face on the HUD and shown the painted mascot instead.
  `ContentCatalog.MascotId` now carries the second meaning, and the screens that lay a face over the painted art compare
  against it.
- **SAVES**: `ContentCatalog.RetiredHeroes` maps `sir_clickington` to `ironheart`, applied as a save loads, so a run in
  progress is not refused as an unknown hero. Both are Knights, so only the face changes. A stored hero preference needs no
  migration: an unknown choice already falls back to the default.
- **ART**: cut from `Iornheart.png` (`IronHeart1.png`'s extra animations are off-model and unused). The sheet's dark armour
  on a dark floor defeated the slicer's colour threshold — any tolerance loose enough to clear the floor also ate the
  armour — so sprite mode gained a `grabcut` option that models colour and edges together.
- **NOT DONE**: the chest reward sequence still shows Sir Clickington's drawn reaction poses whoever plays, because
  Ironheart's sheet has no chest-reaction art. His title and tagline ("The Iron Vanguard", "Sturdy. Stubborn. Unbroken.")
  are placeholders written for this change, not from the sheet.

## D-058 The first expansion monsters: Skeleton Warrior, Goblin Bomber, Armored Boar
- **DECISION**: three new monsters, each adding exactly one rule, each telegraphed the turn before it lands:
  - **Skeleton Warrior** (3 HP, 2 dmg, slow): its first fall leaves bones at 1 heart for 2 of its turns. Break them
    and it is gone; leave them and it stands up at half its hearts, and the next fall is final. Experience counts only
    for the final kill.
  - **Goblin Bomber** (2 HP, 1 dmg): keeps its distance and lobs a lit bomb onto the hero's tile from up to 3 away.
    The bomb lands lit and blows the turn after, so there is always a turn to get clear. Reuses the bomb hazard.
  - **Armored Boar** (5 HP, 3 dmg, range 4): charges down any clear straight line to the hero. The whole path is
    marked; the hero anywhere on it takes the blow. Winded for a turn after.
- **WHY THESE THREE FIRST**: a vertical slice, chosen so each stretches the engine differently - a passive rule on an
  existing mover, a new attack shape through the telegraph system, and an enemy creating an existing hazard - so the
  whole pipeline is proved once before the other monsters are built. The user skipped the scoping questions; this was
  the recommended default, stated when it was taken.
- **WHAT HAD TO CHANGE ALONG THE WAY**: the board drew a charge in the bomb-blast style (it fell through to the last
  case), and drew nothing at all where a bomb was about to land (no damage, so no branch). Both are fixed. New intents
  borrow the nearest existing icon instead of looking up art that does not exist.
- **ART**: Skeleton and Boar from their encyclopedia sheets, the Bomber from the Common Encounter Monster Pack; five
  poses and a portrait each. A fallen skeleton shows its defeat pose, which on its sheet is literally a pile of bones.
- **VERSIONS**: Ruleset 8 -> 9, Generation 2 -> 3, content catalog 1 -> 2, so a save from before this is refused
  cleanly rather than resumed into a dungeon that no longer matches it.
- **TESTS**: 13 in `ExpansionMonsterTests`. Disabling each mechanic in turn turns its tests red: the collapse (4), the
  charge (2), the bomb landing (3), and the charge telegraph (1).

## D-059 The dungeon grows to seven floors
- **DECISION**: seven floors instead of five, so each new monster has a floor that introduces it before it turns up
  alongside the others. Blobert's Court is always the last floor.

  | Floor | Name | New here |
  |---|---|---|
  | 1 | The Upper Halls | - |
  | 2 | The Damp Cellars | - |
  | 3 | **The Bone Crypt** | Skeleton Warrior |
  | 4 | The Ember Vaults | Goblin Bomber |
  | 5 | The Locked Depths | - (everything so far, mixed) |
  | 6 | **The Boar Warrens** | Armored Boar (no lava: a boar needs open lines) |
  | 7 | Blobert's Court | - |
- **WHY**: the user's call ("with new monsters expand the dungeon accordingly"). Crowding three new monsters into the
  two floors before the boss made the difficulty curve a cliff at floors 4-5.
- **KNOCK-ONS**: premium chests may now appear on floors 2-6 (was 2-4). The balance bot's command cap is now 80 a
  floor rather than a flat 400: held at 400 over seven floors, slow winning runs were cut off and counted as stalls.
  Twelve tests that meant "Blobert's floor" by writing `5` now say "the last floor".
- **MEASURED** (60 blind seeds, `DifficultySweep`, won; was D-056):

  | Player | Squire's Stroll | Knight's Trial | Blobert's Wrath |
  |---|---|---|---|
  | casual | 100% (was 97%) | 77% (was 75%) | 52% (was 43%) |
  | novice | 97% (was 100%) | 28% (was 32%) | 12% (was 18%) |

  Class parity on Knight's Trial: casual Knight 30 / Paladin 32, novice 13 / 12 - the closest the two have been.
  A longer run gives a careful player more chests and levels to grow into, and a careless one more floors to die on.
  **No tier was retuned.**

## D-060 Sir Clickington can still be picked
- **DECISION**: the mascot is playable again, listed last on hero select after the heroes. D-057 had retired him to the
  title screen; the user asked that he stay selectable "for now", ahead of his own comedic campaign.
- **HOW**: he is a hero identity again (Knight class, "Brave. Loyal. Clickable."), `RetiredHeroes` is empty, and
  Ironheart stays the default and first on the roster. A run started as Clickington continues as Clickington.
- **TESTS**: `IronheartLeadsTheRosterAndTheMascotCanStillBePicked`, `ARunStartedAsTheMascotContinuesAsTheMascot`.

## D-061 The second wave of monsters
- **DECISION**: every monster with a reference sheet is in. Each brings one new rule, telegraphed a turn ahead:
  - **Mimic Chest** (4 HP, 3 dmg): asleep, it looks and behaves exactly like a closed chest - drawn as one with its tap
    meter, sensed as treasure, and every command a chest would accept is accepted. Tapping, opening, dashing onto it or
    stepping beside it wakes it. 15 coins when it falls.
  - **Cave Spider** (3 HP, 2 dmg): webs the hero's tile from up to 3 away. Webbed, the hero cannot Move or Dash on
    their next command; Slash, Shield, Potion and Wait all still work.
  - **Spooky Spellbook** (3 HP, 2 dmg): fires down lanes as an imp, and every third action calls a Spectral Page
    (1 HP) beside it, never more than two.
  - **Goblin Key Warden** (3 HP, 1 dmg): on floors 4, 8, 14 and 18 it holds the key instead of the key lying on a
    tile. It backs away, resting after each step so it can be caught; the key drops where it falls.
- **WHY THE MIMIC NEEDS PARITY**: a mimic that answered even one command differently from a chest (a refused tap, a
  missing meter, a different clue) would be a free, riskless test for every chest. A parametrised test sends every
  command to a mimic and a real chest side by side and requires the same answer.
- **THE BOT**: the sighted bot stalled on warden floors - it never walked toward a monster it needed. Its goal
  distance now treats a boss or warden as reached from any tile beside it.
- **ART**: all four from their sheets (the warden from the Common Encounter pack); five poses and a portrait each,
  plus the page. A sleeping mimic draws the game's own chest art, never the mimic's closed pose, which differs.
- **TESTS**: 19 in `SecondWaveMonsterTests`. Removing each rule in turn - the tap, dash and open parity, the web,
  the key drop, the page summon, the mimic sleeping when seen - turns its tests red.

## D-062 Twenty floors in four acts, a boss at the end of each
- **DECISION**: the user's call ("bosses every 5 or every 10"): a boss every five floors, twenty floors in all.

  | Act | Floors | Newcomers (floor) | Boss |
  |---|---|---|---|
  | I | 1-4 | Mimic (2), Skeleton (3), Key Warden (4) | 5: Goblin Brute King |
  | II | 6-9 | Bomber (6), Spider (7), Key Warden (8) | 10: Bat Swarm Leader |
  | III | 11-14 | Spellbook (11), Boar (12), Key Warden (14) | 15: Theater Curtain Demon |
  | IV | 16-19 | nothing new; everything, and more of it; Key Warden (18) | 20: Lord Blobert |

  Each act brings its newcomers in one floor at a time, so no floor asks the player to learn two new rules at once.
- **THE BOSSES** (rules §3.6): the **Goblin Brute King** (10 HP) charges and slams, and enrages at half his hearts
  (+1 to every blow, shown in its warning); the **Bat Swarm Leader** (13 HP) calls bats, at most four, and dives
  down lines; the **Theater Curtain Demon** (16 HP) calls masks, brings the curtain down across the hero's whole row
  and column, and vanishes to a tile it marks the turn before.
- **ACT CLEARED**: beating an act boss restores full hearts and mana. Without it the first twenty-floor measurements showed
  a wall at floors 5-6, the King and the first floor after him. The King was also
  softened (12 HP / 3 dmg to 10 / 2) - the first boss should teach, not gate.
- **NO FALLING PAST A BOSS**: a pit on a boss floor is a pit, never a way down.
- **KNOCK-ONS**: premium chests may appear on floors 2-19. Hard-coded command caps (400, 1500) in tests and the watch
  loop now scale with the floor count. "Lord Blobert" in the goal line, the sealed exit and HOW TO PLAY now names the
  floor's own boss. **VERSIONS**: Ruleset 9 -> 10, Generation 3 -> 4, content catalog 2 -> 3. The seven-floor
  game went out in the D-058 builds, so a save from it is refused cleanly rather than resumed into floors that no
  longer match it.
- **MEASURED** (240 blind seeds, `DifficultySweep`, won; was D-059 on 60):

  | Player | Squire's Stroll | Knight's Trial | Blobert's Wrath |
  |---|---|---|---|
  | casual | 100% (was 100%) | 69% (was 77%) | 51% (was 52%) |
  | novice | 100% (was 97%) | 15% (was 28%) | 11% (was 12%) |

  No stalls in any tier. **60 seeds is too few over twenty floors**: the same code measured 80% and then 55% for a
  casual player on Knight's Trial on two sets of 60 (the generation bump deals every seed a new dungeon). The guard
  on novices reaching the last floor was re-based on the 240-seed rate.
  The **Goblin King is the novice wall**: of their deaths, 41 of 205 on Knight's Trial and 61 of 214 on Blobert's
  Wrath come on floor 5. For novices the two harder tiers are now close (15% and 11%). **No tier was retuned**;
  the King is the first thing to look at when one is.

- **TESTS**: 9 in `ActBossTests`. Removing each rule in turn - the act-clear heal, the no-pit rule, the enrage, the
  enraged warning, the bat cap, the vanish - turns its test red.

## D-063 The six new heroes, and a rule for each class
- **DECISION**: every hero on the roster sheet is playable. The sheet is what named them, so the class names follow it:
  Shadowcut is a **Rogue**, not the "Thief" his own sheet says. `ComingSoon` is empty for the first time.

  | Hero | Class | Class rule | Hearts / slash |
  |---|---|---|---|
  | Shadowcut | Rogue | **Ambush**: +2 against a monster whose declared action is not aimed at your tile | 10 / 2 |
  | Emberwisp | Wizard | **Firebolt**: slash reaches 3 tiles down a line, and knocks the target back one | 9 / 3 |
  | Windsong | Ranger | **Longshot**: slash reaches 4 tiles, +1 from 3 or more away | 8 / 3 |
  | Lightbringer | Cleric | **Sanctuary**: every blow her shield blocks heals 1 | 8 / 2 |
  | Rageclaw | Berserker | **Rage**: +1 for every 4 hearts missing | 9 / 2 |
  | Gearspark | Engineer | **Spark Drone**: on every turn he does not slash, it zaps a neighbour for 1 | 7 / 1 |

- **HOW A CLASS RULE WORKS**: as a perk the class starts every run with, so the simulation reads a class rule and a
  learned talent through the same call, and a talent that sharpens a rule (Cruel Edge, Overclock, Blessed Ward) simply
  raises the same perk. Saves and the bot's own copy already carried perks, so neither needed a new field.
- **RANGED SLASHES AND THE COVER RULE**: a shot only crosses tiles the player has already uncovered. Letting one fly
  over a cover would have made a refused shot a free probe - "there is a wall under that one" - which §2.1 forbids.
  The same reason keeps a knock-back from pushing a monster onto a cover.
- **WHY THE DRONE SKIPS THE TURNS YOU SLASH**: as a zap after every action it was simply free damage, and the Engineer
  won 60 of 80 against the Knight's 47. Covering only the turns he spends moving, shielding or drinking makes it a
  choice, and it reads as the drone doing the fighting while he does something else.
- **TALENTS**: 72 new ones, twelve a class in three paths, tiers and capstones as D-037 set out. Most reuse effects the
  simulation already had; thirteen are new (Eviscerate, Dodge, Pickpocket, Fireball, Piercing Arrow, Pinning Shot,
  Hawkeye, Bloodlust, Long-Range Coil, Tesla Coil, Max Mana, and the two that raise a class rule).
- **BALANCE**: tuned over four sweeps of 40 to 80 blind seeds against the Knight and Paladin. The Cleric and the
  Engineer were far too strong on the first numbers (36 and 36 of 40, against the Knight's 26), the Rogue and the
  Wizard too weak. What moved them: the Cleric's mana and shield cost, the Engineer's drone rule and slash, and a
  heavier slash for both shooting classes, who rarely get a clear uncovered line in Free Roam.
- **MEASURED** (40 blind seeds each, Knight's Trial, no talents, `ClassSweep`) - casual / novice wins:

  | knight | paladin | rogue | wizard | ranger | cleric | berserker | engineer |
  |---|---|---|---|---|---|---|---|
  | 26 / 5 | 23 / 8 | 26 / 2 | 26 / 5 | 25 / 5 | 30 / 8 | 23 / 9 | 22 / 7 |

  The Rogue punishes careless play hardest (2 novice wins) and is marked the most demanding class; the Cleric and the
  Berserker are the most forgiving. `TheClassesWinAboutAsOftenAsEachOther` now guards all eight, not just the first two.
- **ART**: each hero's sheet gives a full portrait, two expressions and six poses. The bright sheets cut out on a colour
  threshold as Dawnward's does; Shadowcut's black leather, Rageclaw's browns and Gearspark's dark gear are cut with
  GrabCut instead, seeded on the figure, because any threshold loose enough to clear their backdrop ate the hero.
- **VERSIONS**: Ruleset 10 -> 11, content catalog 3 -> 4. Floor generation is untouched, so seeds still deal the same
  dungeons; a save naming one of the new heroes is refused by an older build.
- **TESTS**: 24 in `HeroClassTests`. Breaking each rule in turn - all 28 of them, every class rule and every new talent -
  turns one of them red.

## D-064 The vault is a room, and the hero's face shows what the run is doing to them

- **THE ROOM**: a vault behind a door is now **nine tiles, three by three**, in the middle of the board, and the door
  the hero came through is the **middle tile of the nine**. It is always open and it is the way back out; the hero
  arrives on one of the four tiles beside it, because nobody ever stands in a doorway. The old vault was the whole
  5x5 board with its stair dropped somewhere two or more tiles from a random entrance, which read as another floor
  rather than a room you had stepped into.
- **THE ONE REAL WALL IN THE GAME**: every dungeon floor is the whole board and generates no walls at all - the wall
  sprite is the cover over an unrevealed tile (D-021, rules 2.1). A room with an edge needs that edge to be real, so
  the sixteen tiles around a vault are `Terrain.Wall`, `HeroCanEnter` refuses them (it refused nothing but actors and
  a locked door before), and they are written **Revealed at generation**: stone the player can click is a cover, and a
  cover has to hide something. Inside the nine, covers work exactly as they do anywhere else.
- **GUARDS AND CHESTS**: unchanged in number - two or three awake guards, one great chest or two or three ordinary
  ones. The guards keep the same two tiles of distance from the tile the hero arrives on that they keep on any floor,
  which in a room this size puts them on the far side of it. Closed chests never blocked the hero, so a room this
  small cannot box them in.
- **THE BOT WOULD NOT LEAVE**: scoring only counted the board the hero stands on, so the monsters left outside
  **vanished** while they were in the vault, and stepping back out - which puts them all back - scored worse than
  staying. The flat -2000 for being in a vault was the only thing pushing it out, and a heavy floor outweighed it. In
  the old 25-tile room the bot blundered onto the stair anyway; in nine tiles it circled until the command cap. The
  fix is to count `run.OuterFloor`'s monsters too, so the two boards compare honestly; a blind bot's redaction now
  hides that floor's sleepers as well, or it would be counting what the player cannot see.
- **A GUARD ON THE WELCOME MAT**: a vault is kept exactly as it was left (REL-26) and its guards keep walking about,
  so a second visit could put the hero on top of one. Three of four arrival tiles is a coin flip in a room this size;
  the hero now takes the arrival tile only if it is free, and any free tile in the room otherwise.
- **VALIDATION**: the shape is a rule, not a habit - `FloorValidator` checks the nine tiles, the stone around them,
  the door at the middle and the hero beside it, so a malformed room can never reach a player.
- **EXPRESSIONS**: 48 portraits, six for each of the eight heroes who are not the mascot - confident, angry, shocked,
  worried, victorious, defeated. A sheet holds five animation frames and two portraits, so `shocked` and `worried`
  both come off the hit frame, tight and wide; `confident` comes off the master pose. Two automated passes failed
  before the faces were read by hand off zoomed, grid-labelled strips of each sheet: a head box taken as a fraction of
  the frame catches hair and backdrop as soon as a pose leans, and an alpha bounding box catches the sheet's own
  section labels ("ATTACK", "VICTORY") and the spell effects around the figure.
- **PARITY GUARD**: raised from 40 seeds a class to 100. The room change moved the numbers a little and the guard landed
  exactly on its ceiling - paladin 18, cleric 28 of 40, ratio 1.56 against 1.6 - which is the state audit 4 built the
  tripwire for. 150 seeds of the same build put the eight classes between 53% and 69% (rogue 80, cleric 103), a ratio of
  1.29, so the spread was the sample, not the classes. The guard now measures what it asserts.
- **MEASURED** (150 blind seeds each, Knight's Trial, casual, no talents): knight 101, paladin 91, rogue 80, wizard 89,
  ranger 84, cleric 103, berserker 85, engineer 86.
- **VERSIONS**: Ruleset 11 -> 12. Floor generation is untouched, so seeds still deal the same dungeons; a run saved
  inside an old vault still loads and finishes in the room it was saved in, because an older ruleset resumes under the
  current rules and that room breaks none of them that the game enforces at runtime.
- **TESTS**: six in `VaultRoomTests`. Widening the room, leaving the stone as a cover, letting the hero walk into it,
  spawning guards in their face, arriving on the door, dropping the outer floor from the bot's scoring and putting the
  returning hero back on the arrival tile regardless - each turns one of them red.

## D-065 The face in the bubble is the face of whoever is playing

- **THE FACES WERE CUT BUT HALF-USED**: D-064 cut 48 expression portraits and the catalog carries all 72 (nine heroes
  by eight faces), but two of the eight only ever appeared as the run ended - Victorious on a win, Defeated on a death -
  and two places drew the **default hero's** face over every run whoever was playing: the chest overlay's reaction and
  the HUD portrait it is built with. Playing Rageclaw, Ironheart celebrated your loot.
- **ONE PLACE DECIDES WHOSE FACE IT IS**: `GameScreen.SpeechPortraitKey(heroId, face)` - their own face for that
  expression, then their neutral one, and only then the default hero's. A hero short of a portrait keeps their own face
  with the wrong feeling rather than borrowing another hero's with the right one, because the face is who is speaking.
  `ChestOverlay.HeroId` carries the same hero to the chest, set on every refresh so a chest opened from anywhere
  celebrates in the right face.
- **TWO FACES THAT HAD NOWHERE TO APPEAR**: a **boss going down** now takes the turn's line at priority 90 - "Goblin
  Brute King is down!" with the victorious face - rather than sharing the ordinary kill line at 30. A **blow worth four
  hearts or more** answers angry rather than worried; below that the old worried line stands, and at three hearts or
  fewer the shocked one still wins.
- **THE GUARD THAT MATTERS**: `EveryHeroHasAFaceForEveryExpressionADialogueLineCanAskFor` reads the **shipped** art
  catalog, not a stub, and fails if any of the 72 is missing - the fallback above is exactly what would otherwise hide
  it. A second test pins the `Expression` enum to the list of expressions the slicer cuts, so adding one to either side
  alone is caught.
- **TESTS**: six in `SpeechPortraitTests`. Four mutations - the bubble and the chest each forced back to the default
  hero, an expression dropped from the art list, and one portrait renamed in the built catalog - each turn a named test
  red.

## D-066 A PlayMode assembly, so a drawn tile can be looked at

- **THE GAP**: `ClickDungeon.Unity.Tests` is `includePlatforms: ["Editor"]`, so nothing in the repo could stand up a
  board and read what it drew. That is why REL-38, REL-39, REL-42 and MAINT-32 were all recorded **COMPILED** in audit
  4 - fixed by reading the draw path, never seen - and the vault's wall ring (D-064) would have joined them.
- **THE ASSEMBLY**: `ClickDungeon.Unity.PlayTests`, all platforms, referencing the game rather than the editor, run
  with `-testPlatform PlayMode`. `BoardView` needs only a `RectTransform` and a `MonoBehaviour` host, and `Render`
  takes the threat list straight, so a test can hand it exactly the warnings it wants to see drawn.
- **WHAT THE FIRST THREE PIN**: a web and a landing bomb stay readable on a tile that is also being hit, each on its own
  row (REL-38); a cover keeps the warnings whose extent reads the board and not the geometric ones (REL-42); and the
  stone around a vault is not drawn like a cover, because a cover is a tile the player is invited to click (D-064).
- **THE LABELS ARE NOT ON THE TILE**: warning labels are parented to the board's label layer, named `Labels x,y`, so
  they draw over the tiles around them. The first version of these tests looked under `Cell x,y`, found nothing, and
  failed - worth knowing before writing the next one.
- **TESTS**: three in `BoardDrawTests`. Reverting each fix to the bug it was written for - the web erased by a blow, the
  warnings drawn over covers, the stone tinted like a cover - turns exactly one of them red.
- **HOW TO RUN**: `-runTests -testPlatform PlayMode` alongside the EditMode pass; the kit gate still runs the headless
  suite only, which is unchanged at 383.

## D-067 Renown arrives with the depth, and the bot had never met it

- **WHAT WAS WRONG WAS NOT ONLY FLATNESS**: renown's threat landed whole on **floor 3** and stayed there to floor 20, so
  a twenty-floor dungeon had no curve of its own (MAINT-36). Measuring it turned up something worse. With threat 2 the
  casual bot won **10 of 100** runs of Knight's Trial and died around floor 8; at threat 3, **5 of 100**, floor 7. A
  hero with no renown wins 64. Renown is meant to be a rubber band and was a wall.
- **WHY NOBODY SAW IT**: `AutoPlayer.PlayRun` builds its run with `RunFactory.NewRun` and never sets `Threat`, so every
  balance number in this repo - every sweep, every tier guard, every class comparison - is a run with **no renown at
  all**. The one number that mattered was the one nothing measured.
- **THE FIX**: `Renown.Level(run, catalog)` is now the single place a floor's threat comes from, and the hero's own
  threat ramps in over the run: none of it on the first floor renown reaches, all of it on the last. `ApplyThreat`
  reads the same function, so a monster's hearts and its blows keep coming from one number (D-046). It ramps over
  `run.FloorCount`, so a ten-floor run is not the first half of a twenty-floor one.
- **MEASURED** (120 blind seeds, Knight's Trial, casual):

  | player threat | flat (before) | ramped (now) |
  |---|---|---|
  | 0 | 64% | 64% |
  | 2 | 10% (avg floor 8.3) | 59% (F15.5) |
  | 3 | 5% (avg floor 7.3) | 30% (F14.0) |

- **THE DUNGEON'S OWN CURVE SHIPS OFF**: `FloorsPerThreat` adds threat for a hero who has earned none. At one step per
  8 floors it cost the casual bot five points (64% to 59%) and stretched the classes to **cleric 67, rogue 42 of 100 -
  a ratio of exactly 1.60**, which is the fence `TheClassesWinAboutAsOftenAsEachOther` refuses. The floors' own profiles
  already carry that curve, so the knob is documented with its price and left at zero.
- **THE GUARD THAT WAS MISSING**: `ARenownedRunIsStillARunSomeoneCanWin` plays 40 seeds at full renown and requires 6
  wins - well under the 30% measured, well over the 5% the cliff gave. `TheTelegraphIsWhatHappens` now carries renown on
  half its seeds, because a blow whose number changes with depth is exactly the case its invariant is for.
- **VERSIONS**: Ruleset 12 -> 13. Floor generation is untouched, so seeds deal the same dungeons.
- **TESTS**: eight in `RenownDepthTests`, plus three in `RenownTests` restated for the new rule - they pinned "threat 2
  at floor 3 means +2 hearts", which was the flatness itself.

## D-068 Audit 5, and closing the class of test that passes either way

The audit was started by a defect that shipped: `17370c1` carried `(Level + 1) / ThreatPerExtraDamage` in `Renown.Hit`,
left behind by a mutation script killed between applying a mutation and restoring the file. **391 headless, 488 EditMode
and 3 PlayMode tests all passed with it in place.** That is the finding the rest of the audit generalises.

- **WHY IT PASSED**: `Level / 2` and `(Level + 1) / 2` differ only at an **odd** level. Every exact assertion on
  `Renown.Hit` sat at level 0 or level 2. One test reached level 3 - the discriminating cell - and asserted
  `deep > shallow`, an inequality, so it saw a wrong number and said nothing. `Renown.Trap`, one line below, **was**
  pinned at an odd level and would have caught the same edit instantly.
- **THE CLASS, NOT THE INSTANCE**: nine renown assertions computed their expected value by calling `Renown.Level`, the
  function under test - `Assert.That(Hit(...), Is.EqualTo(base + Level(...) / TPED))` cannot fail for any Level-based
  bug. The ramp itself had **no** exact-value assertion anywhere; it was fenced entirely by inequalities and
  monotonicity. The repair is `TheRampIsTheseExactNumbersFloorByFloor`: the whole curve written out as literal digits,
  with nothing on the right-hand side that calls into the simulation. Eight mutations that survived the old suite -
  including the one that shipped - now each turn named tests red.
- **WHAT THE VAULT ROOM ACTUALLY DID** (REL-60): guards were placed at `Manhattan >= 2` from the arrival tile, but
  movement and melee are diagonal-inclusive, so that includes the diagonal. Measured over 720 generated rooms, **79%**
  put a guard within reach of the hero before their first action. A normal floor's `>= 3` hides this by accident. The
  rule is now `!IsAdjacent(start)`, said outright.
- **THE DOORWAY** (REL-61): "nobody ever stands in a doorway" was enforced at generation and nowhere else. `EnemyPathable`
  excluded `Terrain.Door` but a vault's door is `Terrain.Floor + IsExit`, so a chaser could park on the only way out of
  a nine-tile room and the hero was told "That way is blocked".
- **SUMMONED MONSTERS** (REL-70): `ApplyThreat` runs once at floor setup, so a minion placed mid-fight hit at the raised
  number and died at the base one - a placed bat had six hearts and a summoned one three on the same floor. Every summon
  takes its share now, through the same function.
- **THE INSTRUMENT WAS BENT TOO** (MAINT-50, DATA-30): D-064 taught `Score` to count the floor waiting outside a vault
  and left `TrackProgress` alone, so a vault visit spiked the bot's progress metric - the room's sixteen revealed stone
  tiles read as ground it had uncovered - and nothing could beat that high-water mark afterwards, pinning caution at
  0.15 for the rest of the floor. 13 of 20 floors have vaults. `Redact` likewise blanked the room and left the floor
  outside it readable, so a blind bot scored "step back through the door" against a key it had not found.
- **MEASURED AFTER THE REPAIR** (120 blind seeds, casual / novice):

  | | before | after |
  |---|---|---|
  | Knight's Trial, no renown | 64% / 6% | **66% / 8%** |
  | Knight's Trial, full renown | 30% / 0% | **42% / 0%** |
  | Blobert's Wrath, no renown | 40% / 4% | **38% / 2%** |
  | Blobert's Wrath, full renown | 17% / 0% | **25% / 0%** |

  A renowned run is meaningfully more winnable, which is the vault ambush and the bot's own caution, not a softening.
- **THE GATE** (TEST-50): the kit ran the headless suite only, so all 488 EditMode and 3 PlayMode tests - including the
  one that proves every hero has the face the dialogue asks for - gated nothing that shipped. `unity-gate.ps1` runs both
  platforms in batchmode and refuses a missing results file, a failure or an empty run; `make-kit.ps1` calls it, and
  `-SkipUnityTests` says so in VERSION.txt for when the Editor has the project open.
- **ALSO**: thirteen duplicate keys in `slices.json`, twelve of them hero portraits, where the good crop won only because
  it sat lower in the file - removed, and the slicer refuses duplicates now. A boss falling no longer talks over the
  hero being down to their last hearts (REL-80). `Lines.React` has tests at all for the first time (TEST-69).
  `Renown.Reaches` is gone: D-067 took its only caller and silently changed what it meant.
- **NOT FIXED, ON PURPOSE**: `Renown.FloorsPerThreat` stays at zero (D-067 records its price). MAINT-72, seventeen
  `wired` flags in `slices.json` that disagree with `ArtKeys.Wired`, is cosmetic - it colours the slicer's contact sheet
  - and verifying the list needs `ArtKeys.Wired` at runtime, so it is left with its evidence rather than guessed at.
  REL-62 (blast telegraphs painted on a vault's stone), REL-71 (a lane marks the raised number but deals the base one to
  an enemy it stops on), DATA-40/42 and MAINT-51 are recorded in `LLMHandOff.md` with their traces.

## D-069 The balance harness can see a player who has been here before

- **THE BLIND SPOT**: `AutoPlayer.PlayRun` built its run with `RunFactory.NewRun` and stopped there, so every balance
  number this repo has ever published - the tier guards, the class parity band, the difficulty tables, the ten-playthrough
  sweeps - measured a hero with **no talents, no gear, no provisions and no renown** (TEST-23). A whole class tree could
  be broken, as Judgement was until audit 3, without moving any of them.
- **THE FIX IS ONE FUNCTION, NOT TWO**: `ProfileSystem.ProvisionRun` is now the single place a profile becomes a run's
  numbers - provisions, talents, worn gear, renown's threat, and the tiles talents reveal. `GameSession.StartNewRun`
  calls it and so does `PlayRun`. Copying those five steps into the harness instead is exactly the drift this audit
  spent its day on; a harness that provisions differently measures a different game.
- **WHAT A BUILT-UP PLAYER LOOKS LIKE** (80 blind seeds a class, Knight's Trial, casual, level 12 with the tree spent
  and the dungeon's gear worn - the first time these numbers have existed):

  | | knight | paladin | rogue | wizard | ranger | cleric | berserker | engineer |
  |---|---|---|---|---|---|---|---|---|
  | empty profile | 62% | 53% | 50% | 60% | 52% | 66% | 51% | 53% |
  | full tree + gear | **95%** | 85% | **75%** | 80% | 78% | 95% | **97%** | 88% |

- **TWO THINGS TO SIT WITH**. The classes stay about as far apart built as they are empty (ratio 1.29 against 1.32), so
  the trees are not pulling them apart. But **Knight's Trial is close to a formality for a returning player** - four of
  eight classes above 88% - and a tree is worth wildly different amounts depending on the class: the Berserker gains 46
  points, the Rogue 25. Neither is touched here. They are design calls that were invisible before today and should be
  made deliberately, with the instrument that can now see them.
- **THE GUARD**: `EveryClassTreeIsWorthPlaying` plays all eight classes built-up at 40 seeds and refuses a class that
  wins fewer than half its runs with its whole tree spent, plus the same 8/5 ratio fence the empty-profile guard uses.
  Verified by neutering every talent effect at once: the weakest class falls to 10/40 and the guard names it.
- **A NOTE FOR THE NEXT MUTATION RUN**: a mutation sweep leaves the build cache holding the mutated assembly, and the
  next `dotnet test` can run it against restored source. The talent suite failed that way here and looked like a
  regression for a minute. Build with `--no-incremental` after a sweep, or do not trust the first run afterwards.

## D-071 Class balance where the classes actually come apart

D-069 made a built-up player measurable. The first thing it showed was that the eight classes are balanced for a
careful player and wildly unbalanced for a careless one.

| built-up, Knight's Trial | kni | pal | rog | wiz | ran | cle | ber | eng | spread |
|---|---|---|---|---|---|---|---|---|---|
| careful | 96 | 88 | 76 | 80 | 80 | 98 | 100 | 90 | 1.3 |
| **careless** | 41 | 25 | 16 | **15** | 23 | 60 | **61** | 51 | **4.1** |
| careless, Blobert's Wrath | 38 | 16 | **6** | 8 | 6 | 63 | **65** | 48 | **10.8** |

- **THE CAUSE IS NOT WHAT IT LOOKS LIKE.** The obvious story - conditional class rules stop firing when the player
  plays badly - is **false**, and measuring it said so: under sloppy play the Rogue's Ambush still fires on 43% of
  slashes (52% careful), the Wizard and Ranger still shoot at range on 15-16% (16% and 14% careful). The rules keep
  paying. What differs is **recovery**. Turns spent under half hearts, careful to careless: Cleric 1% to 11%,
  Berserker 1% to 9%, but Rogue 12% to **33%** and Wizard 12% to **26%**. The Cleric heals damage back - she is the
  only class that blocks at all, 3-4 blocks per 100 turns against everyone else's 0-1, because Sanctuary makes
  blocking worth the turn - and the Berserker ends fights sooner the more hurt he is. The fragile four have no way
  back, so one bad sequence compounds until it kills them.
- **WHAT WAS TRIED AND REJECTED**: +2 hearts moved the Paladin 25% to 21% (noise). +1 slash moved the Rogue 16% to
  13%. Stat levers do not touch this. Softening the three strong classes alone took the spread from 4.1 to 3.7 and
  left the Wizard at 15% - lowering the top does not raise the bottom. Every general recovery lever (more potions,
  bigger potions, a bigger breather) lifted the floor **and** pushed careful play to 93-100%, which is the endgame
  problem in D-070's notes.
- **WHAT SHIPPED**: help that only arrives when it is needed, plus two softenings.
  1. **The stairs' mercy**: the stairs never leave a hero below half their hearts. A careful player is under half on
     6% of turns and a careless one on up to 33%, so it is self-targeting.
  2. **Sanctuary answers danger**: the Cleric's blocks heal while she is at half hearts or fewer. Ungated it paid on
     every block of a run and made the one class that blocks the strongest class under pressure by a distance.
  3. **Rage starts later**: +1 damage per 6 hearts missing rather than 4.
- **MEASURED AFTER** (60 blind seeds a class, built-up, Knight's Trial): careless **33 to 71** against 15 to 61, a
  spread of **2.15** against 4.1. Careful play 86 to 100 against 76 to 100 - the ceiling did not move, the floor did.
  The Sanctuary gate earned its place: the Cleric is 63% careless where mercy alone left her at 76%.
- **NOT SOLVED**: on **Blobert's Wrath** the careless spread is still about 4 to 1 (Ranger 16%, Berserker 65%). The
  mercy helps less where monsters hit hard enough that half hearts is still one blow from death. That tier is opt-in
  and is meant to be brutal, so it is recorded rather than tuned.
- **THE GUARD**: `NoClassIsHopelessForACarelessPlayer` measures the sloppy axis - which nothing did before - and
  refuses a class under a fifth of its runs or a spread past three to one. Verified by reverting the mercy: the
  Wizard falls to 3/30 against the Cleric's 21/30 and the guard names it.

## D-072 The undead, and the Paladin's answer to them

First half of the class-ability work. The tag and the passive need no new system; the usable skills that follow do,
and are not started here.

- **THE TAG**: `EnemyDefinition.Undead`. Three monsters carry it - the Skeleton Warrior, the Spooky Spellbook and the
  Spectral Page it summons. A **Mimic Chest is furniture** and the **Theater Curtain Demon was never alive**, so
  neither is tagged; the tag means risen, not merely unpleasant. It exists because "a bonus against undead" has to
  name something, and because the skills to come - banishing, unmaking, stopping a skeleton from standing back up -
  all need the same noun.
- **THE PALADIN'S PASSIVE**: `Holy Wrath`, hammer branch tier 1, three ranks, +1 slash damage per rank against the
  undead. It **replaces** `p_judgement` rather than joining the tree beside it: the talent overlay draws one node per
  branch and tier, so a thirteenth Paladin talent would sit on top of an existing one.
- **WHICH IS ALSO A FIX**: the Paladin and the Cleric shared six of their twelve effects - Judgement/Rebuke,
  Consecrate/Holy Light, Dawnstrike/Smite, both Blessed Draught, both Prayer, Sanctified/Holy Water. Their trees were
  near-duplicates under different names. Holy Wrath removes one of those pairs and gives the Paladin an opening
  talent that is his own. `Judgement` stays in the game on the Cleric's Rebuke, so no effect was lost.
- **VISIBLE OR IT DOES NOT COUNT**: the Inspect panel names an undead monster on its tile, and the HUD's slash span
  includes Holy Wrath - the panel understating the blow a player is about to land is the same class of bug as a
  telegraph that lies.
- **SAVES**: `p_judgement` no longer exists, and `Progression.PointsSpent` already ignores ranks in talents no class
  has any more, so an old profile's points come back rather than stranding. `TalentEffect.HolyWrath` is appended, so
  no saved perk changes meaning.
- **TESTS**: three. Reverting the tag on the skeleton, or letting Holy Wrath hit everything, each turns named tests red.
- **NEXT, NOT NOW**: three equipped skill slots drawn from a larger per-class pool, with the Paladin's Lay on Hands and
  the Cleric's Dispel as the first two. That needs a new verb, a cost, a target rule and UI, and one caution recorded
  in advance: a castable Cleric heal cuts against D-071, where she was the strongest class under pressure until
  Sanctuary was gated. Measure her the moment it exists.

## D-073 Audit 6, and the tests that were written to close a blind spot and reopened it

Audit 5 found that a mutation had shipped through a 391-test suite and set out to close the "vacuous assertion" class.
Audit 6 asked whether it had. It had not: the test D-068 wrote to pin the stairs' half-hearts rounding used an **even**
max HP, where `MaxHp / 2` and `(MaxHp + 1) / 2` give the same answer - the same blind spot, in the repair for it. Every
hero that crosses a staircase in a unit test had even max hearts; the Paladin's eleven and the Berserker's nine never
did.

Twelve test oracles were rewritten and then **mutated to prove they bite**: nine mutations, nine named tests red.

- **TWO SHIPPED LIES ON THE CLASS-SELECT SCREEN**: the Berserker's card still said "+1 for every 4 hearts missing"
  after D-071 moved Rage to 6, and the Cleric's still said Sanctuary heals on every block after D-071 gated it on being
  at half hearts or fewer. `EveryClassCardNamesTheNumbersItsRuleActuallyUses` now reads the numbers off the rules and
  compares them to the text.
- **THE HUD WAS UNDERSTATING A SLASH**: D-072 said the slash span includes Holy Wrath, and it did - as an *alternative*
  to Judgement and Dawnstrike, where the rule **adds** them. A Paladin reading "3-4" was hitting a risen, reeling boss
  for 6. There were two copies of the bonus list; now there is one, `Talents.SlashSpan`, and the HUD formats what it
  returns. The span is also the best target actually on the board rather than a catalogue of everything the hero owns.
- **A VAULT GUARD THAT COULD NEVER REACH ANYONE**: monsters do not cross a vault's doorway, so a vault's other eight
  tiles are one ring - and two chests on that ring cut the room in half. **17% of 600 generated rooms** walled a guard
  off behind the treasure for the whole visit. `FloorValidator` refuses such a room and the generator makes another:
  measured 17% → 0%. (A further 43% have a guard queueing behind another guard in a nine-tile room. That resolves as
  they fall, and is not a defect.)
- **"NO MERCY" NOW MEANS IT**: D-071's stairs mercy was a single catalog number, so Blobert's Wrath - whose card has
  always ended "No mercy." - brought a dying hero back to half exactly as Squire's Stroll does, and each tier's
  `FloorClearHeal` was dead weight for anyone hurt enough to feel it. `MercyOnStairs` is a tier field now: Squire's
  Stroll 2, Knight's Trial 2, **Blobert's Wrath 0**, which is the tier's behaviour before D-071 and the number its card
  describes.
- **THE INSTRUMENT, AGAIN**: `LeaveVault` parks a live vault in `VisitedVault` so a revisit finds it as it was left,
  and neither the bot's score nor its progress meter read that board - so walking out of a guarded vault scored exactly
  as if the guards had been killed. That is the D-068 bug mirrored, and it survived the fix. One `Boards(run)` now
  enumerates every board a run owns and both readers use it. Separately, the progress high-water mark never reset, so a
  nine-tile vault was judged against the 25-tile floor outside it and the bot ran at minimum caution through most of
  every vault visit - on the 13 floors in 20 that have one. It resets with the floor stamp.
- **THE BUILT-UP PLAYER WAS WEARING COMMONS**: `Inventory.Grant` fills a slot only while it is empty, and the balance
  fixture granted the catalogue in its own order, so "the best gear worn" - the words in the guard's own failure
  message - was six Commons with every Epic in the bag. It also carried no shop provisions at all. Both fixed, and the
  fixture's own claim is now a test.
- **SIXTEEN CAPSTONES IN TWENTY-FOUR WERE MEASURED BY NOTHING**: a class may learn one capstone, and the fixture spent
  points in list order, so it always took the first-listed branch's. `BuiltUp` takes the branch to climb, and
  `EveryCapstoneCarriesABuild` plays all 24 - asserting the capstone was actually learned before counting a run.
- **AND THE TRAP UNDER THAT FIX**: `Provision` **spends** the profile it is handed. One profile shared across a seed
  loop would have armed seed 1 and left the other thirty-nine measuring a different player - harmless only while the
  fixture carried no provisions, which is what had just been fixed. The profile is built inside the loop, and the
  coupling has a test of its own.
- **GUARDS THAT WOULD HAVE GONE RED FOR NO REASON**: the careless-play parity guard ran 30 seeds with 1.3σ of margin -
  about one change in seven that merely perturbs the RNG stream would have turned it red, and the reflex this repo
  records for that is to widen the band until it guards nothing. Sixty seeds now, the number D-071 was measured at.
  Its sibling's `strongest <= weakest * 8/5` was, with the Berserker pinned at 40/40, really "the weakest must win 25
  of 40" - a 62.5% floor under an arm advertising 50%; a class at the ceiling is caught by a headroom assert instead.
- **THE KIT GATES**: `unity-gate.ps1` read Unity's exit code and never compared it to zero, had no floor under the test
  count, and accepted a run in which every test was skipped. `-SkipTests`, documented as skipping the headless suite,
  silently skipped 491 Unity tests as well with nothing in VERSION.txt to say so. And with `-AllowVersionMismatch`,
  VERSION.txt read "Test gate: passed" three lines above the warning explaining that those tests never ran against the
  player in the box. All three closed.

### What was decided rather than fixed

A **mistake** in the balance bot has always been a uniform draw from every legal command that survives the turn. On a
5x5 board in Free Roam that list is up to twenty-four Moves against a handful of anything else, so roughly three
mistakes in four are a step to a random tile - and the hero can never walk into a telegraphed killing blow, which is
the most characteristic novice death there is. D-071 read the class spread off that model and shipped three balance
changes on it.

A second model is now in the bot (`MistakeModel.Misjudged`: the second, third or fourth best command instead of the
best, losing commands included). The default is unchanged. Forty blind seeds a class, careless, built-up:

| model | ordering |
|---|---|
| RandomCommand | knight 38, cleric 38, berserker 37, engineer 33, paladin 31, wizard 30, **rogue 25**, ranger 22 |
| Misjudged | knight 39, berserker 39, **rogue 37**, engineer 35, paladin 33, **cleric 33**, ranger 31, wizard 30 |

They disagree, and in exactly the predicted direction: the Rogue, whose rule pays for reading a telegraph and standing
in the right place, goes seventh to third; the Cleric, the recovery class, goes joint first to joint fifth; the spread
goes 1.73 to 1.30.

### And what fixing the instrument showed

The balance fixture now wears what it always claimed to wear. Forty blind seeds a class, casual, built-up:

| tier | wins of 40 |
|---|---|
| Knight's Trial | knight 40, paladin 40, rogue 38, wizard 37, ranger 37, cleric 40, berserker 40, engineer 40 |
| Blobert's Wrath | knight 40, paladin 39, rogue 39, wizard 37, ranger 38, cleric 40, berserker 40, engineer 38 |

**A player who has been here before saturates the game**, and the hardest tier costs them at most two runs in forty.
`EveryClassTreeIsWorthPlaying` cannot measure a spread through that, so its ratio arm is gone rather than widened: it
keeps the one job it was written for, catching a tree that has stopped paying, with a 30-of-40 floor against a measured
minimum of 37 and an empty-profile baseline of 50-66% to fall through. Parity is measured by the careless guard, where
there is still room to see it. That the top of the progression outruns the hardest tier is recorded here and not tuned.

**No balance number was changed on the error model.** The second instrument stalls 1-10 runs in 40 depending on class where the
first stalls none, so a tenth of its losses are the bot failing to finish rather than the hero dying. Re-justifying
D-071 on a model with that stall rate would repeat the mistake the finding is about. What would settle it, in order:
stop the Misjudged model stalling, then measure the **pre**-D-071 catalog under it. If the 4:1 careless spread that
motivated D-071 is 1.3 under a plausible-error model, the mercy and the Sanctuary gate want re-justifying on their own
merits rather than on that spread.

## D-074 The tiers do something different, not just more of the same

For twelve versions a difficulty tier was a column of magnitudes: more hearts, harder blows, one more monster a floor,
a bigger Blobert. Every number that decides how the dungeon *behaves* was identical on all three - how long a web
holds, how long a bomb sits there, how long the boss is untouchable and how long his guard is down afterwards, how
many minions a summon brings, how fast a skeleton gets back on its feet. Squire's Stroll and Blobert's Wrath hit
differently and played the same.

| Behaviour | Squire's Stroll | Knight's Trial | Blobert's Wrath |
|---|---|---|---|
| Turns stuck in a spider's web | 1 | 1 | **2** |
| Turns a bomb sits before it goes off | **3** | 2 | 2 |
| Turns Lord Blobert is puffed and untouchable | **1** | 2 | **3** |
| Turns he lies deflated afterwards | **2** | 1 | 1 |
| Minions a summon brings | **1** | 2 | **3** |
| Turns a skeleton lies as bones | **3** | 2 | **1** |
| Pages a Spooky Spellbook keeps up | 2 | 2 | **3** |

One idea, applied seven times: **the gentle tier gives you time and fewer bodies; the hard one takes the time away and
adds bodies.** Knight's Trial is the base every other tier is written as a distance from.

- **TWO OF THESE KNOBS DID NOT EXIST.** `WebbedTurns = 1` was a literal in `EnemyAi`, where no tier could reach it. And
  the boss's deflated window - the free-hit window, the whole point of the puff cycle - was `ModeTurns = 1` assigned on
  the way in and then overwritten by the next branch without ever being read. The field looked like it set the window
  and did nothing; the window was one turn on every tier because it was hard-coded in the control flow.
- **TWO FLOORS ARE CONTRACT, NOT TASTE.** A bomb fuse never goes below 1: a blow that lands in the turn it is announced
  is a blow with no warning, and the telegraph rule (§3.2) is not a difficulty setting. A summon never brings fewer
  than one: the telegraph says a minion is coming, so one comes. Both are clamped in `ApplyDifficulty`, and the test
  types in a reckless tier (fuse 0, summon −9) to prove the clamps hold.
- **A COMMENT THAT HAD ALWAYS BEEN WRONG.** `BombFuse`'s doc said "1 = explodes in the environment step of the
  following turn". It does not: the fuse ticks down one a step and explodes on the step that finds it at zero, so a
  bomb sits for `fuse + 1` turns and a fuse of 1 goes off on the *second* turn. The test counts the turns rather than
  reading the field, and the comment now says what the code does.
- **SEVEN MUTATIONS, SEVEN RED.** Every tier value and both clamps have a test that fails when the value goes back.

### What the measurement said, and why nothing was tuned on it

240 blind seeds a tier, empty profile, flat behaviour against tiered:

| | Squire's Stroll | Knight's Trial | Blobert's Wrath |
|---|---|---|---|
| casual, before → after | 100% → 99% | 76% → 76% | 45% → 46% |
| novice, before → after | 97% → 100% | 21% → 21% | 5% → 4% |

**The bot barely notices, and that is the expected answer.** A one-turn look-ahead player does not feel a web that
holds a turn longer or a fuse that gives a turn more: it reads the board perfectly and has no reaction time to lose.
These knobs are aimed at a person.

So they were **not** tuned upward until the win rate moved. Pushing numbers until a bot that cannot perceive them
finally reacts would be tuning against a blind instrument, which is exactly how D-067 happened and what TEST-85 says
about "careless play" already. What the sweep is good for here is confirming that no tier was accidentally inverted,
and none was. Validating the feel of these needs a human at the keyboard, and that is the next thing to ask testers
for rather than the next thing to measure.

## D-075 Three skill slots, and the first verb the game has had

Ninety-six talents, and every one of them passive. A class could only ever be expressed as a modifier on a slash or a
shield — "+1 when the target is at full health", "heals when you block" — so the difference between a Paladin and a
Cleric was a list of adjustments to two buttons. There was no way to *do* a thing. That is what a skill is.

- **UNLOCKED BY THE TREE, NOT BY A NEW SYSTEM.** One talent a branch, at **tier 2**, names a skill; learning the talent
  unlocks it. No new currency, no new screen, and the build already decides the loadout — climb all three branches and
  you carry three, specialise and you carry one. Tier 2 and not a capstone, because a class may learn only **one**
  capstone: hang skills there and two of a class's three could never be reached at once.
- **THREE SLOTS, AND TODAY EXACTLY THREE SKILLS A CLASS.** So the loadout is not yet a choice, and a player is not asked
  to make one before it is. The slots exist because the pool is meant to grow and because the profile has to record
  *which* three from the first version rather than from the day it matters; an empty slot fills itself from what the
  build unlocked, in tree order.
- **THE TWO THAT WERE ASKED FOR**: the Paladin's **Lay on Hands** (mend 3) and the Cleric's **Dispel Undead** (3 damage,
  doubled against the risen). D-072 gave the undead a name so an answer to them could point at something; this is the
  skill that was the reason for naming it.
- **COST IS MANA.** D-032 replaced per-ability cooldowns with a pool on purpose, so the player chooses when to spend
  rather than waiting out timers. Skills pay from the same pool. No timers came back.
- **TWENTY-FOUR SKILLS, FIVE EFFECT KINDS**: Heal, Strike, Banish (double against the risen), Burst (everything beside
  you) and Stagger (one monster loses its turn; bosses shrug it off, as the talents that stagger cannot stagger one).
  Adding a skill is a line in the catalog, not a branch in the resolver. Each class carries one of three different
  effects, so no build is three of the same button.
- **A SIXTH EFFECT WAS WRITTEN AND CUT.** Reveal, on five of the classes. It cannot work on this board: every class
  already uncovers radius 1 as it walks, so a radius-1 sight skill does nothing at all, and radius 2 from the middle of
  a five-by-five board is the *whole* board - a two-mana button that deletes the fog. The ten-playthrough career found
  it: a levelled knight's runs fell from ~410 turns to ~200 and chests found fell with them, because the bot no longer
  had to look for anything. Finding the way down is the game (D-023), so the effect is gone rather than tuned down, and
  the five skills became a Shield Bash, a Bandage, an Arcane Nova, a Snare and an EMP Charge. Runs are back to 321-571
  turns against 364-479 before skills existed.

### The two contracts it had to not break

**A skill may not ask what a cover hides (rules §2.1).** A targeted skill reaches an **awake** monster only — exactly
what a slash requires. Aiming one at a sleeping monster under a cover would be a way to find out it is there. The test
refuses the sleeping target and then accepts the same monster awake, so the refusal is the rule and not a broken skill.

**A skill telegraphs nothing (rules §3.2)**, because it is the hero's own action on their own turn. Nothing new has to
be warned about.

And nothing trusts the profile: an id that is not a skill, not this class's, not unlocked, or listed twice is dropped,
and the list is cut to the slots. That is SEC-04's rule for worn gear, applied to skills.

### What the measurement found — including a bug the tests could not see

First measurement after wiring it: the class table came back **byte-identical** to the one from before skills existed.
Not "barely moved" — identical, to the win. Twenty-four skills had changed nothing, which meant the bot was not using
them.

`AutoPlayer.Copy` did not carry `Skills`. The blind bot's look-ahead saw a hero with no skills, so it never tried one.
**This is REL-44 exactly** — Movement and Threat were missing from that same copy for as long as it had existed — and
`TheCopyCarriesEveryFieldARunHas`, the test written *for* REL-44, was green throughout, because a field nobody thought
to set in it is a field it cannot see.

So the fix is two things. `Copy` carries `Skills`; and that test now walks `RunState`'s fields by reflection and fails
by name on any field still holding its default, so the third time this happens the test is the one that says so.

With the bot actually using them, 40 blind seeds a class, careless, built-up:

| class | before skills | with skills |
|---|---|---|
| paladin | 31 | **40** |
| berserker | 37 | 40 |
| cleric | 38 | 39 |
| engineer | 33 | 39 |
| knight | 38 | 34 |
| ranger | 22 | **34** |
| rogue | 25 | **33** |
| wizard | 30 | 30 |

**Spread 1.73 → 1.33.** Skills *compressed* the classes. The three that gained most — ranger +12, paladin +9, rogue +8 —
are the ones whose passives are conditional on standing in the right place, which is what a careless player cannot do;
an active button pays whatever your footwork is like. The knight, near the top on passives alone, lost 4.

D-071's recorded caution was that a castable Cleric heal would cut against her being the strongest class under
pressure. It did not: she moved 38 → 39 and the field came up to meet her. The Paladin, who got the heal that was asked
for, went 31 → 40.

**Open, and the same question D-074 ended on**: whether three slots are worth having is a question about a person, and
the bot answers a different one. The loadout is also not yet a choice — that arrives when the pool grows past three.

## D-076 The first run, and what was actually killing it

The ten-playthrough career kept ending the same way: lost on floor 6 of run 1, then nine straight wins. The difficulty
of this game lives almost entirely in the first run of a fresh profile, and the question was whether that first run is
teaching or just punishing.

**What kills a new player, measured** (30 blind careless runs, empty profile, Knight's Trial):

| cause | deaths |
|---|---|
| **spikes** | **10** |
| goblin_brute_king (the act-1 boss) | 6 |
| lava | 2 |
| stage_mask | 2 |
| five others | 1 each |
| won | 6 |

Damage taken says it louder: **spikes 975**, next-worst bomb 345. Spikes kill a fresh player more than every monster
in the game put together. And spikes sit under covers, so by the cover rule (§2.1) the player *could not have known*
they were there. That is being punished for not yet knowing the game rather than for playing it badly.

**The fix is slack, not softer rules.** The first run of a profile — only the first, counted by `RunsFinished`, won or
lost — starts with **+3 hearts and +1 potion** on Squire's Stroll and Knight's Trial. Blobert's Wrath gives none, the
same call as the stairs' mercy in D-073: opt-in tier, its card ends "No mercy", nobody meets the game there by accident.

Hearts and potions rather than weaker traps, deliberately. A trap that hits for less while you are learning teaches you
the wrong number and then changes it behind your back; more hearts teaches the right number and gives you room to
learn it.

A null profile counts as a first run. That is not a convenience for the test harness: the game always has a profile, so
null only ever means "a hero with nothing behind them", which is exactly who this is for — and it keeps every
empty-profile sweep in this repo measuring the run a real new player gets rather than one nobody will ever play.

**Measured, 240 blind seeds a tier, grace off against on:**

| tier | casual | careless |
|---|---|---|
| Squire's Stroll | 99% → 100% | 100% → 99% |
| Knight's Trial | **76% → 87%** | **21% → 35%** |
| Blobert's Wrath | 46% → 46% | 4% → 4% |

Nothing after run 1 moved, because nothing after run 1 is touched.

**Recorded, not solved**: the ten-playthrough career is now 10 of 10. One seed is not evidence of over-correction — the
240-seed sweep still loses one first run in eight at casual and two in three at careless — but it does underline what
D-073 already found. A built-up player wins essentially everything, and now the first run usually survives too, so a
whole career can pass without a loss. The difficulty of the late game is the open question, and it is not this one.
