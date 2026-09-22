using System.Collections.Generic;
using ClickDungeon.Content;
using ClickDungeon.Domain;
using ClickDungeon.Simulation;
using NUnit.Framework;
using static ClickDungeon.Tests.Scenario;

namespace ClickDungeon.Tests
{
    /// <summary>
    /// D-062: a boss closes each act of five floors. These pin the three new bosses' scripts, and the two rules that make a
    /// mid-run boss work: beating one restores the hero and opens the way on, and no pit lets the hero drop past one.
    /// </summary>
    public class ActBossTests
    {
        // ------------------------------------------------------------------ what every act boss shares

        [Test]
        public void BeatingAnActBossRestoresTheHeroAndOpensTheWayOnWithoutEndingTheRun()
        {
            var run = Revealed(Run(ContentCatalog.BossEvery, 5UL, ".....", ".....", ".HN..", ".....", "....X"));
            var king = Enemy(run, "goblin_brute_king");
            run.Hero.Hp = 2;
            run.Hero.Mana = 0;
            king.Hp = 1;

            var result = DoOk(run, PlayerCommand.Slash(king.Pos));

            Assert.That(run.Hero.Hp, Is.EqualTo(run.Hero.MaxHp), "An act ends with every heart back.");
            Assert.That(run.Hero.Mana, Is.EqualTo(run.Hero.MaxMana), "And all mana.");
            Assert.That(result.Events.Exists(e => e.Kind == GameEventKind.HeroHealed && e.Source == "act_cleared"), Is.True);
            Assert.That(run.Floor.ExitUnlocked, Is.True);
            Assert.That(run.Status, Is.EqualTo(RunStatus.InProgress), "A boss before the last floor is not the end of the run.");

            DoOk(run, PlayerCommand.Move(run.Floor.Exit));
            Assert.That(run.Floor.FloorIndex, Is.EqualTo(ContentCatalog.BossEvery + 1), "On to the next act.");
            Assert.That(run.Status, Is.EqualTo(RunStatus.InProgress));
        }

        [Test]
        public void NoPitLetsTheHeroDropPastABoss()
        {
            var run = Revealed(Run(ContentCatalog.BossEvery, 5UL, ".....", ".....", ".HN..", ".o...", "....X"));
            Assert.That(Board.CanFallThrough(run), Is.False);
            Assert.That(Do(run, PlayerCommand.Move(P(1, 1))).Accepted, Is.False, "The pit does not lead past the boss.");
        }

        // ------------------------------------------------------------------ Goblin Brute King

        [Test]
        public void TheGoblinKingEnragesAtHalfHisHeartsAndHitsOneHarder()
        {
            var run = Revealed(Run(ContentCatalog.BossEvery, 5UL, ".....", ".....", ".HN..", ".....", "....X"));
            var king = Enemy(run, "goblin_brute_king");
            var def = Catalog.Enemy("goblin_brute_king");
            king.Hp = king.MaxHp / 2 + 2;   // one slash takes him to half or below

            var result = DoOk(run, PlayerCommand.Slash(king.Pos));

            Assert.That(Has(result, GameEventKind.BossEnraged), Is.True);
            Assert.That(king.Mode, Is.EqualTo(EnemyMode.Enraged));
            // Whichever blow he declares, its warning must carry the extra point it will really deal.
            var blows = new[]
            {
                (Intent.Attack(run.Hero.Pos), ThreatKind.Attack, def.Damage),
                (Intent.Charge(Direction.Left), ThreatKind.Charge, def.Damage),
                (Intent.Slam(run.Hero.Pos), ThreatKind.Slam, def.SlamDamage),
            };
            foreach (var (intent, kind, dmg) in blows)
            {
                king.Intent = intent;
                var his = Threats.Compute(run, Catalog).FindAll(t => t.SourceId == king.Id && t.Kind == kind);
                Assert.That(his, Is.Not.Empty, $"{kind} is drawn.");
                foreach (var t in his) Assert.That(t.Damage, Is.EqualTo(dmg + 1), $"{kind} at {t.Cell}");
            }
        }

        [Test]
        public void TheGoblinKingDoesNotEnrageAboveHalf()
        {
            var run = Revealed(Run(ContentCatalog.BossEvery, 5UL, ".....", ".....", ".HN..", ".....", "....X"));
            var king = Enemy(run, "goblin_brute_king");
            DoOk(run, PlayerCommand.Slash(king.Pos));
            Assert.That(king.Hp * 2, Is.GreaterThan(king.MaxHp), "Test setup: one slash leaves him above half.");
            Assert.That(king.Mode, Is.EqualTo(EnemyMode.Normal));
        }

