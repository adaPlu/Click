using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using ClickDungeon.Content;
using ClickDungeon.Domain;
using ClickDungeon.Simulation;
using Newtonsoft.Json;

namespace ClickDungeon.Application
{
    /// <summary>One JSON line of playtest telemetry (decision D-015). Never read by simulation.</summary>
    public sealed class TelemetryEvent
    {
        static readonly JsonSerializerSettings Settings = new JsonSerializerSettings { Formatting = Formatting.None };

        [JsonProperty("time")] public string Time;
        [JsonProperty("session")] public string Session;
        [JsonProperty("event")] public string Name;
        [JsonProperty("run")] public ulong Run;
        [JsonProperty("floor")] public int Floor;
        [JsonProperty("turn")] public int Turn;
        [JsonProperty("data")] public Dictionary<string, object> Data = new Dictionary<string, object>();

        public string ToJson() => JsonConvert.SerializeObject(this, Settings);
    }

    public interface ITelemetrySink
    {
        void Write(TelemetryEvent telemetryEvent);
    }

    public sealed class MemoryTelemetrySink : ITelemetrySink
    {
        public readonly List<TelemetryEvent> Events = new List<TelemetryEvent>();

        public void Write(TelemetryEvent telemetryEvent) => Events.Add(telemetryEvent);

        public List<TelemetryEvent> Named(string name) => Events.FindAll(e => e.Name == name);
    }

    /// <summary>Appends JSON lines to a local file, one file per app session. Nothing is sent over the network.</summary>
    public sealed class JsonlTelemetrySink : ITelemetrySink, IDisposable
    {
        readonly StreamWriter _writer;

        public JsonlTelemetrySink(string directory, DateTime startedUtc)
        {
            Directory.CreateDirectory(directory);
            FilePath = Path.Combine(directory, $"session-{startedUtc:yyyyMMdd-HHmmss}-{Guid.NewGuid().ToString("N").Substring(0, 6)}.jsonl");
            var stream = new FileStream(FilePath, FileMode.Append, FileAccess.Write, FileShare.Read);
            _writer = new StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = true };
        }

        public string FilePath { get; }

        public void Write(TelemetryEvent telemetryEvent) => _writer.WriteLine(telemetryEvent.ToJson());

