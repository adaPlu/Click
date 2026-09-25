using System.Collections.Generic;
using System.Linq;
using ClickDungeon.Application;
using ClickDungeon.Domain;
using ClickDungeon.Simulation;
using NUnit.Framework;
using static ClickDungeon.Tests.Scenario;

namespace ClickDungeon.Tests
{
    /// <summary>
    /// D-064: the room behind a door is nine tiles, three by three, with the door the hero came through at its centre.
    /// It is the only room in the game with an edge - every dungeon floor is the whole board (D-021).
    /// </summary>
    public class VaultRoomTests
    {
        static readonly GridPos Centre = new GridPos(BoardRules.Size / 2, BoardRules.Size / 2);

        static IEnumerable<FloorState> Vaults()
        {
            for (ulong seed = 1; seed <= 6; seed++)
                foreach (int floorIndex in new[] { 1, 4, 9, 17 })
                    foreach (var door in new[] { P(0, 0), P(2, 1), P(4, 3) })
                        yield return FloorGenerator.GenerateVault(seed, floorIndex, door, Catalog);
        }

        [Test]
        public void AVaultIsNineTilesWithTheDoorItCameFromAtTheCentre()
        {
            foreach (var floor in Vaults())
            {
                var room = Board.AllCells.Where(p => floor[p].Terrain == Terrain.Floor).ToList();
                Assert.That(room.Count, Is.EqualTo(9), "A vault is three tiles by three.");
                Assert.That(room.TrueForAll(p => p.Chebyshev(Centre) <= 1), Is.True, "The nine tiles sit around the door.");
                Assert.That(floor.Exit, Is.EqualTo(Centre), "The door the hero came in by is the middle tile.");
                Assert.That(floor[Centre].IsExit && floor.ExitUnlocked, Is.True, "It is open: a vault is never locked.");
                Assert.That(floor[Centre].Knowledge, Is.EqualTo(Knowledge.Revealed),
                    "The hero walked through that door; it is not something to find again.");
                // Nobody ever stands in a doorway: the hero steps through it and stands directly inside it. Every tile in
                // a room this small is a step from the middle, so "beside" has to mean the four tiles that share its edge.
                Assert.That(floor.Start.Manhattan(Centre), Is.EqualTo(1),
                    $"The hero arrives on one of the four tiles beside the door, not on it or in a corner (was {floor.Start}).");
                Assert.That(Board.AllCells.Count(p => floor[p].Terrain == Terrain.Pit), Is.Zero,
                    "A vault hangs off a floor and has nowhere to fall to (D-021).");
            }
        }

        [Test]
        public void TheStoneAroundAVaultIsKnownFromTheStartAndIsNotATileToStepOn()
        {
            foreach (var floor in Vaults())
                foreach (var p in Board.AllCells)
                {
                    if (floor[p].Terrain == Terrain.Floor) continue;
                    Assert.That(floor[p].Terrain, Is.EqualTo(Terrain.Wall), $"{p} is the stone around the room.");
                    // A wall the player can click is a cover, and a cover hides something (rules 2.1). This one does not:
                    // it is the shape of the room, so it is drawn as known the moment they arrive.
                    Assert.That(floor[p].Knowledge, Is.EqualTo(Knowledge.Revealed), $"{p} is not a cover.");
                    Assert.That(floor[p].Content, Is.EqualTo(ContentKind.None), $"{p} holds nothing.");
                    Assert.That(floor.EnemyAt(p), Is.Null, $"Nothing stands in the stone at {p}.");
                }
        }

