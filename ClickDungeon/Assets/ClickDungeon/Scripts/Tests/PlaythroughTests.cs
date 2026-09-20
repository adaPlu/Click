using System;
using System.Collections.Generic;
using System.Linq;
using ClickDungeon.Application;
using ClickDungeon.Content;
using ClickDungeon.Domain;
using ClickDungeon.Simulation;
using NUnit.Framework;

namespace ClickDungeon.Tests
{
    /// <summary>
    /// Playtest aid: one player's first runs, back to back through the real session, so the profile carries over as it
    /// would for a person (coins, XP, gear, achievements, mail). Between runs the player collects their mail and spends
    /// free talent points. The bot plays blind with the casual mistake rate.
    /// </summary>
    public class PlaythroughTests
    {
        [Test, Explicit("Playtest aid: dotnet test --filter Name=TenPlaythroughs --logger \"console;verbosity=detailed\"")]
        public void TenPlaythroughs()
        {
            var session = new GameSession(ContentCatalog.CreateDefault(), null);
            // CD_SEED picks another ten dungeons; without it the same ten play every time.
            ulong seedBase = ulong.TryParse(Environment.GetEnvironmentVariable("CD_SEED"), out var custom) ? custom : 20260918UL;
            // CD_HERO plays another hero (dawnward is the Paladin); its class's tree is the one learned.
            string heroId = Environment.GetEnvironmentVariable("CD_HERO") ?? ContentCatalog.DefaultHeroId;
            string classId = Progression.ClassOf(session.Catalog, heroId);
            // CD_LEVEL starts the hero at that level (its points spent at once); CD_RUNS plays more or fewer than ten.
            if (int.TryParse(Environment.GetEnvironmentVariable("CD_LEVEL"), out int startLevel) && startLevel > 1)
                session.Profile.Xp = Progression.XpForLevel(startLevel);
            int runs = int.TryParse(Environment.GetEnvironmentVariable("CD_RUNS"), out int n) && n > 0 ? n : 10;
            Console.WriteLine($"seed base {seedBase}, hero {heroId} ({classId}), starting level {Progression.Level(session.Profile)}");
            Console.WriteLine("run  seed      result  floor  turns  hp     coins  gems  +xp  level  items  achievements  talents learned before the run");
            for (int i = 1; i <= runs; i++)
            {
                Mailbox.CollectAll(session.Profile);
                // A capstone when one opens, else the path with the fewest points climbs a tier (AutoPlayer.LearnTalents).
                var learned = AutoPlayer.LearnTalents(session.Profile, session.Catalog, classId);

                ulong seed = seedBase + (ulong)i * 101UL;
                session.StartNewRun(seed, Difficulty.Medium, MovementMode.Free, heroId);
                var bot = new AutoPlayer(AutoPlayer.CasualMistakeRate, blind: true);
                for (int c = 0; c < 1500 && session.Run.Status == RunStatus.InProgress; c++)
                    session.Submit(bot.Choose(session.Run, session.Catalog, seed * 7919UL + (ulong)c));
                var run = session.Run;
                if (run.Status == RunStatus.InProgress) session.Abandon();

                var p = session.Profile;
                Console.WriteLine($"{i,3}  {seed,-8}  {(run.Status == RunStatus.InProgress ? "STALL" : run.Status.ToString()),-6}  {run.Floor.FloorIndex,5}  " +
                                  $"{run.Turn,5}  {run.Hero.Hp,2}/{run.Hero.MaxHp,-3}  {p.Coins,5}  {p.Gems,4}  {run.XpEarned,3}  {Progression.Level(p),5}  " +
                                  $"{p.Items.Count,5}  {Achievements.EarnedCount(p, session.Catalog),12}  {string.Join(", ", learned)}");
            }
            var profile = session.Profile;
            Console.WriteLine($"\nWon {profile.RunsWon} of {profile.RunsFinished}. Deepest floor {profile.DeepestFloor}, monsters {profile.MonstersSlain}, " +
                              $"chests {profile.ChestsOpened}, coins carried out {profile.CoinsEarned}.");
            Console.WriteLine("Gear: " + string.Join(", ", profile.Items));
            Console.WriteLine("Talents: " + string.Join(", ", profile.Talents.Select(kv => $"{kv.Key} {kv.Value}")));
            Console.WriteLine("Achievements: " + string.Join(", ", session.Catalog.Achievements.Where(a => Achievements.Earned(profile, a.Id)).Select(a => a.Title)));
        }

