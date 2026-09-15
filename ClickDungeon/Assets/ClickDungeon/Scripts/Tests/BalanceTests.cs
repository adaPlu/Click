using System;
using System.Collections.Generic;
using System.Linq;
using ClickDungeon.Application;
using ClickDungeon.Content;
using ClickDungeon.Domain;
using ClickDungeon.Simulation;
using NUnit.Framework;
using static ClickDungeon.Tests.Scenario;

namespace ClickDungeon.Tests
{
    public class AutoPlayerTests
    {
        [Test]
        public void CopySerializesExactlyLikeTheRun()
        {
            // Every tier and deep runs, so rare state (boss modes, stagger, minions, chests) is copied too.
            foreach (var tier in new[] { Difficulty.Easy, Difficulty.Medium, Difficulty.Hardcore })
            {
                var catalog = ContentCatalog.CreateDefault(tier);
                foreach (ulong seed in new ulong[] { 21, 22, 23 })
                {
                    var run = RunFactory.NewRun(seed, catalog, new List<GameEvent>());
                    var player = new AutoPlayer(AutoPlayer.CasualMistakeRate);
                    for (int i = 0; i < 150 && run.Status == RunStatus.InProgress; i++)
                    {
                        Assert.That(SaveSerializer.ToJson(AutoPlayer.Copy(run)), Is.EqualTo(SaveSerializer.ToJson(run)), $"{tier} seed {seed} command {i}");
                        var result = TurnResolver.Apply(run, player.Choose(run, catalog, seed * 31 + (ulong)i), catalog);
                        Assert.That(result.Accepted, Is.True, result.RejectReason);
                    }
                }
            }
        }

        [Test]
        public void SameSeedPlaysTheSameRun()
        {
            Assert.That(AutoPlayer.PlayRun(Catalog, 3UL, 120).ToString(), Is.EqualTo(AutoPlayer.PlayRun(Catalog, 3UL, 120).ToString()));
        }
    }

    /// <summary>
    /// Balance targets measured with AutoPlayer, a look-ahead bot that plays about as well as a careful player.
    /// The fast checks guard the targets; the explicit report prints the full table for tuning.
    /// </summary>
    public class BalanceTests
    {
        public const int MaxCommands = 400;
        static readonly Difficulty[] Tiers = { Difficulty.Easy, Difficulty.Medium, Difficulty.Hardcore };

        sealed class Tally
        {
            public int Runs, Won, ReachedBoss, Stalled;
            public long Turns;
            public int[] DeathsByFloor = new int[6];
            public int[] StalledByFloor = new int[6];
        }

        static readonly (string name, double mistakeRate)[] Skills =
        {
            ("sharp", 0.0), ("casual", AutoPlayer.CasualMistakeRate), ("sloppy", 0.35), ("novice", NoviceMistakeRate), ("flailing", FlailingMistakeRate),
        };

        static Tally Measure(Difficulty tier, int runs, double mistakeRate) => Measure(ContentCatalog.CreateDefault(tier), runs, mistakeRate);

        static Tally Measure(ContentCatalog catalog, int runs, double mistakeRate)
        {
            var tally = new Tally { Runs = runs };
            for (ulong seed = 1; seed <= (ulong)runs; seed++)
            {
                var r = AutoPlayer.PlayRun(catalog, seed, MaxCommands, mistakeRate);
                if (r.Floor >= catalog.RunFloorCount) tally.ReachedBoss++;
                if (r.Status == RunStatus.Won) tally.Won++;
                else if (r.Status == RunStatus.Lost) tally.DeathsByFloor[r.Floor]++;
                else
                {
                    tally.Stalled++;
                    tally.StalledByFloor[r.Floor]++;
                }
                tally.Turns += r.Turns;
            }
            return tally;
        }

        /// <summary>Half the turns spent on random moves: a first-time player still learning the telegraphs.</summary>
        public const double NoviceMistakeRate = 0.5;

        /// <summary>Most turns spent on random moves. Separates the tiers clearly.</summary>
        public const double FlailingMistakeRate = 0.7;

