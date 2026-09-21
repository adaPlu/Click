using System.IO;
using System.Linq;
using ClickDungeon.Application;
using ClickDungeon.Content;
using ClickDungeon.Domain;
using ClickDungeon.Simulation;
using NUnit.Framework;
using static ClickDungeon.Tests.Scenario;

namespace ClickDungeon.Tests
{
    /// <summary>D-030: achievements counted over every run, and the mail that carries their gifts.</summary>
    public class CrownAndMailTests
    {
        [Test]
        public void ARunCountsItsMonstersAndChests()
        {
            var run = Run(".....", ".....", ".HG..", ".....", ".....");
            var goblin = Enemy(run, "goblin");
            goblin.Hp = 1;
            DoOk(run, PlayerCommand.Slash(goblin.Pos));
            Assert.That(run.MonstersSlain, Is.EqualTo(1));

            var chest = Run(".....", ".....", ".HC..", ".....", ".....");
            OpenChest(chest, P(2, 2));
            Assert.That(chest.ChestsOpened, Is.EqualTo(1));
        }

        [Test]
        public void BankingAddsTheRunsCountsToTheProfile()
        {
            var profile = new ProfileState { DeepestFloor = 2 };
            var run = new RunState
            {
                Hero = new HeroState(), Floor = new FloorState { FloorIndex = 4 }, Status = RunStatus.Lost,
                MonstersSlain = 6, ChestsOpened = 3, CoinsFound = 40,
            };
            ProfileSystem.Bank(profile, run);
            Assert.That(profile.MonstersSlain, Is.EqualTo(6));
            Assert.That(profile.ChestsOpened, Is.EqualTo(3));
            Assert.That(profile.CoinsEarned, Is.EqualTo(40));
            Assert.That(profile.DeepestFloor, Is.EqualTo(4));

            run.Floor.FloorIndex = 1;
            ProfileSystem.Bank(profile, run);
            Assert.That(profile.DeepestFloor, Is.EqualTo(4), "The deepest floor never goes back up.");
        }

        [Test]
        public void AnAchievementIsEarnedOnceAndMailsItsGift()
        {
            var profile = new ProfileState { RunsFinished = 1 };
            var earned = Achievements.Check(profile, Catalog);
            Assert.That(earned.Select(a => a.Id), Is.EqualTo(new[] { "first_steps" }));
            Assert.That(Achievements.Check(profile, Catalog), Is.Empty, "Earned once.");

            var letter = profile.Mail.Single();
            Assert.That(letter.Subject, Does.Contain("First Steps"));
            Assert.That(letter.Coins, Is.EqualTo(25));
            Assert.That(profile.Coins, Is.Zero, "The gift waits in the letter.");
            Assert.That(Mailbox.Unread(profile), Is.EqualTo(1));

            Assert.That(Mailbox.Collect(profile, letter.Id), Is.True);
            Assert.That(profile.Coins, Is.EqualTo(25));
            Assert.That(Mailbox.Collect(profile, letter.Id), Is.False, "Collected once.");
            Assert.That(profile.Coins, Is.EqualTo(25));
            Assert.That(Mailbox.Unread(profile), Is.Zero);
        }

        /// <summary>
        /// SEC-04: a gift only ever adds. A hand-edited letter carrying a negative amount used to be collected as written,
        /// taking coins out of the purse — and the purse is only clamped back to zero on the next load.
        /// </summary>
        [Test]
        public void AGiftCanOnlyEverAddToThePurse()
        {
            var profile = new ProfileState { Coins = 100, Gems = 5 };
            // A letter worth collecting overall, with one amount edited to run the other way.
            var letter = Mailbox.Post(profile, "Nobody", "Hand-edited", "Written into the file by hand.",
                new RewardBundle { Gems = 2000, Coins = -1000, Label = "a trap" });

            Assert.That(Mailbox.Collect(profile, letter.Id), Is.True);
            Assert.That(profile.Coins, Is.EqualTo(100), "Nothing was taken out of the purse.");
            Assert.That(profile.Gems, Is.EqualTo(2005), "And what the letter really gave still arrived.");
        }

