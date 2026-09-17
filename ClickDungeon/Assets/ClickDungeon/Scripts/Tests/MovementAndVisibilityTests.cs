using ClickDungeon.Domain;
using ClickDungeon.Simulation;
using NUnit.Framework;
using static ClickDungeon.Tests.Scenario;

namespace ClickDungeon.Tests
{
    public class MovementTests
    {
        static RunState Open() => Run(
            ".....",
            ".....",
            "..H..",
            ".....",
            ".....");

        [Test]
        public void LegalMoveAdvancesTurn()
        {
            var run = Open();
            DoOk(run, PlayerCommand.Move(P(2, 3)));
            Assert.That(run.Hero.Pos, Is.EqualTo(P(2, 3)));
            Assert.That(run.Turn, Is.EqualTo(1));
        }

        [Test]
        public void IllegalMoveCostsNothing()
        {
            var run = Run(
                ".....",
                "..G..",
                "..H..",
                ".....",
                ".....");
            var result = Do(run, PlayerCommand.Move(P(2, 3)));
            Assert.That(result.Accepted, Is.False, "Someone is standing there.");
            Assert.That(result.RejectReason, Is.Not.Empty);
            Assert.That(run.Hero.Pos, Is.EqualTo(P(2, 2)));
            Assert.That(run.Turn, Is.EqualTo(0));
        }

        [Test]
        public void OnlyShutDoorsAndOccupiedTilesBlockTheHero()
        {
            // Nothing else blocks (D-021): walls are not generated at all, and a chest is furniture you can stand on.
            var door = Run(
                ".....",
                "...D.",
                "...H.",
                ".....",
                ".....");
            // While covered, clicking the door is a bump that uncovers it; once seen, it refuses the click (D-023).
            Assert.That(Has(DoOk(door, PlayerCommand.Move(P(3, 3))), GameEventKind.HeroBumped), Is.True);
            Assert.That(door.Hero.Pos, Is.EqualTo(P(3, 2)));
            Assert.That(Do(door, PlayerCommand.Move(P(3, 3))).Accepted, Is.False, "A shut door is not a tile.");

            var chest = Run(
                ".....",
                "...C.",
                "...H.",
                ".....",
                ".....");
            Assert.That(Do(chest, PlayerCommand.Move(P(3, 3))).Accepted, Is.True);
        }

        [Test]
        public void StepByStepReachesOnlyTheEightNeighbours()
        {
            var run = StepRun(
                ".....",
                ".....",
                "..H..",
                ".....",
                ".....");
            Assert.That(Do(run, PlayerCommand.Move(P(4, 4))).Accepted, Is.False, "Two tiles away is out of reach.");
            Assert.That(Do(run, PlayerCommand.Move(P(3, 3))).Accepted, Is.True, "Diagonals count as neighbours.");
        }

        [Test]
        public void PitsBlockOnlyWhereThereIsNothingBelow()
        {
            // Stepping into a pit is a fall to the next floor (rules §4), so it is blocked only on the last floor.
            var lastFloor = Run(Scenario.Catalog.RunFloorCount, 1234UL,
                ".....",
                "...o.",
                "...H.",
                ".....",
                ".....");
            // An uncovered pit with nothing below refuses the click; a covered one would be a bump (D-023).
            lastFloor.Floor[P(3, 3)].Knowledge = Knowledge.Revealed;
            Assert.That(Do(lastFloor, PlayerCommand.Move(P(3, 3))).Accepted, Is.False);

            var earlyFloor = Run(1, 1234UL,
                ".....",
                "...o.",
                "...H.",
                ".....",
                ".....");
            Assert.That(Do(earlyFloor, PlayerCommand.Move(P(3, 3))).Accepted, Is.True);
        }

        [Test]
        public void CannotLeaveTheBoard()
        {
            var run = Run(
                ".....",
                ".....",
                ".....",
                ".....",
                "H....");
            Assert.That(Do(run, PlayerCommand.Move(P(-1, 0))).Accepted, Is.False);
            Assert.That(Do(run, PlayerCommand.Move(P(0, -1))).Accepted, Is.False);
        }

        [Test]
        public void OpenedChestIsPassable()
        {
            var run = Run(
                ".....",
                ".....",
                ".HC..",
                ".....",
                ".....");
            OpenChest(run, P(2, 2));
            DoOk(run, PlayerCommand.Move(P(2, 2)));
        }

        [Test]
        public void ContextualTapPicksTheObviousCommand()
        {
            var run = Run(
                ".....",
                "..G..",
                ".CH..",
                ".....",
                ".....");
            Assert.That(Commands.TryContextual(run, P(2, 2), out var self), Is.True);
            Assert.That(self.Kind, Is.EqualTo(CommandKind.Wait));
            Commands.TryContextual(run, P(2, 3), out var enemy);
            Assert.That(enemy.Kind, Is.EqualTo(CommandKind.Slash));
            Commands.TryContextual(run, P(1, 2), out var chest);
            Assert.That(chest.Kind, Is.EqualTo(CommandKind.Interact));
            Commands.TryContextual(run, P(3, 2), out var step);
            Assert.That(step.Kind, Is.EqualTo(CommandKind.Move));

            Assert.That(Commands.TryContextual(run, P(4, 4), out var far), Is.True, "Free Roam reaches the whole board.");
            Assert.That(far.Kind, Is.EqualTo(CommandKind.Move));
            Assert.That(Commands.TryContextual(Step(run), P(4, 4), out _), Is.False, "Step by Step does not.");
        }

