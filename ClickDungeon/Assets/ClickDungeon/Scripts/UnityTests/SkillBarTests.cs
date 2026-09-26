using System.Collections.Generic;
using System.Linq;
using ClickDungeon.Content;
using ClickDungeon.Domain;
using ClickDungeon.Simulation;
using ClickDungeon.Unity.Screens;
using NUnit.Framework;

namespace ClickDungeon.UnityTests
{
    /// <summary>
    /// D-075: the tiles the skill strip lights up. The screen must never light a tile the rules would refuse, nor leave
    /// a usable one dark - the drift Commands exists to prevent (Unity EditMode only).
    /// </summary>
    public class SkillBarTests
    {
        static readonly ContentCatalog Catalog = ContentCatalog.CreateDefault();

        static RunState Board(string classId, string heroId, params string[] skills)
        {
            var floor = FloorState.CreateEmpty();
            floor.FloorIndex = 1;
            floor.Start = new GridPos(2, 2);
            foreach (var p in ClickDungeon.Simulation.Board.AllCells) floor[p].Knowledge = Knowledge.Revealed;
            var run = new RunState
            {
                RunSeed = 1,
                FloorCount = Catalog.RunFloorCount,
                Hero = new HeroState { ClassId = classId, IdentityId = heroId, Pos = floor.Start, Hp = 10, MaxHp = 20, Mana = 9, MaxMana = 9, SlashDamage = 3 },
                Floor = floor,
                Skills = new List<string>(skills),
            };
            return run;
        }

        [Test]
        public void TheStripLightsExactlyWhatTheRulesWouldAccept()
        {
            // The hero stands at the EDGE, not the middle: from the centre of a five-by-five board every tile is within
            // Chebyshev 2, so a skill of range 2 has nothing out of reach to be refused and the test proves nothing.
            var run = Board("cleric", "lightbringer", "cle_dispel");
            run.Hero.Pos = new GridPos(0, 2);
            var near = EnemyAi.Spawn(run.Floor, Catalog.Enemy("skeleton"), new GridPos(1, 2), awake: true);
            var far = EnemyAi.Spawn(run.Floor, Catalog.Enemy("goblin"), new GridPos(4, 2), awake: true);
            var asleep = EnemyAi.Spawn(run.Floor, Catalog.Enemy("goblin"), new GridPos(2, 2), awake: false);
            Assert.That(run.Hero.Pos.Chebyshev(far.Pos), Is.GreaterThan(Catalog.Skill("cle_dispel").Range),
                "Test setup: the far one really is out of reach.");
            Assert.That(run.Hero.Pos.Chebyshev(asleep.Pos), Is.LessThanOrEqualTo(Catalog.Skill("cle_dispel").Range),
                "Test setup: the sleeping one is in reach, so only being asleep can refuse it.");

            var lit = GameScreen.SkillTargets(run, Catalog, 0);
            // Contains on the set itself: NUnit's Does.Contain binds to its string overload for a HashSet<GridPos>.
            Assert.That(lit.Contains(near.Pos), Is.True, "In reach and awake.");
            Assert.That(lit.Contains(far.Pos), Is.False, "Out of reach.");
            Assert.That(lit.Contains(asleep.Pos), Is.False, "Asleep, so not a target - and not a way to ask what is there.");

            // The whole board, checked both ways: nothing lit is refused, and nothing refused is lit.
            foreach (var p in ClickDungeon.Simulation.Board.AllCells)
            {
                bool accepted = Commands.Validate(run, PlayerCommand.Skill(0, p), Catalog, out _);
                Assert.That(lit.Contains(p), Is.EqualTo(accepted), $"{p}: the strip and the rules disagree.");
            }
        }

        [Test]
        public void AnEmptySlotLightsNothing()
        {
            var run = Board("paladin", "dawnward");
            Assert.That(GameScreen.SkillTargets(run, Catalog, 0), Is.Empty);

            // Nor does a slot holding another class's skill, however it came to be there.
            run.Skills = new List<string> { "cle_dispel" };
            EnemyAi.Spawn(run.Floor, Catalog.Enemy("skeleton"), new GridPos(3, 2), awake: true);
            Assert.That(GameScreen.SkillTargets(run, Catalog, 0), Is.Empty, "A Paladin does not know the Cleric's word.");
        }

        [Test]
        public void WithoutTheManaNothingIsLit()
        {
            var run = Board("cleric", "lightbringer", "cle_dispel");
            EnemyAi.Spawn(run.Floor, Catalog.Enemy("skeleton"), new GridPos(3, 2), awake: true);
            Assert.That(GameScreen.SkillTargets(run, Catalog, 0), Is.Not.Empty, "Test setup: it can be used at all.");

            run.Hero.Mana = Catalog.Skill("cle_dispel").ManaCost - 1;
            Assert.That(GameScreen.SkillTargets(run, Catalog, 0), Is.Empty,
                "A skill that cannot be paid for lights no tiles, rather than lighting them and refusing the click.");
        }

        [Test]
        public void EverySkillTheGameShipsHasWordsToPutOnItsButton()
        {
            foreach (var skill in Catalog.Skills.Values)
            {
                Assert.That(skill.Name, Is.Not.Empty, skill.Id);
                Assert.That(skill.Summary, Is.Not.Empty, skill.Id);
                Assert.That(skill.ManaCost, Is.GreaterThan(0), $"{skill.Id} is free, so it is not a choice.");
                // The strip shows the cost; a hero who can never afford one would be shown a button that never lights.
                var heroClass = Catalog.HeroClass(skill.ClassId);
                Assert.That(skill.ManaCost, Is.LessThanOrEqualTo(heroClass.MaxMana),
                    $"{skill.Id} costs more mana than a {skill.ClassId} can hold.");
            }
        }
    }
}
