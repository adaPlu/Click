using ClickDungeon.Domain;
using ClickDungeon.Simulation;
using NUnit.Framework;
using static ClickDungeon.Tests.Scenario;

namespace ClickDungeon.Tests
{
    public class FirstContactAndTurnOrderTests
    {
        /// <summary>A sleeping goblin on the covered tile just above the hero. Clicking that tile bumps it awake (D-023).</summary>
        static RunState GoblinAhead() => Run(
            ".....",
            "..g..",
            "..H..",
            ".....",
            ".....");

        [Test]
        public void RevealedEnemyDeclaresButCannotActThatTurn()
        {
            var run = GoblinAhead();
            var result = DoOk(run, PlayerCommand.Move(P(2, 3)));

            var goblin = Enemy(run, "goblin");
            Assert.That(Has(result, GameEventKind.HeroBumped), Is.True);
            Assert.That(run.Hero.Pos, Is.EqualTo(P(2, 2)), "Bumping a hidden monster does not move the hero.");
            Assert.That(Has(result, GameEventKind.EnemyWoke), Is.True);
            Assert.That(Has(result, GameEventKind.HeroDamaged), Is.False);
            Assert.That(run.Hero.Hp, Is.EqualTo(10));
            Assert.That(goblin.Intent.Kind, Is.EqualTo(IntentKind.Attack));
            Assert.That(goblin.Intent.Target, Is.EqualTo(P(2, 2)));
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
            Assert.That(Enemy(run, "goblin").Intent.Target, Is.EqualTo(P(1, 3)), "The next strike aims where the hero is now.");
        }

        [Test]
        public void ChasersStepDiagonallyThenStrikeWithoutMoving()
        {
            var run = StepRun(
                "....G",
                ".....",
                "..H..",
                ".....",
                ".....");
            var goblin = Enemy(run, "goblin");

            DoOk(run, PlayerCommand.Wait());
            Assert.That(goblin.Pos, Is.EqualTo(P(3, 3)), "One diagonal step toward the hero.");
            Assert.That(goblin.Intent.Kind, Is.EqualTo(IntentKind.Attack), "Touching diagonally is in reach.");

            DoOk(run, PlayerCommand.Wait());
            Assert.That(goblin.Pos, Is.EqualTo(P(3, 3)), "It strikes instead of stepping.");
            Assert.That(goblin.Intent.Target, Is.EqualTo(P(2, 2)));
        }

        [Test]
        public void TilesRevealOnlyWhenClicked()
        {
            // D-023: walking up to a sleeping monster reveals nothing; only clicking its tile does.
            var run = Run(
                ".....",
                ".....",
                "..H..",
                "..g..",
                ".....");
            DoOk(run, PlayerCommand.Move(P(1, 2)));
            var goblin = Enemy(run, "goblin");
            Assert.That(run.Floor[P(2, 1)].Knowledge, Is.Not.EqualTo(Knowledge.Revealed), "Standing beside a tile does not reveal it.");
            Assert.That(goblin.Awake, Is.False);

            var bump = DoOk(run, PlayerCommand.Move(P(2, 1)));
            Assert.That(Has(bump, GameEventKind.HeroBumped), Is.True);
            Assert.That(run.Hero.Pos, Is.EqualTo(P(1, 2)));
            Assert.That(run.Floor[P(2, 1)].Knowledge, Is.EqualTo(Knowledge.Revealed));
            Assert.That(goblin.Awake, Is.True);
        }

        [Test]
        public void DashingIntoAHiddenMonsterBumpsIt()
        {
            var run = Run(
                ".....",
                ".....",
                "H.g..",
                ".....",
                ".....");
            var result = DoOk(run, PlayerCommand.Dash(P(2, 2)));
            Assert.That(Has(result, GameEventKind.HeroBumped), Is.True);
            Assert.That(run.Hero.Pos, Is.EqualTo(P(0, 2)), "The dash stops: the hero stays put.");
            Assert.That(Enemy(run, "goblin").Awake, Is.True);
            Assert.That(run.Hero.Mana, Is.LessThan(run.Hero.MaxMana), "The dash was still paid for.");
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
            var run = StepRun(
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
            // Walls are no longer generated, but they still stop fire wherever one stands (rules §1.2).
            var run = StepRun(
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
            var run = StepRun(
                ".....",
                ".....",
                "HI...",
                ".....",
                ".....");
            var imp = Enemy(run, "fire_imp");
            Assert.That(imp.Intent.Kind, Is.EqualTo(IntentKind.Move));
            DoOk(run, PlayerCommand.Wait());
            Assert.That(imp.Pos.Chebyshev(run.Hero.Pos), Is.GreaterThanOrEqualTo(2), "Out of reach, diagonals included.");
        }

        [Test]
        public void FreeRoamMeleeMustStandNextToTheHeroToAttack()
        {
            // D-021: reach is the same in both modes, so a goblin two tiles away closes in rather than striking.
            var run = Run(
                "....G",
                ".....",
                "..H..",
                ".....",
                ".....");
            var goblin = Enemy(run, "goblin");
            Assert.That(goblin.Intent.Kind, Is.EqualTo(IntentKind.Move));

            DoOk(run, PlayerCommand.Wait());
            Assert.That(run.Hero.Hp, Is.EqualTo(10), "Nothing can hit from two tiles away.");
            Assert.That(goblin.Pos, Is.EqualTo(P(3, 3)));
            Assert.That(goblin.Intent.Kind, Is.EqualTo(IntentKind.Attack), "Now it is next to the hero.");
        }

        [Test]
        public void FreeRoamRangedEnemiesAndTheBossReachFromADistance()
        {
            var lane = Run(
                ".....",
                ".....",
                "H..I.",
                ".....",
                ".....");
            Assert.That(Enemy(lane, "fire_imp").Intent.Kind, Is.EqualTo(IntentKind.Fire));
            DoOk(lane, PlayerCommand.Wait());
            Assert.That(lane.Hero.Hp, Is.EqualTo(8), "The imp shoots from three tiles away.");

            var boss = Run(Catalog.RunFloorCount, 1UL,
                "B....",
                ".....",
                "..H..",
                ".....",
                ".....");
            Assert.That(Enemy(boss, "lord_blobert").Intent.Kind, Is.EqualTo(IntentKind.Slam));
            DoOk(boss, PlayerCommand.Wait());
            Assert.That(boss.Hero.Hp, Is.EqualTo(10 - Catalog.Enemy("lord_blobert").SlamDamage), "Blobert slams from across the room.");
        }

        [Test]
        public void EnemiesPathAroundHazards()
        {
            var run = StepRun(
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
