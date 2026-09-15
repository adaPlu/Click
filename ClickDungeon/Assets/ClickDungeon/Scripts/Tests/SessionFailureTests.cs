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
    /// <summary>REL-01: a failing save store never interrupts play, and a finished run can never resume.</summary>
    public class SessionFailureTests
    {
        sealed class FlakyStore : ISaveStore
        {
            public bool FailSave;
            public bool FailDelete;
            public readonly List<string> Calls = new List<string>();
            public RunStatus? Saved;

            public bool Exists => Saved != null;

            public void Save(RunState run)
            {
                Calls.Add("save");
                if (FailSave) throw new UnauthorizedAccessException("save blocked");
                Saved = run.Status;
            }

            public bool TryLoad(out RunState run, out string message)
            {
                run = null;
                message = null;
                return false;
            }

            public void Delete()
            {
                Calls.Add("delete");
                if (FailDelete) throw new IOException("delete blocked");
                Saved = null;
            }
        }

        static void StepOntoLethalSpikes(GameSession session)
        {
            var run = session.Run;
            run.Floor.Enemies.Clear();
            run.Hero.Hp = 1;
            foreach (var d in Directions.All)
            {
                var p = run.Hero.Pos.Step(d);
                if (!Board.HeroCanEnter(run, p)) continue;
                run.Floor[p].Hazard = HazardKind.Spikes;
                run.Floor[p].Content = ContentKind.None;
                run.Floor[p].Knowledge = Knowledge.Revealed;
                var result = session.Submit(PlayerCommand.Move(p));
                Assert.That(result.Accepted, Is.True, result.RejectReason);
                return;
            }
            Assert.Fail("No free neighbour for the spikes.");
        }

        [Test]
        public void SaveFailureKeepsPlayingAndIsReported()
        {
            var store = new FlakyStore();
            var sink = new MemoryTelemetrySink();
            var session = new GameSession(Catalog, store, new TelemetryRecorder(sink, Catalog));
            session.StartNewRun(7UL);
            int turn = session.Run.Turn;
            int events = sink.Events.Count;

            store.FailSave = true;
            CommandResult result = null;
            Assert.DoesNotThrow(() => result = session.Submit(PlayerCommand.Wait()));
            Assert.That(result.Accepted, Is.True);
            Assert.That(session.Run.Turn, Is.EqualTo(turn + 1));
            Assert.That(session.SaveError, Does.Contain("save blocked"));
            Assert.That(sink.Events.Count, Is.GreaterThan(events), "Telemetry still records the turn.");

            store.FailSave = false;
            session.Submit(PlayerCommand.Wait());
            Assert.That(session.SaveError, Is.Null, "The warning clears once saving works again.");
        }

        [Test]
        public void DeathWithAFailingDeleteLeavesAFinishedSaveNotTheLastTurn()
        {
            var store = new FlakyStore();
            var session = new GameSession(Catalog, store);
            session.StartNewRun(8UL);
            store.FailDelete = true;

            Assert.DoesNotThrow(() => StepOntoLethalSpikes(session));
            Assert.That(session.Run.Status, Is.EqualTo(RunStatus.Lost));
            Assert.That(store.Calls.GetRange(store.Calls.Count - 2, 2), Is.EqualTo(new[] { "save", "delete" }), "The finished run is written before the delete.");
            Assert.That(store.Saved, Is.EqualTo(RunStatus.Lost), "What remains on disk cannot be continued.");
            Assert.That(session.SaveError, Does.Contain("delete blocked"));
        }

        [Test]
        public void AbandonNeverThrowsWhenTheDeleteFails()
        {
            var store = new FlakyStore();
            var session = new GameSession(Catalog, store);
            session.StartNewRun(9UL);
            store.FailDelete = true;

            Assert.DoesNotThrow(session.Abandon);
            Assert.That(session.Run, Is.Null);
        }

        [Test]
        public void StartingARunSurvivesASaveFailure()
        {
            var store = new FlakyStore { FailSave = true };
            var session = new GameSession(Catalog, store);

            Assert.DoesNotThrow(() => session.StartNewRun(10UL));
            Assert.That(session.Run, Is.Not.Null);
            Assert.That(session.SaveError, Does.Contain("save blocked"));
        }
    }
}
