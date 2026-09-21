using ClickDungeon.Application;
using ClickDungeon.Content;
using ClickDungeon.Domain;
using ClickDungeon.Simulation;
using NUnit.Framework;
using static ClickDungeon.Tests.Scenario;

namespace ClickDungeon.Tests
{
    /// <summary>
    /// D-058: the first expansion monsters. Each adds one rule, and every rule is telegraphed the turn before it lands —
    /// these tests pin both halves: what the monster does, and that the warning shown matches it.
    /// </summary>
    public class ExpansionMonsterTests
    {
        static RunState Revealed(RunState run)
        {
            foreach (var p in Board.AllCells) run.Floor[p].Knowledge = Knowledge.Revealed;
            return run;
        }

        // ------------------------------------------------------------------ Skeleton Warrior

        [Test]
        public void ASkeletonsFirstFallLeavesBonesAndCountsNoKill()
        {
            var run = Revealed(Run(".....", ".....", ".HZ..", ".....", "....."));
            var skeleton = Enemy(run, "skeleton");
            int xp = run.XpEarned, slain = run.MonstersSlain;

            var result = DoOk(run, PlayerCommand.Slash(skeleton.Pos));

            Assert.That(Has(result, GameEventKind.EnemyCollapsed), Is.True);
            Assert.That(Has(result, GameEventKind.EnemyDied), Is.False, "The first fall is not a death.");
            Assert.That(run.Floor.Enemies, Does.Contain(skeleton), "The bones stay where it fell.");
            Assert.That(skeleton.Mode, Is.EqualTo(EnemyMode.Bones));
            Assert.That(skeleton.Hp, Is.EqualTo(1), "One more hit breaks it.");
            Assert.That(skeleton.Intent.Kind, Is.EqualTo(IntentKind.Reassemble));
            Assert.That(run.XpEarned, Is.EqualTo(xp), "The kill - and its experience - waits until the bones are broken.");
            Assert.That(run.MonstersSlain, Is.EqualTo(slain));
            Assert.That(run.Hero.Hp, Is.EqualTo(run.Hero.MaxHp), "It fell before its blow landed, and bones do not strike.");
        }

        [Test]
        public void BreakingTheBonesEndsItForGood()
        {
            var run = Revealed(Run(".....", ".....", ".HZ..", ".....", "....."));
            var skeleton = Enemy(run, "skeleton");
            DoOk(run, PlayerCommand.Slash(skeleton.Pos));
            int xp = run.XpEarned;

            var result = DoOk(run, PlayerCommand.Slash(skeleton.Pos));

            Assert.That(Has(result, GameEventKind.EnemyDied), Is.True);
            Assert.That(run.Floor.Enemies.Contains(skeleton), Is.False, "Gone for good.");
            Assert.That(run.XpEarned, Is.EqualTo(xp + Catalog.Xp.PerMonster));
        }

        [Test]
        public void LeftAloneTheBonesStandUpAtHalfHeartsAndTheNextFallIsFinal()
        {
            var run = Revealed(Run(".....", ".....", ".HZ..", ".....", "....."));
            var skeleton = Enemy(run, "skeleton");
            DoOk(run, PlayerCommand.Slash(skeleton.Pos));

            // Two turns to break it. Waiting both of them out lets it stand.
            DoOk(run, PlayerCommand.Wait());
            Assert.That(skeleton.Mode, Is.EqualTo(EnemyMode.Bones), "Still bones after one turn.");
            var stood = DoOk(run, PlayerCommand.Wait());

            Assert.That(Has(stood, GameEventKind.EnemyReassembled), Is.True);
            Assert.That(skeleton.Mode, Is.EqualTo(EnemyMode.Normal));
            Assert.That(skeleton.Hp, Is.EqualTo((skeleton.MaxHp + 1) / 2), "It stands up at half its hearts, rounded up.");
            Assert.That(skeleton.Rallied, Is.True);

            var second = DoOk(run, PlayerCommand.Slash(skeleton.Pos));
            Assert.That(Has(second, GameEventKind.EnemyCollapsed), Is.False, "It only rallies once.");
            Assert.That(Has(second, GameEventKind.EnemyDied), Is.True);
            Assert.That(run.Floor.Enemies.Contains(skeleton), Is.False, "Gone for good.");
        }