        [Test]
        public void ContextualTapOffTheBoardEdgeIsIgnored()
        {
            var run = Run(
                ".....",
                ".....",
                ".....",
                ".....",
                "H....");
            Assert.That(Commands.TryContextual(run, P(-1, 0), out _), Is.False);
            Assert.That(Commands.TryContextual(run, P(0, -1), out _), Is.False);
        }
    }

    public class VisibilityTests
    {
        [Test]
        public void RevealsOnlyTheHerosTileAndSensesNearby()
        {
            // D-023: only a clicked tile is revealed. Step by Step still senses clues within two steps (D-021).
            var run = StepRun(
                ".....",
                ".....",
                "..H..",
                ".....",
                ".....");
            var floor = run.Floor;
            Assert.That(floor[P(2, 2)].Knowledge, Is.EqualTo(Knowledge.Revealed));
            Assert.That(floor[P(2, 3)].Knowledge, Is.EqualTo(Knowledge.Sensed), "A neighbour is sensed, not revealed.");
            Assert.That(floor[P(3, 2)].Knowledge, Is.EqualTo(Knowledge.Sensed));
            Assert.That(floor[P(3, 3)].Knowledge, Is.EqualTo(Knowledge.Sensed));
            Assert.That(floor[P(2, 4)].Knowledge, Is.EqualTo(Knowledge.Sensed));
            Assert.That(floor[P(0, 2)].Knowledge, Is.EqualTo(Knowledge.Sensed));
            Assert.That(floor[P(4, 4)].Knowledge, Is.EqualTo(Knowledge.Unseen));
            Assert.That(floor[P(0, 0)].Knowledge, Is.EqualTo(Knowledge.Unseen));
        }

        [Test]
        public void FreeRoamGivesNoHintsAtAll()
        {
            // D-021: every tile the hero has not revealed stays a blank cover, so the board cannot be read from a distance.
            var run = Run(
                "..g..",
                ".....",
                "^.H.K",
                ".....",
                "..C..");
            foreach (var p in Board.AllCells)
                Assert.That(run.Floor[p].Knowledge, Is.Not.EqualTo(Knowledge.Sensed), $"{p} should be a blank cover.");
            Assert.That(run.Floor[P(2, 2)].Knowledge, Is.EqualTo(Knowledge.Revealed), "The hero still sees its own tile.");
            Assert.That(run.Floor[P(0, 2)].Knowledge, Is.EqualTo(Knowledge.Unseen), "Two tiles away is unknown, not hinted.");
        }

        [Test]
        public void ExitIsAlwaysKnown()
        {
            var run = Run(
                "....X",
                ".....",
                ".....",
                ".....",
                "H....");
            Assert.That(run.Floor[P(4, 4)].Knowledge, Is.EqualTo(Knowledge.Revealed));
        }

        [Test]
        public void SensedCellsShowTruthfulClues()
        {
            var run = StepRun(
                "..g..",
                ".....",
                "^.H.K",
                ".....",
                "..C..");
            var floor = run.Floor;
            Assert.That(Board.ClueAt(floor, P(2, 4)), Is.EqualTo(Clue.Enemy));
            Assert.That(Board.ClueAt(floor, P(0, 2)), Is.EqualTo(Clue.Danger));
            Assert.That(Board.ClueAt(floor, P(4, 2)), Is.EqualTo(Clue.Objective));
            Assert.That(Board.ClueAt(floor, P(2, 0)), Is.EqualTo(Clue.Treasure));
            Assert.That(Board.ClueAt(floor, P(3, 3)), Is.EqualTo(Clue.Safe));
            Assert.That(Enemy(run, "goblin").Awake, Is.False);
        }

        [Test]
        public void KnowledgeNeverGoesBackwards()
        {
            var run = StepRun(
                ".....",
                ".....",
                "..H..",
                ".....",
                ".....");
            DoOk(run, PlayerCommand.Move(P(1, 2)));
            DoOk(run, PlayerCommand.Move(P(0, 2)));
            // Tiles once stood on stay revealed, and a sensed tile left far behind stays sensed.
            Assert.That(run.Floor[P(2, 2)].Knowledge, Is.EqualTo(Knowledge.Revealed));
            Assert.That(run.Floor[P(1, 2)].Knowledge, Is.EqualTo(Knowledge.Revealed));
            Assert.That(run.Floor[P(4, 2)].Knowledge, Is.EqualTo(Knowledge.Sensed));
        }
    }
}
