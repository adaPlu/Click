using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ClickDungeon.Application;
using ClickDungeon.Domain;
using ClickDungeon.Simulation;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using static ClickDungeon.Tests.Scenario;

namespace ClickDungeon.Tests
{
    public class TelemetryTests
    {
        static readonly DateTime FixedTime = new DateTime(2026, 9, 14, 12, 0, 0, DateTimeKind.Utc);

        static TelemetryRecorder Recorder(ITelemetrySink sink) => new TelemetryRecorder(sink, Catalog, () => FixedTime, "test-session");

        static CommandResult Play(TelemetryRecorder recorder, RunState run, PlayerCommand command)
        {
            var pending = recorder.Begin(run, command);
            var result = TurnResolver.Apply(run, command, Catalog);
            recorder.Complete(pending, run, result);
            return result;
        }

        static JObject Single(MemoryTelemetrySink sink, string name)
        {
            var events = sink.Named(name);
            Assert.That(events.Count, Is.EqualTo(1), $"Expected one '{name}' event.");
            return JObject.Parse(events[0].ToJson());
        }

        [Test]
        public void NewRunEmitsRunAndFloorStart()
        {
            var sink = new MemoryTelemetrySink();
            var session = new GameSession(Catalog, null, Recorder(sink));
            session.StartNewRun(11UL);

            var started = Single(sink, "run_started");
            Assert.That((string)started["session"], Is.EqualTo("test-session"));
            Assert.That((ulong)started["run"], Is.EqualTo(11UL));
            var floor = Single(sink, "floor_started");
            Assert.That((string)floor["data"]["template"], Is.Not.Empty);
            Assert.That(sink.Named("tile_revealed"), Is.Not.Empty);
        }

        [Test]
        public void TileChoiceRecordsOnlyWhatThePlayerCouldSee()
        {
            var run = Run(
                ".....",
                ".g...",
                ".....",
                ".H...",
                ".....");
            var sink = new MemoryTelemetrySink();
            Play(Recorder(sink), run, PlayerCommand.Move(P(2, 1)));

            var choice = Single(sink, "tile_choice");
            var data = choice["data"];
            Assert.That((bool)data["consequential"], Is.True);
            Assert.That((string)data["chosen"], Is.EqualTo("2,1"));
            var options = (JArray)data["options"];
            Assert.That(options.Count, Is.EqualTo(4));
            var towardEnemy = options.First(o => (string)o["cell"] == "1,2");
            Assert.That((int)towardEnemy["known_enemy"], Is.EqualTo(1));
            Assert.That((int)options[(int)data["chosen_index"]]["known_enemy"], Is.EqualTo(0));
            Assert.That((int)data["hero"]["hp"], Is.EqualTo(10));
        }

        [Test]
        public void IdenticalOptionsAreNotConsequential()
        {
            var run = Run(
                ".....",
                ".....",
                "..H..",
                ".....",
                ".....");
            run.Floor.Exit = GridPos.Invalid;
            foreach (var cell in run.Floor.Cells) cell.Knowledge = Knowledge.Revealed;
            var sink = new MemoryTelemetrySink();
            Play(Recorder(sink), run, PlayerCommand.Move(P(2, 3)));
            Assert.That((bool)Single(sink, "tile_choice")["data"]["consequential"], Is.False);
        }

        [Test]
        public void DamageSourceAndThreatAreRecorded()
        {
            var run = Run(
                ".....",
                ".....",
                ".HG..",
                ".....",
                ".....");
            var sink = new MemoryTelemetrySink();
            Play(Recorder(sink), run, PlayerCommand.Wait());

            var damage = Single(sink, "damage_taken");
            Assert.That((string)damage["data"]["source"], Is.EqualTo("goblin"));
            Assert.That((int)damage["data"]["hp_after"], Is.EqualTo(8));
            var ability = Single(sink, "ability_used");
            Assert.That((string)ability["data"]["ability"], Is.EqualTo("wait"));
            Assert.That((int)ability["data"]["threat_here"], Is.EqualTo(2));
        }

        [Test]
        public void SkippedRewardsAreRecordedWhenLeavingAFloor()
        {
            var run = Run(
                "..KHX",
                ".....",
                ".....",
                ".....",
                "C....");
            var sink = new MemoryTelemetrySink();
            var recorder = Recorder(sink);
            Play(recorder, run, PlayerCommand.Move(P(2, 4)));
            Play(recorder, run, PlayerCommand.Move(P(3, 4)));
            Play(recorder, run, PlayerCommand.Move(P(4, 4)));

            var completed = Single(sink, "floor_completed");
            Assert.That((int)completed["floor"], Is.EqualTo(1));
            var skipped = Single(sink, "optional_reward_skipped");
            Assert.That((string)skipped["data"]["reward"], Is.EqualTo("chest"));
            Assert.That((string)skipped["data"]["cell"], Is.EqualTo("0,0"));
            Assert.That((int)skipped["floor"], Is.EqualTo(1));
            Assert.That((int)sink.Named("floor_started").Last().Floor, Is.EqualTo(2));
            Assert.That(sink.Named("pickup_collected").Count, Is.EqualTo(1));
        }

