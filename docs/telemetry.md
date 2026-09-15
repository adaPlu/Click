# ClickDungeon — Playtest Telemetry (Gate 2)

Purpose: answer the Gate 2 question, *"Are players making informed decisions on the 5×5 board?"*,
alongside observation and interviews. See decision D-015.

## Privacy and storage

- Local files only. Nothing is sent over the network.
- One JSON-lines file per app launch, created when the first event is logged:
  `%USERPROFILE%\AppData\LocalLow\Clickd\ClickDungeon\telemetry\session-<utc>-<id>.jsonl`
  (Android and iOS use `Application.persistentDataPath/telemetry`).
- No personal data: session ids are random, and runs are identified by their seed.
- On by default in prototype builds. Toggle: **Settings → PLAYTEST LOG**. Automation runs (`-cdShot`) only log when `-cdTelemetryDir <folder>` is given, which keeps bot data out of playtest logs.
- Collect files from testers' machines by hand.

## Reading the logs

- In Unity: **ClickDungeon → Telemetry → Summarize Logs** writes `summary.md` next to the logs.
- Anywhere:
  ```bash
  dotnet run --project Sim/ClickDungeon.Telemetry.Report -- path/to/logs --out summary.md
  ```

## Line format

```json
{"time":"2026-09-14T12:00:00.0000000Z","session":"a1b2c3d4e5f6","event":"damage_taken","run":42,"floor":2,"turn":17,"data":{"amount":2,"source":"goblin","hp_after":6,"cell":"1,3"}}
```

`turn` is the turn in which the event happened. Cells are `"x,y"`, with (0,0) bottom-left.
`run_started` carries `schema` (currently 1) and the ruleset, content and generation versions.

## Events

| Event | When | Key data |
|---|---|---|
| `run_started` / `run_resumed` / `run_abandoned` | session lifecycle | versions, hero |
| `floor_started` | floor generated | template, transform, enemies, hazards, chests |
| `tile_sensed` / `tile_revealed` | knowledge changes | clue set / actual contents |
| `tile_choice` | every accepted Move or Dash | options, chosen, `consequential`, hero, nearby intents |
| `player_move` | Move / Dash | from, to, via |
| `ability_used` | every non-Move command | ability, target, `threat_here`, hero, intents |
| `damage_taken` / `damage_blocked` / `healed` | HP changes | amount, source, hp_after |
| `enemy_woke` / `enemy_defeated` | enemies | enemy, cell, killing source |
| `trap_armed` / `trap_triggered` | spikes, bombs | trap, cell, cause (step / slash / blast) |
| `pickup_collected` / `chest_opened` | rewards | item / reward kind |
| `optional_reward_skipped` | leaving a floor with an unopened chest or uncollected potion | reward, cell, knowledge |
| `exit_unlocked` / `floor_completed` | progression | turns on floor, hp |
| `run_completed` / `run_failed` | run end | turns, hp, cause of death |
| `command_rejected` | illegal input | command, reason (UI confusion signal) |

## Tile choices

For each legal destination, `tile_choice` records **only what the player could know**:

| Field | Meaning |
|---|---|
| `threat` | telegraphed damage on that tile next turn |
| `enter_cost` | spike damage for stepping there (if revealed) |
| `arms_bomb` | stepping there arms a revealed bomb |
| `pickup` | revealed key / potion on the tile |
| `exit` | the tile is the exit |
| `known_enemy` / `known_danger` / `known_treasure` / `known_objective` | clues on sensed tiles that stepping there would reveal |
| `unknown_cells` | unseen tiles that stepping there would reveal |
| `exit_distance` | walking distance to the exit (progress) |

**Consequential**: at least two options whose visible signature differs (threat, enter cost, bomb, pickup,
exit, revealed clue counts, unknowns). Choices between equivalent tiles are not consequential.

**Informed (proxy)**: a consequential choice that, while not walking into telegraphed damage when a safe
tile existed, does at least one of these:
- picks the lowest-threat tile
- knowingly wakes a sensed enemy
- goes for visible treasure or the key
- deliberately pays spikes
- moves closest to the exit

The summary reports this against the ~70% Gate 2 target. It is a proxy only, so pair it with
"why did you pick that tile?" interviews.

## Gate 2 questions → metrics

| Question | Summary line |
|---|---|
| Can players explain tile choices? | informed-choice % (plus interviews) |
| Do clues change decisions? | knowingly woke enemy, went for treasure when on offer |
| Do players understand damage? | damage by source, stepped into avoidable damage |
| Do Shield and Dash create decisions? | shield/dash use under telegraphed threat |
| Do enemy intents alter movement? | lowest-threat choice % when threats differ |
| Is optional treasure tempting? | chests opened vs optional rewards skipped |
| Do players knowingly accept danger for reward? | paid spikes on purpose, woke enemies near treasure |
| Can players identify mistakes after death? | deaths by cause + in-game WHAT HAPPENED log |
