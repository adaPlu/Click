# LLMHandOff — ClickDungeon audit

**Audit date:** 2026-09-15 · **Worktree:** `D:\Click` (only worktree) · **Branch:** `main` at `5c8bbc5` **plus uncommitted
working-tree changes** (difficulty tiers, AutoPlayer, balance tests, popup stacking). Findings refer to files as they were on disk.

Evidence levels: **VERIFIED** (path traced by the raising auditor and independently re-checked by the lead, or reproduced) ·
**PLAUSIBLE** (traced by one auditor, not independently re-checked) · **DOWNGRADED** / **REJECTED** listed separately.
Severity is calibrated for a local, offline, single-player game with external playtesters: there is no server, network,
auth or multi-tenant surface, so those categories were out of scope by construction.

## Architecture (trust boundaries)
- Simulation (Domain/Content/Simulation): pure, deterministic rules; catalog per difficulty tier.
- Application: `GameSession` (only mutation entry), `FileSaveStore` (**boundary: user-editable JSON on disk**),
  `TelemetryRecorder`/`JsonlTelemetrySink` (**boundary: files leave the device via testers**), `AutoPlayer` (balance bot).
- Unity: `ClickDungeonApp` (**boundary: command-line automation flags in the shipped build**), screens.
- Tooling: `make-kit.ps1`, `collect-logs.bat` (**boundary: runs on tester PCs**), Unity build, art pipeline. No CI.

## Findings

