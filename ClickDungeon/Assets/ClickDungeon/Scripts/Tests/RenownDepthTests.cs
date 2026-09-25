using System.Collections.Generic;
using ClickDungeon.Application;
using ClickDungeon.Content;
using ClickDungeon.Domain;
using ClickDungeon.Simulation;
using NUnit.Framework;
using static ClickDungeon.Tests.Scenario;

namespace ClickDungeon.Tests
{
    /// <summary>
    /// D-067: renown's threat arrives with the depth instead of landing whole on floor 3. Before this, a player who had
    /// earned any renown met the same raised monsters from the third floor to the twentieth - and the balance bot never
    /// set Threat at all, so nothing measured it.
    /// </summary>
    public class RenownDepthTests
    {
        static RunState AtFloor(int floorIndex, int threat)
        {
            var run = Run(floorIndex, 1234UL,
                ".....",
                ".....",
                "H..GX",
                ".....",
                ".....");
            run.Threat = threat;
            run.Floor.FloorIndex = floorIndex;
            return run;
        }

        [Test]
        public void ThreatArrivesWithTheDepthInsteadOfAllAtOnce()
        {
            int shallow = Renown.Level(AtFloor(3, Catalog.Renown.MaxThreat), Catalog);
            int middle = Renown.Level(AtFloor(11, Catalog.Renown.MaxThreat), Catalog);
            int deep = Renown.Level(AtFloor(Catalog.RunFloorCount, Catalog.Renown.MaxThreat), Catalog);

            Assert.That(shallow, Is.LessThan(middle), "Floor 3 must not meet what floor 11 does.");
            Assert.That(middle, Is.LessThan(deep), "Nor floor 11 what the last floor does.");
            Assert.That(deep, Is.GreaterThanOrEqualTo(Catalog.Renown.MaxThreat),
                "By the bottom the player meets everything their renown earned.");
            Assert.That(Renown.Level(AtFloor(Catalog.Renown.FirstFloor - 1, Catalog.Renown.MaxThreat), Catalog), Is.Zero,
                "Above the floor renown reaches, none of it applies.");
        }

        [Test]
        public void ItNeverFallsAsTheHeroDescends()
        {
            int last = 0;
            for (int floorIndex = 1; floorIndex <= Catalog.RunFloorCount; floorIndex++)
            {
                int level = Renown.Level(AtFloor(floorIndex, Catalog.Renown.MaxThreat), Catalog);
                Assert.That(level, Is.GreaterThanOrEqualTo(last), $"Floor {floorIndex} is softer than the floor above it.");
                last = level;
            }
        }

        [Test]
        public void TheDungeonCanAddSomeOfItsOwnButDoesNotByDefault()
        {
            // The knob exists and works; it ships off, because turning it on costs five points of win rate and stretches
            // the classes apart (D-067). A player who has earned no renown meets the floors' own profiles and nothing else.
            Assert.That(Catalog.Renown.FloorsPerThreat, Is.Zero, "Shipped off.");
            Assert.That(Renown.Level(AtFloor(Catalog.RunFloorCount, 0), Catalog), Is.Zero);

            var tuned = ContentCatalog.CreateDefault();
            tuned.Renown.FloorsPerThreat = 8;
            tuned.Renown.MaxDepthThreat = 1;
            Assert.That(Renown.Level(AtFloor(Catalog.RunFloorCount, 0), tuned), Is.EqualTo(1));
            Assert.That(Renown.Level(AtFloor(Catalog.Renown.FirstFloor, 0), tuned), Is.Zero);
        }

        [Test]
        public void ItRampsOverTheRunTheHeroIsActuallyOn()
        {
            // A ten-floor run is not the first half of a twenty-floor one: its last floor is its last floor.
            var shortRun = AtFloor(10, Catalog.Renown.MaxThreat);
            shortRun.FloorCount = 10;
            var longRun = AtFloor(10, Catalog.Renown.MaxThreat);

            Assert.That(Renown.Level(shortRun, Catalog), Is.GreaterThan(Renown.Level(longRun, Catalog)));
            Assert.That(Renown.Level(shortRun, Catalog), Is.GreaterThanOrEqualTo(Catalog.Renown.MaxThreat));
        }

