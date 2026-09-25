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
    /// <summary>
    /// D-063: the six new classes. Each has one rule of its own, a perk every run of the class starts with; these pin each
    /// rule and each talent the new trees brought, and the roster they fill.
    /// </summary>
    public class HeroClassTests
    {
        static RunState With(RunState run, TalentEffect effect, int value = 1)
        {
            run.Perks[effect.ToString()] = run.Perk(effect) + value;
            return run;
        }

        // ------------------------------------------------------------------ the roster

        [Test]
        public void EveryHeroOnTheSheetsIsPlayableWithIronheartFirstAndTheMascotLast()
        {
            Assert.That(Catalog.HeroIdentities.Keys, Is.EqualTo(new[]
            {
                "ironheart", "dawnward", "shadowcut", "emberwisp", "windsong", "lightbringer", "rageclaw", "gearspark", "sir_clickington",
            }));
            Assert.That(Catalog.ComingSoon, Is.Empty, "Every hero the roster promised is here.");
            var classes = new Dictionary<string, string>
            {
                ["shadowcut"] = "rogue", ["emberwisp"] = "wizard", ["windsong"] = "ranger",
                ["lightbringer"] = "cleric", ["rageclaw"] = "berserker", ["gearspark"] = "engineer",
            };
            foreach (var pair in classes)
            {
                Assert.That(Catalog.HeroIdentity(pair.Key).ClassId, Is.EqualTo(pair.Value), pair.Key);
                var heroClass = Catalog.HeroClass(pair.Value);
                Assert.That(heroClass.Traits, Is.Not.Empty, $"{pair.Value} has a rule of its own.");
                Assert.That(heroClass.TraitName, Is.Not.Empty, pair.Value);
                Assert.That(heroClass.TraitText, Is.Not.Empty, pair.Value);
            }
        }

        [Test]
        public void ANewRunStartsWithItsClassRuleAndAKnightStartsWithNone()
        {
            foreach (var heroClass in Catalog.HeroClasses.Values)
            {
                string heroId = Catalog.HeroIdentities.Values.First(h => h.ClassId == heroClass.Id).Id;
                var run = RunFactory.NewRun(7UL, Catalog, new List<GameEvent>(), heroId);
                if (heroClass.Traits == null)
                {
                    Assert.That(run.Perks, Is.Empty, heroClass.Id);
                    continue;
                }
                foreach (var trait in heroClass.Traits)
                    Assert.That(run.Perk(trait.Key), Is.EqualTo(trait.Value), $"{heroClass.Id}: {trait.Key}");
            }
        }

        [Test]
        public void ATalentThatSharpensAClassRuleAddsToIt()
        {
            var profile = new ProfileState { Xp = Progression.XpForLevel(12) };
            Assert.That(Progression.TryLearn(profile, Catalog, "ro_cruel_edge"), Is.True);
            Assert.That(Progression.TryLearn(profile, Catalog, "w_deep_well"), Is.True);
            var rogue = RunFactory.NewRun(7UL, Catalog, new List<GameEvent>(), "shadowcut");
            Progression.Apply(profile, rogue, Catalog);
            Assert.That(rogue.Perk(TalentEffect.Ambush), Is.EqualTo(3), "The class's 2, and Cruel Edge's 1.");
            var wizard = RunFactory.NewRun(7UL, Catalog, new List<GameEvent>(), "emberwisp");
            Progression.Apply(profile, wizard, Catalog);
            Assert.That(wizard.Hero.MaxMana, Is.EqualTo(Catalog.HeroClass("wizard").MaxMana + 1), "Deep Well is a starting number.");
            Assert.That(wizard.Hero.Mana, Is.EqualTo(wizard.Hero.MaxMana));
        }

        // ------------------------------------------------------------------ Rogue: Ambush

        [Test]
        public void TheRogueCutsDeeperWhereTheMonsterIsNotAiming()
        {
            var run = As(Run(".....", ".....", ".HG..", ".....", "....X"), "shadowcut");
            var goblin = Enemy(run, "goblin");
            Assert.That(goblin.Intent.Target, Is.EqualTo(run.Hero.Pos), "Test setup: it is swinging at her.");
            Assert.That(Talents.SlashDamage(run, goblin, Catalog), Is.EqualTo(2), "No opening while it aims at her.");

            goblin.Intent = Intent.Move();
            Assert.That(Talents.SlashDamage(run, goblin, Catalog), Is.EqualTo(4), "Looking away: +2.");
            var result = DoOk(run, PlayerCommand.Slash(goblin.Pos));
            Assert.That(result.Events.Exists(e => e.Kind == GameEventKind.HeroSlashed && e.Source == "ambush"), Is.True);
            Assert.That(goblin.Hp, Is.EqualTo(0));
        }

        [Test]
        public void OnlyTheRogueAmbushes()
        {
            var run = Run(".....", ".....", ".HG..", ".....", "....X");
            var goblin = Enemy(run, "goblin");
            goblin.Intent = Intent.Move();
            Assert.That(Talents.SlashDamage(run, goblin, Catalog), Is.EqualTo(run.Hero.SlashDamage));
        }

        [Test]
        public void EviscerateStaggersWhatAnAmbushLeavesStanding()
        {
            var run = With(As(Run(".....", ".....", ".HS..", ".....", "....X"), "shadowcut"), TalentEffect.Eviscerate);
            var slime = Enemy(run, "crowned_slime");
            slime.Intent = Intent.Rest();
            var result = DoOk(run, PlayerCommand.Slash(slime.Pos));
            Assert.That(slime.Hp, Is.EqualTo(1));
            Assert.That(Has(result, GameEventKind.EnemyStaggered), Is.True);
            Assert.That(slime.Intent.Kind, Is.EqualTo(IntentKind.Recover), "It loses its next turn.");
        }

        [Test]
        public void SlipperyDodgesTheFirstHitOfAFloorOnly()
        {
            var run = With(As(Run(".....", ".....", ".HG..", ".....", "....X"), "shadowcut"), TalentEffect.Dodge);
            int hp = run.Hero.Hp;
            var first = DoOk(run, PlayerCommand.Wait());
            Assert.That(Has(first, GameEventKind.HeroDodged), Is.True);
            Assert.That(run.Hero.Hp, Is.EqualTo(hp));
            DoOk(run, PlayerCommand.Wait());
            Assert.That(run.Hero.Hp, Is.LessThan(hp), "The second one lands.");
        }

        [Test]
        public void PickpocketLootsEveryKill()
        {
            var run = With(As(Run(".....", ".....", ".HG..", ".....", "....X"), "shadowcut"), TalentEffect.Pickpocket, 5);
            var goblin = Enemy(run, "goblin");
            goblin.Intent = Intent.Move();
            int coins = run.CoinsFound;
            DoOk(run, PlayerCommand.Slash(goblin.Pos));
            Assert.That(run.CoinsFound - coins, Is.EqualTo(5));
        }

        // ------------------------------------------------------------------ Wizard: Firebolt

        [Test]
        public void TheWizardsBoltFliesDownAClearUncoveredLine()
        {
            var run = As(Run(".....", ".....", "H..S.", ".....", "....X"), "emberwisp");
            var slime = Enemy(run, "crowned_slime");
            Assert.That(Do(run, PlayerCommand.Slash(slime.Pos)).Accepted, Is.False, "Covered tiles carry no shot.");
            Revealed(run);
            slime.Intent = Intent.Rest();
            DoOk(run, PlayerCommand.Slash(slime.Pos));
            Assert.That(slime.Hp, Is.EqualTo(slime.MaxHp - run.Hero.SlashDamage));

            var far = Revealed(As(Run(".....", ".....", "H...G", ".....", "....X"), "emberwisp"));
            Assert.That(Do(far, PlayerCommand.Slash(Enemy(far, "goblin").Pos)).Accepted, Is.False, "Four tiles is past his reach.");
            var blocked = Revealed(As(Run(".....", ".....", "H#.G.", ".....", "....X"), "emberwisp"));
            Assert.That(Do(blocked, PlayerCommand.Slash(Enemy(blocked, "goblin").Pos)).Accepted, Is.False, "A wall stops it.");
            var diagonal = Revealed(As(Run(".....", "...G.", ".....", ".H...", "....X"), "emberwisp"));
            Assert.That(Do(diagonal, PlayerCommand.Slash(Enemy(diagonal, "goblin").Pos)).Accepted, Is.True, "Diagonals are lines too.");
        }

        [Test]
        public void AKnightStillSlashesOnlyWhatIsNextToHim()
        {
            var run = Revealed(Run(".....", ".....", "H.G..", ".....", "....X"));
            var result = Do(run, PlayerCommand.Slash(Enemy(run, "goblin").Pos));
            Assert.That(result.Accepted, Is.False);
            Assert.That(result.RejectReason, Does.Contain("neighbouring"));
        }

        [Test]
        public void TheBoltKnocksItsTargetBackOntoASeenFreeTileOnly()
        {
            var run = Revealed(As(Run(".....", ".....", "H.S..", ".....", "....X"), "emberwisp"));
            var slime = Enemy(run, "crowned_slime");
            slime.Intent = Intent.Rest();
            var result = DoOk(run, PlayerCommand.Slash(slime.Pos));
            Assert.That(Has(result, GameEventKind.EnemyKnockedBack), Is.True);
            Assert.That(slime.Pos, Is.EqualTo(P(3, 2)));

            var wall = Revealed(As(Run(".....", ".....", "H.S#.", ".....", "....X"), "emberwisp"));
            var pinned = Enemy(wall, "crowned_slime");
            pinned.Intent = Intent.Rest();
            DoOk(wall, PlayerCommand.Slash(pinned.Pos));
            Assert.That(pinned.Pos, Is.EqualTo(P(2, 2)), "A wall behind it holds it.");

            var covered = Revealed(As(Run(".....", ".....", "H.S..", ".....", "....X"), "emberwisp"));
            covered.Floor[P(3, 2)].Knowledge = Knowledge.Unseen;
            var unseen = Enemy(covered, "crowned_slime");
            unseen.Intent = Intent.Rest();
            DoOk(covered, PlayerCommand.Slash(unseen.Pos));
            Assert.That(unseen.Pos, Is.EqualTo(P(2, 2)), "Never onto a cover: the push must not tell what is under it.");
        }

        [Test]
        public void NoBossIsKnockedBack()
        {
            var run = Revealed(As(Run(".....", ".....", "H.N..", ".....", "....X"), "emberwisp"));
            var king = Enemy(run, "goblin_brute_king");
            king.Intent = Intent.Rest();
            var result = DoOk(run, PlayerCommand.Slash(king.Pos));
            Assert.That(Has(result, GameEventKind.EnemyKnockedBack), Is.False);
        }

        [Test]
        public void FireballScorchesTheTargetsNeighbours()
        {
            var run = Revealed(With(As(Run(".....", "..G..", "H.G..", ".....", "....X"), "emberwisp"), TalentEffect.Fireball));
            var target = run.Floor.EnemyAt(P(2, 2));
            var beside = run.Floor.EnemyAt(P(2, 3));
            target.Intent = Intent.Rest();
            beside.Intent = Intent.Rest();
            DoOk(run, PlayerCommand.Slash(target.Pos));
            Assert.That(beside.Hp, Is.EqualTo(beside.MaxHp - 1));
        }

        // ------------------------------------------------------------------ Ranger: Longshot

        [Test]
        public void TheRangerReachesFourAndHitsHarderFromAfar()
        {
            var run = Revealed(As(Run(".....", ".....", "H...G", ".....", "....X"), "windsong"));
            var goblin = Enemy(run, "goblin");
            Assert.That(Talents.SlashDamage(run, goblin, Catalog), Is.EqualTo(4), "Four tiles away: her 3, +1 for the distance.");
            DoOk(run, PlayerCommand.Slash(goblin.Pos));
            Assert.That(goblin.Hp, Is.EqualTo(0));

            var near = Revealed(As(Run(".....", ".....", "H.G..", ".....", "....X"), "windsong"));
            Assert.That(Talents.SlashDamage(near, Enemy(near, "goblin"), Catalog), Is.EqualTo(3), "Two tiles is not far.");
        }

        [Test]
        public void PiercingArrowHitsTheMonsterBehind()
        {
            var run = Revealed(With(As(Run(".....", ".....", "H.GG.", ".....", "....X"), "windsong"), TalentEffect.PiercingArrow));
            var behind = run.Floor.EnemyAt(P(3, 2));
            foreach (var e in run.Floor.Enemies) e.Intent = Intent.Rest();
            DoOk(run, PlayerCommand.Slash(P(2, 2)));
            Assert.That(behind.Hp, Is.EqualTo(behind.MaxHp - 1));
        }

        [Test]
        public void PinningShotStaggersFromAfarOnly()
        {
            var run = Revealed(With(As(Run(".....", ".....", "H..S.", ".....", "....X"), "windsong"), TalentEffect.PinningShot));
            var slime = Enemy(run, "crowned_slime");
            slime.Intent = Intent.Rest();
            var result = DoOk(run, PlayerCommand.Slash(slime.Pos));
            Assert.That(Has(result, GameEventKind.EnemyStaggered), Is.True);

            var near = Revealed(With(As(Run(".....", ".....", "H.S..", ".....", "....X"), "windsong"), TalentEffect.PinningShot));
            Enemy(near, "crowned_slime").Intent = Intent.Rest();
            Assert.That(Has(DoOk(near, PlayerCommand.Slash(Enemy(near, "crowned_slime").Pos)), GameEventKind.EnemyStaggered), Is.False);
        }

        [Test]
        public void HawkeyeUncoversTheExit()
        {
            var run = With(As(Run(".....", ".....", ".H...", ".....", "....X"), "windsong"), TalentEffect.Hawkeye);
            Assert.That(run.Floor[run.Floor.Exit].Knowledge, Is.Not.EqualTo(Knowledge.Revealed));
            RunFactory.RevealByTalents(run, new List<GameEvent>());
            Assert.That(run.Floor[run.Floor.Exit].Knowledge, Is.EqualTo(Knowledge.Revealed));
        }

        [Test]
        public void AShotAtASleepingMimicIsRefusedAsAShotAtAChest()
        {
            var mimic = Revealed(As(Run(".....", ".....", "H.Q..", ".....", "....X"), "windsong"));
            var chest = Revealed(As(Run(".....", ".....", "H.C..", ".....", "....X"), "windsong"));
            var a = Do(mimic, PlayerCommand.Slash(P(2, 2)));
            var b = Do(chest, PlayerCommand.Slash(P(2, 2)));
            Assert.That(a.Accepted, Is.EqualTo(b.Accepted));
            Assert.That(a.RejectReason, Is.EqualTo(b.RejectReason));
        }

        // ------------------------------------------------------------------ Cleric: Sanctuary

        [Test]
        public void EveryClassCardNamesTheNumbersItsRuleActuallyUses()
        {
            // D-071 changed two class rules, updated the rulebook, and left the cards the player actually reads saying
            // the old thing: the Berserker advertised a ramp he no longer had, and the Cleric promised a heal on every
            // block after it had been gated. The rulebook is not what anyone reads at the hero select screen (D-073).
            var berserker = Catalog.HeroClass("berserker");
            Assert.That(berserker.TraitText, Does.Contain(berserker.Traits[TalentEffect.Rage].ToString()),
                $"Rage is +1 per {berserker.Traits[TalentEffect.Rage]} hearts, and the card says: {berserker.TraitText}");

            var cleric = Catalog.HeroClass("cleric");
            Assert.That(cleric.TraitText.ToLowerInvariant(), Does.Contain("half"),
                $"Sanctuary only answers at half hearts or fewer, and the card says: {cleric.TraitText}");
        }

        [Test]
        public void TheClericsBlocksHeal()
        {
            // D-071: Sanctuary answers danger. Ungated, it paid on every block of the run and made the one class that
            // blocks at all the strongest class under pressure by a distance.
            var run = As(Run(".....", ".....", ".HG..", ".....", "....X"), "lightbringer");
            run.Hero.Hp = run.Hero.MaxHp / 2;
            var result = DoOk(run, PlayerCommand.Shield());
            Assert.That(Has(result, GameEventKind.HeroBlocked), Is.True);
            Assert.That(run.Hero.Hp, Is.EqualTo(run.Hero.MaxHp / 2 + 1), "Hurt, her block heals.");

            var healthy = As(Run(".....", ".....", ".HG..", ".....", "....X"), "lightbringer");
            healthy.Hero.Hp = healthy.Hero.MaxHp - 1;
            DoOk(healthy, PlayerCommand.Shield());
            Assert.That(healthy.Hero.Hp, Is.EqualTo(healthy.Hero.MaxHp - 1), "In no danger, her block only blocks.");

            var knight = Run(".....", ".....", ".HG..", ".....", "....X");
            knight.Hero.Hp = 5;
            DoOk(knight, PlayerCommand.Shield());
            Assert.That(knight.Hero.Hp, Is.EqualTo(5), "A Knight's block only blocks.");
        }

        // ------------------------------------------------------------------ Berserker: Rage

        [Test]
        public void TheBerserkerHitsHarderTheMoreHeBleeds()
        {
            var run = As(Run(".....", ".....", ".HG..", ".....", "....X"), "rageclaw");
            var goblin = Enemy(run, "goblin");
            int step = Catalog.HeroClass("berserker").Traits[TalentEffect.Rage];
            Assert.That(Talents.SlashDamage(run, goblin, Catalog), Is.EqualTo(2), "Unhurt, he swings for his 2.");
            run.Hero.Hp = run.Hero.MaxHp - step;
            Assert.That(Talents.SlashDamage(run, goblin, Catalog), Is.EqualTo(3), "One step of rage.");
            run.Hero.Hp = run.Hero.MaxHp - 2 * step;
            Assert.That(Talents.SlashDamage(run, goblin, Catalog), Is.EqualTo(4), "Two.");
            With(run, TalentEffect.Bloodlust);
            run.Hero.Hp = run.Hero.MaxHp / 2;
            Assert.That(Talents.SlashDamage(run, goblin, Catalog), Is.EqualTo(2 + (run.Hero.MaxHp - run.Hero.Hp) / step + 1), "Bloodlust at half.");
        }

        // ------------------------------------------------------------------ Engineer: Spark Drone

        [Test]
        public void TheDroneZapsANeighbourAfterEveryAction()
        {
            var run = As(Run(".....", ".....", ".HG..", ".....", "....X"), "gearspark");
            var goblin = Enemy(run, "goblin");
            var result = DoOk(run, PlayerCommand.Wait());
            Assert.That(Has(result, GameEventKind.DroneZapped), Is.True);
            Assert.That(goblin.Hp, Is.EqualTo(goblin.MaxHp - 1));

            var knight = Run(".....", ".....", ".HG..", ".....", "....X");
            DoOk(knight, PlayerCommand.Wait());
            Assert.That(Enemy(knight, "goblin").Hp, Is.EqualTo(Enemy(knight, "goblin").MaxHp));
        }

        [Test]
        public void TheDroneRestsOnATurnYouSlash()
        {
            var run = As(Run(".....", ".G...", ".HG..", ".....", "....X"), "gearspark");
            var slashed = run.Floor.EnemyAt(P(2, 2));
            var other = run.Floor.EnemyAt(P(1, 3));
            var result = DoOk(run, PlayerCommand.Slash(slashed.Pos));
            Assert.That(Has(result, GameEventKind.DroneZapped), Is.False, "It covers the other turns, not the slash.");
            Assert.That(other.Hp, Is.EqualTo(other.MaxHp));
        }

        [Test]
        public void TeslaCoilZapsEveryoneInReachAndLongRangeCoilReachesTwo()
        {
            var run = With(As(Run(".....", ".G...", ".HG..", ".....", "....X"), "gearspark"), TalentEffect.ArcChain);
            DoOk(run, PlayerCommand.Wait());
            foreach (var e in run.Floor.Enemies) Assert.That(e.Hp, Is.EqualTo(e.MaxHp - 1), $"goblin at {e.Pos}");

            var near = As(Run(".....", ".....", "H.G..", ".....", "....X"), "gearspark");
            near.Floor.Enemies[0].Intent = Intent.Rest();
            DoOk(near, PlayerCommand.Wait());
            Assert.That(near.Floor.Enemies[0].Hp, Is.EqualTo(near.Floor.Enemies[0].MaxHp), "Two tiles is out of reach.");
            var far = With(As(Run(".....", ".....", "H.G..", ".....", "....X"), "gearspark"), TalentEffect.DroneRange);
            far.Floor.Enemies[0].Intent = Intent.Rest();
            DoOk(far, PlayerCommand.Wait());
            Assert.That(far.Floor.Enemies[0].Hp, Is.EqualTo(far.Floor.Enemies[0].MaxHp - 1), "Long-Range Coil reaches it.");
        }

        [Test]
        public void TheDroneNeverZapsASleepingMimic()
        {
            var run = Revealed(With(As(Run(".....", ".....", "H.Q..", ".....", "....X"), "gearspark"), TalentEffect.DroneRange));
            var mimic = Enemy(run, "mimic_chest");
            var result = DoOk(run, PlayerCommand.Wait());
            Assert.That(Has(result, GameEventKind.DroneZapped), Is.False, "A zap would give it away as no chest.");
            Assert.That(mimic.Hp, Is.EqualTo(mimic.MaxHp));
        }
    }
}
