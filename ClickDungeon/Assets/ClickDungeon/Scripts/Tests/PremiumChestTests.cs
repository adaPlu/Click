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
    /// D-026: a special key, bought with gems, hides a premium chest in the next run and is the only thing that opens it.
    /// </summary>
    public class PremiumChestTests
    {
        static RunState KeyedRun(ulong seed, int keys)
        {
            var run = RunFactory.NewRun(seed, Catalog, new List<GameEvent>(), ContentCatalog.DefaultHeroId, MovementMode.Free);
            var profile = new ProfileState { SpecialKeys = keys };
            ProfileSystem.Provision(profile, run, Catalog);
            return run;
        }

        static List<GridPos> PremiumChests(FloorState floor) =>
            Board.AllCells.Where(p => floor[p].Premium).ToList();

        [Test]
        public void TheSpecialKeyIsPricedInGems()
        {
            var poor = new ProfileState { Coins = 9999, Gems = Shop.SpecialKeyGems - 1 };
            Assert.That(Shop.TryBuy(poor, ShopItem.SpecialKey), Is.False, "Coins do not buy it.");
            Assert.That(poor.Coins, Is.EqualTo(9999));

            var rich = new ProfileState { Gems = Shop.SpecialKeyGems };
            Assert.That(Shop.TryBuy(rich, ShopItem.SpecialKey), Is.True);
            Assert.That(rich.Gems, Is.Zero);
            Assert.That(rich.SpecialKeys, Is.EqualTo(1));
        }

        [Test]
        public void KeysAreCarriedIntoTheRunOnePerFloorThatCanHoldAChest()
        {
            int floors = Catalog.Treasure.PremiumLastFloor - Catalog.Treasure.PremiumFirstFloor + 1;
            var profile = new ProfileState { SpecialKeys = floors + 2 };
            var run = RunFactory.NewRun(3UL, Catalog, new List<GameEvent>(), ContentCatalog.DefaultHeroId, MovementMode.Free);
            ProfileSystem.Provision(profile, run, Catalog);
            Assert.That(run.Hero.SpecialKeys, Is.EqualTo(floors));
            Assert.That(run.PremiumChestsToPlace, Is.EqualTo(floors));
            Assert.That(profile.SpecialKeys, Is.EqualTo(2), "Keys the run cannot use stay in the pocket.");
        }

        [Test]
        public void AKeyedRunFindsItsPremiumChestOnTheFirstPremiumFloor()
        {
            for (ulong seed = 1; seed <= 25; seed++)
            {
                var run = KeyedRun(seed, 1);
                Assert.That(PremiumChests(run.Floor), Is.Empty, $"Seed {seed}: none on floor 1.");
                RunFactory.BeginFloor(run, Catalog.Treasure.PremiumFirstFloor, Catalog, new List<GameEvent>());
                var premium = PremiumChests(run.Floor);
                Assert.That(premium.Count, Is.EqualTo(1), $"Seed {seed}.");
                var cell = run.Floor[premium[0]];
                Assert.That(cell.Content, Is.EqualTo(ContentKind.Chest));
                Assert.That(cell.Knowledge, Is.Not.EqualTo(Knowledge.Revealed), "It hides under a cover like everything else.");
                Assert.That(run.PremiumChestsToPlace, Is.Zero);

                RunFactory.BeginFloor(run, Catalog.Treasure.PremiumFirstFloor + 1, Catalog, new List<GameEvent>());
                Assert.That(PremiumChests(run.Floor), Is.Empty, $"Seed {seed}: one key, one chest.");
            }
        }

        [Test]
        public void APremiumChestChangesNothingElseOnItsFloor()
        {
            var plain = KeyedRun(8UL, 0);
            var keyed = KeyedRun(8UL, 1);
            RunFactory.BeginFloor(plain, Catalog.Treasure.PremiumFirstFloor, Catalog, new List<GameEvent>());
            RunFactory.BeginFloor(keyed, Catalog.Treasure.PremiumFirstFloor, Catalog, new List<GameEvent>());
            var chest = PremiumChests(keyed.Floor).Single();
            foreach (var p in Board.AllCells)
            {
                if (p == chest) continue;
                Assert.That(keyed.Floor[p].Content, Is.EqualTo(plain.Floor[p].Content), p.ToString());
                Assert.That(keyed.Floor[p].Hazard, Is.EqualTo(plain.Floor[p].Hazard), p.ToString());
                Assert.That(keyed.Floor[p].Terrain, Is.EqualTo(plain.Floor[p].Terrain), p.ToString());
            }
            Assert.That(keyed.Floor.Enemies.Count, Is.EqualTo(plain.Floor.Enemies.Count));
        }

        [Test]
        public void OnlyASpecialKeyOpensAPremiumChest()
        {
            var run = Run(
                ".....",
                ".....",
                ".HC..",
                ".....",
                ".....");
            var cell = P(2, 2);
            run.Floor[cell].Premium = true;
            run.Floor[cell].Quality = ChestQuality.Epic;
            run.Floor[cell].Knowledge = Knowledge.Revealed;

            var refused = Do(run, PlayerCommand.Interact(cell));
            Assert.That(refused.Accepted, Is.False, "No key, no opening.");
            Assert.That(refused.RejectReason, Does.Contain("special key"));

            run.Hero.SpecialKeys = 1;
            OpenChest(run, cell);
            Assert.That(run.Floor[cell].ChestOpened, Is.True);
            Assert.That(run.Rewards.Count, Is.EqualTo(Catalog.Treasure.PremiumChestRewards));
            Assert.That(run.Hero.SpecialKeys, Is.Zero, "The key stays in the lock.");
        }

        [Test]
        public void AnUnusedKeyGoesBackToTheProfile()
        {
            var profile = new ProfileState { SpecialKeys = 1 };
            var run = RunFactory.NewRun(4UL, Catalog, new List<GameEvent>(), ContentCatalog.DefaultHeroId, MovementMode.Free);
            ProfileSystem.Provision(profile, run, Catalog);
            Assert.That(profile.SpecialKeys, Is.Zero);
            run.Status = RunStatus.Lost;
            ProfileSystem.Bank(profile, run);
            Assert.That(profile.SpecialKeys, Is.EqualTo(1));
        }

        [Test]
        public void AVaultsGreatChestHoldsGems()
        {
            var run = Run(
                ".....",
                ".....",
                ".HW..",
                ".....",
                ".....");
            run.Floor.IsVault = true;
            OpenChest(run, P(2, 2));
            Assert.That(run.GemsFound, Is.EqualTo(Catalog.Treasure.GemsPerGreatChest));
        }

        [Test]
        public void TheBotStillFinishesRunsWithAKeyInItsPocket()
        {
            // A premium chest is extra loot, never a blocker: keyed runs must still be winnable.
            int won = 0;
            for (ulong seed = 1; seed <= 8; seed++)
            {
                var run = KeyedRun(seed, 3);
                var player = new AutoPlayer();
                for (int i = 0; i < BalanceTests.MaxCommands && run.Status == RunStatus.InProgress; i++)
                    TurnResolver.Apply(run, player.Choose(run, Catalog, seed * 7919UL + (ulong)i), Catalog);
                if (run.Status == RunStatus.Won) won++;
            }
            Assert.That(won, Is.GreaterThanOrEqualTo(7));
        }
    }
}
