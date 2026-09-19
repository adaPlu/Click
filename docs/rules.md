# ClickDungeon — Gate 0 Rules Lock

Status: **LOCKED for Gate 1**. Numbers marked *(tune)* are content-definition
values expected to change during the Gate 2 fun test; the *rules* around them
should not change without a decision-log entry (`docs/decisions.md`).

Nothing in presentation (Unity) may invent or override a rule in this file.

---

## 1. Board

- 5×5 grid. Coordinates `(x, y)`, `x` 0..4 left→right, `y` 0..4 bottom→top.
- Adjacency is **8-way** (the eight surrounding cells), for the hero and for enemies alike (D-021).
- Distance words:
  - *Chebyshev distance* `max(|dx|, |dy|)` — reach: melee, adjacency, dash length, bomb blasts.
  - *Manhattan distance* `|dx| + |dy|` — used only for sensing.

### 1.1 Cell layers

Each cell has independent layers:

| Layer     | Gate 1 values                               |
|-----------|---------------------------------------------|
| Terrain   | `Floor`, `Wall`, `Pit`, `Door` (vault, D-018) |
| Structure | `None`, `Exit` (locked or unlocked)         |
| Hazard    | `None`, `Spikes`, `Bomb`, `Lava`            |
| Content   | `None`, `Key`, `Chest`, `Potion`, `Fountain`, `Teleport`, `PressurePlate` |
| Actor     | hero or enemy (tracked on the actor, not the cell) |

Gate 1 placement rule: a cell holds **at most one** of {Exit, Hazard, Content}.
The layered data model still keeps them separate so later content (e.g. chest
on dangerous terrain) needs no model change.

### 1.2 Passability

Nothing on the board stops the hero except a locked vault door and a tile someone is standing on (D-021).

| Thing        | Hero can enter | Enemy can enter | Blocks ranged line |
|--------------|----------------|-----------------|--------------------|
| Floor        | yes            | yes             | no                 |
| Wall         | yes — never generated, the sprite is the cover over an unrevealed tile (§2.1) | no | **yes** |
| Pit          | **yes: falls to the next floor** | no | no                 |
| Door (locked / open) | **no** / yes (vault) | no | **yes** |
| Closed chest | yes — furniture you can stand on and open from your own tile | no | no |
| Opened chest | yes            | yes             | no                 |
| Any actor    | no             | no              | **yes**            |
| Spikes/Bomb/Lava | yes (triggers) | avoided by AI | no                 |

---

## 2. Information: visibility

### 2.1 What is always known

- **Nothing is known in advance, the exit included** (D-023): the exit is covered like
  every other tile and found by clicking it. Stepping onto it without the key only
  uncovers it.
- **Everything else is covered**, terrain included: an unknown tile is drawn as
  a plain stone cover, and only sensing (its clue) or revealing tells the player
  what is under it. Walls and pits are found the same way as hazards and content.
- In **Free Roam** there is **no sensing at all**: a covered tile shows nothing —
  no clue, no hint — until it is clicked (§2.3, §12). The exit is
  covered too.
- **Every cover is identical** (D-023 amendment). Before a tile is clicked, nothing about
  it depends on what is under it: not its art, tint, border, highlight, hover text, what a
  click on it does, or any popup, effect or log line pointing at it. Empty floor, a sleeping
  monster, a chest, loot, a trap, a pit, a door, a plate, a pad and the exit stairs all look
  and answer the same. Concretely:
  - Opening the exit (key taken, Blobert defeated) is told in the log, but nothing marks a
    covered exit tile.
  - A blast that hits a monster still under its cover says nothing about it.
  - A pressure plate opens its door where it stands; a covered door stays covered.
  - A covered chest is tapped like any cover: the tap uncovers it. Tapping to open needs it uncovered.
  - Exceptions: monsters that start awake (vault guards, Lord Blobert) and monsters that
    woke earlier are drawn wherever they stand. Step by Step's sensing markers (§2.3) sit on
    top of the cover; the cover itself never changes.

### 2.2 Per-cell knowledge states

`Unseen → Sensed → Revealed`. Knowledge never goes backwards within a floor.

- **Unseen**: nothing known beyond terrain.
- **Sensed**: the cell shows its **clue set** (below). Exact thing unknown.
- **Revealed**: exact hazard/content/enemy is shown.

