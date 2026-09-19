using System.Collections.Generic;
using ClickDungeon.Domain;
using ClickDungeon.Simulation;
using NUnit.Framework;
using static ClickDungeon.Tests.Scenario;

namespace ClickDungeon.Tests
{
    public class LordBlobertTests
    {
        static RunState Court() => Run(5, 42UL,
            "....X",
            ".....",
            "H...B",
            ".....",
            ".....");

        [Test]
        public void ScriptRunsSlamSummonPuffUp()
        {
            var run = Court();
            var boss = Enemy(run, "lord_blobert");
            Assert.That(boss.Intent.Kind, Is.EqualTo(IntentKind.Slam));
            Assert.That(boss.Intent.Target, Is.EqualTo(P(0, 2)));

            DoOk(run, PlayerCommand.Wait());
            Assert.That(run.Hero.Hp, Is.EqualTo(6), "Slam hits the telegraphed plus.");
            Assert.That(boss.Intent.Kind, Is.EqualTo(IntentKind.Summon));

            var summon = DoOk(run, PlayerCommand.Wait());
            Assert.That(Has(summon, GameEventKind.EnemySummoned), Is.True);
            var minion = Enemy(run, "slimelet");
            Assert.That(minion, Is.Not.Null);
            Assert.That(minion.Intent.Kind, Is.Not.EqualTo(IntentKind.None), "Summoned minions telegraph first.");
            Assert.That(boss.Intent.Kind, Is.EqualTo(IntentKind.PuffUp));

            DoOk(run, PlayerCommand.Wait());
            Assert.That(boss.Mode, Is.EqualTo(EnemyMode.Puffed));
        }

        [Test]
        public void DashEscapesTheSlam()
        {
            var run = Court();
            // Diagonally out of the 3x3 and off the slam's row and column (D-039).
            DoOk(run, PlayerCommand.Dash(P(2, 4)));
            Assert.That(run.Hero.Hp, Is.EqualTo(10));
        }

        [Test]
        public void SlamShakesItsRowAndColumn()
        {
            var run = Court();
            DoOk(run, PlayerCommand.Dash(P(0, 4)));
            Assert.That(run.Hero.Hp, Is.EqualTo(6), "Two tiles down the target's column still takes the slam (D-039).");
            var shaken = Board.SlamCells(P(0, 2), true);
            Assert.That(shaken.Contains(P(4, 2)) && shaken.Contains(P(0, 0)), Is.True, "The whole row and column shake.");
            Assert.That(shaken.Contains(P(2, 4)), Is.False);
        }

        [Test]
        public void SummonBringsTwoMinions()
        {
            var run = Court();
            run.Hero.Hp = 99;
            run.Hero.MaxHp = 99;
            DoOk(run, PlayerCommand.Dash(P(2, 4)));
            var boss = Enemy(run, "lord_blobert");
            var marked = EnemyAi.SummonCells(run, boss, Catalog.Enemy("lord_blobert"), boss.Intent.Target);
            Assert.That(marked.Count, Is.EqualTo(2), "Both minions' tiles are telegraphed.");
            DoOk(run, PlayerCommand.Wait());
            var minions = run.Floor.Enemies.FindAll(e => e.DefId == "slimelet");
            Assert.That(minions.Count, Is.EqualTo(2));
            Assert.That(minions.TrueForAll(m => marked.Contains(m.Pos)), Is.True, "They appear where they were marked.");
        }

        [Test]
        public void PuffedIsImmuneThenDeflatedTakesDouble()
        {
            var run = Run(5, 42UL,
                "....X",
                ".....",
                "HB...",
                ".....",
                ".....");
            var boss = Enemy(run, "lord_blobert");
            run.Hero.Hp = 99;
            run.Hero.MaxHp = 99;

            boss.Mode = EnemyMode.Puffed;
            boss.ModeTurns = 2;
            var immune = DoOk(run, PlayerCommand.Slash(P(1, 2)));
            Assert.That(Has(immune, GameEventKind.EnemyImmune), Is.True);
            Assert.That(boss.Hp, Is.EqualTo(18));

            boss.Mode = EnemyMode.Deflated;
            DoOk(run, PlayerCommand.Slash(P(1, 2)));
            Assert.That(boss.Hp, Is.EqualTo(14));
        }

        [Test]
        public void PuffLastsTwoTurnsThenDeflatesForOne()
        {
            var run = Court();
            var boss = Enemy(run, "lord_blobert");
            boss.Mode = EnemyMode.Puffed;
            boss.ModeTurns = 2;
            boss.ActionCounter = 3;
            var events = new List<GameEvent>();

            EnemyAi.Declare(run, boss, Catalog, events);
            Assert.That(boss.Mode, Is.EqualTo(EnemyMode.Puffed));
            EnemyAi.Declare(run, boss, Catalog, events);
            Assert.That(boss.Mode, Is.EqualTo(EnemyMode.Puffed));
            EnemyAi.Declare(run, boss, Catalog, events);
            Assert.That(boss.Mode, Is.EqualTo(EnemyMode.Deflated));
            Assert.That(boss.Intent.Kind, Is.EqualTo(IntentKind.Rest));
            EnemyAi.Declare(run, boss, Catalog, events);
            Assert.That(boss.Mode, Is.EqualTo(EnemyMode.Normal));
            Assert.That(boss.Intent.Kind, Is.EqualTo(IntentKind.Slam));
        }

        [Test]
        public void BossDeathClearsMinionsAndUnlocksExit()
        {
            var run = Run(5, 42UL,
                "....X",
                ".....",
                "HB...",
                ".....",
                ".....");
            EnemyAi.Spawn(run.Floor, Catalog.Enemy("slimelet"), P(3, 0), awake: true);
            var boss = Enemy(run, "lord_blobert");
            boss.Hp = 1;

            var result = DoOk(run, PlayerCommand.Slash(P(1, 2)));
            Assert.That(run.Floor.Enemies, Is.Empty);
            Assert.That(run.Floor.ExitUnlocked, Is.True);
            Assert.That(Has(result, GameEventKind.ExitUnlocked), Is.True);
        }
    }
}
