using System;
using System.Collections.Generic;
using System.IO;
using ClickDungeon.Application;
using ClickDungeon.Domain;
using ClickDungeon.Simulation;
using NUnit.Framework;
using static ClickDungeon.Tests.Scenario;

namespace ClickDungeon.Tests
{
    public class SaveTests
    {
        string _dir;

        [SetUp]
        public void SetUp()
        {
            _dir = Path.Combine(Path.GetTempPath(), "clickdungeon-tests-" + Guid.NewGuid().ToString("N"));
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
        }

        [Test]
        public void JsonRoundTripIsLossless()
        {
            var run = RunFactory.NewRun(99UL, Catalog, new List<GameEvent>());
            Bot.Play(run, 12, 5UL);
            var json = SaveSerializer.ToJson(run);
            Assert.That(SaveSerializer.ToJson(SaveSerializer.FromJson(json)), Is.EqualTo(json));
        }

        [Test]
        public void ResumedRunContinuesIdentically()
        {
            var original = RunFactory.NewRun(123UL, Catalog, new List<GameEvent>());
            Bot.Play(original, 8, 1UL);
            var resumed = SaveSerializer.FromJson(SaveSerializer.ToJson(original));

            Bot.Play(original, 20, 2UL);
            Bot.Play(resumed, 20, 2UL);
            Assert.That(SaveSerializer.ToJson(resumed), Is.EqualTo(SaveSerializer.ToJson(original)));
        }

        [Test]
        public void RejectsUnknownSchemaVersion()
        {
            var run = RunFactory.NewRun(1UL, Catalog, new List<GameEvent>());
            var json = SaveSerializer.ToJson(run).Replace("\"SaveSchemaVersion\": 1", "\"SaveSchemaVersion\": 999");
            Assert.Throws<FormatException>(() => SaveSerializer.FromJson(json));
        }

        [Test]
        public void AtomicStoreKeepsBackupAndRecoversFromCorruption()
        {
            var store = new FileSaveStore(_dir);
            var run = RunFactory.NewRun(5UL, Catalog, new List<GameEvent>());
            store.Save(run);
            Bot.Play(run, 3, 9UL);
            store.Save(run);

            Assert.That(File.Exists(store.BackupPath), Is.True);
            Assert.That(File.Exists(store.TempPath), Is.False);

            File.WriteAllText(store.MainPath, "{ broken");
            Assert.That(store.TryLoad(out var restored, out var message), Is.True);
            Assert.That(message, Does.Contain("backup"));
            Assert.That(restored.RunSeed, Is.EqualTo(5UL));

            store.Delete();
            Assert.That(store.Exists, Is.False);
        }

        [Test]
        public void SessionAutosavesAndClearsSaveOnDeath()
        {
            var session = new GameSession(Catalog, new FileSaveStore(_dir));
            session.StartNewRun(77UL);
            session.Submit(PlayerCommand.Wait());
            Assert.That(session.CanContinue, Is.True);

            var resumed = new GameSession(Catalog, new FileSaveStore(_dir));
            Assert.That(resumed.TryContinue(out _), Is.True);
            Assert.That(SaveSerializer.ToJson(resumed.Run), Is.EqualTo(SaveSerializer.ToJson(session.Run)));

            resumed.Run.Hero.Hp = 0;
            resumed.Run.Floor.Enemies.Clear();
            resumed.Run.Hero.Hp = 1;
            PlaceSpikeNextToHero(resumed.Run);
            var result = resumed.Submit(PlayerCommand.Move(SpikeCell(resumed.Run)));
            Assert.That(result.Accepted, Is.True, result.RejectReason);
            Assert.That(resumed.Run.Status, Is.EqualTo(RunStatus.Lost));
            Assert.That(resumed.CanContinue, Is.False);
        }

