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
    /// D-023 amendment: in Free Roam every covered tile is the same until it is clicked. Nothing under a cover, the exit
    /// stairs included, may change how the game answers a click, what the player is told, or what the bot can see.
    /// Board drawing is covered by the Unity test <c>HiddenTileArtTests</c>.
    /// </summary>
    public class HiddenTileTests
    {
        /// <summary>Everything that can hide under a cover, by scenario letter (awake monsters are drawn, so they are not hidden).</summary>
        const string HiddenThings = "XxKCWP^bLDdptfgsio";

        static readonly GridPos Hero = P(2, 2);

        /// <summary>Adjacent, diagonal, a dash away, and far across the board.</summary>
        static readonly GridPos[] Targets = { P(2, 3), P(3, 3), P(2, 4), P(4, 2), P(0, 0) };

        static readonly CommandKind[] TargetedKinds = { CommandKind.Move, CommandKind.Slash, CommandKind.Dash, CommandKind.Interact };

        static RunState BoardWith(int floorIndex, GridPos target, char thing)
        {
            var rows = new[] { ".....", ".....", "..H..", ".....", "....." };
            if (thing != '.')
            {
                int row = BoardRules.Size - 1 - target.Y;
                var chars = rows[row].ToCharArray();
                chars[target.X] = thing;
                rows[row] = new string(chars);
            }
            return Run(floorIndex, 1234UL, rows);
        }

        static IEnumerable<int> Floors => new[] { 1, Catalog.RunFloorCount };

        [Test]
        public void ACoverAnswersEveryCommandTheSameWhateverIsUnderIt()
        {
            foreach (int floorIndex in Floors)
            foreach (var target in Targets)
            {
                var empty = BoardWith(floorIndex, target, '.');
                Assert.That(empty.Floor[target].Knowledge, Is.Not.EqualTo(Knowledge.Revealed));
                foreach (char thing in HiddenThings)
                {
                    var hidden = BoardWith(floorIndex, target, thing);
                    Assert.That(hidden.Floor[target].Knowledge, Is.Not.EqualTo(Knowledge.Revealed), $"'{thing}' starts covered.");
                    string where = $"'{thing}' at {target} on floor {floorIndex}";
                    foreach (var kind in TargetedKinds)
                    {
                        var command = new PlayerCommand(kind, target);
                        Assert.That(Commands.Validate(hidden, command, Catalog, out _),
                            Is.EqualTo(Commands.Validate(empty, command, Catalog, out _)), $"{kind} on {where}");
                    }

                    bool hiddenTap = Commands.TryContextual(hidden, target, out var hiddenCommand);
                    bool emptyTap = Commands.TryContextual(empty, target, out var emptyCommand);
                    Assert.That(hiddenTap, Is.EqualTo(emptyTap), $"Tap on {where}");
                    Assert.That(hiddenCommand.Kind, Is.EqualTo(emptyCommand.Kind), $"Tap on {where}");
                }
            }
        }

        [Test]
        public void TheExitIsLegalToClickLikeAnyOtherCover()
        {
            var run = Run(
                "X....",
                ".....",
                "..H..",
                ".....",
                "....K");
            var legal = Commands.LegalTargets(run, CommandKind.Move, Catalog);
            var covered = Board.AllCells.Where(p => run.Floor[p].Knowledge != Knowledge.Revealed).ToList();
            Assert.That(legal, Is.EquivalentTo(covered), "Every cover, exit and key included, is one click away in Free Roam.");
        }

        [Test]
        public void TheBotSeesEveryCoverAlike()
        {
            foreach (var target in Targets)
            {
                var empty = AutoPlayer.Redact(BoardWith(1, target, '.'));
                foreach (char thing in HiddenThings)
                {
                    var hidden = AutoPlayer.Redact(BoardWith(1, target, thing));
                    string where = $"'{thing}' at {target}";
                    Assert.That(Describe(hidden.Floor[target]), Is.EqualTo(Describe(empty.Floor[target])), where);
                    Assert.That(hidden.Floor.Exit, Is.EqualTo(empty.Floor.Exit), where);
                    Assert.That(hidden.Floor.Enemies.Count, Is.EqualTo(empty.Floor.Enemies.Count), where);
                }
            }
        }

        static string Describe(CellState c) =>
            $"{c.Terrain} {c.Hazard} {c.BombFuse} {c.Content} {c.IsExit} {c.ChestOpened} {c.GreatChest} {c.Quality} {c.ChestTaps} {c.Used} {c.Knowledge}";

        [Test]
        public void ClickingACoveredExitIsWhatRevealsIt()
        {
            var run = Run(
                ".....",
                ".....",
                "..H.X",
                ".....",
                ".....");
            var exit = P(4, 2);
            DoOk(run, PlayerCommand.Move(P(3, 2)));
            Assert.That(run.Floor[exit].Knowledge, Is.EqualTo(Knowledge.Unseen), "Walking next to the exit shows nothing.");
            DoOk(run, PlayerCommand.Move(exit));
            Assert.That(run.Floor[exit].Knowledge, Is.EqualTo(Knowledge.Revealed));
        }

        [Test]
        public void OpeningTheBossExitDoesNotPointAtACoveredExit()
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
            var discovery = Discovery.Before(run);
            var result = DoOk(run, PlayerCommand.Slash(boss.Pos));

            var unlocked = result.Events.Single(e => e.Kind == GameEventKind.ExitUnlocked);
            Assert.That(run.Floor[P(4, 0)].Knowledge, Is.Not.EqualTo(Knowledge.Revealed));
            Assert.That(discovery.CanMention(run, unlocked), Is.True, "The log may say the exit is open...");
            Assert.That(discovery.CanMark(run, unlocked), Is.False, "...but no popup or effect may show where it is.");
            Assert.That(discovery.Markable(run, result.Events).Any(e => e.To == P(4, 0)), Is.False);

            run.Floor[P(4, 0)].Knowledge = Knowledge.Revealed;
            Assert.That(discovery.CanMark(run, unlocked), Is.True, "Once uncovered, the exit can light up.");
        }

        [Test]
        public void ABlastNeverTellsOfAMonsterStillUnderItsCover()
        {
            var run = Run(
                ".....",
                "..g..",
                ".Hb..",
                "..G..",
                ".....");
            var bomb = P(2, 2);
            run.Floor[bomb].Knowledge = Knowledge.Revealed;
            run.Floor[bomb].BombFuse = 0;
            var sleeper = run.Floor.EnemyAt(P(2, 3));
            var awake = run.Floor.EnemyAt(P(2, 1));
            Assert.That(sleeper.Awake, Is.False);

            var discovery = Discovery.Before(run);
            var result = DoOk(run, PlayerCommand.Wait());
            Assert.That(Has(result, GameEventKind.BombExploded), Is.True, "Test setup: the bomb goes off this turn.");

            var aboutSleeper = result.Events.Where(e => e.ActorId == sleeper.Id).ToList();
            Assert.That(aboutSleeper, Is.Not.Empty, "Test setup: the blast reaches the sleeping goblin.");
            foreach (var e in aboutSleeper)
            {
                Assert.That(discovery.CanMention(run, e), Is.False, $"{e.Kind} about a monster nobody has uncovered.");
                Assert.That(discovery.CanMark(run, e), Is.False, e.Kind.ToString());
            }
            foreach (var e in result.Events.Where(e => e.ActorId == awake.Id))
                Assert.That(discovery.CanMention(run, e), Is.True, $"{e.Kind}: an awake monster is in plain sight.");
        }

        [Test]
        public void APressurePlateOpensDoorsWithoutUncoveringThem()
        {
            var run = Run(
                ".....",
                "....D",
                ".Hp..",
                ".....",
                ".....");
            DoOk(run, PlayerCommand.Move(P(2, 2)));
            Assert.That(run.Floor[P(4, 3)].IsOpenDoor, Is.True);
            Assert.That(run.Floor[P(4, 3)].Knowledge, Is.Not.EqualTo(Knowledge.Revealed), "Where the door is stays for a click to find.");
        }

        [Test]
        public void NoPitsAreGeneratedWhereThereIsNoFloorBelow()
        {
            for (ulong seed = 1; seed <= 200; seed++)
            {
                var last = FloorGenerator.Generate(seed, Catalog.RunFloorCount, Catalog);
                Assert.That(Board.AllCells.Any(p => last[p].Terrain == Terrain.Pit), Is.False, $"Seed {seed}, last floor.");
            }
        }
    }
}
