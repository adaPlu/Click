using System;
using System.Collections.Generic;
using System.IO;
using ClickDungeon.Application;
using ClickDungeon.Content;
using ClickDungeon.Domain;
using ClickDungeon.Simulation;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using static ClickDungeon.Tests.Scenario;

namespace ClickDungeon.Tests
{
    /// <summary>DATA-01 / DATA-02: saves that would crash or diverge later are rejected at load, so the backup can take over.</summary>
    public class SaveValidationTests
    {
        string _dir;

        [SetUp]
        public void SetUp() => _dir = Path.Combine(Path.GetTempPath(), "clickdungeon-validation-" + Guid.NewGuid().ToString("N"));

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
        }

        static RunState NewRun() => RunFactory.NewRun(5UL, Catalog, new List<GameEvent>());

        static string Json(Action<RunState> change)
        {
            var run = NewRun();
            change(run);
            return SaveSerializer.ToJson(run);
        }

        static string JsonWith(string property, JToken value)
        {
            var o = JObject.Parse(SaveSerializer.ToJson(NewRun()));
            o[property] = value;
            return o.ToString();
        }

        [Test]
        public void ValidSavesStillLoad()
        {
            var run = NewRun();
            Bot.Play(run, 10, 3UL);
            Assert.That(SaveSerializer.FromJson(SaveSerializer.ToJson(run)).RunSeed, Is.EqualTo(5UL));
        }

        [TestCase("7")]
        [TestCase("\"7\"")]
        [TestCase("\"Easy, Hardcore\"")]
        [TestCase("\"Nightmare\"")]
        public void UndefinedDifficultiesAreRejected(string value)
        {
            var json = JsonWith("Difficulty", JToken.Parse(value));
            Assert.Throws<FormatException>(() => SaveSerializer.FromJson(json));
        }

        [Test]
        public void BrokenStateIsRejected()
        {
            var broken = new Dictionary<string, Action<RunState>>
            {
                ["null enemy list"] = r => r.Floor.Enemies = null,
                ["null reward list"] = r => r.Rewards = null,
                ["null tile"] = r => r.Floor.Cells[3] = null,
                ["enemy off the board"] = r => r.Floor.Enemies[0].Pos = new GridPos(9, 9),
                ["hero off the board"] = r => r.Hero.Pos = new GridPos(-1, -1),
                ["exit off the board"] = r => r.Floor.Exit = new GridPos(7, 7),
                ["hero inside a wall"] = r => r.Floor[r.Hero.Pos].Terrain = Terrain.Wall,
                ["hero health above max"] = r => r.Hero.Hp = r.Hero.MaxHp + 5,
                ["floor beyond the run"] = r => r.Floor.FloorIndex = r.FloorCount + 1,
                ["newer ruleset"] = r => r.RulesetVersion = Versions.Ruleset + 1,
                ["other floor generation"] = r => r.GenerationVersion = Versions.Generation + 1,
            };
            foreach (var entry in broken)
            {
                Assume.That(NewRun().Floor.Enemies.Count, Is.GreaterThan(0));
                var json = Json(entry.Value);
                Assert.Throws<FormatException>(() => SaveSerializer.FromJson(json), entry.Key);
            }
        }

        [Test]
        public void EveryStateTheGameProducesPassesValidation()
        {
            // Saving verifies through FromJson, so a validation rule that rejects a real state would silently stop saves.
            bool sawWon = false, sawLost = false, sawBoss = false;
            // Both movement modes, because they produce different states: Free Roam crosses the board, Step by Step chases (D-021).
            foreach (var (tier, mistakes, movement) in new[]
            {
                (Difficulty.Easy, 0.0, MovementMode.Free),
                (Difficulty.Medium, 0.5, MovementMode.Free),
                (Difficulty.Hardcore, 0.7, MovementMode.Step),
            })
            {
                var catalog = ContentCatalog.CreateDefault(tier);
                for (ulong seed = 1; seed <= 4; seed++)
                {
                    var run = RunFactory.NewRun(seed, catalog, new List<GameEvent>(), ContentCatalog.DefaultHeroId, movement);
                    var player = new AutoPlayer(mistakes);
                    for (int i = 0; i < 400 && run.Status == RunStatus.InProgress; i++)
                    {
                        TurnResolver.Apply(run, player.Choose(run, catalog, seed * 13 + (ulong)i), catalog);
                        sawBoss |= run.Floor.IsBossFloor;
                        var json = SaveSerializer.ToJson(run);
                        Assert.DoesNotThrow(() => SaveSerializer.FromJson(json), $"{tier} seed {seed} command {i} ({run.Status})");
                    }
                    sawWon |= run.Status == RunStatus.Won;
                    sawLost |= run.Status == RunStatus.Lost;
                }
            }
            Assert.That(sawWon && sawLost && sawBoss, Is.True, "The runs must reach a win, a loss and Blobert's floor.");
        }

        [Test]
        public void DeleteThatFailsPartWayNeverLeavesAResumableRun()
        {
            Assume.That(Environment.OSVersion.Platform, Is.EqualTo(PlatformID.Win32NT), "Relies on Windows file locking.");
            var store = new FileSaveStore(_dir);
            var run = NewRun();
            store.Save(run);
            run.Status = RunStatus.Lost;
            store.Save(run);   // the finished run is written first; the backup still holds the turn before the end

            using (new FileStream(store.BackupPath, FileMode.Open, FileAccess.Read, FileShare.None))
                Assert.That(() => store.Delete(), Throws.InstanceOf<IOException>());

            Assert.That(store.TryLoad(out var left, out _), Is.True);
            Assert.That(left.Status, Is.EqualTo(RunStatus.Lost), "The newest save remains, not the in-progress backup.");
            Assert.That(new GameSession(Catalog, store).TryContinue(out _), Is.False);
        }

        [Test]
        public void OlderRulesetsLoadUnderTheCurrentRules()
        {
            var json = Json(r => r.RulesetVersion = 1);
            Assert.That(SaveSerializer.FromJson(json).RulesetVersion, Is.EqualTo(Versions.Ruleset));
        }

        [Test]
        public void TamperedMainSaveFallsBackToTheBackup()
        {
            var store = new FileSaveStore(_dir);
            var run = NewRun();
            store.Save(run);
            Bot.Play(run, 3, 9UL);
            store.Save(run);

            var o = JObject.Parse(File.ReadAllText(store.MainPath));
            o["Difficulty"] = 7;
            File.WriteAllText(store.MainPath, o.ToString());

            Assert.That(store.TryLoad(out var restored, out var message), Is.True);
            Assert.That(message, Does.Contain("backup"));
            Assert.That(Enum.IsDefined(typeof(Difficulty), restored.Difficulty), Is.True);
        }

        [Test]
        public void ContinueRefusesContentThisBuildDoesNotHave()
        {
            var store = new FileSaveStore(_dir);
            var run = NewRun();
            run.Floor.Enemies[0].DefId = "dragon";
            store.Save(run);

            var session = new GameSession(Catalog, store);
            bool continued = true;
            string message = null;
            Assert.DoesNotThrow(() => continued = session.TryContinue(out message));
            Assert.That(continued, Is.False);
            Assert.That(message, Does.Contain("dragon"));
            Assert.That(session.Run, Is.Null);
        }
    }
}
