using System.Collections.Generic;
using ClickDungeon.Application;
using ClickDungeon.Content;
using ClickDungeon.Domain;
using ClickDungeon.Simulation;
using NUnit.Framework;
using static ClickDungeon.Tests.Scenario;

namespace ClickDungeon.Tests
{
    /// <summary>
    /// The load-bearing promise of the whole design (rules 3.2): a monster only ever does what the player was shown a
    /// turn earlier, for the number they were shown. Audit 4 found four separate ways that broke - an enrage raising a
    /// blow already drawn (REL-36), a knocked-back shooter firing down a line nobody saw (REL-37), a warning erased by
    /// another warning on the same tile (REL-38), and a summon overrunning the marks (REL-40) - because every test
    /// until now checked one side of the seam. This checks both sides against each other.
    /// </summary>
    public class TelegraphTests
    {
        /// <summary>Damage a monster actually dealt, by the tile it landed on.</summary>
        static readonly GameEventKind[] Blows =
        {
            GameEventKind.EnemyAttacked, GameEventKind.EnemyFired, GameEventKind.BossSlammed,
        };

        [Test]
        public void TheTelegraphIsWhatHappens()
        {
            var catalog = ContentCatalog.CreateDefault(Difficulty.Medium);
            int blows = 0, deepest = 0;
            var bosses = new HashSet<string>();
            // Ironheart for most of it, and Emberwisp on some seeds because knockback is the Wizard's class rule and
            // a sweep that never shoves a monster cannot see whether a shove moves a declared line (REL-37).
            for (ulong seed = 1; seed <= 12; seed++)
            {
                var player = new AutoPlayer(0.0, blind: false, loots: true);
                var run = RunFactory.NewRun(seed, catalog, new List<GameEvent>(),
                    seed % 3 == 0 ? "emberwisp" : ContentCatalog.DefaultHeroId);
                for (int turn = 0; turn < 400 && run.Status == RunStatus.InProgress; turn++)
                {
                    // What the player is looking at when they commit: the board at a stable boundary.
                    var shown = Threats.Compute(run, catalog);
                    var command = player.Choose(run, catalog, seed * 7919UL + (ulong)turn);
                    var result = TurnResolver.Apply(run, command, catalog);
                    Assert.That(result.Accepted, Is.True, $"seed {seed} turn {turn}: {command} was refused - {result.RejectReason}");
                    deepest = System.Math.Max(deepest, run.Floor.FloorIndex);
                    foreach (var e in result.Events)
                    {
                        if (System.Array.IndexOf(Blows, e.Kind) < 0 || e.Amount <= 0 || !e.To.InBounds) continue;
                        blows++;
                        if (catalog.HasEnemy(e.Source) && catalog.Enemy(e.Source).IsBoss) bosses.Add(e.Source);
                        var marked = shown.FindAll(t => t.Cell == e.To && t.Damage > 0);
                        Assert.That(marked, Is.Not.Empty,
                            $"seed {seed} turn {turn}: {e.Kind} by {e.Source} landed on {e.To}, which the board never marked.");
                        Assert.That(marked.Exists(t => t.Damage == e.Amount), Is.True,
                            $"seed {seed} turn {turn}: {e.Kind} by {e.Source} dealt {e.Amount} on {e.To}, "
                            + $"but the board promised {string.Join("/", marked.ConvertAll(t => t.Damage.ToString()))}.");
                    }
                }
            }
            Assert.That(blows, Is.GreaterThan(40), "Too few blows landed to call this a test.");
            // Without these the sweep could quietly stop reaching the content it exists to cover and stay green.
            Assert.That(deepest, Is.GreaterThanOrEqualTo(2 * ContentCatalog.BossEvery),
                $"The bot only reached floor {deepest}; this sweep is meant to cross act bosses.");
            Assert.That(bosses, Is.Not.Empty, "No boss ever landed a blow, so no boss telegraph was checked.");
        }