        [Test]
        public void TheBonesSurviveASave()
        {
            var run = Revealed(Run(".....", ".....", ".HZ..", ".....", "....."));
            DoOk(run, PlayerCommand.Slash(Enemy(run, "skeleton").Pos));

            var loaded = SaveSerializer.FromJson(SaveSerializer.ToJson(run));
            var bones = Enemy(loaded, "skeleton");

            Assert.That(bones.Mode, Is.EqualTo(EnemyMode.Bones));
            Assert.That(bones.Rallied, Is.True, "A reload must not grant a second rally.");
            Assert.That(bones.ModeTurns, Is.EqualTo(Enemy(run, "skeleton").ModeTurns));
        }

        // ------------------------------------------------------------------ Armored Boar

        [Test]
        public void TheBoarChargesAClearLineAndGoresTheHeroOnIt()
        {
            var run = Revealed(Run(".....", ".....", "R...H", ".....", "....."));
            var boar = Enemy(run, "armored_boar");
            Assert.That(boar.Intent.Kind, Is.EqualTo(IntentKind.Charge), "A clear line to the hero: it declares a charge.");
            Assert.That(boar.Intent.Dir, Is.EqualTo(Direction.Right));

            // The warning covers the whole path, the hero's tile included.
            var threats = Threats.Compute(run, Catalog);
            Assert.That(threats.Exists(t => t.Kind == ThreatKind.Charge && t.Cell == run.Hero.Pos), Is.True);
            int gore = Threats.DamageAt(threats, run.Hero.Pos);

            int hp = run.Hero.Hp;
            var result = DoOk(run, PlayerCommand.Wait());

            Assert.That(Has(result, GameEventKind.EnemyCharged), Is.True);
            Assert.That(boar.Pos, Is.EqualTo(P(3, 2)), "It stops on the tile in front of the hero.");
            Assert.That(run.Hero.Hp, Is.EqualTo(hp - gore), "The blow is the one the telegraph promised.");
        }

        [Test]
        public void SteppingOffTheLineMakesTheChargeMissAndLeavesTheBoarWinded()
        {
            var run = Revealed(Run(".....", ".....", "R...H", ".....", "....."));
            var boar = Enemy(run, "armored_boar");
            int hp = run.Hero.Hp;

            DoOk(run, PlayerCommand.Move(P(4, 1)));

            Assert.That(run.Hero.Hp, Is.EqualTo(hp), "Off the line, off the hook.");
            Assert.That(boar.Pos, Is.EqualTo(P(4, 2)), "It runs the whole line when nothing stops it.");
            Assert.That(boar.Intent.Kind, Is.EqualTo(IntentKind.Rest), "Winded: the turn to hit back.");
        }

        [Test]
        public void AWallBetweenThemMeansNoCharge()
        {
            var run = Revealed(Run(".....", ".....", "R.#.H", ".....", "....."));
            Assert.That(Enemy(run, "armored_boar").Intent.Kind, Is.Not.EqualTo(IntentKind.Charge));
            Assert.That(Threats.Compute(run, Catalog).Exists(t => t.Kind == ThreatKind.Charge), Is.False);
        }

        // ------------------------------------------------------------------ Goblin Bomber

        [Test]
        public void TheBomberThrowsWhereTheHeroStandsAndTheBombLandsLit()
        {
            var run = Revealed(Run("M....", ".....", "..H..", ".....", "....."));
            var bomber = Enemy(run, "goblin_bomber");
            var target = run.Hero.Pos;
            Assert.That(bomber.Intent.Kind, Is.EqualTo(IntentKind.Throw));
            Assert.That(bomber.Intent.Target, Is.EqualTo(target));
            Assert.That(Threats.Compute(run, Catalog).Exists(t => t.Kind == ThreatKind.Throw && t.Cell == target), Is.True,
                "The landing tile is marked the turn before.");

            var landed = DoOk(run, PlayerCommand.Wait());

            Assert.That(Has(landed, GameEventKind.BombThrown), Is.True);
            Assert.That(run.Floor[target].BombArmed, Is.True, "It lands lit.");
            Assert.That(run.Hero.Hp, Is.EqualTo(run.Hero.MaxHp), "Landing does no harm; the blast is a turn away.");
            Assert.That(bomber.Intent.Kind, Is.EqualTo(IntentKind.Rest), "It takes a turn to light the next one.");
        }

