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

Nothing on a dungeon floor stops the hero except a locked vault door and a tile someone is standing on (D-021).
A vault room is the one board with an edge: its nine tiles are surrounded by stone the hero cannot step into (§7.1, D-064).

| Thing        | Hero can enter | Enemy can enter | Blocks ranged line |
|--------------|----------------|-----------------|--------------------|
| Floor        | yes            | yes             | no                 |
| Wall         | **no** — only the stone around a vault room (§7.1); on a dungeon floor the sprite is the cover over an unrevealed tile (§2.1) | no | **yes** |
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
| `Charge(direction)` | the whole line marked, **GORE** | runs the line; the hero anywhere on it takes the blow and stops the charge (D-058) |
| `Throw(cell)`     | landing tile marked **BOMB**      | a lit bomb lands on `cell`; it blows up the turn after, as any bomb (D-058) |
| `Reassemble`      | **BONES n** countdown             | nothing; after its turns run out the bones stand up again (D-058) |
| `Web(cell)`       | **WEB** on the marked tile        | if the hero is still on `cell`, they are webbed: no Move or Dash on their next command (D-061) |
| `Vanish(cell)`    | **ARRIVES** on the marked tile    | the boss reappears on `cell`, unless something has taken it (D-062) |

Attacks target **cells**, not the hero. Moving out of a telegraphed cell
avoids the hit. This is the core of Shield/Dash decisions.

### 3.5 Gate 1 enemies *(tune numbers)*

| Enemy          | HP | Dmg | Behaviour                                                                 |
|----------------|----|-----|---------------------------------------------------------------------------|
| Goblin         | 3  | 2   | Adjacent to hero → `Attack(hero cell)`; else `Move`.                      |
| Crowned Slime  | 5  | 3   | Alternates action / `Rest`. On action turns: adjacent → `Attack`, else `Move`. |
| Fire Imp       | 2  | 2   | Hero in a straight clear lane within 3 → `Fire(dir)` (the telegraph *is* the aim). After firing → `Rest` (reload). Otherwise `Move` toward a lane cell at distance 2–3. Adjacent → steps away if possible. |
| Slimelet (summon) | 1 | 1 | Goblin behaviour.                                                       |

**The undead** *(D-072)*. Three monsters are risen rather than alive: the **Skeleton Warrior**, the
**Spooky Spellbook** and the **Spectral Page** it sends out. Holy damage answers them — today that is the
Paladin's Holy Wrath — and the Inspect panel says so on the tile, because a bonus the player cannot see is
one they cannot plan around. A Mimic Chest is furniture and the Theater Curtain Demon was never alive, so
neither is undead.

**First expansion monsters (D-058).** Each adds one rule, telegraphed a turn ahead like every other.