        [Test]
        public void EnragingDoesNotRaiseABlowThatIsAlreadyDeclared()
        {
            // REL-36. The king declares while whole, the player's slash enrages him, and the blow he already showed
            // has to land for what it showed. His *next* blow is the one that costs more.
            var run = Revealed(Run(ContentCatalog.BossEvery, 5UL, ".....", ".....", ".HN..", ".....", "....X"));
            var king = Enemy(run, "goblin_brute_king");
            var def = Catalog.Enemy("goblin_brute_king");
            king.Hp = king.MaxHp / 2 + 2;
            Assert.That(king.Intent.Kind, Is.EqualTo(IntentKind.Attack), "Test setup: a blow is declared at the hero.");
            int promised = Threats.DamageAt(Threats.Compute(run, Catalog), run.Hero.Pos);
            Assert.That(promised, Is.EqualTo(def.Damage), "Declared while whole, so no fury in the number.");

            int before = run.Hero.Hp;
            var result = DoOk(run, PlayerCommand.Slash(king.Pos));

            Assert.That(king.Mode, Is.EqualTo(EnemyMode.Enraged), "The slash did drive him past half.");
            Assert.That(Has(result, GameEventKind.BossEnraged), Is.True);
            Assert.That(before - run.Hero.Hp, Is.EqualTo(promised), "The blow that was already shown lands for what it showed.");
            Assert.That(Threats.DamageAt(Threats.Compute(run, Catalog), run.Hero.Pos), Is.EqualTo(def.Damage + 1),
                "And the next one is drawn one harder.");
        }

        [Test]
        public void AnEnragedBlowCostsTheHeartsItsWarningPromised()
        {
            // The other half: once the rage is showing, the blow must actually cost that much.
            var run = Revealed(Run(ContentCatalog.BossEvery, 5UL, ".....", ".....", ".HN..", ".....", "....X"));
            var king = Enemy(run, "goblin_brute_king");
            king.Hp = king.MaxHp / 2 + 2;
            DoOk(run, PlayerCommand.Slash(king.Pos));            // enrages; the blow already declared lands here
            run.Hero.Hp = run.Hero.MaxHp;

            int promised = Threats.DamageAt(Threats.Compute(run, Catalog), run.Hero.Pos);
            Assert.That(promised, Is.GreaterThan(0), "Test setup: something is aimed at the hero's tile.");
            int before = run.Hero.Hp;
            DoOk(run, PlayerCommand.Wait());
            Assert.That(before - run.Hero.Hp, Is.EqualTo(promised));
        }

        [Test]
        public void AShovedShooterStillFiresDownTheLineItDrew()
        {
            // REL-37: a lane and a charge used to be traced from wherever the monster stood when it executed, so a
            // Wizard's shove moved the line off the tiles the board had drawn. The line is anchored at the tile it was
            // declared from now, which means the shove changes where the monster is and nothing about what it hits -
            // it does not hand the player a free cancel, and the monster still pays its winded turn.
            var run = Revealed(As(Run(".....", ".....", "H.R..", ".....", "....X"), "emberwisp"));
            var boar = Enemy(run, "armored_boar");
            boar.Intent = Intent.Charge(Direction.Left, boar.Pos);
            boar.Hp = boar.MaxHp;                                 // survives the bolt and acts

            var drawn = Threats.Compute(run, Catalog).FindAll(t => t.SourceId == boar.Id && t.Kind == ThreatKind.Charge)
                .ConvertAll(t => t.Cell);
            Assert.That(drawn, Does.Contain(run.Hero.Pos), "Test setup: the drawn line runs through the hero.");
            int promised = Threats.DamageAt(Threats.Compute(run, Catalog), run.Hero.Pos);
            int before = run.Hero.Hp;

            var result = DoOk(run, PlayerCommand.Slash(boar.Pos));

            Assert.That(Has(result, GameEventKind.EnemyKnockedBack), Is.True, "Test setup: the bolt did shove it.");
            Assert.That(boar.Hp, Is.GreaterThan(0), "Test setup: it survived to act.");
            Assert.That(before - run.Hero.Hp, Is.EqualTo(promised), "The charge lands for what the board promised.");
            Assert.That(boar.Intent.Kind, Is.EqualTo(IntentKind.Rest), "And it is winded afterwards, shove or no shove.");
        }

        [Test]
        public void ADeclaredLineIsTracedFromWhereItWasDeclared()
        {
            // REL-37, stated as the invariant rather than as one of its symptoms: whatever moves a monster between its
            // declaration and its turn - today a Wizard's knockback, tomorrow a pad, a pull or a second shove - the
            // lane resolves from the tile it was declared on, which is the tile the board drew it from.
            var run = Revealed(Run(".....", ".....", "H.I..", ".....", "....X"));
            var imp = Enemy(run, "fire_imp");
            imp.Intent = Intent.Fire(Direction.Left, imp.Pos);
            var drawn = Threats.Compute(run, Catalog).FindAll(t => t.SourceId == imp.Id).ConvertAll(t => t.Cell);

            imp.Pos = P(2, 4);                                    // displaced, by whatever means

            var after = Threats.Compute(run, Catalog).FindAll(t => t.SourceId == imp.Id).ConvertAll(t => t.Cell);
            Assert.That(after, Is.EqualTo(drawn), "The telegraph must not move when the monster does.");
            int promised = Threats.DamageAt(Threats.Compute(run, Catalog), run.Hero.Pos);
            Assert.That(promised, Is.GreaterThan(0), "Test setup: the lane runs through the hero.");
            int before = run.Hero.Hp;
            DoOk(run, PlayerCommand.Wait());
            Assert.That(before - run.Hero.Hp, Is.EqualTo(promised), "And the blow lands where it was drawn, not where the monster went.");
        }

