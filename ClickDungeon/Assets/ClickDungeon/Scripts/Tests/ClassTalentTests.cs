using System.Collections.Generic;
using System.Linq;
using ClickDungeon.Application;
using ClickDungeon.Content;
using ClickDungeon.Domain;
using ClickDungeon.Simulation;
using NUnit.Framework;
using static ClickDungeon.Tests.Scenario;

namespace ClickDungeon.Tests
{
    /// <summary>D-037: each class has its own talent tree; its tiers, prerequisites and capstone choice; and what each does.</summary>
    public class ClassTalentTests
    {
        /// <summary>A scenario run with these talents' perks, as a profile with them learned would start it.</summary>
        static RunState With(RunState run, params (TalentEffect effect, int value)[] perks)
        {
            foreach (var (effect, value) in perks) run.Perks[effect.ToString()] = value;
            return run;
        }

        [Test]
        public void EachPlayableClassHasItsOwnWellFormedTree()
        {
            foreach (var heroClass in Catalog.HeroClasses.Values)
            {
                var tree = Catalog.TalentsOf(heroClass.Id);
                Assert.That(tree.Count, Is.GreaterThanOrEqualTo(12), heroClass.Id);
                Assert.That(heroClass.Branches.Length, Is.EqualTo(3), heroClass.Id);
                foreach (var branch in heroClass.Branches)
                {
                    var path = tree.Where(t => t.BranchId == branch.Id).OrderBy(t => t.Tier).ToList();
                    Assert.That(path.Select(t => t.Tier), Is.EqualTo(new[] { 1, 2, 3, 4 }), branch.Id);
                    for (int i = 1; i < path.Count; i++) Assert.That(path[i].Requires, Is.EqualTo(path[i - 1].Id), path[i].Id);
                }
                foreach (var talent in tree)
                {
                    Assert.That(talent.Name, Is.Not.Empty);
                    Assert.That(talent.Summary, Is.Not.Empty);
                    Assert.That(talent.PerRank, Is.Not.Empty);
                }
            }
            var knight = Catalog.TalentsOf("knight").Select(t => t.Effect);
            var paladin = Catalog.TalentsOf("paladin").Select(t => t.Effect);
            Assert.That(knight.Intersect(paladin), Is.EquivalentTo(new[] { TalentEffect.MaxHearts }), "Only the one basic stat is shared.");
            Assert.That(Catalog.Talents.Select(t => t.Id).Distinct().Count(), Is.EqualTo(Catalog.Talents.Count));
        }

        [Test]
        public void TiersOpenWithPointsSpentAndTheTalentBelow()
        {
            var profile = new ProfileState { Xp = Progression.XpForLevel(12) };
            Assert.That(Progression.Locked(profile, Catalog, "k_cleave"), Does.Contain("points"));
            Assert.That(Progression.TryLearn(profile, Catalog, "k_sturdy"), Is.True);
            Assert.That(Progression.TryLearn(profile, Catalog, "k_sturdy"), Is.True);
            Assert.That(Progression.Locked(profile, Catalog, "k_cleave"), Does.Contain("Opening Strike"), "Two points spent, but not in its path.");
            Assert.That(Progression.TryLearn(profile, Catalog, "k_opening_strike"), Is.True);
            Assert.That(Progression.TryLearn(profile, Catalog, "k_cleave"), Is.True);
            Assert.That(Progression.TryLearn(profile, Catalog, "k_cleave"), Is.False, "One rank.");
        }

        [Test]
        public void AClassTakesOnlyOneCapstone()
        {
            var profile = new ProfileState { Xp = Progression.XpForLevel(30) };
            foreach (var id in new[] { "k_opening_strike", "k_sturdy", "k_light_step", "k_cleave", "k_shield_wall",
                         "k_executioner", "k_riposte", "k_relentless" })
                Assert.That(Progression.TryLearn(profile, Catalog, id), Is.True, id);
            Assert.That(Progression.Locked(profile, Catalog, "k_bastion"), Does.Contain("Only one capstone"));
            Progression.Reset(profile, Catalog, "knight");
            Assert.That(Progression.PointsSpent(profile, Catalog, "knight"), Is.Zero, "Reset refunds the class's points.");
        }

