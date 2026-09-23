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

---

# Audit 2 — 2026-09-19

**Worktree:** `D:\Click` (only worktree) · **Branch:** `main` at `cac106e`, clean tree · **Scope:** whole repository.
65 commits and ~53k added lines since audit 1 (mana D-032, shop D-036, class talents and hero select D-037, difficulty
retunes D-038/D-039, renown and rarer gear D-040/D-041, portrait plus Android/iOS builds D-042, first CI workflow).

**Method:** five read-only component auditors in parallel (simulation · application/persistence · Unity runtime · build/CI ·
tests), then independent verifiers tasked to *disprove* every High and Medium, then lead reconciliation. The
cross-component agent died on a session limit; the lead did that pass instead, so its findings are marked LEAD.

Evidence levels: **VERIFIED** (raised by one auditor and independently re-traced by a second, or checked by the lead) ·
**CONFIRMED** (traced end to end by one auditor, not independently re-checked) · **PLAUSIBLE** (reasoned, not fully traced)
· **NEEDS_EVIDENCE** (cannot be settled by reading code).

Severity is calibrated for an offline single-player game: losing the player's profile is the worst realistic outcome,
free resources in a single-player game are at most Low, and there is no network, auth or multi-tenant surface.

## Findings

| ID | Sev | Evidence | Title | Key files |
|---|---|---|---|---|
| DATA-07 | **High** | VERIFIED (2 agents) | Profile write is delete-then-move: no verify, no `.bak`, and `profile.json.tmp` is never read back. `File.Move` throwing (AV/indexer/cloud-sync lock) needs no timing window at all | Application/ProfileSystem.cs:139-145 vs SaveSystem.cs:135-155 |
| DATA-08 | **High** | VERIFIED (2 agents) | Any profile load failure (corrupt, locked, foreign schema) returns an empty profile, and `GameSession`'s constructor posts the welcome letter and **saves over the damaged file within a frame of launch**. No message, no log line, no backup. Chained to DATA-07 this turns recoverable into permanent | Application/ProfileSystem.cs:107-137 (:113 schema equality, :133 catch-all), GameSession.cs:28-31 |
| SEC-03 | Medium | VERIFIED (docs-sourced) | The iOS workflow hands Unity **account** credentials to `game-ci/unity-builder@v4`, a mutable tag; a moved tag reads them from the step env. (Verifier disproved the proposed fix: game-ci's Personal path genuinely needs all three secrets — the remedy is SHA pinning + a dedicated Unity account, not removing them) | .github/workflows/ios-ipa.yml:33-38 |
| X-1 | Medium | LEAD (parts VERIFIED) | **Chain:** debug-key signing + no profile backup + manual-only log collection. Today's APKs update fine (the debug key is machine-local, not public), but the first release-signed build cannot install over them; the tester uninstalls, and Android wipes app-specific storage — save, profile and every uncollected playtest log at once | ProjectSetup.cs:136-163, ProjectSettings.asset:276,289, ProfileSystem.cs:139-145, ClickDungeonApp.cs:43,47,284 |
| X-2 | Medium | LEAD | **Chain:** the difficulty curve players actually meet is neither guarded nor observable. Guards play an empty profile (no renown, gear or talents) and only assert "not too hard"; the growing-profile run that D-040/D-041 were tuned on is `[Explicit]`; telemetry carries no economy events and `run_started` omits Threat/level | Tests/BalanceTests.cs:95-124, Tests/PlaythroughTests.cs:17, Application/Telemetry.cs:125-141 |
| TEST-02 | Medium | CONFIRMED | Portrait and rotation (D-042) have **zero** tests; no test ever constructs `ClickDungeonApp`, so `Relayout`, screen transitions and the automation loop are untested. `RefLayout.Top` fails silently when a name misses, and `RefLayout.Portrait` is a mutable static with no reset | Unity/Screens/GameScreen.cs:159-178, Unity/Ui/RefLayout.cs:54-65, Scripts/UnityTests/* |
| REL-13 | Low | VERIFIED (raised Medium, downgraded) | Rotating after a run ends destroys the victory/defeat panel and `Reopen()` never re-runs `CheckRunEnd`, so the result screen, NEW RUN and WHAT HAPPENED are unreachable. **Not a lock:** ☰ MENU, Escape/Back, settings and the nav bar all bypass `Blocked`, so QUIT TO TITLE is two taps away; the run is already banked | Unity/ClickDungeonApp.cs:222,233-251, GameScreen.cs:181,519,1223 |
| REL-14 | Low | CONFIRMED | Same root cause: the run-end check is deferred into `ChestOverlay._onClosed`, so a run that ends on a chest turn shows no end panel if the overlay is destroyed rather than tapped closed (rotation, or watch mode starting the next run). Rewards are committed in the simulation first, so nothing is lost or double-granted | GameScreen.cs:477-486, Overlays.cs:248-288 |
| REL-15 | Low | CONFIRMED | The WHAT HAPPENED log lives only in `GameScreen` and is wiped by a rotation; the defeat panel then points at an empty log | GameScreen.cs:46,103,675 |
| REL-16 | Low | CONFIRMED | `ChestOverlay` coroutines call `IsOpen` on a destroyed root → `MissingReferenceException` (which auto-quits an automation build) | Overlays.cs:240,270,351,354 |
| REL-17 | Low | CONFIRMED | First-run HOW TO PLAY is consumed by `SeenHelp` before it is shown; a rotation loses it permanently | GameScreen.cs:207-211 |
| REL-18 | Low | CONFIRMED | In portrait, `OverlayStage` re-parents menus so `SetAsLastSibling` no longer orders against the floor banner: the banner can draw over a modal | Unity/Ui/RefLayout.cs:44-51, FloorBanner.cs:63 |
| REL-19 | Low | VERIFIED (lead re-traced) | **Vault chests never get a quality.** `EnterVault` goes through `ArriveOnFloor`, not `SetupFloor`, which is the only place `CellState.Quality` is assigned — so the vault's "always Epic" great chest opens in 2 taps instead of 4 and ordinary vault chests are always Common, contradicting rules §13 | Simulation/RunFactory.cs:53-63,161,177 vs docs/rules.md:305 |
| REL-20 | Low | VERIFIED (lead re-traced) | Renown is half-applied in vaults: `Renown.Reaches` keys on the inherited floor index so guards **hit harder**, but `ApplyThreat` runs only in `SetupFloor`, so they never get their extra **hearts** | Simulation/Renown.cs:12-13, RunFactory.cs:150,183 |
| REL-21 | Low | CONFIRMED | The summon telegraph can mark a different tile than the one filled: `Threats.Compute` and `EnemyAi.Execute` call `SummonCells` at different hero positions, and a body-blocked declared tile still shows a marker for a summon that is cancelled | Simulation/Threats.cs:46-50, EnemyAi.cs:112-123 |
| DATA-09 | Low | CONFIRMED | A ruleset-7 save resumes under ruleset 8 with `Threat = 0`: the rest of that run meets the 18-HP double-slamming Blobert with no renown at all | Application/SaveSystem.cs:48-55, GameSession.cs:84 |
| DATA-10 | Low | CONFIRMED | Blobert's gear drop is hashed on the **hero's** tile, so both the item and the 50% roll depend on where the player stood for the killing blow; `Treasure.Item`'s "same run always finds the same item" comment is false for the boss drop | Simulation/Combat.cs:77, Treasure.cs:25-38 |
| DATA-11 | Low | VERIFIED | `Abandon` banks and then deletes without writing a finished save; a failing delete leaves a resumable, already-banked run → coins/XP/counters banked twice. The death path deliberately writes first; abandon skips that step | Application/GameSession.cs:150-160 vs :166-177 |
| DATA-12 | Low | CONFIRMED | NEW RUN from the title over an in-progress save never banks that run (the pause menu's ABANDON does) and emits no `run_abandoned` | Unity/Screens/TitleScreen.cs:233-243, GameSession.cs:74-90 |
| DATA-13 | Low | CONFIRMED | `StartNewRun` writes the profile with provisions already consumed before the run save exists; an interrupted start eats heart tokens, tonics, scrolls and a special key | Application/GameSession.cs:80-87, ProfileSystem.cs:37-69 |
| DATA-14 | Low | VERIFIED (downgraded from Medium) | Save validation was never extended for `ItemsFound`; a hand-edited `"ItemsFound": null` throws inside `TurnResolver.Apply` on a drop, so the turn never persists and that save becomes permanently unwinnable. Only a deliberate hand-edit reaches it (an absent key keeps the initializer); no crash, no profile loss | Application/SaveSystem.cs:66, Simulation/Treasure.cs:36 |
| SEC-04 | Low | CONFIRMED | Hand-edited profile: duplicate `Equipped` values apply one item's stats twice; a non-slot key is un-removable from the UI yet still applies; negative mail coins drive the purse negative until the next load clamps it | Application/Inventory.cs:80-93, Mailbox.cs:63-67 |
| REL-22 | Low | VERIFIED (downgraded from Medium) | Profile write failures are never surfaced: `SaveError` is read in exactly one place, `WarnIfSaveFailed` returns early for a finished run, `Abandon` nulls it, and the title screen (shop, mail, talents, daily) never reads it. It does reach `Player.log` | Unity/Screens/GameScreen.cs:683-695, GameSession.cs:158 |
| CI-03 | Low | VERIFIED (lead, via API) | The workflow declares no `permissions:`. The repo's default is already **read** (`gh api .../actions/permissions/workflow` → `"read"`), so the exposure is small — but `contents: read` should be explicit, and it caps SEC-03's blast radius | .github/workflows/ios-ipa.yml |
| CI-04 | Low | CONFIRMED | The only CI workflow produces a distributable `.ipa` and runs no tests (the kit script does gate on tests; CI does not) | .github/workflows/ios-ipa.yml |
| CI-05 | Low | NEEDS_EVIDENCE | The workflow has never run. `BuildIos` writes to the **relative** `Builds/iOS`; under game-ci the tar step may not find it. The `unityci/editor:ubuntu-6000.5.9f1-ios-3` image does exist (verifier checked Docker Hub), and `-customBuildPath` being ignored is by design for a custom build method. Failure would be loud, not silent | .github/workflows/ios-ipa.yml:48, Editor/ProjectSetup.cs:165,180-190 |
| CI-06 | Low | VERIFIED (downgraded from Medium) | Android APK is debug-signed. The key is the machine-local `~/.android/debug.keystore`, **not** a public Unity key, so successive playtest APKs install fine; the cost is one unavoidable uninstall (and wipe) when a release key arrives — see X-1 | Editor/ProjectSetup.cs:136-163 |
| MAINT-09 | Low | CONFIRMED | No telemetry for the profile economy (shop, exchange, chests, daily, mail, achievements) and `run_started` omits Threat/level, so D-025…D-041 cannot be read back from playtest logs | Application/Telemetry.cs:125-141 |
| MAINT-10 | Low | CONFIRMED | App version never bumped (`bundleVersion 1.0`, `AndroidBundleVersionCode 1`) and `BUILD-VERSION.txt` sits *beside* the APK, not in it; there is no in-game version label, so a tester's bug report cannot be tied to a build | ProjectSettings.asset:151,180, Editor/ProjectSetup.cs:160 |
| MAINT-11 | Low | CONFIRMED | INSPECT is hover-only: on touch it is readable only while a finger is held down, and lifting spends a turn | Unity/Screens/BoardView.cs:14-24, GameScreen.cs:120-129 |
| MAINT-12 | Low | LEAD | The portrait board rectangle is a contract between a Python art script and hard-coded C# constants, checked by nothing on either side (and portrait has no tests at all — TEST-02) | tools/art-slicer/make_portrait_backgrounds.py:23-26, Unity/Screens/GameScreen.cs:149-152 |
| MAINT-13 | Info | CONFIRMED | `FileProfileStore`'s summary claims "temp → verify → replace, like the run save"; it does none of the three | Application/ProfileSystem.cs:87 |
| MAINT-14 | Info | CONFIRMED | `RefLayout.Top` fails silently on a missing name; the title screen's two children named `"Settings"` are disambiguated only by construction order | Unity/Ui/RefLayout.cs:54-65, TitleScreen.cs:120,138 |
| TEST-03 | Low | CONFIRMED | `GuardBaselineTests` contains no guards — all six tests are `[Explicit]`, so a green run implies protection that does not exist | Tests/GuardBaselineTests.cs:16,41,71,104,134,160 |
| TEST-04 | Low | CONFIRMED | Renown trap coverage is spikes-only; lava, bombs, the "pits are untouched" clause and the raised telegraph numbers are untested. Profile→`Run.Threat` wiring is untested (every case sets `Threat` by hand); 21 of 24 talents never traverse `Progression.Apply` | Tests/RenownTests.cs:53-69, ClassTalentTests.cs:95-108 |
| TEST-05 | Info | CONFIRMED | `docs/checklist.md` claims 137 headless / 199 Unity; actual 255 / 342. `docs/rules.md` §10.2 table predates D-039/D-040/D-041 | docs/checklist.md:30, docs/rules.md:425-433 |

## Prior findings re-checked

**Fixed and still fixed:** REL-01 (guarded store calls), REL-02, REL-04 (via REL-10), REL-05, REL-06, REL-07 (residual:
exceptions thrown in `Awake` still hang an automation run — PLAUSIBLE), REL-09, REL-11, REL-12, DATA-01 (save validation
is thorough — but never grew, see DATA-14), DATA-02 (ruleset/generation enforced), DATA-03, SEC-01 (crypto seed,
truncated timestamps), PERF-01, MAINT-02 (second half), MAINT-03, MAINT-06, MAINT-07, MAINT-08, CI-02.

**Still open:** DATA-04 (the `.bak` half: `File.Replace` keeps the *previous* run as backup and `Exists` is true for a
bak-only state), DATA-05 (telemetry keys runs by session+seed, so a resumed run double-counts), DATA-06 (tester telemetry
still not gitignored and the guide's own example writes it into the PUBLIC repo — `docs/playtest-guide.md:63-70`),
MAINT-01 (telemetry `Failures` still unread; unguarded `Dispose`), MAINT-04 (cv2/numpy unpinned; net10 vs Unity .NET
Standard divergence), MAINT-05, SEC-02 (`submitAnalytics: 1` now also ships on phones), TEST-01 (screen-flow tests still
missing, against a much larger surface). CI-01 is partially regressed: hosted CI now exists and gates nothing (CI-04).

**Regressed in spirit:** the profile got none of the protections the save boundary earned in audit 1 (DATA-07/DATA-08).

## Rejected / downgraded this round

- **`Shop.TryBuy` NRE after payment — FALSE_POSITIVE.** `PickItem` cannot return null with the shipped catalog
  (7/10/9/6/6 items by rarity; `DropWeight` is strictly positive), and `Shop.cs:154` refuses an empty catalog before
  deducting gems. Latent content-shape invariant only.
- **"UNITY_EMAIL/UNITY_PASSWORD are unnecessary" — FALSE_POSITIVE.** game-ci converts the `.ulf` to a serial and
  activates with username+password; there is no licence-file-only branch. Removing them would break the workflow.
- **"No `unityci/editor` image for 6000.5.9f1+iOS" — FALSE_POSITIVE.** `ubuntu-6000.5.9f1-ios-3.2.2` is published.
- **"Unity's *public* debug keystore" — wrong.** The debug key is machine-local; successive APKs install over each other.
- **"Android refuses an equal versionCode" — wrong.** Only downgrades are refused.
- **Rotation dead-end = "force-quit required" — downgraded.** ☰ MENU, Escape/Back, settings and the nav bar all bypass
  `Blocked`; QUIT TO TITLE is reachable.
- **`"ItemsFound": null` = "crashes every boss kill" — downgraded.** Drop chance is 50%, an absent key keeps the
  initializer, older saves are rejected by the generation gate, and Unity logs the exception rather than crashing.
- **`SureDrops` test helper leaking mutated catalog state — not live.** `using` → try/finally, no parallelism configured
  anywhere. Latent only if `[assembly: Parallelizable]` is ever added.
- **Profile writes lost to a phone background-kill — narrower than raised.** There is no `OnApplicationPause` handler,
  so nothing writes while backgrounding; per-turn `Persist()` makes the *run* safe. DATA-07's realistic trigger is
  `File.Move` throwing, not the OS kill.

## Cross-component notes (lead)

- **X-1** and **X-2** above are the two chains that only exist between layers.
- **REL-13 + REL-14** are one root cause: run-end state is owned by a screen that can be destroyed, and the chest path
  defers the check into a UI callback. One fix closes both, plus the watch-mode variant.
- **Checked, no interaction:** automation/watch mode is fully isolated (separate save folder, in-memory profile, no
  `PlayerPrefs` write, no in-game trigger), and the bot's win/loss stats read `Session.Run` directly, so REL-14 does not
  falsify them. Rewards are committed in the simulation before any overlay shows, so the overlay defects cannot lose or
  duplicate loot. Per-turn `Persist()` genuinely makes a mobile background-kill safe for the run.

## Coverage and limits

- **Reviewed:** all of `Domain/Content/Simulation/Application/Unity/Editor`, both test suites, the playtest kit scripts,
  the art-slicer and icon/portrait Python tools, `.github/workflows/ios-ipa.yml`, project settings, `.gitignore`, csproj
  and `global.json`. Public-repo secret scan: clean (no keys, keystores, binaries or tester data committed; 37.7 MiB).
- **Not reviewed:** `Sim/ClickDungeon.Telemetry.Report` (no auditor covered it), the art slicer's image math in depth,
  Unity package internals.
- **Could not verify:** anything requiring a device or a real run — portrait was checked by arithmetic and desktop
  screenshots, never on a phone; touch behaviour is inferred from code; the iOS Xcode project has never been compiled;
  the `.ipa` workflow has never run (CI-05); IO-failure triggers (DATA-07, DATA-11, REL-22) are traced, not reproduced.
- **Audit error rate, stated plainly:** verifiers rejected one finding outright and downgraded five of the eight
  High/Medium claims they examined. Treat single-agent CONFIRMED items as weaker than the VERIFIED ones.

## Remediation graph

```
G1 Profile boundary (serialize: Application/ProfileSystem.cs, GameSession.cs)   << do first
   DATA-07 (atomic write + .bak) -> DATA-08 (never overwrite an unreadable profile; tell the player)
                                 -> REL-22 (report profile failures) -> MAINT-13 (fix the doc comment)
   then DATA-11, DATA-12, DATA-13 (bank/delete ordering) — same files, serialize with the above
G2 Run-end ownership (serialize: Unity/Screens/GameScreen.cs, Overlays.cs) — parallel with G1
   REL-14 (chest callback on teardown) -> REL-13 (Reopen re-runs CheckRunEnd) -> REL-15, REL-16, REL-17
   REL-18 (RefLayout.OverlayStage ordering) + MAINT-14 — RefLayout-local, independent
G3 Simulation (independent, one file each)
   REL-19 (vault chest quality) and REL-20 (vault renown hearts) share RunFactory.cs -> serialize
   REL-21 (summon telegraph), DATA-09 (Threat on resumed saves), DATA-10 (boss drop hash) independent
G4 Build/CI (independent)
   SEC-03 (pin actions to SHA; dedicated Unity account) -> CI-03 (permissions: contents: read) -> CI-04 (test gate)
   CI-05 needs one real run to settle; X-1 blocks on deciding the keystore BEFORE the next APK goes out
   MAINT-10 (version from git) with it
G5 Tests (land with the group they cover)
   TEST-02 (portrait + Relayout harness) is the prerequisite for trusting any G2 fix
   X-2: make the ten-run playthrough a non-explicit band + add Threat/level to run_started (MAINT-09)
   TEST-03, TEST-04, TEST-05, DATA-14 (extend the validator) independent
```

## Next order

1. **G1 first** — DATA-07/DATA-08 are the only findings that can permanently destroy a player's data.
2. **Decide the Android keystore before the next APK reaches a tester** (X-1); generate it outside the repo.
3. G2 with TEST-02's harness, so the rotation fixes are provable.
4. SEC-03/CI-03 in the same pass as the first real run of the workflow (CI-05).
5. G3 and the rules-doc corrections (REL-19 contradicts rules §13; TEST-05 counts are stale).

### Repair 2026-09-19 (D-043)

| ID | Status | Evidence | Change |
|---|---|---|---|
| DATA-07 | FIXED | VERIFIED (revert → 2 tests red) | `FileProfileStore.Save`: temp → read back → `File.Replace` keeping `profile.json.bak`, with a copy/move fallback. The live file is never deleted before its replacement exists. |
| DATA-08 | FIXED | VERIFIED (revert → test red) | `Load` falls back to the backup; an unreadable profile is kept as `profile.json.broken` and never overwritten; if it cannot be set aside, `Save` refuses. A newer schema is kept, an older one loads and is re-stamped. |
| REL-22 | FIXED (in part) | TESTED | `IProfileStore.LoadNotice` → `GameSession.ProfileNotice` → the title screen's flash line, shown once; a failed `SaveProfile` sets it too. Still not surfaced in-game mid-run. |
| MAINT-13 | FIXED | — | The doc comment now describes what the store actually does. |
| REL-19 | FIXED | VERIFIED (revert → test red) | `RunFactory.AssignChestQuality` is called from `EnterVault` as well as `SetupFloor`, so a vault's great chest is Epic (4 taps) and its ordinary chests are rolled. |

Results: headless 260/260, Unity EditMode 335 passed / 0 failed / 12 explicit skipped.
Not touched this round: REL-20 (vault renown hearts — same function, deliberately left for a separate decision), REL-13/REL-14,
DATA-09..DATA-14, SEC-03/SEC-04, CI-03..CI-06, TEST-02..TEST-05.

### Repair 2026-09-19b (D-044)

| ID | Status | Evidence | Change |
|---|---|---|---|
| REL-13 | FIXED | TESTED by screenshot (`-cdRelayout 1`: the VICTORY panel survives the rebuild) | `GameScreen.Reopen` re-runs `CheckRunEnd`. |
| REL-14 | FIXED | VERIFIED (revert → test red) | `ChestOverlay.CloseNow` runs the pending callback once; `GameScreen.PrepareForRebuild` calls it before the screen is destroyed. |
| REL-15 | FIXED | COMPILED (no harness constructs a GameScreen) | `GameScreen.AdoptFrom` carries the run log and last damage source to the rebuilt screen. |

Results: Unity EditMode 337 passed / 0 failed / 12 skipped (349 total); headless unchanged at 260.
Still open: TEST-02 (nothing constructs `ClickDungeonApp`, so rotation is proved only by screenshot), REL-16 (chest
coroutines on a destroyed root), REL-17, REL-18, REL-20, DATA-09..DATA-14, SEC-03/SEC-04, CI-03..CI-06.

### Repair 2026-09-19c (D-045, D-046)

| ID | Status | Evidence | Change |
|---|---|---|---|
| REL-20 | FIXED | VERIFIED (revert → test red) | `EnterVault` calls `ApplyThreat`, so vault guards get the renown hearts that match the renown damage they already dealt. |
| — | — | — | D-045 (cosmetic): the sparkle stars for key, potion and spike pickups no longer play; bomb blast, exit unlock and enemy wake still do. `BoardFxTests.EventsMapToBoardEffects` pins it. |

Results: headless 261/261, Unity EditMode 338 passed / 0 failed / 12 skipped (350 total).

### Finding 2026-09-20 — MAINT-15 (the balance bot can loop between teleport pads)

**MAINT-15 | Low (instrument quality, not player-facing) | CONFIRMED (reproduced exactly)**
`AutoPlayer` has no memory of a move that got it nowhere, and its look-ahead does not model teleports. Watch-mode run 5
(seed 20300515, Knight, Medium) was replayed headlessly turn by turn and reproduced to the turn: lost on floor 4 after
587 turns. The floor held a teleport pair at (4,1) and (4,4); the hero stood on (4,4), chose `Move(4,1)`, was teleported
straight back to (4,4), and repeated that for roughly 200 turns before dying. The key was three steps away on clear
floor — the dungeon was winnable throughout. The same run shows a second loop on floor 2 (88 turns standing on one tile).
Files: `Application/AutoPlayer.cs` (Choose / look-ahead), `Simulation/TurnResolver.cs` (teleport on entry).

**Why it matters:** this bot is the instrument behind every balance decision (D-038, D-040, D-041, D-047) and behind the
guards in `BalanceTests`. A stall of this kind understates win rates and inflates turn counts, so some share of the
"losses" in every sweep is the bot failing rather than the dungeon winning.

**Fix direction (not applied — it re-baselines every balance number and the D-047 class guard):** give `Choose` a short
memory keyed on (position, command) that skips a command which last produced neither movement nor new knowledge, or teach
the look-ahead a teleport's destination. Either way, re-measure `HeroSweep`, `DifficultySweep` and the guards afterwards.

**Aid added:** `PlaythroughTests.TraceWatchRun` ([Explicit], `CD_RUN=n`) replays a watch batch with the same seeds, hero
alternation and between-run talent spending, then prints the chosen run turn by turn, the floor as it ended, the most
stood-on tiles, and whether the key was reachable.

### Repair 2026-09-20 (D-048) — MAINT-15 fixed, balance re-baselined

| ID | Status | Evidence | Change |
|---|---|---|---|
| MAINT-15 | FIXED | VERIFIED (the traced run flips from a 587-turn loss to a 530-turn win) | `AutoPlayer` skips travel that previously left it on the same tile without uncovering anything; memory cleared per floor. |

**Every balance number in this repo before 2026-09-20 was measured with a bot that could stall.** Re-measured: Knight's
Trial casual 67% → 92% won, novice 30% → 55%; Blobert's Wrath novice 5% → 15%. The tiers are much easier than the
documented targets, and retuning them is open (see D-048 "NOT DONE"). Class parity (D-047) survived the re-measure.
Remaining bot weakness: aimless wandering on a floor whose key it has not found — inflates turn counts, not yet addressed.

### Repair 2026-09-20b (D-049, D-050)

| ID | Status | Evidence | Change |
|---|---|---|---|
| MAINT-15 | FIXED (remainder open) | VERIFIED (traced run 587 → 317 turns) | Vault-door memory stops the bot re-entering a vault it has already looted; a pit becomes the goal when a floor is fully uncovered and the key is still not held. Aimless shuffling on an unsolved floor remains — see D-049 "KNOWN REMAINDER". |

Knight's Trial retuned with the honest bot (D-050): traps +1. 60 blind seeds, won — Easy 97%/100%, **Knight's Trial
78%/35%** (was 90%/52%), Blobert's Wrath 45%/15%. Class parity holds (Knight 12, Paladin 14 on 40 novice seeds).


---

# Audit 3 — 2026-09-20

**Worktree:** `D:\Click` (only worktree) · **Branch:** `main` at `4076142`, clean tree · **Scope:** whole repository.
8 commits since audit 2 (`cac106e`), all of them audit-2 repairs plus balance work: D-043 … D-050.

**Method:** five read-only component auditors in parallel (repairs since audit 2 · never-reviewed tooling · profile economy
and talents · Unity runtime · test honesty), then two independent verifiers tasked to *disprove* every Medium, then a
cross-component/adversarial reviewer. The lead re-traced every Medium personally and arbitrated where two reviewers
disagreed. Headless suite confirmed green at this commit: **262 passed / 0 failed**.

**Weighting, stated plainly:** audits 1 and 2 already swept the whole repo, so this run weighted toward (a) the eight
repair commits, because audit 1's repair cycle introduced a fresh defect that way, (b) the surface both prior audits
recorded as unreviewed, and (c) whether the tests are honest rather than how many there are.

Evidence levels: **VERIFIED** (traced by one auditor and independently re-traced by a verifier or the lead) ·
**CONFIRMED** (traced end to end by one auditor only) · **SPECULATIVE** · **NEEDS_EVIDENCE**.

## Findings

| ID | Sev | Evidence | Title | Key files |
|---|---|---|---|---|
| REL-23 | **Medium** | VERIFIED (3 traces) | **The Paladin talent Judgement can never fire.** `Staggered` is set in step 6 of a turn and cleared in step 9 of the *same* turn, converted into `Intent.Recover()`; the player's next-turn slash reads a flag already false. No reachable ordering exists: `enteredCell` is set only by Move/Dash, so a slash turn cannot take an early exit before `DeclareAll`; nothing in the codebase ever sets `Awake = false`, so no enemy escapes the clear; the boss is exempt on both sides. 3 ranks, tier 1, and the **prerequisite for Consecrate → Dawnstrike → Wrath of Dawn**, so the branch cannot be bought without it. Every Paladin balance number (D-047 parity, D-050 tiers) was measured with it inert | Simulation/Talents.cs:19, EnemyAi.cs:32-36,140, TurnResolver.cs:134, ContentCatalog.cs:280-283, docs/rules.md:615,628 |
| DATA-15 | **Medium** | VERIFIED (lead) | **The profile store collapses three different `TryRead` failures into "damaged".** A profile with a *newer* `SchemaVersion` reads fine but is treated as corrupt: moved to `.broken`, and `profile.json.bak` **deleted unconditionally** — while the notice says "could not be read … nothing was thrown away", both false. A second quarantine deletes the first. `FileSaveStore`, the model it was copied from, rejects a newer ruleset with a `FormatException` and never touches the file. **Latent:** `Versions.ProfileSchema` has been `1` since the profile existed; this fires the first time it is bumped and a tester runs an older kit once. `ProfileStoreTests.cs:323` asserts the destructive behaviour is correct | Application/ProfileSystem.cs:133-141,147-164,174; Tests/ProfileStoreTests.cs:323 |
| REL-24 | **Medium** | VERIFIED | **The one actionable profile message is destroyed one frame after it is set.** When the damaged profile cannot be set aside, `LoadNotice` says "close the game and move profile.json somewhere safe". `GameSession`'s constructor copies it to `ProfileNotice`, then `Mailbox.Welcome` bumps `NextMailId`, then `SaveProfile()` throws (because `_mayOverwrite` is false), and the catch **overwrites the notice with the generic "could not be saved"**. Deterministic, not a race | Application/ProfileSystem.cs:159-163,214-216 → GameSession.cs:26,29-32,50-51 |
| REL-25 | **Medium** | VERIFIED | **A failed shop / talent / mail write is still silent — the exact case D-043 claims it fixed.** `ProfileNotice` is rendered in exactly one place, `TitleScreen.Refresh()`, which also *hides* every overlay and so can never run while the shop is open. Every spending path calls `SaveProfile(); RefreshPurse();`, and `RefreshPurse` does not touch the flash line. The purse updates in memory, the write fails, the player is told nothing, and the purchase is gone on next launch | Unity/Screens/TitleScreen.cs:143-156,299-329; GameSession.cs:45-52 |
| DATA-16 | **Medium** | VERIFIED | **A watch/automation build writes the device's real `PlayerPrefs`, falsifying audit 2's isolation claim.** The *flags* isolate saves (`saves-automation`) and the profile (`MemoryProfileStore`), but the settings UI stays interactive and `UserPrefs` has no automation branch. Toggling PLAYTEST LOG writes `cd.playtestLog = false` while `ApplyTelemetrySetting` ignores the pref in automation — so nothing appears to happen and **the tester's next normal launch starts with telemetry off**. `cd.movementMode`, `cd.reducedMotion`, `cd.screenShake` are equally writable. Only two `AutomationMode` guards exist in the whole presentation layer | Unity/ClickDungeonApp.cs:301,465-528; GameScreen.cs:1234,1238,1509-1537; tools/playtest-kit/watch-bot.bat |
| CI-07 | **Medium** | VERIFIED | **Nothing that leaves a tester's machine can be attributed to a build.** `TelemetryEvent` and `run_started` carry no build or app version; `VERSION.txt` sits beside the exe and is never collected; `bundleVersion` is still `1.0` with no in-game label (MAINT-10). Consequence, checked against the record: **no entry in `docs/decisions.md` has ever cited playtest telemetry** — D-048/D-050 cite local sweeps only. The pipeline is un-load-bearing, which is why the gap survived three audits | tools/playtest-kit/collect-logs.ps1:15-46, Application/Telemetry.cs:17-23,125-141, make-kit.ps1:48-54 |
| DATA-06 | **Medium** | VERIFIED (raised from Low) | **Tester logs still reach the public repo by following the guide.** `docs/playtest-guide.md:64` now says "keep the logs outside the git repo", but `:63` and `:69` still give relative paths that resolve only from the repo root, and `.gitignore` covers none of `playtest-logs/`, `*.jsonl`, `playtest-summary.md`. Upgraded because REL-09's fix (landed *after* this was first raised) added `Player.log` to the collected zip — GPU, CPU, OS build, resolution, plus any path the `%USERPROFILE%` redaction missed. The folder is named after the player. Pushed once, permanent | docs/playtest-guide.md:63-70, .gitignore, tools/playtest-kit/collect-logs.ps1:37-46 |
| TEST-06 | **Medium** | VERIFIED | **All 14 `[Explicit]` tests are printers with no assertions.** `GuardBaselineTests` (6), `BalanceTests` (5), `PlaythroughTests` (2), `TileFeatureTests` (1) — every one is `Console.WriteLine` only. **This corrects audit 2's TEST-03**, whose implied remedy (remove `[Explicit]`) would convert six skips into six unconditional passes — strictly worse, because the runner would then count them as protection. `PlaythroughTests` is the file X-2's remediation plan names as the fix for the unguarded growing-profile curve | Tests/GuardBaselineTests.cs:16,41,71,104,134,160; BalanceTests.cs:127,146,193,249,294; PlaythroughTests.cs:19,65; TileFeatureTests.cs:301 |
| TEST-07 | **Medium** | VERIFIED | **The balance guards cannot catch the instrument failure they exist for.** Medium asserts only `ReachedBoss >= 11/30` and **no win count at all**; the stalling bot of MAINT-15 measured Easy at 100% won, so `SquiresStroll…` (the tightest guard) passes it, and `TiersKeepTheirOrder` (100 > 30 > 5) passes it. Separately, `Tally` **already tracks `Stalled` and `StalledByFloor`** and no non-explicit test reads either field — one `Assert.That(medium.Stalled, Is.Zero)` would have caught MAINT-15 the day it landed. Class parity uses an **absolute** ±8 band against counts D-050 halved to 12/14, so a novice-only 2× imbalance (8 v 16) now passes; the casual arm of the same loop is a partial control | Tests/BalanceTests.cs:52-58,95-125,224-247 |
| REL-26 | Low | VERIFIED (downgraded from Medium) | **A vault can be re-entered indefinitely.** `LeaveVault` restores the outer floor by reference with its door still `Used`, and `GenerateVault` is pure in (seed, floorIndex, door) so it rebuilds an identical room. **The reward dedupe holds** — `run.HasReward(TransactionId)` blocks `MaxHp`, `SlashDamage`, potions and their coins, so permanent hero power is NOT farmable. What leaks sits *above* the loop: `Treasure.Gems` (+1/cycle), `Treasure.Item` (same item, deterministically), and `run.ChestsOpened++` (banks into the "open 20 chests" achievement). Cycle cost is 10-14 turns (Epic chest = 4 taps), not 2. **D-049 fixed the bot instead of the game** | Simulation/RunFactory.cs:73-85, FloorGenerator.cs:210-218, Chests.cs:150-156,163 |
| REL-27 | Low | VERIFIED | **`FloorValidator`'s reachability model does not know teleports exist.** `safe` accepts `ContentKind.Teleport` as plain floor while `Hazards.cs:83-92` relocates the hero on entry. Every solvability proof — including the 3,000-floor fuzz — is proved on a board the game does not implement. **Same wrong model that produced MAINT-15, in production code rather than in the bot** | Simulation/FloorValidator.cs:95-125, Hazards.cs:83-92 |
| REL-28 | Low | VERIFIED (downgraded from Medium) | **Any click that opens a modal during `-cdWatch` freezes the bot for the rest of the run.** `Tick` samples `wasPressedThisFrame` at 60 Hz while the watch loop samples `isPressed` once per move, so a ~100 ms tap opens the pause menu instead of quitting — **Esc-to-stop, which `watch-bot.bat` and `PLAYTEST-README.txt` both tell testers to use, is broken in the common case**. NOT recorded as a loss: `RunStatus` has no Abandoned value and the log line captures the run before abandoning, so it prints `InProgress`. A second Escape unfreezes it. **Corrects the raised claim that this corrupts tuning data:** every tuning entry (D-038/D-048/D-050) records headless `DifficultySweep`/`HeroSweep` numbers, not watch batches | Unity/ClickDungeonApp.cs:182-208, GameScreen.cs:213,272-277,441 |
| REL-29 | Low | VERIFIED | **Watch mode leaves the whole player UI live, and QUIT TO TITLE / ABANDON RUN kill the batch.** `ShowTitle` sets `_game = null`; the loop's next `_game.AutomationSubmit(...)` throws; the handler at `ClickDungeonApp.cs:65-70` turns that into `Application.Quit(1)` **before any per-run line or the summary is written**. Best explanation for an observed batch ending at run 9 of 10 with no summary line. Board taps also inject player commands into the bot's run | Unity/ClickDungeonApp.cs:65-70,189,332-336; GameScreen.cs:590-591,651-660 |
| DATA-17 | Low | VERIFIED (arithmetic re-done by lead) | **`Progression.Level` cannot terminate for a large `Xp`, and `Repair` clamps only the lower bound.** `XpForLevel` returns `(wrapped int)/2`, so it can never exceed 2^30; **any `Xp >= 1,073,741,824` — roughly half the positive int range — loops forever.** The intermediate `50*(L-1)*L` overflows at **L = 6555** (last clean value `XpForLevel(6554) = 1,073,709,050`). The hang is in the `GameSession` constructor via `Achievements.Check` → the `seasoned` achievement, before any screen renders; the `.bak` is never consulted because the file parses fine. Hand-edit only (~15M wins to earn) | Application/Progression.cs:19-26, ProfileSystem.cs:197, Achievements.cs:25, ContentCatalog.cs:376 |
| REL-30 | Low | CONFIRMED | **`AdoptFrom` is a hand-maintained allow-list that drops `_saveWarning` and the armed ability mode.** `_saveWarning` is the "tell the player once" latch, so every rotation re-inserts "Couldn't save your run…" into the adopted log — and that insert skips the `MaxLogLines` trim, so the log grows unbounded. `_mode` resets to Move, turning the player's next tap into a spent turn, possibly onto a hazard | Unity/Screens/GameScreen.cs:200-207,700,709-722 |
| REL-31 | Low | CONFIRMED | Riposte answers only melee while its Paladin twin Holy Bulwark answers **every** block (it lives inside `Combat.DamageHero`). `rules.md` gives both the identical phrasing with no melee qualifier. On Blobert's floor — the fight the Knight's BULWARK path is built for — Riposte is dead weight against the whole slam cycle. Same shape as REL-20 | Simulation/EnemyAi.cs:143-145 vs Combat.cs:18-21; docs/rules.md:604,616 |
| REL-32 | Low | CONFIRMED | Achievements are re-checked only in the `GameSession` constructor and in `BankRun`, but `ItemsOwned` moves on every `Inventory.Grant` — so buying the fifth piece of gear shows "5/5" with the goal unearned and no letter until the next run ends | Application/GameSession.cs:31,155; Unity/Screens/ShopOverlay.cs:288-315 |
| REL-33 | Low | CONFIRMED | `Telemetry.Report` has no exception handling and `FromFiles` keeps neither path nor line number, so a malformed record gives a bare stack trace with no way to find the file; `MalformedLines` is one global counter; a directory with no `.jsonl` is reported as a *usage* error | Sim/ClickDungeon.Telemetry.Report/Program.cs:13-57 |
| REL-34 | Low | CONFIRMED | `collect-logs` failure text blames a cause REL-02 removed ("close the game"), and `collect-logs.ps1:48` deletes the existing zip *before* compressing, so a failing run loses the tester's previous successful collection too | tools/playtest-kit/collect-logs.bat:22-26, collect-logs.ps1:48-55 |
| REL-35 | Low | CONFIRMED | Automation arguments have no upper bounds: `-cdWatch` accepts `Infinity` (permanent hang, and Esc is only polled once per move), `-cdLevel 10000` overflows to a negative `Xp` and reports level 1 — the flag doing the opposite of what it says — and a bad `-cdTelemetryDir` is a warning, so an automation run asked for telemetry exits 0 having produced none | Unity/ClickDungeonApp.cs:94,145-146,159-160,306-313 |
| REL-08 | Low | VERIFIED (upgraded PLAUSIBLE → CONFIRMED) | `TelemetrySummary` wraps only `JObject.Parse`; every cast after it is unguarded, so a type-confused but parseable line terminates the whole run instead of counting as malformed. `:77` defensively coerces `data`, then `:104` re-indexes the raw token, discarding that guard | Application/TelemetrySummary.cs:60-68,70,75-76,104,157,174 |
| DATA-18 | Low | CONFIRMED | The guide's fallback ("copy all .jsonl into your own telemetry folder and use Summarize Logs") points at the facilitator's **live** folder: tester data is folded into their own sample, and a later `collect-logs` run re-zips every tester's log into one envelope | docs/playtest-guide.md:72 → Editor/TelemetryMenu.cs:11,24,32 |
| DATA-19 | Low | CONFIRMED | `write_borders` replaces rather than merges when `--only` is absent, so a slicer run with missing references — **which a fresh clone always is**, the reference art is gitignored — overwrites committed `borders.json` with an empty map. Blast radius bounded: `ArtImportPostprocessor` treats border 0 as a no-op, so existing `.meta` values survive | tools/art-slicer/slice_references.py:537-552, .gitignore:19-21 |
| SEC-04 | Low | VERIFIED (still open, third instance found) | Hand-edited `Equipped`: duplicate values apply one item's stats twice; unclamped `Mailbox` coins. **New:** nothing checks that an `Equipped` *key* matches the item's own slot, so `{"Weapon": "iron_helm"}` wears seven items in six slots — and inflates Renown → Threat, so it partly punishes itself. `Repair` was extended for null collections but never for `Equipped`'s shape | Application/Inventory.cs:80-93, Mailbox.cs:63-67, ProfileSystem.cs:186-206 |
| SEC-05 | Low | CONFIRMED | Every action in the iOS workflow is on a mutable major tag, not just `unity-builder` (SEC-03): `checkout@v4` and `cache@v4` run **in the same job as the Unity secrets**, and the editor then executes every `[InitializeOnLoad]` script in the checkout. Blast radius capped by the trigger surface — `workflow_dispatch` only, so no untrusted repo content reaches it | .github/workflows/ios-ipa.yml:24,27,33,50,61,88 |
| CI-08 | Low | CONFIRMED | No `timeout-minutes` and no `concurrency` on either job: a hung Unity batch inherits the 6-hour limit and repeated dispatches stack. `BuildIos` calls `EditorApplication.Exit` only on the line after `BuildPlayer` returns | .github/workflows/ios-ipa.yml:20-22,56-59 |
| CI-09 | Low | VERIFIED (lead, by execution) | **`dotnet test` exits 0 when zero tests are discovered** — I ran it with a filter matching nothing and the process returned success. `make-kit.ps1`'s gate is `if ($LASTEXITCODE -ne 0) { throw }`, so it cannot distinguish "all passed" from "none ran". Compounding: the 77-test Unity EditMode suite gates nothing, and `-SkipTests` leaves no trace in `VERSION.txt` | tools/playtest-kit/make-kit.ps1:18-22,48-54 |
| CI-10 | Low | CONFIRMED | The kit zip is named from the packaging-time commit even when it disagrees with the player's build stamp; the mismatch is the script's **last line**, a `Write-Warning` after two success lines, exit 0. This is CI-02's residual: the fix delivered the record (`VERSION.txt` names both), not the gate | tools/playtest-kit/make-kit.ps1:29-33,56-76 |
| CI-11 | Info | CONFIRMED | On a public repo, both workflow artifacts are downloadable by anyone who can see the run: the full Xcode project (7 days) and a complete installable unsigned `.ipa` (14 days). Nothing in the workflow, README or guide says so | .github/workflows/ios-ipa.yml:50-54,88-92 |
| PERF-02 | Low | CONFIRMED | Rotation destroys and rebuilds **both** screens and eleven overlays — including a full talent tree and a tile per catalog item — and the trigger `Screen.height > Screen.width` is polled every `Update()` with no hysteresis, so on the *Windows* kit a window drag across square can rebuild per frame. (Correction to the raised claim: the save is re-read only on the not-playing branch, so rotation mid-run does not touch the store) | Unity/ClickDungeonApp.cs:226-262, TitleScreen.cs:57-109,161 |
| MAINT-16 | Low | VERIFIED (lead) | **The gear ladder inverts at the currency change.** Common 150c, Uncommon 300c, Rare **600c**, Epic **15 gems**; the same shop sells gems at 30c each, so Epic is **450c** — 150 coins cheaper than Rare, same screen, same day. Legendary (900c) is consistent | Application/Shop.cs:44-45,183-193 |
| MAINT-17 | Low | CONFIRMED | `make_app_icon.py`'s `SPLIT_X = 377` is a second unchecked art↔manifest contract, depending on three upstream values in `slices.json`. A wrong split does not fail — `crop(None)` returns the whole image — it produces a plausible-looking icon, and the output is committed and baked into all three builds | tools/art-slicer/make_app_icon.py:17,35-36 ↔ slices.json:85 |
| MAINT-18 | Low | CONFIRMED | A wrong-shaped reference image is a warning **printed after all 286 PNGs have already been written** over the committed art; a same-aspect-different-content reference produces no warning at all; out-of-range rects are clamped and stretched rather than rejected; exit code stays 0 | tools/art-slicer/slice_references.py:499-502,510,516,527 |
| MAINT-19 | Low | CONFIRMED | **`rules.md` §10.1's headline "Measured difficulty" table is pre-D-048 sighted data**, contradicting the shipped tuning by 19 points (casual Knight's Trial 97% vs D-050's 78%). The honest numbers are prose below an older paragraph, with two orphaned rows separated from their header | docs/rules.md:386-392,425-438 |
| MAINT-20 | Low | CONFIRMED | Portrait touch targets below the 7 mm / 48 dp floor: shop tabs ≈ 15 × 3.3 mm, mail paging ≈ 8 × 2.9 mm, letter rows ≈ 4 mm. `OverlayStage` scales to panel *width* only and nothing checks the resulting height of an interactive rect | Unity/Ui/RefLayout.cs:44-51, ShopOverlay.cs:70-72, MailOverlay.cs:20,56-58 |
| MAINT-21 | Low | CONFIRMED | `MAINT-12` is worse than recorded: there are **three** copies of the portrait board rectangle (slices.json `blank`, a hand-copied Python literal, and the C# constants), not two. The contract is intact today — verified by arithmetic to within 0.4 px — but nothing on any side checks it | tools/art-slicer/make_portrait_backgrounds.py:22-26 ↔ Unity/Screens/GameScreen.cs:149-152 ↔ slices.json:286 |
| MAINT-22 | Info | CONFIRMED | Eight emitted telemetry events are neither documented nor summarised, including `teleported` — the field signal that would have shown MAINT-15's pattern in human play is collected and never read | Application/Telemetry.cs vs docs/telemetry.md:37-52 |
| MAINT-23 | Info | CONFIRMED | D-045 leftovers: `slices.json` still marks the three removed FX as `"wired": true`, so the contact sheet reports them as wired; `art-brief.md` and `checklist.md` were not updated. No code path can reach a null sprite | tools/art-slicer/slices.json:278,279,283; docs/art-brief.md:309 |
| MAINT-24 | Info | CONFIRMED | Second Wind is sold as "arriving on a new floor" but `TryFall` deliberately grants no breather, so a pit drop — which *is* a new floor — pays nothing. Rule decision, but both texts are false for one of the two ways down | Simulation/TurnResolver.cs:199-203 vs :234-240 |
| MAINT-25 | Info | CONFIRMED | A stray imp fireball hits another enemy for base damage while the tile is telegraphed with the renown-raised number; bomb friendly fire *does* use the raised number, so the two friendly-fire paths disagree with each other | Simulation/EnemyAi.cs:182 vs :170,177; Hazards.cs:126 |
| MAINT-26 | Info | CONFIRMED | Floor 1 is laid out before the profile is applied: `SetupFloor` runs `PlacePremiumChest` and `ApplyThreat` before `Provision` and before `Run.Threat` is assigned. Harmless today only because `PremiumFirstFloor = 2` and `Renown.FirstFloor = 3`; nothing asserts that invariant | Application/GameSession.cs:88-94 vs Simulation/RunFactory.cs:137,194-195 |
| MAINT-27 | Info | CONFIRMED | Sanctified's mana refill is unreachable *only* in the full-health case — `Mana.Refill` runs on every accepted potion, so a hurt Paladin gets it and the fountain half is entirely live. **Downgraded from a raised "half-dead capstone"** | Simulation/Commands.cs:57-60 vs TurnResolver.cs:92-93 |
| MAINT-28 | Info | CONFIRMED | The Royal Chest's card omits the duplicate rule its cheaper sibling states; 20 gems can return 25 coins with no warning and no owned-exclusion | Application/Shop.cs:118-119 vs Inventory.cs:28-32 |
| MAINT-29 | Info | CONFIRMED | Public README describes "Gate 1 — ugly mechanical prototype" and omits `tools/`, `.github/`, the telemetry reporter, `UnityTests/` and the Android/iOS build methods | README.md:5,9-22 |
| TEST-08 | Low | CONFIRMED | `AbandonNeverThrowsWhenTheDeleteFails` walks straight through DATA-11 and asserts only that nothing throws; its sibling death-path test two methods up *does* assert `store.Saved`. Adding the same assertion goes red today | Tests/SessionFailureTests.cs:103-113 vs :89-101 |
| TEST-09 | Low | CONFIRMED | `TheSameRunAlwaysFindsTheSameItem` cannot fail — it builds an identical scenario twice — and names a property DATA-10 says the code does not have; it uses a vault chest, never the boss, which is the one case where the property is false | Tests/InventoryTests.cs:61-72 |
| TEST-10 | Low | CONFIRMED | No test wires a failing profile store into `GameSession`: the `IProfileStore` parameter is never supplied, so `MemoryProfileStore` is always used, `SaveProfile`'s failure branch never runs, and `GameSession.ProfileNotice` is never asserted anywhere. `FileProfileStore` has no injectable seam, so the partial-write case cannot be written without one | Application/GameSession.cs:19-32,45-52; Tests/ProfileStoreTests.cs |
| TEST-11 | Low | CONFIRMED | 20 of 24 talents never traverse `Progression.Apply` (audit 2 said 21; `k_fortune` gained coverage). The `With()` helper writes `run.Perks` directly, which is only `Apply`'s `default:` branch — so the switch's four special cases are structurally unreachable from these tests, and `With(run, (MaxHearts, n))` is a silent no-op. **No non-explicit test plays a talented hero through `TurnResolver`, which is exactly how REL-23 survived** | Tests/ClassTalentTests.cs:16-20; Application/Progression.cs:112-137 |
| TEST-12 | Low | CONFIRMED | Three tests self-skip silently via `Assume.That(… Win32NT)` and are reported as passing runs, so on any non-Windows runner REL-11's and REL-02/REL-09's guards evaporate without a red mark | Tests/SaveValidationTests.cs:122, RemediationTests.cs:159,191 |
| TEST-13 | Info | CONFIRMED | Weak assertions that cannot distinguish right from wrong: `TapsToOpen(...) >= 1` (satisfied by construction), reward-table sums `> 0`, `GearPrice` restated on both sides, and no test asserts successive shop chests differ (both calls are roll 0) | Tests/ClassTalentTests.cs:169, CrownAndMailTests.cs:78, DailyRewardTests.cs:68, ShopTests.cs:100-135 |
| TEST-14 | Low | CONFIRMED | The whole delivery pipeline is outside both suites: `make-kit.ps1` behaviour, `Telemetry.Report`, every Python script, both art contracts, and the workflow. `slice_references.py --self-test` exists and is never invoked by anything | Scripts/Tests/*, Scripts/UnityTests/* |
| TEST-15 | Low | CONFIRMED | The `UnityTests` assembly is `includePlatforms: ["Editor"]` and there is no PlayMode assembly, so **no coroutine in this codebase is ever executed by a test** — every tween, banner, chest sequence and automation loop is structurally untestable today. `RefLayout.Portrait` is a mutable static with no reset; the first portrait test that sets it leaks into every later test | UnityTests/ClickDungeon.Unity.Tests.asmdef; Ui/RefLayout.cs:22 |

### Prior findings re-checked

**Fixed and still fixed:** REL-19, REL-20 (vault chest quality and renown hearts — `EnterVault` now matches `SetupFloor` for
everything that should apply; the four omissions are all correct by the D-018 rule), REL-13, REL-14, REL-15, MAINT-13,
DATA-07/DATA-08 (the atomic write itself is sound; its *failure* semantics are DATA-15/REL-24).

**Still open, re-verified:** DATA-05 (and the direction of the bias is now known — it inflates run count and deflates
deepest floor, biasing exactly the signal tuning would read), DATA-09, DATA-10, DATA-11, DATA-12, DATA-13 (**materially
worse**: D-043 made the profile write atomic and verified, so the window where provisions are consumed before any run
save exists is now reliably committed rather than sometimes lost), DATA-14, REL-16 (**narrowed**: `CloseNow` closes the
`Relayout` path, but `ShowTitle`/`OpenGame` destroy a `GameScreen` without it — safety now rests entirely on all four
callers being guarded on `_chest.IsOpen`), REL-17, REL-18 (**recorded too narrowly**: the banner wins the z-fight in
landscape too; landscape is saved only because an overlay re-asserts `SetAsLastSibling` when it opens, which is exactly
what portrait's re-parenting defeats), REL-22, SEC-02, SEC-03, CI-03, CI-04, CI-05 (**narrowed**: the `Builds/iOS` path
arithmetic is consistent, so the raised concern is probably unfounded; the unknown moved to container file ownership and
the whole macOS job), CI-06, MAINT-01, MAINT-04 (**`requirements.txt`'s own claim is false** — it pins only Pillow while
13 slices route through unpinned OpenCV/numpy, and the two multi-MB backgrounds it regenerates are committed),
MAINT-05, MAINT-09, MAINT-10, MAINT-11, MAINT-12 (see MAINT-21), MAINT-14, MAINT-15 (**remainder only**), TEST-01,
TEST-02 (**still true of the D-044 repairs**: REL-13 and REL-15 rest on a single screenshot), TEST-03 (see TEST-06),
TEST-04 (see TEST-11), TEST-05 (**drift roughly doubled**: checklist says 137 headless / 199 Unity; actual 262 / ~353).

### Rejected / downgraded this round

- **"The D-048 anti-stall memory only catches loops of length 1, so the MAINT-15 teleport bounce is still reachable" —
  FALSE_POSITIVE.** Pads are generated strictly in pairs (`FloorGenerator.cs:165-184`) and arriving on the far pad does
  not re-fire it (`Hazards.cs:83-95`), so from a pad *every* pad-move ends where it started: zero displacement, recorded
  barren, caught. What was described as the loop is a single legitimate hop. The underlying observation (only
  zero-displacement repeats are remembered) is true but maps onto D-049's already-recorded wandering remainder.
- **"A vault yields unbounded hero power" — DOWNGRADED.** The `TransactionId` dedupe blocks `MaxHp`, `SlashDamage`,
  potions and their coins. Gems, duplicate-item coins and the chest counter leak; the cycle is 10-14 turns, not 2.
- **"Sanctified's capstone is half dead" — DOWNGRADED to Info.** `Mana.Refill` runs on every accepted potion.
- **"A rotation after `Abandon()` requires a force-quit" — DOWNGRADED to Low.** The watch coroutine builds a fresh screen
  on its next iteration, so it self-clears in ≤7 s; on the player path `Abandon()` and `ShowTitle()` are consecutive
  statements, a zero-frame window.
- **"An Escape tap during a watch batch is recorded as a loss" — DOWNGRADED.** `RunStatus` has no Abandoned value and the
  log captures the run before abandoning, so it prints `InProgress` — an interruption marker. The freeze is real and
  Esc-to-stop is genuinely broken, but no tuning number is affected: **every difficulty decision cites headless
  `DifficultySweep`/`HeroSweep`, not watch mode.**
- **"Rotation re-reads the save from disk" — DOWNGRADED.** Only on the not-playing branch.
- **"`FileSaveStore` and `FileProfileStore` share a folder, so a save delete could take the profile" — pre-empted as a
  FALSE_POSITIVE.** `Delete` enumerates three explicit paths, not the directory.
- **"`Progression.Level` hangs only at `int.MaxValue`" — CORRECTED by the lead.** Two verifiers disagreed; the
  arithmetic settles it at `Xp >= 2^30`, and the overflow level is 6555, not 9269.
- **`Xp` overflow reachable by play — rejected.** ~15 million winning runs.
- **Level/talent point accounting — examined, sound.** A profile cannot end with more talents learned than points
  earned, nor lose a point; old talents refund. One latent content-shape invariant: a hypothetical tier-5 talent would
  be a capstone with no point requirement.
- **Daily reward across a clock change — examined, no defect.** Backwards clocks block claims until the recorded day
  passes; DST and a UTC+13 → UTC−11 hop cost at most one day and do not break the streak. `Repair`'s `DailyStreak`
  clamp is load-bearing against an out-of-range index.
- **Two telemetry summarisers — FALSE premise.** `Telemetry.Report` calls the same `TelemetrySummary`; there is no drift
  risk from a second implementation.
- **Public-content scan — clean.** No tester data, credential, keystore or binary has ever been committed on any ref;
  pack size 37.7 MiB, unchanged since audit 2.
- **Sell mechanic mid-run corruption — rejected.** There is no sell mechanic anywhere; a run never reads the profile.

## Cross-component chains (lead + dedicated reviewer)

- **Chain A — the player is never told the thing they can act on.** `TryRead` collapses three causes → `Load` quarantines
  → `KeepTheDamagedFile` deletes the backup → the actionable notice is set → `Mailbox.Welcome` forces a save → the save
  refuses → the catch overwrites the notice → `TitleScreen.Refresh` is the only renderer and cannot run while an overlay
  is open. **DATA-15 → REL-24 → REL-25 are one chain, and they are this audit's most damaging finding.**
- **Chain B — the instrument's defects reach the product.** `AutoPlayer.PlayRun` builds a run with no profile, no
  talents, no gear and `Threat = 0` → every guard and sweep goes through it → the guards are lower bounds → the honest
  growing-profile test is `[Explicit]` and has no assertions → `rules.md` §10.1 still shows pre-D-048 numbers →
  `run_started` carries no Threat or level. The curve a real player meets is measured by nothing and logged by nothing.
- **Chain C — the telemetry pipeline is severed and nobody noticed because nothing consumes it.** Build stamps
  `BUILD-VERSION.txt` → kit records both versions → zip is named from the wrong one on a mismatch → no in-game version
  label → `TelemetryEvent` has no build field → `collect-logs` never collects `VERSION.txt` → the summariser records no
  provenance → **no decision has ever cited playtest telemetry.**
- **Chain D — derived art is committed and nothing re-derives or checks it.** The portrait contract holds today to
  within 0.4 px, in three places, checked by nothing, on the one surface nobody has ever run.
- **Chain E — the economy's only mintable currency buys the gear that raises the only hidden difficulty input.** Vault
  re-entry mints gems → gems buy Epic gear at 450 coins (cheaper than Rare) → each worn Epic piece is +1 renown →
  `Run.Threat` → which no guard exercises and no log records. Four Lows that are a Medium together.

### X-1 and X-2 re-verified

- **X-1 (debug-signed APK + no profile backup + manual log collection) — still accurate, GROWN.** D-043's `.bak`
  does **not** help X-1: `.json`, `.bak` and `.broken` all live in the folder Android wipes on uninstall. Three copies,
  one blast radius, and there is no export/import anywhere. New members: DATA-15 (handing a tester an older kit is a
  normal facilitator move and now destroys a backup) and CI-07. X-1 should now block on two decisions, not one — the
  keystore **and** whether the profile gets an export path before the next APK.
- **X-2 (the difficulty curve is neither guarded nor observable) — still accurate, GROWN substantially.** All three
  original legs hold verbatim. Four of this audit's findings belong inside it rather than standing alone: TEST-07's
  parity band, TEST-06's assertion-free `PlaythroughTests` (**which changes X-2's own remediation plan** — un-skipping
  the file as written produces an unconditional pass), MAINT-19's contradictory headline table, and Chain E.

## Coverage and limits

- **Reviewed:** the eight repair commits in full; `Sim/ClickDungeon.Telemetry.Report` (**unreviewed by both prior
  audits**); the Python art pipeline including the portrait and app-icon geometry, done by hand; all of
  `tools/playtest-kit/`; the workflow and the three build methods; the profile economy and all 24 talents one by one;
  the Unity runtime lifetime/rotation/input/automation surface; both test suites for honesty rather than count;
  `.gitignore`, docs and the repo's public history.
- **Not reviewed:** Unity package internals; the art slicer's `bright_boxes`/`snap` image math beyond a bounded-error
  check.
- **Could not verify without running:** anything device-shaped — portrait, rotation, touch, the Android Back gesture,
  the uninstall wipe at the heart of X-1 (this project has **never been run on a phone**); the iOS workflow, which has
  still never run (CI-05); whether `make-kit.ps1` succeeds end to end (CI-01/CI-02/CI-10 remain WRITTEN-level evidence
  since audit 1); whether an already-recorded watch batch was corrupted by REL-28/REL-29 (the logs are not kept);
  whether Unity persists `watch-bot.bat`'s `-screen-*` flags into a tester's preferences.
- **Audit error rate, stated plainly:** verifiers rejected **one** finding outright (the teleport-stall claim),
  downgraded **six** of the Mediums examined, and corrected the lead twice — on the vault cycle cost and on the XP
  overflow threshold. Two verifiers contradicted each other once; the lead settled it by arithmetic. Treat
  single-auditor CONFIRMED items as weaker than the VERIFIED ones.

## Remediation graph

```
G1 Profile failure semantics (serialize: Application/ProfileSystem.cs, GameSession.cs)   << do first
   DATA-15 (TryRead returns a reason: Absent | Unreadable | Newer; never delete a backup or an earlier .broken)
      -> REL-24 (a load notice outranks a later write failure)
      -> REL-25 (render the notice where the spending happens, not only in TitleScreen.Refresh)
   then DATA-11, DATA-12, DATA-13 (bank/delete ordering, still open from audit 2) — same files, serialize
   TEST-10 (a FlakyProfileStore seam) is the prerequisite for proving any of it

G2 Simulation correctness (independent of G1; one file each)
   REL-23 (Judgement) -> re-measure the Paladin -> revisit D-047 parity and D-050 tiers   << blocks balance work
   REL-31 (Riposte vs Holy Bulwark) — rules decision first, then a signature change in Combat.DamageHero
   REL-26 (vault): fix Chests.Open FIRST (move gems/items/counter inside the dedupe), THEN the door.
        These are NOT one fix — the first stops the minting, the second stops the identical room.
   REL-27 (FloorValidator teleport model) — same wrong model as MAINT-15, now in production code
   DATA-17 (cap the XP curve and clamp Repair's upper bound)
   REL-32, MAINT-16, MAINT-24, MAINT-25, MAINT-26, MAINT-27, MAINT-28 — independent, land together

G3 Tests that manufacture confidence (do alongside G1/G2, not after)
   TEST-06 (rename the printers out of *Tests, add separately asserting guards)
      -> TEST-07 (assert Tally.Stalled == 0 in the guards that already run; make the parity band a ratio)
   TEST-08, TEST-09, TEST-11, TEST-12, TEST-13 — each is a one-line-to-one-method change; TEST-08 goes red today
   TEST-15 (a PlayMode assembly) is the prerequisite for ever proving a coroutine or rotation fix

G4 Unity runtime (serialize: GameScreen.cs, Overlays.cs, ClickDungeonApp.cs)
   REL-30 (move screen-owned state off the screen rather than extending the allow-list)
   REL-28 + REL-29 + DATA-16 are ONE decision: what a watch build exposes. Hide the menu/settings affordances in
        AutomationMode and make UserPrefs a no-op writer there — that closes all three.
   REL-16 (fold CloseNow into a single Teardown all three destroyers call), REL-17, REL-18, PERF-02, MAINT-20

G5 Delivery (independent)
   CI-09 (assert a minimum test count — the gate cannot currently tell "passed" from "never ran")
      -> CI-10 (name the zip from the build stamp; throw on mismatch)
      -> CI-07 + MAINT-10 (one fix: stamp the build into TelemetryEvent and onto a screen)
   DATA-06 (gitignore + absolute paths in the guide) — do this before the next tester round
   SEC-03/SEC-05 (SHA-pin all six actions), CI-03, CI-04, CI-08, CI-11
   MAINT-17, MAINT-18, MAINT-21, DATA-19 (the art pipeline's unchecked contracts), MAINT-04
   X-1: decide the Android keystore AND whether the profile gets an export path, before the next APK

G6 Docs (land with whatever group touches them)
   MAINT-19 (rules.md §10.1 contradicts the shipped tuning), TEST-05 counts, MAINT-22, MAINT-23, MAINT-29, DATA-18
```

## Next order

1. **G1 first.** Chain A is deterministic, destroys a backup, and hides the one message the player could act on.
2. **REL-23 before any further balance work** — every Paladin number in the repo was measured with a dead talent.
3. **TEST-07's `Stalled` assertion** is the cheapest change in this document and closes the class of failure that cost
   four difficulty decisions.
4. **DATA-06 before the next tester round**; **X-1's two decisions before the next APK**.
5. G2's vault ordering matters: `Chests.Open` first, the door second.

### Repair 2026-09-20c (graphRepair on audit 3) — D-051, D-052, D-053

Five workstreams: three in isolated worktrees (profile boundary · simulation · delivery), two on the main tree (the
Unity automation surface, the guards). Then one independent reviewer tasked to disprove the whole batch, a repair loop
on what it rejected, and a re-measure.

Results: **headless 262 → 289 passed / 0 failed · Unity EditMode 380 total, 366 passed / 0 failed / 14 explicit skips.**

| ID | Status | Evidence | Change |
|---|---|---|---|
| REL-23 | FIXED | VERIFIED (revert → red: `Expected: 5, But was: 3`) | Judgement reads the durable state: `enemy.Staggered \|\| enemy.Intent.Kind == IntentKind.Recover`. The test goes through `TurnResolver` over two turns and asserts en route that the flag really is already false. The old test, which hand-set `Staggered` and called `Talents.SlashDamage` directly, is gone |
| DATA-15 | FIXED | VERIFIED (3 tests red on revert) | `TryRead` returns `ReadOutcome { Ok, Absent, Unreadable, Newer }`. A `Newer` profile is left exactly where it is with `_mayOverwrite = false`; quarantine rolls the `.broken` name instead of deleting, and an unreadable backup is moved aside rather than deleted. `ProfileStoreTests` was rewritten: it had been asserting the destructive behaviour was correct |
| REL-24 | FIXED | VERIFIED (revert → red) | `ProfileNotice` is a property guarded by `_noticeCameFromTheLoad`, so a later write failure cannot overwrite a load notice. **Discovered dependency:** DATA-15's fix *creates* this condition on the common path — before it, `_mayOverwrite = false` was rare; now every downgrade launch sets it. They must ship together |
| DATA-17 | FIXED | VERIFIED (revert → `Test exceeded Timeout value of 20000ms`) | `Progression.MaxLevel = 500`; `XpForLevel` clamps its argument and widens the product to `long`; `Level`'s loop is bounded; `Repair` clamps `Xp` to the ceiling |
| SEC-04 | FIXED | VERIFIED (revert → 5 bad entries survived instead of 1) | `Inventory.RepairEquipped` rebuilds `Equipped` from `SlotOrder`, keeping only entries filed under their own item's slot whose item is owned; `Mailbox.Collect` clamps every gift field |
| REL-26 | FIXED (both halves) | VERIFIED (each half red on reverting its own file) | `Chests.Open` guards the gem grant, item roll and `ChestsOpened++` behind `firstOpening`; `RunState.VisitedVault` stores the looted room so re-entry restores it instead of regenerating. Vault state now survives save/load, which it did not |
| REL-27 | FIXED | VERIFIED (2 tests red on revert) + drift measured | `FloorValidator` models a pad as an edge to its partner, chosen exactly as `Hazards.HeroEnter` chooses it. **The reviewer's blocker — unmeasured generation drift — was closed by measuring it:** ~3 points a tier (see below), no guard threshold crossed |
| REL-31 | FIXED | VERIFIED (revert → red) | Riposte moved inside `Combat.DamageHero` beside Holy Bulwark, so every blocked blow is answered, not only a neighbour's. **User decision.** A blocked bomb still answers nobody (hazards pass no attacker) |
| MAINT-16 | FIXED | VERIFIED (revert → red: `Epic gear costs 450 coins, no more than the rarity below it`) | Epic 15 → 21 gems (630 coins-equivalent, above Rare's 600). **User decision.** New `Shop.GearPriceInCoins` reads both currencies through the exchange the same screen sells |
| CI-09 | FIXED | VERIFIED for the gate, COMPILED for its caller | New `tools/playtest-kit/test-gate.ps1` requires the runner's summary line and a minimum discovered count (280), so the gate can no longer read "none ran" as "all passed". `VERSION.txt` now records whether the gate ran |
| CI-10 | FIXED | WRITTEN (static-asserted, never executed) | The kit is named from the build stamp, and a mismatch throws **before** staging instead of warning after it |
| DATA-06 | FIXED | VERIFIED (`git check-ignore`) | Ignore rules for the collector's output, anchored; the guide's examples are absolute paths outside the repo, so its commands and its warning finally agree |
| TEST-06 | FIXED | — | `GuardBaselineTests` → `GuardBaselineReports` (`.meta` renamed with it), with a note saying plainly that nothing in the file asserts anything and that un-skipping would add unconditional passes, not guards |
| TEST-07 | PARTIALLY_FIXED | parity band VERIFIED; stall ceiling COMPILED | The parity band is now `Math.Min(fewer + 8, fewer * 8 / 5)` — proved load-bearing by feeding it the 8-vs-16 split the old absolute band accepted. The stall ceiling is **not** proved: see below |
| TEST-10 | FIXED | TESTED (a double, nothing to revert) | `FlakyProfileStore` — the seam that makes every profile-failure path testable at all |
| REL-28 | FIXED | COMPILED | `GameScreen.Tick()` returns early in automation, so the batch's own Escape poll gets the key instead of the pause menu |
| REL-29 | FIXED | COMPILED | Every route to the title goes through `GoToTitle()`, which refuses in automation; the ABANDON action refuses; board and ability taps refuse. **Reworked after review** — see below |
| DATA-16 | FIXED | COMPILED | `UserPrefs.ReadOnly`, set once in `Awake`, refuses every setter — all seven keys, at one choke point |
| TEST-08 | NOT_FIXED | — | Its one-line assertion goes red today because **DATA-11 is still open**. Fixing `Abandon`'s bank/delete ordering is a design decision (what should happen when the delete fails?), not a one-liner, so the test waits on it |

### Independent review, and what it changed

A fresh reviewer was given the diff and told to disprove it. Verdicts: **2 APPROVED, 4 NEEDS_CHANGES, 1 REJECTED.**
Every blocking claim was re-verified by the lead before acting on it. What it caught:

- **Node B REJECTED, correctly — the lead's own work.** Guarding `BuildTopRight` and `OpenPause` on `AutomationMode`
  silently killed `-cdOverlay pause` and `-cdOverlay menu` (documented deliverables, `ClickDungeonApp.cs:82` and
  `decisions.md:426`) and removed the gear and ☰ from every portrait screenshot — in a tool whose entire purpose is
  showing what a player sees. **Reworked:** the UI is built and the pause menu opens as before; only the *destructive
  actions* refuse (`GoToTitle`, the ABANDON button). `Tick()`'s early return stays, since nothing automation relies on
  sits inside it.
- **DATA-15 had a false story on one branch.** Main `Unreadable` + backup `Newer` returned "made by a newer version,
  nothing was thrown away" while the real profile was rubble that never got quarantined. Fixed: the **main** file's
  outcome decides the message, and a newer backup is kept rather than moved (`_keepTheBackup`).
- **REL-31 introduced a new defect:** the riposte can kill inside `DamageHero`, after which `EnemyAi` still emitted
  `EnemyStaggered` — a "DAZED" popup on a corpse. Fixed by guarding the stagger on `enemy.Hp > 0`.
- **The parity ratio was looser than the old band above 13 wins.** Crossover at `fewer = 13.33`; at D-047's own 20/17 the
  old fence allowed 25 and the new one 27. Fixed by taking whichever fence is tighter, so the guard can only have become
  stricter.
- **The gate floor was stale on arrival** (250 against a 289-test suite) and `.gitignore` was "broader than needed while
  missing the one that mattered": `*.jsonl` and `summary.md` unanchored, and **`.claude/worktrees/` — three complete
  stale copies of the repo, 3,055 files — not ignored at all**, which a `git add -A` would have committed. Both fixed.

Not acted on, recorded instead: REL-23 leaves `enemy.Staggered ||` as a provably dead disjunct (defence in depth, and
the Judgement+Dawnstrike stacking case its deleted test also covered is not replaced); `Chests.cs`'s `SpecialKeys--`
is still outside `firstOpening` (unreachable today — premium chests are never placed in vaults — but it is the only
payout left outside the guard); old saves grant one free re-clearing of a vault's guards; `SaveSystem`'s new validation
does not assert `VisitedVault.IsVault`; and `RepairEquipped` rests on "the item table does not vary by difficulty",
which is true and asserted nowhere.

### The stall assertion, stated honestly

`Tally` has always counted `Stalled` and no live test read it. An assertion was added to two guards — but it is **not**
the regression test for MAINT-15 and has never been observed to fire. Reverting D-048's anti-stall memory left both
guards green: they run 30 seeds, and MAINT-15's loop was found on a seed they do not touch. The 60-seed sweep shows one
or two stalls a tier even with a healthy bot, so the assertion is a **ceiling** (`1 + runs/30`), not zero — zero would
go red the first time a legitimate change moved a seed. It is a cheap tripwire for a systemic stall, nothing more.

### Measurements after the batch (60 blind seeds, `DifficultySweep`)

| Tier | casual reach/win | novice reach/win | recorded at D-050 |
|---|---|---|---|
| Squire's Stroll | 97% / 97% | 100% / 100% | 97% / 100% |
| Knight's Trial | 80% / 75% | 52% / 32% | 78% / 35% |
| Blobert's Wrath | 63% / 43% | 35% / 18% | 45% / 15% |

The drift is REL-27's, and it is ~3 points a tier. **No tier was retuned** — that is a design decision.

**The sweeps cannot see REL-23 or REL-31 at all.** `AutoPlayer.PlayRun` starts runs through `RunFactory.NewRun` with an
empty `Perks` dictionary, so a talent fix cannot move a single sweep number; `ClassSweep` is digit-for-digit identical
with and without the Judgement fix. That is audit 3's Chain B demonstrated directly: **the guards measure a hero nobody
plays, which is precisely why a dead talent survived four difficulty decisions.** Measured where it does show — the
growing-profile playthrough, 30 seeds, talented Paladin — Judgement alone moves 18/30 wins to 23/30.

### New findings from this batch

| ID | Sev | Title |
|---|---|---|
| DATA-20 | Low | `Progression.BankXp` accumulates `Xp` in `int` with no ceiling and can wrap negative. No hang (`Level` is now bounded) and `Repair` clamps on the next load, so the window is one session. Spun out as its own task |
| TEST-16 | Low | The 289-test suite cannot see anything in the Unity layer, the `EnemyStaggered` event ordering, or floor-generation drift. Every Node B fix is COMPILED, never exercised — TEST-15's Editor-only assembly is why |

### Next order

1. **DATA-11**, then TEST-08's assertion — decide what `Abandon` does when the delete fails.
2. **Retune the tiers, or accept the drift and correct `rules.md` §10.1**, which still shows pre-D-048 sighted numbers
   contradicting the shipped tuning by 19 points (MAINT-19).
3. **Run `make-kit.ps1` once** — CI-01, CI-02 and CI-10 have been WRITTEN-level evidence since audit 1, and CI-09's
   caller is still unexecuted.
4. A PlayMode assembly (TEST-15) is the prerequisite for ever proving a Node B fix, a coroutine, or a rotation.
5. Still open from audit 3: DATA-18, DATA-19, REL-08, REL-25, REL-30, REL-32…35, SEC-05, CI-08, CI-11, PERF-02,
   MAINT-17…29, TEST-09, TEST-11…15.

# Audit 4 — 2026-09-22

Scope: D-061 (second-wave monsters), D-062 (act bosses, twenty floors in four acts), D-063 (six hero classes and
their class rules and talents). Commit `2f0d6e0`, branch `main`. Five read-only auditors by component, then one
reconciliation/adversarial reviewer; every High re-traced by the lead before it entered this table.

## Findings

| ID | Sev | Evidence | Title |
|---|---|---|---|
| REL-36 | **High** | VERIFIED (lead) | Enrage raises a blow that was already declared **and already drawn**. `Threats.Compute` runs at the stable boundary; `EnemyAi.Fury` is read at execution (EnemyAi.cs:326,381,419) and the player's own slash flips `Mode` (Combat.cs:77-81). Drawn 2, lands 3. The Engineer's drone can flip it with no attack at all (TurnResolver.cs:115). `Lines.IntentExplain` omits Fury entirely (GameScreen.cs:959), so tile band and inspect panel disagree in the same frame |
| REL-37 | Medium (was High) | VERIFIED, severity corrected during remediation | A knockback moves the shooter but not its declared line: `Talents.AfterSlash` mutates `target.Pos` (Talents.cs:85-93) and `Fire`/`Charge` execute from `enemy.Pos` with the old `intent.Dir` (EnemyAi.cs:363,410). The Wizard carries Knockback on every slash, so a surviving Boar or Spellbook charges/fires down a line the board never marked. Cross-component: invisible to both the class and the monster auditors. **Correction found while fixing it:** a knockback is always collinear with hero->target, so the re-aimed line is the drawn line extended backwards through the tile the monster just vacated - which the hero cannot occupy that turn. The hero's exposure was therefore unchanged, and the audit's "the far end becomes safe" was wrong. The defect is real as an invariant break (execution traced from a different origin than the drawing) and would bite the moment anything displaces a monster off the line - a pad, a pull, a second shove - so it was fixed, at Medium |
| REL-38 | **High** | VERIFIED (lead) | `BoardView.DrawThreats` gates the WEB/ARRIVES band on `damage <= 0` (BoardView.cs:437) and BOMB on an `else if` (:422), and `damage` is a **sum across sources** - so one adjacent chaser erases the Spider's web warning and the Bomber's landing marker on the hero's own tile. That band is the only warning either monster gives |
| REL-39 | **High** | VERIFIED (lead) | A sleeping Mimic is drawn as a chest but INSPECT reads "STONE FLOOR / Nothing here" (GameScreen.cs:996-998) where a real chest reads "CHEST - N more taps" (:1028-1033). A free probe that identifies every mimic without spending a turn; `Commands.TryContextual` also routes a mimic tap to Move and a chest tap to Interact |
| DATA-21 | **High** | VERIFIED (lead) | Economy: premium keys are sized `PremiumLastFloor-PremiumFirstFloor+1` = 18 (ProfileSystem.cs:63) against a documented three, and up to 15 land as guaranteed-item chests; **every** act boss pays Blobert's hoard (Combat.cs:139-156), so a won run banks 20 gems / 120 boss XP / four 50% gear rolls against a documented 5 / 30 / one. No guard test bounds any of it |
| MAINT-30 | **High** | VERIFIED (lead) | The shipped playtest kit describes a different game: PLAYTEST-README.txt:17 "Lord Blobert on floor 5", names the mascot as the hero, describes none of the D-058/061/062 monsters, and instructs testers to tap chests while the Mimic is in the floor-2 pool |
| TEST-17 | **High** | VERIFIED (measured) | `TheClassesWinAboutAsOftenAsEachOther` passes with **zero margin**: measured cleric 30, engineer 22, `allowed = min(22+8, 22*8/5) = 30`, asserts `30 <= 30`. One win either way red-builds it. It also measures casual only since D-063, and the ignored novice row spreads rogue 2 to berserker 9 |
| TEST-18 | **High** | CONFIRMED | 72 of 96 talents have neither a test asserting their catalog row (effect/amount) nor an icon file: three quarters of D-063's content is unverified end to end. `EachPlayableClassHasItsOwnWellFormedTree` checks tiers and strings, never `talent.Effect` |
| TEST-19 | **High** | CONFIRMED | The new run state is never round-tripped with a value in it (`Perks`, `CarriesKey`, `Disguised`, `DodgeSpent`, enemy `Mode`), and `JsonRoundTripIsLossless` structurally cannot detect a dropped field. `CarriesKey` lost on resume = an unwinnable floor on 4, 8, 14, 18 |
| REL-40 | Medium | VERIFIED (lead) | Summon cap checked only at declare with `<` (EnemyAi.cs:123,140) while `SummonCells` places up to `SummonCount` (:303-314): 3 bats become 5 against a cap of 4, 2 masks become 4 against 3. Lord Blobert has **no** `MaxMinions` at all. Telegraphed faithfully, so this is balance, not fairness |
| REL-41 | Medium | VERIFIED (lead) | `Combat.ResolveDeaths` applies the act-clear heal (Combat.cs:145-154) **before** the hero-death check (:167): a hero killed by the same blast that fells the boss is restored to full and the run continues. `Talents.Drone` (TurnResolver.cs:115) is a second entry, firing before deaths resolve with no hero-alive guard |
| REL-42 | Medium | VERIFIED (lead) | Ground truth leaks through telegraphs drawn on covered tiles: `BoardView.cs:208` draws threats with no `Knowledge` gate and `GameScreen.cs:904` prints "Danger -n" over a tile INSPECT just called UNKNOWN. A truncated lane/charge (Board.cs:84-94,206-217) reveals a covered hazard or monster, and distinguishes a sleeping Mimic (stops a lane) from a chest (does not) |
| REL-43 | Medium | VERIFIED (lead) | `Dodge` ignores the `blockable` flag (Combat.cs:42-47) and so turns aside spikes, lava and pit-fall damage - the one category the rules say nothing blocks. Guard, Unyielding and Sanctuary all respect it |
| REL-44 | Medium | VERIFIED (lead) | `AutoPlayer.Copy` omits `RunState.Movement` and `RunState.Threat` (AutoPlayer.cs:456-492): the look-ahead scores every candidate under Free-Roam rules with zero renown. `CopySerializesExactlyLikeTheRun` cannot see it because its fixture leaves both at their defaults - the two values the copy drops |
| TEST-20 | Medium | CONFIRMED | One missing test shape would have caught REL-36, REL-37, REL-38 and REL-40 at once: `TheTelegraphIsWhatHappens` - snapshot `Threats.Compute` at the boundary, then assert every point of damage taken appears in that snapshot at the hero's tile and that no monster acted on an unmarked tile |
| TEST-21 | Medium | CONFIRMED | `AssertTheBotWasPlaying` is not wired into `TiersKeepTheirOrder` or the parity guard, and `Wins()` discards the stall count - the exact MAINT-15 pattern the helper was written for, now spanning eight classes |
| DATA-22 | Medium | CONFIRMED | `GameSession.ContentProblem` walks only `run.Floor.Enemies`, not `OuterFloor`/`VisitedVault`: a save taken inside a vault skips the content check and throws `KeyNotFoundException` from `LeaveVault` on the next resume. Latent until an id is renamed |
| MAINT-31 | Medium | CONFIRMED | Docs drift: rules 14 documents 24 of 96 talents; Dawnstrike reads "against Lord Blobert" while the effect is every boss (4x its value since D-062); checklist claims 137/199 tests against ~355/446 and still says "five-floor run"; rules 8.5 does not mention the Key Warden exemption the validator deliberately grants |
| MAINT-32 | Medium | CONFIRMED | HUD and INSPECT "SLASH n" ignore Rage/Ambush/Longshot; the hero's own tile is titled "SIR CLICKINGTON" for all nine heroes; the DASH hint promises two tiles to the three classes that dash one, whose refusal then reads "one or 1 tiles" |
| MAINT-33 | Medium | CONFIRMED | Telemetry records none of the 13 new event kinds (web, summon, charge, thrown bomb, vanish, enrage, collapse, reassemble, stagger, knockback, drone zap, dodge, key drop), so the playtest the kit exists for cannot measure the content it was built to measure |
| REL-45 | Low | DOWNGRADED (adversarial) | `DropKey`'s `Clear()` ignores actors and the hero (Combat.cs:91-92). Unreachable in shipped content: the validator keeps enemies off hazards/content, so the warden's own tile is always clear. Reachable only via a thrown bomb, since `CanHoldBomb` does not check actors |
| DATA-23 | Low | CONFIRMED | Depth achievements stop at "reach floor 5" of 20, and `CrownAndMailTests.cs:96` asserts `Target <= RunFloorCount` - an assertion that can no longer fail. Floors 6-20 carry no crown goal |
| DATA-24 | Low | DOWNGRADED (adversarial) | `SaveSystem` never ties `run.FloorCount` to `catalog.RunFloorCount`. The generation gate (Enums.cs:22, SaveSystem.cs:51) refuses any such save today, so the harm is a missing invariant test, not a live hazard |
| MAINT-34 | Low | CONFIRMED | `test-gate.ps1` floor is 280 against ~355 declared tests: 75 tests can be deleted and the kit still packages green |
| MAINT-35 | Low | CONFIRMED | `ApplyDifficulty` touches only HP/damage/slam, so a Cave Spider's web, every `SummonCount`, `Range`, `ThrowRange` and `ReassembleTurns` are identical on all three tiers; the Bat Leader also carries a phantom `SlamDamage` of 1 it never uses |
| MAINT-36 | Low | CONFIRMED | Renown was tuned for a five-floor run: `FirstFloor = 3`, `MaxThreat = 3`, so the threat is flat and identical from floor 3 to floor 20, with nothing left to answer a levelled hero in the back half |
| MAINT-37 | Low | CONFIRMED | Art: 72 `icon_talent_*` keys, 48 expression portraits and 3 enemy portraits (`crowned_slime`, `fire_imp`, `lord_blobert`) are wired with no file. Tree nodes fall back to a letter; the detail panel and hero-select cards draw empty discs; boss inspect shows a blank portrait |
| MAINT-38 | Low | CONFIRMED | An enraged boss's token never changes pose again (`ActorAnimations.Pose` returns "" for any non-Normal mode, so `BoardView`'s visual key stops changing); `-cdOverlay talents<unknown>` throws out of `TalentOverlay.ShowClass` |

### Rejected / downgraded this round

- **Warden within Manhattan 2 of the start** - FALSE POSITIVE as a rules break. `FloorValidator.cs:80` exempts
  `CarriesKey` deliberately, with the reason on the line above. Real residual: rules 8.5 was never amended (MAINT-31).
- **Hand-edited `FloorCount` wins the run early** - FALSE POSITIVE. The generation-version gate refuses the save
  first (DATA-24 keeps the missing-test residual).
- **`watch-bot.bat` sized for a five-floor dungeon** - WITHDRAWN. `-cdRuns 5` is five runs, and the per-run cap
  already scales with `RunFloorCount`. Residual is ergonomic only (its comment still claims a two-hero roster).
- **Dodge gives a free pit descent every floor** - DOWNGRADED. Falling forfeits that floor's key, chest, coins and
  XP, and pits are barred on all boss floors. REL-43 stands on the unblockable-damage half alone.
- **Unspent premium keys are lost** - FALSE POSITIVE: `ProfileSystem.cs:24` banks them back.
- **Ambush's `Threats.Compute` call is a performance risk** - REJECTED. It short-circuits for seven of eight classes
  and adds well under 1.3x for the Rogue; no re-entrancy (`Threats.Compute` never calls `SlashDamage`).
- **Mimic parity in the simulation** - CLEAN, re-verified command by command. The only divergences are REL-39 (UI)
  and REL-42 (lane truncation).
- **Ranged slashes and the cover rule** - CLEAN. `HeroHasShot` requires every intermediate tile Revealed, both
  refusal strings turn only on facts already on screen, and a sleeping mimic deliberately does not block a shot.
- **Perk lifecycle** - CLEAN. `ApplyClassTraits` and `Progression.Apply` have one caller each and cannot both run
  on a resume.

## Remediation graph

```
REL-44 (bot copy) ---------------------------------> TEST-17 (re-baseline parity, LAST)
                                                          ^
REL-41 --> DATA-21 (boss hoard) --------------------------|   both edit Combat.ResolveDeaths: SERIALIZE
REL-36 (freeze Fury at declare) --+                       |
REL-40 (summon cap) --------------+-- EnemyAi.cs: SERIALIZE
REL-37 (knockback vs declared line) ----------------------+
REL-38 --+- BoardView.DrawThreats: same function: SERIALIZE
REL-42 --+   (+ the GameScreen inspect danger line, separate)
REL-39 --+- GameScreen.InspectTile: same function: SERIALIZE
MAINT-32 -+
REL-43, DATA-22, DATA-21 (keys), MAINT-33, MAINT-34 -- independent, parallel-safe
TEST-18, TEST-19, TEST-20, TEST-21 -- after the rules they pin
MAINT-30, MAINT-31 (docs/kit) -- independent; ship-blockers for the kit
```

**Ship-blockers if the kit goes out as-is:** MAINT-30, REL-38, REL-39, DATA-21, REL-41.

## Coverage and limits

- Five component auditors plus one reconciliation/adversarial pass; every High re-traced by the lead against the
  source before entering this table. No build or test run by the auditors; the lead ran `ClassSweep` (the measured
  numbers behind TEST-17) and the headless suite earlier in the session (357 pass, 434 Unity).
- Not covered: the art-catalog builder itself, Android/iOS build paths, the `Sim/` telemetry reporter, PlayMode
  behaviour (no PlayMode assembly exists - TEST-15 from audit 3 is still the prerequisite for proving a Unity-layer
  fix), and the rendered pixels behind REL-38/REL-39 (traced in source only; Unity was not run).
- Prior audit-3 items remain open and were not re-checked this round.

## Remediation — 2026-09-22 (graphRepair, audit 4)

Nothing committed. Suites after the work: **377 headless**, **454 Unity EditMode**, 0 failures, run repeatedly.

Evidence rule for this table: **VERIFIED** means the fix was reverted and a named test went red. Fifteen mutations were
run in two sweeps; every one is caught. The first sweep caught only 4 of 10 — the six that were merely COMPILED are
listed with the test written for them.

| ID | Outcome | Evidence | What changed |
|---|---|---|---|
| REL-36 | FIXED | VERIFIED (`EnragingDoesNotRaiseABlowThatIsAlreadyDeclared`, `AnEnragedBlowCostsTheHeartsItsWarningPromised`) | `EnemyState.Enraging` banks the rage in `Combat.DamageEnemy`; `EnemyAi.Declare` applies it when the turn settles, so it can only raise blows declared after it. `IntentExplain` now adds Fury, so panel and tile band agree |
| REL-37 | FIXED (severity corrected to Medium) | VERIFIED (`ADeclaredLineIsTracedFromWhereItWasDeclared`, `AShovedShooterStillFiresDownTheLineItDrew`) | `Intent.Fire/Charge` carry the tile they were declared from; `EnemyAi.LineFrom` anchors both execution and telegraph there. **First attempt was wrong**: cancelling the shoved monster's intent skipped its winded turn and was invisible to the player (caught in review) |
| REL-38 | FIXED | COMPILED (no PlayMode assembly exists - TEST-15) | WEB/ARRIVES and BOMB draw beside a damage band, each on its own row via `BandY(enemyHere, hasDamage, row)`. The first attempt still collided the two zero-damage bands on one row (caught in review) |
| REL-39 | FIXED | COMPILED | `InspectTile` reads a sleeping mimic as CHEST with its tap count, word for word with a real chest; `Commands.TryContextual` routes the tap to Interact |
| REL-40 | FIXED | VERIFIED (`LordBlobertStopsSummoningWhenHisCourtIsFull`, `EverySummonedMinionStandsOnATileTheBoardMarked`, `ASummonNeverPutsOutMoreMinionsThanItsLimit`) | `SummonCells` clamps to the room the cap leaves; `Threats` derives its markers from that same list. **First attempt was REJECTED in review**: capping Blobert without gating his declare reopened REL-21 on the last boss - he now checks the cap like the other summoners |
| REL-41 | FIXED | VERIFIED (`AHeroKilledInTheSameStepThatFelledTheBossStillLosesTheRun`, `TheDroneNeverCarriesADeadHeroThroughABossFight`) | `ResolveDeaths` ends the run before the act-clear heal. The drone test was **vacuous** at first (no boss on the board) and was rewritten |
| REL-42 | FIXED | COMPILED | Board-dependent warnings (lane, charge, summon, arrival, web, thrown bomb) draw on uncovered ground only; geometric ones (attack, slam, blast) still draw everywhere. The first attempt gated every kind, which cost the player warnings they are owed (caught in review) |
| REL-43 | FIXED | VERIFIED (`SlipperyTurnsAsideABlowButNotSpikesOrAFall`) | `Dodge` tests `blockable` |
| REL-44 | FIXED | VERIFIED (`TheCopyCarriesEveryFieldARunHas`) | `AutoPlayer.Copy` carries `Movement` and `Threat`; the new test puts a non-default value in every field the look-ahead reads |
| REL-45 | FIXED | VERIFIED (`ADroppedKeyNeverLandsUnderAnActor`) | `DropKey`'s `Clear()` excludes tiles holding an actor or the hero |
| REL-46 (new) | FIXED | VERIFIED (`UnyieldingSoftensBlowsButNotSpikes`) | Found while reviewing REL-43: `Unyielding` softened spikes, lava and falls the same way Dodge did |
| DATA-21 | FIXED | VERIFIED (`AnActBossPaysAShareAndOnlyTheLastOnePaysTheHoard`, `OneRunCarriesAtMostTheRunsCeilingOfKeysAndBanksTheRest`) | Act bosses pay `GemsForAnActBoss`/`ForAnActBoss` (2/10) and no gear roll; Lord Blobert keeps the hoard (5/30/50%). Key carry capped by `PremiumChestsPerRun = 3`. The gear roll was missed on the first pass (caught in review) |
| DATA-22 | FIXED | VERIFIED (`AVaultSaveNamingAMonsterThisBuildLacksIsRefused`) | `ContentProblem` walks `Floor`, `OuterFloor` and `VisitedVault` |
| DATA-23 | FIXED | COMPILED | Depth achievements at floors 10, 15 and 20 |
| TEST-17 | FIXED | VERIFIED by construction | The parity band is the ratio alone (the absolute +8 was sized for a different sample and bound by accident), plus a new assertion that it never again passes sitting exactly on its ceiling |
| TEST-18 | FIXED | VERIFIED (`TalentWiringTests`, 3 tests) | All 96 talents pinned to effect/amount/ranks, their per-rank text checked against their number, and every one walked through the real `TryLearn`/`Apply` path |
| TEST-19 | FIXED | VERIFIED (`ARunCarriesItsClassRuleAndItsMonstersSecretsThroughASave`) | Perks, `CarriesKey`, `Disguised`, `DodgeSpent`, `Enraging` and enemy `Mode` read back one by one after a round trip |
| TEST-20 | FIXED | VERIFIED (`TheTelegraphIsWhatHappens`) | Plays 12 seeded bot runs (Ironheart and Emberwisp), snapshots `Threats.Compute` before each command and asserts every blow landed on a marked tile for the marked number; guards its own coverage on depth and on bosses having acted |
| TEST-21 | FIXED | COMPILED | `AssertTheBotWasPlaying` wired into `TiersKeepTheirOrder`; the parity guard counts stalls per class |
| MAINT-30 | FIXED | docs | The kit README describes twenty floors, four acts, nine heroes, the monsters testers will meet, and warns that not everything that looks like treasure is |
| MAINT-31 | FIXED | docs | All 96 talents documented (generated from the catalog); Dawnstrike reads "any boss"; rules 8.5 states the Key Warden exemption; checklist counts and claims corrected |
| MAINT-32 | FIXED | COMPILED | Hero tile titled by the playing hero; HUD shows the slash span a class rule produces; dash hints, the dash refusal and the help page all follow `DashDistance` |
| MAINT-33 | FIXED | COMPILED | Telemetry for 13 new event kinds |
| MAINT-34 | FIXED | VERIFIED (`TheKitGateAcceptsAFullRun`) | Gate floor 280 -> 340 |
| MAINT-38 | FIXED | VERIFIED (`AnimationsFallBackToSimilarOnesAndPosesFollowIntent`) | An enraged boss poses with its intent again; a puffed one is still drawn by its mode |
| MAINT-37 | FIXED (after the report) | VERIFIED in a built player | All 72 talent icons cut: each class's own affinity art, emblem and stat badges from its sheet, with the shared Core UI icons for the effects that are not class-specific - the pattern the Knight and Paladin trees already used. Holy Light shares Consecrate's icon, being the same effect |
| MAINT-39 (new) | FIXED | VERIFIED (icons drew as letters before, as art after) | The art catalog is rebuilt from an import callback deferred to `EditorApplication.delayCall`, which never runs in a `-quit` batch build: newly sliced art imported and then shipped missing from the catalog. Every build method rebuilds it first now |
| DATA-24, MAINT-35, MAINT-36 | NOT_FIXED | - | Left deliberately: the missing `FloorCount` invariant test, difficulty tiers that do not touch the new monsters' non-damage numbers, and renown tuned for a five-floor run. All three are design calls |

### Corrections to the audit made while fixing it

- **REL-37 was overstated.** A knockback is collinear with hero->target, so the re-aimed line is the drawn line extended
  backwards through the tile the monster has just vacated - which the hero cannot occupy that turn. The hero's exposure
  was unchanged, and "the far end becomes safe" was wrong. The invariant break is real and was fixed at Medium.
- **The first regression test for it could not fail**, because it was written against that symptom. It now asserts the
  invariant directly: displace a monster after it declares, and the lane still resolves from where it was drawn.

### Measured after the repairs (240 blind seeds, `DifficultySweep`)

| Player | Squire's Stroll | Knight's Trial | Blobert's Wrath |
|---|---|---|---|
| casual | 100% | 62% | 52% |
| novice | 100% | 12% | 8% |

No stalls. Knight's Trial and Blobert's Wrath are now close for a blind novice; `TiersKeepTheirOrder` asserts the
ordering is not inverted and leaves the separation to the sweep, because 40 seeds cannot resolve 12% against 8%.
Per class, casual/novice of 40: knight 25/4, paladin 22/8, rogue 20/1, wizard 25/3, ranger 23/6, cleric 28/8,
berserker 21/8, engineer 20/5.

### Next order

1. **The three design calls above** (DATA-24, MAINT-35, MAINT-36) - renown in particular: it is flat from floor 3 to 20.
2. **A PlayMode assembly** (TEST-15, open since audit 3). REL-38, REL-39, REL-42 and MAINT-32 are all COMPILED-level
   because nothing in the repo can exercise a drawn tile; they were verified by reading the draw path only.
3. 48 expression portraits are still wired with no file (the heroes fall back to their neutral face).
4. Audit 3's open items remain open.