"Resolved" is **not** a cell state. Resolution belongs to the individual
thing: chest `Opened`, key `Collected`, bomb `Detonated`, enemy `Dead`,
exit `Unlocked`.

### 2.3 Sensing and revealing (hero passive, Knight)

At the end of the player's action (and at floor start), from the hero's cell:

- **Only a clicked tile is Revealed** (D-023): the tile the hero stands on. Sight range
  reveals nothing — the hero's neighbours stay covered until they are clicked too.
- Clicking a covered tile the hero **cannot enter** is a **bump**: the hero stays put, the
  tile is Revealed and the turn is spent. That covers a sleeping monster, a shut vault
  door and a pit with nothing below; a dash stops the same way at the first covered tile
  on its path that would block it. A refused click would give away what was hidden.
- Cells at Manhattan distance **≤ 2** become at least **Sensed** — **Step by Step only**.
  Sensing is not revealing. Free Roam produces no Sensed cells at all.

Every step onto a covered tile is a blind step: its hazard triggers as usual. Step by
Step dashes may land on a Sensed cell, never on one carrying an ENEMY clue.

### 2.4 Clue set

A Sensed cell shows every category present (flags, not one value):

| Clue      | Present when the cell holds…     | Icon/shape (never colour-only) |
|-----------|----------------------------------|--------------------------------|
| ENEMY     | a dormant enemy                  | claw marks                     |
| DANGER    | spikes or bomb                   | warning triangle               |
| OBJECTIVE | key                              | "K" ring                       |
| EXIT      | the exit (stairs down)           | "E" tile                       |
| FEATURE   | vault door, pressure plate, teleport pad | "+" purple disc        |
| TREASURE  | chest or potion                  | sparkle                        |
| SAFE      | none of the above                | small dot                      |

Clues are **deterministic and truthful**. No false clues in Gate 1.

---

## 3. Enemies: activation and intent

### 3.1 Dormant / Awake

- Enemies start **Dormant** and hidden. Dormant enemies do nothing.
- An enemy **wakes** the moment its cell becomes Revealed — which, since D-023, means the
  moment the player clicks its tile and bumps it.
- Awake enemies stay visible wherever they move.
- The boss starts Revealed and Awake.

### 3.2 Intents are always one turn ahead

Every awake enemy always shows its **intent** for the next enemy phase.
Enemies only ever execute an intent that the player has already seen.

### 3.3 First contact

An enemy that wakes (or is summoned) during a turn only **declares** an
intent at the end of that turn. It cannot act until the player has taken one
more action. There is no way for a newly revealed enemy to deal damage before
the player responds.

### 3.4 Intent kinds

| Intent            | Shown as                          | Execution                                          |
|-------------------|-----------------------------------|----------------------------------------------------|
| `Attack(cell)`    | sword over target cell            | damage to whoever stands on `cell`, if still in reach |
| `Move`            | footsteps arrow                   | step 1 toward hero along shortest AI path          |
| `Fire(direction)` | flaming lane from the imp         | hits first actor (hero *or enemy*) in lane within range; walls block, pits don't |
| `Recover`         | dizzy stars                       | nothing (staggered)                                |
| `Rest`            | "zzz"                             | nothing (slow enemy cooldown)                      |
| `Summon`          | magic circle on target cell       | creates minion on that cell if empty               |
| `Slam(cells)`     | cracked-ground overlay on cells   | damage on all listed cells                         |
| `PuffUp`          | inflating arrows                  | enters Puffed state                                |

Attacks target **cells**, not the hero. Moving out of a telegraphed cell
avoids the hit. This is the core of Shield/Dash decisions.

### 3.5 Gate 1 enemies *(tune numbers)*

| Enemy          | HP | Dmg | Behaviour                                                                 |
|----------------|----|-----|---------------------------------------------------------------------------|
| Goblin         | 3  | 2   | Adjacent to hero → `Attack(hero cell)`; else `Move`.                      |
| Crowned Slime  | 5  | 3   | Alternates action / `Rest`. On action turns: adjacent → `Attack`, else `Move`. |
| Fire Imp       | 2  | 2   | Hero in a straight clear lane within 3 → `Fire(dir)` (the telegraph *is* the aim). After firing → `Rest` (reload). Otherwise `Move` toward a lane cell at distance 2–3. Adjacent → steps away if possible. |
| Slimelet (summon) | 1 | 1 | Goblin behaviour.                                                       |