        [Test]
        public void ASummonNeverPutsOutMoreMinionsThanItsLimit()
        {
            // REL-40: the cap was read once at declare, then a whole batch was placed.
            foreach (var (letter, bossId, minionId) in new[] { ('V', "bat_swarm_leader", "bat"), ('T', "theater_curtain_demon", "stage_mask") })
            {
                var def = Catalog.Enemy(bossId);
                var run = Revealed(Run(2 * ContentCatalog.BossEvery, 5UL, ".....", ".....", $"..{letter}..", ".....", "H...X"));
                var boss = Enemy(run, bossId);
                for (int turn = 0; turn < 12 && boss.Hp > 0 && run.Status == RunStatus.InProgress; turn++)
                {
                    run.Hero.Hp = run.Hero.MaxHp;                 // the cap is what is under test, not survival
                    DoOk(run, PlayerCommand.Wait());
                    int out_ = run.Floor.Enemies.FindAll(e => e.DefId == minionId).Count;
                    Assert.That(out_, Is.LessThanOrEqualTo(def.MaxMinions), $"{bossId} had {out_} {minionId}s out on turn {turn}.");
                }
            }
        }

        [Test]
        public void LordBlobertStopsSummoningWhenHisCourtIsFull()
        {
            // REL-40's own regression: giving him a cap without gating his declare left him marking tiles for a summon
            // the clamp had already cancelled - the REL-21 defect, on the last boss.
            var run = Revealed(Run(Catalog.RunFloorCount, 5UL, ".....", ".....", ".HB..", ".....", "....X"));
            var blobert = Enemy(run, "lord_blobert");
            var def = Catalog.Enemy("lord_blobert");
            for (int turn = 0; turn < 16 && run.Status == RunStatus.InProgress; turn++)
            {
                run.Hero.Hp = run.Hero.MaxHp;
                var marked = Threats.Compute(run, Catalog).FindAll(t => t.Kind == ThreatKind.Summon);
                var before = new HashSet<int>(run.Floor.Enemies.ConvertAll(e => e.Id));
                DoOk(run, PlayerCommand.Wait());
                int arrived = run.Floor.Enemies.FindAll(e => !before.Contains(e.Id)).Count;
                Assert.That(arrived, Is.EqualTo(marked.Count), $"turn {turn}: {marked.Count} tiles marked, {arrived} arrived.");
                Assert.That(run.Floor.Enemies.FindAll(e => e.DefId == def.SummonId).Count, Is.LessThanOrEqualTo(def.MaxMinions));
                // And with the court full he does something else with the turn rather than declaring a summon that
                // cannot place anything.
                if (run.Floor.Enemies.FindAll(e => e.DefId == def.SummonId).Count >= def.MaxMinions)
                    Assert.That(blobert.Intent.Kind, Is.Not.EqualTo(IntentKind.Summon), $"turn {turn}: a summon with no room.");
            }
        }

        [Test]
        public void EverySummonedMinionStandsOnATileTheBoardMarked()
        {
            var run = Revealed(Run(3 * ContentCatalog.BossEvery, 5UL, ".....", ".....", ".H...", ".....", "T...X"));
            for (int turn = 0; turn < 12 && run.Status == RunStatus.InProgress; turn++)
            {
                var marked = Threats.Compute(run, Catalog).FindAll(t => t.Kind == ThreatKind.Summon).ConvertAll(t => t.Cell);
                var before = new HashSet<int>(run.Floor.Enemies.ConvertAll(e => e.Id));
                run.Hero.Hp = run.Hero.MaxHp;
                DoOk(run, PlayerCommand.Wait());
                foreach (var enemy in run.Floor.Enemies)
                    if (!before.Contains(enemy.Id))
                        Assert.That(marked, Does.Contain(enemy.Pos), $"turn {turn}: a {enemy.DefId} appeared on an unmarked tile.");
            }
        }
    }
}