| ID | Sev | Evidence | Title | Key files |
|---|---|---|---|---|
| REL-01 | Medium | VERIFIED | Save/delete exceptions escape `GameSession`; UI desyncs, defeat panel never shows, dead run resumable, Abandon can hard-lock | Application/GameSession.cs (Submit, Abandon), Application/SaveSystem.cs:63-83 (catch filter misses UnauthorizedAccessException), Unity/Screens/GameScreen.cs (Submit, ConfirmAbandon:440, OpenPause:423) |
| DATA-01 | Medium | VERIFIED | Save load validation is shallow: undefined enum ints (`"Difficulty": 7`), null collections, unknown `DefId`/`ClassId`, out-of-bounds positions pass `FromJson` → Continue throws with no backup fallback, or half-rendered board | Application/SaveSystem.cs:26-35, GameSession.cs TryContinue, Content/ContentCatalog.cs DifficultyInfo |
| REL-02 | Medium | VERIFIED (reproduced) | Collecting logs while the game runs fails: sink holds the file with `FileShare.Read`, `Compress-Archive` needs exclusive read → no zip at all; README never says close the game; failure text points to a path it did not print | tools/playtest-kit/collect-logs.bat:15-18, Application/Telemetry.cs:64, tools/playtest-kit/PLAYTEST-README.txt |
| CI-01 | Low | VERIFIED | No CI and no test gate before a kit is packaged | tools/playtest-kit/make-kit.ps1, (no .github) |
| CI-02 | Low | DOWNGRADED from Medium | Kit version label is taken at package time, not bound to the build; `--dirty` ignores untracked files; build folder not cleaned (stale files/exe can ship) | make-kit.ps1:11-30, Editor/ProjectSetup.cs:58-67 |
| DATA-02 | Low | VERIFIED | Version fields written but never enforced: Ruleset/Content/Generation unchecked on load, generation hashes current constant not the run's, older kits silently drop `Difficulty` | SaveSystem.cs:30, Simulation/FloorGenerator.cs:40 |
| DATA-03 | Low | VERIFIED | Floor-arrival (stairs) heal is indistinguishable from potion heal in telemetry; no player feedback on floor change | Simulation/TurnResolver.cs (TryCompleteFloor), Application/Telemetry.cs (healed) |
| DATA-04 | Low | PLAUSIBLE | `.bak` can resurrect a finished run (partial delete) or the previous run (NEW RUN over a save, then corruption) | SaveSystem.cs:61,75-81,101-105 |
| DATA-05 | Low | PLAUSIBLE | Telemetry summary keys runs by session+seed, so resumed runs double count; NEW RUN over a saved run emits no abandon | Application/TelemetrySummary.cs:73, GameSession.StartNewRun |
| DATA-06 | Low | PLAUSIBLE | Returned tester logs/summaries not gitignored; guide example writes inside the repo | .gitignore, docs/playtest-guide.md:63-69 |
| SEC-01 | Low | VERIFIED | Run seed (logged on every telemetry line) is `Ticks ^ TickCount<<32`, recoverable to machine uptime; 100 ns timestamps undisclosed vs "no personal information" | Unity/ClickDungeonApp.cs (StartNewRun seed), Telemetry.cs |
| REL-03 | Low | VERIFIED (also seen in AutoPlayer traces) | Killing Blobert while standing on the exit: exit reads open but does not complete; must step off and on | Simulation/TurnResolver.cs:84, Combat.cs:57-66, GameScreen.cs UnderfootText |
| REL-04 | Low | VERIFIED | Move tween is never cancelled; a non-animated render during it (fast floor change) is overwritten, hero drawn on old tile | Unity/Ui/Tween.cs:10-26, Unity/Screens/BoardView.cs:399-400 |
| REL-05 | Low | VERIFIED | FloorBanner coroutine (hosted on the app) touches destroyed objects after leaving the game screen → MissingReferenceException | Unity/Screens/FloorBanner.cs Run(), GameScreen.cs:96 |
| REL-06 | Low | VERIFIED | Title "START A NEW RUN?" modal stays open after CONTINUE INSTEAD and reappears stale on return to title | Unity/Screens/TitleScreen.cs Play(), ClickDungeonApp.ContinueRun/ShowTitle |
| REL-07 | Low | VERIFIED | Automation: `Enum.TryParse` accepts `-cdDifficulty 7`; any exception in the automation coroutine hangs the process (no quit) | Unity/ClickDungeonApp.cs ParseDifficulty, AutomationShot |
| REL-08 | Low | PLAUSIBLE | TelemetrySummary aborts on valid JSON with unexpected types (only JsonException caught) | Application/TelemetrySummary.cs:59-74,147,169,199 |
| REL-09 | Info | VERIFIED | Crash/Player.log never collected from testers | collect-logs.bat:15 |
| PERF-01 | Low | VERIFIED | Every hover enter/exit rebuilds all 25 cells' children | GameScreen.cs:81-90, BoardView.Render |
| MAINT-01 | Low | PLAUSIBLE | Telemetry failures invisible (`Failures` unused); sink `Dispose` unguarded can leave `TelemetryActive` wrong | Telemetry.cs, ClickDungeonApp.cs ApplyTelemetrySetting/OnDestroy |
| MAINT-02 | Low | VERIFIED | Company/product log folder hardcoded in 4 places; `ApplyPlayerSettings` not run by `BuildWindows` | collect-logs.bat:4, ProjectSetup.cs:40-41, README |
| MAINT-03 | Low | VERIFIED | `collect-logs.bat` stored/shipped with LF endings; no `.gitattributes` | collect-logs.bat |
| MAINT-04 | Info | VERIFIED | Toolchain unpinned (Pillow, .NET SDK); headless net10.0/NUnit 3.14 differs from Unity .NET Std 2.1 | Sim/ClickDungeon.Sim.Tests.csproj, tools/art-slicer |
| MAINT-05 | Info | PLAUSIBLE | Art catalog key collisions only warn; numbered frames override single production file | Editor/ArtCatalogBuilder.cs:43-54,161-166 |
| MAINT-06 | Info | VERIFIED | `ForDifficulty` drops `CreateTuned` numbers; enemy clamp comment wrong when base min is 0 | Content/ContentCatalog.cs |
| MAINT-07 | Low | VERIFIED | New popup stacking still overlaps on the bottom row (clamp) and groups per frame, not per turn | Unity/Screens/BoardView.cs Popup |
| MAINT-08 | Info | VERIFIED | Boss is exempt from stagger on block; rules.md doesn't say so | Simulation/EnemyAi.cs:117, docs/rules.md §5 |
| SEC-02 | Info | VERIFIED | Automation flags ship in tester builds (local-user only, no exploitable path); Unity hardware stats vs privacy wording | Editor/ProjectSetup.cs:63, ProjectSettings submitAnalytics |
| TEST-01 | Low | VERIFIED | Missing tests: tampered saves, store IO failures, screen flows, boss-death-on-exit, per-tier generation fuzz at scale, AutoPlayer copy parity beyond one seed; `ArrivingOnTheNextFloorHealsByTheTiersBreather` runs Easy rules on a Medium-created hero; checklist claims stale (89 tests) | Tests/DifficultyTests.cs, Tests/BalanceTests.cs, docs/checklist.md:27-30 |

### Cross-component notes
- **REL-01 + DATA-04:** death turn with a failing delete leaves the pre-death InProgress save → CONTINUE resurrects a lost run.
- **DATA-01 + DATA-02 + CI-02:** testers keep saves in one `persistentDataPath` across kit updates; renamed content ids and
  unenforced versions are the realistic trigger for DATA-01.
- **REL-02 + DATA-05 + SEC-01 + CI-02:** Gate 2 telemetry (which difficulty tuning depends on) can be lost, double counted and not
  attributable to a build.