AI tie-breaks are deterministic (fixed direction order Up, Right, Down, Left;
then lowest actor id). No RNG in AI.

### 3.6 Lord Blobert (floor 5 boss) *(tune numbers)*

HP 12. Uses the same intent system. Repeating script:

1. **Boast → Slam**: declares `Slam` on the hero's cell and its 4 orthogonal
   neighbours. Executes next phase for 4 damage.
2. **Summon**: declares `Summon` on an empty cell adjacent to himself; a
   Slimelet appears next phase (and obeys first contact).
3. **Puff Up**: declares `PuffUp`. For the next 2 turns he is **Puffed**:
   immune to damage, `Move`s toward the hero, attacks adjacent for 2.
4. **Deflated**: 1 turn, no action, takes **double damage**.

Then back to 1. The encounter is won when Blobert dies (minions vanish).

---

## 4. Hazards *(tune numbers)*

- **Spikes**: any actor that *enters* the cell by walking takes 2 damage.
  Spikes are permanent. Knowingly paying HP for a shortcut is intended.
  Dashing over spikes does not trigger them; *landing* on them does.
- **Bomb**: any actor that enters the cell, or a Slash on the bomb cell,
  **arms** it (fuse 1). An armed bomb shows its 3×3 blast area. It explodes in
  the environment step of the **following** turn, dealing 4 damage to every
  actor in the 3×3 (enemies included), then is Detonated. The player always
  gets one action between arming and explosion.
- **Lava**: any actor that *enters* the cell takes 3 damage. Lava is permanent and
  Guard does not stop it. Enemies path around it.
- **Pit**: stepping into a pit is a **fall**: 3 damage and you land on the next
  floor, leaving this floor's key, chests and exit behind. There is nothing below
  the last floor, and a vault hangs off a floor rather than above one, so pits are
  solid in both. Enemies never enter pits, and fire flies over them.
- AI pathing treats un-detonated hazard cells as blocked.

---

## 5. Hero: Sir Clickington (Knight) *(tune numbers)*

Max HP 10. Slash damage 2. Starts each run with 2 potions. Another hero may take
the run instead (§5.1); everything below is the Knight's, and the table's numbers
come from whichever class is playing.

| Command   | Turn cost | Mana | Rule |
|-----------|-----------|----------|------|
| Move      | 1 | – | Step into an enterable cell: **any tile on the board** in Free Roam, one of the **eight neighbours** in Step by Step (§12). |
| Wait      | 1 | – | Do nothing (tap Sir Clickington / Space). |
| Slash     | 1 | – | Target an adjacent cell (8-way) with an awake enemy (damage) or a bomb (arms it). |
| Shield    | 1 | 2 | Gain **Guard** until end of this turn. Guard blocks all enemy attack and bomb damage. Melee attackers blocked by Guard become **Staggered** (next intent `Recover`); Lord Blobert is never staggered. Spikes are not blocked. |
| Dash      | 1 | 3 | Move **one or two** cells in a straight line, diagonals included. On a 2-cell dash the middle cell must be enterable-or-hazard and not an actor/closed chest/wall/pit; middle-cell hazards are **not** triggered. Landing cell must be enterable; landing triggers its hazard/pickups. In Step by Step the landing may be a Sensed cell but never one carrying an ENEMY clue; Free Roam has no sensing, so a blind dash is allowed there exactly as a blind step is. |
| Potion    | 1 | – | Heal 4 (not above max). Requires ≥ 1 potion. |
| Interact  | 1 | – | Target a closed chest on the hero's **own tile or an adjacent one**: one tap toward opening it (see 7). |

### 5.2 Mana (D-032)

SHIELD and DASH cost **mana**; moving, waiting, slashing, potions and chests are
free. The Knight has a pool of **6**: SHIELD costs **2**, DASH **3**. At the end
of every turn the hero regains **1** (never above the pool), and every new floor
starts with a **full** pool. An ability the pool cannot pay for is rejected and
costs nothing. Each button shows its price; the bar under HP shows the pool.

Illegal commands are rejected with a reason and cost nothing.

Entering a cell (walk or dash landing) resolves, in order: hazard → pickup
(key / potion auto-collect) → exit.

### 5.1 Hero classes (D-024)

A run is taken by one **hero identity**, chosen on the title screen before the run
starts. A run already in progress keeps the hero it began with, and the save
carries it. Every class plays by the same rules; only its numbers differ.