        /// <summary>
        /// Replays a watch-mode batch (the same seeds, hero alternation and between-run talent spending) and traces the
        /// last run turn by turn, so an outlier seen on screen can be read back. CD_RUN picks which run to trace.
        /// </summary>
        [Test, Explicit("Tuning aid: CD_RUN=5 dotnet test --filter Name=TraceWatchRun --logger \"console;verbosity=detailed\"")]
        public void TraceWatchRun()
        {
            int target = int.TryParse(Environment.GetEnvironmentVariable("CD_RUN"), out var r) && r > 0 ? r : 5;
            var session = new GameSession(ContentCatalog.CreateDefault(), null);
            var heroes = new List<string>(session.Catalog.HeroIdentities.Keys);

            for (int run = 1; run <= target; run++)
            {
                Mailbox.CollectAll(session.Profile);
                string heroId = heroes[(run - 1) % heroes.Count];
                AutoPlayer.LearnTalents(session.Profile, session.Catalog, Progression.ClassOf(session.Catalog, heroId));
                ulong seed = 20260920UL + (ulong)run * 7919UL;
                session.StartNewRun(seed, Difficulty.Medium, MovementMode.Free, heroId);
                var bot = new AutoPlayer(AutoPlayer.CasualMistakeRate, blind: true);

                var lines = new List<string>();
                var visits = new Dictionary<string, int>();
                for (int i = 0; i < 1500 && session.Run.Status == RunStatus.InProgress; i++)
                {
                    var command = bot.Choose(session.Run, session.Catalog, seed * 7919UL + (ulong)i);
                    var live = session.Run;
                    string where = $"F{live.Floor.FloorIndex}:{live.Hero.Pos}";
                    visits[where] = visits.TryGetValue(where, out int n) ? n + 1 : 1;
                    if (run == target)
                        lines.Add($"#{i,4} F{live.Floor.FloorIndex} {live.Hero.Pos} hp {live.Hero.Hp,2}/{live.Hero.MaxHp} mana {live.Hero.Mana} key {(live.Hero.HasKey ? "y" : "n")} "
                                  + $"-> {command} | awake {live.Floor.Enemies.FindAll(e => e.Awake).Count} of {live.Floor.Enemies.Count}");
                    session.Submit(command);
                }
                var end = session.Run;
                if (end.Status == RunStatus.InProgress) session.Abandon();
                Console.WriteLine($"run {run}: {session.Catalog.HeroIdentity(heroId).DisplayName} seed {seed} {end.Status} on floor {end.Floor.FloorIndex} after {end.Turn} turns, {end.Hero.Hp}/{end.Hero.MaxHp} hearts");

                if (run != target) continue;
                Console.WriteLine($"\n--- run {run} trace: {lines.Count} commands ---");
                foreach (var line in lines.GetRange(Math.Max(0, lines.Count - 45), Math.Min(45, lines.Count))) Console.WriteLine("  " + line);

                // What the floor looked like when it ended, and whether the key could be reached at all.
                var floor = end.Floor;
                Console.WriteLine($"\nFloor {floor.FloorIndex} at the end (H hero, K key, X exit, E awake, e asleep, D door, p plate, t teleport, ^ spikes, b bomb, L lava, C chest, o pit):");
                for (int y = 0; y < BoardRules.Size; y++)
                {
                    var row = new System.Text.StringBuilder("  ");
                    for (int x = 0; x < BoardRules.Size; x++)
                    {
                        var at = new GridPos(x, y);
                        var cell = floor[at];
                        char ch = cell.Terrain == Terrain.Pit ? 'o' : cell.Terrain == Terrain.Door ? 'D' : '.';
                        if (cell.Hazard == HazardKind.Spikes) ch = '^';
                        else if (cell.Hazard == HazardKind.Bomb) ch = 'b';
                        else if (cell.Hazard == HazardKind.Lava) ch = 'L';
                        if (cell.Content == ContentKind.Key) ch = 'K';
                        else if (cell.IsClosedChest) ch = 'C';
                        else if (cell.Content == ContentKind.Potion) ch = 'P';
                        else if (cell.Content == ContentKind.Teleport) ch = 't';
                        else if (cell.Content == ContentKind.PressurePlate) ch = 'p';
                        if (cell.IsExit) ch = floor.ExitUnlocked ? 'x' : 'X';
                        if (floor.EnemyAt(at) != null) ch = floor.EnemyAt(at).Awake ? 'E' : 'e';
                        if (end.Hero.Pos == at) ch = 'H';
                        row.Append(ch);
                    }
                    Console.WriteLine(row.ToString());
                }

                var keyAt = GridPos.Invalid;
                foreach (var at in Board.AllCells) if (floor[at].Content == ContentKind.Key) keyAt = at;
                Func<GridPos, bool> walkable = q => floor[q].Terrain == Terrain.Floor && floor[q].Hazard == HazardKind.None && !floor[q].IsClosedChest;
                var reach = Pathfinding.DistanceField(new[] { end.Hero.Pos }, walkable);
                foreach (var at in Board.AllCells)
                    if (floor[at].Content == ContentKind.Teleport || floor[at].Content == ContentKind.PressurePlate)
                        Console.WriteLine($"  {floor[at].Content} at {at}");
                Console.WriteLine($"  hero stands on {floor[end.Hero.Pos].Content} / {floor[end.Hero.Pos].Terrain}");
                Console.WriteLine($"key at {keyAt}: "
                    + (keyAt.InBounds
                        ? (reach[keyAt.Index] == Pathfinding.Unreachable ? "UNREACHABLE on clear floor" : reach[keyAt.Index] + " steps from the hero")
                        : "no key on this floor"));

                Console.WriteLine("\nTiles stood on most (floor:cell = times):");
                var hot = new List<KeyValuePair<string, int>>(visits);
                hot.Sort((a, b) => b.Value.CompareTo(a.Value));
                foreach (var kv in hot.GetRange(0, Math.Min(8, hot.Count))) Console.WriteLine($"  {kv.Key} = {kv.Value}");
            }
        }
    }
}