        [Test]
        public void TheStoneAroundAVaultIsNotSomethingTheBotCountsAsUncovered()
        {
            // MAINT-93: the bot's progress meter and its blind score both count uncovered tiles, and both skip
            // Terrain.Wall. That skip is load-bearing only here - a vault's sixteen stone tiles are written Revealed
            // the moment the room is generated, because they are its shape and not a find - and nothing asserted it.
            // Nor did anything assert the invariant it rests on: a dungeon floor never generates a wall at all (D-021),
            // so the clause is a no-op everywhere else. If either drifts, MAINT-50 comes back as an unexplained drift
            // in every balance number in the repo, which is the failure D-068 was written about.
            foreach (var floor in Vaults())
            {
                int found = Board.AllCells.Count(p => floor[p].Terrain == Terrain.Floor && floor[p].Knowledge == Knowledge.Revealed);
                Assert.That(AutoPlayer.RevealedCells(floor), Is.EqualTo(found),
                    "A freshly entered vault has been uncovered as far as its open door, and no further.");
                Assert.That(AutoPlayer.RevealedCells(floor), Is.LessThanOrEqualTo(9),
                    "Nine tiles is the whole room; sixteen would be the stone counted as a find.");
            }

            for (ulong seed = 1; seed <= 60; seed++)
                for (int floorIndex = 1; floorIndex <= Catalog.RunFloorCount; floorIndex++)
                {
                    var floor = FloorGenerator.Generate(seed, floorIndex, Catalog);
                    Assert.That(Board.AllCells.Any(p => floor[p].Terrain == Terrain.Wall), Is.False,
                        $"seed {seed} floor {floorIndex}: a dungeon floor grew a wall, and a wall is a cover (D-021).");
                }
        }

        [Test]
        public void EveryGuardInAVaultCanWalkToTheHero()
        {
            // REL-91: monsters never cross the doorway, so a vault's other eight tiles are a single ring, and two chests
            // on it cut the room in two. 17% of rooms walled a guard off behind the treasure for the whole fight - it
            // could not move, could not be drawn out, and the room was quietly easier than the one the player was shown.
            // Guards do not block each other here: they shuffle, and a guard waiting behind its neighbour gets its turn
            // when the neighbour falls. Chests and traps never move.
            foreach (var floor in Vaults())
                foreach (var guard in floor.Enemies)
                {
                    var seen = new HashSet<GridPos> { guard.Pos };
                    var queue = new Queue<GridPos>(seen);
                    bool reaches = false;
                    while (queue.Count > 0 && !reaches)
                    {
                        var p = queue.Dequeue();
                        reaches = p.IsAdjacent(floor.Start);
                        foreach (var n in Board.Neighbours(p))
                            if (!seen.Contains(n) && Board.EnemyPathable(floor, n)) { seen.Add(n); queue.Enqueue(n); }
                    }
                    Assert.That(reaches, Is.True,
                        $"The guard at {guard.Pos} cannot walk to the hero at {floor.Start}: {Describe(floor)}");
                }
        }

        static string Describe(FloorState floor) => string.Join(" ", Board.AllCells
            .Where(p => floor[p].Terrain == Terrain.Floor)
            .Select(p => $"{p}:{(floor.EnemyAt(p) != null ? "guard" : floor[p].Content.ToString())}"));

        [Test]
        public void AVaultsGuardsStandClearOfTheTileTheHeroArrivesOn()
        {
            foreach (var floor in Vaults())
            {
                Assert.That(floor.Enemies.Count, Is.GreaterThanOrEqualTo(1), "A vault is guarded.");
                foreach (var enemy in floor.Enemies)
                {
                    Assert.That(enemy.Awake, Is.True, "Vault guards are awake.");
                    // Not Manhattan: melee and movement are diagonal-inclusive, so Manhattan 2 includes the diagonal,
                    // which is adjacent. This test asserted the weak bound and so agreed with the bug for 79% of rooms.
                    // Chebyshev, not the IsAdjacent the generator filters with: a test that calls the same helper pins
                    // the generator's use of the rule rather than the rule (TEST-99).
                    Assert.That(enemy.Pos.Chebyshev(floor.Start), Is.GreaterThanOrEqualTo(2),
                        $"A guard at {enemy.Pos} is within reach of the hero arriving at {floor.Start}.");
                    Assert.That(enemy.Pos, Is.Not.EqualTo(Centre), "Nothing starts in the doorway.");
                }
                var chests = Board.AllCells.Where(p => floor[p].IsClosedChest).ToList();
                Assert.That(chests.Count, Is.GreaterThanOrEqualTo(1), "A vault holds treasure.");
                Assert.That(chests.Contains(floor.Start) || chests.Contains(Centre), Is.False,
                    "Chests stand in the room, not on the hero or the door.");
            }
        }