        [Test]
        public void SquiresStrollLetsANovicePlayerBeatBlobert()
        {
            var easy = Measure(Difficulty.Easy, 30, NoviceMistakeRate);
            Assert.That(easy.ReachedBoss, Is.GreaterThanOrEqualTo(27), $"Only {easy.ReachedBoss}/30 novice easy runs reached floor 5.");
            Assert.That(easy.Won, Is.GreaterThanOrEqualTo(21), $"Only {easy.Won}/30 novice easy runs beat Lord Blobert.");
        }

        [Test]
        public void KnightsTrialLetsANovicePlayerReachBlobert()
        {
            var medium = Measure(Difficulty.Medium, 30, NoviceMistakeRate);
            Assert.That(medium.ReachedBoss, Is.GreaterThanOrEqualTo(24), $"Only {medium.ReachedBoss}/30 novice medium runs reached floor 5.");
        }

        [Test]
        public void TiersKeepTheirOrder()
        {
            var easy = Measure(Difficulty.Easy, 40, FlailingMistakeRate);
            var medium = Measure(Difficulty.Medium, 40, FlailingMistakeRate);
            var hardcore = Measure(Difficulty.Hardcore, 40, FlailingMistakeRate);
            Assert.That(easy.Won, Is.GreaterThan(medium.Won), "Squire's Stroll must be won more often than Knight's Trial.");
            Assert.That(medium.Won, Is.GreaterThan(hardcore.Won), "Knight's Trial must be won more often than Blobert's Wrath.");
            Assert.That(easy.ReachedBoss, Is.GreaterThan(hardcore.ReachedBoss));
        }

        [Test, Explicit("Slow balance report: dotnet test --filter Name=BalanceReport --logger \"console;verbosity=detailed\"")]
        public void BalanceReport()
        {
            const int runs = 200;
            TestContext.Out.WriteLine($"AutoPlayer balance report: {runs} seeds per tier, {MaxCommands} command cap");
            TestContext.Out.WriteLine("player  tier       reach F5   won   avg turns  deaths F1..F5         stalled F1..F5");
            foreach (var (name, mistakeRate) in Skills)
            foreach (var tier in Tiers)
            {
                var t = Measure(tier, runs, mistakeRate);
                TestContext.Out.WriteLine(
                    $"{name,-7} {tier,-10} {Pct(t.ReachedBoss, runs),7}  {Pct(t.Won, runs),5}  {t.Turns / (double)runs,9:0.0}  " +
                    $"{ByFloor(t.DeathsByFloor),-20}  {ByFloor(t.StalledByFloor)}");
            }
        }

        [Test, Explicit("Tuning aid: compares candidate numbers for Knight's Trial and Blobert's Wrath")]
        public void DifficultySweep()
        {
            const int runs = 120;
            DifficultyDefinition Tier(Difficulty id, Action<DifficultyDefinition> tweak = null)
            {
                // CreateDefault builds fresh definitions, so tweaking this one changes nothing else.
                var d = ContentCatalog.CreateDefault().DifficultyInfo(id);
                tweak?.Invoke(d);
                return d;
            }

            var candidates = new List<(string name, DifficultyDefinition tuning)>
            {
                ("M0 base", Tier(Difficulty.Medium)),
                ("M1 boss hp +2", Tier(Difficulty.Medium, d => d.BossHp = 2)),
                ("M2 potions -1", Tier(Difficulty.Medium, d => d.StartingPotions = -1)),
                ("M3 enemy hp +1", Tier(Difficulty.Medium, d => d.EnemyHp = 1)),
                ("M4 enemies +1", Tier(Difficulty.Medium, d => d.ExtraEnemies = 1)),
                ("M5 boss +2 slam +1", Tier(Difficulty.Medium, d => { d.BossHp = 2; d.BossSlamDamage = 1; })),
                ("M6 hero hp -2", Tier(Difficulty.Medium, d => d.HeroMaxHp = -2)),
                ("H0 current", Tier(Difficulty.Hardcore)),
                ("H1 hero hp -2", Tier(Difficulty.Hardcore, d => d.HeroMaxHp = -2)),
                ("H2 boss 20 slam 6", Tier(Difficulty.Hardcore, d => { d.BossHp = 8; d.BossSlamDamage = 2; })),
                ("H3 no potions", Tier(Difficulty.Hardcore, d => d.StartingPotions = -2)),
                ("H4 enemies +2", Tier(Difficulty.Hardcore, d => d.ExtraEnemies = 2)),
                ("H5 hero -2 boss 20", Tier(Difficulty.Hardcore, d => { d.HeroMaxHp = -2; d.BossHp = 8; })),
            };

            TestContext.Out.WriteLine($"Difficulty sweep: {runs} seeds, reach F5 / win per player; novice deaths F1..F5");
            foreach (var (name, tuning) in candidates)
            {
                var catalog = ContentCatalog.CreateTuned(tuning);
                var line = new System.Text.StringBuilder($"{name,-20}");
                Tally novice = null;
                foreach (var (skill, rate) in Skills)
                {
                    if (skill == "sloppy") continue;
                    var t = Measure(catalog, runs, rate);
                    if (skill == "novice") novice = t;
                    line.Append($"  {skill} {Pct(t.ReachedBoss, runs),4}/{Pct(t.Won, runs),-4}");
                }
                line.Append($"  | {ByFloor(novice.DeathsByFloor)}");
                TestContext.Out.WriteLine(line.ToString());
            }
        }