        [Test]
        public void GettingClearOfAThrownBombTakesNoDamage()
        {
            var run = Revealed(Run("M....", ".....", "..H..", ".....", "....."));
            var target = run.Hero.Pos;
            DoOk(run, PlayerCommand.Wait());

            var blast = DoOk(run, PlayerCommand.Move(P(4, 0)));

            Assert.That(Has(blast, GameEventKind.BombExploded), Is.True);
            Assert.That(run.Hero.Hp, Is.EqualTo(run.Hero.MaxHp));
            Assert.That(run.Floor[target].Hazard, Is.EqualTo(HazardKind.None), "A thrown bomb leaves no trap behind.");
        }

        [Test]
        public void StandingOnAThrownBombTakesTheBlast()
        {
            var run = Revealed(Run("M....", ".....", "..H..", ".....", "....."));
            DoOk(run, PlayerCommand.Wait());
            int hp = run.Hero.Hp;

            var blast = DoOk(run, PlayerCommand.Wait());

            var exploded = blast.Events.Find(e => e.Kind == GameEventKind.BombExploded);
            Assert.That(exploded, Is.Not.Null);
            Assert.That(run.Hero.Hp, Is.EqualTo(hp - exploded.Amount));
        }

        [Test]
        public void ABomberNextToTheHeroBacksOff()
        {
            var run = Revealed(Run(".....", ".....", ".HM..", ".....", "....."));
            var bomber = Enemy(run, "goblin_bomber");
            Assert.That(bomber.Intent.Kind, Is.EqualTo(IntentKind.Move));
            int before = bomber.Pos.Chebyshev(run.Hero.Pos);

            DoOk(run, PlayerCommand.Wait());

            Assert.That(bomber.Pos.Chebyshev(run.Hero.Pos), Is.GreaterThan(before));
        }

        [Test]
        public void ABomberNeverAimsAtATileThatCannotTakeABomb()
        {
            // The hero stands on a potion: nowhere for a bomb to land, so it closes in instead of throwing.
            var run = Revealed(Run("M....", ".....", "..H..", ".....", "....."));
            run.Floor[run.Hero.Pos].Content = ContentKind.Potion;
            var bomber = Enemy(run, "goblin_bomber");
            EnemyAi.Declare(run, bomber, Catalog, new System.Collections.Generic.List<GameEvent>());
            Assert.That(bomber.Intent.Kind, Is.Not.EqualTo(IntentKind.Throw));
        }

        // ------------------------------------------------------------------ all three

        /// <summary>
        /// D-059: each new monster has a floor that introduces it before it turns up alongside the others - the skeleton
        /// on the Bone Crypt, the bomber in the Ember Vaults, the boar in the Boar Warrens - and Blobert keeps the last floor.
        /// </summary>
        [Test]
        public void EachNewMonsterHasAFloorThatIntroducesIt()
        {
            int FirstFloorWith(string id)
            {
                for (int f = 1; f <= Catalog.RunFloorCount; f++)
                    if (System.Array.IndexOf(Catalog.ProfileFor(f).EnemyPool ?? new string[0], id) >= 0) return f;
                return -1;
            }
            Assert.That(Catalog.RunFloorCount, Is.EqualTo(7));
            Assert.That(Catalog.ProfileFor(Catalog.RunFloorCount).IsBoss, Is.True, "Blobert's Court is the last floor.");
            Assert.That(FirstFloorWith("skeleton"), Is.EqualTo(3));
            Assert.That(FirstFloorWith("goblin_bomber"), Is.EqualTo(4));
            Assert.That(FirstFloorWith("armored_boar"), Is.EqualTo(6));
            Assert.That(Catalog.ProfileFor(3).Name, Is.EqualTo("The Bone Crypt"));
            Assert.That(Catalog.ProfileFor(6).Name, Is.EqualTo("The Boar Warrens"));
        }
    }
}
