# ClickDungeon — Gate 2 Playtest Guide (facilitator)

Question being tested: **can a 5×5 board repeatedly produce understandable, meaningful risk/reward decisions?**
Telemetry details: `docs/telemetry.md`.

## 1. Make the kit

1. In Unity: **ClickDungeon → Build Windows**.
2. Package it:
   ```bash
   powershell -ExecutionPolicy Bypass -File tools/playtest-kit/make-kit.ps1
   ```
3. Hand out `ClickDungeon/Builds/Playtest/ClickDungeon-Playtest-<date>-<version>.zip`.
   It contains the game, `PLAYTEST-README.txt`, `collect-logs.bat` and `VERSION.txt`.

Commit before packaging, so `VERSION.txt` names a real commit instead of `-dirty`.

## 2. Who and how many

- At least **5 players**, ideally 8. Mix people who play roguelikes or tactics games with people who don't.
- Nobody who has seen the design docs or watched you build it.
- **3 runs each**, about 30–40 minutes per session.

## 3. Session script

**Before (2 min).** Say: *"We're testing the game, not you. Please think out loud: say what you notice and
why you pick a tile. I can't help while you play, but ask anything afterwards."* Don't explain the rules;
the in-game help is part of the test.

**During (20–30 min).** Stay quiet. If they're stuck for over a minute, answer with a question:
*"What do you think that icon means?"* Note on the observation sheet:
- moments they hesitate over which tile to pick
- anything they misread (icons, `-N` tiles, intents, locked exit)
- deaths: what they *say* killed them vs what the WHAT HAPPENED log says
- whether Shield and Dash come up on their own
- whether they detour for chests or ignore them

**After each run (2 min).** Ask:
1. "What killed you?" (or "What was hardest?") Can they explain it?
2. "Show me one tile you chose on purpose. Why that one?"
3. "Was there a moment you took a risk for a reward?"
4. "Did anything feel unfair or random?"

**After the session (5 min).**
5. "What were the clue icons telling you?"
6. "When did you use Shield or Dash? Why then?"
7. "Would you play another run right now?" (a 1–5 rating, then why)

## 4. Observation sheet (copy per player)

| Run | Floor reached | Death cause (player's words) | Log's cause | Confusions | Deliberate risk moments | Quotes |
|---|---|---|---|---|---|---|
| 1 | | | | | | |
| 2 | | | | | | |
| 3 | | | | | | |

Replay rating (1–5): ___ Uses Shield/Dash unprompted: yes / no

## 5. Collect the logs

- **On your own machine:** logs are already in `%USERPROFILE%\AppData\LocalLow\Clickd\ClickDungeon\telemetry`.
- **Testers:** they run `collect-logs.bat` and send back `ClickDungeon-playtest-logs.zip`.
- Unzip every tester's logs into one folder, e.g. `playtest-logs/<player>/`.
  Keep the logs outside the git repo.

## 6. Summarize

```bash
dotnet run --project Sim/ClickDungeon.Telemetry.Report -- playtest-logs/<player1> playtest-logs/<player2> --out playtest-summary.md
```

Or copy all `.jsonl` files into your own telemetry folder and use **ClickDungeon → Telemetry → Summarize Logs**.

## 7. Decide

| Signal | Go to Gate 3 | Tune rules first |
|---|---|---|
| Consequential choices consistent with visible information | ≈70% or more | well below 70% |
| Interview question 2 | most players give a real reason | "I just guessed" is common |
| Death explanations match the log | mostly | players blame randomness |
| Chests | opened on purpose, sometimes skipped on purpose | always ignored, or always grabbed without thought |
| Shield/Dash under a telegraphed hit | used regularly | rarely, or only by accident |
| Replay rating | mostly 4–5 | mostly 1–3 |

If most signals say "tune": don't add art. Revisit the watch list in `docs/checklist.md` first
(sensing radius, bomb and Slam escapes, spikes, the goblin dance), change one thing, and test again.