        static string ByFloor(int[] counts) => $"{counts[1]} / {counts[2]} / {counts[3]} / {counts[4]} / {counts[5]}";

        [Test, Explicit("Tuning aid: prints the end of stalled or lost AutoPlayer runs")]
        public void AutoPlayerTrace()
        {
            foreach (var tier in Tiers)
            {
                var catalog = ContentCatalog.CreateDefault(tier);
                int shown = 0;
                for (ulong seed = 1; seed <= 200 && shown < 2; seed++)
                {
                    if (AutoPlayer.PlayRun(catalog, seed, MaxCommands, AutoPlayer.CasualMistakeRate).Status == RunStatus.Won) continue;
                    shown++;
                    Trace(catalog, seed);
                }
            }
        }

        static void Trace(ContentCatalog catalog, ulong seed)
        {
            {
                var run = RunFactory.NewRun(seed, catalog, new List<GameEvent>());
                var player = new AutoPlayer(AutoPlayer.CasualMistakeRate);
                var lines = new List<string>();
                for (int i = 0; i < MaxCommands && run.Status == RunStatus.InProgress; i++)
                {
                    var command = player.Choose(run, catalog, seed * 7919UL + (ulong)i);
                    var enemies = string.Join(" ", run.Floor.Enemies.Select(e => $"{e.DefId}@{e.Pos}{(e.Awake ? "!" : "z")}{e.Intent}"));
                    lines.Add($"#{i} F{run.Floor.FloorIndex} hero {run.Hero.Pos} hp {run.Hero.Hp} key {run.Hero.HasKey} -> {command} | {enemies}");
                    TurnResolver.Apply(run, command, catalog);
                }
                TestContext.Out.WriteLine($"{catalog.Difficulty} seed {seed}: {run.Status} floor {run.Floor.FloorIndex} turn {run.Turn}");
                foreach (var line in lines.Skip(Math.Max(0, lines.Count - 12))) TestContext.Out.WriteLine("  " + line);
                for (int y = BoardRules.Size - 1; y >= 0; y--)
                {
                    var row = new System.Text.StringBuilder("  ");
                    for (int x = 0; x < BoardRules.Size; x++)
                    {
                        var p = new GridPos(x, y);
                        var cell = run.Floor[p];
                        char ch = cell.Terrain == Terrain.Wall ? '#' : cell.Terrain == Terrain.Pit ? 'o' : '.';
                        if (cell.Hazard == HazardKind.Spikes) ch = '^';
                        else if (cell.Hazard == HazardKind.Bomb) ch = 'b';
                        if (cell.Content == ContentKind.Key) ch = 'K';
                        else if (cell.IsClosedChest) ch = 'C';
                        else if (cell.Content == ContentKind.Potion) ch = 'P';
                        if (cell.IsExit) ch = run.Floor.ExitUnlocked ? 'x' : 'X';
                        if (run.Floor.EnemyAt(p) != null) ch = run.Floor.EnemyAt(p).Awake ? 'E' : 'e';
                        if (run.Hero.Pos == p) ch = 'H';
                        row.Append(ch);
                    }
                    TestContext.Out.WriteLine(row.ToString());
                }
            }
        }

        static string Pct(int count, int runs) => $"{100.0 * count / runs:0}%";
    }
}