        public void Dispose() => _writer.Dispose();
    }

    /// <summary>
    /// Turns simulation results into playtest telemetry. Reads state before a command (to capture what the
    /// player could see when deciding) and the CommandResult after it. It never mutates gameplay state, and a
    /// telemetry failure never interrupts play.
    /// </summary>
    public sealed class TelemetryRecorder
    {
        public const int SchemaVersion = 1;

        public sealed class PendingCommand
        {
            internal PlayerCommand Command;
            internal FloorState FloorBefore;
            internal int FloorIndex;
            internal int Turn;
            internal int HeroHp;
            internal GridPos From;
            internal int ThreatHere;
            internal Dictionary<string, object> Hero;
            internal List<Dictionary<string, object>> Intents;
            internal Dictionary<string, object> Choice;
        }

        readonly ITelemetrySink _sink;
        readonly ContentCatalog _catalog;
        readonly Func<DateTime> _clock;
        int _floorStartTurn = -1;

        public TelemetryRecorder(ITelemetrySink sink, ContentCatalog catalog, Func<DateTime> clock = null, string sessionId = null)
        {
            _sink = sink;
            _catalog = catalog;
            _clock = clock ?? (() => DateTime.UtcNow);
            SessionId = sessionId ?? Guid.NewGuid().ToString("N").Substring(0, 12);
        }

        public string SessionId { get; }

        /// <summary>Number of telemetry calls that failed and were swallowed.</summary>
        public int Failures { get; private set; }

        public void RunStarted(RunState run, List<GameEvent> events)
        {
            Guard(() =>
            {
                Emit("run_started", run, run.Floor.FloorIndex, run.Turn, D(
                    "schema", SchemaVersion,
                    "hero", run.Hero.IdentityId,
                    "class", run.Hero.ClassId,
                    "ruleset", run.RulesetVersion,
                    "content", run.ContentCatalogVersion,
                    "generation", run.GenerationVersion,
                    "floors", run.FloorCount));
                MapEvents(run, events, null);
            });
        }

        public void RunResumed(RunState run)
        {
            Guard(() =>
            {
                _floorStartTurn = -1;
                Emit("run_resumed", run, run.Floor.FloorIndex, run.Turn, D("schema", SchemaVersion, "hero", HeroData(run.Hero)));
            });
        }

        public void RunAbandoned(RunState run)
        {
            Guard(() => Emit("run_abandoned", run, run.Floor.FloorIndex, run.Turn, D("hero", HeroData(run.Hero), "rewards", run.Rewards.Count)));
        }

        /// <summary>Snapshot decision context before the command mutates state. Returns null when disabled.</summary>
        public PendingCommand Begin(RunState run, PlayerCommand command)
        {
            if (_sink == null || run == null) return null;
            try
            {
                var threats = Threats.Compute(run, _catalog);
                var pending = new PendingCommand
                {
                    Command = command,
                    FloorBefore = run.Floor,
                    FloorIndex = run.Floor.FloorIndex,
                    Turn = run.Turn,
                    HeroHp = run.Hero.Hp,
                    From = run.Hero.Pos,
                    ThreatHere = Threats.DamageAt(threats, run.Hero.Pos),
                    Hero = HeroData(run.Hero),
                    Intents = NearbyIntents(run),
                };
                bool movement = command.Kind == CommandKind.Move || command.Kind == CommandKind.Dash;
                if (movement && Commands.Validate(run, command, _catalog, out _)) pending.Choice = TileChoice(run, command, threats);
                return pending;
            }
            catch (Exception)
            {
                Failures++;
                return null;
            }
        }

        public void Complete(PendingCommand pending, RunState run, CommandResult result)
        {
            if (pending == null || result == null) return;
            Guard(() =>
            {
                var command = pending.Command;
                if (!result.Accepted)
                {
                    Emit("command_rejected", run, pending.FloorIndex, pending.Turn,
                        D("command", Lower(command.Kind), "target", Cell(command.Target), "reason", result.RejectReason));
                    return;
                }

                if (pending.Choice != null)
                {
                    var data = new Dictionary<string, object>(pending.Choice) { ["hero"] = pending.Hero, ["intents"] = pending.Intents };
                    Emit("tile_choice", run, pending.FloorIndex, pending.Turn, data);
                }
                if (command.Kind == CommandKind.Move || command.Kind == CommandKind.Dash)
                {
                    Emit("player_move", run, pending.FloorIndex, pending.Turn,
                        D("from", Cell(pending.From), "to", Cell(command.Target), "via", command.Kind == CommandKind.Dash ? "dash" : "walk"));
                }
                if (command.Kind != CommandKind.Move)
                {
                    Emit("ability_used", run, pending.FloorIndex, pending.Turn, D(
                        "ability", Lower(command.Kind),
                        "target", Cell(command.Target),
                        "threat_here", pending.ThreatHere,
                        "hero", pending.Hero,
                        "intents", pending.Intents));
                }

                MapEvents(run, result.Events, pending);
            });
        }

        // ------------------------------------------------------------------ event mapping

        void MapEvents(RunState run, List<GameEvent> events, PendingCommand pending)
        {
            if (events == null) return;
            var floorState = pending != null ? pending.FloorBefore : run.Floor;
            int floor = floorState.FloorIndex;
            int turn = pending != null ? pending.Turn : run.Turn;
            int hp = pending != null ? pending.HeroHp : run.Hero.Hp;
            string lastDamageSource = null;
            var lastHitBy = new Dictionary<int, string>();

            foreach (var e in events)
            {
                switch (e.Kind)
                {
                    case GameEventKind.FloorStarted:
                        floorState = run.Floor;
                        floor = run.Floor.FloorIndex;
                        turn = run.Turn;
                        _floorStartTurn = run.Turn;
                        Emit("floor_started", run, floor, turn, FloorData(run.Floor));
                        break;
                    case GameEventKind.CellSensed:
                        Emit("tile_sensed", run, floor, turn, D("cell", Cell(e.To), "clue", ClueNames(e.Clue)));
                        break;
                    case GameEventKind.CellRevealed:
                        Emit("tile_revealed", run, floor, turn, RevealData(floorState, e.To));
                        break;
                    case GameEventKind.HeroDamaged:
                        hp = Math.Max(0, hp - e.Amount);
                        lastDamageSource = e.Source;
                        Emit("damage_taken", run, floor, turn, D("amount", e.Amount, "source", e.Source, "hp_after", hp, "cell", Cell(e.To)));
                        break;
                    case GameEventKind.HeroBlocked:
                        Emit("damage_blocked", run, floor, turn, D("amount", e.Amount, "source", e.Source));
                        break;
                    case GameEventKind.HeroHealed:
                        hp += e.Amount;
                        Emit("healed", run, floor, turn, D("amount", e.Amount, "hp_after", hp));
                        break;
                    case GameEventKind.EnemyWoke:
                        Emit("enemy_woke", run, floor, turn, D("enemy", e.Source, "cell", Cell(e.To)));
                        break;
                    case GameEventKind.EnemyDamaged:
                        lastHitBy[e.ActorId] = e.Source;
                        break;
                    case GameEventKind.EnemyDied:
                        Emit("enemy_defeated", run, floor, turn, D(
                            "enemy", e.Source,
                            "cell", Cell(e.To),
                            "by", lastHitBy.TryGetValue(e.ActorId, out var by) ? by : "boss_defeated"));
                        break;
                    case GameEventKind.SpikesTriggered:
                        Emit("trap_triggered", run, floor, turn, D("trap", "spikes", "cell", Cell(e.To), "damage", e.Amount));
                        break;
                    case GameEventKind.BombExploded:
                        Emit("trap_triggered", run, floor, turn, D("trap", "bomb", "cell", Cell(e.To), "damage", e.Amount));
                        break;
                    case GameEventKind.BombArmed:
                        Emit("trap_armed", run, floor, turn, D("trap", "bomb", "cell", Cell(e.To), "by", ArmedBy(pending, e.To)));
                        break;
                    case GameEventKind.KeyCollected:
                        Emit("pickup_collected", run, floor, turn, D("item", "key", "cell", Cell(e.To)));
                        break;
                    case GameEventKind.PotionCollected:
                        Emit("pickup_collected", run, floor, turn, D("item", "potion", "cell", Cell(e.To)));
                        break;
                    case GameEventKind.ChestOpened:
                        if (e.Reward != null && e.Reward.Kind == RewardKind.MaxHp) hp += e.Reward.Amount;
                        Emit("chest_opened", run, floor, turn, D(
                            "cell", Cell(e.To),
                            "reward", e.Reward != null ? Lower(e.Reward.Kind) : null,
                            "amount", e.Amount,
                            "transaction", e.Reward?.TransactionId));
                        break;
                    case GameEventKind.ExitUnlocked:
                        Emit("exit_unlocked", run, floor, turn, D("cell", Cell(e.To)));
                        break;
                    case GameEventKind.FloorCompleted:
                        Emit("floor_completed", run, floor, turn, D(
                            "turns_on_floor", _floorStartTurn >= 0 ? (object)(turn + 1 - _floorStartTurn) : null,
                            "hp", hp,
                            "potions", run.Hero.Potions,
                            "template", floorState.TemplateId));
                        foreach (var p in Board.AllCells)
                        {
                            var cell = floorState[p];
                            if (cell.IsClosedChest)
                                Emit("optional_reward_skipped", run, floor, turn, D("reward", "chest", "cell", Cell(p), "knowledge", Lower(cell.Knowledge)));
                            else if (cell.Content == ContentKind.Potion)
                                Emit("optional_reward_skipped", run, floor, turn, D("reward", "potion", "cell", Cell(p), "knowledge", Lower(cell.Knowledge)));
                        }
                        break;
                    case GameEventKind.RunWon:
                        Emit("run_completed", run, floor, turn, D("turns", run.Turn, "hp", hp, "potions", run.Hero.Potions, "rewards", run.Rewards.Count));
                        break;
                    case GameEventKind.RunLost:
                        Emit("run_failed", run, floor, turn, D("cause", lastDamageSource, "turns", run.Turn, "hp", hp, "potions", run.Hero.Potions, "rewards", run.Rewards.Count));
                        break;
                }
            }
        }

        // ------------------------------------------------------------------ decision context

        Dictionary<string, object> TileChoice(RunState run, PlayerCommand command, List<Threat> threats)
        {
            var floor = run.Floor;
            var heroClass = _catalog.HeroClass(run.Hero.ClassId);
            var exitField = floor.Exit.InBounds
                ? Pathfinding.DistanceField(new[] { floor.Exit }, p => !Board.BlocksMovement(floor[p]))
                : null;

            var options = new List<Dictionary<string, object>>();
            var signatures = new HashSet<string>();
            int chosenIndex = -1;
            foreach (var cell in Commands.LegalTargets(run, command.Kind, _catalog))
            {
                if (cell == command.Target) chosenIndex = options.Count;
                options.Add(Option(run, cell, threats, heroClass, exitField, out var signature));
                signatures.Add(signature);
            }

            return D(
                "kind", command.Kind == CommandKind.Dash ? "dash" : "walk",
                "options", options,
                "chosen", Cell(command.Target),
                "chosen_index", chosenIndex,
                "consequential", options.Count >= 2 && signatures.Count >= 2);
        }

        /// <summary>
        /// Features of one legal destination, limited to what the player could know: telegraphed threat,
        /// revealed hazards and pickups, and the clues of cells that stepping there would reveal.
        /// </summary>
        Dictionary<string, object> Option(RunState run, GridPos cell, List<Threat> threats, HeroClassDefinition heroClass,
            int[] exitField, out string signature)
        {
            var floor = run.Floor;
            var state = floor[cell];
            bool revealed = state.Knowledge == Knowledge.Revealed;
            int threat = Threats.DamageAt(threats, cell);
            int enterCost = revealed && state.Hazard == HazardKind.Spikes ? _catalog.Hazards.SpikeDamage : 0;
            bool armsBomb = revealed && state.Hazard == HazardKind.Bomb && !state.BombArmed;
            string pickup = !revealed ? null
                : state.Content == ContentKind.Key ? "key"
                : state.Content == ContentKind.Potion ? "potion" : null;

            int enemy = 0, danger = 0, treasure = 0, objective = 0, unknown = 0;
            foreach (var q in Board.AllCells)
            {
                if (q.Manhattan(cell) > heroClass.RevealRadius) continue;
                var qs = floor[q];
                if (qs.Terrain != Terrain.Floor || qs.Knowledge == Knowledge.Revealed) continue;
                if (qs.Knowledge == Knowledge.Unseen)
                {
                    unknown++;
                    continue;
                }
                var clue = Board.ClueAt(floor, q);
                if ((clue & Clue.Enemy) != 0) enemy++;
                if ((clue & Clue.Danger) != 0) danger++;
                if ((clue & Clue.Treasure) != 0) treasure++;
                if ((clue & Clue.Objective) != 0) objective++;
            }

            int exitDistance = exitField == null || exitField[cell.Index] == Pathfinding.Unreachable ? -1 : exitField[cell.Index];
            signature = string.Join("|", threat, enterCost, armsBomb, pickup, state.IsExit, enemy, danger, treasure, objective, unknown > 0);

            return D(
                "cell", Cell(cell),
                "knowledge", Lower(state.Knowledge),
                "clue", state.Knowledge == Knowledge.Sensed ? ClueNames(Board.ClueAt(floor, cell)) : null,
                "threat", threat,
                "enter_cost", enterCost,
                "arms_bomb", armsBomb,
                "pickup", pickup,
                "exit", state.IsExit,
                "known_enemy", enemy,
                "known_danger", danger,
                "known_treasure", treasure,
                "known_objective", objective,
                "unknown_cells", unknown,
                "exit_distance", exitDistance);
        }

        static Dictionary<string, object> HeroData(HeroState hero) => D(
            "cell", Cell(hero.Pos),
            "hp", hero.Hp,
            "max_hp", hero.MaxHp,
            "potions", hero.Potions,
            "slash", hero.SlashDamage,
            "shield_cd", hero.ShieldCooldown,
            "dash_cd", hero.DashCooldown,
            "has_key", hero.HasKey);

        static List<Dictionary<string, object>> NearbyIntents(RunState run)
        {
            var list = new List<Dictionary<string, object>>();
            foreach (var enemy in run.Floor.Enemies)
            {
                if (!enemy.Awake || enemy.Pos.Manhattan(run.Hero.Pos) > 3) continue;
                list.Add(D(
                    "enemy", enemy.DefId,
                    "cell", Cell(enemy.Pos),
                    "hp", enemy.Hp,
                    "intent", Lower(enemy.Intent.Kind),
                    "target", Cell(enemy.Intent.Target),
                    "dir", enemy.Intent.Kind == IntentKind.Fire ? Lower(enemy.Intent.Dir) : null,
                    "mode", enemy.Mode == EnemyMode.Normal ? null : Lower(enemy.Mode)));
            }
            return list;
        }

        static Dictionary<string, object> FloorData(FloorState floor)
        {
            int spikes = 0, bombs = 0, chests = 0, potions = 0;
            foreach (var cell in floor.Cells)
            {
                if (cell.Hazard == HazardKind.Spikes) spikes++;
                if (cell.Hazard == HazardKind.Bomb) bombs++;
                if (cell.Content == ContentKind.Chest) chests++;
                if (cell.Content == ContentKind.Potion) potions++;
            }
            var enemies = new List<string>();
            foreach (var enemy in floor.Enemies) enemies.Add(enemy.DefId);
            return D(
                "template", floor.TemplateId,
                "transform", floor.Transform,
                "attempt", floor.AttemptIndex,
                "boss", floor.IsBossFloor,
                "enemies", enemies,
                "spikes", spikes,
                "bombs", bombs,
                "chests", chests,
                "potions", potions,
                "start", Cell(floor.Start),
                "exit", Cell(floor.Exit));
        }

        static Dictionary<string, object> RevealData(FloorState floor, GridPos cell)
        {
            if (!cell.InBounds) return D("cell", null);
            var state = floor[cell];
            return D(
                "cell", Cell(cell),
                "terrain", Lower(state.Terrain),
                "hazard", state.Hazard == HazardKind.None ? null : Lower(state.Hazard),
                "content", state.Content == ContentKind.None ? null : Lower(state.Content),
                "exit", state.IsExit,
                "enemy", floor.EnemyAt(cell)?.DefId);
        }

        static string ArmedBy(PendingCommand pending, GridPos cell)
        {
            if (pending == null) return "unknown";
            var command = pending.Command;
            if (command.Target == cell && command.Kind == CommandKind.Slash) return "slash";
            if (command.Target == cell && (command.Kind == CommandKind.Move || command.Kind == CommandKind.Dash)) return "step";
            return "blast";
        }

        // ------------------------------------------------------------------ helpers

        void Emit(string name, RunState run, int floor, int turn, Dictionary<string, object> data)
        {
            if (_sink == null) return;
            _sink.Write(new TelemetryEvent
            {
                Time = _clock().ToString("o"),
                Session = SessionId,
                Name = name,
                Run = run.RunSeed,
                Floor = floor,
                Turn = turn,
                Data = data,
            });
        }

        void Guard(Action action)
        {
            if (_sink == null) return;
            try
            {
                action();
            }
            catch (Exception)
            {
                Failures++;
            }
        }

        static Dictionary<string, object> D(params object[] pairs)
        {
            var data = new Dictionary<string, object>();
            for (int i = 0; i + 1 < pairs.Length; i += 2) data[(string)pairs[i]] = pairs[i + 1];
            return data;
        }

        static string Cell(GridPos p) => p.InBounds ? $"{p.X},{p.Y}" : null;

        static string Lower(Enum value) => value.ToString().ToLowerInvariant();

        static List<string> ClueNames(Clue clue)
        {
            var names = new List<string>();
            foreach (var flag in new[] { Clue.Enemy, Clue.Danger, Clue.Objective, Clue.Treasure, Clue.Safe })
                if ((clue & flag) != 0) names.Add(Lower(flag));
            return names;
        }
    }
}
