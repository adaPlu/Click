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
        public void AVaultsGuardsStandClearOfTheTileTheHeroArrivesOn()
        {
            foreach (var floor in Vaults())
            {
                Assert.That(floor.Enemies.Count, Is.GreaterThanOrEqualTo(1), "A vault is guarded.");
                foreach (var enemy in floor.Enemies)
                {
                    Assert.That(enemy.Awake, Is.True, "Vault guards are awake.");
                    Assert.That(enemy.Pos.Manhattan(floor.Start), Is.GreaterThanOrEqualTo(2),
                        $"A guard at {enemy.Pos} is in the hero's face at {floor.Start}.");
                    Assert.That(enemy.Pos, Is.Not.EqualTo(Centre), "Nothing starts in the doorway.");
                }
                var chests = Board.AllCells.Where(p => floor[p].IsClosedChest).ToList();
                Assert.That(chests.Count, Is.GreaterThanOrEqualTo(1), "A vault holds treasure.");
                Assert.That(chests.Contains(floor.Start) || chests.Contains(Centre), Is.False,
                    "Chests stand in the room, not on the hero or the door.");
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
