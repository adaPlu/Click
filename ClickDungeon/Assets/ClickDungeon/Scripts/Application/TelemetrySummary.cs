using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using ClickDungeon.Simulation;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ClickDungeon.Application
{
    /// <summary>
    /// Aggregates playtest JSONL logs into the numbers the Gate 2 fun test asks about.
    /// "Informed" is a proxy: a consequential choice consistent with visible information.
    /// Confirm it with player interviews.
    /// </summary>
    public sealed class TelemetrySummary
    {
        public int Lines;
        public int MalformedLines;
        public readonly HashSet<string> Sessions = new HashSet<string>();
        public readonly SortedDictionary<string, int> EventCounts = new SortedDictionary<string, int>();

        public int RunsStarted, RunsResumed, RunsCompleted, RunsFailed, RunsAbandoned;
        public readonly Dictionary<string, int> DeepestFloorByRun = new Dictionary<string, int>();
        public readonly SortedDictionary<string, int> DeathCauses = new SortedDictionary<string, int>();
        public readonly SortedDictionary<string, int> DamageBySource = new SortedDictionary<string, int>();
        public int FloorsCompleted, FloorsWithTurnCount, FloorTurnsTotal;

        public int TileChoices, ConsequentialChoices, InformedChoices;
        public int ThreatVariedChoices, ChoseLowestThreat, SteppedIntoAvoidableDamage;
        public int KnowinglyWokeEnemy, KnowinglyWokeEnemyAtLowHp;
        public int TreasureOnOffer, TookTreasure;
        public int PaidSpikesOnPurpose;

        public int Shields, ShieldsUnderThreat, Dashes, DashesUnderThreat, PotionsUsed, Waits;
        public int ChestsOpened, ChestRewards, RewardsSkipped, CommandsRejected;

        public static TelemetrySummary FromLines(IEnumerable<string> lines)
        {
            var summary = new TelemetrySummary();
            foreach (var line in lines) summary.Add(line);
            return summary;
        }

        public static TelemetrySummary FromFiles(IEnumerable<string> paths)
        {
            var summary = new TelemetrySummary();
            foreach (var path in paths)
            foreach (var line in File.ReadLines(path))
                summary.Add(line);
            return summary;
        }

        public void Add(string line)
        {
            if (string.IsNullOrWhiteSpace(line)) return;
            Lines++;
            JObject o;
            try
            {
                o = JObject.Parse(line);
            }
            catch (JsonException)
            {
                MalformedLines++;
                return;
            }

            var name = (string)o["event"] ?? "unknown";
            Bump(EventCounts, name);
            var session = (string)o["session"] ?? "?";
            Sessions.Add(session);
            var runKey = session + ":" + o["run"];
            int floor = (int?)o["floor"] ?? 0;
            if (!DeepestFloorByRun.TryGetValue(runKey, out var deepest) || floor > deepest) DeepestFloorByRun[runKey] = floor;
            var data = o["data"] as JObject ?? new JObject();

            switch (name)
            {
                case "run_started": RunsStarted++; break;
                case "run_resumed": RunsResumed++; break;
                case "run_completed": RunsCompleted++; break;
                case "run_abandoned": RunsAbandoned++; break;
                case "run_failed":
                    RunsFailed++;
                    Bump(DeathCauses, (string)data["cause"] ?? "unknown");
                    break;
                case "damage_taken":
                    Bump(DamageBySource, (string)data["source"] ?? "unknown", Int(data, "amount"));
                    break;
                case "floor_completed":
                    FloorsCompleted++;
                    var turns = (int?)data["turns_on_floor"];
                    if (turns.HasValue)
                    {
                        FloorsWithTurnCount++;
                        FloorTurnsTotal += turns.Value;
                    }
                    break;
                case "chest_opened":
                    // One event per reward; only a chest's first draw keeps the chest's own transaction id (D-022).
                    ChestRewards++;
                    if (Chests.IsFirstDraw((string)o["data"]?["transaction"])) ChestsOpened++;
                    break;
                case "optional_reward_skipped": RewardsSkipped++; break;
                case "command_rejected": CommandsRejected++; break;
                case "ability_used":
                    CountAbility(data);
                    break;
                case "tile_choice":
                    CountChoice(data);
                    break;
            }
        }

        void CountAbility(JObject data)
        {
            bool threatened = Int(data, "threat_here") > 0;
            switch ((string)data["ability"])
            {
                case "shield":
                    Shields++;
                    if (threatened) ShieldsUnderThreat++;
                    break;
                case "dash":
                    Dashes++;
                    if (threatened) DashesUnderThreat++;
                    break;
                case "potion":
                    PotionsUsed++;
                    break;
                case "wait":
                    Waits++;
                    break;
            }
        }

        void CountChoice(JObject data)
        {
            TileChoices++;
            if (!((bool?)data["consequential"] ?? false)) return;
            ConsequentialChoices++;

            var options = data["options"] as JArray;
            int chosenIndex = (int?)data["chosen_index"] ?? -1;
            if (options == null || chosenIndex < 0 || chosenIndex >= options.Count) return;
            var chosen = options[chosenIndex];
            bool informed = false;
            bool blunder = false;

            int minThreat = options.Min(t => Int(t, "threat"));
            int maxThreat = options.Max(t => Int(t, "threat"));
            if (minThreat != maxThreat)
            {
                ThreatVariedChoices++;
                if (Int(chosen, "threat") == minThreat)
                {
                    ChoseLowestThreat++;
                    informed = true;
                }
                else if (minThreat == 0)
                {
                    SteppedIntoAvoidableDamage++;
                    blunder = true;
                }
            }

            if (Int(chosen, "known_enemy") > 0)
            {
                KnowinglyWokeEnemy++;
                informed = true;
                var hero = data["hero"];
                if (hero != null && Int(hero, "hp") * 2 <= Int(hero, "max_hp")) KnowinglyWokeEnemyAtLowHp++;
            }

            if (options.Any(HasTreasure) && !options.All(HasTreasure))
            {
                TreasureOnOffer++;
                if (HasTreasure(chosen))
                {
                    TookTreasure++;
                    informed = true;
                }
            }

            if (Int(chosen, "enter_cost") > 0 && options.Any(t => Int(t, "enter_cost") == 0))
            {
                PaidSpikesOnPurpose++;
                informed = true;
            }

            var distances = options.Select(t => Int(t, "exit_distance")).Where(d => d >= 0).ToList();
            int chosenDistance = Int(chosen, "exit_distance");
            if (distances.Count > 1 && distances.Min() != distances.Max() && chosenDistance == distances.Min()) informed = true;
            if ((bool?)chosen["exit"] == true) informed = true;

            if (informed && !blunder) InformedChoices++;
        }

        static bool HasTreasure(JToken option) =>
            Int(option, "known_treasure") > 0 || Int(option, "known_objective") > 0 || option["pickup"]?.Type == JTokenType.String;

        static int Int(JToken token, string key) => (int?)token[key] ?? 0;

        static void Bump<T>(IDictionary<T, int> table, T key, int amount = 1)
        {
            table.TryGetValue(key, out var value);
            table[key] = value + amount;
        }

        static string Pct(int part, int whole) => whole == 0 ? "-" : $"{100.0 * part / whole:0}%";

        static string Join(IEnumerable<KeyValuePair<string, int>> pairs) =>
            pairs.Any() ? string.Join(", ", pairs.OrderByDescending(p => p.Value).Select(p => $"{p.Key} {p.Value}")) : "none";

        public string ToMarkdown()
        {
            var sb = new StringBuilder();
            sb.AppendLine("# ClickDungeon playtest telemetry summary");
            sb.AppendLine();
            sb.AppendLine($"Sessions: {Sessions.Count} | Events: {Lines} | Malformed lines: {MalformedLines}");
            sb.AppendLine();

            sb.AppendLine("## Runs");
            sb.AppendLine();
            sb.AppendLine("| Started | Resumed | Completed | Failed | Abandoned |");
            sb.AppendLine("|---|---|---|---|---|");
            sb.AppendLine($"| {RunsStarted} | {RunsResumed} | {RunsCompleted} | {RunsFailed} | {RunsAbandoned} |");
            sb.AppendLine();
            var floors = DeepestFloorByRun.Values.GroupBy(f => f).OrderBy(g => g.Key).Select(g => $"floor {g.Key}: {g.Count()}");
            sb.AppendLine($"- Deepest floor reached per run: {(DeepestFloorByRun.Count == 0 ? "none" : string.Join(", ", floors))}");
            sb.AppendLine($"- Deaths by cause: {Join(DeathCauses)}");
            sb.AppendLine($"- Damage taken by source: {Join(DamageBySource)}");
            string averageTurns = FloorsWithTurnCount == 0 ? "-" : $"{(double)FloorTurnsTotal / FloorsWithTurnCount:0.0}";
            sb.AppendLine($"- Floors completed: {FloorsCompleted} (average turns per floor: {averageTurns})");
            sb.AppendLine();

            sb.AppendLine("## Decisions: are choices informed?");
            sb.AppendLine();
            sb.AppendLine($"- Tile choices: {TileChoices}; consequential (options differed in visible risk or reward): {ConsequentialChoices} ({Pct(ConsequentialChoices, TileChoices)})");
            sb.AppendLine($"- **Consequential choices consistent with visible information: {InformedChoices} ({Pct(InformedChoices, ConsequentialChoices)})**. Gate 2 target is about 70%; confirm with interviews.");
            sb.AppendLine($"- Options differed in telegraphed damage: {ThreatVariedChoices}; picked a lowest-damage tile: {ChoseLowestThreat} ({Pct(ChoseLowestThreat, ThreatVariedChoices)}); stepped into damage while a safe tile existed: {SteppedIntoAvoidableDamage}");
            sb.AppendLine($"- Knowingly woke an enemy: {KnowinglyWokeEnemy} (at half HP or less: {KnowinglyWokeEnemyAtLowHp})");
            sb.AppendLine($"- Treasure or key on offer on only some options: {TreasureOnOffer}; went for it: {TookTreasure} ({Pct(TookTreasure, TreasureOnOffer)})");
            sb.AppendLine($"- Paid spike damage when a spike-free tile existed: {PaidSpikesOnPurpose}");
            sb.AppendLine();

            sb.AppendLine("## Abilities and rewards");
            sb.AppendLine();
            sb.AppendLine($"- Shield: {Shields} (with a telegraphed hit on the hero's tile: {ShieldsUnderThreat}, {Pct(ShieldsUnderThreat, Shields)})");
            sb.AppendLine($"- Dash: {Dashes} (escaping a telegraphed hit: {DashesUnderThreat}, {Pct(DashesUnderThreat, Dashes)})");
            sb.AppendLine($"- Potions: {PotionsUsed} | Waits: {Waits}");
            sb.AppendLine($"- Chests opened: {ChestsOpened} ({ChestRewards} rewards) | Optional rewards left behind: {RewardsSkipped}");
            sb.AppendLine($"- Rejected commands (possible UI confusion): {CommandsRejected}");
            sb.AppendLine();

            sb.AppendLine("## Event counts");
            sb.AppendLine();
            sb.AppendLine("| Event | Count |");
            sb.AppendLine("|---|---|");
            foreach (var pair in EventCounts) sb.AppendLine($"| {pair.Key} | {pair.Value} |");
            return sb.ToString();
        }
    }
}
