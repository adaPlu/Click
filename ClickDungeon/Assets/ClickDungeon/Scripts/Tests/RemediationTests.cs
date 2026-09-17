using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.RegularExpressions;
using ClickDungeon.Application;
using ClickDungeon.Content;
using ClickDungeon.Domain;
using ClickDungeon.Simulation;
using NUnit.Framework;
using static ClickDungeon.Tests.Scenario;

namespace ClickDungeon.Tests
{
    /// <summary>REL-07 / SEC-01: launch options and run seeds.</summary>
    public class LaunchOptionsTests
    {
        [TestCase("easy", Difficulty.Easy)]
        [TestCase(" HARDCORE ", Difficulty.Hardcore)]
        [TestCase("Medium", Difficulty.Medium)]
        [TestCase("7", Difficulty.Medium)]
        [TestCase("1", Difficulty.Medium)]
        [TestCase("Easy, Hardcore", Difficulty.Medium)]
        [TestCase("nightmare", Difficulty.Medium)]
        [TestCase(null, Difficulty.Medium)]
        public void DifficultyIsParsedByNameOnly(string value, Difficulty expected)
        {
            Assert.That(LaunchOptions.ParseDifficulty(value), Is.EqualTo(expected));
        }

        [TestCase("step", MovementMode.Step)]
        [TestCase(" FREE ", MovementMode.Free)]
        [TestCase("1", MovementMode.Free)]
        [TestCase("sideways", MovementMode.Free)]
        [TestCase(null, MovementMode.Free)]
        public void MovementIsParsedByNameOnly(string value, MovementMode expected)
        {
            Assert.That(LaunchOptions.ParseMovement(value), Is.EqualTo(expected));
        }

        [Test]
        public void RunSeedsAreRandom()
        {
            // Smoke check only: it cannot prove the absence of clock or uptime input, which rests on the implementation.
            var seeds = new HashSet<ulong>();
            for (int i = 0; i < 64; i++) seeds.Add(LaunchOptions.NewRunSeed());
            Assert.That(seeds.Count, Is.EqualTo(64));
        }
    }

    /// <summary>DATA-03 / SEC-01: telemetry says where heals came from and keeps timestamps to the second.</summary>
    public class TelemetryRemediationTests
    {
        static void Play(TelemetryRecorder recorder, ContentCatalog catalog, RunState run, PlayerCommand command)
        {
            var pending = recorder.Begin(run, command);
            var result = TurnResolver.Apply(run, command, catalog);
            Assert.That(result.Accepted, Is.True, result.RejectReason);
            recorder.Complete(pending, run, result);
        }

        [Test]
        public void HealsRecordTheirSourceAndTimesAreWholeSeconds()
        {
            var easy = ContentCatalog.CreateDefault(Difficulty.Easy);
            var run = Run(
                ".....",
                ".....",
                "HKX..",
                ".....",
                ".....");
            run.Hero.MaxHp = easy.HeroClass("knight").MaxHp;
            run.Hero.Hp = 3;
            var sink = new MemoryTelemetrySink();
            var recorder = new TelemetryRecorder(sink, easy, () => new DateTime(2026, 9, 14, 12, 0, 0, 789, DateTimeKind.Utc), "t");

            Play(recorder, easy, run, PlayerCommand.Potion());
            Play(recorder, easy, run, PlayerCommand.Move(P(1, 2)));
            Play(recorder, easy, run, PlayerCommand.Move(P(2, 2)));

            var heals = sink.Named("healed");
            Assert.That(heals.Select(h => (string)h.Data["source"]), Is.EqualTo(new[] { "potion", "stairs" }));
            Assert.That(heals[0].Time, Is.EqualTo("2026-09-14T12:00:00.0000000Z"));
        }
    }

    /// <summary>MAINT-06: tuned catalogs keep their numbers across tier switches, and floors that had enemies keep one.</summary>
    public class CatalogTuningTests
    {
        [Test]
        public void SwitchingTiersKeepsCustomTuning()
        {
            var tuning = ContentCatalog.CreateDefault().DifficultyInfo(Difficulty.Medium);
            tuning.BossHp = 8;
            var tuned = ContentCatalog.CreateTuned(tuning);
            int bossHp = tuned.Enemy("lord_blobert").MaxHp;

            var back = tuned.ForDifficulty(Difficulty.Easy).ForDifficulty(Difficulty.Medium);
            Assert.That(back.Enemy("lord_blobert").MaxHp, Is.EqualTo(bossHp));
            Assert.That(bossHp, Is.EqualTo(ContentCatalog.CreateDefault().Enemy("lord_blobert").MaxHp + 8));
        }

        [Test]
        public void FloorsThatHadEnemiesKeepAtLeastOne()
        {
            var tuning = ContentCatalog.CreateDefault().DifficultyInfo(Difficulty.Easy);
            tuning.ExtraEnemies = -5;
            var catalog = ContentCatalog.CreateTuned(tuning);
            foreach (var profile in catalog.FloorProfiles)
            {
                if (profile.IsBoss) continue;
                Assert.That(profile.MinEnemies, Is.GreaterThanOrEqualTo(1), profile.Name);
                Assert.That(profile.MaxEnemies, Is.GreaterThanOrEqualTo(profile.MinEnemies), profile.Name);
            }
        }
    }