        [Test]
        public void EachClassSpendsTheLevelsPointsOnItsOwnTree()
        {
            var profile = new ProfileState { Xp = Progression.XpForLevel(3) };
            Assert.That(Progression.TryLearn(profile, Catalog, "k_sturdy"), Is.True);
            Assert.That(Progression.TryLearn(profile, Catalog, "k_sturdy"), Is.True);
            Assert.That(Progression.PointsFree(profile, Catalog, "knight"), Is.Zero);
            Assert.That(Progression.PointsFree(profile, Catalog, "paladin"), Is.EqualTo(2), "The Paladin has its own points.");
            Progression.Reset(profile, Catalog, "paladin");
            Assert.That(Progression.Rank(profile, "k_sturdy"), Is.EqualTo(2), "Resetting one class leaves the other.");
        }

        [Test]
        public void OldTalentsFromBeforeTheTreesAreRefunded()
        {
            var profile = new ProfileState { Xp = Progression.XpForLevel(4) };
            profile.Talents["tough"] = 3;
            Assert.That(Progression.PointsFree(profile, Catalog, "knight"), Is.EqualTo(3));
        }

        [Test]
        public void OnlyThePlayingClassesTalentsShapeTheRun()
        {
            var profile = new ProfileState { Xp = Progression.XpForLevel(5) };
            Progression.TryLearn(profile, Catalog, "k_sturdy");
            Progression.TryLearn(profile, Catalog, "p_judgement");
            var knight = RunFactory.NewRun(3UL, Catalog, new List<GameEvent>());
            Progression.Apply(profile, knight, Catalog);
            Assert.That(knight.Hero.MaxHp, Is.EqualTo(Catalog.HeroClass("knight").MaxHp + 1));
            Assert.That(knight.Perk(TalentEffect.Judgement), Is.Zero);
            var paladin = RunFactory.NewRun(3UL, Catalog, new List<GameEvent>(), "dawnward");
            Progression.Apply(profile, paladin, Catalog);
            Assert.That(paladin.Hero.MaxHp, Is.EqualTo(Catalog.HeroClass("paladin").MaxHp));
            Assert.That(paladin.Perk(TalentEffect.Judgement), Is.EqualTo(1));
        }

        // ------------------------------------------------------------------ Knight

        [Test]
        public void OpeningStrikeAndExecutionerHitHarderAtEachEndOfAFight()
        {
            var run = With(Run(".....", ".....", ".HC..", ".....", "....."), (TalentEffect.OpeningStrike, 2), (TalentEffect.Executioner, 1));
            var slime = EnemyAi.Spawn(run.Floor, Catalog.Enemy("crowned_slime"), P(2, 3), awake: true);
            int baseDamage = run.Hero.SlashDamage;
            Assert.That(Talents.SlashDamage(run, slime, Catalog), Is.EqualTo(baseDamage + 2), "Full health.");
            slime.Hp = 2;
            Assert.That(Talents.SlashDamage(run, slime, Catalog), Is.EqualTo(baseDamage + 1), "Nearly dead.");
            slime.Hp = 4;
            Assert.That(Talents.SlashDamage(run, slime, Catalog), Is.EqualTo(baseDamage));
        }

        [Test]
        public void CleaveHitsTheOtherEnemiesBesideYou()
        {
            var run = With(Run(".....", ".....", ".HG..", ".G...", "....."), (TalentEffect.Cleave, 1));
            var first = run.Floor.Enemies[0];
            var second = run.Floor.Enemies[1];
            first.Awake = second.Awake = true;
            DoOk(run, PlayerCommand.Slash(first.Pos));
            Assert.That(second.Hp, Is.EqualTo(second.MaxHp - 1));
        }

        [Test]
        public void RelentlessRestoresOnAKillAndRiposteAnswersABlock()
        {
            var run = With(Run(".....", ".....", ".HG..", ".....", "....."), (TalentEffect.Relentless, 1));
            run.Hero.Hp = 3;
            run.Hero.Mana = 0;
            var goblin = Enemy(run, "goblin");
            goblin.Hp = 1;
            DoOk(run, PlayerCommand.Slash(goblin.Pos));
            Assert.That(run.Hero.Hp, Is.EqualTo(4));
            Assert.That(run.Hero.Mana, Is.EqualTo(2 + Mana.PerTurn));

            var block = With(Run(".....", "..G..", "..H..", ".....", "....."), (TalentEffect.Riposte, 2));
            var attacker = Enemy(block, "goblin");
            for (int i = 0; i < 4 && attacker.Intent.Kind != IntentKind.Attack; i++) DoOk(block, PlayerCommand.Wait());
            Assert.That(attacker.Intent.Kind, Is.EqualTo(IntentKind.Attack));
            block.Hero.Hp = block.Hero.MaxHp;
            DoOk(block, PlayerCommand.Shield());
            Assert.That(attacker.Hp, Is.EqualTo(attacker.MaxHp - 2), "Blocked, and answered.");
        }