| Class | Hearts | Slash | Potions (heal) | Mana | Shield | Dash |
|---|---|---|---|---|---|---|
| Knight (Sir Clickington) | 10 | 2 | 2 (heal 4) | 6 | 2 | 2 tiles for 3 |
| Paladin (Dawnward) | 12 | 2 | 2 (heal 6) | 8 | 2 | **1 tile** for 4 |

The Paladin is the steadier of the two: more hearts, a stronger potion and a
deeper mana pool for shields, paid for with a dash that moves one tile and costs
more — and dash is how a hero crosses a trap or breaks away from a monster.
Difficulty tiers apply to whichever class is playing (§10).

Measured with mana, 40 seeds per tier with the novice bot (`HeroSweep`): sighted,
both heroes win every tier. Blind, the Knight wins 40 / 36 / 26 and the Paladin
37 / 38 / 30 (with cooldowns it was 40 / 36 / 29 and 39 / 38 / 33).

---

## 6. Turn contract

UI actions (inspect, pause, dialogue, the chest reward celebration) cost 0 turns and
never touch simulation state. **Opening** a chest is not one of these: each tap is a
gameplay command that costs a turn (§7).

One committed gameplay command resolves as:

1. **Validate** the command. Illegal → reject, nothing changes.
2. **Player action** resolves (including enter-cell effects).
3. **Deaths** resolve.
4. **Visibility** updates from hero position; enemies whose cell became
   Revealed wake and are marked *just-woken*.
5. If the hero stepped onto an unlocked exit → **floor complete**, stop.
6. **Enemy phase**: each awake, not just-woken enemy executes its *previously
   declared* intent, in ascending actor id. Intents are re-validated (a dead
   enemy does nothing; a moving enemy re-paths; an attack hits only if the
   target cell is still in reach). Deaths resolve after each action.
7. **Environment**: armed bombs with fuse 0 explode; other armed bombs tick
   fuse 1 → 0.
8. **Deaths**; if hero HP ≤ 0 → **run failed**, stop.
9. **Declare**: every awake enemy (including just-woken) declares its next
   intent. Guard expires. The hero regains 1 mana. Turn counter +1.
10. **Events** are emitted for presentation.
11. **Stable boundary** → autosave → accept next command.

Ordering is covered by automated tests.

---

## 7. Keys, exit, chests, floors

- **Key**: normal floors have exactly one key and a locked exit. Walking onto
  the key collects it. Walking onto the locked exit while holding the key
  unlocks it, consumes the key and completes the floor.
  The exit tile is drawn open as soon as the key is held, since stepping on it
  will open it. Blobert's sealed exit ignores keys. The exit only triggers when
  entered: a hero already standing on it when Blobert falls steps off and back on.
- **Chest**: `Interact` with a closed chest on the hero's own tile or one beside it.
  - A chest has a **quality** that sets how many taps it takes to open (D-022):
    **Common 2, Rare 3, Epic 4**. It grants **one reward per tap**: Common 2, Rare 3,
    Epic 4. A vault's great chest is always Epic and grants five.
  - **Every tap is a full player action costing one turn**, so each one gives every
    revealed monster its response. Opening a chest is exposure, not a free pickup.
  - Quality is drawn from `hash(runSeed, floorIndex, chestId)` at floor setup, so it
    never moves the generator's stream: floors from a given seed are unchanged.
  - The **reward is decided and committed by the tap that opens the chest** and saved
    at the stable boundary of that turn. It derives from
    `hash(runSeed, floorIndex, chestId)` and never consumes a shared RNG stream, so
    opening a chest cannot change anything else.
  - An opened chest stays opened; taps cannot duplicate rewards.
  - Reward table (each draw): +1 potion (weight 2), +3 max HP and heal 3 (weight 3),
    +1 Slash damage (weight 1).
  - Opening is shown on the board: after the first tap the chest tile carries one pip per
    tap it needs, filled as taps land. The reward reveal that follows costs no turns and
    lists every reward the chest granted.
- **Floor complete** → next floor generates, hero HP/potions/boons carry over,
  mana refills, key is cleared.
- **Run**: 5 floors. Floor 5 is Lord Blobert's arena; its exit is unlocked
  when he dies and stepping on it wins the run.
- **Death**: hero HP ≤ 0 ends the run. The save is cleared (roguelike run).

---

## 8. Generation and validation

