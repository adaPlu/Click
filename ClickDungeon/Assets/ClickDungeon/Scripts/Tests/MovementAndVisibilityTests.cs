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
            var run = Open();
            var result = Do(run, PlayerCommand.Move(P(4, 4)));
            Assert.That(result.Accepted, Is.False);
            Assert.That(result.RejectReason, Is.Not.Empty);
            Assert.That(run.Hero.Pos, Is.EqualTo(P(2, 2)));
            Assert.That(run.Turn, Is.EqualTo(0));
        }

        [TestCase('#')]
        [TestCase('C')]
        public void WallsAndClosedChestsBlockMovement(char blocker)
        {
            var run = Run(
                ".....",
                "..." + blocker + ".",
                "...H.",
                ".....",
                ".....");
            Assert.That(Do(run, PlayerCommand.Move(P(3, 3))).Accepted, Is.False);
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
            DoOk(run, PlayerCommand.Interact(P(2, 2)));
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
            Assert.That(Commands.TryContextual(run, P(4, 4), out _), Is.False);
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
        public void RevealsDistanceOneAndSensesDistanceTwo()
        {
            var run = Run(
                ".....",
                ".....",
                "..H..",
                ".....",
                ".....");
            var floor = run.Floor;
            Assert.That(floor[P(2, 2)].Knowledge, Is.EqualTo(Knowledge.Revealed));
            Assert.That(floor[P(2, 3)].Knowledge, Is.EqualTo(Knowledge.Revealed));
            Assert.That(floor[P(3, 2)].Knowledge, Is.EqualTo(Knowledge.Revealed));
            Assert.That(floor[P(3, 3)].Knowledge, Is.EqualTo(Knowledge.Sensed));
            Assert.That(floor[P(2, 4)].Knowledge, Is.EqualTo(Knowledge.Sensed));
            Assert.That(floor[P(0, 2)].Knowledge, Is.EqualTo(Knowledge.Sensed));
            Assert.That(floor[P(4, 4)].Knowledge, Is.EqualTo(Knowledge.Unseen));
            Assert.That(floor[P(0, 0)].Knowledge, Is.EqualTo(Knowledge.Unseen));
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
            var run = Run(
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
            var run = Run(
                ".....",
                ".....",
                "..H..",
                ".....",
                ".....");
            DoOk(run, PlayerCommand.Move(P(1, 2)));
            DoOk(run, PlayerCommand.Move(P(0, 2)));
            Assert.That(run.Floor[P(3, 2)].Knowledge, Is.EqualTo(Knowledge.Revealed));
            Assert.That(run.Floor[P(4, 2)].Knowledge, Is.EqualTo(Knowledge.Sensed));
        }
    }
}