        [Test]
        public void ChestOpeningAndDeathAreRecorded()
        {
            var chestRun = Run(
                ".....",
                ".....",
                ".HC..",
                ".....",
                ".....");
            var sink = new MemoryTelemetrySink();
            Play(Recorder(sink), chestRun, PlayerCommand.Interact(P(2, 2)));
            Assert.That((string)Single(sink, "chest_opened")["data"]["reward"], Is.EqualTo(chestRun.Rewards[0].Kind.ToString().ToLowerInvariant()));

            var deathRun = Run(
                ".....",
                ".....",
                "H^...",
                ".....",
                ".....");
            deathRun.Hero.Hp = 1;
            var deathSink = new MemoryTelemetrySink();
            Play(Recorder(deathSink), deathRun, PlayerCommand.Move(P(1, 2)));
            Assert.That((string)Single(deathSink, "run_failed")["data"]["cause"], Is.EqualTo("spikes"));
            Assert.That((string)Single(deathSink, "trap_triggered")["data"]["trap"], Is.EqualTo("spikes"));
        }

        [Test]
        public void RejectedCommandsAreRecorded()
        {
            var run = Run(
                ".....",
                ".....",
                "..H..",
                ".....",
                ".....");
            var sink = new MemoryTelemetrySink();
            Play(Recorder(sink), run, PlayerCommand.Move(P(4, 4)));
            Assert.That((string)Single(sink, "command_rejected")["data"]["reason"], Is.Not.Empty);
            Assert.That(sink.Named("tile_choice"), Is.Empty);
        }

        [Test]
        public void TelemetryNeverChangesTheRun()
        {
            var plain = new GameSession(Catalog, null);
            var recorded = new GameSession(Catalog, null, Recorder(new MemoryTelemetrySink()));
            plain.StartNewRun(5UL);
            recorded.StartNewRun(5UL);

            var rngA = new DeterministicRng(99UL);
            var rngB = new DeterministicRng(99UL);
            for (int i = 0; i < 80; i++)
            {
                if (plain.Run.Status != RunStatus.InProgress) break;
                plain.Submit(PickCommand(plain.Run, rngA));
                recorded.Submit(PickCommand(recorded.Run, rngB));
            }
            Assert.That(SaveSerializer.ToJson(recorded.Run), Is.EqualTo(SaveSerializer.ToJson(plain.Run)));
            Assert.That(recorded.Telemetry.Failures, Is.EqualTo(0));
        }

        [Test]
        public void JsonlSinkWritesOneObjectPerLine()
        {
            var dir = Path.Combine(Path.GetTempPath(), "clickdungeon-telemetry-" + Guid.NewGuid().ToString("N"));
            try
            {
                string path;
                using (var sink = new JsonlTelemetrySink(dir, FixedTime))
                {
                    path = sink.FilePath;
                    var session = new GameSession(Catalog, null, Recorder(sink));
                    session.StartNewRun(3UL);
                    var rng = new DeterministicRng(3UL);
                    for (int i = 0; i < 10 && session.Run.Status == RunStatus.InProgress; i++) session.Submit(PickCommand(session.Run, rng));
                }

                var lines = File.ReadAllLines(path);
                Assert.That(lines.Length, Is.GreaterThan(10));
                foreach (var line in lines) Assert.That((string)JObject.Parse(line)["event"], Is.Not.Empty);
            }
            finally
            {
                if (Directory.Exists(dir)) Directory.Delete(dir, true);
            }
        }

        [Test]
        public void JsonlSinkCreatesNoFileUntilSomethingIsLogged()
        {
            var dir = Path.Combine(Path.GetTempPath(), "clickdungeon-telemetry-" + Guid.NewGuid().ToString("N"));
            try
            {
                string path;
                using (var sink = new JsonlTelemetrySink(dir, FixedTime))
                {
                    path = sink.FilePath;
                    new GameSession(Catalog, null, Recorder(sink));
                }
                Assert.That(File.Exists(path), Is.False);
            }
            finally
            {
                if (Directory.Exists(dir)) Directory.Delete(dir, true);
            }
        }

        [Test]
        public void SummaryAnswersGateTwoQuestions()
        {
            var sink = new MemoryTelemetrySink();
            var recorder = Recorder(sink);

            var laneRun = Run(
                ".....",
                ".....",
                ".H.I.",
                ".....",
                ".....");
            Play(recorder, laneRun, PlayerCommand.Move(P(2, 2)));

            var deathRun = Run(
                ".....",
                ".....",
                "H^...",
                ".....",
                ".....");
            deathRun.Hero.Hp = 1;
            Play(recorder, deathRun, PlayerCommand.Move(P(1, 2)));

            var summary = TelemetrySummary.FromLines(sink.Events.Select(e => e.ToJson()).Concat(new[] { "not json" }));
            Assert.That(summary.TileChoices, Is.EqualTo(2));
            Assert.That(summary.SteppedIntoAvoidableDamage, Is.EqualTo(1));
            Assert.That(summary.RunsFailed, Is.EqualTo(1));
            Assert.That(summary.DeathCauses["spikes"], Is.EqualTo(1));
            Assert.That(summary.MalformedLines, Is.EqualTo(1));
            Assert.That(summary.ToMarkdown(), Does.Contain("consistent with visible information"));
        }

        static PlayerCommand PickCommand(RunState run, DeterministicRng rng)
        {
            var slashes = Commands.LegalTargets(run, CommandKind.Slash, Catalog);
            if (slashes.Count > 0) return PlayerCommand.Slash(slashes[0]);
            var moves = Commands.LegalTargets(run, CommandKind.Move, Catalog);
            return moves.Count > 0 ? PlayerCommand.Move(moves[rng.Next(moves.Count)]) : PlayerCommand.Wait();
        }
    }
}
