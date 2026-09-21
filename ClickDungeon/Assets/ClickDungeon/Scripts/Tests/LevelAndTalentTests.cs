using System.Collections.Generic;
using ClickDungeon.Application;
using ClickDungeon.Content;
using ClickDungeon.Domain;
using ClickDungeon.Simulation;
using NUnit.Framework;
using static ClickDungeon.Tests.Scenario;

namespace ClickDungeon.Tests
{
    /// <summary>D-027: experience from runs sets the level, levels give talent points, and talents shape every new run.</summary>
    public class LevelAndTalentTests
    {
        static RunState NewRun(ProfileState profile)
        {
            var run = RunFactory.NewRun(11UL, Catalog, new List<GameEvent>(), ContentCatalog.DefaultHeroId, MovementMode.Free);
            Progression.Apply(profile, run, Catalog);
            return run;
        }

        /// <summary>
        /// D-054: banking onto a profile that is already at the ceiling leaves it there. The store clamps on load, so
        /// without a clamp here too an absurd total wraps negative and stays that way for the rest of the session —
        /// long enough to show the player level 1 and take their talent points away.
        /// </summary>
        [Test]
        public void BankingOntoACappedProfileLeavesItCappedRatherThanNegative()
        {
            int ceiling = Progression.XpForLevel(Progression.MaxLevel);
            var profile = new ProfileState { Xp = ceiling };
            var run = Scenario.Run(".....", ".....", "..H..", ".....", ".....");
            run.XpEarned = 500;

            Progression.BankXp(profile, run);

            Assert.That(profile.Xp, Is.EqualTo(ceiling), "A capped profile stays capped.");
            Assert.That(Progression.Level(profile), Is.EqualTo(Progression.MaxLevel), "And still reads as the top level.");
        }

        [Test]
        public void TheLevelCurveClimbsByTriangleNumbers()
        {
            Assert.That(Progression.XpForLevel(1), Is.Zero);
            Assert.That(Progression.XpForLevel(2), Is.EqualTo(50));
            Assert.That(Progression.XpForLevel(3), Is.EqualTo(150));
            Assert.That(Progression.XpForLevel(4), Is.EqualTo(300));
            Assert.That(Progression.Level(0), Is.EqualTo(1));
            Assert.That(Progression.Level(49), Is.EqualTo(1));
            Assert.That(Progression.Level(50), Is.EqualTo(2));
            Assert.That(Progression.Level(299), Is.EqualTo(3));
            Assert.That(Progression.Level(300), Is.EqualTo(4));
        }

        /// <summary>
        /// DATA-17: the curve is built from a product that wraps past level 6554, so above about a billion experience it
        /// stops rising and the level search — one level at a time, on the main thread, inside the session's constructor —
        /// never finds a level it cannot afford. The ceiling is what makes it stop. The timeout is the regression's alarm:
        /// without a bound this test does not fail, it hangs.
        /// </summary>
        [Test, Timeout(20000)]
        public void TheLevelCurveStopsAtACeilingRatherThanClimbingForEver()
        {
            Assert.That(Progression.Level(int.MaxValue), Is.EqualTo(Progression.MaxLevel));
            Assert.That(Progression.Level(Progression.XpForLevel(Progression.MaxLevel)), Is.EqualTo(Progression.MaxLevel));
            Assert.That(Progression.XpForLevel(Progression.MaxLevel), Is.GreaterThan(0), "The last level costs a real, unwrapped number.");
            Assert.That(Progression.XpForLevel(int.MaxValue), Is.EqualTo(Progression.XpForLevel(Progression.MaxLevel)),
                "And the curve is flat past it rather than wrapping negative.");
        }

        [Test]
        public void RunsEarnExperienceForMonstersFloorsTheBossAndTheWin()
        {
            var goblin = Run(".....", ".....", ".HG..", ".....", ".....");
            var mob = Enemy(goblin, "goblin");
            mob.Hp = 1;
            DoOk(goblin, PlayerCommand.Slash(mob.Pos));
            Assert.That(goblin.XpEarned, Is.EqualTo(Catalog.Xp.PerMonster));

            var stairs = Run(".....", ".....", ".HxK.", ".....", ".....");
            DoOk(stairs, PlayerCommand.Move(P(3, 2)));
            DoOk(stairs, PlayerCommand.Move(P(2, 2)));
            Assert.That(stairs.XpEarned, Is.EqualTo(Catalog.Xp.PerFloor));

            var boss = Run(Catalog.RunFloorCount, 1234UL, ".....", ".....", ".HB..", ".....", "....X");
            var blobert = Enemy(boss, "lord_blobert");
            blobert.Mode = EnemyMode.Normal;
            blobert.Hp = 1;
            DoOk(boss, PlayerCommand.Slash(blobert.Pos));
            Assert.That(boss.XpEarned, Is.EqualTo(Catalog.Xp.ForTheBoss));
        }

        [Test]
        public void ExperienceIsBankedWhenTheRunEnds()
        {
            var profile = new ProfileState();
            var run = Run(".....", ".....", "..H..", ".....", ".....");
            run.XpEarned = 60;
            run.Status = RunStatus.Lost;
            ProfileSystem.Bank(profile, run);
            Assert.That(profile.Xp, Is.EqualTo(60));
            Assert.That(Progression.Level(profile), Is.EqualTo(2));
            Assert.That(Progression.PointsFree(profile, Catalog, "knight"), Is.EqualTo(1));
        }

        [Test]
        public void TheDashNeverCostsLessThanOneMana()
        {
            var run = Run(".....", ".....", "H....", ".....", ".....");
            run.DashCostCut = 99;
            run.Hero.Mana = 1;
            DoOk(run, PlayerCommand.Dash(P(2, 2)));
            Assert.That(run.Hero.Mana, Is.EqualTo(Mana.PerTurn), "Paid one, then one back at the end of the turn.");
        }
    }
}
