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