- Hand-authored 5×5 topology templates × 8 transforms (rotations/mirrors).
- Seeded content placement per floor from `hash(runSeed, generationVersion,
  floorIndex, attemptIndex)`.
- Invalid boards regenerate with `attemptIndex + 1`.

A floor is valid only if:

1. exactly one start, on Floor, with no hazard/content/actor;
2. exactly one exit on Floor;
3. key reachable from start, and exit reachable from key, **without stepping on
   hazards** and treating closed chests as blocked (enemies may be in the way);
4. every enemy on Floor, not on a hazard/content cell, one actor per cell;
5. no enemy within Manhattan distance 2 of the start (no instant wake);
6. at most one of Exit/Hazard/Content per cell;
7. every content/enemy id exists in the catalog;
8. boss floor: boss placed, and at least 8 enterable cells.

---

## 9. Save boundary

- Autosave only at step 11 of the turn contract, and at floor start.
- The save contains the full authoritative run state (board, actors, intents,
  hero, knowledge, turn, floor, version numbers). The seed alone is never
  used to reconstruct a suspended floor.
- Writes are atomic: temp file → validate by reading back → replace, keeping
  the previous save as `.bak`.

---

## 10. Difficulty tiers *(tune numbers)*

Chosen when starting a run and stored in the run (decision D-017). Every tier uses the rules
above; only numbers change. §3–§8 describe the base content; Knight's Trial blunts traps by one
(retuned for click-to-reveal, where every click is a blind step — §10.2).

| Setting                                | Squire's Stroll (easy) | Knight's Trial (medium) | Blobert's Wrath (hardcore) |
|----------------------------------------|------------------------|-------------------------|----------------------------|
| Hero max HP                            | 14 (+4)                | 10                      | 10                         |
| Starting potions                       | 3 (+1)                 | 2                       | 1 (−1)                     |
| Goblin / Slime / Imp / Slimelet HP     | 3 / 5 / 2 / 1          | 3 / 5 / 2 / 1           | 4 / 6 / 3 / 2 (+1)         |
| Goblin / Slime / Imp / Slimelet damage | 1 / 2 / 1 / 1 (−1)     | 2 / 3 / 2 / 1           | 2 / 3 / 2 / 1              |
| Lord Blobert HP / Slam / puffed attack | 10 / 3 / 1             | 12 / 4 / 2              | 16 / 5 / 2                 |
| Spikes / bomb damage                   | 1 / 3                  | 1 / 3 (−1)              | 2 / 4                      |
| Enemies on normal floors               | profile                | profile                 | profile                    |
| HP restored on arriving at a new floor | 3                      | 0                       | 0                          |

Damage and HP never drop below 1.

### 10.1 Measured difficulty

`BalanceReport` (explicit test) plays 200 seeds per tier with `AutoPlayer`, a one-turn look-ahead
bot. "Mistakes" is the share of turns it spends on a random move that does not lose on the spot.

**Free Roam** (default, §12) — reach F5 / win:

| Player (mistakes) | Squire's Stroll | Knight's Trial | Blobert's Wrath |
|-------------------|-----------------|----------------|-----------------|
| sharp (0%)        | 100% / 100%     | 100% / 100%    | 100% / 98%      |
| casual (20%)      | 99% / 99%       | 99% / 97%      | 98% / 75%       |
| sloppy (35%)      | 99% / 98%       | 100% / 74%     | 97% / 34%       |
| novice (50%)      | 98% / 75%       | 98% / 33%      | 95% / 8%        |
| flailing (70%)    | 96% / 18%       | 97% / 3%       | 96% / 0%        |

**Step by Step** (option) — reach F5 / win:

| Player (mistakes) | Squire's Stroll | Knight's Trial | Blobert's Wrath |
|-------------------|-----------------|----------------|-----------------|
| sharp (0%)        | 52% / 52%       | 41% / 41%      | 24% / 24%       |
| casual (20%)      | 71% / 71%       | 74% / 74%      | 61% / 60%       |
| sloppy (35%)      | 76% / 76%       | 77% / 76%      | 64% / 57%       |
| novice (50%)      | 75% / 73%       | 76% / 68%      | 71% / 46%       |
| flailing (70%)    | 83% / 70%       | 82% / 42%      | 59% / 16%       |

Two findings from this sweep, both **open**:

