# ClickDungeon — Gate 0 Rules Lock

Status: **LOCKED for Gate 1**. Numbers marked *(tune)* are content-definition
values expected to change during the Gate 2 fun test; the *rules* around them
should not change without a decision-log entry (`docs/decisions.md`).

Nothing in presentation (Unity) may invent or override a rule in this file.

---

## 1. Board

- 5×5 grid. Coordinates `(x, y)`, `x` 0..4 left→right, `y` 0..4 bottom→top.
- Adjacency is **orthogonal** (4 neighbours) unless a rule says otherwise.
- Distance words:
  - *Manhattan distance* `|dx| + |dy|` — used for sensing/revealing.
  - *Chebyshev ring* (8 neighbours) — used only for bomb blasts.

### 1.1 Cell layers

Each cell has independent layers:

| Layer     | Gate 1 values                               |
|-----------|---------------------------------------------|
| Terrain   | `Floor`, `Wall`, `Pit`                      |
| Structure | `None`, `Exit` (locked or unlocked)         |
| Hazard    | `None`, `Spikes`, `Bomb`                    |
| Content   | `None`, `Key`, `Chest`, `Potion`            |
| Actor     | hero or enemy (tracked on the actor, not the cell) |

Gate 1 placement rule: a cell holds **at most one** of {Exit, Hazard, Content}.
The layered data model still keeps them separate so later content (e.g. chest
on dangerous terrain) needs no model change.

### 1.2 Passability

| Thing        | Hero can enter | Enemy can enter | Blocks ranged line |
|--------------|----------------|-----------------|--------------------|
| Floor        | yes            | yes             | no                 |
| Wall         | no             | no              | **yes**            |
| Pit          | no             | no              | no                 |
| Closed chest | no             | no              | no                 |
| Opened chest | yes            | yes             | no                 |
| Any actor    | no             | no              | **yes**            |
| Spikes/Bomb  | yes (triggers) | avoided by AI   | no                 |

---

## 2. Information: visibility

### 2.1 What is always known

- **Terrain** (walls, pits, floor) of the whole board is visible from the
  start of a floor. The player can always read the room's shape.
- The **Exit** cell is Revealed from the start (you always know where you are
  going; you may not yet know how to open it).
- Everything else (hazards, content, dormant enemies) is concealed.

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

- Cells at Manhattan distance **≤ 1** become **Revealed**.
- Cells at Manhattan distance **= 2** become at least **Sensed**.

Because the hero can only step orthogonally, **the hero never steps onto an
unrevealed cell by walking**. Dash is the only way to land on a merely Sensed
cell (see 5.4) and the clue set is shown before committing.

### 2.4 Clue set

A Sensed cell shows every category present (flags, not one value):

| Clue      | Present when the cell holds…     | Icon/shape (never colour-only) |
|-----------|----------------------------------|--------------------------------|
| ENEMY     | a dormant enemy                  | claw marks                     |
| DANGER    | spikes or bomb                   | warning triangle               |
| OBJECTIVE | key                              | keyhole                        |
| TREASURE  | chest or potion                  | sparkle                        |
| SAFE      | none of the above                | small dot                      |

Clues are **deterministic and truthful**. No false clues in Gate 1.

---

## 3. Enemies: activation and intent

### 3.1 Dormant / Awake

- Enemies start **Dormant** and hidden. Dormant enemies do nothing.
- An enemy **wakes** the moment its cell becomes Revealed.
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
- AI pathing treats un-detonated hazard cells as blocked.

---

## 5. Hero: Sir Clickington (Knight) *(tune numbers)*

Max HP 10. Slash damage 2. Starts each run with 2 potions.

| Command   | Turn cost | Cooldown | Rule |
|-----------|-----------|----------|------|
| Move      | 1 | – | Orthogonal step into an enterable cell. |
| Wait      | 1 | – | Do nothing (tap Sir Clickington / Space). |
| Slash     | 1 | – | Target orthogonally adjacent cell with an awake enemy (damage) or a bomb (arms it). |
| Shield    | 1 | 3 | Gain **Guard** until end of this turn. Guard blocks all enemy attack and bomb damage. Melee attackers blocked by Guard become **Staggered** (next intent `Recover`). Spikes are not blocked. |
| Dash      | 1 | 3 | Move exactly 2 cells in a straight line. The middle cell must be enterable-or-hazard and not an actor/closed chest/wall/pit; middle-cell hazards are **not** triggered. Landing cell must be enterable; landing triggers its hazard/pickups. Landing may be a Sensed cell; it may not be a cell with an ENEMY clue. |
| Potion    | 1 | – | Heal 4 (not above max). Requires ≥ 1 potion. |
| Interact  | 1 | – | Target orthogonally adjacent closed chest: opens it (see 7). |

Cooldown `N` = after use, the ability is unavailable for the next `N−1`
player turns.

Illegal commands are rejected with a reason and cost nothing.

Entering a cell (walk or dash landing) resolves, in order: hazard → pickup
(key / potion auto-collect) → exit.

---

## 6. Turn contract

UI actions (inspect, pause, dialogue, chest celebration taps) cost 0 turns and
never touch simulation state.

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
   intent. Guard expires. Cooldowns tick down. Turn counter +1.
10. **Events** are emitted for presentation.
11. **Stable boundary** → autosave → accept next command.

Ordering is covered by automated tests.

---

## 7. Keys, exit, chests, floors

- **Key**: normal floors have exactly one key and a locked exit. Walking onto
  the key collects it. Walking onto the locked exit while holding the key
  unlocks it, consumes the key and completes the floor.
- **Chest**: `Interact` with an adjacent closed chest.
  - The **reward is decided and committed by the Interact command itself**
    (turn cost 1) and saved at the stable boundary of that turn.
  - Reward is derived from `hash(runSeed, floorIndex, chestId)` — it never
    consumes a shared RNG stream, so opening a chest cannot change anything
    else.
  - The 3-tap open sequence is **presentation only** revealing an
    already-committed reward. Taps cost 0 turns and cannot duplicate rewards.
  - Gate 1 reward table: +1 potion, +2 max HP (and heal 2), +1 Slash damage.
- **Floor complete** → next floor generates, hero HP/potions/boons carry over,
  cooldowns reset, key is cleared.
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
