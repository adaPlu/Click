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

        /// <summary>
        /// The whole curve, floor by floor, as literal numbers. Every other renown assertion in the suite was written as
        /// `something + Renown.Level(...)`, which restates the implementation and cannot fail when it changes; the audit
        /// found a committed mutation that survived 391 tests for exactly that reason. These digits are the contract: a
        /// character of one is a balance change, and changing one here means changing the measurement in D-067 too.
        /// Read as floors 1..20 for the hero's own threat, with the dungeon's own depth term off (its default).
        /// </summary>
        static readonly Dictionary<int, string> RampByThreat = new Dictionary<int, string>
        {
            [0] = "00000000000000000000",
            [1] = "00000000000000000001",
            [2] = "00000000001111111112",
            [3] = "00000001111112222223",
        };

        [Test]
        public void TheRampIsTheseExactNumbersFloorByFloor()
        {
            Assert.That(Catalog.RunFloorCount, Is.EqualTo(20), "The table below is written for a twenty-floor run.");
            Assert.That(Catalog.Renown.FirstFloor, Is.EqualTo(3));
            Assert.That(Catalog.Renown.MaxThreat, Is.EqualTo(3));
            Assert.That(Catalog.Renown.FloorsPerThreat, Is.Zero, "The dungeon's own depth term is off by default.");

            foreach (var row in RampByThreat)
            {
                var got = new System.Text.StringBuilder();
                for (int floorIndex = 1; floorIndex <= Catalog.RunFloorCount; floorIndex++)
                    got.Append(Renown.Level(AtFloor(floorIndex, row.Key), Catalog));
                Assert.That(got.ToString(), Is.EqualTo(row.Value), $"threat {row.Key}, floors 1..{Catalog.RunFloorCount}");
            }
        }

        [Test]
        public void ABlowIsRaisedByExactlyTheseNumbers()
        {
            // Pinned at an ODD level, which is where the arithmetic is decided: `Level / 2` and `(Level + 1) / 2` agree on
            // every even level, so an assertion at threat 2 - which is what this suite had - cannot see a rounding change.
            var def = Catalog.Enemy("goblin");
            Assert.That(Renown.Level(AtFloor(10, 3), Catalog), Is.EqualTo(1), "Test setup: floor 10 at full renown is one point.");
            Assert.That(Renown.Hit(AtFloor(10, 3), Catalog, def.Damage), Is.EqualTo(def.Damage),
                "One point of threat alone buys no extra damage.");
            Assert.That(Renown.Level(AtFloor(20, 3), Catalog), Is.EqualTo(3), "Test setup: the last floor is three points.");
            Assert.That(Renown.Hit(AtFloor(20, 3), Catalog, def.Damage), Is.EqualTo(def.Damage + 1),
                "Three points buys one, not two.");
            Assert.That(Renown.Trap(AtFloor(20, 3), Catalog, Catalog.Hazards.SpikeDamage),
                Is.EqualTo(Catalog.Hazards.SpikeDamage + 1), "And a trap answers the same number.");
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
            // rules 14.1: "none of it on floor 3, half of it halfway, all of it on the last floor". Rounding the ramp up
            // instead of down puts a point back on floor 3 - the old flat rule returning at the shallow end - and every
            // other assertion in this file survives it, because `Hit` divides the difference away again.
            Assert.That(Renown.Level(AtFloor(Catalog.Renown.FirstFloor, Catalog.Renown.MaxThreat), Catalog), Is.Zero,
                "The first floor renown reaches carries none of it.");
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

            // MaxDepthThreat is raised past its shipped 1 on purpose: at 1 the clamp swallows every step position, so
            // counting the depth from floor 0 instead of from FirstFloor gives identical answers at both ends.
            var tuned = ContentCatalog.CreateDefault();
            tuned.Renown.FloorsPerThreat = 8;
            tuned.Renown.MaxDepthThreat = 3;
            Assert.That(Renown.Level(AtFloor(10, 0), tuned), Is.Zero, "The first step falls at FirstFloor + 8, not before.");
            Assert.That(Renown.Level(AtFloor(11, 0), tuned), Is.EqualTo(1));
            Assert.That(Renown.Level(AtFloor(19, 0), tuned), Is.EqualTo(2));
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
        public void ASummonedMinionCarriesTheSameHeartsAsOneThatWasPlaced()
        {
            // D-046: a monster raised on one number and killed on another. ApplyThreat runs once at floor setup, so a
            // minion put on the board mid-fight had the raised blows and the bare hearts - on the same floor, a placed
            // bat had six hearts and a summoned one three (REL-70).
            var run = Run(Catalog.RunFloorCount, 21UL,
                ".....",
                ".....",
                ".H.Y.",
                ".....",
                ".....");
            run.Threat = Catalog.Renown.MaxThreat;
            RunFactory.ApplyThreat(run, Catalog);
            int level = Renown.Level(run, Catalog);
            Assert.That(level, Is.EqualTo(3), "Test setup: the bottom of the dungeon at full renown.");

            var summoner = run.Floor.Enemies[0];
            var def = Catalog.Enemy(summoner.DefId);
            Assert.That(Catalog.HasEnemy(def.SummonId), Is.True, "Test setup: a Spooky Spellbook summons.");
            int before = run.Floor.Enemies.Count;
            for (int turn = 0; turn < 12 && run.Floor.Enemies.Count == before; turn++)
                TurnResolver.Apply(run, PlayerCommand.Wait(), Catalog);
            Assert.That(run.Floor.Enemies.Count, Is.GreaterThan(before), "Test setup: something was summoned.");

            var minion = run.Floor.Enemies[run.Floor.Enemies.Count - 1];
            int bare = Catalog.Enemy(minion.DefId).MaxHp;
            Assert.That(minion.MaxHp, Is.EqualTo(bare + level * Catalog.Renown.HpPerThreat),
                "A summoned minion carries the floor's threat in its hearts, as a placed one does.");
            Assert.That(minion.Hp, Is.EqualTo(minion.MaxHp));
        }

        [Test]
        public void AVaultIsAsDeepAsTheFloorItHangsOff()
        {
            // Floor 14 is a step boundary (floor 13 is one point, floor 14 is two), so a vault that inherited the wrong
            // floor index fails here rather than by luck.
            var run = Run(14, 99UL,
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
        public void AFloorSetUpForARenownedHeroBakesItsHeartsExactlyOnce()
        {
            // Every renown test in this suite captured its baseline AFTER SetupFloor had run with Threat still 0, then
            // called ApplyThreat by hand - so they measured that one call and could not see the production one at all.
            // Two ApplyThreat calls in SetupFloor would have shipped green (TEST-66).
            var plain = RunFactory.NewRun(4242UL, Catalog, new List<GameEvent>(), ContentCatalog.DefaultHeroId, MovementMode.Free);
            RunFactory.BeginFloor(plain, Catalog.RunFloorCount, Catalog, new List<GameEvent>());

            var renowned = RunFactory.NewRun(4242UL, Catalog, new List<GameEvent>(), ContentCatalog.DefaultHeroId, MovementMode.Free);
            renowned.Threat = Catalog.Renown.MaxThreat;
            RunFactory.BeginFloor(renowned, Catalog.RunFloorCount, Catalog, new List<GameEvent>());

            int level = Renown.Level(renowned, Catalog);
            Assert.That(level, Is.EqualTo(3), "Test setup: the last floor at full renown.");
            Assert.That(renowned.Floor.Enemies, Is.Not.Empty, "Test setup: the floor has monsters.");
            Assert.That(renowned.Floor.Enemies.Count, Is.EqualTo(plain.Floor.Enemies.Count), "Same seed, same floor, same monsters.");
            for (int i = 0; i < renowned.Floor.Enemies.Count; i++)
            {
                var bare = plain.Floor.Enemies[i];
                var raised = renowned.Floor.Enemies[i];
                int perPoint = Catalog.Enemy(raised.DefId).IsBoss ? Catalog.Renown.BossHpPerThreat : Catalog.Renown.HpPerThreat;
                Assert.That(raised.MaxHp, Is.EqualTo(bare.MaxHp + level * perPoint), $"{raised.DefId} took its hearts once.");
                Assert.That(raised.Hp, Is.EqualTo(raised.MaxHp));
            }
        }

        [Test]
        public void ARunLongerThanItsOwnCountAndANegativeThreatBothStaySane()
        {
            // Two guards in Renown.Level that no test reached: a floor index past the run's length (a save from a longer
            // ruleset), and a threat below zero (a corrupt one). Neither may produce more threat than the cap.
            var past = AtFloor(14, Catalog.Renown.MaxThreat);
            past.FloorCount = 10;
            Assert.That(Renown.Level(past, Catalog), Is.LessThanOrEqualTo(Catalog.Renown.MaxThreat),
                "A floor past the run's length saturates rather than running away.");

            var negative = AtFloor(Catalog.RunFloorCount, -1);
            Assert.That(Renown.Level(negative, Catalog), Is.Zero, "A threat below zero is no threat.");
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
            Assert.That(deep, Is.EqualTo(def.Damage + 1), "At the bottom it hits one harder - exactly one.");
            Assert.That(trapShallow, Is.EqualTo(Catalog.Hazards.SpikeDamage));
            Assert.That(trapDeep, Is.EqualTo(Catalog.Hazards.SpikeDamage + 1), "So do the traps (D-041).");
        }
    }
}
