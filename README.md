# ClickDungeon

A compact, turn-based 5×5 dungeon crawler. Read the dungeon, take calculated risks, survive the consequences.

**Current stage: Gate 1 — ugly mechanical prototype.** See `docs/checklist.md`.

## Layout

| Path | What |
|------|------|
| `docs/rules.md` | Gate 0 rules lock. The single source of gameplay truth. |
| `docs/decisions.md` | High-reversal-cost decisions (DECISION / WHY / DEPENDENCIES / REVERSIBILITY). |
| `docs/checklist.md` | Gate progress and Gate 2 watch list. |
| `ClickDungeon/` | Unity 6 project (6000.5.9f1). |
| `ClickDungeon/Assets/ClickDungeon/Scripts/Domain` | Runtime state, grid, enums. Pure C#. |
| `.../Content` | Content definitions and the default catalog (ids, stats, templates, floor profiles). |
| `.../Simulation` | Rules: turn resolver, visibility, AI, combat, hazards, chests, generation, validation. |
| `.../Application` | `GameSession`, JSON saves with atomic writes. |
| `.../Unity` | Presentation only: screens, board view, input, programmer art. |
| `.../Editor` | Scene/build setup (`ClickDungeon` menu). |
| `.../Tests` | NUnit rule tests; run in Unity *and* headless. |
| `Sim/ClickDungeon.Sim.Tests` | Headless dotnet test project compiling the same sources. |

Dependency direction: `Domain → Content → Simulation → Application → Unity`. Nothing below `Unity` references `UnityEngine`.

## Run the rules tests (no Unity needed)

```bash
dotnet test Sim/ClickDungeon.Sim.Tests/ClickDungeon.Sim.Tests.csproj
```

## Open and play

1. Open `ClickDungeon/` in Unity 6000.5.9f1.
2. Open `Assets/ClickDungeon/Scenes/Main.unity` (or run **ClickDungeon → Rebuild Main Scene**).
3. Press Play.

Build Windows: **ClickDungeon → Build Windows** → `ClickDungeon/Builds/Windows/ClickDungeon.exe`.

### Controls

- Tap/click a lit neighbouring tile: step, slash an adjacent enemy, or open an adjacent chest. Tap the hero to wait.
- SLASH / DASH buttons select a target mode; SHIELD / POTION act immediately.
- Keyboard: WASD/arrows, Space wait, 1–5 abilities, Esc menu/back, H help.

### Automation screenshot (dev)

```bash
ClickDungeon/Builds/Windows/ClickDungeon.exe -cdShot shot.png -cdScreen game -cdSeed 42 -cdTurns 6
```

Uses a separate save folder and quits after capturing.

## Playtest telemetry (Gate 2)

Runs write local JSONL logs (no network) to `%USERPROFILE%\AppData\LocalLow\Clickd\ClickDungeon\telemetry`.
Turn them off in **Settings → PLAYTEST LOG**. Schema and metrics: `docs/telemetry.md`.

```bash
dotnet run --project Sim/ClickDungeon.Telemetry.Report -- "%USERPROFILE%\AppData\LocalLow\Clickd\ClickDungeon\telemetry"
```

In Unity: **ClickDungeon → Telemetry → Summarize Logs**.

### Playtest kit

```bash
powershell -ExecutionPolicy Bypass -File tools/playtest-kit/make-kit.ps1
```

Zips the Windows build with a tester README and `collect-logs.bat` into `ClickDungeon/Builds/Playtest/`.
Session plan, interview questions and decision criteria: `docs/playtest-guide.md`.

## Art

Brief (reference images, specs, deliverables, decisions): `docs/art-brief.md`.
Drop PNGs into `ClickDungeon/Assets/ClickDungeon/Art/Runtime/` named by key (e.g. `tile_spikes.png`);
anything missing keeps its placeholder. **ClickDungeon → Art → Report Art Coverage** lists what's done.