        [Test]
        public void NothingStandsInTheDoorwayWhileTheHeroIsInTheRoom()
        {
            // "Nobody ever stands in a doorway" was written into the generator and never enforced after it. The door is
            // the only way out of a nine-tile room, and the guard chasing the hero is the one most likely to be on it.
            var run = Run(6, 3UL,
                ".....",
                ".....",
                "HdK.X",
                ".....",
                ".....");
            run.Hero.HasKey = true;
            Assert.That(Scenario.DoOk(run, PlayerCommand.Move(P(1, 2))).Accepted, Is.True);
            Assert.That(run.Floor.IsVault, Is.True, "Test setup: the hero is in the vault.");
            Assert.That(run.Floor.Enemies, Is.Not.Empty, "Test setup: the room is guarded.");

            var exit = run.Floor.Exit;
            Assert.That(Board.EnemyPathable(run.Floor, exit), Is.False, "No monster paths onto the way out.");
            for (int turn = 0; turn < 12 && run.Status == RunStatus.InProgress && run.Floor.IsVault; turn++)
            {
                TurnResolver.Apply(run, PlayerCommand.Wait(), Catalog);
                Assert.That(run.Floor.EnemyAt(exit), Is.Null, $"turn {turn}: a guard is standing in the doorway.");
            }
        }

        [Test]
        public void TheHeroCannotWalkIntoTheStoneAroundAVault()
        {
            var run = Run(
                ".....",
                ".....",
                "HdK.X",
                ".....",
                ".....");
            run.Hero.HasKey = true;
            Assert.That(Scenario.DoOk(run, PlayerCommand.Move(P(1, 2))).Accepted, Is.True);
            Assert.That(run.Floor.IsVault, Is.True, "Test setup: the hero is in the vault.");

            var stone = Board.AllCells.First(p => run.Floor[p].Terrain == Terrain.Wall);
            int turn = run.Turn;
            var refused = TurnResolver.Apply(run, PlayerCommand.Move(stone), Catalog);

            Assert.That(refused.Accepted, Is.False, "Solid stone is not a tile to step on.");
            Assert.That(run.Turn, Is.EqualTo(turn), "A refused command costs no turn.");
            Assert.That(run.Hero.Pos, Is.Not.EqualTo(stone));
        }

        [Test]
        public void ABlindBotInAVaultCannotReadTheFloorWaitingOutside()
        {
            // DATA-30: Redact blanked the room and left the floor outside it readable, so the one command this is all
            // about - stepping back through the door - was scored against a key and an exit the player has not found.
            var run = Run(6, 8UL,
                ".....",
                ".....",
                "HdK.X",
                ".....",
                ".....");
            run.Hero.HasKey = true;
            Assert.That(Scenario.DoOk(run, PlayerCommand.Move(P(1, 2))).Accepted, Is.True);
            Assert.That(run.Floor.IsVault, Is.True, "Test setup: the hero is in the vault.");
            var outer = run.OuterFloor;
            var hidden = Board.AllCells.Where(p => outer[p].Knowledge != Knowledge.Revealed).ToList();
            Assert.That(hidden, Is.Not.Empty, "Test setup: the floor outside still has covers on it.");

            var view = AutoPlayer.Redact(run);

            foreach (var p in hidden)
            {
                Assert.That(view.OuterFloor[p].Content, Is.EqualTo(ContentKind.None), $"{p} still reports what it holds.");
                Assert.That(view.OuterFloor[p].IsExit, Is.False, $"{p} still reports the way down.");
                Assert.That(view.OuterFloor[p].Hazard, Is.EqualTo(HazardKind.None), $"{p} still reports its trap.");
            }
            // The other direction, or blanking the whole board passes: a redaction that hides what the player HAS seen
            // bends the instrument just as badly, and every assertion above would still be green (TEST-101).
            var known = Board.AllCells.Where(p => outer[p].Knowledge == Knowledge.Revealed).ToList();
            Assert.That(known, Is.Not.Empty, "Test setup: the hero uncovered something before stepping through.");
            foreach (var p in known)
                Assert.That(view.OuterFloor[p].Terrain, Is.EqualTo(outer[p].Terrain), $"{p} is still what the player saw.");
            Assert.That(view.OuterFloor.Enemies.Count, Is.EqualTo(outer.Enemies.Count(e => e.Awake
                || outer[e.Pos].Knowledge == Knowledge.Revealed)), "Awake monsters outside are still counted.");
        }