1. *(Superseded: Free Roam no longer strikes from anywhere — see §12 and §10.2.)* **Nobody ever dies in Free Roam.** Across all 15 Free Roam rows the bot died zero times at every
   tier and every skill level; every lost run is a *stall* against the 400-command cap, nearly all of
   them on Blobert's floor. Attacks target cells one turn ahead (§3.4), and in Free Roam the hero can
   step to any of the other 24 tiles, so the dodge is always free and no enemy can ever connect.
   Reaching Blobert is therefore 95–100% whatever the tier, and the tiers separate only on wins.
2. **The look-ahead bot stalls in Step by Step.** The 0%-mistake bot wins *less* than the sloppy one
   (52% vs 76% on easy) because it deadlocks in the melee "step-away dance" already on the Gate 2 watch
   list. Its Step rows understate how hard the mode is, rather than measuring it.

Until (1) is resolved the difficulty tiers are not meaningful in the default mode. The rows above are all
measured with a **sighted** bot, which walks straight to a key it should not be able to see.

### 10.2 Sighted vs blind

A blind `AutoPlayer` decides on a redacted board: unrevealed tiles blanked, hidden enemies removed, its
look-ahead included, so it cannot find a trap by simulating a step onto it. `GuardBaseline` (explicit),
Free Roam, 40 seeds, measured after enemy reach became the same in both modes (melee monsters must stand
next to the hero, §12) — reach F5 / win:

| Player             | Squire's Stroll | Knight's Trial | Blobert's Wrath |
|--------------------|-----------------|----------------|-----------------|
| novice, sighted    | 40 / 40         | 40 / 40        | 40 / 40         |
| novice, **blind**  | 40 / 39         | 33 / 21        | 21 / 10         |
| flailing, sighted  | 40 / 40         | 40 / 37        | 40 / 31         |
| flailing, **blind**| 40 / 32         | 29 / 11        | 16 / 1          |

