using System;
using System.IO;
using ClickDungeon.Application;
using ClickDungeon.Domain;
using NUnit.Framework;
using static ClickDungeon.Tests.Scenario;

namespace ClickDungeon.Tests
{
    /// <summary>D-029: one claim a day, a week that climbs while the player keeps coming back, and a fresh start after a gap.</summary>
    public class DailyRewardTests
    {
        static readonly DateTime Monday = new DateTime(2026, 9, 14, 20, 30, 0);

        [Test]
        public void TheFirstClaimPaysDayOneAndOnlyOncePerDay()
        {
            var profile = new ProfileState();
            Assert.That(DailyReward.CanClaim(profile, Monday), Is.True);
            var reward = DailyReward.Claim(profile, Catalog, Monday);
            Assert.That(reward, Is.SameAs(Catalog.DailyRewards[0]));
            Assert.That(profile.Coins, Is.EqualTo(reward.Coins));
            Assert.That(DailyReward.CanClaim(profile, Monday.AddHours(3)), Is.False, "Later the same day.");
            Assert.That(DailyReward.Claim(profile, Catalog, Monday.AddHours(3)), Is.Null);
            Assert.That(DailyReward.CanClaim(profile, Monday.Date.AddDays(1)), Is.True, "Just after midnight.");
        }

        [Test]
        public void EachDayInARowPaysTheNextDayOfTheWeekThenTheWeekStartsOver()
        {
            var profile = new ProfileState();
            int week = Catalog.DailyRewards.Count;
            for (int i = 0; i < week + 2; i++)
            {
                var day = Monday.AddDays(i);
                Assert.That(DailyReward.NextDay(profile, Catalog, day), Is.EqualTo(i % week + 1), $"Day {i}.");
                Assert.That(DailyReward.Claim(profile, Catalog, day), Is.SameAs(Catalog.DailyRewards[i % week]));
            }
        }

        [Test]
        public void AMissedDayStartsTheWeekOver()
        {
            var profile = new ProfileState();
            DailyReward.Claim(profile, Catalog, Monday);
            DailyReward.Claim(profile, Catalog, Monday.AddDays(1));
            DailyReward.Claim(profile, Catalog, Monday.AddDays(2));
            Assert.That(DailyReward.NextDay(profile, Catalog, Monday.AddDays(3)), Is.EqualTo(4));
            Assert.That(DailyReward.NextDay(profile, Catalog, Monday.AddDays(4)), Is.EqualTo(1), "Skipped a day.");
            Assert.That(DailyReward.Claim(profile, Catalog, Monday.AddDays(4)), Is.SameAs(Catalog.DailyRewards[0]));
        }

        [Test]
        public void TurningTheClockBackCannotClaimAgain()
        {
            var profile = new ProfileState();
            DailyReward.Claim(profile, Catalog, Monday);
            Assert.That(DailyReward.CanClaim(profile, Monday.AddDays(-3)), Is.False);
        }

        [Test]
        public void EveryDayGivesSomethingTheGameAlreadyHas()
        {
            Assert.That(Catalog.DailyRewards.Count, Is.EqualTo(7));
            foreach (var reward in Catalog.DailyRewards)
            {
                Assert.That(reward.Label, Is.Not.Empty);
                Assert.That(reward.Coins + reward.Gems + reward.PotionRations + reward.HeartTokens + reward.SpecialKeys, Is.GreaterThan(0));
            }
            var profile = new ProfileState();
            for (int i = 0; i < 7; i++) DailyReward.Claim(profile, Catalog, Monday.AddDays(i));
            Assert.That(profile.PotionRations, Is.EqualTo(1));
            Assert.That(profile.HeartTokens, Is.EqualTo(1));
            Assert.That(profile.SpecialKeys, Is.EqualTo(1));
            Assert.That(profile.Gems, Is.EqualTo(3));
        }

        [Test]
        public void TheClaimSurvivesASaveAndLoad()
        {
            string dir = Path.Combine(Path.GetTempPath(), "cd-daily-" + Guid.NewGuid().ToString("N"));
            try
            {
                var store = new FileProfileStore(dir);
                var profile = new ProfileState();
                DailyReward.Claim(profile, Catalog, Monday);
                store.Save(profile);
                var loaded = store.Load();
                Assert.That(DailyReward.CanClaim(loaded, Monday), Is.False);
                Assert.That(DailyReward.NextDay(loaded, Catalog, Monday.AddDays(1)), Is.EqualTo(2));
            }
            finally
            {
                if (Directory.Exists(dir)) Directory.Delete(dir, true);
            }
        }
    }
}
