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
            var run = Run(Catalog.RunFloorCount, 3UL,
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
        internal static string RepoRoot()
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

    /// <summary>
    /// CI-09 / CI-10 / DATA-06: the kit is the only gate there is, and this repo is public.
    /// These run real processes (git, powershell) on purpose -- the thing under test is the tooling.
    /// </summary>
    public class KitGateAndTesterDataTests
    {
        static string RepoRoot() => PlaytestKitTests.RepoRoot();

        static string Kit(string file) => Path.Combine(RepoRoot(), "tools", "playtest-kit", file);

        struct Ran
        {
            public int Code;
            public string Output;
        }

        static Ran Run(string exe, string args)
        {
            var start = new ProcessStartInfo(exe, args)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                WorkingDirectory = RepoRoot(),
            };
            var text = new System.Text.StringBuilder();
            using (var process = new Process { StartInfo = start })
            {
                // Read both pipes asynchronously: a synchronous ReadToEnd on one can deadlock on the other.
                process.OutputDataReceived += (s, e) => { if (e.Data != null) lock (text) text.AppendLine(e.Data); };
                process.ErrorDataReceived += (s, e) => { if (e.Data != null) lock (text) text.AppendLine(e.Data); };
                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                Assert.That(process.WaitForExit(120000), Is.True, exe + " timed out.");
                process.WaitForExit();
                lock (text) return new Ran { Code = process.ExitCode, Output = text.ToString() };
            }
        }

        // ---- DATA-06 -------------------------------------------------------

        [Test]
        public void NoTelemetryArtifactCanBeCommitted()
        {
            // Tester zips are named after the player and carry Player.log (GPU/CPU/OS/resolution).
            // github.com/adaPlu/Click is public; one push is permanent.
            foreach (var path in new[]
                     {
                         "playtest-logs/x/a.jsonl",
                         "playtest-summary.md",
                         "ClickDungeon-playtest-logs.zip",
                         "summary.md",
                         "tools/playtest-kit/ClickDungeon-playtest-logs.zip",
                         "docs/playtest-logs/someone/run.jsonl",
                     })
            {
                var ran = Run("git", $"-C \"{RepoRoot()}\" check-ignore -q -- \"{path}\"");
                Assert.That(ran.Code, Is.EqualTo(0), $"{path} is not gitignored. {ran.Output}");
            }
        }

        [Test]
        public void TheGuideNeverWritesTesterDataIntoTheRepo()
        {
            string repo = RepoRoot();
            string guidePath = Path.Combine(repo, "docs", "playtest-guide.md");
            var lines = File.ReadAllLines(guidePath);
            string guide = string.Join("\n", lines);

            void MustBeOutsideTheRepo(string token, string where)
            {
                // Placeholders like <player1> are not legal path characters; they do not change where the path lives.
                string probe = token.Trim('`', '.', ',').Replace("<", "").Replace(">", "");
                Assert.That(Path.IsPathRooted(probe), Is.True, $"{where}: '{token}' is a repo-relative path. Tester data must live outside the worktree.");
                string full = Path.GetFullPath(probe).TrimEnd(Path.DirectorySeparatorChar);
                Assert.That(full.StartsWith(repo.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase),
                    Is.False, $"{where}: '{token}' resolves inside the repo ({full}).");
            }

            // The summarize command: every path it reads from and writes to.
            var command = lines.SingleOrDefault(l => l.Contains("ClickDungeon.Telemetry.Report"));
            Assert.That(command, Is.Not.Null, "The summarize command vanished from the guide.");
            var words = command.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            int dashDash = Array.IndexOf(words, "--");
            Assert.That(dashDash, Is.GreaterThan(-1), command);
            var arguments = words.Skip(dashDash + 1).Where(w => !w.StartsWith("--")).ToList();
            Assert.That(arguments.Count, Is.GreaterThanOrEqualTo(2), command);
            foreach (var argument in arguments) MustBeOutsideTheRepo(argument, "summarize command");
            int outFlag = Array.IndexOf(words, "--out");
            Assert.That(outFlag, Is.GreaterThan(dashDash), "The summarize command no longer writes to an explicit --out.");
            MustBeOutsideTheRepo(words[outFlag + 1], "--out");

            // The "unzip every tester's logs into ..." example.
            int bullet = Array.FindIndex(lines, l => l.Contains("Unzip every tester's logs"));
            Assert.That(bullet, Is.GreaterThan(-1), "The unzip step vanished from the guide.");
            var bulletText = new System.Text.StringBuilder(lines[bullet]);
            for (int i = bullet + 1; i < lines.Length && lines[i].StartsWith("  "); i++) bulletText.Append(' ').Append(lines[i]);
            var examples = Regex.Matches(bulletText.ToString(), "`([^`]+)`").Cast<Match>()
                .Select(m => m.Groups[1].Value)
                .Where(v => v.Contains("/") || v.Contains("\\"))
                .ToList();
            Assert.That(examples, Is.Not.Empty, "The unzip step gives no example folder at all.");
            foreach (var example in examples) MustBeOutsideTheRepo(example, "unzip example");

            // Belt: no relative playtest-logs directory anywhere in the page.
            Assert.That(Regex.IsMatch(guide, @"(?<![\w:\\/])playtest-logs[/\\]"), Is.False,
                "The guide still names a repo-relative playtest-logs/ folder.");
        }

        // ---- CI-09 ---------------------------------------------------------

        static Ran Gate(string runnerOutput, int exitCode = 0)
        {
            string file = Path.Combine(Path.GetTempPath(), "cd-gate-" + Guid.NewGuid().ToString("N") + ".txt");
            File.WriteAllText(file, runnerOutput);
            try
            {
                return Run("powershell", $"-NoProfile -ExecutionPolicy Bypass -File \"{Kit("test-gate.ps1")}\" -OutputFile \"{file}\" -ExitCode {exitCode}");
            }
            finally
            {
                if (File.Exists(file)) File.Delete(file);
            }
        }

        // MAINT-34: a sample of a real full run. It has to stay at or above the gate's floor, which tracks the suite.
        const string PassedLine = "Passed!  - Failed:     0, Passed:   357, Skipped:     0, Total:   357, Duration: 19 s - ClickDungeon.Sim.Tests.dll (net10.0)";

        [Test]
        public void TheKitGateAcceptsAFullRun()
        {
            var ran = Gate("Determining projects to restore...\n" + PassedLine + "\n");
            Assert.That(ran.Code, Is.EqualTo(0), ran.Output);
            Assert.That(ran.Output.Trim(), Does.Contain("357"), "The gate reports the count it verified, for VERSION.txt.");
        }

        [Test]
        public void TheKitGateRejectsARunThatDiscoveredNothing()
        {
            // This is the whole point: `dotnet test` exits 0 here, so the old `if ($LASTEXITCODE -ne 0)` passed it.
            var ran = Gate("No test matches the given testcase filter `FullyQualifiedName~Nope` in ClickDungeon.Sim.Tests.dll\n", 0);
            Assert.That(ran.Code, Is.Not.EqualTo(0), "A run that discovered no tests was accepted as a gate pass.");

            var quiet = Gate("Determining projects to restore...\nRestored ClickDungeon.Sim.Tests.csproj\n", 0);
            Assert.That(quiet.Code, Is.Not.EqualTo(0), "A run with no summary line at all was accepted as a gate pass.");
        }

        [Test]
        public void TheKitGateRejectsAShrunkenSuite()
        {
            var ran = Gate("Passed!  - Failed:     0, Passed:     4, Skipped:     0, Total:     4, Duration: 1 s - x.dll (net10.0)\n");
            Assert.That(ran.Code, Is.Not.EqualTo(0), "Four tests passed for the whole suite and the gate let it through.");
        }

        [Test]
        public void TheKitGateStillRejectsFailuresAndBadExitCodes()
        {
            var failed = Gate("Failed!  - Failed:     3, Passed:   286, Skipped:     0, Total:   289, Duration: 19 s - x.dll (net10.0)\n");
            Assert.That(failed.Code, Is.Not.EqualTo(0), "Failing tests were accepted.");

            var crashed = Gate(PassedLine + "\n", 1);
            Assert.That(crashed.Code, Is.Not.EqualTo(0), "A non-zero dotnet test exit code was ignored.");
        }

        [Test]
        public void TheKitGateFloorIsWorthHaving()
        {
            var script = File.ReadAllText(Kit("test-gate.ps1"));
            var floor = Regex.Match(script, @"\$MinimumTests\s*=\s*(\d+)");
            Assert.That(floor.Success, Is.True, "test-gate.ps1 no longer has a default minimum.");
            Assert.That(int.Parse(floor.Groups[1].Value), Is.GreaterThanOrEqualTo(200), "The floor is too low to detect a suite that stopped running.");
        }

        [Test]
        public void MakeKitUsesTheGateAndRecordsWhetherItRan()
        {
            var kit = File.ReadAllText(Kit("make-kit.ps1"));
            Assert.That(kit, Does.Contain("test-gate.ps1"), "make-kit.ps1 no longer runs the gate.");
            Assert.That(Regex.IsMatch(kit, @"if\s*\(\s*\$LASTEXITCODE\s*-ne\s*0\s*\)\s*\{\s*throw"), Is.False,
                "make-kit.ps1 is back to gating on the exit code alone, which is 0 when no tests are discovered.");
            // VERSION.txt has to distinguish a gated kit from a -SkipTests one.
            Assert.That(kit, Does.Contain("SKIPPED"), "A -SkipTests kit leaves no trace of having skipped the gate.");
            var versionLines = Regex.Match(kit, @"\$versionLines\s*=\s*@\((.*?)^\)", RegexOptions.Singleline | RegexOptions.Multiline);
            Assert.That(versionLines.Success, Is.True, "make-kit.ps1 no longer builds VERSION.txt from $versionLines.");
            Assert.That(versionLines.Groups[1].Value, Does.Contain("$gateLine"), "The gate result is not written into VERSION.txt.");
            Assert.That(Regex.IsMatch(kit, @"\$versionLines\s*\|\s*Set-Content[^\r\n]*VERSION\.txt"), Is.True,
                "$versionLines is not what gets written to VERSION.txt.");
        }

        // ---- CI-10 ---------------------------------------------------------

        [Test]
        public void TheKitIsNamedAfterTheBuildStampAndAMismatchIsFatal()
        {
            var kit = File.ReadAllText(Kit("make-kit.ps1"));

            var name = Regex.Match(kit, @"(?m)^\$name\s*=\s*(.+)$");
            Assert.That(name.Success, Is.True, "make-kit.ps1 no longer names the kit.");
            Assert.That(name.Groups[1].Value, Does.Contain("$builtFrom"), "The kit name must come from the build stamp.");
            Assert.That(name.Groups[1].Value, Does.Not.Contain("$version"),
                "The kit is still labelled with the packaging-time commit, which the player may not have been built from.");

            Assert.That(kit, Does.Contain("AllowVersionMismatch"), "There is no explicit opt-out switch for a stamp mismatch.");
            var guard = Regex.Match(kit, @"if\s*\(\s*\$mismatch\s+-and\s+-not\s+\$AllowVersionMismatch\s*\)\s*\{\s*\r?\n?\s*throw");
            Assert.That(guard.Success, Is.True, "A build/packaging version mismatch is not a hard failure.");
            // ...and the throw has to come before anything is staged or zipped.
            Assert.That(guard.Index, Is.LessThan(kit.IndexOf("New-Item -ItemType Directory", StringComparison.Ordinal)),
                "The mismatch check runs after the kit has already been built.");

            // VERSION.txt keeps both versions, as CI-02's fix required.
            Assert.That(kit, Does.Contain("Player built from: $builtFrom"));
            Assert.That(Regex.IsMatch(kit, @"""[^""]*\(git\):\s*\$version"""), Is.True, "VERSION.txt no longer records the packaging-time git version.");
        }
    }
}