Measured after click-to-reveal (D-023), its retune, and covering the exit (novice rows; covering the exit
added ~24 turns to a Knight's Trial run and moved novice wins from 36 / 25 / 8 to 39 / 21 / 10). **Sighted bots now win every run at every tier**:
seeing the board makes the dungeon trivial, and hidden information is what makes the tiers bite. Before the
retune, blind clicking made Knight's Trial and Blobert's Wrath near-unwinnable (8 and 0 novice wins of 30),
with traps and bumped monsters doing the killing. In the 60-seed `DifficultySweep` the careful (casual) blind
bot wins 93% / 77% / 53%.

- **Adjacent-only melee first made every tier much easier** (blind novice won 39 / 35 / 26 of 40, Knight's
  Trial four wins behind Squire's Stroll). The tiers were retuned with `DifficultySweep` (D-017 amendment):
  Knight's Trial gets one more enemy per floor and one fewer potion; Blobert's Wrath two more enemies and
  two fewer hearts on top of its existing changes. A blind novice now wins **39 / 31 / 9**.
- **Enemy and boss HP barely move the results.** A melee monster can be walked away from, so what counts is
  damage you cannot avoid and how crowded the floor is. Raising enemy HP mostly made the bot stall.
- **Only hidden information makes the tiers bite.** A sighted bot still wins 38 / 38 / 37: seeing the board,
  it steers around the extra monsters.
- **The casual blind bot (20% mistakes) has a stall bug.** It runs out of commands in about 1 run in 10 at
  every tier — including Squire's Stroll, where it never dies — so its numbers measure the bot, not the
  dungeon. The guards measure a blind novice.

These are still bot numbers. The `difficulty` field in playtest telemetry remains the real check.

---

## 11. Tile catalogue *(production tile set)*

Every tile in the production sheets, and what it means. "Decor" means the tile is a picture of a state that already exists;
the board never shows a feature the rules do not implement.

| Tile key | Meaning | Rule |
|---|---|---|
| `tile_floor_stone` | floor | walkable |
| `tile_floor_cracked`, `tile_floor_moss` | floor (decor) | variants picked from the cell position, no rule of their own |
| `tile_wall`, `tile_wall_corner`, `tile_torch` | wall (decor variants) | blocks movement and fire |
| `tile_trap_pit`, `tile_water` | pit (water is decor for a pit) | nobody crosses; fire flies over |
| `tile_trap_spike` | spikes | entering on foot costs 2 HP (tier-tuned); permanent |
| `tile_trap_bomb` | bomb | arms on entry or slash, explodes next environment step in 3×3 |
| `tile_lava` | lava | entering costs 3 HP; permanent, never expires; enemies avoid it |
| `tile_pressure_plate` | pressure plate | stepping on it opens every door on the floor; stays pressed |
| `tile_door_locked`, `tile_door_open` | vault door | blocks movement while locked; entering an open door enters the vault (D-018) |
| `tile_teleport`, `tile_shadow` | teleport pad | one pair per floor; entering one places the hero on the other, no extra turn |
| `tile_fountain_heal` | healing fountain | entering heals 3 once, then it is spent |
| `tile_key` | key | walking over it collects it |
| `tile_chest_closed`, `tile_chest_open` | chest | Interact from an adjacent tile; vault chests hold three rewards |
| `tile_stair_down_locked`, `tile_stair_down` | floor exit | locked until the key is held; open exits descend (also the vault's way back) |
| `tile_stair_up` | floor entrance (decor) | marks the start tile |

Sheet one uses shorter names (`floor_stone.png`); the registry uses the `tile_*` names above.

---

## 12. Movement modes *(D-021)*

Chosen in **Settings → MOVEMENT** (default Free Roam); the choice applies to the next new run and is stored in that run's
save, so a run in progress keeps its mode. Both modes use every other rule in this file;
only how far the hero may move, and how enemies threaten, differ.

| | **Free Roam** (default) | **Step by Step** (option) |
|---|---|---|
| Move | tap **any** tile on the board; distance does not matter | one of the **eight** neighbouring tiles |
| Dash | one or two tiles in a straight line | same |
| Enemies | **the same in both modes** (below) | same |
| Hints | **none** — every unrevealed tile is a blank cover, whatever is under it | sensing marks clues at Manhattan ≤ 2 (§2.4) |
| Revealing | only a clicked tile; a blocked click is a bump (§2.3, D-023) | same |

**Enemy reach does not depend on the mode.** Melee monsters — goblin, crowned slime, slimelet, and Lord
Blobert while puffed up — attack only from a tile next to the hero (8-way) and otherwise step closer.
Ranged monsters and the boss act from a distance: the fire imp shoots down a clear straight lane up to 3
tiles, and Blobert's slam and summon reach anywhere on the board. Every attack still targets a cell
declared a turn ahead (§3.4).

Nothing on the board blocks the hero except a **locked vault door** and a tile with an actor on it.
Walls are no longer generated: the wall sprite is the cover drawn over a tile the player has not
revealed (§2.1). A **pit** is entered deliberately and drops the hero a floor for fixed damage (§4);
pits are never generated on a floor with nothing below it.

Both modes are the same simulation, so a save records its mode and resumes in it.

## 13. Treasure, the profile and the shop *(D-025, tune numbers)*

A run carries treasure out; the **profile** keeps it between runs.

| Source | Pays |
|---|---|
| Each reward a chest gives up | 4 coins |
| Walking down the stairs off a floor | 10 coins (falling through a pit pays nothing) |
| Defeating Lord Blobert | 5 gems |
| Opening a vault's great chest | 1 gem |

What a run found is banked once, when it ends — won, lost or abandoned. The
**shop** (the title's SHOP, or the game screen's during a run) sells provisions for the *next* run:

| Item | Price | Effect |
|---|---|---|
| Potion ration | 60 coins | +1 potion at the start of the next run |
| Heart token | 120 coins | +2 max hearts (full) at the start of the next run |
| Special key | 25 gems | Hides one premium chest in the next run (D-026) |

The **"+"** beside coins or gems (title card and game HUD) opens an exchange (D-033):
**1 gem for 15 coins**, or **30 coins for 1 gem**. A round trip loses half, so
it moves value between the two without making more of either. There is no
real-money purchase.

Provisions are spent when the next run starts. A run never reads the profile:
provisions become the hero's starting numbers, so a run still follows only from
its seed, tier, hero and provisions.

**Premium chests (D-026).** Each special key carried in places one premium
chest, one per floor from floor 2 to 4 (at most three keys are carried; the rest
stay in the profile). It sits on a plain, empty floor tile at least two steps
from the start, under a cover like everything else, and nothing else on its
floor changes. It opens only with a special key — tapping it without one is
refused — takes 4 taps like an Epic chest and gives 5 rewards; the key stays in
the lock. A key whose chest was never reached goes back to the profile.

## 14. Levels and talents *(D-027, tune numbers)*

Every run earns **experience**, banked with its coins when it ends:

| Source | XP |
|---|---|
| Each monster defeated | 3 |
| Lord Blobert | 30 |
| Each floor walked down (not fallen through) | 10 |
| Winning the run | 20 |

The **level** follows from the total: level 2 at 50 XP, 3 at 150, 4 at 300, 5 at
500 (50 × the triangle numbers). Each level past the first gives one **talent
point**. Talents are learned on the title screen and shape every run started
afterwards; unlike shop provisions they are never used up. Resetting refunds
every point for free.

| Talent | Ranks | Per rank |
|---|---|---|
| Tough | 3 | +1 max heart |
| Stocked | 2 | +1 starting potion |
| Focus | 2 | +1 max mana |
| Fleet | 1 | Dash costs 1 less mana (never below 1) |
| Lucky | 2 | +2 coins for every chest reward |

The level shows as a badge on the portrait on both screens. The TALENTS button
wears its red "!" only while a point is waiting. A run never reads the profile:
talents become the hero's starting numbers, so bots and tier guards (which play
an empty profile) still measure a first run.

## 15. Inventory *(D-028, tune numbers)*

Equipment is found in runs and worn between them. **Lord Blobert**, a vault's
**great chest** and a **premium chest** each drop one item, picked from the table
below by a hash of the run seed, the floor and the spot, so a run always finds
the same item. A find joins the inventory when the run ends (won, lost or
abandoned) and fills its slot if the slot is empty; an item already owned
becomes 25 coins instead. Nothing found changes the run it was found in.

| Slot | Item | Effect |
|---|---|---|
| Weapon | Steel Sword | +1 slash damage |
| Weapon | Lucky Wand | +3 coins for every chest reward |
| Shield | Iron Shield | +1 max heart |
| Shield | Gilded Shield | +1 max mana |
| Armor | Iron Cuirass | +1 max heart |
| Armor | Royal Plate | +2 max hearts |
| Boots | Swift Boots | Dash costs 1 less mana |
| Trinket | Healing Charm | Potions heal 2 more |
| Trinket | Scholar's Ring | +5 XP for every floor walked down |

One item per slot. What is worn shapes every run started afterwards and is never
used up; a dash cost cut adds to the talent's and never goes below one. The
INVENTORY screen shows the five slots and every item (unfound ones as "?"); tap a
found item to wear it, tap a worn slot to take it off.

## 16. Daily reward *(D-029, tune numbers)*

The title's DAILY REWARD panel pays once per calendar day (the player's own
clock). Coming back every day walks through a week; missing a day starts the
week over at day 1, and day 7 is followed by day 1 again.

| Day | Reward |
|---|---|
| 1 | 30 coins |
| 2 | a potion ration |
| 3 | 50 coins |
| 4 | a heart token |
| 5 | 80 coins |
| 6 | 3 gems |
| 7 | a special key |

Rations, tokens and keys are the shop's own and go into the next run like bought
ones. After claiming, the panel shows the open chest and tomorrow's reward. A
clock set back before the last claim cannot claim again until that day has
passed. HOW TO PLAY, whose panel this place held, is now in the top-right menu.

## 17. Achievements and mail *(D-030, tune numbers)*

The **crown** (top right of the title) lists goals counted over every run. Each
is earned once, when its count reaches the target; the counts only change when a
run is banked (won, lost or abandoned).

| Achievement | Goal | Gift |
|---|---|---|
| First Steps | Finish a run | 25 coins |
| Deep Diver | Reach floor 3 | 40 coins |
| Into the Lair | Reach floor 5 | a potion ration |
| Blobert Bested | Defeat Lord Blobert | 5 gems |
| Champion | Win 5 runs | a special key |
| Monster Hunter | Slay 25 monsters | 50 coins |
| Monster Slayer | Slay 100 monsters | 3 gems |
| Treasure Seeker | Open 20 chests | 50 coins |
| Hoarder | Carry out 1,000 coins | 4 gems |
| Seasoned | Reach level 5 | a heart token |
| Collector | Own 5 pieces of gear | 3 gems |

**Mail** (next to the crown) holds letters: a welcome with a potion ration for a
new profile, one for every level gained, and one for every achievement, carrying
its gift. A gift stays in its letter until COLLECT (or COLLECT ALL) adds it to
the profile, once. The mail button shows the red "!" while any letter is unread
or holds a gift. The newest 40 letters are kept; the oldest read letters with
nothing to collect make room, and a gift is never thrown away.

