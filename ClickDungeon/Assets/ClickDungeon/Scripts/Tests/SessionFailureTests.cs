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
    /// <summary>
    /// TEST-10: the profile store's failure branches had no way in — nothing ever handed <see cref="GameSession"/> a store
    /// that fails, so the constructor was only ever seen against <see cref="MemoryProfileStore"/>, which cannot fail. This
    /// is <c>FlakyStore</c> for the other store: settable failure flags, a record of the calls, and what it holds.
    /// </summary>
    public sealed class FlakyProfileStore : IProfileStore
    {
        public bool FailSave;
        public string SaveFailure = "profile write blocked";

        /// <summary>Every call in order, so a test can say what the session did as well as what it ended up with.</summary>
        public readonly List<string> Calls = new List<string>();

        /// <summary>What the store holds, before and after the session writes to it.</summary>
        public ProfileState Held = new ProfileState();

        /// <summary>What the store says about the load it just did. Settable, so a test can play back a store that refuses to be overwritten.</summary>
        public string LoadNotice { get; set; }

        public ProfileState Load()
        {
            Calls.Add("load");
            return Held;
        }

        public void Save(ProfileState profile)
        {
            Calls.Add("save");
            if (FailSave) throw new IOException(SaveFailure);
            Held = profile;
        }
    }

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

        /// <summary>
        /// TEST-10: the constructor writes the profile (the welcome letter moves the mail counter), so a profile store that
        /// throws is exercised before the game has a screen. It must not take the launch down, and it must say so.
        /// </summary>
        [Test]
        public void AProfileWriteThatFailsAtLaunchIsSurvivedAndReported()
        {
            var profiles = new FlakyProfileStore { FailSave = true };

            GameSession session = null;
            Assert.DoesNotThrow(() => session = new GameSession(Catalog, new FlakyStore(), null, profiles));
            Assert.That(profiles.Calls, Is.EqualTo(new[] { "load", "save" }), "The welcome letter is written back at launch.");
            Assert.That(session.Profile, Is.Not.Null);
            Assert.That(session.ProfileNotice, Does.Contain("could not be saved"));
            Assert.That(session.SaveError, Does.Contain("profile write blocked"), "The reason reaches the log.");
        }

        [Test]
        public void AProfileStoreThatWorksKeepsTheWelcomeLetter()
        {
            var profiles = new FlakyProfileStore();
            var session = new GameSession(Catalog, new FlakyStore(), null, profiles);

            Assert.That(session.ProfileNotice, Is.Null, "A profile that reads and writes cleanly says nothing.");
            Assert.That(profiles.Held.Mail.Count, Is.EqualTo(1), "And the letter is on disk, not only in memory.");
            Assert.That(profiles.Held, Is.SameAs(session.Profile));
        }

        /// <summary>
        /// REL-24: a store that could not set the damaged profile aside refuses every write, so the constructor's own save
        /// fails within a line of the load. The load's notice is the only one that says where the file is — the generic
        /// write failure must not take its place, or the player is told to do nothing in particular.
        /// </summary>
        [Test]
        public void ALoadNoticeSurvivesTheWriteFailureItCauses()
        {
            var profiles = new FlakyProfileStore
            {
                FailSave = true,
                LoadNotice = "Your profile could not be read, and could not be set aside either. It will not be overwritten: " +
                             "close the game and move profile.json somewhere safe.",
            };

            var session = new GameSession(Catalog, new FlakyStore(), null, profiles);

            Assert.That(profiles.Calls, Does.Contain("save"), "The constructor really did try to write.");
            Assert.That(session.ProfileNotice, Does.Contain("profile.json"), "The player is still told where their profile is.");
            Assert.That(session.ProfileNotice, Does.Not.Contain("could not be saved"), "The write failure did not take the notice's place.");

            // Once the player has been shown it, the screen clears it — and the next write failure may speak.
            session.ProfileNotice = null;
            session.SaveProfile();
            Assert.That(session.ProfileNotice, Does.Contain("could not be saved"));
        }
    }
}