| Enemy            | HP | Dmg | From floor | Behaviour |
|------------------|----|-----|------------|-----------|
| Skeleton Warrior | 3  | 2   | 3 | Crowned Slime's pace (acts every other turn). Its **first** fall is not a death: it collapses into bones at 1 heart, shown as `BONES n`. Any hit in the next 2 of its turns breaks it for good; left alone, it stands up at half its hearts (rounded up), and its next fall is final. The kill, and its experience, count only when the bones break. |
| Goblin Bomber    | 2  | 1   | 6 | Within 3 tiles (king's moves) of the hero, and the hero on open floor → `Throw(hero's cell)`: a lit bomb lands there next turn and explodes the turn after, so the hero has a turn to step clear. After a throw → `Rest`. Adjacent → steps away, or `Attack` if cornered. Never throws at a tile that cannot take a bomb (a chest, key, potion, pad, plate, the exit, or a hazard). |
| Armored Boar     | 5  | 3   | 12 | Adjacent → `Attack`. Otherwise, the hero on a clear straight line within 4 → `Charge(dir)`: the whole path is marked, and the hero anywhere on it next turn is gored and stops the charge. Walls, hazards, doors and other monsters stop the line. After a charge it is winded → `Rest`. Otherwise it lumbers a step every other turn. |

**Second expansion monsters (D-061).**

| Enemy             | HP | Dmg | From floor | Behaviour |
|-------------------|----|-----|------------|-----------|
| Mimic Chest       | 4  | 3   | 2 | Sits asleep looking exactly like a closed chest: the board draws a chest and its tap meter, the clue is Treasure, and every command a chest accepts (tap, open, dash onto it) is accepted - there is no free probe. Tapping or bumping it, or the hero stepping beside it, wakes it; then it is a Goblin. Drops 15 coins when it falls. |
| Cave Spider       | 3  | 2   | 7 | Hero within 3 and not already webbed → `Web(hero's cell)`. Webbed, the hero cannot Move or Dash on their next command (Slash, Shield, Potion and Wait still work); the web is gone after that command. After a web → `Rest`. Otherwise a Goblin. |
| Spooky Spellbook  | 3  | 2   | 11 | Fires down lanes as a Fire Imp. Every third action, with fewer than 2 pages out → `Summon` a Spectral Page (1 HP, 1 dmg, a Goblin) beside it. |
| Goblin Key Warden | 3  | 1   | 4, 8, 14, 18 | Placed by the floor, not drawn from a pool: on these floors it holds the key instead of the key lying on a tile. It backs away from the hero, resting after each step so the hero can catch it; cornered and adjacent it attacks. When it falls the key drops on its tile (or the nearest clear one). |

AI tie-breaks are deterministic (fixed direction order Up, Right, Down, Left;
then lowest actor id). No RNG in AI.

### 3.6 Act bosses *(D-062, tune numbers)*

A boss closes each act of five floors. Its floor has no key; the exit is sealed until it falls, and no pit on it leads
down past it. **Beating an act boss restores the hero's full hearts and mana.**

| Floor | Boss | HP | Dmg | Script |
|---|---|---|---|---|
| 5 | Goblin Brute King | 10 | 2 (slam 2) | A three-step cycle: (1) adjacent → `Attack`, else the hero on a clear line within 3 → `Charge` as the Boar, else `Move`; (2) `Slam` on the hero's cell and every tile touching it; (3) `Charge` if the hero is on a line, else as (1). Winded → `Rest` after any charge. At half his hearts or below he is **Enraged** for the rest of the fight: every blow +1, shown in its warning. |
| 10 | Bat Swarm Leader | 13 | 2 | Cycles `Summon` (two Bats, 1 HP / 1 dmg, never more than 4 out) → `Charge` (a dive down a clear line within 4) → `Attack` adjacent, else `Charge` or `Move`. Rests after any dive. |
| 15 | Theater Curtain Demon | 16 | 3 (slam 3) | Four-step script: `Summon` two Stage Masks (1 HP / 2 dmg, at most 3 out) → `Slam` the hero's whole row and column → `Fire` down a lane, or `Attack` adjacent → `Vanish` to the free tile farthest from the hero (only if one is farther than where it stands), marked **ARRIVES** the turn before. |
| 20 | Lord Blobert | 18 | 2 (slam 4) | Below. |

### 3.7 Lord Blobert (last-floor boss) *(tune numbers)*

HP 18 (D-039). Uses the same intent system. His court hides 2 spike traps and a bomb
under its covers. Repeating script:

1. **Boast → Slam**: declares `Slam` on the hero's cell, every tile touching it,
   and that cell's whole row and column (D-039). Executes next phase for 4 damage.
2. **Summon**: declares `Summon` on an empty cell adjacent to himself; two
   Slimelets appear next phase, the second on another free tile beside him (D-039).
3. **Slam again** (D-040): a second slam, as in step 1.
4. **Puff Up**: declares `PuffUp`. For the next 2 turns he is **Puffed**:
   immune to damage, `Move`s toward the hero, attacks adjacent for 2.
5. **Deflated**: 1 turn, no action, takes **double damage**.

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

Max HP 10. Slash damage 3 (D-047). Starts each run with 2 potions. Another hero may take
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

| Class | Hero | Hearts | Slash | Potions (heal) | Mana | Shield | Dash | Class rule |
|---|---|---|---|---|---|---|---|---|
| Knight | Ironheart, Sir Clickington | 10 | **3** | 2 (heal 4) | 6 | 2 | 2 tiles for 3 | - |
| Paladin | Dawnward | **11** | 2 | 2 (heal 6) | 8 | 2 | **1 tile** for 4 | - |
| Rogue | Shadowcut | 10 | 2 | 2 (heal 4) | 6 | 2 | 2 tiles for 2 | **Ambush** |
| Wizard | Emberwisp | 9 | **3** | 2 (heal 4) | **8** | 2 | 2 tiles for 3 | **Firebolt** |
| Ranger | Windsong | 8 | **3** | 2 (heal 4) | 6 | 2 | 2 tiles for 2 | **Longshot** |
| Cleric | Lightbringer | 8 | 2 | 2 (heal 4) | 6 | **3** | 1 tile for 4 | **Sanctuary** |
| Berserker | Rageclaw | 9 | 2 | 2 (heal 4) | **4** | **3** | 1 tile for 3 | **Rage** |
| Engineer | Gearspark | 7 | **1** | 2 (heal 4) | 6 | 2 | 2 tiles for 3 | **Spark Drone** |

The Paladin is the steadier of the first two: more hearts, a stronger potion and a
deeper mana pool for shields, paid for with a dash that moves one tile and costs
more — and dash is how a hero crosses a trap or breaks away from a monster.
Difficulty tiers apply to whichever class is playing (§10).

**Class rules (D-063).** Six of the classes carry one rule of their own. Each is a perk the class starts every run
with, so a talent that sharpens it simply raises the same perk.

| Rule | Class | What it does |
|---|---|---|
| **Ambush** | Rogue | Slashes deal **+2** against a monster whose declared action is not aimed at the hero's tile — the same telegraph the player is shown (§3.2), so the opening is always visible before it is taken. |
| **Firebolt** | Wizard | Slash reaches **3** tiles in a straight line, diagonals included, and knocks the target back one tile. |
| **Longshot** | Ranger | Slash reaches **4** tiles in a straight line, **+1** damage against a target 3 or more tiles away. |
| **Sanctuary** | Cleric | Every attack the shield blocks heals **1** heart, **while she is at half hearts or fewer** (D-071). |
| **Rage** | Berserker | Slashes deal **+1** for every **6** hearts missing (D-071; it was 4). |
| **Spark Drone** | Engineer | On every turn the hero does **not** slash, the drone zaps one awake monster within 1 tile for **1**. |

**A slash that reaches** (Wizard, Ranger) obeys the cover rule (§2.1): every tile between the hero and the target must
be **uncovered** and clear of walls, doors and awake monsters. A covered tile refuses the shot, whatever is under it, so
a refusal never tells the player anything about a cover. A bomb is still armed by hand, from a tile beside it, and a
knock-back only ever pushes onto an uncovered tile for the same reason. Bosses are never knocked back.

Measured on Knight's Trial, 40 blind seeds per class with the casual bot and no talents (`ClassSweep`, D-063):
Knight 26, Paladin 23, Rogue 26, Wizard 26, Ranger 25, Cleric 30, Berserker 23, Engineer 22. With the novice bot:
5 / 8 / 2 / 5 / 5 / 8 / 9 / 7. `TheClassesWinAboutAsOftenAsEachOther` guards the spread.

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
  will open it. A boss floor's sealed exit ignores keys. The exit only triggers when
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
### 7.1 The vault room *(D-018, D-064)*

- An **open door** on a floor leads to a room off it. Stepping into the door enters the room; the
  floor behind is kept exactly as it was and waits.
- The room is **nine tiles, three by three**, in the middle of the board, and the rest of the board is
  solid stone — the one place in the game where a wall is real rather than a cover (§1.2). The stone is
  drawn as known from the moment the hero arrives, so it never reads as something to click.
- **The door the hero came through is the middle tile.** It is the way back out, always open, and the
  hero arrives on one of the four tiles beside it — nobody ever stands in a doorway. Stepping back onto
  it returns them to the tile they entered from, on the floor they left.
- **Guards** (2–3) start awake and stand two tiles clear of the tile the hero arrives on, as on any
  other floor. **Chests** stand in the room: one great chest, or two or three ordinary ones.
- A vault hangs off a floor, so it has no key, no exit down, no hazards and no pits, and it carries its
  floor's index — renown reaches its guards (D-046).
- **A vault is a one-time room.** Its door stays open and a second visit finds it exactly as it was
  left: looted chests, dead guards and all.

- **Floor complete** → next floor generates, hero HP/potions/boons carry over,
  mana refills, key is cleared.
- **The stairs' mercy** *(D-071, D-073)*: on top of the tier's breather, the stairs never leave a hero below a
  share of their hearts **set by the tier** — half on Squire's Stroll and Knight's Trial, and **none at all on
  Blobert's Wrath**, whose card has always ended "No mercy." It is help that only arrives when it is needed — a
  careful player is under half on 6% of turns and a careless one on up to a third of them — so on the tiers that
  give it, it lifts the floor of the game without raising its ceiling.
- **Run**: 20 floors in four acts of five (D-062; it was 7). Floors 5, 10 and 15 are act bosses (§3.6); floor 20 is
  Lord Blobert's arena, whose exit is unlocked when he dies and stepping on it wins the run.
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
5. no enemy within Manhattan distance 2 of the start (no instant wake). **A Key Warden is exempt**
   (D-061): it is handed the key after the floor validates, it backs away rather than ambushing, and it is the
   floor's key, so it stands where the key was placed;
6. at most one of Exit/Hazard/Content per cell;
7. every content/enemy id exists in the catalog;
8. boss floor: boss placed, and at least 8 enterable cells;
9. vault: nine Floor tiles in a three-by-three block, the exit at its middle, the start beside it, stone
   everywhere else, no key and no boss (§7.1).

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
above; only numbers change. §3–§8 describe the base content; Knight's Trial's traps hit one harder than that (D-050) and
it adds one monster a floor (D-038).

| Setting                                | Squire's Stroll (easy) | Knight's Trial (medium) | Blobert's Wrath (hardcore) |
|----------------------------------------|------------------------|-------------------------|----------------------------|
| Hero max HP                            | 14 (+4)                | 10                      | 10                         |
| Starting potions                       | 3 (+1)                 | 2                       | 1 (−1)                     |
| Goblin / Slime / Imp / Slimelet HP     | 3 / 5 / 2 / 1          | 3 / 5 / 2 / 1           | 4 / 6 / 3 / 2 (+1)         |
| Goblin / Slime / Imp / Slimelet damage | 1 / 2 / 1 / 1 (−1)     | 2 / 3 / 2 / 1           | 2 / 3 / 2 / 1              |
| Lord Blobert HP / Slam / puffed attack | 16 / 3 / 1             | 18 / 4 / 2              | 22 / 5 / 2                 |
| Spikes / bomb damage                   | 1 / 3                  | 3 / 5 (+1)              | 3 / 5 (+1)                 |
| Enemies on normal floors               | profile                | profile +1              | profile                    |
| HP restored on arriving at a new floor | 3                      | 0                       | 0                          |
| The stairs never leave the hero below  | half their hearts      | half their hearts       | — (no mercy)               |
| First run of a profile starts with     | +3 hearts, +1 potion   | +3 hearts, +1 potion    | — (no grace)               |

Hero max HP is the Knight's. The Paladin has one more heart on every tier (D-047).

Damage and HP never drop below 1.

### 10.0 The first run *(D-076)*

The **first** run of a profile — and only the first, counted by `RunsFinished`, won or lost — starts with a few more
hearts and an extra potion. It is onboarding, not a difficulty setting.

Why it exists, measured: on Knight's Trial, a fresh careless player was killed by **spikes in a third of runs**, and
spikes did more damage than every monster in the game put together (975 against the next-worst 345 over 30 runs). The
cover rule means a new player could not have known the spikes were there — they are being punished for not yet knowing
the game rather than for playing it badly.

Hearts and potions rather than softer traps, deliberately: **slack, not a lie.** A trap that hits for less while you
are learning teaches you the wrong number and then changes it behind your back.

Measured over 240 blind seeds, grace off against on:

| tier | casual | careless |
|---|---|---|
| Squire's Stroll | 99% → 100% | 100% → 99% |
| Knight's Trial | **76% → 87%** | **21% → 35%** |
| Blobert's Wrath | 46% → 46% | 4% → 4% |

Blobert's Wrath gives none, the same call as the stairs' mercy (D-073): the tier is opt-in, its card ends "No mercy",
and nobody meets the game for the first time on it by accident.

### 10.0.1 What the dungeon does differently *(D-074)*

Everything in the table above is a magnitude. These are the things a tier changes about how the dungeon **behaves** —
how long the player has, and how fast the board fills. Squire's Stroll gives time and fewer bodies; Blobert's Wrath
takes the time away and adds bodies.

| Behaviour                                   | Squire's Stroll | Knight's Trial | Blobert's Wrath |
|---------------------------------------------|-----------------|----------------|-----------------|
| Turns stuck in a spider's web                | 1               | 1              | **2**           |
| Turns a bomb sits before it goes off         | **3**           | 2              | 2               |
| Turns Lord Blobert is puffed and untouchable | **1**           | 2              | **3**           |
| Turns he lies deflated afterwards            | **2**           | 1              | 1               |
| Minions a summon brings                      | **1**           | 2              | **3**           |
| Turns a skeleton lies as bones               | **3**           | 2              | **1**           |
| Pages a Spooky Spellbook keeps up            | 2               | 2              | **3**           |

Two floors are the telegraph contract (§3.2) and not taste: a bomb always waits at least one full turn, because a blow
with no warning is not a difficulty setting, and a summon always brings at least one minion, or the warning promises
someone who never arrives. `ApplyDifficulty` clamps both.

### 10.1 Measured difficulty

**Current** (after D-076, 2026-09-25). **240** blind seeds, Free Roam, empty profile — won:

| Player       | Squire's Stroll | Knight's Trial | Blobert's Wrath |
|--------------|-----------------|----------------|-----------------|
| casual (20%) | 100%            | 87%            | 46%             |
| novice (50%) | 99%             | 35%            | 4%              |

An empty profile is a **first** run now, so these include D-076's grace — +3 hearts and a potion on the two gentler
tiers, none on Blobert's Wrath. Without it the same sweep gives 99 / 76 / 46 casual and 100 / 21 / 4 novice, which is
what every measurement in this file before D-076 was taken against. A hero on their *second* run and after meets those
older numbers, and nothing else about them changed.

No run stalled in any tier. Knight's Trial and Blobert's Wrath sit higher than audit 4 recorded (62% and 53% casual)
because D-071's stairs mercy landed in between; Blobert's Wrath came back down when D-073 took that mercy away again
from the one tier whose card promises none.

**D-074's behavioural numbers barely move these, and that is the expected result rather than a disappointing one.**
Flat behaviour against tiered, same 240 seeds: Squire's Stroll novice 97% → 100%, Blobert's Wrath casual 45% → 46%,
novice 5% → 4%, Knight's Trial unchanged either way. A bot with a one-turn look-ahead does not feel a web that holds a
turn longer or a fuse that gives a turn more — it reads the board perfectly and has no reaction time to pressure. These
knobs are aimed at a person, and **the bot is the wrong instrument for them** (the same point TEST-85 makes about what
"careless play" measures). They were therefore NOT tuned upward until the bot noticed; doing that would be tuning
against an instrument that cannot see the thing being tuned, which is how D-067 happened. What the sweep is good for
here is confirming no tier was accidentally inverted, and none was.

No run stalled in any tier. The repairs cost a few points across the board — a boss no longer revives a hero who died
in the same step, summons no longer overrun their caps, and Dodge no longer eats spikes — and they brought Knight's
Trial and Blobert's Wrath closer together for a blind novice (12% against 8%). That gap is real at 240 seeds but too
small for the 40-seed `TiersKeepTheirOrder` guard to resolve, so that guard asserts the ordering is not inverted and
leaves the separation to this sweep.

Per class on Knight's Trial (`ClassSweep`, 40 blind seeds, no talents; pre-D-071 numbers), casual / novice wins: knight 25/4,
paladin 22/8, rogue 20/1, wizard 25/3, ranger 23/6, cleric 28/8, berserker 21/8, engineer 20/5. The parity guard
holds the spread to a ratio of 8/5 between the weakest and strongest class.

These numbers are for a hero with **no talents, gear or renown**: `AutoPlayer.PlayRun` starts every run from an
empty profile and never sets `Threat`, so **none of them say anything about renown** (D-067). A renowned run is
measured on its own by `ARenownedRunIsStillARunSomeoneCanWin`: 30 wins in 100 at full renown against the 64 a hero
with none gets. They measure the dungeon and cannot see the class trees at all — a talent could be broken, as
Judgement was until audit 3, without moving any of them. The curve a levelled player meets is measured only by the
explicit `TenPlaythroughs`.

#### The first sweep (sighted — superseded)

Kept for the record: everything below was measured with a **sighted** bot, before the bot could stall
(D-048) was found and fixed. A sighted bot walks straight to a key it should not be able to see, so these
rows are far easier than the game. Use the table above.

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

**Re-measured since, newest first** (60 blind seeds, `DifficultySweep`, won, casual then novice):

*D-056 re-measure* (after audit 3: the floor validator learned that a teleport pad carries the hero to its pair).
Casual 97% / 75% / 43%, novice 100% / 32% / 18% — about three points a tier against D-050, and no tier retuned. See
§10.1 for the full table.

*D-050 retune* (Knight's Trial traps hit one harder). `DifficultySweep`, 60 blind seeds, won: casual 97% / 78% / 45%,
novice 100% / 35% / 15%. `HeroSweep`, 40 blind novice seeds: Knight 40 / 12 / 8 won, Paladin 40 / 14 / 14.

*D-048 re-measure* (the balance bot no longer loops between teleport pads, so these are the first numbers that measure
the dungeon rather than the bot). `DifficultySweep`, 60 blind seeds, Free Roam, won: casual 97% / 92% / 48%, novice
95% / 55% / 15%. `HeroSweep`, 40 blind novice seeds: Knight 37 / 20 / 9 won, Paladin 39 / 17 / 10.

*D-038 retune* (full-strength traps and one more monster a floor on Knight's Trial; traps +1 on Blobert's
Wrath). `DifficultySweep`, 60 blind seeds, Free Roam, won: casual 95% / 70% / 53%, novice 100% / 67% / 33%.
Before it, casual won 88% of Knight's Trial and the ten-run playthrough won all ten.

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
| `tile_stair_down_locked`, `tile_stair_down` | floor exit | locked until the key is held; open exits descend. A vault's is the door at the middle of the room and leads back out (§7.1) |
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

Nothing on a dungeon floor blocks the hero except a **locked vault door** and a tile with an actor on it.
No dungeon floor is generated with walls: the wall sprite is the cover drawn over a tile the player has
not revealed (§2.1). The stone around a vault room is the one real wall in the game, and it is drawn as
known from the moment the hero walks in, so it is never a cover (§7.1). A **pit** is entered deliberately and drops the hero a floor for fixed damage (§4);
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
| Mana tonic | 90 coins | +2 max mana at the start of the next run (D-036) |
| Strength elixir | 150 coins | +1 slash damage on the next run |
| Fortune scroll | 100 coins | +3 coins per chest reward on the next run |
| Wisdom scroll | 100 coins | +10 XP per floor walked down on the next run |

The shop (D-036) has four tabs. **BOOSTS** are the provisions above. **GEAR** sells
four pieces of equipment a day, chosen by rarity weight from the calendar day:
common 150 coins, uncommon 300, rare 600, epic 15 gems, legendary 30 gems; a piece
already owned is not for sale. **CHESTS**: a gear chest (400 coins) holds one random
piece of gear, a royal chest (20 gems) one rare or better; a piece already owned
becomes 25 coins. **EXCHANGE** is the purse's trade below.

Gear has a **rarity** (common, uncommon, rare, epic, legendary) and six slots
(weapon, helmet, armor, shield, boots, trinket). Drops and chests pick by rarity
weight 6 / 4 / 3 / 2 / 1, so a legendary piece is six times rarer than a common one.

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

## 14. Levels and class talents *(D-027, D-037, tune numbers)*

Every run earns **experience**, banked with its coins when it ends:

| Source | XP |
|---|---|
| Each monster defeated | 3 |
| Lord Blobert | 30 |
| Each floor walked down (not fallen through) | 10 |
| Winning the run | 20 |

The **level** follows from the total: level 2 at 50 XP, 3 at 150, 4 at 300, 5 at
500 (50 × the triangle numbers). Each level past the first gives one **talent
point** for **every class**: each class spends the level's points on its own tree
(D-037), so switching heroes never costs a build. Talents shape every run that class
starts; unlike shop boosts they are never used up. Resetting a tree is free.

Each tree has three **paths** of four tiers. Tier 1 is open at once; tier 2 needs 2
points spent in the class, tier 3 needs 4, and the tier-4 **capstone** needs 7. Every
talent past tier 1 needs the talent below it in its path. A class takes **one
capstone** only, so a build commits to a path.

**Knight (Sir Clickington): versatile vanguard.**

| Path | Tier | Talent | Ranks | Per rank |
|---|---|---|---|---|
| Blade | 1 | Opening Strike | 3 | +1 slash damage against an enemy at full health |
| Blade | 2 | Cleave | 1 | A slash also deals 1 to every other awake enemy next to you |
| Blade | 3 | Executioner | 2 | +1 slash damage against an enemy at 2 hearts or fewer |
| Blade | 4 | Relentless | 1 | A slash kill restores 1 heart and 2 mana |
| Bulwark | 1 | Sturdy | 3 | +1 max heart |
| Bulwark | 2 | Shield Wall | 1 | SHIELD costs 1 less mana |
| Bulwark | 3 | Riposte | 2 | A blocked attack deals 1 back to the attacker |
| Bulwark | 4 | Bastion | 1 | Every SHIELD restores 1 heart |
| Adventurer | 1 | Light Step | 2 | DASH costs 1 less mana (never below 1) |
| Adventurer | 2 | Treasure Sense | 1 | Chests open with one tap fewer (never below 1) |
| Adventurer | 3 | Fortune's Favour | 2 | +3 coins for every chest reward |
| Adventurer | 4 | Second Wind | 1 | A new floor restores 3 more hearts |

**Paladin (Dawnward): holy guardian.**

| Path | Tier | Talent | Ranks | Per rank |
|---|---|---|---|---|
| Hammer | 1 | Holy Wrath | 3 | +1 slash damage against **the undead** (D-072) |
| Hammer | 2 | Consecrate | 1 | SHIELD deals 1 to every awake enemy next to you |
| Hammer | 3 | Dawnstrike | 2 | +1 slash damage against **any boss** (four of them since D-062) |
| Hammer | 4 | Wrath of Dawn | 1 | A slash kill staggers every other awake enemy next to you |
| Aegis | 1 | Plated | 3 | +1 max heart |
| Aegis | 2 | Holy Bulwark | 1 | A blocked attack restores 2 mana |
| Aegis | 3 | Unyielding | 1 | At half hearts or fewer, every hit deals 1 less (never below 1) |
| Aegis | 4 | Divine Shield | 1 | Once per floor, a killing blow leaves you at 1 heart and heals 3 |
| Devotion | 1 | Blessed Draught | 3 | Potions heal 1 more |
| Devotion | 2 | Prayer | 1 | Waiting restores 1 extra mana |
| Devotion | 3 | Guiding Light | 1 | Each new floor starts with its key uncovered |
| Devotion | 4 | Sanctified | 1 | Potions also refill mana; fountains heal fully |

Paths meet: the Knight's Riposte and Bastion reward shielding, which Shield Wall makes
cheap; the Paladin's Holy Wrath answers the risen, and Holy Bulwark pays for the next
shield. The Cleric keeps Judgement under its own name, Rebuke — before D-072 both
classes opened their offence with the same talent twice over. Talents learned before the trees (D-027) are
refunded.

The level shows as a badge on the portrait on both screens. The TALENTS button
wears its red "!" only while a point is waiting. A run never reads the profile:
talents become the hero's starting numbers, so bots and tier guards (which play
an empty profile) still measure a first run.


#### The six new trees (D-063)

Twelve talents a class, three paths of four, on the same tiers and the same capstone rule as above. Each row is
what one rank gives; the ranks column is how many may be bought.

**Rogue (Shadowcut)**

| Path | Tier | Talent | Ranks | Each rank |
|---|---|---|---|---|
| evasion | 1 | Supple Leathers | 3 | +1 max heart |
| evasion | 2 | Light Feet | 1 | DASH costs 1 less mana (never below 1) |
| evasion | 3 | Slippery | 1 | Once per floor, the first hit that would land on you misses |
| evasion | 4 | Vanishing Act | 1 | Arriving on a new floor restores 3 more hearts |
| greed | 1 | Fence | 2 | +3 coins for every chest reward |
| greed | 2 | Lockpick | 1 | Chests open with one tap fewer (never below 1) |
| greed | 3 | Casing the Joint | 1 | Each new floor starts with its key uncovered |
| greed | 4 | Pickpocket | 1 | Every monster you slay with a slash drops 5 coins |
| shadows | 1 | Cruel Edge | 3 | +1 ambush damage |
| shadows | 2 | Twin Fangs | 1 | A slash also deals 1 damage to every other awake enemy next to you |
| shadows | 3 | Coup de Grace | 2 | +1 slash damage against an enemy at 2 hearts or fewer |
| shadows | 4 | Eviscerate | 1 | An ambush that does not kill staggers the target (not bosses) |

**Wizard (Emberwisp)**

| Path | Tier | Talent | Ranks | Each rank |
|---|---|---|---|---|
| arcana | 1 | Deep Well | 3 | +1 max mana |
| arcana | 2 | Meditation | 1 | Waiting a turn restores 1 extra mana |
| arcana | 3 | Soul Siphon | 1 | Slaying an enemy with a slash restores 1 heart and 2 mana |
| arcana | 4 | Alchemy | 1 | Potions also refill your mana, and fountains heal you fully |
| pyromancy | 1 | Searing Bolt | 3 | +1 slash damage against an enemy at full health |
| pyromancy | 2 | Fireball | 1 | A slash also deals 1 damage to every other awake enemy next to your target |
| pyromancy | 3 | Cinders | 2 | +1 slash damage against an enemy at 2 hearts or fewer |
| pyromancy | 4 | Blinding Flash | 1 | Slaying an enemy with a slash staggers every other awake enemy next to you |
| warding | 1 | Warded Robes | 3 | +1 max heart |
| warding | 2 | Quick Ward | 1 | SHIELD costs 1 less mana |
| warding | 3 | Flame Ward | 1 | SHIELD deals 1 damage to every awake enemy next to you |
| warding | 4 | Phoenix Feather | 1 | Once per floor, a blow that would end you leaves you at 1 heart and heals 3 |

**Ranger (Windsong)**

| Path | Tier | Talent | Ranks | Each rank |
|---|---|---|---|---|
| awareness | 1 | Fleet Foot | 2 | DASH costs 1 less mana (never below 1) |
| awareness | 2 | Tracker | 1 | Each new floor starts with its key uncovered |
| awareness | 3 | Scavenger | 2 | +3 coins for every chest reward |
| awareness | 4 | Hawkeye | 1 | Each new floor starts with its exit uncovered |
| marksman | 1 | Aimed Shot | 3 | +1 slash damage against an enemy at full health |
| marksman | 2 | Piercing Arrow | 1 | A slash also deals 1 damage to the enemy right behind your target |
| marksman | 3 | Kill Shot | 2 | +1 slash damage against an enemy at 2 hearts or fewer |
| marksman | 4 | Pinning Shot | 1 | A shot from 3 or more tiles away staggers the target (not bosses) |
| survival | 1 | Ranger's Leathers | 3 | +1 max heart |
| survival | 2 | Herbalism | 1 | Potions heal 1 more |
| survival | 3 | Endurance | 1 | While at half hearts or fewer, every hit deals 1 less damage (never below 1) |
| survival | 4 | Second Wind | 1 | Arriving on a new floor restores 3 more hearts |

**Cleric (Lightbringer)**

| Path | Tier | Talent | Ranks | Each rank |
|---|---|---|---|---|
| judgement | 1 | Rebuke | 3 | +1 slash damage against a staggered enemy |
| judgement | 2 | Holy Light | 1 | SHIELD deals 1 damage to every awake enemy next to you |
| judgement | 3 | Smite the Mighty | 2 | +1 slash damage against bosses |
| judgement | 4 | Radiance | 1 | Every SHIELD also restores 1 heart |
| mercy | 1 | Blessed Draught | 3 | Potions heal 1 more |
| mercy | 2 | Prayer | 1 | Waiting a turn restores 1 extra mana |
| mercy | 3 | Renewal | 1 | Arriving on a new floor restores 3 more hearts |
| mercy | 4 | Holy Water | 1 | Potions also refill your mana, and fountains heal you fully |
| sanctity | 1 | Faith | 3 | +1 max heart |
| sanctity | 2 | Swift Grace | 1 | SHIELD costs 1 less mana |
| sanctity | 3 | Blessed Ward | 1 | Blocked attacks heal 1 more |
| sanctity | 4 | Miracle | 1 | Once per floor, a blow that would end you leaves you at 1 heart and heals 3 |

**Berserker (Rageclaw)**

| Path | Tier | Talent | Ranks | Each rank |
|---|---|---|---|---|
| fury | 1 | Bloodlust | 3 | +1 slash damage while at half hearts or fewer |
| fury | 2 | Wide Swing | 1 | A slash also deals 1 damage to every other awake enemy next to you |
| fury | 3 | Brutal Finish | 2 | +1 slash damage against an enemy at 2 hearts or fewer |
| fury | 4 | Blood Frenzy | 1 | Slaying an enemy with a slash restores 1 heart and 2 mana |
| hide | 1 | Thick Hide | 3 | +1 max heart |
| hide | 2 | Iron Gut | 1 | Potions heal 2 more |
| hide | 3 | Pain Is Progress | 1 | While at half hearts or fewer, every hit deals 1 less damage (never below 1) |
| hide | 4 | Undying Rage | 1 | Once per floor, a blow that would end you leaves you at 1 heart and heals 3 |
| warpath | 1 | Headlong | 2 | DASH costs 1 less mana (never below 1) |
| warpath | 2 | Smash Open | 1 | Chests open with one tap fewer (never below 1) |
| warpath | 3 | Plunder | 2 | +3 coins for every chest reward |
| warpath | 4 | War Cry | 1 | Slaying an enemy with a slash staggers every other awake enemy next to you |

**Engineer (Gearspark)**

| Path | Tier | Talent | Ranks | Each rank |
|---|---|---|---|---|
| control | 1 | Riveted Plating | 3 | +1 max heart |
| control | 2 | Quick Deploy | 1 | SHIELD costs 1 less mana |
| control | 3 | Shock Plating | 2 | An attack your shield blocks deals 1 damage back to the attacker |
| control | 4 | Static Field | 1 | SHIELD deals 1 damage to every awake enemy next to you |
| invention | 1 | Calibrated Wrench | 3 | +1 slash damage against an enemy at full health |
| invention | 2 | Overclock | 1 | Your drone zaps for 1 more |
| invention | 3 | Long-Range Coil | 1 | Your drone reaches monsters 2 tiles away |
| invention | 4 | Tesla Coil | 1 | Your drone zaps every monster in reach, not just one |
| tactics | 1 | Salvage | 2 | +3 coins for every chest reward |
| tactics | 2 | Lockpicks | 1 | Chests open with one tap fewer (never below 1) |
| tactics | 3 | Survey Drone | 1 | Each new floor starts with its exit uncovered |
| tactics | 4 | Field Repairs | 1 | Arriving on a new floor restores 3 more hearts |

### 14.1 Renown *(D-040, D-067, tune numbers)*

The deeper floors answer a built-up hero. **Renown** is levels past the first plus items worn; every
**2** renown is one point of **threat**, up to **3**. A new hero has no threat.

**Threat arrives with the depth** (D-067). From **floor 3** on, the threat a floor carries is the hero's
own, scaled by how far down the run has come: none of it on floor 3, half of it halfway, all of it on the
last floor. On that floor's threat, every monster has one extra heart per point (Lord Blobert two) — a
monster **summoned** mid-fight takes the same share as one the floor was built with (D-046) — and
every enemy blow — hits, fire, slams — deals one more per 2 points.

Two truncating divisions compose there, so the damage half is coarser than that sentence suggests (D-068).
In a twenty-floor run a hero at **threat 1 meets no raised blow on any floor**; at threat 2, only on floor
20; at threat 3, on floors 14-20. The hearts are what the first point of renown buys. This is the shipped
behaviour, measured rather than intended, and it is recorded here so it is a decision and not a surprise. Spikes, lava and bombs there hurt one
more per 2 points too (D-041); a pit's fall does not change. The telegraphs and the tile descriptions show
the raised damage, and a vault carries its floor's threat because it carries its floor's index (D-046).

Before D-067 the hero's whole threat landed on floor 3 and stayed flat to floor 20. A hero at full renown
then won 5 runs in 100 instead of 64 and died around floor 7; nothing was red because nothing measured it
(see the note under §10.2). The dungeon can also add threat of its own for a hero who has earned none —
`FloorsPerThreat` — but that ships **off**: measured at one step per 8 floors it cost the casual bot five
points on Knight's Trial and stretched the classes to the edge of the parity band.

### 14.2 Usable skills *(D-075, tune numbers)*

The ninety-six talents are all **passive**. A skill is the other thing: a command the player chooses, on a turn they
spend, for mana. A hero carries up to **three**.

- **Unlocking**: one talent a branch, at **tier 2**, names a skill; learning that talent unlocks it. There is no
  separate currency and no separate screen — the tree already decides which branches a build climbed, so it already
  decides which skills that build has. A finished tree carries three; a single-branch build carries one.
- **Why tier 2**: a class may learn only one **capstone**, so skills hung off capstones could never all be reached.
- **Slots**: three, and each class has exactly three skills today, so the loadout is not yet a choice. The profile
  records it anyway, and an empty slot fills itself from what the build unlocked, in tree order.
- **Cost**: mana, from the same pool SHIELD and DASH draw on (D-032). No cooldowns — those were deliberately replaced.
- **Targets**: a skill aimed at the hero needs no tile. A targeted skill reaches an **awake** monster within its range,
  and only an awake one: aiming at a sleeping monster under a cover would be a way to ask what is under it (§2.1).
- **Telegraphs**: a skill is the hero's own action on their own turn, so it warns of nothing and needs no warning (§3.2).

The five things a skill can do — adding one is a line in the catalog, not a branch in the resolver:

| Effect | What it does |
|---|---|
| Heal | Mends the hero. Refused at full health, so a turn is never spent on nothing. |
| Strike | Damages one monster in range. |
| Banish | Damages one monster, **doubled against the undead** (D-072). |
| Burst | Damages every awake monster beside the hero. Refused with nothing adjacent. |
| Stagger | One monster loses its turn. A boss shrugs it off, as the talents that stagger cannot stagger one. |

There is deliberately **no sight skill**. One was written and cut: every class already uncovers radius 1 as it walks,
so a radius-1 reveal does nothing, and radius 2 from the middle of a five-by-five board is the *whole* board. Measured
with one in: a levelled knight's runs fell from ~410 turns to ~200, because there was nothing left to look for. Finding
the way down is the game (§2.1, D-023).

The twenty-four skills, one a branch:

| Class | Skills |
|---|---|
| Knight | Shockwave (burst 2), Rally (mend 2), Shield Bash (stagger) |
| Paladin | Smite (4), Bulwark (burst 3), **Lay on Hands** (mend 3) |
| Rogue | Throat Cut (5), Smoke Bomb (stagger at 2), Bandage (mend 3) |
| Wizard | Firebolt (4 at 3), Arcane Nova (burst 3), Concussion (stagger at 2) |
| Ranger | Aimed Shot (5 at 3), Poultice (mend 3), Snare (stagger at 3) |
| Cleric | Mend (3), Ward (burst 2), **Dispel Undead** (3, doubled against the risen) |
| Berserker | Whirlwind (burst 3), Second Wind (mend 4), War Cry (stagger) |
| Engineer | Discharge (burst 3), Field Repairs (mend 3), EMP Charge (stagger at 2) |

Each class carries one of three different effects, so no build is three of the same button.

Measured, 40 blind seeds a class, careless, built-up: the classes went from a 1.73 spread to **1.33**. Skills compress
rather than spread, because they pay the same whatever the player's footwork is like, and the classes that gained most
(ranger +12, paladin +9, rogue +8) are the ones whose passives are conditional on standing in the right place.

## 15. Inventory *(D-028, tune numbers)*

Equipment is found in runs and worn between them. **Lord Blobert** (50%), a vault's
**great chest** (35%) and a **premium chest** (always) can each drop one item (D-040), picked from the table
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