        /// <summary>
        /// D-052: Riposte and Holy Bulwark are written with the same promise — "a blocked attack" — so they answer the
        /// same blows. A fire imp's lane shot and Lord Blobert's slam are blocked attacks like any other.
        /// </summary>
        [Test]
        public void RiposteAnswersEveryBlockItIsPromisedFor()
        {
            var fire = With(Run(".....", ".....", "I.H..", ".....", "....."), (TalentEffect.Riposte, 1));
            var imp = Enemy(fire, "fire_imp");
            for (int i = 0; i < 6 && imp.Intent.Kind != IntentKind.Fire; i++) DoOk(fire, PlayerCommand.Wait());
            Assert.That(imp.Intent.Kind, Is.EqualTo(IntentKind.Fire), "Test setup: the imp is about to shoot down the lane.");
            fire.Hero.Hp = fire.Hero.MaxHp;
            DoOk(fire, PlayerCommand.Shield());
            Assert.That(imp.Hp, Is.EqualTo(imp.MaxHp - 1), "A blocked fireball is answered like a blocked sword.");
        }

        [Test]
        public void ShieldWallBastionAndTreasureSense()
        {
            var run = With(Run(".....", ".....", ".HC..", ".....", "....."), (TalentEffect.ShieldCostCut, 1), (TalentEffect.Bastion, 1), (TalentEffect.ChestTapCut, 1));
            var knight = Catalog.HeroClass("knight");
            Assert.That(Mana.ShieldCost(run, knight), Is.EqualTo(knight.ShieldCost - 1));
            run.Hero.Hp = 5;
            DoOk(run, PlayerCommand.Shield());
            Assert.That(run.Hero.Hp, Is.EqualTo(6), "Bastion mends.");
            run.Floor[P(2, 2)].Quality = ChestQuality.Epic;
            Assert.That(Chests.TapsToOpen(run, run.Floor[P(2, 2)]), Is.EqualTo(Chests.TapsToOpen(ChestQuality.Epic) - 1));
            run.Floor[P(2, 2)].Quality = ChestQuality.Common;
            Assert.That(Chests.TapsToOpen(run, run.Floor[P(2, 2)]), Is.GreaterThanOrEqualTo(1));
        }

        [Test]
        public void SecondWindHealsMoreOnEveryNewFloor()
        {
            var plain = Run(".....", ".....", ".HxK.", ".....", ".....");
            var winded = With(Run(".....", ".....", ".HxK.", ".....", "....."), (TalentEffect.SecondWind, 3));
            foreach (var run in new[] { plain, winded })
            {
                // Exactly half, so the stairs' mercy (D-071) adds nothing and this measures Second Wind alone.
                run.Hero.Hp = run.Hero.MaxHp / 2;
                DoOk(run, PlayerCommand.Move(P(3, 2)));
                DoOk(run, PlayerCommand.Move(P(2, 2)));
            }
            Assert.That(winded.Hero.Hp, Is.EqualTo(System.Math.Min(winded.Hero.MaxHp, plain.Hero.Hp + 3)));
        }

        // ------------------------------------------------------------------ Paladin

        [Test]
        public void JudgementHitsAnEnemyTheShieldJustStaggered()
        {
            // REL-23: Staggered is set in the enemy phase and cleared in the same turn's declare step, so the flag a slash
            // reads on the player's next turn is always false. What lasts is the Recover intent the board calls a free hit.
            int SlashAStaggeredGoblin(params (TalentEffect effect, int value)[] perks)
            {
                var run = With(Run(".....", ".....", ".HG..", ".....", "....."), perks);
                var goblin = Enemy(run, "goblin");
                // Plenty of hearts, so the blow can be read off them instead of killing it.
                goblin.MaxHp = goblin.Hp = 20;
                Assert.That(goblin.Intent.Kind, Is.EqualTo(IntentKind.Attack), "Test setup: the goblin is about to swing.");

                DoOk(run, PlayerCommand.Shield());
                Assert.That(goblin.Staggered, Is.False, "The stagger flag never outlives the turn that set it.");
                Assert.That(goblin.Intent.Kind, Is.EqualTo(IntentKind.Recover), "A blocked blow staggers: it does nothing next turn.");

                int before = goblin.Hp;
                DoOk(run, PlayerCommand.Slash(goblin.Pos));
                return before - goblin.Hp;
            }

            Assert.That(SlashAStaggeredGoblin((TalentEffect.Judgement, 2)),
                Is.EqualTo(SlashAStaggeredGoblin() + 2), "Judgement has to land on the enemy the shield just staggered.");
        }

