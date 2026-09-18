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
            Assert.That(Progression.PointsFree(profile), Is.EqualTo(1));
        }

        [Test]
        public void ATalentNeedsAFreePointAndStopsAtItsTopRank()
        {
            var profile = new ProfileState();
            Assert.That(Progression.TryLearn(profile, Progression.Tough), Is.False, "Level 1 has no points.");

            profile.Xp = Progression.XpForLevel(5); // four points
            Assert.That(Progression.TryLearn(profile, Progression.Fleet), Is.True);
            Assert.That(Progression.TryLearn(profile, Progression.Fleet), Is.False, "Fleet has one rank.");
            Assert.That(Progression.TryLearn(profile, "nonsense"), Is.False);
            Assert.That(Progression.TryLearn(profile, Progression.Tough), Is.True);
            Assert.That(Progression.TryLearn(profile, Progression.Tough), Is.True);
            Assert.That(Progression.TryLearn(profile, Progression.Tough), Is.True);
            Assert.That(Progression.PointsFree(profile), Is.Zero);
            Assert.That(Progression.TryLearn(profile, Progression.Stocked), Is.False, "Out of points.");

            Progression.Reset(profile);
            Assert.That(Progression.PointsFree(profile), Is.EqualTo(4), "Resetting refunds every point.");
        }

        [Test]
        public void TalentsShapeTheNewRunAndStayLearned()
        {
            var profile = new ProfileState { Xp = Progression.XpForLevel(10) };
            foreach (var id in new[] { Progression.Tough, Progression.Tough, Progression.Stocked, Progression.QuickShield,
                         Progression.Fleet, Progression.Lucky })
                Assert.That(Progression.TryLearn(profile, id), Is.True, id);

            var plain = NewRun(new ProfileState());
            var trained = NewRun(profile);
            Assert.That(trained.Hero.MaxHp, Is.EqualTo(plain.Hero.MaxHp + 2));
            Assert.That(trained.Hero.Hp, Is.EqualTo(plain.Hero.Hp + 2));
            Assert.That(trained.Hero.Potions, Is.EqualTo(plain.Hero.Potions + 1));
            Assert.That(trained.BonusCoinsPerChestReward, Is.EqualTo(Progression.LuckyCoins));
            Assert.That(Progression.Rank(profile, Progression.Tough), Is.EqualTo(2), "Talents are not spent by a run.");

            // Focus deepens the mana pool and Fleet makes the dash cheaper (D-032).
            Assert.That(trained.Hero.MaxMana, Is.EqualTo(plain.Hero.MaxMana + 1));
            Assert.That(trained.Hero.Mana, Is.EqualTo(trained.Hero.MaxMana));
            Assert.That(trained.DashCostCut, Is.EqualTo(1));
            Assert.That(Mana.DashCost(trained, Catalog.HeroClass("knight")), Is.EqualTo(Catalog.HeroClass("knight").DashCost - 1));
        }

        [Test]
        public void LuckyPaysMoreCoinsPerChestReward()
        {
            var run = Run(".....", ".....", ".HC..", ".....", ".....");
            run.BonusCoinsPerChestReward = Progression.LuckyCoins;
            run.Floor[P(2, 2)].Quality = ChestQuality.Common;
            OpenChest(run, P(2, 2));
            Assert.That(run.CoinsFound, Is.EqualTo(run.Rewards.Count * (Catalog.Treasure.CoinsPerChestReward + Progression.LuckyCoins)));
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
