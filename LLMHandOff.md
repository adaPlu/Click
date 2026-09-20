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