        // ------------------------------------------------------------------ Bat Swarm Leader

        [Test]
        public void TheBatLeaderCallsBatsAndKeepsNoMoreThanFour()
        {
            var run = Revealed(Run(2 * ContentCatalog.BossEvery, 5UL, ".....", ".....", "..V..", ".....", "H...X"));
            var leader = Enemy(run, "bat_swarm_leader");
            Assert.That(leader.Intent.Kind, Is.EqualTo(IntentKind.Summon));

            DoOk(run, PlayerCommand.Wait());
            Assert.That(run.Floor.Enemies.FindAll(e => e.DefId == "bat").Count, Is.EqualTo(2), "Two at a call.");

            foreach (var p in new[] { P(0, 3), P(4, 3) })
                EnemyAi.Spawn(run.Floor, Catalog.Enemy("bat"), p, awake: true);
            leader.ActionCounter = 0;
            leader.Intent = Intent.None();
            EnemyAi.Declare(run, leader, Catalog, new List<GameEvent>());
            Assert.That(leader.Intent.Kind, Is.Not.EqualTo(IntentKind.Summon), "Four is the swarm's limit.");
        }

        [Test]
        public void TheBatLeaderDivesDownAClearLine()
        {
            var run = Revealed(Run(2 * ContentCatalog.BossEvery, 5UL, ".....", ".....", "V..H.", ".....", "....X"));
            var leader = Enemy(run, "bat_swarm_leader");
            leader.ActionCounter = 1;   // the dive
            leader.Intent = Intent.None();
            EnemyAi.Declare(run, leader, Catalog, new List<GameEvent>());
            Assert.That(leader.Intent.Kind, Is.EqualTo(IntentKind.Charge));
            Assert.That(leader.Intent.Dir, Is.EqualTo(Direction.Right));
        }

        // ------------------------------------------------------------------ Theater Curtain Demon

        [Test]
        public void TheCurtainFallsAcrossTheHerosWholeRowAndColumn()
        {
            var run = Revealed(Run(3 * ContentCatalog.BossEvery, 5UL, ".....", ".....", ".H...", ".....", "T...X"));
            var demon = Enemy(run, "theater_curtain_demon");
            demon.ActionCounter = 1;   // the curtain slam
            demon.Intent = Intent.None();
            EnemyAi.Declare(run, demon, Catalog, new List<GameEvent>());

            Assert.That(demon.Intent.Kind, Is.EqualTo(IntentKind.Slam));
            var slammed = Threats.Compute(run, Catalog).FindAll(t => t.Kind == ThreatKind.Slam);
            Assert.That(slammed.Exists(t => t.Cell == P(4, 2)), Is.True, "The far end of the hero's row.");
            Assert.That(slammed.Exists(t => t.Cell == P(1, 4)), Is.True, "The far end of the hero's column.");
        }

        [Test]
        public void TheCurtainDemonVanishesToTheTileItMarked()
        {
            var run = Revealed(Run(3 * ContentCatalog.BossEvery, 5UL, ".....", ".....", ".H...", ".....", "T...X"));
            var demon = Enemy(run, "theater_curtain_demon");
            demon.ActionCounter = 3;   // the vanishing act
            demon.Intent = Intent.None();
            EnemyAi.Declare(run, demon, Catalog, new List<GameEvent>());
            Assert.That(demon.Intent.Kind, Is.EqualTo(IntentKind.Vanish));
            var stage = demon.Intent.Target;
            Assert.That(Threats.Compute(run, Catalog).Exists(t => t.Kind == ThreatKind.Arrive && t.Cell == stage), Is.True,
                "Where it will reappear is marked the turn before.");
            Assert.That(stage.Chebyshev(run.Hero.Pos), Is.GreaterThan(demon.Pos.Chebyshev(run.Hero.Pos)), "It goes farther away.");

            var result = DoOk(run, PlayerCommand.Wait());

            Assert.That(Has(result, GameEventKind.BossVanished), Is.True);
            Assert.That(demon.Pos, Is.EqualTo(stage));
        }

        [Test]
        public void TheCurtainDemonSummonsMasks()
        {
            var run = Revealed(Run(3 * ContentCatalog.BossEvery, 5UL, ".....", ".....", ".H...", ".....", "T...X"));
            Assert.That(Enemy(run, "theater_curtain_demon").Intent.Kind, Is.EqualTo(IntentKind.Summon));
            DoOk(run, PlayerCommand.Wait());
            Assert.That(run.Floor.Enemies.Exists(e => e.DefId == "stage_mask"), Is.True);
        }
    }
}
