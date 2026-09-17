using ClickDungeon.Application;
using ClickDungeon.Domain;
using ClickDungeon.Simulation;
using NUnit.Framework;
using static ClickDungeon.Tests.Scenario;

namespace ClickDungeon.Tests
{
    public class KeyAndExitTests
    {
        [Test]
        public void LockedExitDoesNothingWithoutKey()
        {
            var run = Run(
                "...HX",
                ".....",
                ".....",
                ".....",
                ".....");
            var result = DoOk(run, PlayerCommand.Move(P(4, 4)));
            Assert.That(Has(result, GameEventKind.FloorCompleted), Is.False);
            Assert.That(run.Floor.FloorIndex, Is.EqualTo(1));
        }

        [Test]
        public void KeyUnlocksExitAndDescends()
        {
            var run = Run(
                "..KHX",
                ".....",
                ".....",
                ".....",
                ".....");
            DoOk(run, PlayerCommand.Move(P(2, 4)));
            Assert.That(run.Hero.HasKey, Is.True);
            DoOk(run, PlayerCommand.Move(P(3, 4)));
            var result = DoOk(run, PlayerCommand.Move(P(4, 4)));

            Assert.That(Has(result, GameEventKind.ExitUnlocked), Is.True);
            Assert.That(Has(result, GameEventKind.FloorCompleted), Is.True);
            Assert.That(run.Floor.FloorIndex, Is.EqualTo(2));
            Assert.That(run.Hero.Pos, Is.EqualTo(run.Floor.Start));
            Assert.That(run.Hero.HasKey, Is.False);
            Assert.That(FloorValidator.Validate(run.Floor, Catalog), Is.True);
        }

        [Test]
        public void FinalExitWinsTheRun()
        {
            var run = Run(5, 7UL,
                ".....",
                ".....",
                "Hx...",
                ".....",
                ".....");
            var result = DoOk(run, PlayerCommand.Move(P(1, 2)));
            Assert.That(run.Status, Is.EqualTo(RunStatus.Won));
            Assert.That(Has(result, GameEventKind.RunWon), Is.True);
        }

        [Test]
        public void DeathEndsTheRunAndRejectsFurtherCommands()
        {
            var run = Run(
                ".....",
                ".....",
                "H^...",
                ".....",
                ".....");
            run.Hero.Hp = 1;
            var result = DoOk(run, PlayerCommand.Move(P(1, 2)));
            Assert.That(run.Status, Is.EqualTo(RunStatus.Lost));
            Assert.That(Has(result, GameEventKind.RunLost), Is.True);
            Assert.That(Do(run, PlayerCommand.Wait()).Accepted, Is.False);
        }
    }

    public class ChestTests
    {
        /// <summary>A Common chest, so these tests see its fixed reward count; <see cref="BetterChestsGrantMoreRewards"/> covers quality.</summary>
        static int CommonRewards => Catalog.ChestRewardsByQuality[(int)ChestQuality.Common];

        static RunState ChestRun(ulong seed = 1234UL)
        {
            var run = Run(1, seed,
                ".....",
                ".....",
                ".HC..",
                ".....",
                ".....");
            run.Floor[P(2, 2)].Quality = ChestQuality.Common;
            return run;
        }

        [Test]
        public void ChestsOpenedCountsChestsNotRewards()
        {
            // An Epic chest grants several rewards, but it is one chest (the end screen and telemetry count chests).
            var run = ChestRun();
            run.Floor[P(2, 2)].Quality = ChestQuality.Epic;
            OpenChest(run, P(2, 2));
            Assert.That(run.Rewards.Count, Is.GreaterThan(1));
            Assert.That(Chests.ChestsOpened(run.Rewards), Is.EqualTo(1));
            Assert.That(Chests.IsFirstDraw("chest:3:12"), Is.True);
            Assert.That(Chests.IsFirstDraw("chest:v3:12"), Is.True, "A vault chest's first draw.");
            Assert.That(Chests.IsFirstDraw("chest:3:12:2"), Is.False);
        }