        [Test]
        public void DawnstrikePunishesTheBoss()
        {
            var boss = With(Run(Catalog.RunFloorCount, 1234UL, ".....", ".....", ".HB..", ".....", "....X"), (TalentEffect.Dawnstrike, 1));
            Assert.That(Talents.SlashDamage(boss, Enemy(boss, "lord_blobert"), Catalog), Is.EqualTo(boss.Hero.SlashDamage + 1));
        }

        [Test]
        public void ConsecrateBurnsNeighboursAndWrathStaggersThemOnAKill()
        {
            var run = With(Run(".....", ".....", ".HG..", ".G...", "....."), (TalentEffect.Consecrate, 1));
            foreach (var e in run.Floor.Enemies) e.Awake = true;
            DoOk(run, PlayerCommand.Shield());
            Assert.That(run.Floor.Enemies.All(e => e.Hp == e.MaxHp - 1), Is.True);

            var wrath = With(Run(".....", ".....", ".HG..", ".G...", "....."), (TalentEffect.WrathOfDawn, 1));
            foreach (var e in wrath.Floor.Enemies) e.Awake = true;
            var target = wrath.Floor.Enemies[0];
            var other = wrath.Floor.Enemies[1];
            target.Hp = 1;
            var result = DoOk(wrath, PlayerCommand.Slash(target.Pos));
            Assert.That(result.Events.Any(e => e.Kind == GameEventKind.EnemyStaggered && e.ActorId == other.Id), Is.True);
        }

        [Test]
        public void HolyBulwarkUnyieldingAndDivineShield()
        {
            var run = With(Run(".....", ".....", "..H..", ".....", "....."), (TalentEffect.HolyBulwark, 2), (TalentEffect.Unyielding, 1), (TalentEffect.DivineShield, 3));
            var hero = run.Hero;
            var events = new List<GameEvent>();
            hero.Guard = true;
            hero.Mana = 0;
            Combat.DamageHero(run, 3, "goblin", events);
            Assert.That(hero.Mana, Is.EqualTo(2), "A block restores mana.");
            hero.Guard = false;
            hero.Hp = hero.MaxHp / 2;
            int before = hero.Hp;
            Combat.DamageHero(run, 3, "goblin", events);
            Assert.That(hero.Hp, Is.EqualTo(before - 2), "Softened at half hearts.");
            Combat.DamageHero(run, 99, "slam", events);
            Assert.That(hero.Hp, Is.EqualTo(4), "The ward holds: 1 heart plus 3.");
            Combat.DamageHero(run, 99, "slam", events);
            Assert.That(hero.Hp, Is.Zero, "Once per floor.");
        }

        [Test]
        public void PrayerSanctifiedAndGuidingLight()
        {
            var run = With(Run(".....", ".....", "..H..", ".....", "....."), (TalentEffect.Prayer, 1), (TalentEffect.Sanctified, 1));
            run.Hero.Mana = 0;
            DoOk(run, PlayerCommand.Wait());
            Assert.That(run.Hero.Mana, Is.EqualTo(1 + Mana.PerTurn));
            run.Hero.Hp = 1;
            run.Hero.Potions = 1;
            DoOk(run, PlayerCommand.Potion());
            Assert.That(run.Hero.Mana, Is.EqualTo(run.Hero.MaxMana), "A potion refills mana.");

            var profile = new ProfileState { Xp = Progression.XpForLevel(20) };
            foreach (var id in new[] { "p_blessed_draught", "p_plated", "p_prayer", "p_plated", "p_guiding_light" })
                Assert.That(Progression.TryLearn(profile, Catalog, id), Is.True, id);
            var session = new GameSession(Catalog, null);
            foreach (var kv in profile.Talents) session.Profile.Talents[kv.Key] = kv.Value;
            session.Profile.Xp = profile.Xp;
            session.StartNewRun(21UL, Difficulty.Medium, MovementMode.Free, "dawnward");
            var floor = session.Run.Floor;
            var key = Board.AllCells.FirstOrDefault(p => floor[p].Content == ContentKind.Key);
            Assert.That(floor[key].Knowledge, Is.EqualTo(Knowledge.Revealed), "The key starts uncovered.");
        }
    }
}
