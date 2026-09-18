using System.Collections.Generic;
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
    /// <summary>
    /// D-025: a run carries treasure out, the profile keeps it between runs, and the shop turns it into provisions the next
    /// run starts with. A run never reads the profile, so its outcome still follows only from seed, tier, hero and provisions.
    /// </summary>
    public class TreasureAndShopTests
    {
        [Test]
        public void ChestsPayCoinsForEveryRewardTheyGiveUp()
        {
            var run = Run(
                ".....",
                ".....",
                ".HC..",
                ".....",
                ".....");
            var chest = P(2, 2);
            run.Floor[chest].Quality = ChestQuality.Common;
            var result = OpenChest(run, chest);

            int rewards = run.Rewards.Count;
            Assert.That(rewards, Is.GreaterThan(0));
            Assert.That(run.CoinsFound, Is.EqualTo(rewards * Catalog.Treasure.CoinsPerChestReward));
            Assert.That(result.Events.Count(e => e.Kind == GameEventKind.CoinsFound), Is.EqualTo(rewards),
                "Each reward's coins are their own event, so the log can say so.");
        }

        [Test]
        public void WalkingDownTheStairsPaysButFallingThroughAPitDoesNot()
        {
            var stairs = Run(
                ".....",
                ".....",
                ".HxK.",
                ".....",
                ".....");
            DoOk(stairs, PlayerCommand.Move(P(3, 2)));
            DoOk(stairs, PlayerCommand.Move(P(2, 2)));
            Assert.That(stairs.Floor.FloorIndex, Is.EqualTo(2), "Test setup: the hero took the stairs.");
            Assert.That(stairs.CoinsFound, Is.EqualTo(Catalog.Treasure.CoinsPerFloor));

            var pit = Run(
                ".....",
                ".....",
                ".Ho..",
                ".....",
                ".....");
            DoOk(pit, PlayerCommand.Move(P(2, 2)));
            Assert.That(pit.Floor.FloorIndex, Is.EqualTo(2), "Test setup: the hero fell a floor.");
            Assert.That(pit.CoinsFound, Is.Zero, "A fall skips the floor, so it pays nothing for it.");
        }

        [Test]
        public void OnlyLordBlobertCarriesAGem()
        {
            var run = Run(Catalog.RunFloorCount, 1234UL,
                ".....",
                ".....",
                ".HB..",
                ".....",
                "....X");
            var boss = Enemy(run, "lord_blobert");
            boss.Mode = EnemyMode.Normal;
            boss.Hp = 1;
            Assert.That(run.GemsFound, Is.Zero);
            DoOk(run, PlayerCommand.Slash(boss.Pos));
            Assert.That(run.GemsFound, Is.EqualTo(Catalog.Treasure.GemsForTheBoss));

            var goblin = Run(
                ".....",
                ".....",
                ".HG..",
                ".....",
                ".....");
            var mob = Enemy(goblin, "goblin");
            mob.Hp = 1;
            DoOk(goblin, PlayerCommand.Slash(mob.Pos));
            Assert.That(goblin.GemsFound, Is.Zero, "An ordinary monster is not a jeweller.");
        }

        [Test]
        public void TheProfileBanksWhatARunCarriedOut()
        {
            var profile = new ProfileState();
            var run = Run(".....", ".....", "..H..", ".....", ".....");
            run.CoinsFound = 37;
            run.GemsFound = 1;
            run.Status = RunStatus.Won;

            ProfileSystem.Bank(profile, run);
            Assert.That(profile.Coins, Is.EqualTo(37));
            Assert.That(profile.Gems, Is.EqualTo(1));
            Assert.That(profile.RunsFinished, Is.EqualTo(1));
            Assert.That(profile.RunsWon, Is.EqualTo(1));

            var lost = Run(".....", ".....", "..H..", ".....", ".....");
            lost.CoinsFound = 5;
            lost.Status = RunStatus.Lost;
            ProfileSystem.Bank(profile, lost);
            Assert.That(profile.Coins, Is.EqualTo(42), "A lost run still carries out what it found.");
            Assert.That(profile.RunsWon, Is.EqualTo(1));
        }

        [Test]
        public void TheShopOnlySellsWhatThePlayerCanAfford()
        {
            var profile = new ProfileState { Coins = Shop.PotionRationCoins };
            Assert.That(Shop.TryBuy(profile, ShopItem.HeartToken), Is.False, "Not enough for a heart token.");
            Assert.That(profile.Coins, Is.EqualTo(Shop.PotionRationCoins), "A refused purchase changes nothing.");
            Assert.That(profile.HeartTokens, Is.Zero);

            Assert.That(Shop.TryBuy(profile, ShopItem.PotionRation), Is.True);
            Assert.That(profile.Coins, Is.Zero);
            Assert.That(profile.PotionRations, Is.EqualTo(1));
            Assert.That(Shop.TryBuy(profile, ShopItem.PotionRation), Is.False, "And no credit.");
        }

        [Test]
        public void ProvisionsAreHandedToTheNextRunAndSpent()
        {
            var profile = new ProfileState { PotionRations = 1, HeartTokens = 2 };
            var run = RunFactory.NewRun(7UL, Catalog, new List<GameEvent>(), ContentCatalog.DefaultHeroId, MovementMode.Free);
            int hp = run.Hero.Hp, maxHp = run.Hero.MaxHp, potions = run.Hero.Potions;

            ProfileSystem.Provision(profile, run, Catalog);
            Assert.That(run.Hero.MaxHp, Is.EqualTo(maxHp + 2 * Catalog.Treasure.HeartTokenHearts));
            Assert.That(run.Hero.Hp, Is.EqualTo(hp + 2 * Catalog.Treasure.HeartTokenHearts), "The hearts are full ones.");
            Assert.That(run.Hero.Potions, Is.EqualTo(potions + Catalog.Treasure.PotionRationPotions));
            Assert.That(profile.HeartTokens, Is.Zero);
            Assert.That(profile.PotionRations, Is.Zero, "A provision is used up by the run it outfits.");
        }

        [Test]
        public void ARunNeverDependsOnTheProfile()
        {
            // Two runs from the same seed, one after a rich profile, must play out identically: only provisions may differ,
            // and they are spent into the hero before the first turn.
            var plain = AutoPlayer.PlayRun(Catalog, 21UL, 400);
            var rich = new ProfileState { Coins = 9999, Gems = 42, RunsFinished = 80 };
            ProfileSystem.Bank(rich, new RunState { Status = RunStatus.Won, Hero = new HeroState(), CoinsFound = 10 });
            var again = AutoPlayer.PlayRun(Catalog, 21UL, 400);
            Assert.That(again.Status, Is.EqualTo(plain.Status));
            Assert.That(again.Turns, Is.EqualTo(plain.Turns));
            Assert.That(again.Floor, Is.EqualTo(plain.Floor));
        }

        [Test]
        public void ASavedProfileComesBackAndABrokenOneIsEmptyRatherThanFatal()
        {
            string dir = Path.Combine(Path.GetTempPath(), "cd-profile-" + System.Guid.NewGuid().ToString("N"));
            try
            {
                var store = new FileProfileStore(dir);
                Assert.That(store.Load().Coins, Is.Zero, "No file yet: an empty profile.");

                store.Save(new ProfileState { Coins = 120, Gems = 2, PotionRations = 1 });
                var loaded = store.Load();
                Assert.That(loaded.Coins, Is.EqualTo(120));
                Assert.That(loaded.Gems, Is.EqualTo(2));
                Assert.That(loaded.PotionRations, Is.EqualTo(1));

                File.WriteAllText(store.MainPath, "{ this is not json");
                Assert.That(store.Load().Coins, Is.Zero, "A broken profile must not stop the game from starting.");

                File.WriteAllText(store.MainPath, "{ \"SchemaVersion\": 999, \"Coins\": 500 }");
                Assert.That(store.Load().Coins, Is.Zero, "Nor may a profile from another schema be read as this one.");
            }
            finally
            {
                if (Directory.Exists(dir)) Directory.Delete(dir, true);
            }
        }

        [Test]
        public void ARunThatEndsBanksItsTreasureOnce()
        {
            var session = new GameSession(Catalog, null);
            session.StartNewRun(5UL, Difficulty.Medium, MovementMode.Free);
            session.Run.CoinsFound = 25;
            session.Run.Hero.Hp = 1;

            // Kill the hero with the next command: spikes under foot are the simplest way in a scripted board.
            session.Run.Floor[session.Run.Hero.Pos].Hazard = HazardKind.None;
            var target = session.Run.Hero.Pos.Step(Direction.Up);
            session.Run.Floor[target].Hazard = HazardKind.Spikes;
            session.Run.Floor[target].Terrain = Terrain.Floor;
            session.Run.Floor.Enemies.Clear();
            var result = session.Submit(PlayerCommand.Move(target));

            Assert.That(result.Accepted, Is.True);
            Assert.That(session.Run.Status, Is.EqualTo(RunStatus.Lost), "Test setup: that killed the hero.");
            Assert.That(session.Profile.Coins, Is.EqualTo(25));
            Assert.That(session.Profile.RunsFinished, Is.EqualTo(1));
        }
    }
}