        [Test]
        public void BetterChestsGrantMoreRewards()
        {
            // D-022: a chest that costs more taps pays more.
            foreach (var quality in new[] { ChestQuality.Common, ChestQuality.Rare, ChestQuality.Epic })
            {
                int expected = Catalog.ChestRewardsByQuality[(int)quality];
                var run = ChestRun();
                run.Floor[P(2, 2)].Quality = quality;
                OpenChest(run, P(2, 2));
                Assert.That(run.Rewards.Count, Is.EqualTo(expected), quality.ToString());
                if (quality != ChestQuality.Common)
                    Assert.That(expected, Is.GreaterThan(Catalog.ChestRewardsByQuality[(int)quality - 1]), "A better chest pays more.");
                var ids = new System.Collections.Generic.HashSet<string>();
                foreach (var reward in run.Rewards) ids.Add(reward.TransactionId);
                Assert.That(ids.Count, Is.EqualTo(expected), "Every reward is its own transaction, so none can be granted twice.");
            }
        }

        [Test]
        public void InteractCommitsDeterministicReward()
        {
            var run = ChestRun();
            var expected = Chests.RollReward(run.RunSeed, 1, P(2, 2), Catalog);
            int taps = Chests.TapsToOpen(run.Floor[P(2, 2)].Quality);
            var result = OpenChest(run, P(2, 2));

            var opened = result.Events.Find(e => e.Kind == GameEventKind.ChestOpened);
            Assert.That(opened, Is.Not.Null);
            Assert.That(opened.Reward.Kind, Is.EqualTo(expected.Kind));
            Assert.That(run.Rewards.Count, Is.EqualTo(CommonRewards));
            Assert.That(run.Floor[P(2, 2)].ChestOpened, Is.True);
            Assert.That(run.Turn, Is.EqualTo(taps), "Every tap is a full gameplay turn (D-022).");
        }

        [Test]
        public void ChestCannotBeOpenedTwice()
        {
            var run = ChestRun();
            OpenChest(run, P(2, 2));
            var hero = SaveSerializer.ToJson(run);

            Assert.That(Do(run, PlayerCommand.Interact(P(2, 2))).Accepted, Is.False);
            Assert.That(Chests.Open(run, P(2, 2), Catalog, new System.Collections.Generic.List<GameEvent>()), Is.Null);
            Assert.That(run.Rewards.Count, Is.EqualTo(CommonRewards));
            Assert.That(SaveSerializer.ToJson(run), Is.EqualTo(hero));
        }

        [Test]
        public void RewardSurvivesSaveAndCannotDuplicateAfterLoad()
        {
            var run = ChestRun();
            OpenChest(run, P(2, 2));
            var loaded = SaveSerializer.FromJson(SaveSerializer.ToJson(run));
            Assert.That(Do(loaded, PlayerCommand.Interact(P(2, 2))).Accepted, Is.False);
            Assert.That(loaded.Rewards.Count, Is.EqualTo(CommonRewards));
        }

        [Test]
        public void LootDoesNotDependOnWhenTheChestIsOpened()
        {
            var early = ChestRun();
            var late = ChestRun();
            DoOk(late, PlayerCommand.Wait());
            DoOk(late, PlayerCommand.Wait());
            DoOk(late, PlayerCommand.Wait());

            OpenChest(early, P(2, 2));
            OpenChest(late, P(2, 2));
            Assert.That(late.Rewards[0].Kind, Is.EqualTo(early.Rewards[0].Kind));
            Assert.That(late.Rewards[0].Amount, Is.EqualTo(early.Rewards[0].Amount));
        }

        [Test]
        public void EveryRewardKindAppearsAcrossSeeds()
        {
            var seen = new System.Collections.Generic.HashSet<RewardKind>();
            for (ulong seed = 1; seed < 200; seed++) seen.Add(Chests.RollReward(seed, 1, P(2, 2), Catalog).Kind);
            Assert.That(seen.Count, Is.EqualTo(3));
        }
    }
}
