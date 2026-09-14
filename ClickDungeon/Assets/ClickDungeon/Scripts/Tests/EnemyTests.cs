using ClickDungeon.Domain;
using ClickDungeon.Simulation;
using NUnit.Framework;
using static ClickDungeon.Tests.Scenario;

namespace ClickDungeon.Tests
{
    public class FirstContactAndTurnOrderTests
    {
        static RunState GoblinAhead() => Run(
            "..g..",
            ".....",
            "..H..",
            ".....",
            ".....");

        [Test]
        public void RevealedEnemyDeclaresButCannotActThatTurn()
        {
            var run = GoblinAhead();
            var result = DoOk(run, PlayerCommand.Move(P(2, 3)));

            var goblin = Enemy(run, "goblin");
            Assert.That(Has(result, GameEventKind.EnemyWoke), Is.True);
            Assert.That(Has(result, GameEventKind.HeroDamaged), Is.False);
            Assert.That(run.Hero.Hp, Is.EqualTo(10));
            Assert.That(goblin.Intent.Kind, Is.EqualTo(IntentKind.Attack));
            Assert.That(goblin.Intent.Target, Is.EqualTo(P(2, 3)));
            Assert.That(goblin.JustWoken, Is.False);
        }

        [Test]
        public void DeclaredAttackLandsNextTurnIfPlayerStays()
        {
            var run = GoblinAhead();
            DoOk(run, PlayerCommand.Move(P(2, 3)));
            DoOk(run, PlayerCommand.Wait());
            Assert.That(run.Hero.Hp, Is.EqualTo(8));
        }

        [Test]
        public void AttacksTargetCellsSoSteppingAwayDodges()
        {
            var run = GoblinAhead();
            DoOk(run, PlayerCommand.Move(P(2, 3)));
            var result = DoOk(run, PlayerCommand.Move(P(1, 3)));

            Assert.That(run.Hero.Hp, Is.EqualTo(10));
            Assert.That(Has(result, GameEventKind.EnemyMissed), Is.True);
            Assert.That(Enemy(run, "goblin").Intent.Kind, Is.EqualTo(IntentKind.Move));
        }

        [Test]
        public void ChaserStepsAlongShortestPathWithDirectionTieBreak()
        {
            var run = GoblinAhead();
            DoOk(run, PlayerCommand.Move(P(2, 3)));
            DoOk(run, PlayerCommand.Move(P(1, 3)));
            DoOk(run, PlayerCommand.Wait());

            var goblin = Enemy(run, "goblin");
            Assert.That(goblin.Pos, Is.EqualTo(P(2, 3)));
            Assert.That(goblin.Intent.Kind, Is.EqualTo(IntentKind.Attack));
            Assert.That(goblin.Intent.Target, Is.EqualTo(P(1, 3)));
        }

        [Test]
        public void EnemiesActInAscendingIdOrder()
        {
            var run = Run(
                ".....",
                ".....",
                "GHG..",
                ".....",
                ".....");
            var result = DoOk(run, PlayerCommand.Wait());
            var attacks = result.Events.FindAll(e => e.Kind == GameEventKind.EnemyAttacked);
            Assert.That(attacks.Count, Is.EqualTo(2));
            Assert.That(attacks[0].ActorId, Is.LessThan(attacks[1].ActorId));
            Assert.That(run.Hero.Hp, Is.EqualTo(6));
        }

        [Test]
        public void DormantEnemiesDoNothing()
        {
            var run = Run(
                "g....",
                ".....",
                ".....",
                ".....",
                "....H");
            DoOk(run, PlayerCommand.Wait());
            DoOk(run, PlayerCommand.Wait());
            var goblin = Enemy(run, "goblin");
            Assert.That(goblin.Pos, Is.EqualTo(P(0, 4)));
            Assert.That(goblin.Awake, Is.False);
        }
    }

    public class EnemyBehaviourTests
    {
        [Test]
        public void CrownedSlimeAlternatesActionAndRest()
        {
            var run = Run(
                ".....",
                ".....",
                "HS...",
                ".....",
                ".....");
            var slime = Enemy(run, "crowned_slime");
            Assert.That(slime.Intent.Kind, Is.EqualTo(IntentKind.Attack));

            DoOk(run, PlayerCommand.Wait());
            Assert.That(run.Hero.Hp, Is.EqualTo(7));
            Assert.That(slime.Intent.Kind, Is.EqualTo(IntentKind.Rest));

            DoOk(run, PlayerCommand.Wait());
            Assert.That(run.Hero.Hp, Is.EqualTo(7));
            Assert.That(slime.Intent.Kind, Is.EqualTo(IntentKind.Attack));
        }

        [Test]
        public void FireImpTelegraphsLaneThenReloads()
        {
            var run = Run(
                ".....",
                ".....",
                "H..I.",
                ".....",
                ".....");
            var imp = Enemy(run, "fire_imp");
            Assert.That(imp.Intent.Kind, Is.EqualTo(IntentKind.Fire));
            Assert.That(imp.Intent.Dir, Is.EqualTo(Direction.Left));

            DoOk(run, PlayerCommand.Wait());
            Assert.That(run.Hero.Hp, Is.EqualTo(8));
            Assert.That(imp.Intent.Kind, Is.EqualTo(IntentKind.Rest));

            DoOk(run, PlayerCommand.Wait());
            Assert.That(run.Hero.Hp, Is.EqualTo(8));
            Assert.That(imp.Intent.Kind, Is.EqualTo(IntentKind.Fire));
        }

        [Test]
        public void LeavingTheLaneDodgesTheShot()
        {
            var run = Run(
                ".....",
                ".....",
                "H..I.",
                ".....",
                ".....");
            DoOk(run, PlayerCommand.Move(P(0, 3)));
            Assert.That(run.Hero.Hp, Is.EqualTo(10));
        }

        [Test]
        public void WallsBlockFireLanes()
        {
            var run = Run(
                ".....",
                ".....",
                "H.#I.",
                ".....",
                ".....");
            Assert.That(Enemy(run, "fire_imp").Intent.Kind, Is.EqualTo(IntentKind.Move));
        }

        [Test]
        public void FireImpBacksAwayWhenAdjacent()
        {
            var run = Run(
                ".....",
                ".....",
                "HI...",
                ".....",
                ".....");
            var imp = Enemy(run, "fire_imp");
            Assert.That(imp.Intent.Kind, Is.EqualTo(IntentKind.Move));
            DoOk(run, PlayerCommand.Wait());
            Assert.That(imp.Pos.Manhattan(run.Hero.Pos), Is.EqualTo(2));
        }

        [Test]
        public void EnemiesPathAroundHazards()
        {
            var run = Run(
                ".....",
                ".....",
                "H^G..",
                ".....",
                ".....");
            DoOk(run, PlayerCommand.Wait());
            var goblin = Enemy(run, "goblin");
            Assert.That(run.Floor[goblin.Pos].Hazard, Is.EqualTo(HazardKind.None));
            Assert.That(goblin.Pos, Is.Not.EqualTo(P(2, 2)));
        }
    }
}