### Rejected / downgraded
- CI-02 kit mislabel: Medium → Low. Both existing kits were built 3-4 min before their commit from the same content; no wrong content shipped.
- "Partial zip left behind" (REL-02 detail): false. Reproduction shows no zip is created; severity unchanged.
- Hardcore extra enemies make `FloorGenerator.Generate` throw: rejected (placement only stops early; validator ignores enemies).
- `AutoPlayer.Copy` missing fields: rejected (field parity checked for all state types).
- JSON type injection: rejected (`TypeNameHandling` None).
- Invalid `UserPrefs.LastDifficulty` crashes the picker: rejected (picker iterates a fixed tier list).
- Stale difficulty catalog on screens: rejected (all reads go through `Session.Catalog`).

## Remediation graph
```
G1 Application save boundary (serialize: SaveSystem.cs, GameSession.cs)
   DATA-01 → REL-01 → DATA-04 → DATA-02
G1 → G2 Unity screens (serialize: GameScreen.cs, BoardView.cs, TitleScreen.cs, FloorBanner.cs)
   REL-01 UI handling → REL-06, REL-05, REL-04, MAINT-07, PERF-01
G3 Telemetry pipeline (serialize: Telemetry.cs, TelemetrySummary.cs, collect-logs.bat, README) — parallel with G1
   REL-02 → MAINT-01, DATA-03, DATA-05, REL-08, REL-09
G4 Tooling / CI (independent) — CI-01, CI-02, MAINT-02, MAINT-03, DATA-06, MAINT-04, MAINT-05
ClickDungeonApp.cs shared by SEC-01, REL-07, MAINT-01 → serialize those edits
G5 Simulation (independent) — REL-03 (rule decision needed), MAINT-06, MAINT-08
TEST-01 items land with the group they cover.
```

## Coverage and limits
- Reviewed: all gameplay/application/Unity scripts, editor build/art scripts, playtest kit scripts, package manifests, csproj, .gitignore.
- The dedicated test-quality auditor was cut off by a usage limit; test gaps come from the other four auditors plus lead spot checks
  (balance guard runtime 2 s in Unity, PlayerPrefs restored, Art overrides reset).
- Not reviewed: PlayMode behaviour on real devices/phones, Unity package internals, the art slicer's image math in depth,
  real tester machines (antivirus locks, redirected profiles). IO-failure triggers for REL-01/DATA-04 are plausible, not observed.

## Remediation — 2026-09-15 (graphRepair)

Implemented serially in the main tree: the uncommitted difficulty work shared most files, so worktree isolation would have
branched from stale code. Results: headless 135/135, Unity EditMode 193 passed / 0 failed / 3 explicit skipped.
Load-bearing check: each Application/Content fix reverted in a scratch copy → its tests went red (16 of 26 targeted).

Evidence: **VERIFIED** tests pass and go red when the fix is reverted · **TESTED** tests pass, revert not demonstrated ·
**COMPILED** builds, no test reaches the changed path · **WRITTEN** not yet executed.

| ID | Status | Evidence | Change |
|---|---|---|---|
| REL-01 | FIXED | VERIFIED | `GameSession` guards store calls (`SaveError`), writes finished runs before deleting, `Abandon` never throws; `GameScreen` warns once (warning COMPILED) |
| DATA-01 | FIXED | VERIFIED | `SaveSerializer` validation (enums by name only, lists, bounds, terrain), JSON errors → FormatException; `TryContinue` refuses unknown content with a message |
| REL-02 | FIXED | VERIFIED | `collect-logs.ps1` copies with shared access, then zips; `.bat` calls it; README says close the game |
| DATA-02 | FIXED | VERIFIED (older-ruleset upgrade: TESTED) | newer ruleset / other generation rejected; older ruleset upgraded on load |
| DATA-03 | FIXED | VERIFIED (UI popup/log text: COMPILED) | `healed.source` potion/stairs; stairs popup and log line |
| SEC-01 | FIXED | VERIFIED timestamps; seed COMPILED (smoke test only) | `LaunchOptions.NewRunSeed` (crypto RNG); telemetry time to whole seconds; README discloses |
| REL-07 | FIXED | VERIFIED parsing; quit-on-exception COMPILED | `LaunchOptions.ParseDifficulty` names only; automation quits on logged exception |
| REL-09 | FIXED | VERIFIED | Player logs collected with profile path redacted |
| MAINT-06 | FIXED | VERIFIED ForDifficulty; clamp WRITTEN-level (no default profile has min 0) | tier table copied; max enemies ≥ 1 |
| MAINT-02 | FIXED | TESTED | `BuildWindows` applies player settings; kit path test |
| MAINT-03 | FIXED | TESTED | `.gitattributes` CRLF for .bat; line-ending test |
| REL-04 | FIXED | TESTED | `Tween.MoveTo` yields to newer placement |
| MAINT-07 | FIXED | TESTED | popups stack up when no room below; `BeginPopupBatch` per turn |
| PERF-01 | FIXED | TESTED (GameScreen wiring COMPILED) | hover uses `BoardView.RenderHighlights` |
| REL-03 | FIXED (UI truth; rule unchanged) | TESTED behaviour, text COMPILED | underfoot text + rules §7 |
| REL-05 | FIXED | COMPILED | FloorBanner stops after its root is destroyed |
| REL-06 | FIXED | COMPILED | title `Refresh` closes a leftover modal |
| CI-02 | FIXED | COMPILED (ProjectSetup) / WRITTEN (make-kit) | build cleans output and writes BUILD-VERSION.txt; kit records both versions |
| CI-01 | PARTIALLY_FIXED | WRITTEN | make-kit runs headless tests first; no hosted CI |
| MAINT-04 | PARTIALLY_FIXED | — | `global.json`, slicer `requirements.txt`; net10 vs Unity .NET Std divergence remains |
| MAINT-08 | FIXED | docs | rules §5: Blobert is never staggered |
| TEST-01 | PARTIALLY_FIXED | — | new save/session/telemetry/catalog/kit/board tests; screen-flow tests still missing |
| SEC-02 | NOT_FIXED | accepted | no exploitable path |