        [Test]
        public void AMonstersHeartsFollowTheSameThreatItsBlowsDo()
        {
            // D-046: a monster raised on one number and killed on another is the bug this pairing exists to prevent.
            var deep = AtFloor(Catalog.RunFloorCount, Catalog.Renown.MaxThreat);
            var goblin = deep.Floor.Enemies[0];
            // What it stands there with, whatever else has already shaped it - this is about what threat adds on top.
            int bare = goblin.MaxHp;
            RunFactory.ApplyThreat(deep, Catalog);

            int level = Renown.Level(deep, Catalog);
            Assert.That(level, Is.GreaterThan(0), "Test setup: the bottom of the dungeon carries threat.");
            Assert.That(goblin.MaxHp, Is.EqualTo(bare + level * Catalog.Renown.HpPerThreat));
            Assert.That(goblin.Hp, Is.EqualTo(goblin.MaxHp));
        }

        [Test]
        public void AVaultIsAsDeepAsTheFloorItHangsOff()
        {
            var run = Run(12, 99UL,
                ".....",
                ".....",
                "HdK.X",
                ".....",
                ".....");
            run.Threat = Catalog.Renown.MaxThreat;
            run.Hero.HasKey = true;
            int outside = Renown.Level(run, Catalog);

            Assert.That(DoOk(run, PlayerCommand.Move(P(1, 2))).Accepted, Is.True);
            Assert.That(run.Floor.IsVault, Is.True, "Test setup: the hero is in the vault.");

            Assert.That(Renown.Level(run, Catalog), Is.EqualTo(outside),
                "A vault keeps its floor's index, so it keeps its floor's threat (D-046).");
        }

        [Test]
        public void ARenownedRunIsStillARunSomeoneCanWin()
        {
            // The guard that was missing. AutoPlayer.PlayRun never sets Threat, so every balance number this repo has is a
            // run with no renown at all - and with renown flat from floor 3, a player who had earned any won 5 of 100
            // instead of 64 and died around floor 7. Nothing was red, because nothing was looking (D-067).
            const int runs = 40;
            var catalog = ContentCatalog.CreateDefault(Difficulty.Medium);
            int won = 0, stalled = 0;
            for (ulong seed = 1; seed <= runs; seed++)
            {
                var player = new AutoPlayer(AutoPlayer.CasualMistakeRate, blind: true);
                var run = RunFactory.NewRun(seed, catalog, new List<GameEvent>(), ContentCatalog.DefaultHeroId, MovementMode.Free);
                run.Threat = catalog.Renown.MaxThreat;
                for (int i = 0; i < BalanceTests.MaxCommands && run.Status == RunStatus.InProgress; i++)
                    TurnResolver.Apply(run, player.Choose(run, catalog, seed * 7919UL + (ulong)i), catalog);
                if (run.Status == RunStatus.Won) won++;
                else if (run.Status == RunStatus.InProgress) stalled++;
            }
            Assert.That(stalled, Is.LessThanOrEqualTo(1 + runs / 30), $"{stalled}/{runs} renowned runs stalled: this measures nothing.");
            // Measured 30% over 120 seeds with the ramp, 5% without it. Six of forty sits well below the first and well
            // above the second, so this catches the cliff coming back without red-building on noise.
            Assert.That(won, Is.GreaterThanOrEqualTo(6),
                $"A hero at full renown won {won}/{runs} on Knight's Trial. Renown is a rubber band, not a wall.");
        }

        [Test]
        public void ABlowIsRaisedByTheThreatOfTheFloorItLandsOn()
        {
            var def = Catalog.Enemy("goblin");
            int shallow = Renown.Hit(AtFloor(3, Catalog.Renown.MaxThreat), Catalog, def.Damage);
            int deep = Renown.Hit(AtFloor(Catalog.RunFloorCount, Catalog.Renown.MaxThreat), Catalog, def.Damage);
            int trapShallow = Renown.Trap(AtFloor(3, Catalog.Renown.MaxThreat), Catalog, Catalog.Hazards.SpikeDamage);
            int trapDeep = Renown.Trap(AtFloor(Catalog.RunFloorCount, Catalog.Renown.MaxThreat), Catalog, Catalog.Hazards.SpikeDamage);

            Assert.That(shallow, Is.EqualTo(def.Damage), "On floor 3 a goblin hits for what it is worth.");
            Assert.That(deep, Is.GreaterThan(shallow), "At the bottom it hits harder.");
            Assert.That(trapDeep, Is.GreaterThan(trapShallow), "So do the traps (D-041).");
        }
    }
}
