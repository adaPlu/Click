using ClickDungeon.Content;
using ClickDungeon.Domain;
using ClickDungeon.Simulation;
using ClickDungeon.Unity.Screens;
using NUnit.Framework;

namespace ClickDungeon.UnityTests
{
    /// <summary>Hover text never leaks what is under a cover (Unity EditMode only).</summary>
    public class InspectTileTests
    {
        static readonly ContentCatalog Catalog = ContentCatalog.CreateDefault();

        static readonly GridPos Pit = new GridPos(0, 0);
        static readonly GridPos Door = new GridPos(4, 4);
        static readonly GridPos Wall = new GridPos(4, 0);
        static readonly GridPos Lurker = new GridPos(0, 4);

        static RunState CoveredBoard(MovementMode movement)
        {
            var floor = FloorState.CreateEmpty();
            floor.FloorIndex = 1;
            floor.Start = new GridPos(2, 2);
            floor[Pit].Terrain = Terrain.Pit;
            floor[Door].Terrain = Terrain.Door;
            floor[Wall].Terrain = Terrain.Wall;
            EnemyAi.Spawn(floor, Catalog.Enemy("goblin"), Lurker, awake: false);
            return new RunState
            {
                RunSeed = 1,
                FloorCount = Catalog.RunFloorCount,
                Hero = new HeroState { Pos = floor.Start, Hp = 10, MaxHp = 10, SlashDamage = 2 },
                Floor = floor,
                Movement = movement,
            };
        }

        [Test]
        public void CoveredTilesReadAsUnknownWhateverIsUnderThem()
        {
            // D-021 / D-023: hovering must not reveal a pit, a vault door, a wall or a sleeping monster.
            var run = CoveredBoard(MovementMode.Free);
            foreach (var p in new[] { Pit, Door, Wall, Lurker })
            {
                var text = GameScreen.InspectTile(run, p, Catalog, out var title);
                Assert.That(title, Is.EqualTo("UNKNOWN"), p.ToString());
                Assert.That(text, Does.Not.Contain("pit").IgnoreCase.And.Not.Contain("door").IgnoreCase.And.Not.Contain("goblin").IgnoreCase, p.ToString());
                Assert.That(text, Does.Contain("Click it"), "Free Roam has no sensing, so the only way to learn is to click.");
            }
        }

        [Test]
        public void UncoveredTilesSayWhatTheyAre()
        {
            var run = CoveredBoard(MovementMode.Free);
            run.Floor[Pit].Knowledge = Knowledge.Revealed;
            GameScreen.InspectTile(run, Pit, Catalog, out var title);
            Assert.That(title, Is.EqualTo("PIT"));
        }

        [Test]
        public void StepByStepKeepsItsSensingHints()
        {
            var run = CoveredBoard(MovementMode.Step);
            GameScreen.InspectTile(run, Pit, Catalog, out var unseen);
            Assert.That(unseen, Is.EqualTo("UNKNOWN"));

            run.Floor[Lurker].Knowledge = Knowledge.Sensed;
            var text = GameScreen.InspectTile(run, Lurker, Catalog, out var sensed);
            Assert.That(sensed, Is.EqualTo("SENSED"));
            Assert.That(text, Does.Contain("Click it to uncover it."));
        }
    }
}