        [Test]
        public void TheBotWalksBackOutOfAVaultInsteadOfCirclingIt()
        {
            // The scoring counted only the board the hero stands on, so the monsters left outside vanished while they were
            // in the vault and stepping back out - which puts them back on the board - always scored worse than staying.
            // In a room of nine tiles there is nowhere else to go, so the bot circled it until the command cap (D-064).
            // The floor outside is deliberately heavy: the flat penalty for standing in a vault was all that ever pushed
            // the bot back out, and a floor worth more than that penalty is exactly what used to hold it in the room.
            var run = Run(17, 1234UL,
                ".RRR.",
                "RRRRR",
                "HdK.X",
                "RRRRR",
                ".RRR.");
            run.Hero.HasKey = true;
            Assert.That(Scenario.DoOk(run, PlayerCommand.Move(P(1, 2))).Accepted, Is.True);
            Assert.That(run.Floor.IsVault, Is.True, "Test setup: the hero is in the vault.");
            // Nothing left to loot and nothing left to fight: the only thing to do is leave.
            run.Floor.Enemies.Clear();
            foreach (var p in Board.AllCells)
            {
                run.Floor[p].Content = ContentKind.None;
                run.Floor[p].GreatChest = false;
            }

            var player = new AutoPlayer();
            for (int i = 0; i < 8 && run.Floor.IsVault; i++)
                TurnResolver.Apply(run, player.Choose(run, Catalog, 41UL + (ulong)i), Catalog);

            Assert.That(run.Floor.IsVault, Is.False, "The bot took the door back out.");
            Assert.That(run.OuterFloor, Is.Null);
        }
        [Test]
        public void AHeroComingBackIntoAVaultNeverLandsOnItsGuard()
        {
            // A vault is kept as it was left, and its guards have been standing there since. On a second visit the tile the
            // room was built around can be taken - in a room of nine tiles it often is - and the hero used to be put on top
            // of the monster standing on it (D-064).
            var run = Run(
                ".....",
                ".....",
                "HdK.X",
                ".....",
                ".....");
            run.Hero.HasKey = true;
            Assert.That(Scenario.DoOk(run, PlayerCommand.Move(P(1, 2))).Accepted, Is.True);
            Assert.That(run.Floor.IsVault, Is.True, "Test setup: the hero is in the vault.");

            var start = run.Floor.Start;
            var guard = run.Floor.Enemies[0];
            // Walk out past the guard, leaving it standing on the tile the hero arrived on.
            run.Hero.Pos = Board.AllCells.First(p => run.Floor[p].Terrain == Terrain.Floor && p != start
                && p != run.Floor.Exit && run.Floor.EnemyAt(p) == null);
            guard.Pos = start;
            Assert.That(Scenario.DoOk(run, PlayerCommand.Move(run.Floor.Exit)).Accepted, Is.True);
            Assert.That(run.Floor.IsVault, Is.False, "Test setup: the hero walked back out.");

            Assert.That(Scenario.DoOk(run, PlayerCommand.Move(P(1, 2))).Accepted, Is.True);

            Assert.That(run.Floor.IsVault, Is.True, "The door leads back into the same room.");
            Assert.That(run.Floor.EnemyAt(run.Hero.Pos), Is.Null, "Two actors cannot share a tile.");
            Assert.That(run.Floor[run.Hero.Pos].Terrain, Is.EqualTo(Terrain.Floor), "And the hero stands in the room.");
        }
    }
}
