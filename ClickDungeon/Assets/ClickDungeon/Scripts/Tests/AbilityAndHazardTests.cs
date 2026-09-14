using ClickDungeon.Domain;
using ClickDungeon.Simulation;
using NUnit.Framework;
using static ClickDungeon.Tests.Scenario;

namespace ClickDungeon.Tests
{
    public class ShieldTests
    {
        [Test]
        public void ShieldBlocksTelegraphedMeleeAndStaggers()
        {
            var run = Run(
                "..g..",
                ".....",
                "..H..",
                ".....",
                ".....");
            DoOk(run, PlayerCommand.Move(P(2, 3)));
            var result = DoOk(run, PlayerCommand.Shield());

            var goblin = Enemy(run, "goblin");
            Assert.That(run.Hero.Hp, Is.EqualTo(10));
            Assert.That(Has(result, GameEventKind.HeroBlocked), Is.True);
            Assert.That(goblin.Intent.Kind, Is.EqualTo(IntentKind.Recover));
            Assert.That(run.Hero.Guard, Is.False, "Guard lasts only through the enemy phase.");
        }

        [Test]
        public void ShieldCooldownSkipsTwoTurns()
        {
            var run = Run(
                "..g..",
                ".....",
                "..H..",
                ".....",
                ".....");
            DoOk(run, PlayerCommand.Move(P(2, 3)));
            DoOk(run, PlayerCommand.Shield());

            Assert.That(Do(run, PlayerCommand.Shield()).Accepted, Is.False);
            DoOk(run, PlayerCommand.Slash(P(2, 4)));
            Assert.That(run.Hero.Hp, Is.EqualTo(10), "Staggered goblin recovers instead of attacking.");

            Assert.That(Do(run, PlayerCommand.Shield()).Accepted, Is.False);
            DoOk(run, PlayerCommand.Slash(P(2, 4)));
            Assert.That(run.Floor.Enemies, Is.Empty);

            DoOk(run, PlayerCommand.Shield());
        }
    }

    public class DashTests
    {
        [Test]
        public void DashJumpsOverHazards()
        {
            var run = Run(
                ".....",
                ".....",
                "H^...",
                ".....",
                ".....");
            DoOk(run, PlayerCommand.Dash(P(2, 2)));
            Assert.That(run.Hero.Pos, Is.EqualTo(P(2, 2)));
            Assert.That(run.Hero.Hp, Is.EqualTo(10));
            Assert.That(run.Hero.DashCooldown, Is.EqualTo(2));
            Assert.That(Do(run, PlayerCommand.Dash(P(0, 2))).Accepted, Is.False);
        }

        [Test]
        public void LandingOnSpikesHurts()
        {
            var run = Run(
                ".....",
                ".....",
                "H.^..",
                ".....",
                ".....");
            DoOk(run, PlayerCommand.Dash(P(2, 2)));
            Assert.That(run.Hero.Hp, Is.EqualTo(8));
        }

        [TestCase("H#...")]
        [TestCase("Ho...")]
        [TestCase("HC...")]
        [TestCase("HG...")]
        [TestCase("H.g..")]
        [TestCase("H.#..")]
        public void DashRejectsBlockedPathsAndEnemyClues(string row)
        {
            var run = Run(
                ".....",
                ".....",
                row,
                ".....",
                ".....");
            Assert.That(Do(run, PlayerCommand.Dash(P(2, 2))).Accepted, Is.False);
        }

        [Test]
        public void DashMustBeStraightAndExact()
        {
            var run = Run(
                ".....",
                ".....",
                "..H..",
                ".....",
                ".....");
            Assert.That(Do(run, PlayerCommand.Dash(P(3, 3))).Accepted, Is.False);
            Assert.That(Do(run, PlayerCommand.Dash(P(2, 3))).Accepted, Is.False);
            Assert.That(Do(run, PlayerCommand.Dash(P(2, 4))).Accepted, Is.True);
        }
    }

    public class PotionTests
    {
        [Test]
        public void PotionHealsAndIsConsumed()
        {
            var run = Run(
                ".....",
                ".....",
                "..H..",
                ".....",
                ".....");
            run.Hero.Hp = 5;
            DoOk(run, PlayerCommand.Potion());
            Assert.That(run.Hero.Hp, Is.EqualTo(9));
            Assert.That(run.Hero.Potions, Is.EqualTo(1));
        }

        [Test]
        public void PotionNeverOverheals()
        {
            var run = Run(
                ".....",
                ".....",
                "..H..",
                ".....",
                ".....");
            run.Hero.Hp = 8;
            DoOk(run, PlayerCommand.Potion());
            Assert.That(run.Hero.Hp, Is.EqualTo(10));
        }

        [Test]
        public void PotionRejectedAtFullHealthOrWhenEmpty()
        {
            var run = Run(
                ".....",
                ".....",
                "..H..",
                ".....",
                ".....");
            Assert.That(Do(run, PlayerCommand.Potion()).Accepted, Is.False);
            run.Hero.Hp = 3;
            run.Hero.Potions = 0;
            Assert.That(Do(run, PlayerCommand.Potion()).Accepted, Is.False);
        }
    }

    public class HazardTests
    {
        [Test]
        public void WalkingOntoSpikesCostsHp()
        {
            var run = Run(
                ".....",
                ".....",
                "H^...",
                ".....",
                ".....");
            var result = DoOk(run, PlayerCommand.Move(P(1, 2)));
            Assert.That(run.Hero.Hp, Is.EqualTo(8));
            Assert.That(Has(result, GameEventKind.SpikesTriggered), Is.True);
        }

        [Test]
        public void SteppingOnBombGivesOneActionBeforeBlast()
        {
            var run = Run(
                ".....",
                ".....",
                ".Hb..",
                ".....",
                ".....");
            DoOk(run, PlayerCommand.Move(P(2, 2)));
            Assert.That(run.Floor[P(2, 2)].BombArmed, Is.True);
            Assert.That(run.Hero.Hp, Is.EqualTo(10));

            var result = DoOk(run, PlayerCommand.Wait());
            Assert.That(Has(result, GameEventKind.BombExploded), Is.True);
            Assert.That(run.Hero.Hp, Is.EqualTo(6));
            Assert.That(run.Floor[P(2, 2)].Hazard, Is.EqualTo(HazardKind.None));
        }

        [Test]
        public void DashingOutEscapesTheBlast()
        {
            var run = Run(
                ".....",
                ".....",
                ".Hb..",
                ".....",
                ".....");
            DoOk(run, PlayerCommand.Move(P(2, 2)));
            DoOk(run, PlayerCommand.Dash(P(4, 2)));
            Assert.That(run.Hero.Hp, Is.EqualTo(10));
        }

        [Test]
        public void SlashedBombDamagesEnemiesInBlast()
        {
            var run = Run(
                ".....",
                ".....",
                ".gb..",
                "..H..",
                ".....");
            DoOk(run, PlayerCommand.Slash(P(2, 2)));
            DoOk(run, PlayerCommand.Move(P(2, 0)));
            Assert.That(run.Floor.Enemies, Is.Empty);
            Assert.That(run.Hero.Hp, Is.EqualTo(10));
        }
    }
}
