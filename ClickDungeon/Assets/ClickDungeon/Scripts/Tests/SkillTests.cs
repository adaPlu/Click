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
    /// D-075: the usable skills. Every one of the ninety-six talents is passive, so until now the game had no verb for
    /// "do a thing now" and a class could only be expressed as a modifier on a slash or a shield.
    /// </summary>
    public class SkillTests
    {
        static ProfileState WithTalent(string talentId, string classId)
        {
            var profile = new ProfileState { Xp = Progression.XpForLevel(20) };
            foreach (var talent in Catalog.TalentsOf(classId).OrderBy(t => t.Tier))
            {
                Progression.TryLearn(profile, Catalog, talent.Id);
                if (Progression.Rank(profile, talentId) > 0) break;
            }
            return profile;
        }

        [Test]
        public void ASkillIsUnlockedByTheTalentThatNamesItAndNotBefore()
        {
            // The whole progression rule: no new currency, no new screen to unlock one. The tree already decides which
            // branches a build climbed, so it already decides which skills that build has.
            var blank = new ProfileState { Xp = Progression.XpForLevel(20) };
            Assert.That(Progression.UnlockedSkills(blank, Catalog, "paladin"), Is.Empty,
                "Twenty levels of unspent points buy no skills.");

            var devout = WithTalent("p_prayer", "paladin");
            Assert.That(Progression.Rank(devout, "p_prayer"), Is.GreaterThan(0), "Test setup: Prayer is learned.");
            Assert.That(Progression.UnlockedSkills(devout, Catalog, "paladin"), Does.Contain("pal_lay_on_hands"));

            // And it is that class's: a Paladin's prayer does not arm a Cleric.
            Assert.That(Progression.UnlockedSkills(devout, Catalog, "cleric"), Is.Empty);
        }

        [Test]
        public void EverySkillIsReachableAndNamedByExactlyOneTalent()
        {
            // A skill nothing unlocks is a skill nobody can use, and two talents granting one would make "which talent
            // do I need" unanswerable on the panel.
            foreach (var skill in Catalog.Skills.Values)
            {
                var granting = Catalog.Talents.Where(t => t.SkillId == skill.Id).ToList();
                Assert.That(granting.Count, Is.EqualTo(1), $"{skill.Id} is named by {granting.Count} talents.");
                Assert.That(granting[0].ClassId, Is.EqualTo(skill.ClassId), $"{skill.Id} hangs off another class's tree.");
                Assert.That(granting[0].Tier, Is.LessThan(4),
                    $"{skill.Id} hangs off a capstone, and a class may learn only one capstone - two of its three skills would be unreachable.");
            }

            // Three slots, and no class may own more than it can carry.
            foreach (var heroClass in Catalog.HeroClasses.Values)
                Assert.That(Catalog.SkillsOf(heroClass.Id).Count, Is.LessThanOrEqualTo(BoardRules.SkillSlots), heroClass.Id);
        }

        [Test]
        public void AFullBuildCarriesThreeSkillsAndASingleBranchCarriesOne()
        {
            // Why tier 2 and one a branch: three slots are only worth having if a build can fill them, and skipping a
            // branch has to cost something. A capstone could not do this - a class may learn only one.
            foreach (var heroClass in Catalog.HeroClasses.Values)
            {
                var everything = new ProfileState { Xp = Progression.XpForLevel(60) };
                foreach (var talent in Catalog.TalentsOf(heroClass.Id).OrderBy(t => t.Tier))
                    for (int rank = 0; rank < talent.MaxRank; rank++)
                        Progression.TryLearn(everything, Catalog, talent.Id);

                Assert.That(Progression.EquippedSkills(everything, Catalog, heroClass.Id).Count,
                    Is.EqualTo(BoardRules.SkillSlots), $"{heroClass.Id}: a finished tree fills every slot.");
            }

            // And a narrow build carries less. One branch climbed, and only that branch's skill answers.
            var narrow = new ProfileState { Xp = Progression.XpForLevel(60) };
            foreach (var talent in Catalog.TalentsOf("paladin").Where(t => t.BranchId == "devotion").OrderBy(t => t.Tier))
                for (int rank = 0; rank < talent.MaxRank; rank++)
                    Progression.TryLearn(narrow, Catalog, talent.Id);
            var carried = Progression.EquippedSkills(narrow, Catalog, "paladin");
            Assert.That(carried, Is.EqualTo(new[] { "pal_lay_on_hands" }),
                "Only the branch that was climbed pays a skill, so the tree choice is the skill choice.");
        }

        /// <summary>
        /// D-081: the bot mends when hurt, not when scratched. A heal costs a turn, and the only thing the RULES
        /// forbid is casting one at full health - so a one-turn look-ahead, which scores a heart the same whether it
        /// was the last or the first, mended whenever it was a heart down. Twelve Knight runs cast Rally 838 times.
        /// The bot then played BETTER for having its heal taken away (+9 wins in 48 for the Knight, +6 for the
        /// Cleric), which means the class numbers were reading off the bot's valuation rather than the class.
        /// </summary>
        [Test]
        public void TheBotWillNotSpendATurnMendingAScratch()
        {
            var run = As(Revealed(Run(".....", ".....", "H....", ".....", ".....")), "dawnward");
            run.Skills = new List<string> { "pal_lay_on_hands" };
            run.Hero.MaxHp = 20;
            run.Hero.MaxMana = run.Hero.Mana = 9;

            bool MendOffered() => AutoPlayer.LegalCommands(run, Catalog)
                .Any(c => c.Kind == CommandKind.Skill && c.Slot == 0);

            run.Hero.Hp = 19;
            Assert.That(MendOffered(), Is.False, "One heart down is not worth a turn.");
            run.Hero.Hp = 11;
            Assert.That(MendOffered(), Is.False, "Still above half: the turn buys more somewhere else.");

            run.Hero.Hp = 10;
            Assert.That(MendOffered(), Is.True, "At half the hero is in trouble and a mend is worth the turn.");
            run.Hero.Hp = 4;
            Assert.That(MendOffered(), Is.True, "And below it.");

            // The distinction that matters: this is the BOT's policy, not a rule of the game. A player may still
            // press the button on a scratch, and the HUD must go on offering it.
            run.Hero.Hp = 19;
            Assert.That(Skills.CanUse(run, Catalog, 0, GridPos.Invalid, out _), Is.True,
                "The game still allows it - only the bot declines to.");
        }

        [Test]
        public void LayOnHandsMendsTheHeroForManaAndATurn()
        {
            var run = As(Revealed(Run(".....", ".....", "H....", ".....", ".....")), "dawnward");
            run.Skills = new List<string> { "pal_lay_on_hands" };
            var skill = Catalog.Skill("pal_lay_on_hands");
            run.Hero.MaxHp = 20;
            run.Hero.Hp = 10;
            run.Hero.MaxMana = run.Hero.Mana = 9;
            int turn = run.Turn;

            var result = DoOk(run, PlayerCommand.Skill(0));
            Assert.That(run.Hero.Hp, Is.EqualTo(10 + skill.Amount), "Mended by exactly what the card says.");
            Assert.That(run.Hero.Mana, Is.LessThan(9), "And paid for.");
            Assert.That(run.Turn, Is.GreaterThan(turn), "A skill costs the turn, like every other action.");
            Assert.That(Has(result, GameEventKind.SkillUsed), Is.True);

            // Never above full, and refused when there is nothing to mend - so a turn is never spent on nothing.
            run.Hero.Hp = run.Hero.MaxHp;
            var full = Do(run, PlayerCommand.Skill(0));
            Assert.That(full.Accepted, Is.False);
            Assert.That(full.RejectReason, Does.Contain("full health"));
        }

        [Test]
        public void ASkillIsRefusedWithoutTheManaToPayForIt()
        {
            var run = As(Revealed(Run(".....", ".....", "H....", ".....", ".....")), "dawnward");
            run.Skills = new List<string> { "pal_lay_on_hands" };
            run.Hero.Hp = 1;
            run.Hero.Mana = Catalog.Skill("pal_lay_on_hands").ManaCost - 1;

            var result = Do(run, PlayerCommand.Skill(0));
            Assert.That(result.Accepted, Is.False);
            Assert.That(result.RejectReason, Does.Contain("mana"));
            Assert.That(run.Hero.Hp, Is.EqualTo(1), "And nothing happened.");
        }

        [Test]
        public void DispelHurtsTheRisenTwiceAsHard()
        {
            // D-072 gave the undead a name so that "an answer to the undead" could point at something. This is the
            // skill that was the reason for naming it.
            int Hit(string enemyId)
            {
                var run = As(Revealed(Run(".....", ".....", "H....", ".....", ".....")), "lightbringer");
                var enemy = EnemyAi.Spawn(run.Floor, Catalog.Enemy(enemyId), P(1, 2), awake: true);
                enemy.MaxHp = enemy.Hp = 40;
                run.Skills = new List<string> { "cle_dispel" };
                run.Hero.MaxMana = run.Hero.Mana = 9;
                DoOk(run, PlayerCommand.Skill(0, enemy.Pos));
                return 40 - enemy.Hp;
            }

            int amount = Catalog.Skill("cle_dispel").Amount;
            Assert.That(Catalog.Enemy("skeleton").Undead, Is.True, "Test setup: a skeleton is risen.");
            Assert.That(Catalog.Enemy("goblin").Undead, Is.False, "Test setup: a goblin is not.");
            Assert.That(Hit("goblin"), Is.EqualTo(amount), "The living take the plain number.");
            Assert.That(Hit("skeleton"), Is.EqualTo(amount * 2), "The risen take double.");
        }

        [Test]
        public void TheEngineersBombCatchesWhatStandsAroundTheTileItLandsOn()
        {
            // D-079: Gearspark builds things that go off. A thrown charge is not a punch - what makes it a bomb is
            // that it hurts the tile's neighbours too.
            var run = As(Revealed(Run(".....", ".....", "H....", ".....", ".....")), "gearspark");
            var centre = EnemyAi.Spawn(run.Floor, Catalog.Enemy("goblin"), P(2, 2), awake: true);
            var beside = EnemyAi.Spawn(run.Floor, Catalog.Enemy("goblin"), P(2, 1), awake: true);
            var far = EnemyAi.Spawn(run.Floor, Catalog.Enemy("goblin"), P(4, 4), awake: true);
            foreach (var e in new[] { centre, beside, far }) e.MaxHp = e.Hp = 40;
            run.Skills = new List<string> { "eng_bomb" };
            run.Hero.MaxMana = run.Hero.Mana = 9;

            int amount = Catalog.Skill("eng_bomb").Amount;
            DoOk(run, PlayerCommand.Skill(0, centre.Pos));
            Assert.That(40 - centre.Hp, Is.EqualTo(amount), "The tile it landed on.");
            Assert.That(40 - beside.Hp, Is.EqualTo(amount), "And everything around that tile.");
            Assert.That(far.Hp, Is.EqualTo(40), "But not the far side of the room.");
        }

        [Test]
        public void TheRoguesSmokeHurtsAndLeavesThemAllReeling()
        {
            // The stealth, said in the vocabulary the game has: everything beside the hero loses its turn. Putting a
            // monster back to sleep does not work - Visibility.Update wakes anything on a revealed tile the same turn.
            var run = As(Revealed(Run(".....", ".....", "H....", ".....", ".....")), "shadowcut");
            var near = EnemyAi.Spawn(run.Floor, Catalog.Enemy("goblin"), P(1, 2), awake: true);
            var far = EnemyAi.Spawn(run.Floor, Catalog.Enemy("goblin"), P(4, 4), awake: true);
            foreach (var e in new[] { near, far }) e.MaxHp = e.Hp = 40;
            run.Skills = new List<string> { "rog_smoke" };
            run.Hero.MaxMana = run.Hero.Mana = 9;

            var result = DoOk(run, PlayerCommand.Skill(0));
            Assert.That(40 - near.Hp, Is.EqualTo(Catalog.Skill("rog_smoke").Amount), "A little damage...");
            Assert.That(Has(result, GameEventKind.EnemyStaggered), Is.True, "...and it loses track of him.");
            Assert.That(far.Hp, Is.EqualTo(40), "Only what is beside him.");
            Assert.That(far.Staggered, Is.False);
        }

        [Test]
        public void SmokeDoesNotDazeABoss()
        {
            // Every other stagger in the game leaves a boss alone, so this one does too (D-037).
            var run = As(Revealed(Run(Catalog.RunFloorCount, 42UL, ".....", ".....", "HB...", ".....", ".....")), "shadowcut");
            var boss = run.Floor.Enemies[0];
            Assert.That(Catalog.Enemy(boss.DefId).IsBoss, Is.True, "Test setup: that is a boss.");
            boss.Awake = true;
            run.Skills = new List<string> { "rog_smoke" };
            run.Hero.MaxMana = run.Hero.Mana = 9;
            run.Hero.MaxHp = run.Hero.Hp = 99;

            DoOk(run, PlayerCommand.Skill(0));
            Assert.That(boss.Staggered, Is.False, "A boss shrugs off the smoke, as it shrugs off every other stagger.");
        }

        [Test]
        public void TheClericHealsHarderThanThePaladin()
        {
            // Asked for outright: the same button on both, and the Paladin's is the weaker one. She is the healer.
            var cleric = Catalog.Skill("cle_mend");
            var paladin = Catalog.Skill("pal_lay_on_hands");
            Assert.That(cleric.Effect, Is.EqualTo(SkillEffect.Heal));
            Assert.That(paladin.Effect, Is.EqualTo(cleric.Effect), "The same button.");
            Assert.That(paladin.Amount, Is.LessThan(cleric.Amount), "And the Paladin's mends less.");
        }

        [Test]
        public void ASkillCannotBeAimedAtSomethingACoverHides()
        {
            // Rules 2.1, the rule the whole board is built on: nothing may differ on what an unrevealed cover hides. A
            // skill that reached a sleeping monster under a cover would be a way to ask what is under it.
            var run = As(Run(".....", ".....", "H....", ".....", "....."), "lightbringer");
            var asleep = EnemyAi.Spawn(run.Floor, Catalog.Enemy("skeleton"), P(1, 2), awake: false);
            run.Skills = new List<string> { "cle_dispel" };
            run.Hero.MaxMana = run.Hero.Mana = 9;

            var hidden = Do(run, PlayerCommand.Skill(0, asleep.Pos));
            Assert.That(hidden.Accepted, Is.False, "A sleeping monster is not a target.");
            Assert.That(asleep.Hp, Is.EqualTo(asleep.MaxHp), "And it took nothing.");

            // Awake, and it works - so the refusal above is the cover rule and not the skill being broken.
            asleep.Awake = true;
            Assert.That(Do(run, PlayerCommand.Skill(0, asleep.Pos)).Accepted, Is.True);
        }

        [Test]
        public void ASkillReachesOnlyAsFarAsItSays()
        {
            var run = As(Revealed(Run(".....", ".....", "H....", ".....", ".....")), "lightbringer");
            var far = EnemyAi.Spawn(run.Floor, Catalog.Enemy("goblin"), P(4, 2), awake: true);
            run.Skills = new List<string> { "cle_dispel" };
            run.Hero.MaxMana = run.Hero.Mana = 9;

            Assert.That(run.Hero.Pos.Chebyshev(far.Pos), Is.GreaterThan(Catalog.Skill("cle_dispel").Range),
                "Test setup: out of reach.");
            var result = Do(run, PlayerCommand.Skill(0, far.Pos));
            Assert.That(result.Accepted, Is.False);
            Assert.That(result.RejectReason, Does.Contain("reaches"));
        }

        [Test]
        public void AProfileCannotCarryASkillItNeverUnlockedOrOneOfAnothersClass()
        {
            // A profile is a file on disk. SEC-04 taught this for worn gear; the same rule, for skills.
            var profile = WithTalent("p_prayer", "paladin");
            var unlocked = Progression.UnlockedSkills(profile, Catalog, "paladin");
            Assert.That(unlocked, Does.Contain("pal_lay_on_hands"), "Test setup: the build unlocked something.");
            profile.Skills["paladin"] = new List<string> { "cle_dispel", "not_a_skill", "pal_lay_on_hands", "pal_lay_on_hands" };

            var equipped = Progression.EquippedSkills(profile, Catalog, "paladin");
            Assert.That(equipped[0], Is.EqualTo("pal_lay_on_hands"),
                "The one entry that was real, unlocked and this class's is kept, and kept first.");
            Assert.That(equipped, Does.Not.Contain("cle_dispel"), "Another class's skill is dropped.");
            Assert.That(equipped, Does.Not.Contain("not_a_skill"), "An invented one is dropped.");
            Assert.That(equipped.Distinct().Count(), Is.EqualTo(equipped.Count), "And a duplicate is not carried twice.");
            Assert.That(equipped, Is.SubsetOf(unlocked), "Nothing is carried that the build never unlocked.");
            Assert.That(equipped.Count, Is.LessThanOrEqualTo(BoardRules.SkillSlots));
        }

        [Test]
        public void AnEmptySlotIsFilledWithWhatTheBuildUnlocked()
        {
            // With exactly three skills a class, the loadout is not yet a choice, and a player should not have to make
            // it before it is one. The profile still records it, so the day the pool grows the field is already there.
            var profile = WithTalent("p_prayer", "paladin");
            Assert.That(profile.Skills.ContainsKey("paladin"), Is.False, "Test setup: nothing was ever chosen.");

            var unlocked = Progression.UnlockedSkills(profile, Catalog, "paladin");
            Assert.That(unlocked, Is.Not.Empty, "Test setup: the build unlocked something to fill a slot with.");
            Assert.That(Progression.EquippedSkills(profile, Catalog, "paladin"),
                Is.EqualTo(unlocked.Take(BoardRules.SkillSlots).ToList()),
                "Every unlocked skill the slots can hold, in tree order.");
        }

        [Test]
        public void ASkillTheHeroDoesNotCarryIsNotACommand()
        {
            var run = As(Revealed(Run(".....", ".....", "H....", ".....", ".....")), "dawnward");
            run.Hero.MaxMana = run.Hero.Mana = 9;
            run.Hero.Hp = 1;
            run.Skills = new List<string>();

            foreach (int slot in new[] { -1, 0, 1, 2, 99 })
                Assert.That(Do(run, PlayerCommand.Skill(slot)).Accepted, Is.False, $"Slot {slot} holds nothing.");

            // Nor one belonging to another class, however it got into the list.
            run.Skills = new List<string> { "cle_dispel" };
            Assert.That(Do(run, PlayerCommand.Skill(0, P(1, 2))).Accepted, Is.False, "A Paladin does not know the Cleric's word.");
        }

        [Test]
        public void TheRunTakesItsSkillsFromTheProfileAndTheBotCanUseThem()
        {
            // The same path the game takes (ProfileSystem.ProvisionRun), and the bot has to see them: a balance number
            // measured with a built-up profile whose hero never uses three of its abilities measures the wrong hero,
            // which is TEST-23 one system later.
            var profile = WithTalent("p_prayer", "paladin");
            var run = RunFactory.NewRun(5UL, Catalog, new List<GameEvent>(), "dawnward");
            ProfileSystem.ProvisionRun(profile, run, Catalog, new List<GameEvent>());

            Assert.That(run.Skills, Is.EqualTo(Progression.EquippedSkills(profile, Catalog, "paladin")),
                "The run carries what the profile equipped, through the same function the game uses.");
            Assert.That(run.Skills, Does.Contain("pal_lay_on_hands"));

            run.Hero.MaxHp = 20;
            run.Hero.Hp = 5;
            run.Hero.MaxMana = run.Hero.Mana = 9;
            Assert.That(AutoPlayer.LegalCommands(run, Catalog).Any(c => c.Kind == CommandKind.Skill), Is.True,
                "The bot can see the skill, so every measurement taken with it includes it.");
        }
    }
}