        [Test]
        public void EveryAchievementCanBeReachedAndGivesSomething()
        {
            Assert.That(Catalog.Achievements.Select(a => a.Id).Distinct().Count(), Is.EqualTo(Catalog.Achievements.Count));
            foreach (var a in Catalog.Achievements)
            {
                Assert.That(a.Target, Is.GreaterThan(0), a.Id);
                var r = a.Reward;
                Assert.That(r.Coins + r.Gems + r.PotionRations + r.HeartTokens + r.SpecialKeys, Is.GreaterThan(0), a.Id);
                if (a.Stat == AchievementStat.DeepestFloor) Assert.That(a.Target, Is.LessThanOrEqualTo(Catalog.RunFloorCount), a.Id);
                if (a.Stat == AchievementStat.ItemsOwned) Assert.That(a.Target, Is.LessThanOrEqualTo(Catalog.Items.Count), a.Id);
            }
        }

        [Test]
        public void ANewProfileIsWelcomedOnceWithAGift()
        {
            var session = new GameSession(Catalog, null);
            var welcome = session.Profile.Mail.Single();
            Assert.That(welcome.HasGift, Is.True);
            Mailbox.Welcome(session.Profile, Catalog);
            Assert.That(session.Profile.Mail.Count, Is.EqualTo(1), "Welcomed once.");
            Assert.That(Mailbox.CollectAll(session.Profile), Is.EqualTo(1));
            Assert.That(session.Profile.PotionRations, Is.EqualTo(Catalog.WelcomeGift.PotionRations));
        }

        [Test]
        public void AFinishedRunSendsLettersForLevelsAndAchievements()
        {
            var session = new GameSession(Catalog, null);
            session.StartNewRun(11UL);
            session.Run.XpEarned = Progression.XpForLevel(3);
            session.Abandon();
            var subjects = session.Profile.Mail.Select(m => m.Subject).ToList();
            Assert.That(subjects, NUnit.Framework.Has.Some.Contains("Level 2"));
            Assert.That(subjects, NUnit.Framework.Has.Some.Contains("Level 3"));
            Assert.That(subjects, NUnit.Framework.Has.Some.Contains("First Steps"));
            Assert.That(Achievements.Earned(session.Profile, "first_steps"), Is.True);
        }

        [Test]
        public void AFullMailboxDropsOldReadLettersButNeverAGift()
        {
            var profile = new ProfileState();
            var gift = Mailbox.Post(profile, "The Crown", "Gift", "", Catalog.WelcomeGift);
            gift.Read = true;
            for (int i = 0; i < Mailbox.Capacity + 5; i++)
                Mailbox.Post(profile, "The Guild", $"Note {i}", "").Read = true;
            Assert.That(profile.Mail.Count, Is.EqualTo(Mailbox.Capacity));
            Assert.That(profile.Mail.Contains(gift), Is.True, "An uncollected gift is kept.");
            Assert.That(profile.Mail.Any(m => m.Subject == "Note 0"), Is.False, "The oldest read letter went.");
        }

        [Test]
        public void MailAndAchievementsSurviveASaveAndLoad()
        {
            string dir = Path.Combine(Path.GetTempPath(), "cd-mail-" + System.Guid.NewGuid().ToString("N"));
            try
            {
                var store = new FileProfileStore(dir);
                var profile = new ProfileState { RunsFinished = 1 };
                Achievements.Check(profile, Catalog);
                store.Save(profile);
                var loaded = store.Load();
                Assert.That(Achievements.Earned(loaded, "first_steps"), Is.True);
                Assert.That(loaded.Mail.Single().Coins, Is.EqualTo(25));
                Assert.That(Mailbox.Post(loaded, "x", "y", "").Id, Is.EqualTo(2), "Letter ids keep counting.");
            }
            finally
            {
                if (Directory.Exists(dir)) Directory.Delete(dir, true);
            }
        }
    }
}