    /// <summary>REL-03: the exit only triggers when entered, including after Blobert falls under a hero standing on it.</summary>
    public class BossExitTests
    {
        [Test]
        public void HeroOnTheExitWhenBlobertFallsStepsOffAndBackOnToWin()
        {
            var run = Run(5, 3UL,
                "HB...",
                ".....",
                ".....",
                ".....",
                "....X");
            var floor = run.Floor;
            floor[floor.Exit].IsExit = false;
            floor.Exit = run.Hero.Pos;
            floor[floor.Exit].IsExit = true;
            Enemy(run, "lord_blobert").Hp = 1;

            DoOk(run, PlayerCommand.Slash(P(1, 4)));
            Assert.That(floor.ExitUnlocked, Is.True);
            Assert.That(Board.ExitReadsOpen(run), Is.True);
            Assert.That(run.Status, Is.EqualTo(RunStatus.InProgress), "Standing on the exit does not use it.");

            DoOk(run, PlayerCommand.Move(P(0, 3)));
            DoOk(run, PlayerCommand.Move(P(0, 4)));
            Assert.That(run.Status, Is.EqualTo(RunStatus.Won));
        }
    }

    /// <summary>REL-02 / REL-09 / MAINT-02 / MAINT-03: the tester log collector.</summary>
    public class PlaytestKitTests
    {
        static string RepoRoot()
        {
            foreach (var start in new[] { Directory.GetCurrentDirectory(), TestContext.CurrentContext.TestDirectory })
            {
                for (var dir = new DirectoryInfo(start); dir != null; dir = dir.Parent)
                    if (Directory.Exists(Path.Combine(dir.FullName, "tools", "playtest-kit"))) return dir.FullName;
            }
            Assert.Inconclusive("Repository root not found.");
            return null;
        }

        static string Kit(string file) => Path.Combine(RepoRoot(), "tools", "playtest-kit", file);

        [Test]
        public void CollectorLooksWherePlayerSettingsPutTheLogs()
        {
            var settings = File.ReadAllText(Path.Combine(RepoRoot(), "ClickDungeon", "ProjectSettings", "ProjectSettings.asset"));
            string company = Regex.Match(settings, @"^\s*companyName:\s*(.+)$", RegexOptions.Multiline).Groups[1].Value.Trim();
            string product = Regex.Match(settings, @"^\s*productName:\s*(.+)$", RegexOptions.Multiline).Groups[1].Value.Trim();
            Assert.That(company, Is.Not.Empty);
            var expected = $@"AppData\LocalLow\{company}\{product}\telemetry";

            foreach (var file in new[] { "collect-logs.bat", "collect-logs.ps1", "PLAYTEST-README.txt" })
                Assert.That(File.ReadAllText(Kit(file)), Does.Contain(expected), file);
            var setup = File.ReadAllText(Path.Combine(RepoRoot(), "ClickDungeon", "Assets", "ClickDungeon", "Scripts", "Editor", "ProjectSetup.cs"));
            Assert.That(setup, Does.Contain($"companyName = \"{company}\"").And.Contain($"productName = \"{product}\""));
        }

        [Test]
        public void BatchFileUsesWindowsLineEndings()
        {
            var bytes = File.ReadAllBytes(Kit("collect-logs.bat"));
            for (int i = 0; i < bytes.Length; i++)
                if (bytes[i] == (byte)'\n') Assert.That(i > 0 && bytes[i - 1] == (byte)'\r', Is.True, $"Bare LF at byte {i}.");
        }

        [Test]
        public void CollectorZipsLogsWhileTheGameStillHasThemOpen()
        {
            Assume.That(Environment.OSVersion.Platform, Is.EqualTo(PlatformID.Win32NT));
            var root = Path.Combine(Path.GetTempPath(), "cd-collect-" + Guid.NewGuid().ToString("N"));
            var logs = Path.Combine(root, "game", "telemetry");
            var zip = Path.Combine(root, "logs.zip");
            var profile = Environment.GetEnvironmentVariable("USERPROFILE") ?? "C:\\Users\\tester";
            Directory.CreateDirectory(logs);
            File.WriteAllText(Path.Combine(root, "game", "Player.log"), "Loaded " + profile.Replace('\\', '/') + "/AppData/LocalLow/x\n");
            try
            {
                // The real sink, still open: it holds its file the way the running game does.
                var sink = new JsonlTelemetrySink(logs, DateTime.UtcNow);
                try
                {
                    sink.Write(new TelemetryEvent { Time = "t", Session = "s", Name = "run_started", Run = 1, Data = new Dictionary<string, object>() });
                    var start = new ProcessStartInfo("powershell", $"-NoProfile -ExecutionPolicy Bypass -File \"{Kit("collect-logs.ps1")}\" -LogsDir \"{logs}\" -OutZip \"{zip}\"")
                    {
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        CreateNoWindow = true,
                    };
                    using (var process = Process.Start(start))
                    {
                        string output = process.StandardOutput.ReadToEnd();
                        Assert.That(process.WaitForExit(120000), Is.True, "Collector timed out.");
                        Assert.That(process.ExitCode, Is.EqualTo(0), output);
                    }
                }
                finally
                {
                    sink.Dispose();
                }

                using (var archive = ZipFile.OpenRead(zip))
                {
                    var names = archive.Entries.Select(e => e.Name).ToList();
                    Assert.That(names.Any(n => n.EndsWith(".jsonl")), Is.True, string.Join(", ", names));
                    var playerLog = archive.Entries.Single(e => e.Name == "Player.log");
                    using (var reader = new StreamReader(playerLog.Open()))
                        Assert.That(reader.ReadToEnd(), Does.Not.Contain(profile.Replace('\\', '/')).IgnoreCase, "User folder is removed.");
                }
            }
            finally
            {
                if (Directory.Exists(root)) Directory.Delete(root, true);
            }
        }
    }
}
