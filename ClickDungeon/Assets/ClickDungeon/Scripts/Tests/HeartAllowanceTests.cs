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
    /// <summary>
    /// D-077: what a run's chests are allowed to add to the health bar. The hero outgrew the dungeon - a finished
    /// build ended runs on 52 hearts against monsters that hit for 4 - and chests were the biggest single source,
    /// adding more than gear, talents and the shop put together.
    /// </summary>
    public class HeartAllowanceTests
    {
        static int HeartsGranted(RunState run) =>
            run.Rewards.Where(r => r.Kind == RewardKind.MaxHp).Sum(r => r.Amount);

        static RunState PlayOut(ContentCatalog catalog, ulong seed, ProfileState profile)
        {
            var run = RunFactory.NewRun(seed, catalog, new List<GameEvent>(), "ironheart");
            ProfileSystem.ProvisionRun(profile, run, catalog, new List<GameEvent>());
            var player = new AutoPlayer(AutoPlayer.CasualMistakeRate, blind: true);
            for (int i = 0; i < BalanceTests.MaxCommands && run.Status == RunStatus.InProgress; i++)
                TurnResolver.Apply(run, player.Choose(run, catalog, seed * 7919UL + (ulong)i), catalog);
            return run;
        }

        [Test]
        public void ChestsStopAddingHeartsOnceTheRunHasHadItsAllowance()
        {
            var catalog = ContentCatalog.CreateDefault(Difficulty.Medium);
            Assert.That(catalog.Treasure.MaxHeartsFromChests, Is.GreaterThan(0), "Test setup: the allowance is on.");

            foreach (ulong seed in new ulong[] { 3, 7, 11 })
            {
                var run = PlayOut(catalog, seed, new ProfileState());
                Assert.That(HeartsGranted(run), Is.LessThanOrEqualTo(catalog.Treasure.MaxHeartsFromChests),
                    $"seed {seed}: the chests handed out more hearts than the run is allowed.");
            }
        }

        [Test]
        public void AHeroWhoArrivesBulkyIsHandedLessThanOneWhoArrivesWithNothing()
        {
            // The whole point of measuring the allowance down from what the profile brought. A newcomer needs the
            // hearts to survive at all - cutting them flat took a fresh player from 85% of runs won to 13% - so the
            // cut has to fall on the hero who no longer needs them.
            var catalog = ContentCatalog.CreateDefault(Difficulty.Medium);
            int fresh = 0, built = 0;
            foreach (ulong seed in new ulong[] { 3, 7, 11 })
            {
                fresh += HeartsGranted(PlayOut(catalog, seed, new ProfileState()));
                built += HeartsGranted(PlayOut(catalog, seed, BalanceTests.BuiltUp(catalog, "knight")));
            }
            Assert.That(built, Is.LessThan(fresh),
                $"A built-up hero was handed {built} hearts and a newcomer {fresh}: the allowance is not reaching the right hero.");
        }

        [Test]
        public void AChestPastTheAllowancePaysAPotionRatherThanNothing()
        {
            // A chest is always worth opening. It stops adding to a health bar that has outgrown the dungeon; it does
            // not stop paying.
            var catalog = ContentCatalog.CreateDefault(Difficulty.Medium);
            catalog.Treasure.MaxHeartsFromChests = 1;   // exhausted by the first heart reward
            var run = PlayOut(catalog, 3UL, new ProfileState());

            Assert.That(run.Rewards, Is.Not.Empty, "Test setup: the run opened chests.");
            Assert.That(HeartsGranted(run), Is.LessThanOrEqualTo(1));
            Assert.That(run.Rewards.Any(r => r.Kind == RewardKind.Potion), Is.True,
                "The rolls that would have been hearts came back as potions.");
            foreach (var reward in run.Rewards)
                Assert.That(reward.Amount, Is.GreaterThan(0), $"{reward.Kind} paid nothing at all.");
        }

        [Test]
        public void TheAllowanceSurvivesASaveAndCannotBeFarmedByReopeningAVault()
        {
            // It is counted off the reward log rather than a field of its own, which is what makes both of these true:
            // the log travels with the save, and a reopened vault is refused by transaction id (REL-26).
            var catalog = ContentCatalog.CreateDefault(Difficulty.Medium);
            var run = PlayOut(catalog, 7UL, new ProfileState());
            int granted = HeartsGranted(run);
            Assert.That(granted, Is.GreaterThan(0), "Test setup: some hearts were handed out.");

            var loaded = SaveSerializer.FromJson(SaveSerializer.ToJson(run));
            Assert.That(HeartsGranted(loaded), Is.EqualTo(granted), "The allowance spent so far travelled with the save.");

            // Every transaction id is unique, so no chest - in a vault or anywhere else - pays into it twice.
            var ids = run.Rewards.Select(r => r.TransactionId).ToList();
            Assert.That(ids.Distinct().Count(), Is.EqualTo(ids.Count));
        }

        [Test]
        public void AnOldSaveWithNoStartingHeartsRecordedStillGetsAnAllowance()
        {
            // StartingMaxHp is zero on a save written before D-077. That reads as "start from the class's own hearts"
            // rather than as "this hero brought nothing", so an old save is not handed a bigger allowance than a new one.
            var catalog = ContentCatalog.CreateDefault(Difficulty.Medium);
            var run = RunFactory.NewRun(3UL, catalog, new List<GameEvent>(), "ironheart");
            run.StartingMaxHp = 0;
            run.Hero.MaxHp = 40;

            var player = new AutoPlayer(AutoPlayer.CasualMistakeRate, blind: true);
            for (int i = 0; i < BalanceTests.MaxCommands && run.Status == RunStatus.InProgress; i++)
                TurnResolver.Apply(run, player.Choose(run, catalog, 3UL * 7919UL + (ulong)i), catalog);

            Assert.That(HeartsGranted(run), Is.LessThanOrEqualTo(catalog.Treasure.MaxHeartsFromChests));
        }
    }
}