Not in scope (PLAUSIBLE, unverified): DATA-04 (partly mitigated by REL-01's finished-save order), DATA-05, DATA-06, REL-08, MAINT-01, MAINT-05.

**Independent review** (fresh read-only reviewer tasked to disprove; verdicts re-checked by the lead before acting):
APPROVED: DATA-01, DATA-02, REL-02/REL-09, CI-01/CI-02, DATA-03, SEC-01, REL-07, REL-03, REL-05, REL-06, PERF-01, MAINT-07,
MAINT-02/03/04/08. REJECTED: REL-04 (its fix caused REL-10). NEEDS_CHANGES: REL-01 (REL-11), MAINT-06 (inaccurate comments).

### Repair loop (after commit 161ecd8, uncommitted)

| ID | Status | Evidence | Change |
|---|---|---|---|
| REL-10 (new, Low) | FIXED | TESTED (tween); BoardView wiring COMPILED | Regression from the first REL-04 fix: a newer move on the same token was cancelled by the older one, leaving tokens on stale tiles. Now each placement bumps `Token.MoveVersion`; `Tween.MoveTo` takes an `isCurrent` check and stops without writing. Replaces the REL-04 fix; REL-04 now FIXED via REL-10. |
| REL-11 (new, Low; remainder of REL-01/DATA-04) | FIXED | VERIFIED (reverted order → test red: "Expected Lost, was InProgress") | `FileSaveStore.Delete` removes backup, temp, then main, so a delete failing part-way leaves the finished save, not the pre-death backup. |
| REL-12 (new, Low) | FIXED | COMPILED | Save warning no longer shows the raw error (paths with the user name) and is not shown for finished runs; raw error goes to Player.log (redacted by collect-logs). |
| MAINT-06 | FIXED | docs | Comments now state the clamp and `ForDifficulty` limits accurately. |
| TEST-01 | PARTIALLY_FIXED | TESTED | `EveryStateTheGameProducesPassesValidation` round-trips every AutoPlayer state incl. win, loss and Blobert's floor; `DeleteThatFailsPartWayNeverLeavesAResumableRun`; tween tests cover two moves on one token. |

Results after repairs: headless 137/137; Unity EditMode 196 passed / 0 failed / 3 explicit skipped.
Known non-load-bearing tests: `RunSeedsAreRandom` (smoke), `FloorsThatHadEnemiesKeepAtLeastOne` (no min-0 profile),
`BossExitTests` (pins an unchanged rule), `HoverRedrawsHighlightsWithoutRebuildingTiles` (BoardView only, not the GameScreen hover wiring).
Reviewer residuals, not fixed: a save refused by `ContentProblem` is kept, so CONTINUE repeats the message (by design, the player is told to start a new run);
collect-logs fails cleanly (exit 1) if the temp path contains `[` or `]`; 8.3 short profile paths are not redacted.

### Next order
1. Commit the repair loop (REL-10, REL-11, REL-12, MAINT-06 comments, new tests).
2. Run make-kit once to execute CI-01/CI-02 (WRITTEN → verified).
3. Verify the PLAUSIBLE findings; screen-flow tests (TEST-01).
4. Resume Knight's Trial / Blobert's Wrath tuning (`DifficultySweep`).