        static GridPos SpikeCell(RunState run)
        {
            foreach (var d in Directions.All)
            {
                var n = run.Hero.Pos.Step(d);
                if (n.InBounds && run.Floor[n].Hazard == HazardKind.Spikes) return n;
            }
            return GridPos.Invalid;
        }

        static void PlaceSpikeNextToHero(RunState run)
        {
            foreach (var d in Directions.All)
            {
                var n = run.Hero.Pos.Step(d);
                if (!n.InBounds || !Board.HeroCanEnter(run, n)) continue;
                var cell = run.Floor[n];
                cell.Content = ContentKind.None;
                cell.IsExit = false;
                cell.Hazard = HazardKind.Spikes;
                return;
            }
            Assert.Fail("No free neighbour for the spike.");
        }
    }

    public class DeterminismFuzzTests
    {
        [Test]
        public void BotRunsNeverBreakInvariants()
        {
            for (ulong seed = 1; seed <= 60; seed++)
            {
                var run = RunFactory.NewRun(seed, Catalog, new List<GameEvent>());
                for (int step = 0; step < 250 && run.Status == RunStatus.InProgress; step++)
                {
                    Bot.Play(run, 1, seed * 31 + (ulong)step);
                    AssertInvariants(run, seed, step);
                }
            }
        }

        [Test]
        public void SameSeedAndInputsGiveIdenticalRuns()
        {
            for (ulong seed = 1; seed <= 10; seed++)
            {
                var a = RunFactory.NewRun(seed, Catalog, new List<GameEvent>());
                var b = RunFactory.NewRun(seed, Catalog, new List<GameEvent>());
                Bot.Play(a, 60, seed);
                Bot.Play(b, 60, seed);
                Assert.That(SaveSerializer.ToJson(a), Is.EqualTo(SaveSerializer.ToJson(b)));
            }
        }

        static void AssertInvariants(RunState run, ulong seed, int step)
        {
            string where = $"seed {seed} step {step}";
            var hero = run.Hero;
            Assert.That(hero.Hp, Is.InRange(0, hero.MaxHp), where);
            Assert.That(hero.Pos.InBounds, Is.True, where);
            Assert.That(run.Floor[hero.Pos].Terrain, Is.EqualTo(Terrain.Floor), where);
            var positions = new HashSet<GridPos> { hero.Pos };
            foreach (var enemy in run.Floor.Enemies)
            {
                Assert.That(positions.Add(enemy.Pos), Is.True, $"{where}: overlapping actors at {enemy.Pos}");
                Assert.That(Board.BlocksMovement(run.Floor[enemy.Pos]), Is.False, where);
                Assert.That(enemy.Hp, Is.GreaterThan(0), where);
                if (enemy.Awake) Assert.That(enemy.Intent.Kind, Is.Not.EqualTo(IntentKind.None), where);
            }
        }
    }

    /// <summary>Deterministic random player used for fuzzing and determinism checks.</summary>
    public static class Bot
    {
        public static void Play(RunState run, int turns, ulong seed)
        {
            var rng = new DeterministicRng(seed);
            for (int i = 0; i < turns && run.Status == RunStatus.InProgress; i++)
            {
                var options = new List<PlayerCommand>();
                foreach (var kind in new[] { CommandKind.Move, CommandKind.Slash, CommandKind.Dash, CommandKind.Interact })
                foreach (var target in Commands.LegalTargets(run, kind, Catalog))
                    options.Add(new PlayerCommand(kind, target));
                foreach (var kind in new[] { CommandKind.Shield, CommandKind.Potion, CommandKind.Wait })
                    if (Commands.Validate(run, new PlayerCommand(kind, GridPos.Invalid), Catalog, out _))
                        options.Add(new PlayerCommand(kind, GridPos.Invalid));

                var command = options[rng.Next(options.Count)];
                var result = TurnResolver.Apply(run, command, Catalog);
                Assert.That(result.Accepted, Is.True, $"Legal command {command} rejected: {result.RejectReason}");
            }
        }
    }
}
